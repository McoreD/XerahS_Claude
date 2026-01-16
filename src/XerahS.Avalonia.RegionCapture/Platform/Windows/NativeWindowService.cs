#if WINDOWS
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using XerahS.Avalonia.RegionCapture.Models;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using Windows.Win32.Graphics.Dwm;

namespace XerahS.Avalonia.RegionCapture.Platform.Windows;

/// <summary>
/// Native Windows window enumeration and detection using DWM APIs.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class NativeWindowService
{
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const uint WS_VISIBLE = 0x10000000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_NOACTIVATE = 0x08000000;

    /// <summary>
    /// Gets the window at the specified physical point.
    /// </summary>
    public static WindowInfo? GetWindowAtPoint(PixelPoint point)
    {
        var windows = EnumerateVisibleWindows();

        // Find the topmost window that contains the point
        foreach (var window in windows.OrderBy(w => w.ZOrder))
        {
            if (window.SnapBounds.Contains(point))
            {
                return window;
            }
        }

        return null;
    }

    /// <summary>
    /// Enumerates all visible windows with their visual bounds.
    /// </summary>
    public static IReadOnlyList<WindowInfo> EnumerateVisibleWindows()
    {
        var windows = new List<WindowInfo>();
        var zOrder = 0;

        PInvoke.EnumWindows((hWnd, lParam) =>
        {
            if (IsWindowValidForCapture(hWnd))
            {
                var info = GetWindowInfo(hWnd, zOrder++);
                if (info is not null)
                {
                    windows.Add(info);
                }
            }
            return true;
        }, 0);

        return windows;
    }

    private static bool IsWindowValidForCapture(HWND hWnd)
    {
        if (!PInvoke.IsWindowVisible(hWnd))
            return false;

        if (PInvoke.IsIconic(hWnd))
            return false;

        // Get window style
        var style = GetWindowLongAuto(hWnd, GWL_STYLE);
        if ((style & WS_VISIBLE) == 0)
            return false;

        // Skip tool windows and other special windows
        var exStyle = GetWindowLongAuto(hWnd, GWL_EXSTYLE);
        if ((exStyle & WS_EX_TOOLWINDOW) != 0)
            return false;

        // Skip windows with empty titles (usually internal windows)
        var titleLength = PInvoke.GetWindowTextLength(hWnd);
        if (titleLength == 0)
            return false;

        return true;
    }

    private static WindowInfo? GetWindowInfo(HWND hWnd, int zOrder)
    {
        // Get standard window rect
        if (!PInvoke.GetWindowRect(hWnd, out var windowRect))
            return null;

        var bounds = new PixelRect(
            windowRect.X,
            windowRect.Y,
            windowRect.Width,
            windowRect.Height);

        // Get visual bounds using DWM (excludes shadow/invisible borders)
        var visualBounds = GetDwmFrameBounds(hWnd) ?? bounds;

        // Get window title
        var titleLength = PInvoke.GetWindowTextLength(hWnd);
        var titleBuilder = new char[titleLength + 1];
        string title;

        unsafe
        {
            fixed (char* pTitle = titleBuilder)
            {
                PInvoke.GetWindowText(hWnd, pTitle, titleLength + 1);
                title = new string(pTitle);
            }
        }

        // Get class name
        var classBuilder = new char[256];
        string className;

        unsafe
        {
            fixed (char* pClass = classBuilder)
            {
                PInvoke.GetClassName(hWnd, pClass, 256);
                className = new string(pClass);
            }
        }

        return new WindowInfo(
            Handle: hWnd.Value,
            Title: title,
            ClassName: className,
            Bounds: bounds,
            VisualBounds: visualBounds,
            IsMinimized: PInvoke.IsIconic(hWnd),
            ZOrder: zOrder);
    }

    private static PixelRect? GetDwmFrameBounds(HWND hWnd)
    {
        var rect = new RECT();

        unsafe
        {
            var hr = PInvoke.DwmGetWindowAttribute(
                hWnd,
                DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS,
                &rect,
                (uint)sizeof(RECT));

            if (hr.Failed)
                return null;
        }

        return new PixelRect(
            rect.X,
            rect.Y,
            rect.Width,
            rect.Height);
    }

    private static nint GetWindowLongAuto(HWND hWnd, int index)
    {
        if (IntPtr.Size == 8)
        {
            return PInvoke.GetWindowLongPtr(hWnd, (WINDOW_LONG_PTR_INDEX)index);
        }
        else
        {
            return PInvoke.GetWindowLong(hWnd, (WINDOW_LONG_PTR_INDEX)index);
        }
    }
}
#endif
