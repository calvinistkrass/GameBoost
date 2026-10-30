using System.Collections.ObjectModel;
using System.Globalization;
using GameBoost.Infrastructure;
using GameBoost.Services.SystemCheck;
using GameBoost.Views;

namespace GameBoost.ViewModels;

/// <summary>"PC check" page: hardware, Windows gaming settings with one-click fixes, recommended game settings.</summary>
public sealed class PcCheckViewModel : ObservableObject, IDisposable, INavigationAware
{
    private readonly AppController _controller;
    private bool _isRunning;
    private HardwareInfo? _hardware;

    public PcCheckViewModel(AppController controller)
    {
        _controller = controller;
        RescanCommand = new AsyncRelayCommand(RescanAsync, () => !IsRunning);
        FixCommand = new AsyncRelayCommand(p => FixAsync(p as SystemCheckItem), p => !IsRunning && p is SystemCheckItem { CanFix: true });
        UndoCommand = new AsyncRelayCommand(p => UndoAsync(p as SystemCheckItem), p => !IsRunning && p is SystemCheckItem { CanUndo: true });
        OpenCommand = new RelayCommand(p => SystemCheckService.OpenLink((p as SystemCheckItem)?.Link), p => p is SystemCheckItem { Link: not null });
        FixAllCommand = new AsyncRelayCommand(FixAllAsync, () => !IsRunning && Checks.Any(c => c.CanFix && c.Id != "RefreshRate"));

        _controller.SystemCheck.Completed += OnCompleted;
        Loc.LanguageChanged += OnLanguageChanged;
        if (_controller.SystemCheck.LastResult is { } last) Apply(last.Hardware, last.Checks);
    }

    public AsyncRelayCommand RescanCommand { get; }
    public AsyncRelayCommand FixCommand { get; }
    public AsyncRelayCommand UndoCommand { get; }
    public RelayCommand OpenCommand { get; }
    public AsyncRelayCommand FixAllCommand { get; }

    public ObservableCollection<SystemCheckItem> Checks { get; } = new();
    public ObservableCollection<GameSettingRecommendation> Recommendations { get; } = new();

    public bool IsRunning
    {
        get => _isRunning;
        private set => SetProperty(ref _isRunning, value);
    }

    public bool HasResult => _hardware != null;

    public int Score => SystemCheckService.Score(Checks);
    public string ScoreText => HasResult ? Score.ToString(CultureInfo.CurrentCulture) : "–";
    public double ScoreValue => HasResult ? Score : 0;
    public string ScoreLabel => !HasResult ? Loc.T("Home.Analyzing")
        : Score >= 90 ? Loc.T("PC.Score.Great") : Score >= 70 ? Loc.T("PC.Score.Good") : Loc.T("PC.Score.Attention");
    public string Summary => HasResult
        ? Loc.F("PC.Summary", Checks.Count(c => c.Status == CheckStatus.Good), Checks.Count(c => c.Status is CheckStatus.Recommended or CheckStatus.Warning))
        : "";

    public string Cpu => _hardware is { } h ? $"{h.Cpu} · {Loc.F("PC.Threads", h.Threads)}" : "…";
    public string Gpu => _hardware is { } h ? h.Gpu + (h.VramGb is { } v ? $" · {v:0.#} GB VRAM" : "") : "…";
    public string Memory => _hardware is { } h ? $"{h.RamGb:0} GB" : "…";
    public string Display => _hardware is { ResolutionWidth: > 0 } h ? $"{h.ResolutionWidth} × {h.ResolutionHeight} · {h.CurrentHz} Hz (max. {h.MaxHz} Hz)" : "…";
    public string Os => _hardware?.Os ?? "…";
    public string Tier => _hardware is { } h ? Loc.T($"Tier.{h.Tier}") : "…";
    public string TierDescription => _hardware is { } h ? Loc.T($"Tier.{h.Tier}.Desc") : "";

    public void OnNavigatedTo()
    {
        if (!HasResult && !IsRunning) _ = RescanAsync();
    }

    private async Task RescanAsync()
    {
        IsRunning = true;
        try
        {
            await _controller.SystemCheck.RunAsync();
        }
        catch (Exception ex)
        {
            ErrorReporter.Report("PC check failed", ex);
        }
        finally
        {
            IsRunning = false;
        }
    }

    private async Task FixAsync(SystemCheckItem? item)
    {
        if (item == null || _hardware == null) return;
        if (item.Id == "RefreshRate" && !Dialogs.Confirm(Loc.F("PC.ConfirmRefresh", _hardware.MaxHz))) return;
        if (!_controller.SystemCheck.Fix(item.Id, _hardware)) Dialogs.Error(Loc.T("PC.FixFailed"));
        await RescanAsync();
    }

    private async Task UndoAsync(SystemCheckItem? item)
    {
        if (item == null) return;
        if (!_controller.SystemCheck.Undo(item.Id)) Dialogs.Error(Loc.T("PC.UndoFailed"));
        await RescanAsync();
    }

    /// <summary>Applies all safe fixes at once (the refresh rate is left out because the screen flickers).</summary>
    private async Task FixAllAsync()
    {
        if (_hardware == null) return;
        foreach (var item in Checks.Where(c => c.CanFix && c.Id != "RefreshRate").ToList())
            _controller.SystemCheck.Fix(item.Id, _hardware);
        await RescanAsync();
    }

    private void Apply(HardwareInfo hardware, List<SystemCheckItem> checks)
    {
        _hardware = hardware;
        Checks.Clear();
        foreach (var c in checks.OrderBy(c => c.Status switch
                 {
                     CheckStatus.Warning => 0,
                     CheckStatus.Recommended => 1,
                     CheckStatus.Info => 2,
                     _ => 3
                 }))
            Checks.Add(c);

        Recommendations.Clear();
        foreach (var r in SystemCheckService.GameRecommendations(hardware)) Recommendations.Add(r);
        OnPropertyChanged(string.Empty);
    }

    private void OnCompleted(object? sender, EventArgs e)
    {
        if (_controller.SystemCheck.LastResult is { } last) Apply(last.Hardware, last.Checks);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (_hardware != null) _ = RescanAsync();
    }

    public void Dispose()
    {
        _controller.SystemCheck.Completed -= OnCompleted;
        Loc.LanguageChanged -= OnLanguageChanged;
    }
}
