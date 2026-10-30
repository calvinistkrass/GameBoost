using System.Diagnostics;
using GameBoost.Infrastructure;
using GameBoost.Models;

namespace GameBoost.Services;

/// <summary>Aggregated entry for a running process (all instances with the same name).</summary>
public sealed record ProcessSnapshot(string Name, string DisplayName, string Path, int Count, long MemoryBytes);

/// <summary>Reads running processes.</summary>
public static class ProcessService
{
    /// <summary>All running processes that <see cref="ProcessGuard"/> allows to be closed.</summary>
    public static List<ProcessSnapshot> GetKillableProcesses()
    {
        var groups = new Dictionary<string, (string Display, string Path, int Count, long Memory)>(StringComparer.OrdinalIgnoreCase);

        foreach (var process in SafeGetProcesses())
        {
            using (process)
            {
                try
                {
                    var check = ProcessGuard.Check(process);
                    if (!check.Allowed || check.Path == null) continue;

                    var key = ProcessNames.Normalize(process.ProcessName);
                    long memory = 0;
                    try { memory = process.WorkingSet64; } catch { /* ignore */ }

                    if (groups.TryGetValue(key, out var g))
                        groups[key] = (g.Display, g.Path, g.Count + 1, g.Memory + memory);
                    else
                        groups[key] = (process.ProcessName, check.Path, 1, memory);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Could not read process: {ex.Message}");
                }
            }
        }

        return groups
            .Select(kv => new ProcessSnapshot(kv.Key, kv.Value.Display, kv.Value.Path, kv.Value.Count, kv.Value.Memory))
            .OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Normalized names of all running processes (for game detection).</summary>
    public static HashSet<string> GetRunningNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in SafeGetProcesses())
        {
            using (process)
            {
                try { names.Add(ProcessNames.Normalize(process.ProcessName)); } catch { /* exited */ }
            }
        }
        return names;
    }

    public static bool IsRunning(string normalizedName)
    {
        var processes = Array.Empty<Process>();
        try
        {
            processes = Process.GetProcessesByName(normalizedName);
            return processes.Length > 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            foreach (var p in processes) p.Dispose();
        }
    }

    private static Process[] SafeGetProcesses()
    {
        try
        {
            return Process.GetProcesses();
        }
        catch (Exception ex)
        {
            Logger.Error("Could not read process list", ex);
            return Array.Empty<Process>();
        }
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / 1024d / 1024 / 1024:0.0} GB";
        return $"{bytes / 1024d / 1024:0} MB";
    }
}
