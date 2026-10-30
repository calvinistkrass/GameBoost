using System.Runtime.InteropServices;
using GameBoost.Infrastructure;

namespace GameBoost.Services.Performance;

/// <summary>
/// Reads the GPU temperature through the Windows display driver interface (D3DKMT, WDDM 2.7+),
/// the same source Task Manager uses. Works for NVIDIA, AMD and Intel without admin rights or extra drivers.
/// </summary>
internal sealed class GpuTemperatureReader : IDisposable
{
    private const int KMTQAITYPE_ADAPTERPERFDATA = 62;

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_ADAPTERINFO
    {
        public uint hAdapter;
        public LUID AdapterLuid;
        public uint NumOfSources;
        public int bPrecisePresentRegionsPreferred;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_ENUMADAPTERS2
    {
        public uint NumAdapters;
        public IntPtr pAdapters;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_QUERYADAPTERINFO
    {
        public uint hAdapter;
        public int Type;
        public IntPtr pPrivateDriverData;
        public uint PrivateDriverDataSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_ADAPTER_PERFDATA
    {
        public uint PhysicalAdapterIndex;
        public ulong MemoryFrequency;
        public ulong MaxMemoryFrequency;
        public ulong MaxMemoryFrequencyOC;
        public ulong MemoryBandwidth;
        public ulong PCIEBandwidth;
        public uint FanRPM;
        public uint Power;
        public uint Temperature; // tenths of a degree Celsius
        public byte PowerStateOverride;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_CLOSEADAPTER
    {
        public uint hAdapter;
    }

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTEnumAdapters2(ref D3DKMT_ENUMADAPTERS2 data);

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTQueryAdapterInfo(ref D3DKMT_QUERYADAPTERINFO data);

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTCloseAdapter(ref D3DKMT_CLOSEADAPTER data);

    private readonly List<uint> _adapters = new();

    public GpuTemperatureReader()
    {
        try
        {
            var enumData = new D3DKMT_ENUMADAPTERS2();
            if (D3DKMTEnumAdapters2(ref enumData) != 0 || enumData.NumAdapters == 0) return;

            var itemSize = Marshal.SizeOf<D3DKMT_ADAPTERINFO>();
            var buffer = Marshal.AllocHGlobal(itemSize * (int)enumData.NumAdapters);
            try
            {
                enumData.pAdapters = buffer;
                if (D3DKMTEnumAdapters2(ref enumData) != 0) return;
                for (var i = 0; i < enumData.NumAdapters; i++)
                {
                    var info = Marshal.PtrToStructure<D3DKMT_ADAPTERINFO>(buffer + i * itemSize);
                    _adapters.Add(info.hAdapter);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("GPU adapters could not be enumerated", ex);
        }
    }

    /// <summary>Highest temperature reported by any GPU in °C, or null if not supported.</summary>
    public double? Read()
    {
        double? best = null;
        foreach (var adapter in _adapters)
        {
            try
            {
                var perf = new D3DKMT_ADAPTER_PERFDATA();
                var size = Marshal.SizeOf<D3DKMT_ADAPTER_PERFDATA>();
                var ptr = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(perf, ptr, false);
                    var query = new D3DKMT_QUERYADAPTERINFO
                    {
                        hAdapter = adapter,
                        Type = KMTQAITYPE_ADAPTERPERFDATA,
                        pPrivateDriverData = ptr,
                        PrivateDriverDataSize = (uint)size
                    };
                    if (D3DKMTQueryAdapterInfo(ref query) != 0) continue;
                    perf = Marshal.PtrToStructure<D3DKMT_ADAPTER_PERFDATA>(ptr);
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }

                var celsius = perf.Temperature / 10.0;
                if (celsius is > 1 and < 150 && (best == null || celsius > best)) best = celsius;
            }
            catch (Exception ex)
            {
                Logger.Warn("GPU temperature could not be read", ex);
            }
        }
        return best;
    }

    public void Dispose()
    {
        foreach (var adapter in _adapters)
        {
            try
            {
                var close = new D3DKMT_CLOSEADAPTER { hAdapter = adapter };
                D3DKMTCloseAdapter(ref close);
            }
            catch
            {
                // ignore
            }
        }
        _adapters.Clear();
    }
}
