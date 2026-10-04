using Dnp.Core;

namespace Dnp.Transport.Windows;

public static class WindowsPrinterDetector
{
    public static PrinterDetectionResult Detect(
        string? explicitPrinterName = null,
        string? modelHint = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new PrinterDetectionResult(false, false, "windows", "--device", null, null, null, null, null, null, null, null, null, null, null, null, null, "Windows detection can only run on Windows.", null, null);
        }

        var preferredPrinter = !string.IsNullOrWhiteSpace(explicitPrinterName)
            ? explicitPrinterName!.Trim()
            : Environment.GetEnvironmentVariable("DNP_PRINTER_NAME")?.Trim();
        var preferredHint = DnpModelResolver.Normalize(modelHint);
        var usbIdentity = WindowsUsbIdentityProbe.TryFind(preferredPrinter, preferredHint);

        if (usbIdentity is null)
        {
            return new PrinterDetectionResult(false, false, "windows", "--device", null, null, preferredPrinter, null,
                DnpModelResolver.TryDetectFromText(preferredPrinter, preferredHint), null, null, null, null, null, null, null, null,
                "No matching physical USB DNP printer was found. Allowed VID/PID pairs are filtered from the built-in USB catalog.", null, null);
        }

        var transport = new WindowsUsbPrinterTransport(printerName: preferredPrinter, selectionHint: preferredHint);
        var devicePath = transport.ResolveAutoDevicePath();
        var resolvedPrinterName = preferredPrinter ?? usbIdentity.PrinterName;
        var resolvedModel = DnpModelResolver.TryDetectFromText(resolvedPrinterName, preferredHint, usbIdentity.FriendlyName, usbIdentity.DeviceDescription);
        var manufacturer = ResolveManufacturer(resolvedPrinterName, usbIdentity.FriendlyName, usbIdentity.DeviceDescription);
        var queryReady = !string.IsNullOrWhiteSpace(devicePath);
        var reason = queryReady
            ? "Detected physical USB printer and a matching Windows device interface path."
            : "Detected physical DNP USB printer, but no usable Windows device interface path was found yet.";

        return new PrinterDetectionResult(
            Success: true,
            QueryReady: queryReady,
            Transport: "windows",
            QueryArgumentName: "--device",
            QueryValue: devicePath,
            Port: null,
            PrinterName: resolvedPrinterName,
            DevicePath: devicePath,
            Model: resolvedModel,
            ProductName: usbIdentity.FriendlyName ?? resolvedPrinterName,
            Manufacturer: manufacturer,
            LoadedDllPath: null,
            VendorId: usbIdentity.VendorId,
            ProductId: usbIdentity.ProductId,
            SerialNumber: usbIdentity.SerialNumber,
            BusNumber: null,
            DeviceAddress: null,
            MatchReason: reason,
            UsbInstanceId: usbIdentity.UsbInstanceId,
            UsbPrintInstanceId: usbIdentity.UsbPrintInstanceId);
    }

    private static string? ResolveManufacturer(params string?[] values)
    {
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (value.Contains("CITIZEN", StringComparison.OrdinalIgnoreCase))
            {
                return "Citizen";
            }

            if (value.Contains("DNP", StringComparison.OrdinalIgnoreCase) || value.Contains("MITSUBISHI", StringComparison.OrdinalIgnoreCase))
            {
                return "DNP";
            }
        }

        return null;
    }
}
