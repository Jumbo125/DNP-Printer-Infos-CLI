using System.Globalization;
using Dnp.Core;

namespace Dnp.Transport.Linux;

public sealed record LinuxUsbDeviceInfo(
    string DevicePath,
    string? ProductName,
    string? Manufacturer,
    string? VendorId,
    string? ProductId,
    string? SerialNumber,
    int? BusNumber,
    int? DeviceAddress)
{
    public string BuildQueryValue()
    {
        if (!string.IsNullOrWhiteSpace(VendorId) && !string.IsNullOrWhiteSpace(ProductId))
        {
            var selector = $"usb:vid={VendorId!.ToLowerInvariant()},pid={ProductId!.ToLowerInvariant()}";
            if (!string.IsNullOrWhiteSpace(SerialNumber))
            {
                return selector + ",serial=" + SerialNumber;
            }

            if (BusNumber.HasValue && DeviceAddress.HasValue)
            {
                return selector
                    + ",bus=" + BusNumber.Value.ToString("D3", CultureInfo.InvariantCulture)
                    + ",addr=" + DeviceAddress.Value.ToString("D3", CultureInfo.InvariantCulture);
            }

            return selector;
        }

        return DevicePath;
    }

    public bool LooksLikeDnp(string? modelHint = null)
    {
        return string.Equals(VendorId, "1343", StringComparison.OrdinalIgnoreCase)
               || DnpModelResolver.IsPotentialDnpPrinterText(ProductName)
               || DnpModelResolver.IsPotentialDnpPrinterText(Manufacturer)
               || DnpModelResolver.MatchesHint(modelHint, ProductName, Manufacturer, DevicePath, SerialNumber);
    }

    public bool MatchesSelector(LinuxUsbDeviceSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        if (!string.IsNullOrWhiteSpace(selector.DevicePath)
            && !string.Equals(DevicePath, selector.DevicePath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(selector.VendorId)
            && !string.Equals(VendorId, selector.VendorId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(selector.ProductId)
            && !string.Equals(ProductId, selector.ProductId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(selector.SerialNumber)
            && !string.Equals(SerialNumber, selector.SerialNumber, StringComparison.Ordinal))
        {
            return false;
        }

        if (selector.BusNumber.HasValue && BusNumber != selector.BusNumber)
        {
            return false;
        }

        if (selector.DeviceAddress.HasValue && DeviceAddress != selector.DeviceAddress)
        {
            return false;
        }

        return true;
    }
}

public sealed record LinuxUsbDeviceSelector(
    string? DevicePath,
    string? VendorId,
    string? ProductId,
    string? SerialNumber,
    int? BusNumber,
    int? DeviceAddress)
{
    public static bool TryParse(string? text, out LinuxUsbDeviceSelector? selector)
    {
        selector = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (!trimmed.StartsWith("usb:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string? devicePath = null;
        string? vendorId = null;
        string? productId = null;
        string? serialNumber = null;
        int? busNumber = null;
        int? deviceAddress = null;

        foreach (var part in trimmed[4..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var index = part.IndexOf('=');
            if (index <= 0 || index >= part.Length - 1)
            {
                continue;
            }

            var key = part[..index].Trim().ToLowerInvariant();
            var value = part[(index + 1)..].Trim();
            if (value.Length == 0)
            {
                continue;
            }

            switch (key)
            {
                case "path":
                    devicePath = value;
                    break;
                case "vid":
                    vendorId = NormalizeHex(value);
                    break;
                case "pid":
                    productId = NormalizeHex(value);
                    break;
                case "serial":
                    serialNumber = value;
                    break;
                case "bus":
                    busNumber = ParseInt(value);
                    break;
                case "addr":
                case "device":
                case "dev":
                    deviceAddress = ParseInt(value);
                    break;
            }
        }

        selector = new LinuxUsbDeviceSelector(devicePath, vendorId, productId, serialNumber, busNumber, deviceAddress);
        return true;
    }

    private static int? ParseInt(string text)
        => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static string NormalizeHex(string value)
        => value.Trim().TrimStart('0').Length == 0
            ? "0000"
            : value.Trim().TrimStart('0').PadLeft(4, '0').ToLowerInvariant();
}

public static class LinuxUsbDeviceCatalog
{
    private static readonly string[] DefaultDeviceCandidates =
    [
        "/dev/usb/lp0",
        "/dev/usb/lp1",
        "/dev/usb/lp2",
        "/dev/usb/lp3",
        "/dev/lp0",
        "/dev/lp1",
        "/dev/lp2"
    ];

    public static IReadOnlyList<LinuxUsbDeviceInfo> Enumerate(string? preferredDevicePath = null)
    {
        var results = new List<LinuxUsbDeviceInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in GetDeviceCandidates(preferredDevicePath))
        {
            if (!seen.Add(path))
            {
                continue;
            }

            results.Add(ReadUsbDeviceInfo(path));
        }

        return results;
    }

    public static LinuxUsbDeviceInfo? Resolve(string? queryValue)
    {
        if (string.IsNullOrWhiteSpace(queryValue))
        {
            return Enumerate().FirstOrDefault();
        }

        var trimmed = queryValue.Trim();
        if (!LinuxUsbDeviceSelector.TryParse(trimmed, out var selector) || selector is null)
        {
            return File.Exists(trimmed) ? ReadUsbDeviceInfo(trimmed) : null;
        }

        return Enumerate(selector.DevicePath)
            .FirstOrDefault(device => device.MatchesSelector(selector));
    }

    private static IEnumerable<string> GetDeviceCandidates(string? preferredDevicePath)
    {
        if (!string.IsNullOrWhiteSpace(preferredDevicePath) && !preferredDevicePath.Trim().StartsWith("usb:", StringComparison.OrdinalIgnoreCase))
        {
            yield return preferredDevicePath.Trim();
        }

        foreach (var path in DefaultDeviceCandidates)
        {
            if (File.Exists(path))
            {
                yield return path;
            }
        }
    }

    private static LinuxUsbDeviceInfo ReadUsbDeviceInfo(string devicePath)
    {
        var baseName = Path.GetFileName(devicePath);
        var candidateRoots = new[]
        {
            Path.Combine("/sys/class/usb", baseName),
            Path.Combine("/sys/class/usblp", baseName)
        };

        foreach (var root in candidateRoots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            var metadata = ReadMetadataFromAncestors(Path.Combine(root, "device"));
            return new LinuxUsbDeviceInfo(
                DevicePath: devicePath,
                ProductName: metadata.ProductName,
                Manufacturer: metadata.Manufacturer,
                VendorId: metadata.VendorId,
                ProductId: metadata.ProductId,
                SerialNumber: metadata.SerialNumber,
                BusNumber: metadata.BusNumber,
                DeviceAddress: metadata.DeviceAddress);
        }

        return new LinuxUsbDeviceInfo(devicePath, null, null, null, null, null, null, null);
    }

    private static UsbMetadata ReadMetadataFromAncestors(string startPath)
    {
        var current = new DirectoryInfo(startPath);

        while (current is not null)
        {
            var product = ReadTextFile(Path.Combine(current.FullName, "product"));
            var manufacturer = ReadTextFile(Path.Combine(current.FullName, "manufacturer"));
            var vendorId = NormalizeHexOrNull(ReadTextFile(Path.Combine(current.FullName, "idVendor")));
            var productId = NormalizeHexOrNull(ReadTextFile(Path.Combine(current.FullName, "idProduct")));
            var serial = ReadTextFile(Path.Combine(current.FullName, "serial"));
            var busNumber = ReadIntFile(Path.Combine(current.FullName, "busnum"));
            var deviceAddress = ReadIntFile(Path.Combine(current.FullName, "devnum"));

            if (product is not null
                || manufacturer is not null
                || vendorId is not null
                || productId is not null
                || serial is not null)
            {
                return new UsbMetadata(product, manufacturer, vendorId, productId, serial, busNumber, deviceAddress);
            }

            current = current.Parent;
        }

        return new UsbMetadata(null, null, null, null, null, null, null);
    }

    private static string? ReadTextFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var value = File.ReadAllText(path).Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    private static int? ReadIntFile(string path)
        => int.TryParse(ReadTextFile(path), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static string? NormalizeHexOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim().TrimStart('0');
        if (trimmed.Length == 0)
        {
            return "0000";
        }

        return trimmed.PadLeft(4, '0').ToLowerInvariant();
    }

    private sealed record UsbMetadata(
        string? ProductName,
        string? Manufacturer,
        string? VendorId,
        string? ProductId,
        string? SerialNumber,
        int? BusNumber,
        int? DeviceAddress);
}
