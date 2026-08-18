using System.Text.Json;

namespace MixedMediaPrint.Core.Calibration;

/// <summary>
/// One JSON file per printer queue name under a directory (default:
/// %AppData%\MixedMediaPrint\calibration on Windows). Pure file I/O + JSON —
/// no OS-specific dependency, testable on macOS with a temp directory.
/// </summary>
public sealed class JsonFileCalibrationStore : ICalibrationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _directory;

    public JsonFileCalibrationStore(string directory)
    {
        _directory = directory;
    }

    public static JsonFileCalibrationStore CreateDefault() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MixedMediaPrint", "calibration"));

    public PrinterCalibrationProfile? Load(string printerQueueName)
    {
        string path = PathFor(printerQueueName);
        if (!File.Exists(path))
        {
            return null;
        }

        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<PrinterCalibrationProfile>(json, JsonOptions);
    }

    public void Save(PrinterCalibrationProfile profile)
    {
        Directory.CreateDirectory(_directory);
        string path = PathFor(profile.PrinterQueueName);
        string json = JsonSerializer.Serialize(profile, JsonOptions);
        File.WriteAllText(path, json);
    }

    public IReadOnlyList<string> ListKnownPrinters()
    {
        if (!Directory.Exists(_directory))
        {
            return [];
        }

        return Directory.GetFiles(_directory, "*.json")
            .Select(f => Uri.UnescapeDataString(Path.GetFileNameWithoutExtension(f)))
            .ToList();
    }

    // Printer queue names can contain spaces/punctuation (e.g. "SHARP BP-71C65
    // PCL6") — escape for a safe filename rather than restricting queue names.
    private string PathFor(string printerQueueName) =>
        Path.Combine(_directory, $"{Uri.EscapeDataString(printerQueueName)}.json");
}
