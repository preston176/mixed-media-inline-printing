using MixedMediaPrint.Core.Calibration;
using Xunit;

namespace MixedMediaPrint.Tests.Calibration;

public class JsonFileCalibrationStoreTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "mmp-calibration-tests-" + Guid.NewGuid());

    [Fact]
    public void Load_UnknownPrinter_ReturnsNull()
    {
        var store = new JsonFileCalibrationStore(_tempDir);

        Assert.Null(store.Load("Some Printer That Was Never Saved"));
    }

    [Fact]
    public void SaveThenLoad_RoundTripsExactly()
    {
        var store = new JsonFileCalibrationStore(_tempDir);
        PrinterCalibrationProfile profile = RealConfirmedProfile();

        store.Save(profile);
        PrinterCalibrationProfile? loaded = store.Load(profile.PrinterQueueName);

        Assert.NotNull(loaded);
        // Compared field-by-field rather than via Assert.Equal(profile, loaded):
        // records compare IReadOnlyList<T>-typed properties by reference (no
        // structural IEquatable for the interface type), so a freshly
        // deserialized list is never "equal" to the original by the record's
        // own Equals even when its contents genuinely match.
        Assert.Equal(profile.PrinterQueueName, loaded.PrinterQueueName);
        Assert.Equal(profile.DeviceFingerprint, loaded.DeviceFingerprint);
        Assert.Equal(profile.RotationDegrees, loaded.RotationDegrees);
        Assert.Equal(profile.LastVerifiedUtc, loaded.LastVerifiedUtc);
        Assert.Equal(profile.LastVerificationResult, loaded.LastVerificationResult);
        Assert.Equal(profile.Scenarios, loaded.Scenarios);
    }

    [Fact]
    public void Save_PrinterNameWithSpacesAndPunctuation_DoesNotThrow()
    {
        var store = new JsonFileCalibrationStore(_tempDir);
        var profile = new PrinterCalibrationProfile("SHARP BP-71C65 PCL6", null, 90f, []);

        store.Save(profile);

        Assert.NotNull(store.Load("SHARP BP-71C65 PCL6"));
    }

    [Fact]
    public void ListKnownPrinters_NoProfilesSavedYet_ReturnsEmpty()
    {
        var store = new JsonFileCalibrationStore(_tempDir);

        Assert.Empty(store.ListKnownPrinters());
    }

    [Fact]
    public void ListKnownPrinters_AfterSaving_ReturnsTheQueueName()
    {
        var store = new JsonFileCalibrationStore(_tempDir);
        store.Save(RealConfirmedProfile());

        Assert.Contains("SHARP BP-71C65 PCL6", store.ListKnownPrinters());
    }

    /// <summary>
    /// The two real, hardware-confirmed scenarios from legacy-testkit/README.md,
    /// as recorded in IMPLEMENTATION_PLAN.md — not invented placeholder data.
    /// </summary>
    private static PrinterCalibrationProfile RealConfirmedProfile() => new(
        PrinterQueueName: "SHARP BP-71C65 PCL6",
        DeviceFingerprint: new DeviceFingerprint(DpiX: 600, DpiY: 600, HorzRes: 4960, VertRes: 6496),
        RotationDegrees: 90f,
        Scenarios:
        [
            new CalibrationScenario(
                TabTrayPattern: "(?i)tray\\s*1", BodyTrayPattern: "(?i)tray\\s*2",
                FlipX: false, FlipY: false, NudgeXIn: -0.625, NudgeYIn: 0.0,
                ConfirmedOnPaper: true, Note: "README.md 'this one worked' -- TabNumber 3, default trays"),
            new CalibrationScenario(
                TabTrayPattern: "(?i)bypass", BodyTrayPattern: "(?i)tray\\s*1",
                FlipX: false, FlipY: true, NudgeXIn: 0.0, NudgeYIn: 0.0,
                ConfirmedOnPaper: true, Note: "README.md -- TabNumber 3, Text 'EMAIL CORRESPONDENCE'"),
        ],
        LastVerifiedUtc: new DateTimeOffset(2026, 8, 17, 0, 0, 0, TimeSpan.Zero),
        LastVerificationResult: "PASS-PERPAGE-TRAY");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
