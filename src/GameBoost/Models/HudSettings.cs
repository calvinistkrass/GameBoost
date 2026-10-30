using GameBoost.Infrastructure;

namespace GameBoost.Models;

public enum HudPosition
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

/// <summary>Settings of the in-game performance overlay (HUD).</summary>
public sealed class HudSettings : ObservableObject
{
    private bool _visible;
    private HudPosition _position = HudPosition.TopLeft;
    private int _scale = 100;
    private int _backgroundOpacity = 70;
    private bool _showFps = true;
    private bool _showGpu = true;
    private bool _showGpuTemperature = true;
    private bool _showVram = true;
    private bool _showCpu = true;
    private bool _showRam = true;
    private bool _showClock;
    private bool _showWithGameMode = true;
    private bool _onlyOverFullscreen;

    /// <summary>Was the HUD visible when the app last exited?</summary>
    public bool Visible { get => _visible; set => SetProperty(ref _visible, value); }

    public HudPosition Position
    {
        get => _position;
        set => SetProperty(ref _position, Enum.IsDefined(value) ? value : HudPosition.TopLeft);
    }

    /// <summary>Size in percent (75–200).</summary>
    public int Scale { get => _scale; set => SetProperty(ref _scale, Math.Clamp(value, 75, 200)); }

    /// <summary>Background opacity in percent (0–100).</summary>
    public int BackgroundOpacity { get => _backgroundOpacity; set => SetProperty(ref _backgroundOpacity, Math.Clamp(value, 0, 100)); }

    public bool ShowFps { get => _showFps; set => SetProperty(ref _showFps, value); }
    public bool ShowGpu { get => _showGpu; set => SetProperty(ref _showGpu, value); }
    public bool ShowGpuTemperature { get => _showGpuTemperature; set => SetProperty(ref _showGpuTemperature, value); }
    public bool ShowVram { get => _showVram; set => SetProperty(ref _showVram, value); }
    public bool ShowCpu { get => _showCpu; set => SetProperty(ref _showCpu, value); }
    public bool ShowRam { get => _showRam; set => SetProperty(ref _showRam, value); }
    public bool ShowClock { get => _showClock; set => SetProperty(ref _showClock, value); }

    /// <summary>Show the HUD automatically while game mode is active.</summary>
    public bool ShowWithGameMode { get => _showWithGameMode; set => SetProperty(ref _showWithGameMode, value); }

    /// <summary>Hide the HUD while no fullscreen/borderless game is in the foreground.</summary>
    public bool OnlyOverFullscreen { get => _onlyOverFullscreen; set => SetProperty(ref _onlyOverFullscreen, value); }
}
