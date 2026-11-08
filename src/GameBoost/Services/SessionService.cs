using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using GameBoost.Infrastructure;
using GameBoost.Services.Performance;

namespace GameBoost.Services;

/// <summary>Summary of one gaming session (one game mode run).</summary>
public sealed class SessionRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Game { get; set; } = "";
    public string Profile { get; set; } = "";
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public double? AvgFps { get; set; }
    public double? OnePercentLowFps { get; set; }
    public double? MaxFps { get; set; }
    public double? MaxGpuTemp { get; set; }
    public double? AvgGpuTemp { get; set; }
    public double? AvgGpuLoad { get; set; }
    public double? AvgCpuLoad { get; set; }
    public double MaxRamGb { get; set; }

    /// <summary>FPS per 5 seconds (for the chart).</summary>
    public List<float> FpsTimeline { get; set; } = new();

    /// <summary>GPU temperature per 5 seconds (for the chart).</summary>
    public List<float> TempTimeline { get; set; } = new();

    public TimeSpan Duration => End - Start;

    /// <summary>Calculates average, 1 % low (from per-second samples) and max of a series.</summary>
    public static (double? Avg, double? Low1, double? Max) Stats(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return (null, null, null);
        var sorted = values.OrderBy(v => v).ToList();
        var index = (int)Math.Floor(sorted.Count * 0.01);
        return (values.Average(), sorted[Math.Min(index, sorted.Count - 1)], sorted[^1]);
    }
}

/// <summary>Records FPS, temperature and load while game mode is active and keeps a history.</summary>
public sealed class SessionService : IDisposable
{
    private const int MaxSessions = 200;
    private const int TimelineStep = 5;

    private readonly PerformanceMonitorService _monitor;
    private readonly object _gate = new();
    private readonly List<double> _fps = new(), _temp = new(), _gpu = new(), _cpu = new();
    private readonly List<float> _fpsTimeline = new(), _tempTimeline = new();
    private readonly Dictionary<string, int> _processVotes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<double> _fpsBucket = new(), _tempBucket = new();
    private double _maxRam;
    private DateTime _start;
    private string _profile = "";
    private bool _running;

    public SessionService(PerformanceMonitorService monitor)
    {
        _monitor = monitor;
        Sessions = new ObservableCollection<SessionRecord>(Load());
    }

    private static string FilePath => Path.Combine(AppPaths.DataDirectory, "sessions.json");

    /// <summary>Newest first.</summary>
    public ObservableCollection<SessionRecord> Sessions { get; }

    public bool IsRecording => _running;

    /// <summary>Raised on the UI thread after a session was saved.</summary>
    public event EventHandler<SessionRecord>? Completed;

    public void Start(string profile)
    {
        lock (_gate)
        {
            if (_running) return;
            _running = true;
            _start = DateTime.Now;
            _profile = profile;
            _fps.Clear(); _temp.Clear(); _gpu.Clear(); _cpu.Clear();
            _fpsTimeline.Clear(); _tempTimeline.Clear(); _fpsBucket.Clear(); _tempBucket.Clear();
            _processVotes.Clear();
            _maxRam = 0;
        }
        _monitor.Updated += OnSnapshot;
        _monitor.SetSessionFps(true);
        _monitor.Acquire();
        Logger.Info($"Session recording started ({profile})");
    }

    public void Stop()
    {
        SessionRecord record;
        lock (_gate)
        {
            if (!_running) return;
            _running = false;
            FlushBuckets();
            var (avgFps, low, maxFps) = SessionRecord.Stats(_fps);
            var game = _processVotes.Count > 0 ? _processVotes.OrderByDescending(kv => kv.Value).First().Key : _profile;
            record = new SessionRecord
            {
                Game = game,
                Profile = _profile,
                Start = _start,
                End = DateTime.Now,
                AvgFps = avgFps,
                OnePercentLowFps = low,
                MaxFps = maxFps,
                MaxGpuTemp = _temp.Count > 0 ? _temp.Max() : null,
                AvgGpuTemp = _temp.Count > 0 ? _temp.Average() : null,
                AvgGpuLoad = _gpu.Count > 0 ? _gpu.Average() : null,
                AvgCpuLoad = _cpu.Count > 0 ? _cpu.Average() : null,
                MaxRamGb = _maxRam,
                FpsTimeline = _fpsTimeline.ToList(),
                TempTimeline = _tempTimeline.ToList()
            };
        }

        _monitor.Updated -= OnSnapshot;
        _monitor.SetSessionFps(false);
        _monitor.Release();

        if (record.Duration < TimeSpan.FromSeconds(30))
        {
            Logger.Info("Session shorter than 30 seconds – not saved");
            return;
        }

        Application.Current?.Dispatcher.Invoke(() =>
        {
            Sessions.Insert(0, record);
            while (Sessions.Count > MaxSessions) Sessions.RemoveAt(Sessions.Count - 1);
            Save();
            Completed?.Invoke(this, record);
        });
        Logger.Info($"Session saved: {record.Game}, {record.Duration:hh\\:mm\\:ss}, avg FPS {record.AvgFps:0}, max GPU {record.MaxGpuTemp:0} °C");
    }

    public void Delete(SessionRecord record)
    {
        Sessions.Remove(record);
        Save();
    }

    public void Clear()
    {
        Sessions.Clear();
        Save();
    }

    private void OnSnapshot(object? sender, PerformanceSnapshot s)
    {
        lock (_gate)
        {
            if (!_running) return;
            if (s.Fps is { } fps)
            {
                _fps.Add(fps);
                _fpsBucket.Add(fps);
                if (!string.IsNullOrEmpty(s.FpsProcess))
                    _processVotes[s.FpsProcess] = _processVotes.TryGetValue(s.FpsProcess, out var v) ? v + 1 : 1;
            }
            if (s.GpuTemperature is { } t) { _temp.Add(t); _tempBucket.Add(t); }
            if (s.GpuLoad is { } g) _gpu.Add(g);
            if (s.CpuLoad is { } c) _cpu.Add(c);
            _maxRam = Math.Max(_maxRam, s.RamUsedGb);

            if (Math.Max(_fpsBucket.Count, _tempBucket.Count) >= TimelineStep) FlushBuckets();
        }
    }

    private void FlushBuckets()
    {
        if (_fpsBucket.Count > 0) _fpsTimeline.Add((float)_fpsBucket.Average());
        if (_tempBucket.Count > 0) _tempTimeline.Add((float)_tempBucket.Average());
        _fpsBucket.Clear();
        _tempBucket.Clear();
    }

    private static List<SessionRecord> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new List<SessionRecord>();
            return JsonSerializer.Deserialize<List<SessionRecord>>(File.ReadAllText(FilePath), SettingsService.JsonOptions) ?? new();
        }
        catch (Exception ex)
        {
            Logger.Warn("Session history could not be read", ex);
            return new List<SessionRecord>();
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Sessions.ToList(), SettingsService.JsonOptions));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Logger.Warn("Session history could not be saved", ex);
        }
    }

    public void Dispose()
    {
        if (_running) Stop();
    }
}
