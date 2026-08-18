using System.IO;
using System.Windows.Input;
using MixedMediaPrint.App.Mvvm;
using MixedMediaPrint.App.Session;
using MixedMediaPrint.Core.Calibration;
using MixedMediaPrint.Core.Execution;
using MixedMediaPrint.Core.JobModel;
using MixedMediaPrint.Core.Rendering;
using Microsoft.Win32;

namespace MixedMediaPrint.App.ViewModels;

/// <summary>
/// Screen 4: the three-tier run-mode selector (Preview / DryRunToFile /
/// Physical) plus the typed-confirmation gate before anything touches paper —
/// carrying forward the same safety pattern every legacy-testkit script used
/// ("Type PRINT to send to the device").
/// </summary>
public sealed class RunViewModel : ViewModelBase
{
    private readonly JobSessionState _session;
    private readonly Action _onBack;

    public int TotalPages { get; }
    public int TabPageCount { get; }
    public int BodyPageCount { get; }

    private RunMode _selectedMode = RunMode.Preview;
    public RunMode SelectedMode
    {
        get => _selectedMode;
        private set
        {
            if (SetProperty(ref _selectedMode, value))
            {
                OnPropertyChanged(nameof(IsPreviewMode));
                OnPropertyChanged(nameof(IsDryRunMode));
                OnPropertyChanged(nameof(IsPhysicalMode));
            }
        }
    }

    public bool IsPreviewMode { get => SelectedMode == RunMode.Preview; set { if (value) { SelectedMode = RunMode.Preview; } } }
    public bool IsDryRunMode { get => SelectedMode == RunMode.DryRunToFile; set { if (value) { SelectedMode = RunMode.DryRunToFile; } } }
    public bool IsPhysicalMode { get => SelectedMode == RunMode.Physical; set { if (value) { SelectedMode = RunMode.Physical; } } }

    private string _outputFilePath = Path.Combine(Path.GetTempPath(), "MixedMediaPrint", "job-dryrun.prn");
    public string OutputFilePath { get => _outputFilePath; set => SetProperty(ref _outputFilePath, value); }

    private string _confirmationText = string.Empty;
    public string ConfirmationText { get => _confirmationText; set => SetProperty(ref _confirmationText, value); }

    private string _resultText = string.Empty;
    public string ResultText { get => _resultText; private set => SetProperty(ref _resultText, value); }

    public ICommand BrowseOutputFileCommand { get; }
    public ICommand RunCommand { get; }
    public ICommand BackCommand { get; }

    public RunViewModel(JobSessionState session, Action onBack)
    {
        _session = session;
        _onBack = onBack;

        PrintJobPlan plan = session.Plan ?? new PrintJobPlan([]);
        IReadOnlyList<PageInstance> pages = JobExpander.Expand(plan);
        TotalPages = pages.Count;
        TabPageCount = pages.Count(p => p.Role == PageRole.Tab);
        BodyPageCount = pages.Count(p => p.Role == PageRole.Body);

        BrowseOutputFileCommand = new RelayCommand(BrowseOutputFile);
        RunCommand = new AsyncRelayCommand(RunAsync, CanRun);
        BackCommand = new RelayCommand(_onBack);
    }

    private bool CanRun() => SelectedMode switch
    {
        RunMode.Preview => true,
        RunMode.DryRunToFile => !string.IsNullOrWhiteSpace(OutputFilePath),
        RunMode.Physical => ConfirmationText == "PRINT",
        _ => false,
    };

    private void BrowseOutputFile()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Print job capture (*.prn)|*.prn|All files (*.*)|*.*",
            FileName = Path.GetFileName(OutputFilePath),
        };
        if (dialog.ShowDialog() == true)
        {
            OutputFilePath = dialog.FileName;
        }
    }

    private async Task RunAsync()
    {
        if (_session.Plan is null || string.IsNullOrWhiteSpace(_session.PrinterName) || _session.SelectedScenario is null)
        {
            ResultText = "Missing printer, scenario, or job plan — go back and complete the earlier steps.";
            return;
        }

        PrintJobPlan plan = _session.Plan;
        string printer = _session.PrinterName;
        CalibrationScenario scenario = _session.SelectedScenario;
        float rotation = _session.RotationDegrees;
        string? pdfPath = _session.PdfPath;
        RunMode mode = SelectedMode;
        string outputFile = OutputFilePath;

        if (mode == RunMode.Preview)
        {
            ResultText = await Task.Run(() => DescribePlan(plan));
            return;
        }

        if (string.IsNullOrWhiteSpace(pdfPath) && plan.Items.OfType<BodyRangeItem>().Any())
        {
            ResultText = "The job plan references body pages but no PDF was loaded.";
            return;
        }

        ResultText = mode == RunMode.Physical ? "Printing..." : "Writing dry-run file...";
        try
        {
            await Task.Run(() =>
            {
                using IPdfPageSource pdfSource = string.IsNullOrWhiteSpace(pdfPath)
                    ? EmptyPdfPageSource.Instance
                    : PdfiumPdfPageSource.FromFile(pdfPath);

                var options = new PrintEngine.Options(
                    printer, scenario.TabTrayPattern, scenario.BodyTrayPattern, rotation,
                    scenario.NudgeXIn, scenario.NudgeYIn, scenario.FlipX, scenario.FlipY);

                if (mode == RunMode.DryRunToFile)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(outputFile) is { Length: > 0 } dir ? dir : ".");
                }

                PrintEngine.Run(plan, pdfSource, options, mode, mode == RunMode.DryRunToFile ? outputFile : null);
            });

            ResultText = mode == RunMode.Physical
                ? $"Sent {TotalPages} page(s) to '{printer}'."
                : $"Wrote {outputFile} ({TotalPages} page(s), no paper used).";
        }
        catch (Exception ex)
        {
            ResultText = $"FAILED: {ex.Message}";
        }
    }

    private static string DescribePlan(PrintJobPlan plan)
    {
        IReadOnlyList<PageInstance> pages = JobExpander.Expand(plan);
        var lines = new List<string> { $"{pages.Count} page(s), in order:" };
        for (int i = 0; i < pages.Count; i++)
        {
            PageInstance page = pages[i];
            lines.Add(page.Role == PageRole.Body
                ? $"  {i + 1}. BODY — PDF page {page.BodyPageIndex + 1}"
                : $"  {i + 1}. TAB — #{page.TabNumber} \"{page.LabelText}\"");
        }
        return string.Join(Environment.NewLine, lines);
    }
}
