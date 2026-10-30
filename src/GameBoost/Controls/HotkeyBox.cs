using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GameBoost.Infrastructure;
using GameBoost.Models;

namespace GameBoost.Controls;

/// <summary>
/// Eingabefeld zum Aufnehmen einer Tastenkombination.
/// Entf/Rücktaste = Hotkey entfernen, Esc = abbrechen.
/// </summary>
public sealed class HotkeyBox : TextBox
{
    public static readonly DependencyProperty HotkeyProperty = DependencyProperty.Register(
        nameof(Hotkey), typeof(HotkeyBinding), typeof(HotkeyBox),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((HotkeyBox)d).UpdateText()));

    /// <summary>true, solange irgendeine HotkeyBox Eingaben aufnimmt (globale Hotkeys werden dann pausiert).</summary>
    public static event EventHandler<bool>? CaptureStateChanged;

    public HotkeyBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        IsUndoEnabled = false;
        Cursor = Cursors.Hand;
        ContextMenu = null;
        Loc.LanguageChanged += (_, _) => UpdateText();
        Loaded += (_, _) => UpdateText();
    }

    public HotkeyBinding? Hotkey
    {
        get => (HotkeyBinding?)GetValue(HotkeyProperty);
        set => SetValue(HotkeyProperty, value);
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        Text = Loc.T("Hotkey.Press");
        CaptureStateChanged?.Invoke(this, true);
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        UpdateText();
        CaptureStateChanged?.Invoke(this, false);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;

        if (key == Key.Escape)
        {
            EndCapture();
            return;
        }

        if ((key == Key.Delete || key == Key.Back) && modifiers == ModifierKeys.None)
        {
            Hotkey = null;
            EndCapture();
            return;
        }

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.ImeProcessed or Key.DeadCharProcessed or Key.None)
        {
            Text = DescribeModifiers(modifiers) + "…";
            return;
        }

        var isFunctionKey = key is >= Key.F1 and <= Key.F24;
        if (modifiers == ModifierKeys.None && !isFunctionKey)
        {
            // Einzelne Buchstaben ohne Modifier würden das Tippen in anderen Programmen blockieren.
            Text = Loc.T("Hotkey.NeedModifier");
            return;
        }

        var mods = 0;
        if (modifiers.HasFlag(ModifierKeys.Alt)) mods |= HotkeyBinding.ModAlt;
        if (modifiers.HasFlag(ModifierKeys.Control)) mods |= HotkeyBinding.ModControl;
        if (modifiers.HasFlag(ModifierKeys.Shift)) mods |= HotkeyBinding.ModShift;
        if (modifiers.HasFlag(ModifierKeys.Windows)) mods |= HotkeyBinding.ModWin;

        Hotkey = HotkeyBinding.Create(mods, key);
        EndCapture();
    }

    private void EndCapture()
    {
        Keyboard.ClearFocus();
        var scope = FocusManager.GetFocusScope(this);
        FocusManager.SetFocusedElement(scope, null);
        UpdateText();
    }

    private static string DescribeModifiers(ModifierKeys modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add(Loc.T("Key.Ctrl"));
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add(Loc.T("Key.Alt"));
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add(Loc.T("Key.Shift"));
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add(Loc.T("Key.Win"));
        return parts.Count == 0 ? "" : string.Join(" + ", parts) + " + ";
    }

    private void UpdateText()
    {
        if (IsKeyboardFocused) return;
        Text = Hotkey?.IsValid == true ? Hotkey.ToString() : Loc.T("Hotkey.None");
    }
}
