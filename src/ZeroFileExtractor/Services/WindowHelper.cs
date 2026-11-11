using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace ZeroFileExtractor.Services;

/// <summary>Pencere boyutu, başlık çubuğu renkleri ve açık pencerelerin kaydı.</summary>
public static class WindowHelper
{
    /// <summary>Açık pencereler (çöp toplayıcı kapatmasın diye tutulur).</summary>
    public static List<Window> Open { get; } = new();

    public static void Track(Window window)
    {
        Open.Add(window);
        window.Closed += (_, _) => Open.Remove(window);
    }

    public static double Scale(IntPtr hwnd)
    {
        uint dpi = GetDpiForWindow(hwnd);
        return dpi == 0 ? 1.0 : dpi / 96.0;
    }

    /// <summary>Mantıksal (96 DPI) boyuta getirir; <paramref name="center"/> ise çalışma alanında ortalar.</summary>
    public static void Resize(AppWindow window, IntPtr hwnd, int width, int height, bool center)
    {
        double scale = Scale(hwnd);
        var area = DisplayArea.GetFromWindowId(window.Id, DisplayAreaFallback.Primary).WorkArea;
        int w = Math.Min((int)(width * scale), area.Width - 20);
        int h = Math.Min((int)(height * scale), area.Height - 20);
        if (center)
        {
            window.MoveAndResize(new RectInt32(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h));
        }
        else
        {
            // Büyürken ekranın dışına taşmasın.
            int x = Math.Clamp(window.Position.X, area.X, Math.Max(area.X, area.X + area.Width - w));
            int y = Math.Clamp(window.Position.Y, area.Y, Math.Max(area.Y, area.Y + area.Height - h));
            window.MoveAndResize(new RectInt32(x, y, w, h));
        }
    }

    public static void ApplyTitleBarColors(AppWindow window, bool dark)
    {
        var bar = window.TitleBar;
        bar.ButtonBackgroundColor = Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonForegroundColor = dark ? Colors.White : Colors.Black;
        bar.ButtonInactiveForegroundColor = dark ? ColorHelper.FromArgb(0xFF, 0x9A, 0x9A, 0x9A) : ColorHelper.FromArgb(0xFF, 0x80, 0x80, 0x80);
        bar.ButtonHoverBackgroundColor = dark ? ColorHelper.FromArgb(0x20, 0xFF, 0xFF, 0xFF) : ColorHelper.FromArgb(0x14, 0x00, 0x00, 0x00);
        bar.ButtonHoverForegroundColor = dark ? Colors.White : Colors.Black;
        bar.ButtonPressedBackgroundColor = dark ? ColorHelper.FromArgb(0x30, 0xFF, 0xFF, 0xFF) : ColorHelper.FromArgb(0x24, 0x00, 0x00, 0x00);
        bar.ButtonPressedForegroundColor = dark ? Colors.White : Colors.Black;
    }

    public static string IconPath => Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
