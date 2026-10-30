using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using GameBoost.Infrastructure;
using GameBoost.Models;
using GameBoost.Services;
using GameBoost.Views;
using WinForms = System.Windows.Forms;

namespace GameBoost.ViewModels;

/// <summary>"Crosshair" page: editor, preview, overlay, presets, sharing.</summary>
public sealed class CrosshairViewModel : ObservableObject, IDisposable, INavigationAware
{
    private readonly AppController _controller;
    private CrosshairPreset? _selectedPreset;
    private string _newPresetName = "";
    private string _importCode = "";
    private string _shareMessage = "";
    private bool _shareMessageIsError;
    private int _previewZoom = 3;
    private MonitorInfo? _selectedMonitor;
    private bool _suppressMonitorChange;

    public CrosshairViewModel(AppController controller)
    {
        _controller = controller;
        Config.PropertyChanged += OnConfigChanged;
        _controller.Overlay.VisibilityChanged += OnOverlayVisibilityChanged;
        _controller.PresetApplied += OnPresetApplied;
        Loc.LanguageChanged += OnLanguageChanged;

        SetColorCommand = new RelayCommand(p => { if (p is string hex) Config.Color = hex; });
        SetOutlineColorCommand = new RelayCommand(p => { if (p is string hex) Config.OutlineColor = hex; });
        PickColorCommand = new RelayCommand(() => PickColor(false));
        PickOutlineColorCommand = new RelayCommand(() => PickColor(true));
        ResetCommand = new RelayCommand(() => Config.CopyFrom(new CrosshairConfig()));
        ResetOffsetCommand = new RelayCommand(() => { Config.OffsetX = 0; Config.OffsetY = 0; });
        RefreshMonitorsCommand = new RelayCommand(RefreshMonitors);
        LoadPresetCommand = new RelayCommand(LoadPreset, () => SelectedPreset != null);
        OverwritePresetCommand = new RelayCommand(OverwritePreset, () => SelectedPreset != null);
        DeletePresetCommand = new RelayCommand(DeletePreset, () => SelectedPreset != null);
        SavePresetCommand = new RelayCommand(SaveAsNewPreset);
        CopyCodeCommand = new RelayCommand(CopyCode);
        ImportCommand = new RelayCommand(Import, () => !string.IsNullOrWhiteSpace(ImportCode));

        _selectedPreset = Presets.FirstOrDefault(p => p.Name == _controller.Settings.Crosshair.ActivePresetName);
        RefreshMonitors();
    }

    public CrosshairConfig Config => _controller.Settings.Crosshair.Current;

    public static IReadOnlyList<string> ColorSwatches { get; } = new[]
    {
        "#00FF7F", "#00FFFF", "#FF3B3B", "#FFE14D", "#FF00FF", "#FFFFFF", "#FF8C00", "#7C5CFF"
    };

    public static IReadOnlyList<string> OutlineSwatches { get; } = new[] { "#000000", "#FFFFFF", "#303030" };

    // ------------------------------------------------------------------ Commands

    public RelayCommand SetColorCommand { get; }
    public RelayCommand SetOutlineColorCommand { get; }
    public RelayCommand PickColorCommand { get; }
    public RelayCommand PickOutlineColorCommand { get; }
    public RelayCommand ResetCommand { get; }
    public RelayCommand ResetOffsetCommand { get; }
    public RelayCommand RefreshMonitorsCommand { get; }
    public RelayCommand LoadPresetCommand { get; }
    public RelayCommand OverwritePresetCommand { get; }
    public RelayCommand DeletePresetCommand { get; }
    public RelayCommand SavePresetCommand { get; }
    public RelayCommand CopyCodeCommand { get; }
    public RelayCommand ImportCommand { get; }

    // ------------------------------------------------------------------ Editor helper properties

    public string ColorText
    {
        get => Config.Color;
        set
        {
            var n = ColorUtil.Normalize(value);
            if (n != null) Config.Color = n;
            OnPropertyChanged();
        }
    }

    public string OutlineColorText
    {
        get => Config.OutlineColor;
        set
        {
            var n = ColorUtil.Normalize(value);
            if (n != null) Config.OutlineColor = n;
            OnPropertyChanged();
        }
    }

    public double OpacityPercent
    {
        get => Math.Round(Config.Opacity * 100);
        set => Config.Opacity = value / 100.0;
    }

    public bool ShowLength => Config.Style != CrosshairStyle.Dot;
    public bool ShowThickness => Config.Style != CrosshairStyle.Dot;
    public bool ShowGap => Config.Style is CrosshairStyle.Cross or CrosshairStyle.CrossWithDot or CrosshairStyle.TShape;
    public bool ShowDotSize => Config.Style is CrosshairStyle.Dot or CrosshairStyle.CrossWithDot;
    public string LengthLabel => Config.Style == CrosshairStyle.Circle ? Loc.T("CH.Radius") : Loc.T("CH.Length");

    public int PreviewZoom
    {
        get => _previewZoom;
        set => SetProperty(ref _previewZoom, Math.Clamp(value, 1, 6));
    }

    // ------------------------------------------------------------------ Overlay

    public bool OverlayVisible
    {
        get => _controller.Overlay.IsVisible;
        set => _controller.SetCrosshairVisible(value);
    }

    public ObservableCollection<MonitorInfo> Monitors { get; } = new();

    public MonitorInfo? SelectedMonitor
    {
        get => _selectedMonitor;
        set
        {
            if (!SetProperty(ref _selectedMonitor, value) || value == null || _suppressMonitorChange) return;
            _controller.Settings.Crosshair.MonitorDeviceName = value.DeviceName;
            _controller.SettingsService.RequestSave();
            _controller.Overlay.Reposition();
        }
    }

    // ------------------------------------------------------------------ Presets

    public ObservableCollection<CrosshairPreset> Presets => _controller.Settings.Crosshair.Presets;

    public CrosshairPreset? SelectedPreset
    {
        get => _selectedPreset;
        set => SetProperty(ref _selectedPreset, value);
    }

    public string NewPresetName
    {
        get => _newPresetName;
        set => SetProperty(ref _newPresetName, value ?? "");
    }

    // ------------------------------------------------------------------ Sharing

    public string ExportCode => CrosshairCodec.Encode(Config);

    public string ImportCode
    {
        get => _importCode;
        set => SetProperty(ref _importCode, value ?? "");
    }

    public string ShareMessage
    {
        get => _shareMessage;
        private set => SetProperty(ref _shareMessage, value);
    }

    public bool ShareMessageIsError
    {
        get => _shareMessageIsError;
        private set => SetProperty(ref _shareMessageIsError, value);
    }

    // ------------------------------------------------------------------ Logic

    public void OnNavigatedTo()
    {
        RefreshMonitors();
        OnPropertyChanged(nameof(OverlayVisible));
    }

    private void RefreshMonitors()
    {
        _suppressMonitorChange = true;
        try
        {
            Monitors.Clear();
            foreach (var m in MonitorService.GetMonitors()) Monitors.Add(m);
            var wanted = _controller.Settings.Crosshair.MonitorDeviceName;
            SelectedMonitor = Monitors.FirstOrDefault(m => string.Equals(m.DeviceName, wanted, StringComparison.OrdinalIgnoreCase))
                              ?? Monitors.FirstOrDefault(m => m.IsPrimary)
                              ?? Monitors.FirstOrDefault();
        }
        finally
        {
            _suppressMonitorChange = false;
        }
    }

    private void PickColor(bool outline)
    {
        var current = ColorUtil.ToColor(outline ? Config.OutlineColor : Config.Color, System.Windows.Media.Colors.White);
        using var dlg = new WinForms.ColorDialog
        {
            FullOpen = true,
            AnyColor = true,
            Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B)
        };
        if (dlg.ShowDialog() != WinForms.DialogResult.OK) return;

        var hex = ColorUtil.ToHex(dlg.Color.R, dlg.Color.G, dlg.Color.B);
        if (outline) Config.OutlineColor = hex;
        else Config.Color = hex;
    }

    private void LoadPreset()
    {
        if (SelectedPreset == null) return;
        _controller.ApplyPreset(SelectedPreset);
    }

    private void OverwritePreset()
    {
        if (SelectedPreset == null) return;
        if (!Dialogs.Confirm(Loc.F("CH.Preset.OverwriteConfirm", SelectedPreset.Name))) return;
        SelectedPreset.Config = Config.Clone();
        _controller.Settings.Crosshair.ActivePresetName = SelectedPreset.Name;
        _controller.SettingsService.RequestSave();
        ShowShareMessage(Loc.F("CH.Preset.Saved", SelectedPreset.Name), false);
    }

    private void DeletePreset()
    {
        if (SelectedPreset == null) return;
        if (!Dialogs.Confirm(Loc.F("CH.Preset.DeleteConfirm", SelectedPreset.Name))) return;
        var preset = SelectedPreset;
        SelectedPreset = null;
        Presets.Remove(preset);
        if (_controller.Settings.Crosshair.ActivePresetName == preset.Name)
            _controller.Settings.Crosshair.ActivePresetName = null;
        _controller.SettingsService.RequestSave();
    }

    private void SaveAsNewPreset()
    {
        var name = NewPresetName.Trim();
        if (name.Length == 0)
        {
            var n = Presets.Count + 1;
            while (Presets.Any(p => p.Name == Loc.F("CH.Preset.DefaultName", n))) n++;
            name = Loc.F("CH.Preset.DefaultName", n);
        }

        var existing = Presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase));
        if (existing != null)
        {
            if (!Dialogs.Confirm(Loc.F("CH.Preset.OverwriteConfirm", existing.Name))) return;
            existing.Config = Config.Clone();
            SelectedPreset = existing;
        }
        else
        {
            var preset = CrosshairPreset.Create(name, Config);
            Presets.Add(preset);
            SelectedPreset = preset;
        }

        _controller.Settings.Crosshair.ActivePresetName = name;
        _controller.SettingsService.RequestSave();
        NewPresetName = "";
        ShowShareMessage(Loc.F("CH.Preset.Saved", name), false);
    }

    private void CopyCode()
    {
        try
        {
            Clipboard.SetText(ExportCode);
            ShowShareMessage(Loc.T("CH.Share.Copied"), false);
        }
        catch (Exception ex)
        {
            Logger.Warn("Clipboard not available", ex);
            ShowShareMessage(Loc.T("CH.Share.CopyFailed"), true);
        }
    }

    private void Import()
    {
        if (CrosshairCodec.TryDecode(ImportCode, out var config))
        {
            Config.CopyFrom(config);
            _controller.Settings.Crosshair.ActivePresetName = null;
            ImportCode = "";
            ShowShareMessage(Loc.T("CH.Share.Imported"), false);
        }
        else
        {
            ShowShareMessage(Loc.T("CH.Share.Invalid"), true);
        }
    }

    private void ShowShareMessage(string message, bool isError)
    {
        ShareMessageIsError = isError;
        ShareMessage = message;
    }

    private void OnConfigChanged(object? sender, PropertyChangedEventArgs e)
    {
        _controller.SettingsService.RequestSave();
        OnPropertyChanged(nameof(ExportCode));
        OnPropertyChanged(nameof(ColorText));
        OnPropertyChanged(nameof(OutlineColorText));
        OnPropertyChanged(nameof(OpacityPercent));
        if (e.PropertyName == nameof(CrosshairConfig.Style))
        {
            OnPropertyChanged(nameof(ShowLength));
            OnPropertyChanged(nameof(ShowThickness));
            OnPropertyChanged(nameof(ShowGap));
            OnPropertyChanged(nameof(ShowDotSize));
            OnPropertyChanged(nameof(LengthLabel));
        }
    }

    private void OnOverlayVisibilityChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(OverlayVisible));

    private void OnPresetApplied(object? sender, CrosshairPreset preset) => SelectedPreset = preset;

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(LengthLabel));
        RefreshMonitors();
    }

    public void Dispose()
    {
        Config.PropertyChanged -= OnConfigChanged;
        _controller.Overlay.VisibilityChanged -= OnOverlayVisibilityChanged;
        _controller.PresetApplied -= OnPresetApplied;
        Loc.LanguageChanged -= OnLanguageChanged;
    }
}
