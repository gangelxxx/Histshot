using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Histshot.Core.Services;

namespace Histshot.Services;

/// <summary>
/// Hands memory the process no longer needs back to the OS after an interaction, so the
/// working set shown in Task Manager settles near the live footprint rather than the
/// launch/editing peak (~130 MB at one point, ~230 MB mid-capture).
///
/// Two-step, deliberately *soft*:
///  1. Compact the managed heaps (ConserveMemory in runtimeconfig then releases the
///     decommitted segments to the OS) — reclaims the large transient capture bitmaps.
///  2. Apply a soft working-set quota. Windows trims the working set toward the cap by
///     evicting least-recently-used pages, so the freed bitmap pages and cold UI paths
///     go but the code that just ran stays resident — the next capture is still instant.
///
/// This is the gentler counterpart to a full <c>EmptyWorkingSet</c>, which would drop the
/// idle figure to a few MB but evict the hot code too, making the first capture after an
/// idle period pay to fault everything back in. The quota is soft (no hard-limit flags),
/// so the process freely exceeds it again during the next capture.
/// </summary>
internal static class MemoryTrimmer
{
    // Soft working-set bounds. The max is the figure the idle process trims toward; the
    // hot capture/startup code (~a few tens of MB) stays under it and survives the trim.
    private const long MinWorkingSetBytes = 16L * 1024 * 1024;
    private const long MaxWorkingSetBytes = 48L * 1024 * 1024;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMin, IntPtr dwMax);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [SupportedOSPlatform("windows")]
    public static void Trim()
    {
        if (!OperatingSystem.IsWindows())
            return;

        // Compact the managed heaps so the pages the quota then evicts are genuinely free
        // rather than holding collectable-but-not-yet-collected objects (e.g. SKBitmaps
        // released via finalizer).
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);

        try { SetProcessWorkingSetSize(GetCurrentProcess(), (IntPtr)MinWorkingSetBytes, (IntPtr)MaxWorkingSetBytes); }
        catch (Exception ex) { DebugLogger.Log($"Working-set trim failed: {ex.Message}"); }
    }
}
