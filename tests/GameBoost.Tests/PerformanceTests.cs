using GameBoost.Services.Performance;
using Xunit.Abstractions;

namespace GameBoost.Tests;

public class PerformanceTests
{
    private readonly ITestOutputHelper _output;

    public PerformanceTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Monitor_Produces_Real_Values()
    {
        using var monitor = new PerformanceMonitorService();
        PerformanceSnapshot? last = null;
        var count = 0;
        monitor.Updated += (_, s) => { last = s; Interlocked.Increment(ref count); };
        monitor.Acquire();
        var deadline = DateTime.UtcNow.AddSeconds(6);
        while (Volatile.Read(ref count) < 3 && DateTime.UtcNow < deadline) Thread.Sleep(100);
        monitor.Release();

        Assert.NotNull(last);
        _output.WriteLine($"CPU {last!.CpuLoad:0.0}%  GPU {last.GpuLoad:0.0}%  GPU temp {last.GpuTemperature:0.0}°C  VRAM {last.VramUsedGb:0.00} GB  RAM {last.RamUsedGb:0.0}/{last.RamTotalGb:0.0} GB  FPS needs admin: {last.FpsNeedsAdmin}");
        Assert.True(last.RamTotalGb > 1);
        Assert.True(last.RamUsedGb > 0 && last.RamUsedGb <= last.RamTotalGb);
        Assert.NotNull(last.CpuLoad);
        Assert.InRange(last.CpuLoad!.Value, 0, 100);
    }

    [Fact]
    public void Release_Stops_Measuring()
    {
        using var monitor = new PerformanceMonitorService();
        monitor.Acquire();
        Thread.Sleep(300);
        monitor.Release();
        var count = 0;
        monitor.Updated += (_, _) => Interlocked.Increment(ref count);
        Thread.Sleep(1500);
        Assert.Equal(0, count);
    }
}
