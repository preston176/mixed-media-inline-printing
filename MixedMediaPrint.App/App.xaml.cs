using System.Windows;
using MixedMediaPrint.App.ViewModels;
using MixedMediaPrint.Core.Calibration;

namespace MixedMediaPrint.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Manual composition root -- small enough app that a DI container would
        // add ceremony without buying much. ICalibrationStore is the one seam
        // worth naming explicitly (JSON-on-disk today, swappable later).
        ICalibrationStore calibrationStore = JsonFileCalibrationStore.CreateDefault();
        var shellViewModel = new ShellViewModel(calibrationStore);

        var mainWindow = new MainWindow(shellViewModel);
        mainWindow.Show();
    }
}
