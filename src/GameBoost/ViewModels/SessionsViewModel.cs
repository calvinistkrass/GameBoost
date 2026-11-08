using System.Collections.ObjectModel;
using System.Globalization;
using GameBoost.Infrastructure;
using GameBoost.Services;
using GameBoost.Views;

namespace GameBoost.ViewModels;

/// <summary>"Sessions" page: history of game mode runs with FPS / temperature report.</summary>
public sealed class SessionsViewModel : ObservableObject, IDisposable, INavigationAware
{
    private readonly AppController _controller;
    private SessionRecord? _selected;

    public SessionsViewModel(AppController controller)
    {
        _controller = controller;
        DeleteCommand = new RelayCommand(Delete, () => Selected != null);
        ClearCommand = new RelayCommand(Clear, () => Sessions.Count > 0);
        _controller.Sessions.Completed += OnCompleted;
        Loc.LanguageChanged += OnLanguageChanged;
        _selected = Sessions.FirstOrDefault();
    }

    public ObservableCollection<SessionRecord> Sessions => _controller.Sessions.Sessions;
    public RelayCommand DeleteCommand { get; }
    public RelayCommand ClearCommand { get; }

    public SessionRecord? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value)) OnPropertyChanged(string.Empty);
        }
    }

    public bool HasSessions => Sessions.Count > 0;
    public bool HasSelection => Selected != null;
    public bool IsRecording => _controller.Sessions.IsRecording;

    public string Title => Selected?.Game ?? "";
    public string When => Selected is { } s ? $"{s.Start.ToString("g", CultureInfo.DefaultThreadCurrentUICulture ?? CultureInfo.CurrentUICulture)} · {FormatDuration(s.Duration)} · {s.Profile}" : "";
    public string AvgFps => Fmt(Selected?.AvgFps);
    public string LowFps => Fmt(Selected?.OnePercentLowFps);
    public string MaxFps => Fmt(Selected?.MaxFps);
    public string MaxTemp => Selected?.MaxGpuTemp is { } t ? $"{t:0} °C" : "–";
    public string AvgGpu => Selected?.AvgGpuLoad is { } g ? $"{g:0} %" : "–";
    public string AvgCpu => Selected?.AvgCpuLoad is { } c ? $"{c:0} %" : "–";
    public string MaxRam => Selected is { MaxRamGb: > 0 } s ? $"{s.MaxRamGb:0.0} GB" : "–";
    public bool HasFps => Selected?.FpsTimeline.Count > 1;
    public bool HasTemp => Selected?.TempTimeline.Count > 1;
    public IList<float>? FpsTimeline => Selected?.FpsTimeline;
    public IList<float>? TempTimeline => Selected?.TempTimeline;

    public static string FormatDuration(TimeSpan d) =>
        d.TotalHours >= 1 ? $"{(int)d.TotalHours} h {d.Minutes:00} min" : $"{Math.Max(1, (int)d.TotalMinutes)} min";

    private static string Fmt(double? v) => v is { } x ? x.ToString("0", CultureInfo.CurrentCulture) : "–";

    public void OnNavigatedTo()
    {
        Selected ??= Sessions.FirstOrDefault();
        OnPropertyChanged(string.Empty);
    }

    private void Delete()
    {
        if (Selected is not { } s) return;
        var index = Sessions.IndexOf(s);
        _controller.Sessions.Delete(s);
        Selected = Sessions.Count == 0 ? null : Sessions[Math.Min(index, Sessions.Count - 1)];
        OnPropertyChanged(string.Empty);
    }

    private void Clear()
    {
        if (!Dialogs.Confirm(Loc.T("Sessions.ClearConfirm"), Loc.T("Sessions.Clear"))) return;
        _controller.Sessions.Clear();
        Selected = null;
        OnPropertyChanged(string.Empty);
    }

    private void OnCompleted(object? sender, SessionRecord e)
    {
        Selected = e;
        OnPropertyChanged(string.Empty);
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);

    public void Dispose()
    {
        _controller.Sessions.Completed -= OnCompleted;
        Loc.LanguageChanged -= OnLanguageChanged;
    }
}
