using System.Diagnostics;
using GameBoost.Infrastructure;
using GameBoost.Models;

namespace GameBoost.Services;

/// <summary>Zusammengefasster Eintrag für einen laufenden Prozess (alle Instanzen gleichen Namens).</summary>
public sealed record ProcessSnapshot(string Name, string DisplayName, string Path, int Count, long MemoryBytes);

/// <summary>Liest laufende Prozesse aus.</summary>
public static class ProcessService
{
    /// <summary>Alle laufenden Prozesse, die laut <see cref="ProcessGuard"/> beendet werden dürfen.</summary>
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
                    try { memory = process.WorkingSet64; } catch { /* ignorieren */ }

                    if (groups.TryGetValue(key, out var g))
                        groups[key] = (g.Display, g.Path, g.Count + 1, g.Memory + memory);
                    else
                        groups[key] = (process.ProcessName, check.Path, 1, memory);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Prozess konnte nicht gelesen werden: {ex.Message}");
                }
            }
        }

        return groups
            .Select(kv => new ProcessSnapshot(kv.Key, kv.Value.Display, kv.Value.Path, kv.Value.Count, kv.Value.Memory))
            .OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Normalisierte Namen aller laufenden Prozesse (für die Spielerkennung).</summary>
    public static HashSet<string> GetRunningNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in SafeGetProcesses())
        {
            using (process)
            {
                try { names.Add(ProcessNames.Normalize(process.ProcessName)); } catch { /* beendet */ }
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
            Logger.Error("Prozessliste konnte nicht gelesen werden", ex);
            return Array.Empty<Process>();
        }
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / 1024d / 1024 / 1024:0.0} GB";
        return $"{bytes / 1024d / 1024:0} MB";
    }
}
