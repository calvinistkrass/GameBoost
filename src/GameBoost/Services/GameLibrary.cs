using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using GameBoost.Infrastructure;
using GameBoost.Models;
using Microsoft.Win32;

namespace GameBoost.Services;

/// <summary>An installed game found by the library scan.</summary>
public sealed record GameEntry(
    string Name,
    string Source,
    string? InstallDir,
    string? ExePath,
    string? LaunchTarget,
    string? LaunchArgs)
{
    /// <summary>Normalized process name used for game mode automation ("fortniteclient-win64-shipping").</summary>
    public string? ProcessName => ExePath == null ? null : ProcessNames.Normalize(ExePath);
}

/// <summary>Finds installed games from Steam, Epic Games, Riot and games added by hand.</summary>
public static partial class GameLibrary
{
    private static readonly Regex ExcludedExe = new(
        @"unins|setup|install|redist|vcredist|vc_|dxsetup|directx|crash|report|helper|prereq|easyanticheat|battleye|^be(service|launcher)|update|patcher|cleanup|benchmark|dotnet|uplay|ubisoftconnect|launcher(?!.*game)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<GameEntry> Scan(IEnumerable<ManualGame> manual)
    {
        var games = new List<GameEntry>();
        Try(() => games.AddRange(Steam()), "Steam");
        Try(() => games.AddRange(Epic()), "Epic");
        Try(() => games.AddRange(Riot()), "Riot");
        foreach (var m in manual)
        {
            if (string.IsNullOrWhiteSpace(m.ExePath)) continue;
            games.Add(new GameEntry(string.IsNullOrWhiteSpace(m.Name) ? Path.GetFileNameWithoutExtension(m.ExePath) : m.Name,
                "Manual", Path.GetDirectoryName(m.ExePath), m.ExePath, m.ExePath, null));
        }

        return games
            .GroupBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static void Launch(GameEntry game)
    {
        if (string.IsNullOrWhiteSpace(game.LaunchTarget)) return;
        var psi = new ProcessStartInfo(game.LaunchTarget) { UseShellExecute = true, Arguments = game.LaunchArgs ?? "" };
        if (File.Exists(game.LaunchTarget)) psi.WorkingDirectory = Path.GetDirectoryName(game.LaunchTarget) ?? "";
        Process.Start(psi)?.Dispose();
        Logger.Info($"Game launched: {game.Name} ({game.Source})");
    }

    // ------------------------------------------------------------------ Steam

    [GeneratedRegex("\"path\"\\s+\"(?<p>[^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex VdfPath();

    [GeneratedRegex("\"(?<k>appid|name|installdir)\"\\s+\"(?<v>[^\"]*)\"", RegexOptions.IgnoreCase)]
    private static partial Regex AcfField();

    internal static List<string> ParseSteamLibraries(string vdf) =>
        VdfPath().Matches(vdf).Select(m => m.Groups["p"].Value.Replace(@"\\", @"\")).ToList();

    internal static (string AppId, string Name, string InstallDir)? ParseSteamManifest(string acf)
    {
        string? id = null, name = null, dir = null;
        foreach (Match m in AcfField().Matches(acf))
        {
            var v = m.Groups["v"].Value;
            switch (m.Groups["k"].Value.ToLowerInvariant())
            {
                case "appid": id ??= v; break;
                case "name": name ??= v; break;
                case "installdir": dir ??= v; break;
            }
        }
        return id != null && name != null && dir != null ? (id, name, dir) : null;
    }

    private static IEnumerable<GameEntry> Steam()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        var steamPath = (key?.GetValue("SteamPath") as string)?.Replace('/', '\\');
        if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath)) yield break;

        var libraries = new List<string> { steamPath };
        var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf)) libraries.AddRange(ParseSteamLibraries(File.ReadAllText(vdf)));

        foreach (var lib in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var apps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (var acf in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                (string AppId, string Name, string InstallDir)? info;
                try { info = ParseSteamManifest(File.ReadAllText(acf)); }
                catch { continue; }
                if (info is not { } a) continue;
                if (Regex.IsMatch(a.Name, "Redistributable|Steamworks|Proton|Steam Linux Runtime|SteamVR", RegexOptions.IgnoreCase)) continue;
                var dir = Path.Combine(apps, "common", a.InstallDir);
                if (!Directory.Exists(dir)) continue;
                yield return new GameEntry(a.Name, "Steam", dir, GuessExe(dir), $"steam://rungameid/{a.AppId}", null);
            }
        }
    }

    // ------------------------------------------------------------------ Epic Games

    internal static GameEntry? ParseEpicManifest(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        string S(string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        var display = S("DisplayName");
        var location = S("InstallLocation");
        var exe = S("LaunchExecutable");
        if (display.Length == 0 || location.Length == 0 || exe.Length == 0) return null;
        if (r.TryGetProperty("bIsIncompleteInstall", out var inc) && inc.ValueKind == JsonValueKind.True) return null;

        var uri = $"com.epicgames.launcher://apps/{Uri.EscapeDataString(S("CatalogNamespace"))}%3A{Uri.EscapeDataString(S("CatalogItemId"))}%3A{Uri.EscapeDataString(S("AppName"))}?action=launch&silent=true";
        return new GameEntry(display, "Epic", location, Path.Combine(location, exe), uri, null);
    }

    private static IEnumerable<GameEntry> Epic()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(dir)) yield break;
        foreach (var item in Directory.EnumerateFiles(dir, "*.item"))
        {
            GameEntry? game = null;
            try { game = ParseEpicManifest(File.ReadAllText(item)); }
            catch { /* broken manifest */ }
            if (game == null) continue;
            // Fortnite's launcher exe starts the real game process – use that for automation.
            if (game.ExePath?.EndsWith("FortniteLauncher.exe", StringComparison.OrdinalIgnoreCase) == true)
                game = game with { ExePath = Path.Combine(Path.GetDirectoryName(game.ExePath)!, "FortniteClient-Win64-Shipping.exe") };
            yield return game;
        }
    }

    // ------------------------------------------------------------------ Riot Games

    private static IEnumerable<GameEntry> Riot()
    {
        using var uninstall = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
        if (uninstall == null) yield break;
        foreach (var name in uninstall.GetSubKeyNames().Where(n => n.StartsWith("Riot Game", StringComparison.OrdinalIgnoreCase)))
        {
            using var k = uninstall.OpenSubKey(name);
            var display = k?.GetValue("DisplayName") as string;
            var location = k?.GetValue("InstallLocation") as string;
            var uninstallString = k?.GetValue("UninstallString") as string ?? "";
            var m = Regex.Match(uninstallString, "\"(?<exe>[^\"]*RiotClientServices\\.exe)\".*--uninstall-product=(?<prod>[\\w-]+)(?:.*--uninstall-patchline=(?<line>[\\w-]+))?", RegexOptions.IgnoreCase);
            if (display == null || !m.Success) continue;
            var line = m.Groups["line"].Success ? m.Groups["line"].Value : "live";
            var exe = location != null && Directory.Exists(location) ? GuessExe(location) : null;
            if (m.Groups["prod"].Value.Equals("valorant", StringComparison.OrdinalIgnoreCase) && location != null)
                exe = Path.Combine(location, "ShooterGame", "Binaries", "Win64", "VALORANT-Win64-Shipping.exe");
            yield return new GameEntry(display, "Riot", location, exe, m.Groups["exe"].Value,
                $"--launch-product={m.Groups["prod"].Value} --launch-patchline={line}");
        }
    }

    // ------------------------------------------------------------------ Helpers

    /// <summary>Best guess for the game's main exe: the largest exe in the first levels that isn't a tool or installer.</summary>
    internal static string? GuessExe(string dir)
    {
        try
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 4, IgnoreInaccessible = true };
            return new DirectoryInfo(dir).EnumerateFiles("*.exe", options)
                .Where(f => !ExcludedExe.IsMatch(f.Name))
                .OrderByDescending(f => f.Length)
                .FirstOrDefault()?.FullName;
        }
        catch
        {
            return null;
        }
    }

    private static void Try(Action action, string source)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Logger.Warn($"Game library: {source} could not be read", ex);
        }
    }
}
