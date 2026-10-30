using System.ComponentModel;
using System.Runtime.InteropServices;
using GameBoost.Infrastructure;
using GameBoost.Rendering;

namespace GameBoost.Views;

/// <summary>
/// Minimal native window with per-pixel alpha (UpdateLayeredWindow). Always on top, click-through,
/// never activated and hidden from the taskbar and Alt+Tab. Coordinates are physical pixels.
/// </summary>
internal sealed class LayeredWindow : IDisposable
{
    private const string ClassName = "GameBoostLayeredOverlay";
    private static readonly Win32.WndProc Proc = WindowProc; // kept alive for the lifetime of the process
    private static bool _classRegistered;

    private IntPtr _hwnd;

    public LayeredWindow(string title)
    {
        EnsureClass();
        var ex = NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW
                 | NativeMethods.WS_EX_NOACTIVATE | Win32.WS_EX_TOPMOST;
        _hwnd = Win32.CreateWindowEx(ex, ClassName, title, Win32.WS_POPUP, 0, 0, 1, 1,
            IntPtr.Zero, IntPtr.Zero, Win32.GetModuleHandle(null), IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Overlay window could not be created");
    }

    public IntPtr Handle => _hwnd;
    public bool IsVisible { get; private set; }

    /// <summary>Puts the image on screen at the given physical position.</summary>
    public void SetImage(PixelImage image, int left, int top)
    {
        if (_hwnd == IntPtr.Zero) return;

        var screenDc = Win32.GetDC(IntPtr.Zero);
        var memDc = Win32.CreateCompatibleDC(screenDc);
        var header = new Win32.BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<Win32.BITMAPINFOHEADER>(),
            biWidth = image.Width,
            biHeight = -image.Height, // top-down
            biPlanes = 1,
            biBitCount = 32
        };
        var bitmap = Win32.CreateDIBSection(memDc, ref header, Win32.DIB_RGB_COLORS, out var bits, IntPtr.Zero, 0);
        var old = IntPtr.Zero;
        try
        {
            if (bitmap == IntPtr.Zero || bits == IntPtr.Zero) return;
            Marshal.Copy(image.Pixels, 0, bits, image.Pixels.Length);
            old = Win32.SelectObject(memDc, bitmap);

            var dst = new Win32.POINT { X = left, Y = top };
            var size = new Win32.SIZE { Cx = image.Width, Cy = image.Height };
            var src = new Win32.POINT();
            var blend = new Win32.BLENDFUNCTION
            {
                BlendOp = Win32.AC_SRC_OVER,
                SourceConstantAlpha = 255,
                AlphaFormat = Win32.AC_SRC_ALPHA
            };
            if (!Win32.UpdateLayeredWindow(_hwnd, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, Win32.ULW_ALPHA))
                Logger.Warn($"UpdateLayeredWindow failed ({Marshal.GetLastWin32Error()})");
        }
        finally
        {
            if (old != IntPtr.Zero) Win32.SelectObject(memDc, old);
            if (bitmap != IntPtr.Zero) Win32.DeleteObject(bitmap);
            Win32.DeleteDC(memDc);
            Win32.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    public void Show()
    {
        if (_hwnd == IntPtr.Zero || IsVisible) return;
        Win32.ShowWindow(_hwnd, Win32.SW_SHOWNOACTIVATE);
        IsVisible = true;
        EnsureTopmost();
    }

    public void Hide()
    {
        if (_hwnd == IntPtr.Zero || !IsVisible) return;
        Win32.ShowWindow(_hwnd, Win32.SW_HIDE);
        IsVisible = false;
    }

    public void EnsureTopmost()
    {
        if (_hwnd == IntPtr.Zero || !IsVisible) return;
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);
    }

    private static void EnsureClass()
    {
        if (_classRegistered) return;
        var wc = new Win32.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<Win32.WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(Proc),
            hInstance = Win32.GetModuleHandle(null),
            lpszClassName = ClassName
        };
        if (Win32.RegisterClassEx(ref wc) == 0)
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 1410) throw new Win32Exception(error, "Overlay window class could not be registered"); // 1410 = already exists
        }
        _classRegistered = true;
    }

    private static IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == Win32.WM_NCHITTEST) return new IntPtr(Win32.HTTRANSPARENT);
        return Win32.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hwnd != IntPtr.Zero) Win32.DestroyWindow(_hwnd);
        _hwnd = IntPtr.Zero;
        IsVisible = false;
    }
}
