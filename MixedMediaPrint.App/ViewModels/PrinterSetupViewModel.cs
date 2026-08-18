using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using MixedMediaPrint.App.Mvvm;
using MixedMediaPrint.App.Session;
using MixedMediaPrint.Core.Calibration;
using MixedMediaPrint.Core.Execution;
using MixedMediaPrint.Core.Printing.Diagnostics;
using MixedMediaPrint.Core.Printing.Gdi;

namespace MixedMediaPrint.App.ViewModels;

/// <summary>
/// Screen 1: pick a printer, see its live capabilities, and manage the
/// (tab-tray, body-tray) scenarios calibrated for it. Replaces manually typing
/// -Printer/-TabTrayPattern/-BodyTrayPattern on every PowerShell invocation.
/// </summary>
public sealed class PrinterSetupViewModel : ViewModelBase
{
    private readonly JobSessionState _session;
    private readonly ICalibrationStore _calibrationStore;
    private readonly Action _onContinue;

    public ObservableCollection<string> Printers { get; } = [];
    public ObservableCollection<CapabilityOption> Bins { get; } = [];
    public ObservableCollection<CapabilityOption> MediaTypes { get; } = [];
    public ObservableCollection<CalibrationScenario> Scenarios { get; } = [];

    private string? _selectedPrinter;
    public string? SelectedPrinter
    {
        get => _selectedPrinter;
        set
        {
            if (SetProperty(ref _selectedPrinter, value))
            {
                OnPrinterSelected();
            }
        }
    }

    private DeviceInfo? _deviceInfo;
    public DeviceInfo? DeviceInfo { get => _deviceInfo; private set => SetProperty(ref _deviceInfo, value); }

    private string? _fingerprintWarning;
    public string? FingerprintWarning { get => _fingerprintWarning; private set => SetProperty(ref _fingerprintWarning, value); }

    private CalibrationScenario? _selectedScenario;
    public CalibrationScenario? SelectedScenario { get => _selectedScenario; set => SetProperty(ref _selectedScenario, value); }

    private string _newTabTrayPattern = "(?i)tray\\s*1";
    public string NewTabTrayPattern { get => _newTabTrayPattern; set => SetProperty(ref _newTabTrayPattern, value); }

    private string _newBodyTrayPattern = "(?i)tray\\s*2";
    public string NewBodyTrayPattern { get => _newBodyTrayPattern; set => SetProperty(ref _newBodyTrayPattern, value); }

    private bool _newFlipX;
    public bool NewFlipX { get => _newFlipX; set => SetProperty(ref _newFlipX, value); }

    private bool _newFlipY;
    public bool NewFlipY { get => _newFlipY; set => SetProperty(ref _newFlipY, value); }

    private double _newNudgeXIn;
    public double NewNudgeXIn { get => _newNudgeXIn; set => SetProperty(ref _newNudgeXIn, value); }

    private double _newNudgeYIn;
    public double NewNudgeYIn { get => _newNudgeYIn; set => SetProperty(ref _newNudgeYIn, value); }

    private string _selfTestOutput = string.Empty;
    public string SelfTestOutput { get => _selfTestOutput; private set => SetProperty(ref _selfTestOutput, value); }

    private string? _errorMessage;
    public string? ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }

    public ICommand RefreshPrintersCommand { get; }
    public ICommand AddScenarioCommand { get; }
    public ICommand RemoveScenarioCommand { get; }
    public ICommand RunSelfTestCommand { get; }
    public ICommand ContinueCommand { get; }

    public PrinterSetupViewModel(JobSessionState session, ICalibrationStore calibrationStore, Action onContinue)
    {
        _session = session;
        _calibrationStore = calibrationStore;
        _onContinue = onContinue;

        RefreshPrintersCommand = new RelayCommand(RefreshPrinters);
        AddScenarioCommand = new RelayCommand(AddScenario, () => !string.IsNullOrWhiteSpace(SelectedPrinter));
        RemoveScenarioCommand = new RelayCommand<CalibrationScenario>(RemoveScenario);
        RunSelfTestCommand = new AsyncRelayCommand(RunSelfTestAsync, () => SelectedScenario is not null && !string.IsNullOrWhiteSpace(SelectedPrinter));
        ContinueCommand = new RelayCommand(Continue, () => SelectedScenario is not null && !string.IsNullOrWhiteSpace(SelectedPrinter));

        RefreshPrinters();
    }

    private void RefreshPrinters()
    {
        ErrorMessage = null;
        Printers.Clear();
        try
        {
            foreach (string name in InstalledPrinters.List())
            {
                Printers.Add(name);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not list printers: {ex.Message}";
        }
    }

    private void OnPrinterSelected()
    {
        ErrorMessage = null;
        Bins.Clear();
        MediaTypes.Clear();
        Scenarios.Clear();
        DeviceInfo = null;
        FingerprintWarning = null;

        if (string.IsNullOrWhiteSpace(SelectedPrinter))
        {
            return;
        }

        try
        {
            foreach (CapabilityOption bin in PrinterCapabilities.GetBins(SelectedPrinter))
            {
                Bins.Add(bin);
            }
            foreach (CapabilityOption media in PrinterCapabilities.GetMediaTypes(SelectedPrinter))
            {
                MediaTypes.Add(media);
            }
            DeviceInfo = GdiDeviceInfoReader.Read(SelectedPrinter);
            _session.DeviceInfo = DeviceInfo;

            PrinterCalibrationProfile? profile = _calibrationStore.Load(SelectedPrinter);
            if (profile is not null)
            {
                foreach (CalibrationScenario scenario in profile.Scenarios)
                {
                    Scenarios.Add(scenario);
                }
                _session.RotationDegrees = profile.RotationDegrees;
                FingerprintWarning = DescribeFingerprintMismatch(profile.DeviceFingerprint, DeviceInfo);
            }

            _session.PrinterName = SelectedPrinter;
            _session.CalibrationProfile = profile;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not read this printer's capabilities: {ex.Message}";
        }
    }

    private static string? DescribeFingerprintMismatch(DeviceFingerprint? saved, DeviceInfo live)
    {
        if (saved is null)
        {
            return null;
        }
        bool matches = saved.DpiX == live.DpiX && saved.DpiY == live.DpiY
            && saved.HorzRes == live.HorzRes && saved.VertRes == live.VertRes;
        return matches
            ? null
            : "This printer's DPI/imageable area no longer matches the saved calibration — it may be a " +
              "different physical device (see legacy-testkit/errors.md ERR-3) or a driver/config change. " +
              "Re-verify the scenarios below before trusting them.";
    }

    private void AddScenario()
    {
        if (string.IsNullOrWhiteSpace(SelectedPrinter))
        {
            return;
        }

        var scenario = new CalibrationScenario(NewTabTrayPattern, NewBodyTrayPattern, NewFlipX, NewFlipY, NewNudgeXIn, NewNudgeYIn);
        Scenarios.Add(scenario);
        SelectedScenario = scenario;
        SaveProfile();
    }

    private void RemoveScenario(CalibrationScenario? scenario)
    {
        if (scenario is null)
        {
            return;
        }

        Scenarios.Remove(scenario);
        if (ReferenceEquals(SelectedScenario, scenario))
        {
            SelectedScenario = null;
        }
        SaveProfile();
    }

    private void SaveProfile()
    {
        if (string.IsNullOrWhiteSpace(SelectedPrinter) || DeviceInfo is null)
        {
            return;
        }

        var profile = new PrinterCalibrationProfile(
            SelectedPrinter,
            new DeviceFingerprint(DeviceInfo.DpiX, DeviceInfo.DpiY, DeviceInfo.HorzRes, DeviceInfo.VertRes),
            _session.RotationDegrees,
            Scenarios.ToList());
        _calibrationStore.Save(profile);
        _session.CalibrationProfile = profile;
    }

    private async Task RunSelfTestAsync()
    {
        if (SelectedScenario is null || string.IsNullOrWhiteSpace(SelectedPrinter))
        {
            return;
        }

        string printer = SelectedPrinter;
        CalibrationScenario scenario = SelectedScenario;
        SelfTestOutput = "Running...";

        string outcome;
        try
        {
            // Heavy/blocking work off the UI thread; property writes below happen
            // after the await, back on the UI thread's synchronization context.
            outcome = await Task.Run(() =>
            {
                IReadOnlyList<CapabilityOption> bins = PrinterCapabilities.GetBins(printer);
                CapabilityOption tabBin = TrayResolver.Resolve(bins, scenario.TabTrayPattern);
                CapabilityOption bodyBin = TrayResolver.Resolve(bins, scenario.BodyTrayPattern);
                string dir = Path.Combine(Path.GetTempPath(), "MixedMediaPrint", "selftest");
                PerPageTraySelfTestResult result = PerPageTraySelfTest.Run(printer, (short)tabBin.Id, (short)bodyBin.Id, dir);
                return $"Verdict: {result.Verdict}. Bytes differing vs all-A: {result.DiffCountMixedVsA}, vs all-B: {result.DiffCountMixedVsB}.";
            });
        }
        catch (Exception ex)
        {
            outcome = $"Self-test failed: {ex.Message}";
        }

        SelfTestOutput = outcome;
    }

    private void Continue()
    {
        if (SelectedScenario is null)
        {
            return;
        }
        _session.SelectedScenario = SelectedScenario;
        _onContinue();
    }
}
