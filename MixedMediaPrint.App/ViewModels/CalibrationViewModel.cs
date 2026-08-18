using System.IO;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using MixedMediaPrint.App.Mvvm;
using MixedMediaPrint.App.Session;
using MixedMediaPrint.Core.Calibration;
using MixedMediaPrint.Core.Execution;
using MixedMediaPrint.Core.JobModel;
using MixedMediaPrint.Core.Printing.Gdi;
using MixedMediaPrint.Core.Rendering;

namespace MixedMediaPrint.App.ViewModels;

/// <summary>
/// Screen 2: live preview + tuning for one scenario's rotation/nudge/flip, and a
/// "print one real test tab" action — replaces manually re-running
/// legacy-testkit/capture-gdi-tabpos.ps1 with different -NudgeXIn/-FlipTabY
/// values and eyeballing the printed sheet each time.
/// </summary>
public sealed class CalibrationViewModel : ViewModelBase
{
    private readonly JobSessionState _session;
    private readonly ICalibrationStore _calibrationStore;
    private readonly Action _onContinue;
    private readonly Action _onBack;
    private readonly CalibrationScenario _originalScenario;

    private int _tabNumber = 1;
    public int TabNumber { get => _tabNumber; set { if (SetProperty(ref _tabNumber, value)) UpdatePreview(); } }

    private string _labelText = "1";
    public string LabelText { get => _labelText; set { if (SetProperty(ref _labelText, value)) UpdatePreview(); } }

    private float _rotationDegrees;
    public float RotationDegrees { get => _rotationDegrees; set { if (SetProperty(ref _rotationDegrees, value)) UpdatePreview(); } }

    private double _nudgeXIn;
    public double NudgeXIn { get => _nudgeXIn; set { if (SetProperty(ref _nudgeXIn, value)) UpdatePreview(); } }

    private double _nudgeYIn;
    public double NudgeYIn { get => _nudgeYIn; set { if (SetProperty(ref _nudgeYIn, value)) UpdatePreview(); } }

    private bool _flipX;
    public bool FlipX { get => _flipX; set { if (SetProperty(ref _flipX, value)) UpdatePreview(); } }

    private bool _flipY;
    public bool FlipY { get => _flipY; set { if (SetProperty(ref _flipY, value)) UpdatePreview(); } }

    private BitmapImage? _previewImage;
    public BitmapImage? PreviewImage { get => _previewImage; private set => SetProperty(ref _previewImage, value); }

    private string? _statusMessage;
    public string? StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

    public ICommand PrintTestTabDryRunCommand { get; }
    public ICommand SaveAndContinueCommand { get; }
    public ICommand BackCommand { get; }

    public CalibrationViewModel(JobSessionState session, ICalibrationStore calibrationStore, Action onContinue, Action onBack)
    {
        _session = session;
        _calibrationStore = calibrationStore;
        _onContinue = onContinue;
        _onBack = onBack;

        _originalScenario = session.SelectedScenario
            ?? throw new InvalidOperationException($"{nameof(CalibrationViewModel)} requires a scenario selected in the previous step.");
        _rotationDegrees = session.RotationDegrees;
        _nudgeXIn = _originalScenario.NudgeXIn;
        _nudgeYIn = _originalScenario.NudgeYIn;
        _flipX = _originalScenario.FlipX;
        _flipY = _originalScenario.FlipY;

        PrintTestTabDryRunCommand = new AsyncRelayCommand(PrintTestTabDryRunAsync);
        SaveAndContinueCommand = new RelayCommand(SaveAndContinue);
        BackCommand = new RelayCommand(_onBack);

        UpdatePreview();
    }

    private void UpdatePreview()
    {
        DeviceInfo? device = _session.DeviceInfo;
        if (device is null || string.IsNullOrWhiteSpace(LabelText))
        {
            return;
        }

        try
        {
            byte[] png = TabPreviewRenderer.RenderToPng(TabNumber, LabelText, device, NudgeXIn, NudgeYIn, FlipX, FlipY, RotationDegrees);
            var bitmap = new BitmapImage();
            using var stream = new MemoryStream(png);
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            PreviewImage = bitmap;
            StatusMessage = null;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Preview failed: {ex.Message}";
        }
    }

    private async Task PrintTestTabDryRunAsync()
    {
        if (string.IsNullOrWhiteSpace(_session.PrinterName))
        {
            StatusMessage = "No printer selected.";
            return;
        }

        string printer = _session.PrinterName;
        var options = new PrintEngine.Options(
            printer, _originalScenario.TabTrayPattern, _originalScenario.BodyTrayPattern,
            RotationDegrees, NudgeXIn, NudgeYIn, FlipX, FlipY);
        var plan = new PrintJobPlan([new TabRunItem(TabNumber, LabelTextOverride: LabelText)]);
        string outputFile = Path.Combine(Path.GetTempPath(), "MixedMediaPrint", "calibration", $"tab{TabNumber}-test.prn");

        StatusMessage = "Printing test tab to file...";
        try
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
                PrintEngine.Run(plan, EmptyPdfPageSource.Instance, options, RunMode.DryRunToFile, outputFile);
            });
            StatusMessage = $"Wrote {outputFile}. This scenario has a body tray configured but this test only used the tab page, so the body tray was resolved but never drawn on.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Test print failed: {ex.Message}";
        }
    }

    private void SaveAndContinue()
    {
        if (string.IsNullOrWhiteSpace(_session.PrinterName) || _session.DeviceInfo is null)
        {
            return;
        }

        var updatedScenario = _originalScenario with
        {
            FlipX = FlipX,
            FlipY = FlipY,
            NudgeXIn = NudgeXIn,
            NudgeYIn = NudgeYIn,
        };

        // ReferenceEquals, not != : CalibrationScenario is a record, so != means
        // value equality. If two scenarios ever happened to share identical field
        // values, a value-equality filter would drop both instead of just the one
        // being edited here.
        List<CalibrationScenario> scenarios = (_session.CalibrationProfile?.Scenarios ?? [])
            .Where(s => !ReferenceEquals(s, _originalScenario))
            .Append(updatedScenario)
            .ToList();

        DeviceInfo device = _session.DeviceInfo;
        var profile = new PrinterCalibrationProfile(
            _session.PrinterName,
            new DeviceFingerprint(device.DpiX, device.DpiY, device.HorzRes, device.VertRes),
            RotationDegrees,
            scenarios);

        _calibrationStore.Save(profile);
        _session.CalibrationProfile = profile;
        _session.SelectedScenario = updatedScenario;
        _session.RotationDegrees = RotationDegrees;

        _onContinue();
    }
}
