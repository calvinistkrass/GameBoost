using System.Windows.Media;
using GameBoost.Infrastructure;

namespace GameBoost.ViewModels;

/// <summary>A row in the "Close programs" list.</summary>
public sealed class ProcessItemViewModel : ObservableObject
{
    private readonly Action<ProcessItemViewModel> _onSelectionChanged;
    private bool _isSelected;

    public ProcessItemViewModel(string name, string displayName, string? path, int count, long memoryBytes, ImageSource? icon,
        bool isSelected, Action<ProcessItemViewModel> onSelectionChanged)
    {
        Name = name;
        DisplayName = displayName;
        Path = path;
        Count = count;
        MemoryBytes = memoryBytes;
        Icon = icon;
        _isSelected = isSelected;
        _onSelectionChanged = onSelectionChanged;
    }

    /// <summary>Normalized process name (lowercase, without .exe).</summary>
    public string Name { get; }
    public string DisplayName { get; }
    public string? Path { get; }
    public int Count { get; }
    public long MemoryBytes { get; }
    public ImageSource? Icon { get; }
    public bool IsRunning => Count > 0;

    public string InfoText => IsRunning
        ? Loc.F("GM.Proc.Instances", Count, Services.ProcessService.FormatBytes(MemoryBytes))
        : Loc.T("GM.Proc.NotRunning");

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value)) _onSelectionChanged(this);
        }
    }

    /// <summary>Sets the selection without callback (when switching profiles).</summary>
    public void SetSelectedSilently(bool value)
    {
        if (_isSelected == value) return;
        _isSelected = value;
        OnPropertyChanged(nameof(IsSelected));
    }
}
