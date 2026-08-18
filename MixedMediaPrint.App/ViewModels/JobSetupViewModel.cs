using System.Collections.ObjectModel;
using System.Windows.Input;
using MixedMediaPrint.App.Mvvm;
using MixedMediaPrint.App.Session;
using MixedMediaPrint.Core.JobModel;
using MixedMediaPrint.Core.Rendering;
using Microsoft.Win32;

namespace MixedMediaPrint.App.ViewModels;

/// <summary>One job-plan item plus a human-readable description, for display in the ordered list.</summary>
public sealed record JobItemDisplay(PrintJobItem Item, string Description);

/// <summary>
/// Screen 3: load the body PDF and assemble the ordered sequence of body-page
/// ranges and tab runs. Reordering is move-up/move-down rather than drag-and-
/// drop — functionally equivalent, and verifiable without a live UI session on
/// the one machine (Windows) that can actually run this app.
/// </summary>
public sealed class JobSetupViewModel : ViewModelBase
{
    private readonly JobSessionState _session;
    private readonly Action _onContinue;
    private readonly Action _onBack;

    public ObservableCollection<JobItemDisplay> Items { get; } = [];

    private string? _pdfPath;
    public string? PdfPath { get => _pdfPath; private set => SetProperty(ref _pdfPath, value); }

    private int _pdfPageCount;
    public int PdfPageCount { get => _pdfPageCount; private set => SetProperty(ref _pdfPageCount, value); }

    private JobItemDisplay? _selectedItem;
    public JobItemDisplay? SelectedItem { get => _selectedItem; set => SetProperty(ref _selectedItem, value); }

    private int _newBodyFirstPage = 1;
    public int NewBodyFirstPage { get => _newBodyFirstPage; set => SetProperty(ref _newBodyFirstPage, value); }

    private int _newBodyPageCount = 1;
    public int NewBodyPageCount { get => _newBodyPageCount; set => SetProperty(ref _newBodyPageCount, value); }

    private int _newTabFirstNumber = 1;
    public int NewTabFirstNumber { get => _newTabFirstNumber; set => SetProperty(ref _newTabFirstNumber, value); }

    private int _newTabCount = 1;
    public int NewTabCount { get => _newTabCount; set => SetProperty(ref _newTabCount, value); }

    private int _newTabCopies = 1;
    public int NewTabCopies { get => _newTabCopies; set => SetProperty(ref _newTabCopies, value); }

    private string _newTabLabel = string.Empty;
    public string NewTabLabel { get => _newTabLabel; set => SetProperty(ref _newTabLabel, value); }

    private string? _errorMessage;
    public string? ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }

    public ICommand LoadPdfCommand { get; }
    public ICommand AddBodyRangeCommand { get; }
    public ICommand AddTabRunCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand RemoveItemCommand { get; }
    public ICommand ContinueCommand { get; }
    public ICommand BackCommand { get; }

    public JobSetupViewModel(JobSessionState session, Action onContinue, Action onBack)
    {
        _session = session;
        _onContinue = onContinue;
        _onBack = onBack;

        LoadPdfCommand = new RelayCommand(LoadPdf);
        AddBodyRangeCommand = new RelayCommand(AddBodyRange, () => PdfPageCount > 0);
        AddTabRunCommand = new RelayCommand(AddTabRun);
        MoveUpCommand = new RelayCommand<JobItemDisplay>(MoveUp);
        MoveDownCommand = new RelayCommand<JobItemDisplay>(MoveDown);
        RemoveItemCommand = new RelayCommand<JobItemDisplay>(RemoveItem);
        ContinueCommand = new RelayCommand(Continue, () => Items.Count > 0);
        BackCommand = new RelayCommand(_onBack);
    }

    private void LoadPdf()
    {
        ErrorMessage = null;
        var dialog = new OpenFileDialog { Filter = "PDF files (*.pdf)|*.pdf", Title = "Select the body PDF" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            using var source = PdfiumPdfPageSource.FromFile(dialog.FileName);
            PdfPageCount = source.PageCount;
            PdfPath = dialog.FileName;
            _session.PdfPath = dialog.FileName;
            NewBodyFirstPage = 1;
            NewBodyPageCount = PdfPageCount;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not open '{dialog.FileName}': {ex.Message}";
        }
    }

    private void AddBodyRange()
    {
        if (NewBodyFirstPage < 1 || NewBodyPageCount < 1 || NewBodyFirstPage + NewBodyPageCount - 1 > PdfPageCount)
        {
            ErrorMessage = $"Body range must fall within the loaded PDF's {PdfPageCount} page(s).";
            return;
        }

        ErrorMessage = null;
        var item = new BodyRangeItem(NewBodyFirstPage - 1, NewBodyPageCount); // 1-based in the UI, 0-based internally
        Items.Add(new JobItemDisplay(item, Describe(item)));
    }

    private void AddTabRun()
    {
        if (NewTabFirstNumber < 1 || NewTabCount < 1 || NewTabCopies < 1)
        {
            ErrorMessage = "Tab number, count, and copies must all be at least 1.";
            return;
        }

        ErrorMessage = null;
        string? label = string.IsNullOrWhiteSpace(NewTabLabel) ? null : NewTabLabel;
        var item = new TabRunItem(NewTabFirstNumber, NewTabCount, NewTabCopies, label);
        Items.Add(new JobItemDisplay(item, Describe(item)));
    }

    private void MoveUp(JobItemDisplay? item)
    {
        if (item is null)
        {
            return;
        }
        int index = Items.IndexOf(item);
        if (index > 0)
        {
            Items.Move(index, index - 1);
        }
    }

    private void MoveDown(JobItemDisplay? item)
    {
        if (item is null)
        {
            return;
        }
        int index = Items.IndexOf(item);
        if (index >= 0 && index < Items.Count - 1)
        {
            Items.Move(index, index + 1);
        }
    }

    private void RemoveItem(JobItemDisplay? item)
    {
        if (item is not null)
        {
            Items.Remove(item);
        }
    }

    private void Continue()
    {
        _session.Plan = new PrintJobPlan(Items.Select(d => d.Item).ToList());
        _onContinue();
    }

    private static string Describe(PrintJobItem item) => item switch
    {
        BodyRangeItem b => $"Body pages {b.FirstPageIndex + 1}-{b.FirstPageIndex + b.PageCount} ({b.PageCount} page(s))",
        TabRunItem t => $"Tab(s) {t.FirstTabNumber}..{t.FirstTabNumber + t.Count - 1}"
            + (t.CopiesPerTab > 1 ? $" x{t.CopiesPerTab} copies each" : string.Empty)
            + (t.LabelTextOverride is not null ? $" — \"{t.LabelTextOverride}\"" : string.Empty),
        _ => item.ToString() ?? "?",
    };
}
