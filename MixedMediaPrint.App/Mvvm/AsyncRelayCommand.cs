using System.Windows.Input;

namespace MixedMediaPrint.App.Mvvm;

/// <summary>For commands that call into PrintEngine/PDFium/GDI, which can block for real time (rendering a multi-page PDF, spooling a job). Disables itself while running so a slow operation can't be started twice.</summary>
public sealed class AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    private bool _isExecuting;

    public bool CanExecute(object? parameter) => !_isExecuting && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        _isExecuting = true;
        RaiseCanExecuteChanged();
        try
        {
            await execute();
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    private static void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
}
