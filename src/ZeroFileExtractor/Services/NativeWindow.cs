using System.Runtime.InteropServices;

namespace ZeroFileExtractor.Services;

/// <summary>Pencereyi güvenilir biçimde öne getirir (tepsiden açarken Windows'un ön plan kısıtına takılmadan).</summary>
public static class NativeWindow
{
    private const int SW_RESTORE = 9, SW_SHOW = 5;

    public static void BringToFront(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        ShowWindow(hwnd, IsIconic(hwnd) ? SW_RESTORE : SW_SHOW);
        if (GetForegroundWindow() == hwnd) return;

        // Ön plandaki pencerenin iş parçacığına kısa süre bağlanmak SetForegroundWindow'un reddedilmesini önler.
        IntPtr foreground = GetForegroundWindow();
        uint foregroundThread = GetWindowThreadProcessId(foreground, IntPtr.Zero);
        uint currentThread = GetCurrentThreadId();
        bool attached = foregroundThread != 0 && foregroundThread != currentThread && AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached) AttachThreadInput(currentThread, foregroundThread, false);
        }
    }

    /// <summary>Pencere ön planda değilse görev çubuğu düğmesini birkaç kez yakıp söndürür.</summary>
    public static void Flash(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || GetForegroundWindow() == hwnd) return;
        var info = new FLASHWINFO
        {
            cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
            hwnd = hwnd,
            dwFlags = 0x00000002 | 0x0000000C, // FLASHW_TRAY | FLASHW_TIMERNOFG
            uCount = 3,
        };
        FlashWindowEx(ref info);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [DllImport("user32.dll")] private static extern bool FlashWindowEx(ref FLASHWINFO info);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
