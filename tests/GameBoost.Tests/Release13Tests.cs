using System.Buffers.Binary;
using System.Text;
using GameBoost.Models;
using GameBoost.Services;

namespace GameBoost.Tests;

public class UpdateServiceTests
{
    [Theory]
    [InlineData("v1.3.0", "1.2.0", true)]
    [InlineData("1.2.1", "1.2.0", true)]
    [InlineData("v1.10.0", "1.9.9", true)]
    [InlineData("v1.2.0", "1.2.0", false)]
    [InlineData("1.1.9", "1.2.0", false)]
    [InlineData("garbage", "1.2.0", false)]
    public void IsNewer_ComparesVersions(string candidate, string current, bool expected)
        => Assert.Equal(expected, UpdateService.IsNewer(candidate, current));
}

public class GameLibraryTests
{
    [Fact]
    public void ParseSteamLibraries_ReadsAllPaths()
    {
        const string vdf = "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"D:\\\\SteamLibrary\"\n\t}\n}";
        var libs = GameLibrary.ParseSteamLibraries(vdf);
        Assert.Equal(new[] { @"C:\Program Files (x86)\Steam", @"D:\SteamLibrary" }, libs);
    }

    [Fact]
    public void ParseSteamManifest_ReadsIdNameAndDir()
    {
        const string acf = "\"AppState\"\n{\n\t\"appid\"\t\t\"730\"\n\t\"name\"\t\t\"Counter-Strike 2\"\n\t\"installdir\"\t\t\"Counter-Strike Global Offensive\"\n}";
        var info = GameLibrary.ParseSteamManifest(acf);
        Assert.NotNull(info);
        Assert.Equal("730", info!.Value.AppId);
        Assert.Equal("Counter-Strike 2", info.Value.Name);
        Assert.Equal("Counter-Strike Global Offensive", info.Value.InstallDir);
    }

    [Fact]
    public void ParseEpicManifest_BuildsLaunchUri()
    {
        const string json = """
            {"DisplayName":"Fortnite","InstallLocation":"C:\\Games\\Fortnite","LaunchExecutable":"FortniteGame/Binaries/Win64/FortniteLauncher.exe",
             "CatalogNamespace":"fn","CatalogItemId":"4fe75bbc","AppName":"Fortnite","bIsIncompleteInstall":false}
            """;
        var game = GameLibrary.ParseEpicManifest(json);
        Assert.NotNull(game);
        Assert.Equal("Fortnite", game!.Name);
        Assert.Equal("com.epicgames.launcher://apps/fn%3A4fe75bbc%3AFortnite?action=launch&silent=true", game.LaunchTarget);
    }

    [Fact]
    public void ParseEpicManifest_SkipsIncompleteInstalls()
    {
        const string json = """{"DisplayName":"X","InstallLocation":"C:\\X","LaunchExecutable":"x.exe","bIsIncompleteInstall":true}""";
        Assert.Null(GameLibrary.ParseEpicManifest(json));
    }

    [Fact]
    public void GuessExe_PrefersLargestNonToolExe()
    {
        var dir = Directory.CreateTempSubdirectory("gb-lib-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "unins000.exe"), new byte[5000]);
            File.WriteAllBytes(Path.Combine(dir, "Game.exe"), new byte[3000]);
            File.WriteAllBytes(Path.Combine(dir, "CrashReporter.exe"), new byte[4000]);
            Assert.Equal(Path.Combine(dir, "Game.exe"), GameLibrary.GuessExe(dir));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

public class SessionStatsTests
{
    [Fact]
    public void Stats_ReturnsAverageLowAndMax()
    {
        var values = Enumerable.Range(1, 200).Select(i => (double)i).ToList();
        var (avg, low, max) = SessionRecord.Stats(values);
        Assert.Equal(100.5, avg);
        Assert.Equal(3, low); // 1 % of 200 = index 2
        Assert.Equal(200, max);
    }

    [Fact]
    public void Stats_EmptyIsNull()
    {
        var (avg, low, max) = SessionRecord.Stats(Array.Empty<double>());
        Assert.Null(avg);
        Assert.Null(low);
        Assert.Null(max);
    }
}

public class DiscordFrameTests
{
    [Fact]
    public void Frame_HasOpLengthAndUtf8Body()
    {
        var json = "{\"v\":1,\"x\":\"ä\"}";
        var frame = DiscordPresenceService.Frame(1, json);
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(0, 4)));
        var len = BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(4, 4));
        Assert.Equal(Encoding.UTF8.GetByteCount(json), len);
        Assert.Equal(json, Encoding.UTF8.GetString(frame, 8, len));
    }
}

public class ProfileTransferTests
{
    [Fact]
    public void ExportImport_RoundTripsAndRenamesDuplicates()
    {
        var file = Path.Combine(Path.GetTempPath(), $"gb-{Guid.NewGuid():N}.gbprofile");
        try
        {
            var p = new GameModeProfile { Name = "Fortnite", SwitchPowerPlan = true, FreeRamOnActivate = true };
            p.ProcessesToKill.Add("discord");
            p.TriggerExecutables.Add("fortniteclient-win64-shipping");
            ProfileTransfer.Export(p, file);

            var imported = ProfileTransfer.Import(file, new[] { "Fortnite" });
            Assert.NotNull(imported);
            Assert.Equal("Fortnite (2)", imported!.Name);
            Assert.NotEqual(p.Id, imported.Id);
            Assert.True(imported.SwitchPowerPlan);
            Assert.True(imported.FreeRamOnActivate);
            Assert.Contains("discord", imported.ProcessesToKill);
            Assert.Contains("fortniteclient-win64-shipping", imported.TriggerExecutables);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Import_RemovesProtectedProcesses()
    {
        var file = Path.Combine(Path.GetTempPath(), $"gb-{Guid.NewGuid():N}.gbprofile");
        try
        {
            File.WriteAllText(file, """{"Format":"GameBoostProfile","Version":1,"Name":"Evil","ProcessesToKill":["explorer","csrss","spotify"]}""");
            var imported = ProfileTransfer.Import(file, Array.Empty<string>());
            Assert.NotNull(imported);
            Assert.DoesNotContain("csrss", imported!.ProcessesToKill);
            Assert.Contains("spotify", imported.ProcessesToKill);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Import_RejectsOtherJson()
    {
        var file = Path.Combine(Path.GetTempPath(), $"gb-{Guid.NewGuid():N}.gbprofile");
        try
        {
            File.WriteAllText(file, """{"hello":"world"}""");
            Assert.Null(ProfileTransfer.Import(file, Array.Empty<string>()));
        }
        finally
        {
            File.Delete(file);
        }
    }
}

public class TempCleanerTests
{
    [Fact]
    public void Clean_DeletesOnlyOldFiles()
    {
        var dir = Directory.CreateTempSubdirectory("gb-clean-").FullName;
        try
        {
            var oldFile = Path.Combine(dir, "sub", "old.tmp");
            Directory.CreateDirectory(Path.GetDirectoryName(oldFile)!);
            File.WriteAllBytes(oldFile, new byte[1024]);
            File.SetLastWriteTime(oldFile, DateTime.Now.AddDays(-3));
            var newFile = Path.Combine(dir, "new.tmp");
            File.WriteAllBytes(newFile, new byte[512]);

            var category = new CleanupCategory("Temp", new[] { dir }, TimeSpan.FromHours(24), true);
            TempCleaner.Scan(category);
            Assert.Equal(1, category.FileCount);
            Assert.Equal(1024, category.SizeBytes);

            var (files, bytes) = TempCleaner.Clean(category);
            Assert.Equal(1, files);
            Assert.Equal(1024, bytes);
            Assert.False(File.Exists(oldFile));
            Assert.True(File.Exists(newFile));
            Assert.True(Directory.Exists(dir));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData(500, "500 B")]
    [InlineData(2048, "2 KB")]
    [InlineData(5L * 1024 * 1024, "5 MB")]
    public void FormatSize_IsReadable(long bytes, string expected)
        => Assert.Equal(expected, TempCleaner.FormatSize(bytes).Replace(',', '.'));
}

public class SettingsDefaults13Tests
{
    [Fact]
    public void Normalize_FixesThemeAccentAndLists()
    {
        var s = new AppSettings { Theme = "Neon", AccentColor = "nope", Discord = null!, ManualGames = null! };
        s.Normalize();
        Assert.Equal("Dark", s.Theme);
        Assert.Matches("^#[0-9A-F]{6}$", s.AccentColor.ToUpperInvariant());
        Assert.NotNull(s.Discord);
        Assert.NotNull(s.ManualGames);
    }

    [Fact]
    public void HudTempLimit_IsClamped()
    {
        var h = new HudSettings { TempWarningLimit = 150 };
        Assert.Equal(100, h.TempWarningLimit);
        h.TempWarningLimit = 10;
        Assert.Equal(60, h.TempWarningLimit);
    }
}

public class ThemeTransformTests
{
    private static string Source()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "src", "GameBoost", "Themes", "Theme.xaml"))) dir = Path.GetDirectoryName(dir);
        return File.ReadAllText(Path.Combine(dir!, "src", "GameBoost", "Themes", "Theme.xaml"));
    }

    [Fact]
    public void Transform_SetsLightPaletteAndAccent()
    {
        var x = ThemeService.Transform(Source(), true, System.Windows.Media.Color.FromRgb(0x4F, 0x8E, 0xF7));
        Assert.Contains("<Color x:Key=\"C.Bg0\">#FFF3F4F6</Color>", x);
        Assert.Contains("<Color x:Key=\"C.Accent\">#FF4F8EF7</Color>", x);
        Assert.Contains("<Color x:Key=\"C.OnAccent\">#FFFFFFFF</Color>", x);
        Assert.Contains("clr-namespace:GameBoost.Infrastructure;assembly=GameBoost\"", x);
        Assert.DoesNotContain("#1A2ED497", x);
    }

    [Fact]
    public void Transform_DarkKeepsBackgrounds()
    {
        var source = Source();
        var x = ThemeService.Transform(source, false, System.Windows.Media.Color.FromRgb(0xF2, 0x54, 0x5B));
        Assert.Contains("<Color x:Key=\"C.Bg0\">#FF0E1015</Color>", x);
        Assert.Contains("<Color x:Key=\"C.Accent\">#FFF2545B</Color>", x);
    }
}
