using System.Windows;
using GameBoost.Infrastructure;
using GameBoost.Models;

namespace GameBoost.Services;

/// <summary>
/// Detects when a game (.exe) starts and activates the matching profile automatically.
/// When the game exits, game mode can be deactivated automatically.
/// </summary>
public sealed class ProcessWatcherService : IDisposable
{
    private readonly AppController _controller;
    private Timer? _timer;
    private int _busy;
    private string? _suppressedProfileId;

    public ProcessWatcherService(AppController controller)
    {
        _controller = controller;
    }

    public void Start()
    {
        _timer ??= new Timer(Tick, null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>
    /// After a manual deactivation the profile must not restart automatically
    /// while the game is still running.
    /// </summary>
    public void SuppressUntilExit(string profileId) => _suppressedProfileId = profileId;

    private void Tick(object? _)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            var names = ProcessService.GetRunningNames();
            Application.Current?.Dispatcher.BeginInvoke(() => Evaluate(names));
        }
        catch (Exception ex)
        {
            Logger.Error("Process monitoring failed", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private void Evaluate(HashSet<string> running)
    {
        try
        {
            var settings = _controller.Settings;
            var gameMode = _controller.GameMode;

            if (_suppressedProfileId != null)
            {
                var suppressed = settings.Profiles.FirstOrDefault(p => p.Id == _suppressedProfileId);
                if (suppressed == null || !AnyTriggerRunning(suppressed, running)) _suppressedProfileId = null;
            }

            if (gameMode.IsBusy) return;

            if (gameMode.IsActive)
            {
                var active = gameMode.ActiveProfile;
                if (gameMode.ActivatedAutomatically && active is { AutoDeactivateOnExit: true } && !AnyTriggerRunning(active, running))
                {
                    Logger.Info($"Game for profile '{active.Name}' exited – deactivating game mode automatically");
                    _ = _controller.DeactivateGameModeAsync(manual: false);
                }
                return;
            }

            if (!settings.AutoProfileSwitch) return;

            foreach (var profile in settings.Profiles)
            {
                if (profile.Id == _suppressedProfileId) continue;
                if (!AnyTriggerRunning(profile, running)) continue;

                Logger.Info($"Game detected – activating profile '{profile.Name}' automatically");
                _ = _controller.ActivateGameModeAsync(profile, automatic: true);
                return;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Game detection evaluation failed", ex);
        }
    }

    private static bool AnyTriggerRunning(GameModeProfile profile, HashSet<string> running)
        => profile.TriggerExecutables.Any(running.Contains);

    public void Dispose() => Stop();
}
