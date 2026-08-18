namespace MixedMediaPrint.Core.Calibration;

public interface ICalibrationStore
{
    PrinterCalibrationProfile? Load(string printerQueueName);
    void Save(PrinterCalibrationProfile profile);
    IReadOnlyList<string> ListKnownPrinters();
}
