namespace Dnp.Core;

public sealed record PrinterDetectionResult(
    bool Success,
    bool QueryReady,
    string Transport,
    string? QueryArgumentName,
    string? QueryValue,
    int? Port,
    string? PrinterName,
    string? DevicePath,
    string? Model,
    string? ProductName,
    string? Manufacturer,
    string? LoadedDllPath,
    string? VendorId,
    string? ProductId,
    string? SerialNumber,
    int? BusNumber,
    int? DeviceAddress,
    string? MatchReason,
    string? UsbInstanceId,
    string? UsbPrintInstanceId)
{
    public int? DeviceIndex => Port;
    public string? UsbVid => VendorId;
    public string? UsbPid => ProductId;
}
