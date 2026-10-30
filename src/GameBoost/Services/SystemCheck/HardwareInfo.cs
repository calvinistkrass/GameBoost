using System.Globalization;
using System.Text.RegularExpressions;
using GameBoost.Infrastructure;
using Microsoft.Win32;

namespace GameBoost.Services.SystemCheck;

public enum PerformanceTier
{
    Entry,
    Mid,
    High,
    Enthusiast
}

public enum GpuVendor
{
    Unknown,
    Nvidia,
    Amd,
    Intel
}

/// <summary>Basic hardware facts, read without admin rights (registry, Win32).</summary>
public sealed record HardwareInfo(
    string Cpu,
    int Threads,
    string Gpu,
    GpuVendor GpuVendor,
    double? VramGb,
    bool IntegratedGpu,
    DateTime? GpuDriverDate,
    double RamGb,
    string Os,
    int ResolutionWidth,
    int ResolutionHeight,
    int CurrentHz,
    int MaxHz,
    double? SystemDriveFreeGb,
    double? SystemDriveTotalGb)
{
    public PerformanceTier Tier
    {
        get
        {
            PerformanceTier tier;
            if (IntegratedGpu) tier = PerformanceTier.Entry;
            else if (TierFromModel(Gpu) is { } byModel) tier = byModel;
            else if (VramGb is null or < 4) tier = PerformanceTier.Entry;
            else if (VramGb < 8) tier = PerformanceTier.Mid;
            else if (VramGb < 12) tier = PerformanceTier.High;
            else tier = PerformanceTier.Enthusiast;

            if (RamGb < 12 && tier > PerformanceTier.Mid) tier = PerformanceTier.Mid;
            if (Threads > 0 && Threads < 6 && tier > PerformanceTier.Mid) tier = PerformanceTier.Mid;
            return tier;
        }
    }

    /// <summary>Performance class from the GPU model name (e.g. "RTX 4070" → High). Null if unknown.</summary>
    internal static PerformanceTier? TierFromModel(string gpu)
    {
        var nv = Regex.Match(gpu, @"\b(RTX|GTX)\s*(\d{1,2})(\d{2})\b", RegexOptions.IgnoreCase);
        if (nv.Success)
        {
            var model = int.Parse(nv.Groups[3].Value, CultureInfo.InvariantCulture);
            if (nv.Groups[1].Value.Equals("GTX", StringComparison.OrdinalIgnoreCase))
                return model <= 50 ? PerformanceTier.Entry : PerformanceTier.Mid;
            return model switch
            {
                <= 60 => PerformanceTier.Mid,
                70 => PerformanceTier.High,
                _ => PerformanceTier.Enthusiast
            };
        }

        var amd = Regex.Match(gpu, @"\bRX\s*(\d)(\d)(\d)(\d)\b", RegexOptions.IgnoreCase);
        if (amd.Success)
        {
            // RX 9060/9070 use the third digit, older series (5000–7000) the second.
            var digit = amd.Groups[1].Value == "9" ? amd.Groups[3].Value[0] : amd.Groups[2].Value[0];
            return digit switch
            {
                <= '5' => PerformanceTier.Entry,
                '6' => PerformanceTier.Mid,
                '7' => PerformanceTier.High,
                _ => PerformanceTier.Enthusiast
            };
        }
        if (Regex.IsMatch(gpu, @"\bRX\s*\d{3}\b", RegexOptions.IgnoreCase)) return PerformanceTier.Entry; // RX 580 etc.

        var arc = Regex.Match(gpu, @"\bArc\S*\s*([AB])(\d)", RegexOptions.IgnoreCase);
        if (arc.Success) return arc.Groups[2].Value == "3" ? PerformanceTier.Entry : PerformanceTier.Mid;

        return null;
    }

    public static HardwareInfo Read()
    {
        var (gpu, vendor, vram, integrated, driverDate) = ReadGpu();
        var (w, h, hz, maxHz) = ReadDisplay();
        var mem = Win32.GetMemoryStatus();
        double? free = null, total = null;
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
            free = drive.AvailableFreeSpace / 1024d / 1024d / 1024d;
            total = drive.TotalSize / 1024d / 1024d / 1024d;
        }
        catch (Exception ex)
        {
            Logger.Warn("System drive could not be read", ex);
        }

        return new HardwareInfo(ReadCpu(), Environment.ProcessorCount, gpu, vendor, vram, integrated, driverDate,
            Math.Round(mem.ullTotalPhys / 1024d / 1024d / 1024d, 1), ReadOs(), w, h, hz, maxHz, free, total);
    }

    private static string ReadCpu()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            var name = key?.GetValue("ProcessorNameString") as string;
            if (!string.IsNullOrWhiteSpace(name)) return Regex.Replace(name.Trim(), @"\s+", " ");
        }
        catch (Exception ex)
        {
            Logger.Warn("CPU name could not be read", ex);
        }
        return "Unknown CPU";
    }

    private static string ReadOs()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var build = int.TryParse(key?.GetValue("CurrentBuildNumber") as string, out var b) ? b : Environment.OSVersion.Version.Build;
            var display = key?.GetValue("DisplayVersion") as string;
            var name = build >= 22000 ? "Windows 11" : "Windows 10";
            return string.IsNullOrEmpty(display) ? $"{name} (Build {build})" : $"{name} {display} (Build {build})";
        }
        catch
        {
            return Environment.OSVersion.VersionString;
        }
    }

    private static (string, GpuVendor, double?, bool, DateTime?) ReadGpu()
    {
        const string classKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
        var candidates = new List<(string Name, GpuVendor Vendor, double? Vram, bool Integrated, DateTime? Date)>();
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(classKey);
            if (root != null)
            {
                foreach (var sub in root.GetSubKeyNames().Where(n => n.All(char.IsDigit)))
                {
                    try
                    {
                        using var key = root.OpenSubKey(sub);
                        var name = key?.GetValue("DriverDesc") as string;
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        if (name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("Remote", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("Virtual", StringComparison.OrdinalIgnoreCase)) continue;

                        double? vram = null;
                        var raw = key!.GetValue("HardwareInformation.qwMemorySize") ?? key.GetValue("HardwareInformation.MemorySize");
                        vram = raw switch
                        {
                            long l when l > 0 => l / 1024d / 1024d / 1024d,
                            int i when i > 0 => (uint)i / 1024d / 1024d / 1024d,
                            byte[] bytes when bytes.Length >= 8 => BitConverter.ToInt64(bytes, 0) / 1024d / 1024d / 1024d,
                            byte[] bytes when bytes.Length >= 4 => BitConverter.ToUInt32(bytes, 0) / 1024d / 1024d / 1024d,
                            _ => null
                        };

                        var vendor = name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Nvidia
                            : name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Amd
                            : name.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Intel
                            : GpuVendor.Unknown;

                        var integrated = vendor == GpuVendor.Intel && !name.Contains("Arc", StringComparison.OrdinalIgnoreCase)
                                         || vendor == GpuVendor.Amd && !Regex.IsMatch(name, @"\bRX\b|Radeon Pro|\bR[579]\b", RegexOptions.IgnoreCase)
                                         || vram is < 1.5;

                        DateTime? date = null;
                        if (key.GetValue("DriverDate") is string ds &&
                            DateTime.TryParseExact(ds, new[] { "M-d-yyyy", "MM-dd-yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                            date = d;

                        candidates.Add((name.Trim(), vendor, vram, integrated, date));
                    }
                    catch
                    {
                        // access denied for some subkeys – skip
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("GPU information could not be read", ex);
        }

        if (candidates.Count == 0) return ("Unknown GPU", GpuVendor.Unknown, null, false, null);
        var best = candidates.OrderBy(c => c.Integrated).ThenByDescending(c => c.Vram ?? 0).First();
        return (best.Name, best.Vendor, best.Vram is { } v ? Math.Round(v, 1) : null, best.Integrated, best.Date);
    }

    private static (int, int, int, int) ReadDisplay()
    {
        try
        {
            var current = Win32.NewDevMode();
            if (!Win32.EnumDisplaySettings(null, Win32.ENUM_CURRENT_SETTINGS, ref current)) return (0, 0, 0, 0);
            var maxHz = (int)current.dmDisplayFrequency;
            var mode = Win32.NewDevMode();
            for (var i = 0; Win32.EnumDisplaySettings(null, i, ref mode); i++)
            {
                if (mode.dmPelsWidth == current.dmPelsWidth && mode.dmPelsHeight == current.dmPelsHeight && mode.dmBitsPerPel == current.dmBitsPerPel)
                    maxHz = Math.Max(maxHz, (int)mode.dmDisplayFrequency);
                mode = Win32.NewDevMode();
            }
            return ((int)current.dmPelsWidth, (int)current.dmPelsHeight, (int)current.dmDisplayFrequency, maxHz);
        }
        catch (Exception ex)
        {
            Logger.Warn("Display mode could not be read", ex);
            return (0, 0, 0, 0);
        }
    }
}
