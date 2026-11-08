using System.Diagnostics;
using System.Runtime.InteropServices;
using GameBoost.Infrastructure;
using GameBoost.Models;

namespace GameBoost.Services;

/// <summary>
/// Frees unused memory by trimming the working sets of background programs of the current user.
/// Programs keep running; Windows reloads pages on demand. Protected and excluded processes are skipped.
/// </summary>
public static class MemoryCleaner
{
    private const uint PROCESS_SET_QUOTA = 0x0100;

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    public sealed record Result(int Processes, double FreedMb, double AvailableMbAfter);

    public static Result Trim(IEnumerable<string>? excludeNames = null)
    {
        var exclude = new HashSet<string>((excludeNames ?? Array.Empty<string>()).Select(ProcessNames.Normalize), StringComparer.OrdinalIgnoreCase);
        var before = Win32.GetMemoryStatus().ullAvailPhys;
        var trimmed = 0;

        Process[] processes;
        try { processes = Process.GetProcesses(); }
        catch (Exception ex)
        {
            Logger.Warn("Process list unavailable for memory cleanup", ex);
            return new Result(0, 0, before / 1024d / 1024d);
        }

        foreach (var p in processes)
        {
            using (p)
            {
                try
                {
                    if (exclude.Contains(ProcessNames.Normalize(p.ProcessName))) continue;
                    if (!ProcessGuard.Check(p).Allowed) continue;
                    var h = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_SET_QUOTA, false, p.Id);
                    if (h == IntPtr.Zero) continue;
                    try
                    {
                        if (EmptyWorkingSet(h)) trimmed++;
                    }
                    finally
                    {
                        NativeMethods.CloseHandle(h);
                    }
                }
                catch
                {
                    // process exited or access denied – skip
                }
            }
        }

        Thread.Sleep(300);
        var after = Win32.GetMemoryStatus().ullAvailPhys;
        var freed = after > before ? (after - before) / 1024d / 1024d : 0;
        Logger.Info($"Memory cleanup: {trimmed} processes trimmed, {freed:0} MB freed");
        return new Result(trimmed, freed, after / 1024d / 1024d);
    }
}
