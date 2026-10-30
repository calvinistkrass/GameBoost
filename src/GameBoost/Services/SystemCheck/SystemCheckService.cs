using System.Diagnostics;
using GameBoost.Infrastructure;
using Microsoft.Win32;

namespace GameBoost.Services.SystemCheck;

public enum CheckStatus
{
    Good,
    Recommended,
    Info,
    Warning
}

/// <summary>Result of one PC check.</summary>
public sealed class SystemCheckItem : ObservableObject
{
    private CheckStatus _status;
    private string _current = "";
    private bool _canFix;
    private bool _canUndo;

    public SystemCheckItem(string id, string? link = null)
    {
        Id = id;
        Link = link;
    }

    public string Id { get; }
    public string Title => Loc.T($"PC.{Id}.Title");
    public string Description => Loc.T($"PC.{Id}.Desc");

    /// <summary>Optional Windows settings page or website that helps with this check.</summary>
    public string? Link { get; }

    public CheckStatus Status { get => _status; set => SetProperty(ref _status, value); }
    public string Current { get => _current; set => SetProperty(ref _current, value); }
    public bool CanFix { get => _canFix; set => SetProperty(ref _canFix, value); }
    public bool CanUndo { get => _canUndo; set => SetProperty(ref _canUndo, value); }

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
    }
}

/// <summary>One recommended in-game setting.</summary>
public sealed record GameSettingRecommendation(string Setting, string Value, string Why);

/// <summary>
/// Analyses the PC and the Windows settings that matter for gaming, applies recommended settings with one click
/// and can undo every change. All changes are per-user (no admin rights needed).
/// </summary>
public sealed class SystemCheckService
{
    private const string GameBarKey = @"Software\Microsoft\GameBar";
    private const string GameDvrKey = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";
    private const string MouseKey = @"Control Panel\Mouse";
    private const string DirectXKey = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private const string DirectXValue = "DirectXUserGlobalSettings";

    private readonly SettingsService _settings;

    public SystemCheckService(SettingsService settings)
    {
        _settings = settings;
    }

    private Dictionary<string, Dictionary<string, string?>> Backups => _settings.Settings.TweakBackups;

    /// <summary>Result of the most recent check (null until the first run).</summary>
    public (HardwareInfo Hardware, List<SystemCheckItem> Checks)? LastResult { get; private set; }

    /// <summary>Raised on the UI thread after every check run.</summary>
    public event EventHandler? Completed;

    public async Task<(HardwareInfo Hardware, List<SystemCheckItem> Checks)> RunAsync()
    {
        var result = await Task.Run(Run);
        LastResult = result;
        Completed?.Invoke(this, EventArgs.Empty);
        return result;
    }

    /// <summary>Reads hardware and runs all checks (read-only).</summary>
    public (HardwareInfo Hardware, List<SystemCheckItem> Checks) Run()
    {
        var hw = HardwareInfo.Read();
        var checks = new List<SystemCheckItem>
        {
            CheckGameMode(),
            CheckBackgroundRecording(),
            CheckMouseAcceleration(),
            CheckRefreshRate(hw),
            CheckWindowedOptimizations(),
            CheckPowerPlan(),
            CheckHags(),
            CheckDriver(hw),
            CheckRam(hw),
            CheckDisk(hw)
        };
        foreach (var c in checks)
        {
            if (Backups.ContainsKey(c.Id)) c.CanUndo = true;
        }
        Logger.Info($"PC check: {hw.Cpu} | {hw.Gpu} ({hw.VramGb} GB) | {hw.RamGb} GB RAM | {hw.ResolutionWidth}x{hw.ResolutionHeight}@{hw.CurrentHz}/{hw.MaxHz} Hz | tier {hw.Tier}");
        return (hw, checks);
    }

    public static int Score(IEnumerable<SystemCheckItem> checks)
    {
        var score = 100;
        foreach (var c in checks)
        {
            score -= c.Status switch
            {
                CheckStatus.Recommended => 8,
                CheckStatus.Warning => 15,
                _ => 0
            };
        }
        return Math.Clamp(score, 0, 100);
    }

    // ------------------------------------------------------------------ Checks

    private static SystemCheckItem CheckGameMode()
    {
        var item = new SystemCheckItem("GameMode", "ms-settings:gaming-gamemode");
        var value = ReadDword(GameBarKey, "AutoGameModeEnabled");
        var on = value is null or 1;
        item.Status = on ? CheckStatus.Good : CheckStatus.Recommended;
        item.Current = Loc.T(on ? "PC.Value.On" : "PC.Value.Off");
        item.CanFix = !on;
        return item;
    }

    private static SystemCheckItem CheckBackgroundRecording()
    {
        var item = new SystemCheckItem("Recording", "ms-settings:gaming-gamedvr");
        var on = ReadDword(GameDvrKey, "HistoricalCaptureEnabled") == 1;
        item.Status = on ? CheckStatus.Recommended : CheckStatus.Good;
        item.Current = Loc.T(on ? "PC.Value.On" : "PC.Value.Off");
        item.CanFix = on;
        return item;
    }

    private static SystemCheckItem CheckMouseAcceleration()
    {
        var item = new SystemCheckItem("MouseAccel", "ms-settings:mousetouchpad");
        var on = ReadString(MouseKey, "MouseSpeed") is { } s && s.Trim() != "0";
        item.Status = on ? CheckStatus.Recommended : CheckStatus.Good;
        item.Current = Loc.T(on ? "PC.Value.On" : "PC.Value.Off");
        item.CanFix = on;
        return item;
    }

    private static SystemCheckItem CheckRefreshRate(HardwareInfo hw)
    {
        var item = new SystemCheckItem("RefreshRate", "ms-settings:display-advanced");
        if (hw.CurrentHz <= 0)
        {
            item.Status = CheckStatus.Info;
            item.Current = "–";
            return item;
        }
        var low = hw.MaxHz > hw.CurrentHz;
        item.Status = low ? CheckStatus.Recommended : CheckStatus.Good;
        item.Current = low ? Loc.F("PC.Value.HzOf", hw.CurrentHz, hw.MaxHz) : $"{hw.CurrentHz} Hz";
        item.CanFix = low;
        return item;
    }

    private static SystemCheckItem CheckWindowedOptimizations()
    {
        var item = new SystemCheckItem("Windowed", "ms-settings:display-advancedgraphics");
        var settings = ParseDirectX(ReadString(DirectXKey, DirectXValue));
        var on = settings.TryGetValue("SwapEffectUpgradeEnable", out var swap) && swap == "1"
                 && settings.TryGetValue("VRROptimizeEnable", out var vrr) && vrr == "1";
        item.Status = on ? CheckStatus.Good : CheckStatus.Recommended;
        item.Current = Loc.T(on ? "PC.Value.On" : "PC.Value.Off");
        item.CanFix = !on;
        return item;
    }

    private SystemCheckItem CheckPowerPlan()
    {
        var item = new SystemCheckItem("PowerPlan", "ms-settings:powersleep");
        var active = PowerPlanManager.GetActiveScheme();
        var fast = active is PowerPlanManager.HighPerformanceGuid or PowerPlanManager.UltimatePerformanceGuid
                   || (active != null && string.Equals(active, _settings.Settings.CachedHighPerformanceSchemeGuid, StringComparison.OrdinalIgnoreCase));
        var gameModeHandles = _settings.Settings.Profiles.Any(p => p.SwitchPowerPlan);
        item.Status = fast ? CheckStatus.Good : gameModeHandles ? CheckStatus.Info : CheckStatus.Recommended;
        item.Current = Loc.T(fast ? "PC.Value.HighPerf" : gameModeHandles ? "PC.Value.ByGameMode" : "PC.Value.Balanced");
        item.CanFix = !fast;
        return item;
    }

    private static SystemCheckItem CheckHags()
    {
        var item = new SystemCheckItem("Hags", "ms-settings:display-advancedgraphics");
        int? mode = null;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
            mode = key?.GetValue("HwSchMode") as int?;
        }
        catch
        {
            // not readable
        }
        item.Status = mode == 2 ? CheckStatus.Good : CheckStatus.Info;
        item.Current = Loc.T(mode switch { 2 => "PC.Value.On", 1 => "PC.Value.Off", _ => "PC.Value.Default" });
        return item;
    }

    private static SystemCheckItem CheckDriver(HardwareInfo hw)
    {
        var link = hw.GpuVendor switch
        {
            GpuVendor.Nvidia => "https://www.nvidia.com/Download/index.aspx",
            GpuVendor.Amd => "https://www.amd.com/en/support/download/drivers.html",
            GpuVendor.Intel => "https://www.intel.com/content/www/us/en/download-center/home.html",
            _ => null
        };
        var item = new SystemCheckItem("Driver", link);
        if (hw.GpuDriverDate is not { } date)
        {
            item.Status = CheckStatus.Info;
            item.Current = "–";
            return item;
        }
        var ageDays = (DateTime.Now - date).TotalDays;
        item.Status = ageDays > 180 ? CheckStatus.Recommended : CheckStatus.Good;
        item.Current = Loc.F("PC.Value.DriverDate", date.ToString("d"), (int)(ageDays / 30));
        return item;
    }

    private static SystemCheckItem CheckRam(HardwareInfo hw)
    {
        var item = new SystemCheckItem("Ram");
        item.Status = hw.RamGb >= 15 ? CheckStatus.Good : CheckStatus.Info;
        item.Current = $"{hw.RamGb:0} GB";
        return item;
    }

    private static SystemCheckItem CheckDisk(HardwareInfo hw)
    {
        var item = new SystemCheckItem("Disk", "ms-settings:storagesense");
        if (hw.SystemDriveFreeGb is not { } free || hw.SystemDriveTotalGb is not { } total)
        {
            item.Status = CheckStatus.Info;
            item.Current = "–";
            return item;
        }
        item.Status = free < 20 || free / total < 0.1 ? CheckStatus.Warning : CheckStatus.Good;
        item.Current = Loc.F("PC.Value.Free", free.ToString("0"), total.ToString("0"));
        return item;
    }

    // ------------------------------------------------------------------ Fix / undo

    public bool Fix(string id, HardwareInfo hw)
    {
        try
        {
            var backup = new Dictionary<string, string?>();
            switch (id)
            {
                case "GameMode":
                    SetDword(GameBarKey, "AutoGameModeEnabled", 1, backup);
                    break;
                case "Recording":
                    SetDword(GameDvrKey, "HistoricalCaptureEnabled", 0, backup);
                    break;
                case "MouseAccel":
                    SetString(MouseKey, "MouseSpeed", "0", backup);
                    SetString(MouseKey, "MouseThreshold1", "0", backup);
                    SetString(MouseKey, "MouseThreshold2", "0", backup);
                    ApplyMouseParameters(0, 0, 0);
                    break;
                case "RefreshRate":
                    backup["display|hz"] = hw.CurrentHz.ToString();
                    if (!SetRefreshRate(hw.MaxHz)) return false;
                    break;
                case "Windowed":
                    var raw = ReadString(DirectXKey, DirectXValue);
                    backup[$"{DirectXKey}|{DirectXValue}"] = raw == null ? null : "sz:" + raw;
                    var parsed = ParseDirectX(raw);
                    parsed["SwapEffectUpgradeEnable"] = "1";
                    parsed["VRROptimizeEnable"] = "1";
                    WriteValue(DirectXKey, DirectXValue, "sz:" + string.Concat(parsed.Select(kv => $"{kv.Key}={kv.Value};")));
                    break;
                case "PowerPlan":
                    var previous = PowerPlanManager.GetActiveScheme();
                    var target = PowerPlanManager.EnsureHighPerformanceScheme(_settings.Settings.CachedHighPerformanceSchemeGuid, out var created);
                    if (created != null) _settings.Settings.CachedHighPerformanceSchemeGuid = created;
                    if (target == null || !PowerPlanManager.SetActive(target)) return false;
                    backup["power|scheme"] = previous;
                    break;
                default:
                    return false;
            }

            if (!Backups.ContainsKey(id)) Backups[id] = backup; // keep the very first original values
            _settings.RequestSave();
            Logger.Info($"PC check fix applied: {id}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"PC check fix failed: {id}", ex);
            return false;
        }
    }

    public bool Undo(string id)
    {
        if (!Backups.TryGetValue(id, out var backup)) return false;
        try
        {
            foreach (var (key, value) in backup)
            {
                switch (key)
                {
                    case "display|hz":
                        if (int.TryParse(value, out var hz)) SetRefreshRate(hz);
                        break;
                    case "power|scheme":
                        if (value != null) PowerPlanManager.SetActive(value);
                        break;
                    default:
                        var sep = key.LastIndexOf('|');
                        WriteValue(key[..sep], key[(sep + 1)..], value);
                        break;
                }
            }

            if (id == "MouseAccel")
            {
                var speed = int.TryParse(ReadString(MouseKey, "MouseSpeed"), out var s) ? s : 1;
                var t1 = int.TryParse(ReadString(MouseKey, "MouseThreshold1"), out var a) ? a : 6;
                var t2 = int.TryParse(ReadString(MouseKey, "MouseThreshold2"), out var b) ? b : 10;
                ApplyMouseParameters(t1, t2, speed);
            }

            Backups.Remove(id);
            _settings.RequestSave();
            Logger.Info($"PC check change undone: {id}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"PC check undo failed: {id}", ex);
            return false;
        }
    }

    public static void OpenLink(string? link)
    {
        if (string.IsNullOrEmpty(link)) return;
        try
        {
            Process.Start(new ProcessStartInfo(link) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Warn($"Could not open {link}", ex);
        }
    }

    // ------------------------------------------------------------------ Game setting recommendations

    public static List<GameSettingRecommendation> GameRecommendations(HardwareInfo hw)
    {
        var tier = hw.Tier;
        string Pick(string entry, string mid, string high, string enthusiast) => tier switch
        {
            PerformanceTier.Entry => entry,
            PerformanceTier.Mid => mid,
            PerformanceTier.High => high,
            _ => enthusiast
        };

        var list = new List<GameSettingRecommendation>
        {
            new(Loc.T("GR.DisplayMode"), Loc.T("GR.DisplayMode.Value"), Loc.T("GR.DisplayMode.Why")),
            new(Loc.T("GR.Resolution"), hw.ResolutionWidth > 0 ? $"{hw.ResolutionWidth} × {hw.ResolutionHeight}" : Loc.T("GR.Native"), Loc.T("GR.Resolution.Why")),
            new(Loc.T("GR.RefreshRate"), hw.MaxHz > 0 ? $"{hw.MaxHz} Hz" : Loc.T("GR.Max"), Loc.T("GR.RefreshRate.Why")),
            new(Loc.T("GR.FpsLimit"), hw.MaxHz >= 100 ? Loc.F("GR.FpsLimit.Value", hw.MaxHz - 3) : Loc.T("GR.FpsLimit.Uncapped"), Loc.T("GR.FpsLimit.Why")),
            new(Loc.T("GR.VSync"), Loc.T("PC.Value.Off"), Loc.T("GR.VSync.Why")),
            new(Loc.T("GR.Preset"), Loc.T(Pick("GR.Low", "GR.Medium", "GR.High", "GR.Ultra")), Loc.F("GR.Preset.Why", Loc.T($"Tier.{tier}"))),
            new(Loc.T("GR.Textures"), Loc.T(hw.VramGb switch { < 4 => "GR.Low", < 6 => "GR.Medium", < 10 => "GR.High", _ => "GR.Ultra" }), Loc.T("GR.Textures.Why")),
            new(Loc.T("GR.Shadows"), Loc.T(Pick("GR.Low", "GR.Low", "GR.Medium", "GR.High")), Loc.T("GR.Shadows.Why")),
            new(Loc.T("GR.AntiAliasing"), Pick("FXAA / Off", "TAA (low)", "TAA", "TAA / MSAA 4×"), Loc.T("GR.AntiAliasing.Why")),
            new(Loc.T("GR.Upscaling"), hw.GpuVendor == GpuVendor.Nvidia ? Pick("DLSS Performance", "DLSS Balanced", "DLSS Quality", "DLAA / DLSS Quality") : Pick("FSR Performance", "FSR Balanced", "FSR Quality", Loc.T("GR.Native")), Loc.T("GR.Upscaling.Why")),
            new(Loc.T("GR.Latency"), hw.GpuVendor switch { GpuVendor.Nvidia => "NVIDIA Reflex: On + Boost", GpuVendor.Amd => "AMD Anti-Lag: On", _ => Loc.T("GR.Latency.Generic") }, Loc.T("GR.Latency.Why")),
            new(Loc.T("GR.Effects"), Loc.T("PC.Value.Off"), Loc.T("GR.Effects.Why"))
        };
        return list;
    }

    // ------------------------------------------------------------------ Helpers

    private static int? ReadDword(string path, string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(path);
            return key?.GetValue(name) as int?;
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadString(string path, string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(path);
            return key?.GetValue(name)?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static string? Snapshot(string path, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        var value = key?.GetValue(name);
        return value switch
        {
            null => null,
            int i => "dword:" + i,
            _ => "sz:" + value
        };
    }

    private static void SetDword(string path, string name, int value, Dictionary<string, string?> backup)
    {
        backup[$"{path}|{name}"] = Snapshot(path, name);
        WriteValue(path, name, "dword:" + value);
    }

    private static void SetString(string path, string name, string value, Dictionary<string, string?> backup)
    {
        backup[$"{path}|{name}"] = Snapshot(path, name);
        WriteValue(path, name, "sz:" + value);
    }

    private static void WriteValue(string path, string name, string? encoded)
    {
        using var key = Registry.CurrentUser.CreateSubKey(path, writable: true);
        if (encoded == null)
        {
            key.DeleteValue(name, throwOnMissingValue: false);
        }
        else if (encoded.StartsWith("dword:", StringComparison.Ordinal))
        {
            key.SetValue(name, int.Parse(encoded[6..]), RegistryValueKind.DWord);
        }
        else
        {
            key.SetValue(name, encoded.StartsWith("sz:", StringComparison.Ordinal) ? encoded[3..] : encoded, RegistryValueKind.String);
        }
    }

    internal static Dictionary<string, string> ParseDirectX(string? raw)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw)) return result;
        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq > 0) result[part[..eq].Trim()] = part[(eq + 1)..].Trim();
        }
        return result;
    }

    private static void ApplyMouseParameters(int threshold1, int threshold2, int speed)
    {
        Win32.SystemParametersInfo(Win32.SPI_SETMOUSE, 0, new[] { threshold1, threshold2, speed },
            Win32.SPIF_UPDATEINIFILE | Win32.SPIF_SENDCHANGE);
    }

    private static bool SetRefreshRate(int hz)
    {
        var mode = Win32.NewDevMode();
        if (!Win32.EnumDisplaySettings(null, Win32.ENUM_CURRENT_SETTINGS, ref mode)) return false;
        mode.dmDisplayFrequency = (uint)hz;
        mode.dmFields = Win32.DM_DISPLAYFREQUENCY;
        var result = Win32.ChangeDisplaySettingsEx(null, ref mode, IntPtr.Zero, Win32.CDS_UPDATEREGISTRY, IntPtr.Zero);
        if (result != Win32.DISP_CHANGE_SUCCESSFUL) Logger.Warn($"Refresh rate {hz} Hz could not be set (code {result})");
        return result == Win32.DISP_CHANGE_SUCCESSFUL;
    }
}
