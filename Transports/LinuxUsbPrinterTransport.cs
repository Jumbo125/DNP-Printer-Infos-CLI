using Dnp.Core;

namespace Dnp.Transport.Linux;

public sealed class LinuxUsbPrinterTransport : IDnpTransport
{
    private readonly string? _devicePath;
    private readonly int _readChunkSize;
    private readonly int _readTimeoutMs;
    private readonly int _postWriteDelayMs;

    public LinuxUsbPrinterTransport(
        string? devicePath = null,
        int readChunkSize = 4096,
        int readTimeoutMs = 5000,
        int postWriteDelayMs = 75)
    {
        _devicePath = string.IsNullOrWhiteSpace(devicePath) ? null : devicePath.Trim();
        _readChunkSize = Math.Max(256, readChunkSize);
        _readTimeoutMs = Math.Max(250, readTimeoutMs);
        _postWriteDelayMs = Math.Max(0, postWriteDelayMs);
    }

    public async Task<byte[]> QueryAsync(DnpCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("Linux transport can only run on Linux.");
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

    private string ResolveDevicePath()
    {
        if (TryResolveDevicePath(_devicePath, out var explicitResolved))
        {
            return explicitResolved;
        }

        var envPath = Environment.GetEnvironmentVariable("DNP_PRINTER_DEVICE");
        if (TryResolveDevicePath(envPath, out var envResolved))
        {
            return envResolved;
        }

        var firstExisting = LinuxUsbDeviceCatalog.Enumerate().FirstOrDefault();
        if (firstExisting is not null)
        {
            return firstExisting.DevicePath;
        }

        throw new InvalidOperationException(
            "No Linux printer device could be selected automatically. Pass --device /dev/usb/lp0 or --device usb:vid=1343,pid=000c,... " +
            "or set DNP_PRINTER_DEVICE.");
    }

    private static bool TryResolveDevicePath(string? queryValue, out string devicePath)
    {
        devicePath = string.Empty;
        if (string.IsNullOrWhiteSpace(queryValue))
        {
            return false;
        }

        var trimmed = queryValue.Trim();
        if (!trimmed.StartsWith("usb:", StringComparison.OrdinalIgnoreCase))
        {
            if (!File.Exists(trimmed))
            {
                return false;
            }

            devicePath = trimmed;
            return true;
        }

        var resolved = LinuxUsbDeviceCatalog.Resolve(trimmed);
        if (resolved is null || string.IsNullOrWhiteSpace(resolved.DevicePath))
        {
            return false;
        }

        devicePath = resolved.DevicePath;
        return true;
    }
}
