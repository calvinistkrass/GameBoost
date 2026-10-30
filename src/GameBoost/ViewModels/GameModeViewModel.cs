using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Media;
using GameBoost.Infrastructure;
using GameBoost.Models;
using GameBoost.Services;
using GameBoost.Views;
using Microsoft.Win32;

namespace GameBoost.ViewModels;

public sealed record PresetChoice(string? Name, string Display);

/// <summary>"Game mode" page.</summary>
public sealed class GameModeViewModel : ObservableObject, IDisposable, INavigationAware
{
    private readonly AppController _controller;
    private GameModeProfile _selectedProfile;
    private string _selectedTab = "Kill";
    private string _searchText = "";
    private bool _isLoadingProcesses;
    private string _newTriggerName = "";
    private List<ProcessSnapshot> _snapshots = new();
    private Dictionary<string, ImageSource?> _icons = new(StringComparer.OrdinalIgnoreCase);
    private ObservableCollection<ProcessItemViewModel> _processes = new();
    private ICollectionView? _processesView;
    private List<PresetChoice> _presetChoices = new();
    private DateTime _lastRefresh = DateTime.MinValue;

    public GameModeViewModel(AppController controller)
    {
        _controller = controller;
        _selectedProfile = controller.SelectedProfile;
        SubscribeProfile(_selectedProfile);

        ToggleCommand = new AsyncRelayCommand(_controller.ToggleGameModeAsync, () => !_controller.GameMode.IsBusy);
        RefreshProcessesCommand = new AsyncRelayCommand(RefreshProcessesAsync, () => !IsLoadingProcesses);
        NewProfileCommand = new RelayCommand(NewProfile);
        RenameProfileCommand = new RelayCommand(RenameProfile);
        DuplicateProfileCommand = new RelayCommand(DuplicateProfile);
        DeleteProfileCommand = new RelayCommand(DeleteProfile, () => Profiles.Count > 1);
        AddProgramCommand = new RelayCommand(AddProgram);
        RemoveProgramCommand = new RelayCommand(p => RemoveProgram(p as StartProgram));
        AddTriggerCommand = new RelayCommand(AddTriggerByName, () => !string.IsNullOrWhiteSpace(NewTriggerName));
        BrowseTriggerCommand = new RelayCommand(BrowseTrigger);
        RemoveTriggerCommand = new RelayCommand(p => RemoveTrigger(p as string));
        SelectAllVisibleCommand = new RelayCommand(() => SetVisibleSelection(true));
        SelectNoneCommand = new RelayCommand(() => SetVisibleSelection(false));

        _controller.GameMode.StateChanged += OnGameModeStateChanged;
        _controller.Settings.Crosshair.Presets.CollectionChanged += OnPresetsChanged;
        Loc.LanguageChanged += OnLanguageChanged;

        RebuildPresetChoices();
        RebuildProcessItems();
        RefreshStatus();
    }

    // ------------------------------------------------------------------ Commands

    public AsyncRelayCommand ToggleCommand { get; }
    public AsyncRelayCommand RefreshProcessesCommand { get; }
    public RelayCommand NewProfileCommand { get; }
    public RelayCommand RenameProfileCommand { get; }
    public RelayCommand DuplicateProfileCommand { get; }
    public RelayCommand DeleteProfileCommand { get; }
    public RelayCommand AddProgramCommand { get; }
    public RelayCommand RemoveProgramCommand { get; }
    public RelayCommand AddTriggerCommand { get; }
    public RelayCommand BrowseTriggerCommand { get; }
    public RelayCommand RemoveTriggerCommand { get; }
    public RelayCommand SelectAllVisibleCommand { get; }
    public RelayCommand SelectNoneCommand { get; }

    // ------------------------------------------------------------------ Profiles

    public ObservableCollection<GameModeProfile> Profiles => _controller.Settings.Profiles;

    public GameModeProfile SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (value == null || ReferenceEquals(value, _selectedProfile)) return;
            UnsubscribeProfile(_selectedProfile);
            _selectedProfile = value;
            SubscribeProfile(value);
            _controller.Settings.ActiveProfileId = value.Id;
            _controller.SettingsService.RequestSave();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedPresetChoice));
            RebuildProcessItems();
            RefreshStatus();
        }
    }

    public string SelectedTab
    {
        get => _selectedTab;
        set => SetProperty(ref _selectedTab, value);
    }

    // ------------------------------------------------------------------ Status

    public bool IsActive => _controller.GameMode.IsActive;
    public bool IsBusy => _controller.GameMode.IsBusy;

    public string ToggleState => IsBusy ? "…" : IsActive ? Loc.T("Status.On") : Loc.T("Status.Off");

    public string StatusHeadline => IsBusy
        ? Loc.T("GM.Status.Busy")
        : IsActive ? Loc.T("GM.Status.ActiveHeadline") : Loc.T("GM.Status.ReadyHeadline");

    public string StatusSubline
    {
        get
        {
            var gm = _controller.GameMode;
            if (IsActive)
            {
                var suffix = gm.ActivatedAutomatically ? Loc.T("GM.Status.AutoSuffix") : "";
                return Loc.F("GM.Status.ActiveSince", gm.ActiveProfile?.Name ?? "", gm.ActivatedAt?.ToString("HH:mm") ?? "", suffix);
            }
            return Loc.F("GM.Status.ReadySub", SelectedProfile.Name);
        }
    }

    public ObservableCollection<StatusEntry> DisplayEntries { get; } = new();

    // ------------------------------------------------------------------ Processes

    public ICollectionView? ProcessesView
    {
        get => _processesView;
        private set => SetProperty(ref _processesView, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? "")) ProcessesView?.Refresh();
        }
    }

    public bool IsLoadingProcesses
    {
        get => _isLoadingProcesses;
        private set => SetProperty(ref _isLoadingProcesses, value);
    }

    public string SelectedCountText => Loc.F("GM.Proc.SelectedCount", SelectedProfile.ProcessesToKill.Count);

    // ------------------------------------------------------------------ Options / automation

    public List<PresetChoice> PresetChoices
    {
        get => _presetChoices;
        private set => SetProperty(ref _presetChoices, value);
    }

    public PresetChoice? SelectedPresetChoice
    {
        get => PresetChoices.FirstOrDefault(c => c.Name == SelectedProfile.CrosshairPresetName) ?? PresetChoices.FirstOrDefault();
        set
        {
            if (value == null) return;
            SelectedProfile.CrosshairPresetName = value.Name;
            OnPropertyChanged();
        }
    }

    public string NewTriggerName
    {
        get => _newTriggerName;
        set => SetProperty(ref _newTriggerName, value ?? "");
    }

    public bool AutoSwitchDisabled => !_controller.Settings.AutoProfileSwitch;

    // ------------------------------------------------------------------ Navigation

    public void OnNavigatedTo()
    {
        OnPropertyChanged(nameof(AutoSwitchDisabled));
        if (DateTime.Now - _lastRefresh > TimeSpan.FromSeconds(5) && RefreshProcessesCommand.CanExecute(null))
            RefreshProcessesCommand.Execute(null);
    }

    // ------------------------------------------------------------------ Process list

    private async Task RefreshProcessesAsync()
    {
        IsLoadingProcesses = true;
        try
        {
            var (snapshots, icons) = await Task.Run(() =>
            {
                var list = ProcessService.GetKillableProcesses();
                var map = new Dictionary<string, ImageSource?>(StringComparer.OrdinalIgnoreCase);
                foreach (var s in list) map[s.Path] = IconCache.Get(s.Path);
                return (list, map);
            });
            _snapshots = snapshots;
            _icons = icons;
            _lastRefresh = DateTime.Now;
            RebuildProcessItems();
        }
        catch (Exception ex)
        {
            ErrorReporter.Report("Could not load process list", ex);
        }
        finally
        {
            IsLoadingProcesses = false;
        }
    }

    private void RebuildProcessItems()
    {
        var selected = new HashSet<string>(SelectedProfile.ProcessesToKill, StringComparer.OrdinalIgnoreCase);
        var items = new List<ProcessItemViewModel>();

        foreach (var s in _snapshots)
        {
            _icons.TryGetValue(s.Path, out var icon);
            items.Add(new ProcessItemViewModel(s.Name, s.DisplayName, s.Path, s.Count, s.MemoryBytes, icon,
                selected.Contains(s.Name), OnItemSelectionChanged));
        }

        // Still show programs that are selected but not running right now.
        foreach (var name in selected.Where(n => _snapshots.All(s => !s.Name.Equals(n, StringComparison.OrdinalIgnoreCase))))
        {
            items.Add(new ProcessItemViewModel(name, name, null, 0, 0, null, true, OnItemSelectionChanged));
        }

        _processes = new ObservableCollection<ProcessItemViewModel>(items
            .OrderByDescending(i => i.IsSelected)
            .ThenBy(i => i.DisplayName, StringComparer.CurrentCultureIgnoreCase));

        var view = CollectionViewSource.GetDefaultView(_processes);
        view.Filter = FilterProcess;
        ProcessesView = view;
        OnPropertyChanged(nameof(SelectedCountText));
    }

    private bool FilterProcess(object obj)
    {
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        var item = (ProcessItemViewModel)obj;
        return item.DisplayName.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase)
               || (item.Path?.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) ?? false);
    }

    private void OnItemSelectionChanged(ProcessItemViewModel item)
    {
        var list = SelectedProfile.ProcessesToKill;
        if (item.IsSelected)
        {
            if (!list.Contains(item.Name)) list.Add(item.Name);
        }
        else
        {
            list.RemoveAll(n => n.Equals(item.Name, StringComparison.OrdinalIgnoreCase));
        }
        _controller.SettingsService.RequestSave();
        OnPropertyChanged(nameof(SelectedCountText));
        RefreshStatus();
    }

    private void SetVisibleSelection(bool selected)
    {
        if (ProcessesView == null) return;
        foreach (ProcessItemViewModel item in ProcessesView) item.IsSelected = selected;
    }

    // ------------------------------------------------------------------ Profile management

    private void NewProfile()
    {
        var name = Dialogs.Input(Loc.T("Dlg.NewProfile.Title"), Loc.T("Dlg.ProfileName"));
        if (name == null) return;
        var profile = new GameModeProfile { Name = name };
        Profiles.Add(profile);
        SelectedProfile = profile;
        _controller.SettingsService.RequestSave();
    }

    private void RenameProfile()
    {
        var name = Dialogs.Input(Loc.T("Dlg.RenameProfile.Title"), Loc.T("Dlg.ProfileName"), SelectedProfile.Name);
        if (name == null) return;
        SelectedProfile.Name = name;
        _controller.SettingsService.RequestSave();
        RefreshStatus();
    }

    private void DuplicateProfile()
    {
        var copy = SelectedProfile.CloneAsNew(Loc.F("Dlg.Copy", SelectedProfile.Name));
        Profiles.Add(copy);
        SelectedProfile = copy;
        _controller.SettingsService.RequestSave();
    }

    private void DeleteProfile()
    {
        if (Profiles.Count <= 1)
        {
            Dialogs.Info(Loc.T("Dlg.LastProfile"));
            return;
        }
        if (IsActive && ReferenceEquals(_controller.GameMode.ActiveProfile, SelectedProfile))
        {
            Dialogs.Info(Loc.T("Dlg.ProfileActive"));
            return;
        }
        if (!Dialogs.Confirm(Loc.F("Dlg.DeleteProfile.Msg", SelectedProfile.Name), Loc.T("Dlg.DeleteProfile.Title"))) return;

        var toDelete = SelectedProfile;
        var index = Profiles.IndexOf(toDelete);
        SelectedProfile = Profiles[index == 0 ? 1 : index - 1];
        Profiles.Remove(toDelete);
        _controller.SettingsService.RequestSave();
    }

    // ------------------------------------------------------------------ Start programs

    private void AddProgram()
    {
        var dlg = new OpenFileDialog
        {
            Title = Loc.T("Dlg.SelectProgram"),
            Filter = Loc.T("Dlg.ExeFilter"),
            CheckFileExists = true,
            DereferenceLinks = false
        };
        if (dlg.ShowDialog() != true) return;

        if (SelectedProfile.ProgramsToStart.Any(p => p.Path.Equals(dlg.FileName, StringComparison.OrdinalIgnoreCase))) return;
        SelectedProfile.ProgramsToStart.Add(new StartProgram { Path = dlg.FileName });
    }

    private void RemoveProgram(StartProgram? program)
    {
        if (program != null) SelectedProfile.ProgramsToStart.Remove(program);
    }

    // ------------------------------------------------------------------ Automation

    private void AddTriggerByName()
    {
        AddTrigger(NewTriggerName);
        NewTriggerName = "";
    }

    private void BrowseTrigger()
    {
        var dlg = new OpenFileDialog
        {
            Title = Loc.T("Dlg.SelectGame"),
            Filter = Loc.T("Dlg.ExeOnlyFilter"),
            CheckFileExists = true
        };
        if (dlg.ShowDialog() == true) AddTrigger(dlg.FileName);
    }

    private void AddTrigger(string input)
    {
        var name = ProcessNames.Normalize(input);
        if (name.Length == 0 || SelectedProfile.TriggerExecutables.Contains(name)) return;
        if (ProcessGuard.IsProtectedName(name))
        {
            Dialogs.Info(Loc.F("GM.Auto.Protected", name));
            return;
        }
        SelectedProfile.TriggerExecutables.Add(name);
    }

    private void RemoveTrigger(string? name)
    {
        if (name != null) SelectedProfile.TriggerExecutables.Remove(name);
    }

    // ------------------------------------------------------------------ Helpers

    private void RebuildPresetChoices()
    {
        var list = new List<PresetChoice> { new(null, Loc.T("GM.Opt.CurrentCrosshair")) };
        list.AddRange(_controller.Settings.Crosshair.Presets.Select(p => new PresetChoice(p.Name, p.Name)));
        PresetChoices = list;
        OnPropertyChanged(nameof(SelectedPresetChoice));
    }

    private void RefreshStatus()
    {
        DisplayEntries.Clear();
        var gm = _controller.GameMode;

        if (gm.IsBusy)
        {
            DisplayEntries.Add(new StatusEntry(StatusKind.Info, Loc.T("GM.Status.Working")));
        }
        else if (gm.IsActive)
        {
            foreach (var e in gm.StatusEntries) DisplayEntries.Add(e);
            if (DisplayEntries.Count == 0) DisplayEntries.Add(new StatusEntry(StatusKind.Ok, Loc.T("GM.Status.NothingToDo")));
        }
        else
        {
            foreach (var e in BuildPlan(SelectedProfile)) DisplayEntries.Add(e);
        }

        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(ToggleState));
        OnPropertyChanged(nameof(StatusHeadline));
        OnPropertyChanged(nameof(StatusSubline));
    }

    private static IEnumerable<StatusEntry> BuildPlan(GameModeProfile p)
    {
        yield return p.ProcessesToKill.Count > 0
            ? new StatusEntry(StatusKind.Info, Loc.F("GM.Plan.Kill", p.ProcessesToKill.Count))
            : new StatusEntry(StatusKind.Info, Loc.T("GM.Plan.KillNone"));
        if (p.SwitchPowerPlan) yield return new StatusEntry(StatusKind.Info, Loc.T("GM.Plan.Power"));
        if (p.DisableNotifications) yield return new StatusEntry(StatusKind.Info, Loc.T("GM.Plan.Notif"));
        if (p.ProgramsToStart.Count > 0) yield return new StatusEntry(StatusKind.Info, Loc.F("GM.Plan.Start", p.ProgramsToStart.Count));
        if (p.ShowCrosshair) yield return new StatusEntry(StatusKind.Info, Loc.T("GM.Plan.Crosshair"));
        if (p.TriggerExecutables.Count > 0)
            yield return new StatusEntry(StatusKind.Info, Loc.F("GM.Plan.Auto", string.Join(", ", p.TriggerExecutables.Select(t => t + ".exe"))));
    }

    private void SubscribeProfile(GameModeProfile profile)
    {
        profile.PropertyChanged += OnProfilePropertyChanged;
        profile.ProgramsToStart.CollectionChanged += OnProfileCollectionChanged;
        profile.TriggerExecutables.CollectionChanged += OnProfileCollectionChanged;
    }

    private void UnsubscribeProfile(GameModeProfile profile)
    {
        profile.PropertyChanged -= OnProfilePropertyChanged;
        profile.ProgramsToStart.CollectionChanged -= OnProfileCollectionChanged;
        profile.TriggerExecutables.CollectionChanged -= OnProfileCollectionChanged;
    }

    private void OnProfilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _controller.SettingsService.RequestSave();
        RefreshStatus();
    }

    private void OnProfileCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _controller.SettingsService.RequestSave();
        RefreshStatus();
    }

    private void OnGameModeStateChanged(object? sender, EventArgs e) => RefreshStatus();

    private void OnPresetsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildPresetChoices();

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RebuildPresetChoices();
        RebuildProcessItems();
        RefreshStatus();
    }

    public void Dispose()
    {
        UnsubscribeProfile(_selectedProfile);
        _controller.GameMode.StateChanged -= OnGameModeStateChanged;
        _controller.Settings.Crosshair.Presets.CollectionChanged -= OnPresetsChanged;
        Loc.LanguageChanged -= OnLanguageChanged;
    }
}

/// <summary>Pages that want to refresh when shown.</summary>
public interface INavigationAware
{
    void OnNavigatedTo();

    /// <summary>Called when the page is left or the window is hidden (stop live data here).</summary>
    void OnNavigatedFrom()
    {
    }
}
