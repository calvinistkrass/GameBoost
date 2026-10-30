using System.Text.RegularExpressions;
using GameBoost.Infrastructure;

namespace GameBoost.Services;

/// <summary>A physical monitor. All coordinates in physical pixels (per-monitor DPI aware).</summary>
public sealed record MonitorInfo(string DeviceName, int Number, int Left, int Top, int Width, int Height, bool IsPrimary, double DpiScale)
{
    public string DisplayName =>
        $"{Loc.T("Monitor.Name")} {Number} · {Width}×{Height}" +
        (Math.Abs(DpiScale - 1) > 0.01 ? $" · {DpiScale * 100:0}%" : "") +
        (IsPrimary ? $" · {Loc.T("Monitor.Primary")}" : "");
}

public static class MonitorService
{
    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var result = new List<MonitorInfo>();
        try
        {
            NativeMethods.MonitorEnumProc callback = (IntPtr hMonitor, IntPtr _, ref NativeMethods.RECT _, IntPtr _) =>
            {
                try
                {
                    var info = new NativeMethods.MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
                    if (NativeMethods.GetMonitorInfo(hMonitor, ref info))
                    {
                        double scale = 1;
                        if (NativeMethods.GetDpiForMonitor(hMonitor, NativeMethods.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0)
                            scale = dpiX / 96.0;

                        var r = info.rcMonitor;
                        result.Add(new MonitorInfo(
                            info.szDevice,
                            ParseNumber(info.szDevice, result.Count + 1),
                            r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top,
                            (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0,
                            scale));
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn("Could not read monitor info", ex);
                }
                return true;
            };
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
            GC.KeepAlive(callback);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not enumerate monitors", ex);
        }

        if (result.Count == 0)
        {
            // Emergency fallback: primary screen according to WinForms.
            var b = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
            result.Add(new MonitorInfo(@"\\.\DISPLAY1", 1, b.Left, b.Top, b.Width, b.Height, true, 1));
        }

        return result.OrderBy(m => m.Number).ToList();
    }

    public static MonitorInfo FindOrPrimary(string? deviceName)
    {
        var monitors = GetMonitors();
        return monitors.FirstOrDefault(m => string.Equals(m.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
               ?? monitors.FirstOrDefault(m => m.IsPrimary)
               ?? monitors[0];
    }

    private static int ParseNumber(string deviceName, int fallback)
    {
        var m = Regex.Match(deviceName ?? "", @"(\d+)\s*$");
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : fallback;
    }
}
