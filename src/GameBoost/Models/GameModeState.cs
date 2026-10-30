namespace GameBoost.Models;

/// <summary>
/// Runtime state of game mode. Written to disk during activation so that power plan and
/// notifications can be restored after a crash or power loss.
/// </summary>
public sealed class GameModeState
{
    public bool IsActive { get; set; }
    public string? ProfileId { get; set; }
    public string? ProfileName { get; set; }
    public DateTime ActivatedAt { get; set; }
    public bool Automatic { get; set; }

    public bool PowerSchemeChanged { get; set; }
    public string? PreviousPowerScheme { get; set; }

    public bool PowerOverlayChanged { get; set; }
    public Guid PreviousPowerOverlay { get; set; }

    public bool NotificationsChanged { get; set; }
    public Dictionary<string, int?> PreviousNotificationValues { get; set; } = new();

    /// <summary>Full paths of the closed programs (for restarting them).</summary>
    public List<string> KilledPrograms { get; set; } = new();
}

public enum StatusKind
{
    Ok,
    Info,
    Warning,
    Error
}

public sealed record StatusEntry(StatusKind Kind, string Text);
