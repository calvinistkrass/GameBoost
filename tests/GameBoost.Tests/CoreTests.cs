using System.Diagnostics;
using GameBoost.Infrastructure;
using GameBoost.Models;
using GameBoost.Services;

namespace GameBoost.Tests;

public class CrosshairCodecTests
{
    [Fact]
    public void Encode_Decode_RoundTrip()
    {
        var original = new CrosshairConfig
        {
            Style = CrosshairStyle.TShape, Color = "#12ABEF", Opacity = 0.75, Length = 9, Thickness = 3, Gap = 5,
            DotSize = 4, Outline = false, OutlineColor = "#FFFFFF", OutlineThickness = 2, OffsetX = -7, OffsetY = 12
        };

        var code = CrosshairCodec.Encode(original);
        Assert.StartsWith("GB1-", code);
        Assert.True(CrosshairCodec.TryDecode(code, out var decoded));
        Assert.True(original.ValueEquals(decoded));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hallo")]
    [InlineData("GB1-")]
    [InlineData("GB1-!!!!")]
    [InlineData("GB2-MCwwMEZGN0YsMTAwLDYsMiwzLDIsMSwwMDAwMDAsMSwwLDA")]
    public void Decode_Rejects_Invalid(string? code)
    {
        Assert.False(CrosshairCodec.TryDecode(code, out _));
    }

    [Fact]
    public void Decode_Tolerates_Whitespace_And_Clamps_Values()
    {
        // Length 999 and offset 5000 are clamped to the allowed limits.
        var raw = "1,FF0000,100,999,2,3,2,1,000000,1,5000,0";
        var code = "GB1-" + Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(raw)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.True(CrosshairCodec.TryDecode("  " + code.Insert(8, " \n") + "  ", out var c));
        Assert.Equal(CrosshairConfig.MaxLength, c.Length);
        Assert.Equal(CrosshairConfig.MaxOffset, c.OffsetX);
        Assert.Equal(CrosshairStyle.Dot, c.Style);
    }
}

public class ColorUtilTests
{
    [Theory]
    [InlineData("#00ff7f", "#00FF7F")]
    [InlineData("00FF7F", "#00FF7F")]
    [InlineData("#0F7", "#00FF77")]
    [InlineData("#FF00FF7F", "#00FF7F")]
    [InlineData("xyz", null)]
    [InlineData("#12345", null)]
    public void Normalize(string input, string? expected) => Assert.Equal(expected, ColorUtil.Normalize(input));

    [Fact]
    public void Config_Ignores_Invalid_Color()
    {
        var c = new CrosshairConfig { Color = "#112233" };
        c.Color = "kaputt";
        Assert.Equal("#112233", c.Color);
    }
}

public class ProcessNameTests
{
    [Theory]
    [InlineData(@"C:\Games\VALORANT-Win64-Shipping.exe", "valorant-win64-shipping")]
    [InlineData("Discord.EXE", "discord")]
    [InlineData("  steam  ", "steam")]
    [InlineData("\"C:\\x\\a b.exe\"", "a b")]
    [InlineData("", "")]
    public void Normalize(string input, string expected) => Assert.Equal(expected, ProcessNames.Normalize(input));
}

public class ProcessGuardTests
{
    [Theory]
    [InlineData("explorer")]
    [InlineData("svchost")]
    [InlineData("csrss")]
    [InlineData("lsass")]
    [InlineData("dwm")]
    [InlineData("MsMpEng")]
    [InlineData("vgc")]
    [InlineData("GameBoost")]
    public void Blacklist_Contains_Critical_Processes(string name) => Assert.True(ProcessGuard.IsProtectedName(name));

    [Fact]
    public void Windows_Directory_Is_Protected()
    {
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        Assert.True(ProcessGuard.IsProtectedPath(Path.Combine(win, "System32", "notepad.exe")));
        Assert.False(ProcessGuard.IsProtectedPath(@"D:\Games\Steam\steam.exe"));
    }

    [Fact]
    public void Own_Process_Is_Never_Allowed()
    {
        using var self = Process.GetCurrentProcess();
        Assert.False(ProcessGuard.Check(self).Allowed);
    }

    [Fact]
    public void System_Processes_Are_Never_Allowed()
    {
        foreach (var name in new[] { "csrss", "winlogon", "services", "lsass", "explorer", "svchost" })
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                using (p) Assert.False(ProcessGuard.Check(p).Allowed, $"{name} ({p.Id}) must never be closable");
            }
        }
    }

    [Fact]
    public void Process_From_Windows_Directory_Is_Not_Allowed()
    {
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "ping.exe"), "-n 10 127.0.0.1")
        {
            CreateNoWindow = true, UseShellExecute = false
        };
        using var p = Process.Start(psi)!;
        try
        {
            Assert.False(ProcessGuard.Check(p).Allowed);
        }
        finally
        {
            p.Kill();
        }
    }

    [Fact]
    public void Own_User_Process_Outside_Windows_Is_Allowed()
    {
        // A copy of ping.exe in a temp folder = a normal user program.
        var dir = Path.Combine(Path.GetTempPath(), "gb-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, "gbtestping.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), exe);

        var psi = new ProcessStartInfo(exe, "-n 10 127.0.0.1") { CreateNoWindow = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        try
        {
            Thread.Sleep(300);
            var result = ProcessGuard.Check(p);
            Assert.True(result.Allowed, result.Reason);
            Assert.Equal(exe, result.Path, ignoreCase: true);
        }
        finally
        {
            p.Kill();
            p.WaitForExit(3000);
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }
}

public class PowerPlanParsingTests
{
    [Fact]
    public void Parses_Guid_From_Localized_Output()
    {
        const string de = "GUID des Energieschemas: 381b4222-f694-41f0-9685-ff5bb260df2e  (Ausbalanciert)";
        Assert.Equal("381b4222-f694-41f0-9685-ff5bb260df2e", PowerPlanManager.ParseFirstGuid(de));
    }

    [Fact]
    public void Parses_All_Guids_From_List()
    {
        const string list = """
            Bestehende Energieschemas (* Aktiv)
            -----------------------------------
            GUID des Energieschemas: 381b4222-f694-41f0-9685-ff5bb260df2e  (Ausbalanciert) *
            GUID des Energieschemas: 8C5E7FDA-E8BF-4A96-9A85-A6E23A8C635C  (Höchstleistung)
            """;
        var guids = PowerPlanManager.ParseAllGuids(list);
        Assert.Equal(2, guids.Count);
        Assert.Contains(PowerPlanManager.HighPerformanceGuid, guids);
    }

    [Fact]
    public void Active_Scheme_Can_Be_Read()
    {
        // Read-only – does not change the system.
        Assert.NotNull(PowerPlanManager.GetActiveScheme());
    }
}

public class HotkeyTests
{
    [Fact]
    public void Default_Hotkeys_Are_Distinct_And_Valid()
    {
        var h = new HotkeySettings();
        var all = new[] { h.ToggleCrosshair!, h.ToggleGameMode!, h.NextPreset! };
        Assert.All(all, b => Assert.True(b.IsValid));
        Assert.Equal(3, all.Select(b => (b.Modifiers, b.Key)).Distinct().Count());
    }

    [Fact]
    public void ToString_Shows_Modifiers_And_Key()
    {
        Loc.Apply("en");
        var b = new HotkeyBinding { Modifiers = HotkeyBinding.ModControl | HotkeyBinding.ModAlt, Key = 0x43 };
        Assert.Equal("Ctrl + Alt + C", b.ToString());
    }
}

public class LocalizationTests
{
    [Fact]
    public void All_Texts_Have_Both_Languages()
    {
        foreach (var (key, (de, en)) in Loc.Strings)
        {
            Assert.False(string.IsNullOrWhiteSpace(de), $"DE missing: {key}");
            Assert.False(string.IsNullOrWhiteSpace(en), $"EN missing: {key}");
        }
    }

    [Fact]
    public void All_Xaml_Resource_Keys_Exist()
    {
        // Every {DynamicResource X} text key used in the views must be translated.
        var root = FindRepoRoot();
        var xamlFiles = Directory.GetFiles(Path.Combine(root, "src", "GameBoost"), "*.xaml", SearchOption.AllDirectories);
        var regex = new System.Text.RegularExpressions.Regex(@"\{DynamicResource ([\w\.]+)\}");
        var missing = new List<string>();
        foreach (var file in xamlFiles)
        {
            foreach (System.Text.RegularExpressions.Match m in regex.Matches(File.ReadAllText(file)))
            {
                var key = m.Groups[1].Value;
                if (!Loc.Strings.ContainsKey(key)) missing.Add($"{Path.GetFileName(file)}: {key}");
            }
        }
        Assert.Empty(missing);
    }

    [Fact]
    public void All_Code_Keys_Exist()
    {
        var root = FindRepoRoot();
        var csFiles = Directory.GetFiles(Path.Combine(root, "src", "GameBoost"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .Where(f => !f.EndsWith("Loc.cs", StringComparison.OrdinalIgnoreCase)); // contains an example in a doc comment
        var regex = new System.Text.RegularExpressions.Regex(@"Loc\.[TF]\(""([\w\.]+)""");
        var missing = new List<string>();
        foreach (var file in csFiles)
        {
            foreach (System.Text.RegularExpressions.Match m in regex.Matches(File.ReadAllText(file)))
            {
                var key = m.Groups[1].Value;
                if (!Loc.Strings.ContainsKey(key)) missing.Add($"{Path.GetFileName(file)}: {key}");
            }
        }
        Assert.Empty(missing);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "GameBoost"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found");
    }
}

[Collection("Settings")]
public class SettingsServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gb-settings-" + Guid.NewGuid().ToString("N"));

    public SettingsServiceTests()
    {
        AppPaths.OverrideDataDirectory(_dir);
    }

    [Fact]
    public void Creates_Defaults_And_RoundTrips()
    {
        var s = new SettingsService();
        s.Load();
        Assert.True(File.Exists(AppPaths.SettingsFile));
        Assert.Equal(3, s.Settings.Profiles.Count);
        Assert.Equal(5, s.Settings.Crosshair.Presets.Count);

        s.Settings.Profiles[0].ProcessesToKill.Add("discord");
        s.Settings.Crosshair.Current.Color = "#ABCDEF";
        s.Settings.Hotkeys.NextPreset = null;
        s.Save();

        var s2 = new SettingsService();
        s2.Load();
        Assert.Contains("discord", s2.Settings.Profiles[0].ProcessesToKill);
        Assert.Equal("#ABCDEF", s2.Settings.Crosshair.Current.Color);
        Assert.Null(s2.Settings.Hotkeys.NextPreset);
        Assert.Equal(s.Settings.ActiveProfileId, s2.Settings.ActiveProfileId);
    }

    [Fact]
    public void Corrupt_File_Falls_Back_To_Defaults_With_Backup()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(AppPaths.SettingsFile, "{ this is not json");
        var s = new SettingsService();
        s.Load();
        Assert.NotEmpty(s.Settings.Profiles);
        Assert.NotEmpty(Directory.GetFiles(_dir, "settings.corrupt-*.json"));
    }

    [Fact]
    public void Normalize_Repairs_Bad_Values()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(AppPaths.SettingsFile, """
            { "SchemaVersion": 2, "Language": "fr", "Profiles": [ { "Id": "", "Name": "X", "ProcessesToKill": ["Discord.exe", "discord", ""] } ],
              "ActiveProfileId": "doesnotexist", "Crosshair": { "Current": { "Length": 9999, "Color": "red-ish" } } }
            """);
        var s = new SettingsService();
        s.Load();
        Assert.Equal("en", s.Settings.Language);
        Assert.Single(s.Settings.Profiles);
        Assert.Equal(new[] { "discord" }, s.Settings.Profiles[0].ProcessesToKill);
        Assert.Equal(s.Settings.Profiles[0].Id, s.Settings.ActiveProfileId);
        Assert.Equal(CrosshairConfig.MaxLength, s.Settings.Crosshair.Current.Length);
        Assert.Equal("#00FF7F", s.Settings.Crosshair.Current.Color);
    }

    [Fact]
    public void Schema1_German_Settings_Are_Migrated_To_English()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(AppPaths.SettingsFile, """
            { "SchemaVersion": 1, "Language": "de",
              "Profiles": [ { "Id": "a", "Name": "Standard", "CrosshairPresetName": "Roter Punkt" } ],
              "Crosshair": { "ActivePresetName": "Klassisch Grün",
                             "Presets": [ { "Name": "Klassisch Grün", "Config": {} }, { "Name": "Mein Preset", "Config": {} } ] } }
            """);
        var s = new SettingsService();
        s.Load();
        Assert.Equal("en", s.Settings.Language);
        Assert.Equal(AppSettings.CurrentSchemaVersion, s.Settings.SchemaVersion);
        Assert.Equal("Default", s.Settings.Profiles[0].Name);
        Assert.Equal("Red Dot", s.Settings.Profiles[0].CrosshairPresetName);
        Assert.Equal("Classic Green", s.Settings.Crosshair.ActivePresetName);
        Assert.Equal(new[] { "Classic Green", "Mein Preset" }, s.Settings.Crosshair.Presets.Select(p => p.Name));
    }

    [Fact]
    public void New_Settings_Default_To_English()
    {
        var s = new SettingsService();
        s.Load();
        Assert.Equal("en", s.Settings.Language);
        Assert.Equal("Default", s.Settings.Profiles[0].Name);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* ignore */ }
    }
}
