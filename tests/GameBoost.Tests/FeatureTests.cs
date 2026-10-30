using GameBoost.Infrastructure;
using GameBoost.Models;
using GameBoost.Rendering;
using GameBoost.Services;
using GameBoost.Services.Performance;
using GameBoost.Services.SystemCheck;
using Xunit.Abstractions;

namespace GameBoost.Tests;

public class HudRendererTests
{
    private static T RunSta<T>(Func<T> func)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = func(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) throw error;
        return result;
    }

    private static PerformanceSnapshot Sample(double? fps = 144) =>
        new(fps, "game", false, 37, 88, 71, 6.2, 12.5, 32, new DateTime(2026, 10, 30, 21, 15, 0));

    [Fact]
    public void Renders_Visible_Panel()
    {
        var img = RunSta(() => HudRenderer.Render(Sample(), new HudSettings(), 1.0));
        Assert.True(img.Width > 80 && img.Height > 60, $"{img.Width}x{img.Height}");
        Assert.True(img.CountVisiblePixels() > img.Width * img.Height / 2, "background should cover the panel");
    }

    [Fact]
    public void Scale_Makes_Panel_Bigger()
    {
        var (small, big) = RunSta(() => (HudRenderer.Render(Sample(), new HudSettings(), 1.0), HudRenderer.Render(Sample(), new HudSettings(), 2.0)));
        Assert.True(big.Width > small.Width * 1.6 && big.Height > small.Height * 1.6);
    }

    [Fact]
    public void Fewer_Metrics_Make_Panel_Smaller()
    {
        var (all, few) = RunSta(() => (
            HudRenderer.Render(Sample(), new HudSettings(), 1.0),
            HudRenderer.Render(Sample(), new HudSettings { ShowCpu = false, ShowRam = false, ShowVram = false }, 1.0)));
        Assert.True(few.Height < all.Height);
    }

    [Fact]
    public void Transparent_Background_Only_Draws_Text()
    {
        var img = RunSta(() => HudRenderer.Render(Sample(), new HudSettings { BackgroundOpacity = 0 }, 1.0));
        var visible = img.CountVisiblePixels();
        Assert.InRange(visible, 50, img.Width * img.Height / 2);
    }

    [Fact]
    public void Pixels_Are_Premultiplied()
    {
        var img = RunSta(() => HudRenderer.Render(Sample(null), new HudSettings { BackgroundOpacity = 40 }, 1.0));
        for (var y = 0; y < img.Height; y++)
        for (var x = 0; x < img.Width; x++)
        {
            var (b, g, r, a) = img.GetPixel(x, y);
            Assert.True(b <= a && g <= a && r <= a, $"pixel {x},{y} not premultiplied");
        }
    }
}

public class SystemCheckTests
{
    private readonly ITestOutputHelper _output;
    public SystemCheckTests(ITestOutputHelper output) => _output = output;

    private static HardwareInfo Hw(double? vram, double ram = 32, int threads = 16, bool integrated = false, GpuVendor vendor = GpuVendor.Nvidia) =>
        new("CPU", threads, "GPU", vendor, vram, integrated, DateTime.Now, ram, "Windows 11", 2560, 1440, 144, 165, 200, 1000);

    [Theory]
    [InlineData(2.0, PerformanceTier.Entry)]
    [InlineData(6.0, PerformanceTier.Mid)]
    [InlineData(8.0, PerformanceTier.High)]
    [InlineData(16.0, PerformanceTier.Enthusiast)]
    public void Tier_Follows_Vram(double vram, PerformanceTier expected) => Assert.Equal(expected, Hw(vram).Tier);

    [Theory]
    [InlineData("NVIDIA GeForce RTX 3050", PerformanceTier.Mid)]
    [InlineData("NVIDIA GeForce RTX 4070 Ti SUPER", PerformanceTier.High)]
    [InlineData("NVIDIA GeForce RTX 5090", PerformanceTier.Enthusiast)]
    [InlineData("NVIDIA GeForce GTX 1050 Ti", PerformanceTier.Entry)]
    [InlineData("NVIDIA GeForce GTX 1660 SUPER", PerformanceTier.Mid)]
    [InlineData("AMD Radeon RX 6600", PerformanceTier.Mid)]
    [InlineData("AMD Radeon RX 7900 XTX", PerformanceTier.Enthusiast)]
    [InlineData("AMD Radeon RX 9070 XT", PerformanceTier.High)]
    [InlineData("Radeon RX 580 Series", PerformanceTier.Entry)]
    [InlineData("Intel(R) Arc(TM) A770 Graphics", PerformanceTier.Mid)]
    [InlineData("Some Unknown GPU", null)]
    public void Tier_From_Gpu_Model(string gpu, PerformanceTier? expected) => Assert.Equal(expected, HardwareInfo.TierFromModel(gpu));

    [Fact]
    public void Model_Beats_Vram_For_Tier()
    {
        var hw = Hw(8) with { Gpu = "NVIDIA GeForce RTX 3050" };
        Assert.Equal(PerformanceTier.Mid, hw.Tier);
    }

    [Fact]
    public void Low_Ram_Or_Integrated_Caps_Tier()
    {
        Assert.Equal(PerformanceTier.Mid, Hw(16, ram: 8).Tier);
        Assert.Equal(PerformanceTier.Entry, Hw(16, integrated: true).Tier);
    }

    [Fact]
    public void Game_Recommendations_Match_Hardware()
    {
        Loc.Apply("en");
        var recs = SystemCheckService.GameRecommendations(Hw(12));
        Assert.True(recs.Count >= 10);
        Assert.Contains(recs, r => r.Value.Contains("162 FPS"));     // 165 Hz − 3
        Assert.Contains(recs, r => r.Value.Contains("Reflex"));      // NVIDIA
        Assert.Contains(recs, r => r.Value == "2560 × 1440");
        Assert.DoesNotContain(recs, r => string.IsNullOrWhiteSpace(r.Setting) || r.Setting.StartsWith("GR."));
    }

    [Fact]
    public void Score_Penalizes_Open_Items()
    {
        var items = new[]
        {
            new SystemCheckItem("A") { Status = CheckStatus.Good },
            new SystemCheckItem("B") { Status = CheckStatus.Recommended },
            new SystemCheckItem("C") { Status = CheckStatus.Warning },
            new SystemCheckItem("D") { Status = CheckStatus.Info }
        };
        Assert.Equal(100 - 8 - 15, SystemCheckService.Score(items));
    }

    [Fact]
    public void DirectX_Settings_Are_Parsed()
    {
        var d = SystemCheckService.ParseDirectX("SwapEffectUpgradeEnable=1;VRROptimizeEnable=0;AutoHDREnable=1;");
        Assert.Equal("1", d["SwapEffectUpgradeEnable"]);
        Assert.Equal("0", d["VRROptimizeEnable"]);
        Assert.Equal(3, d.Count);
        Assert.Empty(SystemCheckService.ParseDirectX(null));
    }

    [Fact]
    public void Real_Hardware_Can_Be_Read()
    {
        var hw = HardwareInfo.Read();
        _output.WriteLine($"{hw.Cpu} | {hw.Threads} threads | {hw.Gpu} ({hw.GpuVendor}, {hw.VramGb} GB, integrated {hw.IntegratedGpu}, driver {hw.GpuDriverDate:d}) | {hw.RamGb} GB | {hw.Os} | {hw.ResolutionWidth}x{hw.ResolutionHeight} {hw.CurrentHz}/{hw.MaxHz} Hz | C: {hw.SystemDriveFreeGb:0}/{hw.SystemDriveTotalGb:0} GB | {hw.Tier}");
        Assert.False(string.IsNullOrWhiteSpace(hw.Cpu));
        Assert.True(hw.Threads > 0);
        Assert.True(hw.RamGb > 1);
    }

    [Fact]
    public void All_Check_Texts_Exist()
    {
        foreach (var id in new[] { "GameMode", "Recording", "MouseAccel", "RefreshRate", "Windowed", "PowerPlan", "Hags", "Driver", "Ram", "Disk" })
        {
            Assert.True(Loc.Strings.ContainsKey($"PC.{id}.Title"), id);
            Assert.True(Loc.Strings.ContainsKey($"PC.{id}.Desc"), id);
        }
        foreach (var tier in Enum.GetNames<PerformanceTier>())
        {
            Assert.True(Loc.Strings.ContainsKey($"Tier.{tier}"), tier);
            Assert.True(Loc.Strings.ContainsKey($"Tier.{tier}.Desc"), tier);
        }
    }
}

[Collection("Settings")]
public class MigrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gb-migrate-" + Guid.NewGuid().ToString("N"));

    public MigrationTests() => AppPaths.OverrideDataDirectory(_dir);

    [Fact]
    public void Untouched_Valorant_Profile_Is_Removed_Edited_One_Is_Kept()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(AppPaths.SettingsFile, """
            { "SchemaVersion": 2, "ActiveProfileId": "v",
              "Profiles": [
                { "Id": "d", "Name": "Default" },
                { "Id": "v", "Name": "Valorant", "TriggerExecutables": ["valorant-win64-shipping"] },
                { "Id": "f", "Name": "Fortnite", "TriggerExecutables": ["fortniteclient-win64-shipping"] } ] }
            """);
        var s = new SettingsService();
        s.Load();
        Assert.Equal(new[] { "Default", "Fortnite" }, s.Settings.Profiles.Select(p => p.Name));
        Assert.Equal("d", s.Settings.ActiveProfileId);
        Assert.Equal(AppSettings.CurrentSchemaVersion, s.Settings.SchemaVersion);
        Assert.NotNull(s.Settings.Hud);
        Assert.NotNull(s.Settings.Hotkeys.ToggleHud);

        File.WriteAllText(AppPaths.SettingsFile, """
            { "SchemaVersion": 2,
              "Profiles": [ { "Id": "v", "Name": "Valorant", "ProcessesToKill": ["discord"], "TriggerExecutables": ["valorant-win64-shipping"] } ] }
            """);
        var s2 = new SettingsService();
        s2.Load();
        Assert.Contains(s2.Settings.Profiles, p => p.Name == "Valorant");
    }

    [Fact]
    public void Defaults_Ship_Without_Valorant()
    {
        var s = AppSettings.CreateDefault();
        Assert.DoesNotContain(s.Profiles, p => p.Name.Contains("Valorant", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(s.Profiles, p => p.Name == "Fortnite");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* ignore */ }
    }
}
