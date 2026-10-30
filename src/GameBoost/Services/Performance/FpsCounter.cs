using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Principal;
using GameBoost.Infrastructure;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace GameBoost.Services.Performance;

/// <summary>
/// Counts presented frames per process via ETW (DXGI and Direct3D 9 present events), like PresentMon.
/// Works for DirectX 9–12 games without touching the game. Windows only allows ETW sessions
/// for administrators, so FPS is only available when GameBoost runs elevated.
/// </summary>
internal sealed class FpsCounter : IDisposable
{
    private const string SessionName = "GameBoost-FPS";
    private static readonly Guid DxgiProvider = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private static readonly Guid D3D9Provider = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");

    private readonly ConcurrentDictionary<int, int> _presents = new();
    private readonly Stopwatch _window = Stopwatch.StartNew();
    private TraceEventSession? _session;
    private Thread? _thread;

    public static bool IsElevated
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    public bool IsRunning => _session != null;

    public bool Start()
    {
        if (_session != null) return true;
        if (!IsElevated) return false;

        try
        {
            var session = new TraceEventSession(SessionName) { StopOnDispose = true };
            session.EnableProvider(DxgiProvider, TraceEventLevel.Informational, ulong.MaxValue);
            session.EnableProvider(D3D9Provider, TraceEventLevel.Informational, ulong.MaxValue);
            session.Source.AllEvents += OnEvent;

            _thread = new Thread(() =>
            {
                try
                {
                    session.Source.Process();
                }
                catch (Exception ex)
                {
                    Logger.Warn("FPS event processing stopped", ex);
                }
            })
            { IsBackground = true, Name = "GameBoost FPS" };

            _session = session;
            _window.Restart();
            _thread.Start();
            Logger.Info("FPS measurement started (ETW)");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn("FPS measurement could not be started", ex);
            Stop();
            return false;
        }
    }

    private void OnEvent(TraceEvent e)
    {
        var id = (int)e.ID;
        var isPresent = (e.ProviderGuid == DxgiProvider && (id == 42 || id == 55)) // Present_Start, PresentMultiplaneOverlay_Start
                        || (e.ProviderGuid == D3D9Provider && id == 1);              // Present_Start
        if (isPresent) _presents.AddOrUpdate(e.ProcessID, 1, (_, c) => c + 1);
    }

    /// <summary>Frames per second per process since the last call.</summary>
    public Dictionary<int, double> Sample()
    {
        var seconds = Math.Max(0.05, _window.Elapsed.TotalSeconds);
        _window.Restart();
        var result = new Dictionary<int, double>();
        foreach (var pid in _presents.Keys.ToList())
        {
            if (_presents.TryRemove(pid, out var count) && count > 0) result[pid] = count / seconds;
        }
        return result;
    }

    public void Stop()
    {
        try
        {
            _session?.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Warn("FPS session could not be stopped", ex);
        }
        _session = null;
        _thread = null;
        _presents.Clear();
    }

    public void Dispose() => Stop();
}
