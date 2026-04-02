using Dnp.Core;

namespace Dnp.Transport.Linux;

public static class LinuxPrinterDetector
{
    public static PrinterDetectionResult Detect(string? explicitDevicePath = null, string? modelHint = null)
    {
        if (!OperatingSystem.IsLinux())
        {
            return new PrinterDetectionResult(
                Success: false,
                QueryReady: false,
                Transport: "linux",
                QueryArgumentName: "--device",
                QueryValue: null,
                Port: null,
                PrinterName: null,
                DevicePath: null,
                Model: null,
                ProductName: null,
                Manufacturer: null,
                LoadedDllPath: null,
                VendorId: null,
                ProductId: null,
                SerialNumber: null,
                BusNumber: null,
                DeviceAddress: null,
                MatchReason: "Linux detection can only run on Linux.",
                UsbInstanceId: null,
                UsbPrintInstanceId: null);
        }

        var preferredDevice = !string.IsNullOrWhiteSpace(explicitDevicePath)
            ? explicitDevicePath!.Trim()
            : Environment.GetEnvironmentVariable("DNP_PRINTER_DEVICE")?.Trim();

        var preferredHint = DnpModelResolver.Normalize(modelHint);
        var devices = LinuxUsbDeviceCatalog.Enumerate(preferredDevice);

        foreach (var device in devices)
        {
            if (!device.LooksLikeDnp(preferredHint))
            {
                continue;
            }

            var queryValue = device.BuildQueryValue();
            var model = DnpModelResolver.TryDetectFromText(device.ProductName, device.Manufacturer, preferredHint);
            var printerName = device.ProductName ?? model;
            var reason = string.Equals(device.DevicePath, preferredDevice, StringComparison.OrdinalIgnoreCase)
                ? "Matched explicit Linux printer device."
                : queryValue.StartsWith("usb:", StringComparison.OrdinalIgnoreCase)
                    ? "Matched Linux USB metadata and built a stable query selector."
                    : "Matched Linux USB product information.";

            return new PrinterDetectionResult(
                Success: true,
                QueryReady: true,
                Transport: "linux",
                QueryArgumentName: "--device",
                QueryValue: queryValue,
                Port: null,
                PrinterName: printerName,
                DevicePath: device.DevicePath,
                Model: model,
                ProductName: device.ProductName,
                Manufacturer: device.Manufacturer,
                LoadedDllPath: null,
                VendorId: device.VendorId,
                ProductId: device.ProductId,
                SerialNumber: device.SerialNumber,
                BusNumber: device.BusNumber,
                DeviceAddress: device.DeviceAddress,
                MatchReason: reason,
                UsbInstanceId: null,
                UsbPrintInstanceId: null);
        }

        return new PrinterDetectionResult(
            Success: false,
            QueryReady: false,
            Transport: "linux",
            QueryArgumentName: "--device",
            QueryValue: null,
            Port: null,
            PrinterName: null,
            DevicePath: preferredDevice,
            Model: DnpModelResolver.TryDetectFromText(preferredHint),
            ProductName: null,
            Manufacturer: null,
            LoadedDllPath: null,
            VendorId: null,
            ProductId: null,
            SerialNumber: null,
            BusNumber: null,
            DeviceAddress: null,
            MatchReason: "No DNP/Citizen USB printer could be identified from Linux devices.",
            UsbInstanceId: null,
            UsbPrintInstanceId: null);
    }
}
