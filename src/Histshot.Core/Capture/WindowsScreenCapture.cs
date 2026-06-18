using System.Runtime.InteropServices;
using Histshot.Core.Models;
using Histshot.Core.Services;
using SkiaSharp;

namespace Histshot.Core.Capture;

public class WindowsScreenCapture : IScreenCaptureProvider
{
    public IReadOnlyList<ScreenInfo> GetScreens()
    {
        var screens = new List<ScreenInfo>();
        uint deviceIndex = 0;

        while (true)
        {
            var displayDevice = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(null, deviceIndex, ref displayDevice, 0))
                break;

            if ((displayDevice.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0)
            {
                var devMode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
                if (EnumDisplaySettings(displayDevice.DeviceName, ENUM_CURRENT_SETTINGS, ref devMode))
                {
                    var x = devMode.dmPositionX;
                    var y = devMode.dmPositionY;
                    var width = (int)devMode.dmPelsWidth;
                    var height = (int)devMode.dmPelsHeight;

                    var rect = new RECT { Left = x, Top = y, Right = x + width, Bottom = y + height };
                    var hMonitor = MonitorFromRect(ref rect, MONITOR_DEFAULTTONEAREST);
                    var scaling = GetMonitorScaling(hMonitor);

                    var screenInfo = new ScreenInfo
                    {
                        Name = displayDevice.DeviceName,
                        X = x,
                        Y = y,
                        Width = width,
                        Height = height,
                        Scaling = scaling
                    };
                    screens.Add(screenInfo);
                    DebugLogger.Log($"Screen detected: {screenInfo.Name}, bounds=({screenInfo.X},{screenInfo.Y},{screenInfo.Width},{screenInfo.Height}), scaling={screenInfo.Scaling}");
                }
            }

            deviceIndex++;
        }

        return screens;
    }

    private static double GetMonitorScaling(IntPtr hMonitor)
    {
        try
        {
            int result = GetDpiForMonitor(hMonitor, MonitorDpiType.MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY);
            if (result == 0 && dpiX > 0)
                return dpiX / 96.0;
        }
        catch { }
        return 1.0;
    }

    public SKBitmap CaptureScreen(ScreenInfo screen)
    {
        DebugLogger.Log($"CaptureScreen: {screen.Name}, bounds=({screen.X},{screen.Y},{screen.Width},{screen.Height})");

        IntPtr hScreenDC = IntPtr.Zero;
        try
        {
            // Try to create a DC for this specific display device.
            hScreenDC = CreateDC(screen.Name, null, null, IntPtr.Zero);
            if (hScreenDC == IntPtr.Zero)
            {
                hScreenDC = CreateDC("DISPLAY", screen.Name, null, IntPtr.Zero);
            }

            if (hScreenDC != IntPtr.Zero)
            {
                var width = GetDeviceCaps(hScreenDC, DeviceCaps.HORZRES);
                var height = GetDeviceCaps(hScreenDC, DeviceCaps.VERTRES);
                DebugLogger.Log($"CaptureScreen via CreateDC: {screen.Name}, device size={width}x{height}");
                return CaptureFromDC(hScreenDC, 0, 0, width, height);
            }
        }
        finally
        {
            if (hScreenDC != IntPtr.Zero)
                DeleteDC(hScreenDC);
        }

        // Fallback: capture from the global screen DC using the screen's virtual coordinates.
        DebugLogger.Log($"CaptureScreen fallback to BitBlt: {screen.Name}");
        return CaptureRegion(screen.X, screen.Y, screen.Width, screen.Height);
    }

    public SKBitmap CaptureRegion(int x, int y, int width, int height)
    {
        DebugLogger.Log($"CaptureRegion: x={x}, y={y}, width={width}, height={height}");

        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be greater than zero.");
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be greater than zero.");

        IntPtr hScreenDC = IntPtr.Zero;
        try
        {
            hScreenDC = GetDC(IntPtr.Zero);
            if (hScreenDC == IntPtr.Zero)
                throw new InvalidOperationException("Failed to get device context.");

            return CaptureFromDC(hScreenDC, x, y, width, height);
        }
        finally
        {
            if (hScreenDC != IntPtr.Zero)
                ReleaseDC(IntPtr.Zero, hScreenDC);
        }
    }

    private static SKBitmap CaptureFromDC(IntPtr hScreenDC, int x, int y, int width, int height)
    {
        IntPtr hMemoryDC = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr hOldBitmap = IntPtr.Zero;

        try
        {
            hMemoryDC = CreateCompatibleDC(hScreenDC);
            if (hMemoryDC == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create compatible DC.");

            hBitmap = CreateCompatibleBitmap(hScreenDC, width, height);
            if (hBitmap == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create compatible bitmap.");

            hOldBitmap = SelectObject(hMemoryDC, hBitmap);

            if (!BitBlt(hMemoryDC, 0, 0, width, height, hScreenDC, x, y, TernaryRasterOperations.SRCCOPY))
                throw new InvalidOperationException("BitBlt failed.");

            SelectObject(hMemoryDC, hOldBitmap);
            hOldBitmap = IntPtr.Zero;

            var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);

            var info = new BITMAPINFO();
            info.biSize = (uint)Marshal.SizeOf(info);
            info.biWidth = width;
            info.biHeight = -height;
            info.biPlanes = 1;
            info.biBitCount = 32;
            info.biCompression = 0;

            int result = GetDIBits(hScreenDC, hBitmap, 0, (uint)height, bitmap.GetPixels(), ref info, DIB_RGB_COLORS);
            if (result == 0)
                throw new InvalidOperationException("GetDIBits failed.");

            // Screen captures usually have zero alpha; force opaque so the image displays correctly.
            EnsureOpaque(bitmap);

            return bitmap;
        }
        finally
        {
            if (hOldBitmap != IntPtr.Zero)
                SelectObject(hMemoryDC, hOldBitmap);
            if (hBitmap != IntPtr.Zero)
                DeleteObject(hBitmap);
            if (hMemoryDC != IntPtr.Zero)
                DeleteDC(hMemoryDC);
        }
    }

    private static void EnsureOpaque(SKBitmap bitmap)
    {
        if (bitmap.ColorType != SKColorType.Bgra8888)
            return;

        unsafe
        {
            byte* pixels = (byte*)bitmap.GetPixels();
            if (pixels == null)
                return;

            int count = bitmap.Width * bitmap.Height;
            for (int i = 0; i < count; i++)
            {
                pixels[i * 4 + 3] = 255; // Alpha channel
            }
        }
    }

    private const int DIB_RGB_COLORS = 0;
    private const uint DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x00000001;
    private const int ENUM_CURRENT_SETTINGS = -1;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    private enum DeviceCaps
    {
        HORZRES = 8,
        VERTRES = 10
    }

    [DllImport("gdi32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr CreateDC(string lpszDriver, string? lpszDevice, string? lpszOutput, IntPtr lpInitData);

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr hdc, DeviceCaps nIndex);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, TernaryRasterOperations dwRop);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines, IntPtr lpvBits, ref BITMAPINFO lpbi, uint uUsage);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref RECT lprc, uint dwFlags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hMonitor, MonitorDpiType dpiType, out uint dpiX, out uint dpiY);

    private enum TernaryRasterOperations : uint
    {
        SRCCOPY = 0x00CC0020
    }

    private enum MonitorDpiType
    {
        MDT_EFFECTIVE_DPI = 0,
        MDT_ANGULAR_DPI = 1,
        MDT_RAW_DPI = 2
    }
}
