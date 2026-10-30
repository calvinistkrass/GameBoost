using System.Windows.Interop;
using GameBoost.Infrastructure;
using GameBoost.Models;

namespace GameBoost.Services;

public enum HotkeyRegistrationState
{
    None,
    Registered,
    Conflict
}

/// <summary>Registriert globale Hotkeys über RegisterHotKey auf einem unsichtbaren Nachrichtenfenster.</summary>
public sealed class HotkeyService : IDisposable
{
    private static readonly HotkeyAction[] AllActions = Enum.GetValues<HotkeyAction>();

    private readonly Dictionary<HotkeyAction, HotkeyRegistrationState> _states = new();
    private readonly HashSet<int> _registeredIds = new();
    private HwndSource? _source;
    private HotkeySettings? _current;
    private bool _suspended;

    public IReadOnlyDictionary<HotkeyAction, HotkeyRegistrationState> States => _states;

    public event EventHandler<HotkeyAction>? HotkeyPressed;
    public event EventHandler? StatesChanged;

    public void Initialize()
    {
        var parameters = new HwndSourceParameters("GameBoostHotkeyWindow")
        {
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE: reines Nachrichtenfenster
            Width = 0,
            Height = 0,
            WindowStyle = 0
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public void Apply(HotkeySettings settings)
    {
        _current = settings;
        Reregister();
    }

    /// <summary>Während der Nutzer einen Hotkey neu belegt, werden alle Hotkeys vorübergehend freigegeben.</summary>
    public bool Suspended
    {
        get => _suspended;
        set
        {
            if (_suspended == value) return;
            _suspended = value;
            if (value) UnregisterAll();
            else Reregister();
        }
    }

    private void Reregister()
    {
        UnregisterAll();
        if (_suspended || _current == null || _source == null) return;

        foreach (var action in AllActions)
        {
            var binding = _current.Get(action);
            if (binding == null || !binding.IsValid)
            {
                _states[action] = HotkeyRegistrationState.None;
                continue;
            }

            var ok = NativeMethods.RegisterHotKey(_source.Handle, (int)action,
                (uint)binding.Modifiers | NativeMethods.MOD_NOREPEAT, (uint)binding.Key);
            if (ok)
            {
                _registeredIds.Add((int)action);
                _states[action] = HotkeyRegistrationState.Registered;
            }
            else
            {
                _states[action] = HotkeyRegistrationState.Conflict;
                Logger.Warn($"Hotkey {binding} für {action} konnte nicht registriert werden (bereits belegt?)");
            }
        }

        try
        {
            StatesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Logger.Error("Fehler in StatesChanged-Handler", ex);
        }
    }

    private void UnregisterAll()
    {
        if (_source == null) return;
        foreach (var id in _registeredIds) NativeMethods.UnregisterHotKey(_source.Handle, id);
        _registeredIds.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != NativeMethods.WM_HOTKEY) return IntPtr.Zero;

        var id = wParam.ToInt32();
        if (Enum.IsDefined(typeof(HotkeyAction), id))
        {
            handled = true;
            try
            {
                HotkeyPressed?.Invoke(this, (HotkeyAction)id);
            }
            catch (Exception ex)
            {
                Logger.Error($"Fehler bei Hotkey-Aktion {(HotkeyAction)id}", ex);
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        try
        {
            UnregisterAll();
            _source?.RemoveHook(WndProc);
            _source?.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Warn("Hotkeys konnten nicht freigegeben werden", ex);
        }
        _source = null;
    }
}
