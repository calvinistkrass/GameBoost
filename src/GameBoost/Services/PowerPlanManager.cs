using System.Diagnostics;
using System.Text.RegularExpressions;
using GameBoost.Infrastructure;

namespace GameBoost.Services;

/// <summary>Controls Windows power plans via powercfg.exe (no admin rights needed).</summary>
public static partial class PowerPlanManager
{
    public const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    public const string UltimatePerformanceGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";

    /// <summary>"Best performance" overlay (power mode slider on systems without classic plans).</summary>
    public static readonly Guid BestPerformanceOverlay = new("ded574b5-45a0-4f42-8737-46345c09c238");

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidRegex();

    public static string? ParseFirstGuid(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var m = GuidRegex().Match(text);
        return m.Success ? m.Value.ToLowerInvariant() : null;
    }

    public static List<string> ParseAllGuids(string? text)
    {
        if (string.IsNullOrEmpty(text)) return new List<string>();
        return GuidRegex().Matches(text).Select(m => m.Value.ToLowerInvariant()).Distinct().ToList();
    }

    public static string? GetActiveScheme()
    {
        var (code, output) = Run("/getactivescheme");
        return code == 0 ? ParseFirstGuid(output) : null;
    }

    public static List<string> ListSchemes()
    {
        var (code, output) = Run("/list");
        return code == 0 ? ParseAllGuids(output) : new List<string>();
    }

    public static bool SetActive(string guid)
    {
        var (code, output) = Run($"/setactive {guid}");
        if (code != 0) Logger.Warn($"powercfg /setactive {guid} failed ({code}): {output.Trim()}");
        return code == 0;
    }

    /// <summary>
    /// Returns the GUID of a High performance plan. If none exists, one is duplicated from the Windows template.
    /// </summary>
    public static string? EnsureHighPerformanceScheme(string? cachedGuid, out string? createdGuid)
    {
        createdGuid = null;
        var schemes = ListSchemes();

        if (schemes.Contains(HighPerformanceGuid)) return HighPerformanceGuid;
        if (schemes.Contains(UltimatePerformanceGuid)) return UltimatePerformanceGuid;
        if (cachedGuid != null && schemes.Contains(cachedGuid.ToLowerInvariant())) return cachedGuid.ToLowerInvariant();

        var (code, output) = Run($"/duplicatescheme {HighPerformanceGuid}");
        if (code == 0)
        {
            createdGuid = ParseFirstGuid(output);
            if (createdGuid != null)
            {
                Logger.Info($"Created High performance plan: {createdGuid}");
                return createdGuid;
            }
        }

        Logger.Warn($"No High performance plan available (powercfg: {output.Trim()})");
        return null;
    }

    /// <summary>Sets the power mode slider to "Best performance" (fallback for Modern Standby devices).</summary>
    public static bool TrySetBestPerformanceOverlay(out Guid previous)
    {
        previous = Guid.Empty;
        try
        {
            if (NativeMethods.PowerGetEffectiveOverlayScheme(out previous) != 0) previous = Guid.Empty;
            var result = NativeMethods.PowerSetActiveOverlayScheme(BestPerformanceOverlay);
            return result == 0;
        }
        catch (Exception ex)
        {
            Logger.Warn("Power mode overlay not supported", ex);
            return false;
        }
    }

    public static void RestoreOverlay(Guid previous)
    {
        try
        {
            NativeMethods.PowerSetActiveOverlayScheme(previous);
        }
        catch (Exception ex)
        {
            Logger.Warn("Could not restore power mode overlay", ex);
        }
    }

    private static (int ExitCode, string Output) Run(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "powercfg.exe"),
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(psi);
            if (process == null) return (-1, "Could not start process");

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(10000))
            {
                try { process.Kill(); } catch { /* ignore */ }
                return (-1, "Timeout");
            }
            return (process.ExitCode, stdout.Result + stderr.Result);
        }
        catch (Exception ex)
        {
            Logger.Error($"powercfg {arguments} failed", ex);
            return (-1, ex.Message);
        }
    }
}
