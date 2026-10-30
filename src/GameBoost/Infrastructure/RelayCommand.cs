using System.Windows.Input;

namespace GameBoost.Infrastructure;

/// <summary>ICommand-Implementierung, die alle Ausnahmen abfängt und protokolliert.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute == null ? null : _ => canExecute())
    {
    }

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter)
    {
        try
        {
            return _canExecute?.Invoke(parameter) ?? true;
        }
        catch (Exception ex)
        {
            Logger.Error("CanExecute fehlgeschlagen", ex);
            return false;
        }
    }

    public void Execute(object? parameter)
    {
        try
        {
            _execute(parameter);
        }
        catch (Exception ex)
        {
            ErrorReporter.Report("Befehl fehlgeschlagen", ex);
        }
    }
}

/// <summary>Asynchroner Befehl; ist während der Ausführung deaktiviert.</summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<object?, bool>? _canExecute;
    private bool _isRunning;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute == null ? null : _ => canExecute())
    {
    }

    public AsyncRelayCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter)
    {
        if (_isRunning) return false;
        try
        {
            return _canExecute?.Invoke(parameter) ?? true;
        }
        catch (Exception ex)
        {
            Logger.Error("CanExecute fehlgeschlagen", ex);
            return false;
        }
    }

    public async void Execute(object? parameter)
    {
        if (_isRunning) return;
        _isRunning = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            await _execute(parameter);
        }
        catch (Exception ex)
        {
            ErrorReporter.Report("Asynchroner Befehl fehlgeschlagen", ex);
        }
        finally
        {
            _isRunning = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
