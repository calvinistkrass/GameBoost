namespace GameBoost.Models;

/// <summary>
/// Laufzeitzustand des Gaming-Modus. Wird während der Aktivierung auf die Platte geschrieben,
/// damit nach einem Absturz/Stromausfall Energieplan und Benachrichtigungen wiederhergestellt werden können.
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

    /// <summary>Vollständige Pfade der beendeten Programme (für den Neustart).</summary>
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
