using Dnp.Core;

namespace Dnp.Transport.Windows;

public sealed class WindowsUsbPrinterTransport : IDnpTransport
{
    private readonly string? _printerName;
    private readonly string? _selectionHint;
    private readonly string? _explicitDevicePath;
    private readonly int _readChunkSize;
    private readonly int _readTimeoutMs;
    private readonly int _postWriteDelayMs;

    public WindowsUsbPrinterTransport(
        string? printerName = null,
        string? selectionHint = null,
        int? port = null,
        string? dllPath = null,
        string? devicePath = null,
        int readChunkSize = 4096,
        int readTimeoutMs = 5000,
        int postWriteDelayMs = 75)
    {
        _printerName = string.IsNullOrWhiteSpace(printerName) ? null : printerName.Trim();
        _selectionHint = DnpModelResolver.Normalize(selectionHint);
        _explicitDevicePath = string.IsNullOrWhiteSpace(devicePath) ? null : devicePath.Trim();
        _readChunkSize = Math.Max(256, readChunkSize);
        _readTimeoutMs = Math.Max(250, readTimeoutMs);
        _postWriteDelayMs = Math.Max(0, postWriteDelayMs);
    }

    public async Task<byte[]> QueryAsync(DnpCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows raw USB transport can only run on Windows.");
        }

        var devicePath = ResolveDevicePath();
        var fileOptions = new FileStreamOptions
        {
            Access = FileAccess.ReadWrite,
            Mode = FileMode.Open,
            Share = FileShare.ReadWrite,
            BufferSize = _readChunkSize,
            Options = FileOptions.Asynchronous | FileOptions.WriteThrough
        };

        await using var stream = new FileStream(devicePath, fileOptions);
        var requestBytes = command.Encode();
        await stream.WriteAsync(requestBytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

        if (_postWriteDelayMs > 0)
        {
            await Task.Delay(_postWriteDelayMs, cancellationToken).ConfigureAwait(false);
        }

        return await ReadResponseAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    public string? ResolveAutoDevicePath()
    {
        var identity = WindowsUsbIdentityProbe.TryFind(_printerName, _selectionHint);
        return WindowsUsbDevicePathResolver.ResolveDevicePath(identity);
    }

    private string ResolveDevicePath()
    {
        if (!string.IsNullOrWhiteSpace(_explicitDevicePath))
        {
            return _explicitDevicePath!;
        }

        var envPath = Environment.GetEnvironmentVariable("DNP_PRINTER_DEVICE");
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            return envPath.Trim();
        }

        var resolved = ResolveAutoDevicePath();
        if (!string.IsNullOrWhiteSpace(resolved))
        {
            return resolved;
        }

        throw new InvalidOperationException(
            "No Windows USB printer device path could be selected automatically. Pass --device \\\\?\\usb#... or set DNP_PRINTER_DEVICE.");
    }

    private async Task<byte[]> ReadResponseAsync(FileStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[_readChunkSize];
        using var response = new MemoryStream();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytesRead = await stream.ReadAsync(buffer, cancellationToken)
                .AsTask()
                .WaitAsync(TimeSpan.FromMilliseconds(_readTimeoutMs), cancellationToken)
                .ConfigureAwait(false);

            if (bytesRead <= 0)
            {
                if (response.Length == 0)
                {
                    throw new TimeoutException($"No response received from device within {_readTimeoutMs} ms.");
                }

                break;
            }

            response.Write(buffer, 0, bytesRead);

            if (DnpPacketCodec.TryGetExpectedResponseLength(response.GetBuffer().AsSpan(0, checked((int)response.Length)), out var expectedLength)
                && response.Length >= expectedLength)
            {
                break;
            }
        }

        return response.ToArray();
    }
}

public sealed record WindowsUsbIdentity(
    string? PrinterName,
    string? PnpInstanceId,
    string? UsbPrintInstanceId,
    string? UsbInstanceId,
    string? VendorId,
    string? ProductId,
    string? SerialNumber,
    string? DeviceDescription,
    string? FriendlyName);
