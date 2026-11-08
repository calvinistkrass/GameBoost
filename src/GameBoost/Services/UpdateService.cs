using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using GameBoost.Infrastructure;

namespace GameBoost.Services;

/// <summary>A newer GameBoost release found on GitHub.</summary>
public sealed record UpdateInfo(string Version, string ReleaseUrl, string? SetupUrl, string? SetupName, long SetupSize);

/// <summary>Checks GitHub for new releases and installs them with the setup.</summary>
public sealed class UpdateService
{
    public const string Repository = "calvinistkrass/GameBoost";

    private static readonly HttpClient Http = CreateClient();

    public UpdateInfo? Available { get; private set; }
    public DateTime? LastCheck { get; private set; }

    /// <summary>Raised on the UI thread when <see cref="Available"/> changes.</summary>
    public event EventHandler? AvailableChanged;

    /// <summary>true if GameBoost was installed with the setup (an uninstaller sits next to the exe).</summary>
    public static bool IsInstalled => File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GameBoost", AppPaths.Version));
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>Compares two versions like "v1.2.0" and "1.10.1".</summary>
    public static bool IsNewer(string candidate, string current)
    {
        static Version Parse(string v)
        {
            var clean = v.Trim().TrimStart('v', 'V');
            var plus = clean.IndexOfAny(new[] { '-', '+' });
            if (plus > 0) clean = clean[..plus];
            return Version.TryParse(clean, out var parsed) ? parsed : new Version(0, 0);
        }
        return Parse(candidate) > Parse(current);
    }

    public async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            using var response = await Http.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest");
            if (!response.IsSuccessStatusCode)
            {
                Logger.Info($"Update check: GitHub answered {(int)response.StatusCode}");
                return Available;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            var url = root.GetProperty("html_url").GetString() ?? $"https://github.com/{Repository}/releases";
            string? setupUrl = null, setupName = null;
            long setupSize = 0;
            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (!name.StartsWith("GameBoost-Setup", StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                setupName = name;
                setupUrl = asset.GetProperty("browser_download_url").GetString();
                setupSize = asset.GetProperty("size").GetInt64();
            }

            LastCheck = DateTime.Now;
            var newer = IsNewer(tag, AppPaths.Version);
            Logger.Info($"Update check: latest {tag}, running {AppPaths.Version}, newer: {newer}");
            Set(newer ? new UpdateInfo(tag.TrimStart('v', 'V'), url, setupUrl, setupName, setupSize) : null);
            return Available;
        }
        catch (Exception ex)
        {
            Logger.Warn("Update check failed", ex);
            return Available;
        }
    }

    /// <summary>Downloads the setup and starts it silently. Returns false if it could not be started.</summary>
    public async Task<bool> DownloadAndInstallAsync(IProgress<double>? progress)
    {
        var info = Available;
        if (info?.SetupUrl == null || info.SetupName == null) return false;

        var dir = Path.Combine(Path.GetTempPath(), "GameBoost-Update");
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, info.SetupName);

        using (var response = await Http.GetAsync(info.SetupUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? info.SetupSize;
            await using var input = await response.Content.ReadAsStreamAsync();
            await using var output = File.Create(target);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await input.ReadAsync(buffer)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read));
                done += read;
                if (total > 0) progress?.Report((double)done / total);
            }
        }

        if (info.SetupSize > 0 && new FileInfo(target).Length != info.SetupSize)
        {
            Logger.Warn("Downloaded setup has the wrong size – update aborted");
            return false;
        }

        // Per-user installs live in %LocalAppData%; everything else was installed for all users.
        var perUser = AppContext.BaseDirectory.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), StringComparison.OrdinalIgnoreCase);
        var args = $"/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /UPDATE=1 {(perUser ? "/CURRENTUSER" : "/ALLUSERS")}";
        Logger.Info($"Starting update {info.Version}: {target} {args}");
        Process.Start(new ProcessStartInfo(target, args) { UseShellExecute = true })?.Dispose();
        return true;
    }

    public static void OpenReleasePage(string? url = null)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url ?? $"https://github.com/{Repository}/releases/latest") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Warn("Release page could not be opened", ex);
        }
    }

    private void Set(UpdateInfo? info)
    {
        Available = info;
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => AvailableChanged?.Invoke(this, EventArgs.Empty));
    }
}
