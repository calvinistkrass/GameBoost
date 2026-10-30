using System.Windows;
using GameBoost.Infrastructure;
using GameBoost.Models;

namespace GameBoost.Services;

/// <summary>
/// Erkennt den Start eines Spiels (.exe) und aktiviert das zugehörige Profil automatisch.
/// Wird das Spiel beendet, kann der Gaming-Modus automatisch wieder deaktiviert werden.
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
    /// Nach manuellem Deaktivieren darf das Profil nicht sofort wieder automatisch starten,
    /// solange das Spiel noch läuft.
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
            Logger.Error("Prozessüberwachung fehlgeschlagen", ex);
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
                    Logger.Info($"Spiel für Profil '{active.Name}' beendet – Gaming-Modus wird automatisch deaktiviert");
                    _ = _controller.DeactivateGameModeAsync(manual: false);
                }
                return;
            }

            if (!settings.AutoProfileSwitch) return;

            foreach (var profile in settings.Profiles)
            {
                if (profile.Id == _suppressedProfileId) continue;
                if (!AnyTriggerRunning(profile, running)) continue;

                Logger.Info($"Spiel erkannt – Profil '{profile.Name}' wird automatisch aktiviert");
                _ = _controller.ActivateGameModeAsync(profile, automatic: true);
                return;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Auswertung der Spielerkennung fehlgeschlagen", ex);
        }
    }

    private static bool AnyTriggerRunning(GameModeProfile profile, HashSet<string> running)
        => profile.TriggerExecutables.Any(running.Contains);

    public void Dispose() => Stop();
}
