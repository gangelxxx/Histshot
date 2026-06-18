using Avalonia.Threading;
using Histshot.Core.Models;
using Histshot.Core.Services;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace Histshot.Services;

[SupportedOSPlatform("windows")]
public sealed class GlobalHotkeyService : IDisposable
{
    private const uint WM_HOTKEY = 0x0312;
    private const uint WM_QUIT = 0x0012;
    // Custom message used to (re)apply hotkeys on the thread that owns the window.
    // RegisterHotKey/UnregisterHotKey must run on that thread, not the UI thread.
    private const uint WM_REAPPLY = 0x8000 + 1; // WM_APP + 1
    private const uint MOD_NONE = 0;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClass(ref WNDCLASS lpWndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string? lpWindowName,
        uint dwStyle, int X, int Y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    private static readonly IntPtr HwndMessage = new(-3);

    private IntPtr _hwnd;
    private WndProcDelegate? _wndProcDelegate;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly Thread _thread;
    private readonly object _lock = new();
    private readonly Dictionary<int, Action> _handlers = new();
    private int _nextId = 1;
    private bool _disposed;

    // Latest settings/handlers to apply; read by the message-loop thread on WM_REAPPLY.
    private AppSettings? _pendingSettings;
    private Action? _pendingCapture;
    private Action? _pendingQuickSave;

    public GlobalHotkeyService()
    {
        _thread = new Thread(RunLoop) { IsBackground = true, Name = "HotkeyMsgLoop" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    private void RunLoop()
    {
        _wndProcDelegate = WndProc;
        var hInst = GetModuleHandle(null);
        var className = "HistshotHK_" + Environment.ProcessId;

        var wc = new WNDCLASS
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance = hInst,
            lpszClassName = className
        };
        RegisterClass(ref wc);

        _hwnd = CreateWindowEx(0, className, null, 0, 0, 0, 0, 0, HwndMessage, IntPtr.Zero, hInst, IntPtr.Zero);
        _ready.Set();

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0))
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_HOTKEY)
        {
            var id = (int)wParam;
            DebugLogger.Log($"Hotkey: WM_HOTKEY received (id={id}).");
            Action? handler;
            lock (_lock) _handlers.TryGetValue(id, out handler);
            if (handler != null)
                Dispatcher.UIThread.Post(handler);
        }
        else if (msg == WM_REAPPLY)
        {
            ApplyOnLoopThread();
        }
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    /// <summary>
    /// (Re)registers the global hotkeys for the given settings. Safe to call from any thread:
    /// the actual RegisterHotKey/UnregisterHotKey calls are marshalled onto the thread that
    /// owns the message window (Windows requires that thread to perform them).
    /// </summary>
    public void Apply(AppSettings settings, Action onCapture, Action onQuickSave)
    {
        lock (_lock)
        {
            _pendingSettings = settings;
            _pendingCapture = onCapture;
            _pendingQuickSave = onQuickSave;
        }

        if (_hwnd != IntPtr.Zero)
            PostMessage(_hwnd, WM_REAPPLY, IntPtr.Zero, IntPtr.Zero);
    }

    // Runs on the message-loop thread (via WM_REAPPLY): unregister the current hotkeys and
    // register the pending ones.
    private void ApplyOnLoopThread()
    {
        AppSettings? settings;
        Action? onCapture;
        Action? onQuickSave;
        lock (_lock)
        {
            settings = _pendingSettings;
            onCapture = _pendingCapture;
            onQuickSave = _pendingQuickSave;

            foreach (var id in _handlers.Keys)
                UnregisterHotKey(_hwnd, id);
            _handlers.Clear();
            _nextId = 1;
        }

        if (settings == null)
            return;

        if (settings.PrimaryHotkeyEnabled && onCapture != null)
            TryRegister(settings.PrimaryHotkey, onCapture);
        if (settings.QuickSaveEnabled && onQuickSave != null)
            TryRegister(settings.QuickSaveHotkey, onQuickSave);
    }

    private void TryRegister(string hotkeyStr, Action handler)
    {
        if (!TryParse(hotkeyStr, out var mods, out var vk))
        {
            DebugLogger.Log($"Hotkey: could not parse '{hotkeyStr}', not registered.");
            return;
        }
        lock (_lock)
        {
            var id = _nextId++;
            if (RegisterHotKey(_hwnd, id, mods | MOD_NOREPEAT, vk))
            {
                _handlers[id] = handler;
                DebugLogger.Log($"Hotkey: registered '{hotkeyStr}' (id={id}, mods=0x{mods:X}, vk=0x{vk:X}).");
            }
            else
            {
                var err = Marshal.GetLastWin32Error();
                DebugLogger.Log($"Hotkey: FAILED to register '{hotkeyStr}' (mods=0x{mods:X}, vk=0x{vk:X}), Win32 error {err}" +
                    (err == 1409 ? " (ERROR_HOTKEY_ALREADY_REGISTERED — another app owns this key)." : "."));
            }
        }
    }

    private static bool TryParse(string text, out uint modifiers, out uint vk)
    {
        modifiers = MOD_NONE;
        vk = 0;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL": modifiers |= MOD_CONTROL; break;
                case "SHIFT": modifiers |= MOD_SHIFT; break;
                case "ALT": modifiers |= MOD_ALT; break;
                case "WIN":
                case "WINDOWS": modifiers |= 0x0008; break;
            }
        }
        vk = parts[^1].Trim().ToUpperInvariant() switch
        {
            "PRNT SCRN" or "PRINT SCREEN" or "PRTSCN" or "PRINTSCREEN" => 0x2C,
            "F1" => 0x70, "F2" => 0x71, "F3" => 0x72, "F4" => 0x73,
            "F5" => 0x74, "F6" => 0x75, "F7" => 0x76, "F8" => 0x77,
            "F9" => 0x78, "F10" => 0x79, "F11" => 0x7A, "F12" => 0x7B,
            _ => 0
        };
        return vk != 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hwnd != IntPtr.Zero)
            PostMessage(_hwnd, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }
}
