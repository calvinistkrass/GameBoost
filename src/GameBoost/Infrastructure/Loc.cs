using System.Globalization;
using System.Windows;

namespace GameBoost.Infrastructure;

/// <summary>
/// Lokalisierung (Deutsch/Englisch). Texte stehen hier zentral nebeneinander, damit keine Übersetzung fehlt.
/// XAML nutzt {DynamicResource Schlüssel}, Code nutzt Loc.T("Schlüssel").
/// </summary>
public static class Loc
{
    private static ResourceDictionary? _current;

    public static string Language { get; private set; } = "de";

    public static event EventHandler? LanguageChanged;

    public static IReadOnlyDictionary<string, (string De, string En)> Strings => Texts;

    public static void Apply(string language)
    {
        Language = language == "en" ? "en" : "de";
        try
        {
            CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo(Language == "en" ? "en-US" : "de-DE");

            var app = Application.Current;
            if (app != null)
            {
                var dict = new ResourceDictionary();
                foreach (var (key, value) in Texts) dict[key] = Language == "en" ? value.En : value.De;

                var merged = app.Resources.MergedDictionaries;
                if (_current != null) merged.Remove(_current);
                merged.Add(dict);
                _current = dict;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Sprache konnte nicht angewendet werden", ex);
        }

        try
        {
            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Logger.Error("Fehler in LanguageChanged-Handler", ex);
        }
    }

    public static string T(string key)
        => Texts.TryGetValue(key, out var v) ? (Language == "en" ? v.En : v.De) : key;

    public static string F(string key, params object[] args)
    {
        try
        {
            return string.Format(CultureInfo.CurrentCulture, T(key), args);
        }
        catch (FormatException)
        {
            return T(key);
        }
    }

    private static readonly Dictionary<string, (string De, string En)> Texts = new()
    {
        // ---------------------------------------------------------- Allgemein / Navigation
        ["App.Tagline"] = ("Gaming-Modus & Crosshair", "Game mode & crosshair"),
        ["Nav.GameMode"] = ("Gaming-Modus", "Game Mode"),
        ["Nav.Crosshair"] = ("Crosshair", "Crosshair"),
        ["Nav.Settings"] = ("Einstellungen", "Settings"),
        ["Status.Title"] = ("STATUS", "STATUS"),
        ["Status.On"] = ("AN", "ON"),
        ["Status.Off"] = ("AUS", "OFF"),
        ["Caption.Minimize"] = ("Minimieren", "Minimize"),
        ["Caption.Maximize"] = ("Maximieren", "Maximize"),
        ["Caption.Close"] = ("Schließen", "Close"),
        ["Err.Generic"] = ("Ein unerwarteter Fehler ist aufgetreten. Details stehen in der Logdatei.", "An unexpected error occurred. Details are in the log file."),

        // ---------------------------------------------------------- Gaming-Modus
        ["GM.Title"] = ("Gaming-Modus", "Game Mode"),
        ["GM.Subtitle"] = ("Mehr Leistung, weniger Ablenkung – mit einem Klick.", "More performance, fewer distractions – with one click."),
        ["GM.Toggle.Title"] = ("GAMING-MODUS", "GAME MODE"),
        ["GM.Toggle.Tooltip"] = ("Gaming-Modus ein- oder ausschalten", "Turn game mode on or off"),
        ["GM.Profile"] = ("PROFIL", "PROFILE"),
        ["GM.NewProfile"] = ("Neues Profil", "New profile"),
        ["GM.RenameProfile"] = ("Profil umbenennen", "Rename profile"),
        ["GM.DuplicateProfile"] = ("Profil duplizieren", "Duplicate profile"),
        ["GM.DeleteProfile"] = ("Profil löschen", "Delete profile"),
        ["GM.HotkeyHint"] = ("Tipp: Per Hotkey (siehe Einstellungen) lässt sich der Gaming-Modus auch im Spiel umschalten.", "Tip: Use the hotkey (see Settings) to toggle game mode while in-game."),
        ["GM.Tab.Kill"] = ("Programme beenden", "Close programs"),
        ["GM.Tab.Start"] = ("Programme starten", "Start programs"),
        ["GM.Tab.Options"] = ("Optionen", "Options"),
        ["GM.Tab.Auto"] = ("Automatik", "Automation"),
        ["GM.Search"] = ("Programme suchen…", "Search programs…"),
        ["GM.SelectNone"] = ("Auswahl aufheben", "Clear selection"),
        ["GM.Refresh"] = ("Aktualisieren", "Refresh"),
        ["GM.Loading"] = ("Prozesse werden geladen…", "Loading processes…"),
        ["GM.Kill.SafetyHint"] = ("Angezeigt werden nur Programme deines Benutzerkontos. System-, Windows-, Sicherheits- und Anti-Cheat-Prozesse sind geschützt und werden nie beendet. Ungespeicherte Daten in beendeten Programmen gehen verloren.",
                                  "Only programs of your user account are shown. System, Windows, security and anti-cheat processes are protected and never closed. Unsaved data in closed programs will be lost."),
        ["GM.Start.Hint"] = ("Diese Programme werden beim Aktivieren gestartet, z. B. Discord, Steam oder ein Spiel-Launcher.", "These programs are started on activation, e.g. Discord, Steam or a game launcher."),
        ["GM.Start.Add"] = ("Programm hinzufügen", "Add program"),
        ["GM.Start.Empty"] = ("Noch keine Programme hinzugefügt.", "No programs added yet."),
        ["GM.Remove"] = ("Entfernen", "Remove"),
        ["GM.Opt.Power"] = ("Energieplan „Höchstleistung“ aktivieren", "Activate “High performance” power plan"),
        ["GM.Opt.PowerDesc"] = ("Der vorherige Energieplan wird gemerkt und beim Deaktivieren wiederhergestellt.", "The previous power plan is remembered and restored on deactivation."),
        ["GM.Opt.Notif"] = ("Windows-Benachrichtigungen deaktivieren", "Disable Windows notifications"),
        ["GM.Opt.NotifDesc"] = ("Unterdrückt Toast-Benachrichtigungen während des Spielens.", "Suppresses toast notifications while gaming."),
        ["GM.Opt.Restart"] = ("Beendete Programme beim Deaktivieren neu starten", "Restart closed programs on deactivation"),
        ["GM.Opt.RestartDesc"] = ("Startet die Programme, die beim Aktivieren beendet wurden, automatisch wieder.", "Automatically restarts the programs that were closed on activation."),
        ["GM.Opt.Crosshair"] = ("Crosshair automatisch einblenden", "Show crosshair automatically"),
        ["GM.Opt.CrosshairDesc"] = ("Blendet das Crosshair-Overlay ein, solange der Gaming-Modus aktiv ist.", "Shows the crosshair overlay while game mode is active."),
        ["GM.Opt.CrosshairPreset"] = ("Preset:", "Preset:"),
        ["GM.Opt.CurrentCrosshair"] = ("(aktuelles Crosshair)", "(current crosshair)"),
        ["GM.Auto.Desc"] = ("Startet eines dieser Programme, wird dieses Profil automatisch aktiviert. Gib den Namen der Spiel-.exe ein (z. B. „VALORANT-Win64-Shipping.exe“) oder wähle sie aus. Tipp: Den Namen findest du im Task-Manager unter „Details“, während das Spiel läuft.",
                            "When one of these programs starts, this profile is activated automatically. Enter the game's .exe name (e.g. “VALORANT-Win64-Shipping.exe”) or browse for it. Tip: Task Manager → “Details” shows the name while the game is running."),
        ["GM.Auto.GlobalOff"] = ("Die automatische Profilaktivierung ist in den Einstellungen ausgeschaltet.", "Automatic profile activation is turned off in Settings."),
        ["GM.Auto.Placeholder"] = ("z. B. FortniteClient-Win64-Shipping.exe", "e.g. FortniteClient-Win64-Shipping.exe"),
        ["GM.Auto.AddName"] = ("Hinzufügen", "Add"),
        ["GM.Auto.Browse"] = ("Durchsuchen…", "Browse…"),
        ["GM.Auto.Deactivate"] = ("Gaming-Modus beenden, wenn das Spiel geschlossen wird", "Turn off game mode when the game closes"),
        ["GM.Auto.DeactivateDesc"] = ("Gilt nur, wenn das Profil automatisch aktiviert wurde.", "Only applies if the profile was activated automatically."),
        ["GM.Auto.Empty"] = ("Noch keine Spiele hinterlegt.", "No games added yet."),
        ["GM.Auto.Protected"] = ("„{0}“ ist ein geschützter Systemprozess und kann nicht als Auslöser verwendet werden.", "“{0}” is a protected system process and cannot be used as a trigger."),
        ["GM.Status.Busy"] = ("Wird ausgeführt…", "Working…"),
        ["GM.Status.Working"] = ("Bitte einen Moment Geduld…", "Please wait a moment…"),
        ["GM.Status.ActiveHeadline"] = ("Gaming-Modus aktiv", "Game mode active"),
        ["GM.Status.ReadyHeadline"] = ("Bereit", "Ready"),
        ["GM.Status.ActiveSince"] = ("Profil „{0}“ · aktiv seit {1} Uhr{2}", "Profile “{0}” · active since {1}{2}"),
        ["GM.Status.AutoSuffix"] = (" · automatisch gestartet", " · started automatically"),
        ["GM.Status.ReadySub"] = ("Beim Aktivieren von „{0}“ passiert Folgendes:", "Activating “{0}” will do the following:"),
        ["GM.Status.NothingToDo"] = ("Aktiv – für dieses Profil sind keine Aktionen eingestellt.", "Active – no actions configured for this profile."),
        ["GM.Plan.Kill"] = ("{0} Programm(e) werden beendet", "{0} program(s) will be closed"),
        ["GM.Plan.KillNone"] = ("Es werden keine Programme beendet", "No programs will be closed"),
        ["GM.Plan.Power"] = ("Energieplan → Höchstleistung", "Power plan → High performance"),
        ["GM.Plan.Notif"] = ("Windows-Benachrichtigungen werden deaktiviert", "Windows notifications will be disabled"),
        ["GM.Plan.Start"] = ("{0} Programm(e) werden gestartet", "{0} program(s) will be started"),
        ["GM.Plan.Crosshair"] = ("Crosshair wird eingeblendet", "Crosshair will be shown"),
        ["GM.Plan.Auto"] = ("Startet automatisch mit: {0}", "Starts automatically with: {0}"),
        ["GM.Proc.Instances"] = ("{0}× · {1}", "{0}× · {1}"),
        ["GM.Proc.NotRunning"] = ("läuft gerade nicht", "not running"),
        ["GM.Proc.SelectedCount"] = ("{0} ausgewählt", "{0} selected"),

        // ---------------------------------------------------------- Gaming-Modus: Ergebnisse
        ["GMS.Unexpected"] = ("Unerwarteter Fehler – Details im Log", "Unexpected error – see log"),
        ["GMS.PowerSet"] = ("Energieplan: Höchstleistung aktiv", "Power plan: High performance active"),
        ["GMS.PowerAlready"] = ("Energieplan war bereits Höchstleistung", "Power plan was already High performance"),
        ["GMS.PowerOverlay"] = ("Energiemodus: Beste Leistung aktiv", "Power mode: Best performance active"),
        ["GMS.PowerFailed"] = ("Energieplan konnte nicht umgestellt werden", "Power plan could not be changed"),
        ["GMS.NotifOff"] = ("Windows-Benachrichtigungen deaktiviert", "Windows notifications disabled"),
        ["GMS.NotifFailed"] = ("Benachrichtigungen konnten nicht deaktiviert werden", "Notifications could not be disabled"),
        ["GMS.Killed"] = ("{0} beendet", "{0} closed"),
        ["GMS.KillNotRunning"] = ("{0} lief nicht", "{0} was not running"),
        ["GMS.KillProtected"] = ("{0} ist geschützt und wurde übersprungen", "{0} is protected and was skipped"),
        ["GMS.KillFailed"] = ("{0} konnte nicht beendet werden", "{0} could not be closed"),
        ["GMS.Started"] = ("{0} gestartet", "{0} started"),
        ["GMS.StartAlreadyRunning"] = ("{0} läuft bereits", "{0} is already running"),
        ["GMS.StartMissing"] = ("{0} wurde nicht gefunden", "{0} was not found"),
        ["GMS.StartFailed"] = ("{0} konnte nicht gestartet werden", "{0} could not be started"),

        // ---------------------------------------------------------- Dialoge
        ["Dlg.OK"] = ("OK", "OK"),
        ["Dlg.Cancel"] = ("Abbrechen", "Cancel"),
        ["Dlg.Yes"] = ("Ja", "Yes"),
        ["Dlg.No"] = ("Nein", "No"),
        ["Dlg.Info"] = ("Hinweis", "Note"),
        ["Dlg.Error"] = ("Fehler", "Error"),
        ["Dlg.Confirm"] = ("Bestätigen", "Confirm"),
        ["Dlg.NewProfile.Title"] = ("Neues Profil", "New profile"),
        ["Dlg.RenameProfile.Title"] = ("Profil umbenennen", "Rename profile"),
        ["Dlg.ProfileName"] = ("Name des Profils, z. B. „Valorant“:", "Profile name, e.g. “Valorant”:"),
        ["Dlg.Copy"] = ("{0} (Kopie)", "{0} (copy)"),
        ["Dlg.DeleteProfile.Title"] = ("Profil löschen", "Delete profile"),
        ["Dlg.DeleteProfile.Msg"] = ("Profil „{0}“ wirklich löschen?", "Really delete profile “{0}”?"),
        ["Dlg.LastProfile"] = ("Das letzte Profil kann nicht gelöscht werden.", "The last profile cannot be deleted."),
        ["Dlg.ProfileActive"] = ("Das aktive Profil kann nicht gelöscht werden, solange der Gaming-Modus läuft.", "The active profile cannot be deleted while game mode is running."),
        ["Dlg.SelectProgram"] = ("Programm auswählen", "Select program"),
        ["Dlg.SelectGame"] = ("Spiel (.exe) auswählen", "Select game (.exe)"),
        ["Dlg.ExeFilter"] = ("Programme (*.exe;*.lnk)|*.exe;*.lnk|Alle Dateien (*.*)|*.*", "Programs (*.exe;*.lnk)|*.exe;*.lnk|All files (*.*)|*.*"),
        ["Dlg.ExeOnlyFilter"] = ("Programme (*.exe)|*.exe", "Programs (*.exe)|*.exe"),

        // ---------------------------------------------------------- Crosshair
        ["CH.Title"] = ("Crosshair", "Crosshair"),
        ["CH.Subtitle"] = ("Gestalte dein Fadenkreuz und blende es als Overlay über jedem Spiel ein.", "Design your crosshair and show it as an overlay on top of any game."),
        ["CH.Preview"] = ("Vorschau", "Preview"),
        ["CH.Zoom"] = ("Vorschau-Zoom", "Preview zoom"),
        ["CH.Overlay.Show"] = ("Crosshair-Overlay anzeigen", "Show crosshair overlay"),
        ["CH.Overlay.ShowDesc"] = ("Exakt mittig auf dem gewählten Monitor, immer im Vordergrund. Mausklicks gehen durch das Overlay hindurch.", "Exactly centered on the selected monitor, always on top. Mouse clicks pass through the overlay."),
        ["CH.Monitor"] = ("MONITOR", "MONITOR"),
        ["CH.Monitor.Refresh"] = ("Monitore neu erkennen", "Detect monitors again"),
        ["CH.Share"] = ("Teilen", "Share"),
        ["CH.Share.Export"] = ("DEIN CROSSHAIR-CODE", "YOUR CROSSHAIR CODE"),
        ["CH.Share.Copy"] = ("Kopieren", "Copy"),
        ["CH.Share.Import"] = ("CODE IMPORTIEREN", "IMPORT CODE"),
        ["CH.Share.ImportPlaceholder"] = ("GB1-… hier einfügen", "Paste GB1-… here"),
        ["CH.Share.ImportBtn"] = ("Importieren", "Import"),
        ["CH.Share.Copied"] = ("Code in die Zwischenablage kopiert.", "Code copied to clipboard."),
        ["CH.Share.CopyFailed"] = ("Zwischenablage ist gerade nicht verfügbar – bitte erneut versuchen.", "Clipboard is busy – please try again."),
        ["CH.Share.Imported"] = ("Crosshair importiert.", "Crosshair imported."),
        ["CH.Share.Invalid"] = ("Ungültiger Code. Erwartet wird ein GameBoost-Code im Format GB1-…", "Invalid code. Expected a GameBoost code like GB1-…"),
        ["CH.Editor"] = ("Editor", "Editor"),
        ["CH.Reset"] = ("Standard", "Default"),
        ["CH.Style"] = ("STIL", "STYLE"),
        ["CH.Style.Cross"] = ("Kreuz", "Cross"),
        ["CH.Style.Dot"] = ("Punkt", "Dot"),
        ["CH.Style.Circle"] = ("Kreis", "Circle"),
        ["CH.Style.CrossDot"] = ("Kreuz + Punkt", "Cross + dot"),
        ["CH.Style.T"] = ("T-Form", "T-shape"),
        ["CH.Color"] = ("FARBE", "COLOR"),
        ["CH.ColorPick"] = ("Eigene Farbe wählen", "Pick custom color"),
        ["CH.Opacity"] = ("Deckkraft", "Opacity"),
        ["CH.Length"] = ("Länge", "Length"),
        ["CH.Radius"] = ("Radius", "Radius"),
        ["CH.Thickness"] = ("Linienstärke", "Thickness"),
        ["CH.Gap"] = ("Abstand (Gap)", "Gap"),
        ["CH.DotSize"] = ("Punktgröße", "Dot size"),
        ["CH.Outline"] = ("Umrandung (Outline)", "Outline"),
        ["CH.OutlineColor"] = ("Outline-Farbe", "Outline color"),
        ["CH.OutlineThickness"] = ("Outline-Stärke", "Outline width"),
        ["CH.Offset"] = ("FEINTUNING (OFFSET)", "FINE-TUNING (OFFSET)"),
        ["CH.OffsetX"] = ("Offset X", "Offset X"),
        ["CH.OffsetY"] = ("Offset Y", "Offset Y"),
        ["CH.ResetOffset"] = ("Auf 0 setzen", "Reset to 0"),
        ["CH.Presets"] = ("Presets", "Presets"),
        ["CH.Preset.Load"] = ("Laden", "Load"),
        ["CH.Preset.Overwrite"] = ("Überschreiben", "Overwrite"),
        ["CH.Preset.Delete"] = ("Löschen", "Delete"),
        ["CH.Preset.NamePlaceholder"] = ("Name für neues Preset", "Name for new preset"),
        ["CH.Preset.SaveNew"] = ("Als Preset speichern", "Save as preset"),
        ["CH.Preset.Saved"] = ("Preset „{0}“ gespeichert.", "Preset “{0}” saved."),
        ["CH.Preset.OverwriteConfirm"] = ("Preset „{0}“ mit dem aktuellen Crosshair überschreiben?", "Overwrite preset “{0}” with the current crosshair?"),
        ["CH.Preset.DeleteConfirm"] = ("Preset „{0}“ löschen?", "Delete preset “{0}”?"),
        ["CH.Preset.DefaultName"] = ("Preset {0}", "Preset {0}"),
        ["Monitor.Name"] = ("Monitor", "Monitor"),
        ["Monitor.Primary"] = ("Hauptmonitor", "primary"),

        // ---------------------------------------------------------- Hinweise
        ["Hint.Borderless"] = ("Das Overlay funktioniert nur, wenn das Spiel im Modus „Randloses Fenster“ (Borderless/Fenster-Vollbild) läuft – nicht im exklusiven Vollbildmodus.",
                               "The overlay only works when the game runs in “Borderless window” (windowed fullscreen) mode – not in exclusive fullscreen."),
        ["Hint.AntiCheat"] = ("Bei Spielen mit strengem Anti-Cheat (z. B. Valorant/Vanguard, FACEIT, ESEA) oder in Turnieren kann ein Overlay unerwünscht oder verboten sein. Nutzung auf eigene Verantwortung.",
                              "Games with strict anti-cheat (e.g. Valorant/Vanguard, FACEIT, ESEA) or tournaments may disallow overlays. Use at your own risk."),
        ["Hint.Safety"] = ("GameBoost benötigt keine Administratorrechte. Es beendet ausschließlich Programme deines Benutzerkontos, die du selbst ausgewählt hast, und stellt Energieplan und Benachrichtigungen beim Deaktivieren (auch nach einem Absturz) wieder her.",
                           "GameBoost needs no admin rights. It only closes programs of your user account that you selected yourself, and restores power plan and notifications on deactivation (even after a crash)."),

        // ---------------------------------------------------------- Einstellungen
        ["ST.Title"] = ("Einstellungen", "Settings"),
        ["ST.Subtitle"] = ("Sprache, Autostart, Hotkeys und mehr.", "Language, autostart, hotkeys and more."),
        ["ST.General"] = ("Allgemein", "General"),
        ["ST.Language"] = ("Sprache", "Language"),
        ["ST.LanguageDesc"] = ("Wird sofort übernommen.", "Applied immediately."),
        ["ST.Autostart"] = ("Mit Windows starten", "Start with Windows"),
        ["ST.AutostartDesc"] = ("GameBoost startet beim Anmelden minimiert im Infobereich.", "GameBoost starts minimized in the system tray when you sign in."),
        ["ST.StartMin"] = ("Minimiert starten", "Start minimized"),
        ["ST.StartMinDesc"] = ("Beim Start nur das Tray-Symbol anzeigen, ohne Hauptfenster.", "Only show the tray icon on startup, without the main window."),
        ["ST.CloseToTray"] = ("Beim Schließen im Tray weiterlaufen", "Keep running in tray when closed"),
        ["ST.CloseToTrayDesc"] = ("Das X schließt nur das Fenster. Beenden über das Tray-Menü.", "The X only closes the window. Quit via the tray menu."),
        ["ST.AutoProfile"] = ("Profile automatisch aktivieren", "Activate profiles automatically"),
        ["ST.AutoProfileDesc"] = ("Aktiviert ein Profil, sobald eines seiner Spiele startet (Tab „Automatik“).", "Activates a profile as soon as one of its games starts (“Automation” tab)."),
        ["ST.Hotkeys"] = ("Hotkeys", "Hotkeys"),
        ["ST.HotkeysDesc"] = ("Globale Tastenkürzel funktionieren auch im Spiel. Ins Feld klicken und Kombination drücken · Entf = entfernen · Esc = abbrechen.",
                              "Global shortcuts also work in-game. Click the field and press a combination · Del = remove · Esc = cancel."),
        ["ST.Hk.Crosshair"] = ("Crosshair ein/aus", "Toggle crosshair"),
        ["ST.Hk.GameMode"] = ("Gaming-Modus ein/aus", "Toggle game mode"),
        ["ST.Hk.NextPreset"] = ("Nächstes Crosshair-Preset", "Next crosshair preset"),
        ["ST.Hk.Ok"] = ("✓ Aktiv", "✓ Active"),
        ["ST.Hk.Conflict"] = ("⚠ Bereits von einem anderen Programm belegt", "⚠ Already used by another program"),
        ["ST.Hk.None"] = ("Nicht belegt", "Not assigned"),
        ["ST.Hk.Reset"] = ("Standard wiederherstellen", "Restore defaults"),
        ["ST.Notes"] = ("Wichtige Hinweise", "Important notes"),
        ["ST.Data"] = ("Daten & Fehlerbehebung", "Data & troubleshooting"),
        ["ST.DataDesc"] = ("Einstellungen und Logdateien liegen unter", "Settings and log files are stored in"),
        ["ST.OpenData"] = ("Datenordner öffnen", "Open data folder"),
        ["ST.OpenLogs"] = ("Logdateien öffnen", "Open log files"),
        ["ST.Reset"] = ("Alles zurücksetzen", "Reset everything"),
        ["ST.ResetConfirm"] = ("Alle Einstellungen, Profile und Crosshair-Presets werden auf den Auslieferungszustand zurückgesetzt. Eine Sicherung der alten Datei wird im Datenordner abgelegt. Fortfahren?",
                               "All settings, profiles and crosshair presets will be reset to defaults. A backup of the old file is kept in the data folder. Continue?"),
        ["ST.ResetDone"] = ("Alle Einstellungen wurden zurückgesetzt.", "All settings have been reset."),
        ["ST.AutostartFailed"] = ("Autostart konnte nicht geändert werden. Details stehen in der Logdatei.", "Autostart could not be changed. See the log file for details."),
        ["ST.Version"] = ("GameBoost Version {0}", "GameBoost version {0}"),

        // ---------------------------------------------------------- Hotkey-Eingabe
        ["Hotkey.None"] = ("– nicht belegt –", "– not set –"),
        ["Hotkey.Press"] = ("Tastenkombination drücken…", "Press a key combination…"),
        ["Hotkey.NeedModifier"] = ("Bitte mit Strg, Alt, Umschalt oder Win kombinieren", "Please combine with Ctrl, Alt, Shift or Win"),
        ["Key.Ctrl"] = ("Strg", "Ctrl"),
        ["Key.Alt"] = ("Alt", "Alt"),
        ["Key.Shift"] = ("Umschalt", "Shift"),
        ["Key.Win"] = ("Win", "Win"),

        // ---------------------------------------------------------- Tray
        ["Tray.Open"] = ("GameBoost öffnen", "Open GameBoost"),
        ["Tray.GameMode"] = ("Gaming-Modus", "Game mode"),
        ["Tray.Crosshair"] = ("Crosshair", "Crosshair"),
        ["Tray.NextPreset"] = ("Nächstes Crosshair-Preset", "Next crosshair preset"),
        ["Tray.Exit"] = ("Beenden", "Exit"),
        ["Tray.Tooltip.Idle"] = ("GameBoost", "GameBoost"),
        ["Tray.Tooltip.Active"] = ("GameBoost – Gaming-Modus AN ({0})", "GameBoost – game mode ON ({0})"),
        ["Tray.Hint.Title"] = ("GameBoost läuft weiter", "GameBoost is still running"),
        ["Tray.Hint.Text"] = ("GameBoost ist im Infobereich aktiv. Über das Symbol kannst du es öffnen oder beenden.", "GameBoost is running in the system tray. Use the icon to open or quit it."),
        ["Tray.Recovered.Title"] = ("Zustand wiederhergestellt", "State restored"),
        ["Tray.Recovered.Text"] = ("GameBoost wurde zuletzt nicht sauber beendet. Energieplan und Benachrichtigungen wurden zurückgesetzt.", "GameBoost did not shut down cleanly last time. Power plan and notifications have been restored."),
    };
}
