using System.Windows;
using System.Windows.Threading;

namespace MixedMediaPrint.App;

public partial class App : Application
{
    public App()
    {
        // A WinExe has no console -- an unhandled exception here otherwise just kills the
        // process with nothing visible at all (see MainWindow's PresetCombo.SelectedIndex
        // comment for a startup crash that hit exactly this). Show it instead.
        DispatcherUnhandledException += (_, e) =>
        {
            MessageBox.Show(e.Exception.ToString(), "MixedMediaPrint failed to start", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
            Shutdown(1);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            MessageBox.Show(e.ExceptionObject?.ToString() ?? "Unknown error", "MixedMediaPrint crashed", MessageBoxButton.OK, MessageBoxImage.Error);
        };
    }
}
