using System.Runtime.InteropServices;
using GameBoost.Infrastructure;

namespace GameBoost.Services.Performance;

/// <summary>
/// Reads Windows performance counters (the same data Task Manager shows) via PDH.
/// Wildcard counters are summed or maxed across their instances. No admin rights required.
/// </summary>
internal sealed class PdhCounters : IDisposable
{
    private const uint PDH_FMT_DOUBLE = 0x00000200;
    private const uint PDH_FMT_NOCAP100 = 0x00008000;
    private const uint PDH_MORE_DATA = 0x800007D2;
    private const uint PDH_CSTATUS_VALID_DATA = 0x0;
    private const uint PDH_CSTATUS_NEW_DATA = 0x1;

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounter(IntPtr query, string fullCounterPath, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr itemBuffer);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);

    private IntPtr _query;
    private readonly Dictionary<string, IntPtr> _counters = new();

    public PdhCounters(params string[] paths)
    {
        if (PdhOpenQuery(null, IntPtr.Zero, out _query) != 0)
        {
            _query = IntPtr.Zero;
            Logger.Warn("PDH query could not be opened");
            return;
        }

        foreach (var path in paths)
        {
            var status = PdhAddEnglishCounter(_query, path, IntPtr.Zero, out var counter);
            if (status == 0) _counters[path] = counter;
            else Logger.Info($"Performance counter not available: {path} (0x{status:X8})");
        }
        PdhCollectQueryData(_query); // first sample primes rate counters
    }

    public bool IsAvailable(string path) => _counters.ContainsKey(path);

    public void Collect()
    {
        if (_query != IntPtr.Zero) PdhCollectQueryData(_query);
    }

    /// <summary>Returns all instance values of a counter (empty if not available).</summary>
    public List<double> Values(string path)
    {
        var result = new List<double>();
        if (!_counters.TryGetValue(path, out var counter)) return result;

        uint size = 0;
        var status = PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out _, IntPtr.Zero);
        if (status != PDH_MORE_DATA || size == 0) return result;

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            status = PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out var count, buffer);
            if (status != 0) return result;

            // PDH_FMT_COUNTERVALUE_ITEM_W: { LPWSTR name; { DWORD CStatus; (pad); double value } }
            var itemSize = IntPtr.Size + 16;
            for (var i = 0; i < count; i++)
            {
                var item = buffer + i * itemSize;
                var cstatus = (uint)Marshal.ReadInt32(item + IntPtr.Size);
                if (cstatus != PDH_CSTATUS_VALID_DATA && cstatus != PDH_CSTATUS_NEW_DATA) continue;
                var value = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item + IntPtr.Size + 8));
                if (!double.IsNaN(value) && !double.IsInfinity(value)) result.Add(value);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        return result;
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero) PdhCloseQuery(_query);
        _query = IntPtr.Zero;
        _counters.Clear();
    }
}
