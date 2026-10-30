using System.Diagnostics;
using GameBoost.Infrastructure;

namespace GameBoost.Services.Performance;

/// <summary>One measurement of all system values. Null means "not available".</summary>
public sealed record PerformanceSnapshot(
    double? Fps,
    string? FpsProcess,
    bool FpsNeedsAdmin,
    double? CpuLoad,
    double? GpuLoad,
    double? GpuTemperature,
    double? VramUsedGb,
    double RamUsedGb,
    double RamTotalGb,
    DateTime Timestamp)
{
    public static PerformanceSnapshot Empty => new(null, null, !FpsCounter.IsElevated, null, null, null, null, 0, 0, DateTime.Now);

    public double? FrameTimeMs => Fps is > 0 ? 1000.0 / Fps : null;
}

/// <summary>
/// Measures FPS, CPU/GPU load, GPU temperature, VRAM and RAM once per second.
/// Only runs while something needs it (reference counted), so it costs nothing otherwise.
/// </summary>
public sealed class PerformanceMonitorService : IDisposable
{
    private const string CpuCounter = @"\Processor Information(_Total)\% Processor Utility";
    private const string CpuFallbackCounter = @"\Processor(_Total)\% Processor Time";
    private const string GpuCounter = @"\GPU Engine(*engtype_3D)\Utilization Percentage";
    private const string VramCounter = @"\GPU Adapter Memory(*)\Dedicated Usage";

    private readonly object _gate = new();
    private int _users;
    private bool _wantFps;
    private Timer? _timer;
    private PdhCounters? _pdh;
    private GpuTemperatureReader? _gpuTemp;
    private FpsCounter? _fps;
    private int _busy;

    public PerformanceSnapshot Latest { get; private set; } = PerformanceSnapshot.Empty;

    /// <summary>Raised on a background thread after every measurement.</summary>
    public event EventHandler<PerformanceSnapshot>? Updated;

    public bool IsElevated => FpsCounter.IsElevated;

    /// <summary>Starts measuring (if not already running). Every Acquire needs a matching Release.</summary>
    public void Acquire()
    {
        lock (_gate)
        {
            _users++;
            if (_timer != null) return;
            _timer = new Timer(Tick, null, 0, 1000);
            Logger.Info("Performance monitoring started");
        }
    }

    public void Release()
    {
        lock (_gate)
        {
            if (_users > 0) _users--;
            if (_users > 0 || _timer == null) return;
            StopInternal();
            Logger.Info("Performance monitoring stopped");
        }
    }

    /// <summary>FPS measurement (ETW) only runs while the in-game overlay actually shows FPS.</summary>
    public void SetFpsWanted(bool wanted)
    {
        lock (_gate)
        {
            _wantFps = wanted;
            if (!wanted) _fps?.Stop();
        }
    }

    private void Tick(object? _)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            PerformanceSnapshot snapshot;
            // Measured under the lock so Release() cannot dispose the readers mid-measurement.
            lock (_gate)
            {
                if (_timer == null) return;
                _pdh ??= new PdhCounters(CpuCounter, CpuFallbackCounter, GpuCounter, VramCounter);
                _gpuTemp ??= new GpuTemperatureReader();
                if (_wantFps && FpsCounter.IsElevated)
                {
                    _fps ??= new FpsCounter();
                    if (!_fps.IsRunning) _fps.Start();
                }
                snapshot = Measure(_pdh, _gpuTemp, _fps is { IsRunning: true } ? _fps : null);
            }

            Latest = snapshot;
            Updated?.Invoke(this, snapshot);
        }
        catch (Exception ex)
        {
            Logger.Error("Performance measurement failed", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private static PerformanceSnapshot Measure(PdhCounters pdh, GpuTemperatureReader gpuTemp, FpsCounter? fps)
    {
        pdh.Collect();

        var cpuValues = pdh.IsAvailable(CpuCounter) ? pdh.Values(CpuCounter) : pdh.Values(CpuFallbackCounter);
        double? cpu = cpuValues.Count > 0 ? Math.Clamp(cpuValues.Max(), 0, 100) : null;

        double? gpu = null;
        if (pdh.IsAvailable(GpuCounter)) gpu = Math.Clamp(pdh.Values(GpuCounter).Sum(), 0, 100);

        double? vram = null;
        if (pdh.IsAvailable(VramCounter))
        {
            var v = pdh.Values(VramCounter);
            if (v.Count > 0) vram = v.Max() / 1024d / 1024d / 1024d;
        }

        var mem = Win32.GetMemoryStatus();
        var ramTotal = mem.ullTotalPhys / 1024d / 1024d / 1024d;
        var ramUsed = (mem.ullTotalPhys - mem.ullAvailPhys) / 1024d / 1024d / 1024d;

        double? fpsValue = null;
        string? fpsProcess = null;
        if (fps != null) (fpsValue, fpsProcess) = PickGameFps(fps.Sample());

        return new PerformanceSnapshot(fpsValue, fpsProcess, !FpsCounter.IsElevated, cpu, gpu, gpuTemp.Read(),
            vram, ramUsed, ramTotal, DateTime.Now);
    }

    /// <summary>Uses the foreground window's process if it renders frames, otherwise the busiest renderer.</summary>
    private static (double?, string?) PickGameFps(Dictionary<int, double> perProcess)
    {
        if (perProcess.Count == 0) return (null, null);

        var foregroundPid = 0;
        var fg = Win32.GetForegroundWindow();
        if (fg != IntPtr.Zero)
        {
            Win32.GetWindowThreadProcessId(fg, out var pid);
            foregroundPid = (int)pid;
        }

        int chosen;
        if (foregroundPid != 0 && perProcess.ContainsKey(foregroundPid)) chosen = foregroundPid;
        else chosen = perProcess.OrderByDescending(kv => kv.Value).First().Key;

        string? name = null;
        try
        {
            using var p = Process.GetProcessById(chosen);
            name = p.ProcessName;
        }
        catch
        {
            // process already exited
        }
        return (perProcess[chosen], name);
    }

    private void StopInternal()
    {
        _timer?.Dispose();
        _timer = null;
        _fps?.Dispose();
        _fps = null;
        _pdh?.Dispose();
        _pdh = null;
        _gpuTemp?.Dispose();
        _gpuTemp = null;
        Latest = PerformanceSnapshot.Empty;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _users = 0;
            StopInternal();
        }
    }
}
