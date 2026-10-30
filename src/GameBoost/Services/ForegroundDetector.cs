using System.Text;
using GameBoost.Infrastructure;

namespace GameBoost.Services;

/// <summary>Detects whether a fullscreen or borderless game covers a monitor.</summary>
public static class ForegroundDetector
{
    private static readonly HashSet<string> ShellClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"
    };

    public static bool IsFullscreenAppOn(MonitorInfo monitor)
    {
        try
        {
            var fg = Win32.GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;

            Win32.GetWindowThreadProcessId(fg, out var pid);
            if (pid == Environment.ProcessId) return false;

            var sb = new StringBuilder(64);
            Win32.GetClassName(fg, sb, sb.Capacity);
            if (ShellClasses.Contains(sb.ToString())) return false;

            if (!Win32.GetWindowRect(fg, out var r)) return false;
            return r.Left <= monitor.Left && r.Top <= monitor.Top
                   && r.Right >= monitor.Left + monitor.Width && r.Bottom >= monitor.Top + monitor.Height;
        }
        catch (Exception ex)
        {
            Logger.Warn("Foreground window could not be checked", ex);
            return true;
        }
    }
}
