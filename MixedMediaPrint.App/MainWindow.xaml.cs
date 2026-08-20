using System.IO;
using System.Printing;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using MixedMediaPrint.Core;
using MixedMediaPrint.Core.Pdf;

namespace MixedMediaPrint.App;

// All UI presentation lives in web/ (HTML/CSS/JS) now. This class is purely a bridge:
// it answers JSON requests from the page by calling the same PrintEngine the previous
// WPF-controls version called, and posts JSON events back (log lines, tray discovery,
// prepared-job summaries). It holds no UI state of its own -- the page owns that, including
// the page order; the one exception is _loadedPdfPath, since the page never sees a real
// filesystem path, only the file name and rendered thumbnails.
public partial class MainWindow : Window
{
    private readonly PrintEngine _engine;
    private PrintJobRequest? _lastRequest;
    private PreparedJob? _lastPrepared;
    private string? _loadedPdfPath;

    public MainWindow()
    {
        InitializeComponent();

        string templatePath = Path.Combine(AppContext.BaseDirectory, "Templates", "5th-cut-1-to-500.docx");
        string workDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MixedMediaPrint", "work");
        _engine = new PrintEngine(templatePath, workDir);
        _engine.Log += line => PostMessage(new { type = "log", line });

        Loaded += async (_, _) => await InitializeWebViewAsync();
    }

    private async Task InitializeWebViewAsync()
    {
        await WebView.EnsureCoreWebView2Async();

        string webRoot = Path.Combine(AppContext.BaseDirectory, "web");
        WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "mixedmediaprint.local", webRoot, CoreWebView2HostResourceAccessKind.Allow);
        WebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        WebView.CoreWebView2.Navigate("https://mixedmediaprint.local/index.html");
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        JsonElement msg;
        string type;
        try
        {
            msg = JsonSerializer.Deserialize<JsonElement>(e.WebMessageAsJson);
            type = msg.GetProperty("type").GetString() ?? "";
        }
        catch (Exception ex)
        {
            PostMessage(new { type = "log", line = $"Malformed message from the page: {ex.Message}" });
            return;
        }

        try
        {
            switch (type)
            {
                case "loadPrinters": HandleLoadPrinters(); break;
                case "refreshTrays": HandleRefreshTrays(msg); break;
                case "pickPdf": HandlePickPdf(); break;
                case "prepare": HandlePrepare(msg); break;
                case "print": HandlePrint(); break;
            }
        }
        catch (Exception ex)
        {
            PostMessage(new { type = "log", line = $"Unexpected error handling '{type}': {ex.Message}" });
        }
    }

    private void HandleLoadPrinters()
    {
        try
        {
            using var server = new LocalPrintServer();
            var names = server.GetPrintQueues()
                .Select(q => q.FullName)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
            string? preferred = names.FirstOrDefault(n => n.Contains("BP-71C65", StringComparison.OrdinalIgnoreCase))
                ?? names.FirstOrDefault();

            PostMessage(new { type = "printers", names, preferred });
        }
        catch (Exception ex)
        {
            PostMessage(new { type = "printersError", message = ex.Message });
        }
    }

    private void HandleRefreshTrays(JsonElement msg)
    {
        string printer = msg.GetProperty("printer").GetString() ?? "";
        string tabPattern = msg.GetProperty("tabTrayPattern").GetString() ?? "";
        string bodyPattern = msg.GetProperty("bodyTrayPattern").GetString() ?? "";
        if (string.IsNullOrWhiteSpace(printer) || string.IsNullOrWhiteSpace(tabPattern) || string.IsNullOrWhiteSpace(bodyPattern))
            return;

        try
        {
            var discovery = _engine.DiscoverTrays(printer, tabPattern, bodyPattern);
            PostMessage(new
            {
                type = "trayDiscovery",
                tabTrayName = discovery.Resolved.TabTrayName,
                tabTrayId = discovery.Resolved.TabTrayId,
                bodyTrayName = discovery.Resolved.BodyTrayName,
                bodyTrayId = discovery.Resolved.BodyTrayId,
            });
        }
        catch (Exception ex)
        {
            PostMessage(new { type = "trayError", message = ex.Message });
        }
    }

    private void HandlePickPdf()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PDF files (*.pdf)|*.pdf",
            Title = "Load body PDF",
        };
        if (dialog.ShowDialog(this) != true)
        {
            PostMessage(new { type = "pdfPickCancelled" });
            return;
        }

        try
        {
            byte[] pdfBytes = File.ReadAllBytes(dialog.FileName);
            int pageCount = PdfPageRenderer.GetPageCount(pdfBytes);
            var thumbnails = new string[pageCount];
            for (int i = 0; i < pageCount; i++)
                thumbnails[i] = Convert.ToBase64String(PdfPageRenderer.RenderThumbnailPng(pdfBytes, i, widthPx: 160));

            _loadedPdfPath = dialog.FileName;
            PostMessage(new
            {
                type = "pdfLoaded",
                fileName = Path.GetFileName(dialog.FileName),
                pageCount,
                thumbnails,
            });
        }
        catch (Exception ex)
        {
            PostMessage(new { type = "pdfError", message = ex.Message });
        }
    }

    private void HandlePrepare(JsonElement msg)
    {
        var requestEl = msg.GetProperty("request");
        var request = ParseRequest(requestEl) with { PdfPath = _loadedPdfPath };
        try
        {
            var prepared = _engine.Prepare(request);
            _lastRequest = request;
            _lastPrepared = prepared;

            PostMessage(new
            {
                type = "prepared",
                printer = request.Printer,
                tabNumber = request.TabNumber,
                position = prepared.Position,
                displayText = prepared.DisplayText,
                tabTrayName = prepared.Trays.TabTrayName,
                tabTrayId = prepared.Trays.TabTrayId,
                bodyTrayName = prepared.Trays.BodyTrayName,
                bodyTrayId = prepared.Trays.BodyTrayId,
                copies = request.Copies,
                sectionCount = prepared.Sequence.Count,
            });
        }
        catch (Exception ex)
        {
            PostMessage(new { type = "prepareError", message = ex.Message });
        }
    }

    private void HandlePrint()
    {
        if (_lastRequest is null || _lastPrepared is null)
        {
            PostMessage(new { type = "printError", message = "Nothing prepared yet." });
            return;
        }

        try
        {
            _engine.Print(_lastRequest, _lastPrepared);
            PostMessage(new { type = "printDone" });
        }
        catch (Exception ex)
        {
            PostMessage(new { type = "printError", message = ex.Message });
        }
    }

    private static PrintJobRequest ParseRequest(JsonElement el) => new(
        Printer: el.GetProperty("printer").GetString() ?? "",
        TabNumber: el.GetProperty("tabNumber").GetInt32(),
        Text: el.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null,
        NudgeXIn: el.GetProperty("nudgeXIn").GetDouble(),
        NudgeYIn: el.GetProperty("nudgeYIn").GetDouble(),
        Copies: el.GetProperty("copies").GetInt32(),
        TabTrayPattern: el.GetProperty("tabTrayPattern").GetString() ?? "",
        BodyTrayPattern: el.GetProperty("bodyTrayPattern").GetString() ?? "",
        FlipTabX: el.GetProperty("flipTabX").GetBoolean(),
        FlipTabY: el.GetProperty("flipTabY").GetBoolean(),
        Sequence: ParseSequence(el));

    private static IReadOnlyList<PageSlot>? ParseSequence(JsonElement requestEl)
    {
        if (!requestEl.TryGetProperty("sequence", out var seqEl) || seqEl.ValueKind != JsonValueKind.Array)
            return null;

        var slots = new List<PageSlot>();
        foreach (var item in seqEl.EnumerateArray())
        {
            string kind = item.GetProperty("kind").GetString() ?? "";
            slots.Add(kind switch
            {
                "tab" => new PageSlot(PageSlotKind.Tab),
                "pdf" => new PageSlot(PageSlotKind.BodyPdfPage, item.GetProperty("pageIndex").GetInt32()),
                _ => new PageSlot(PageSlotKind.BodyPlaceholder),
            });
        }
        return slots;
    }

    private void PostMessage(object payload)
    {
        if (WebView.CoreWebView2 is null) return; // page not ready yet (e.g. an early Log line)
        WebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload));
    }
}
