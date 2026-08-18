using MixedMediaPrint.App.Mvvm;
using MixedMediaPrint.App.Session;
using MixedMediaPrint.Core.Calibration;

namespace MixedMediaPrint.App.ViewModels;

/// <summary>Hosts whichever of the four wizard steps is current; ViewModel-first navigation via a DataTemplate keyed on each ViewModel's type (see App.xaml).</summary>
public sealed class ShellViewModel : ViewModelBase
{
    private readonly JobSessionState _session = new();
    private readonly ICalibrationStore _calibrationStore;

    private object _currentViewModel;
    public object CurrentViewModel { get => _currentViewModel; private set => SetProperty(ref _currentViewModel, value); }

    public ShellViewModel(ICalibrationStore calibrationStore)
    {
        _calibrationStore = calibrationStore;
        _currentViewModel = new PrinterSetupViewModel(_session, _calibrationStore, NavigateToCalibration);
    }

    private void NavigateToPrinterSetup() =>
        CurrentViewModel = new PrinterSetupViewModel(_session, _calibrationStore, NavigateToCalibration);

    private void NavigateToCalibration() =>
        CurrentViewModel = new CalibrationViewModel(_session, _calibrationStore, NavigateToJobSetup, NavigateToPrinterSetup);

    private void NavigateToJobSetup() =>
        CurrentViewModel = new JobSetupViewModel(_session, NavigateToRun, NavigateToCalibration);

    private void NavigateToRun() =>
        CurrentViewModel = new RunViewModel(_session, NavigateToJobSetup);
}
