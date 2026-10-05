using System.Text;

namespace Dnp.Core;

public interface IDnpTransport
{
    Task<byte[]> QueryAsync(DnpCommand command, CancellationToken cancellationToken = default);
}

public interface IMediaTypeMapper
{
    MediaTypeInfo Map(string rawValue);
}

public sealed class DefaultMediaTypeMapper : IMediaTypeMapper
{
    public MediaTypeInfo Map(string rawValue)
    {
        var normalized = rawValue.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new MediaTypeInfo(rawValue, "Unknown");
        }

        return normalized.ToUpperInvariant() switch
        {
            _ => new MediaTypeInfo(normalized, normalized)
        };
    }
}

public sealed record DnpCommand(string Arg1, string Arg2, byte[]? Payload = null)
{
    public byte[] Encode() => DnpPacketCodec.Encode(this);

    public override string ToString() => $"{Arg1}/{Arg2}";
}

public static class DnpCommands
{
    public static readonly DnpCommand Status = new("STATUS", string.Empty);
    public static readonly DnpCommand RemainingPrints = new("INFO", "MQTY");
    public static readonly DnpCommand Media = new("INFO", "MEDIA");
    public static readonly DnpCommand FreeBuffer = new("INFO", "FREE_PBUFFER");
    public static readonly DnpCommand SerialNumber = new("INFO", "SERIAL_NUMBER");
}

public static class DnpPacketCodec
{
    private const byte Esc = 0x1B;
    private const byte CommandMarker = 0x50; // 'P'

    public static byte[] Encode(DnpCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var payload = command.Payload ?? Array.Empty<byte>();
        var arg1 = PadAscii(command.Arg1, 6);
        var arg2 = PadAscii(command.Arg2, 16);
        var length = Encoding.ASCII.GetBytes(payload.Length.ToString("D8"));

        using var stream = new MemoryStream();
        stream.WriteByte(Esc);
        stream.WriteByte(CommandMarker);
        stream.Write(arg1, 0, arg1.Length);
        stream.Write(arg2, 0, arg2.Length);
        stream.Write(length, 0, length.Length);
        stream.Write(payload, 0, payload.Length);
        return stream.ToArray();
    }

    public static string DecodeAsciiPayload(byte[] responseBytes)
    {
        ArgumentNullException.ThrowIfNull(responseBytes);

        if (TryGetExpectedResponseLength(responseBytes, out var expectedLength) && responseBytes.Length >= expectedLength)
        {
            var payloadLengthText = Encoding.ASCII.GetString(responseBytes, 0, 8);
            if (int.TryParse(payloadLengthText, out var payloadLength) && responseBytes.Length >= 8 + payloadLength)
            {
                return Encoding.ASCII.GetString(responseBytes, 8, payloadLength).TrimEnd('\0', '\r', '\n');
            }
        }

        return Encoding.ASCII.GetString(responseBytes).TrimEnd('\0', '\r', '\n');
    }

    public static bool TryGetExpectedResponseLength(ReadOnlySpan<byte> responseBytes, out int expectedLength)
    {
        if (responseBytes.Length >= 8 && AreDigits(responseBytes[..8]))
        {
            var payloadLengthText = Encoding.ASCII.GetString(responseBytes[..8]);
            if (int.TryParse(payloadLengthText, out var payloadLength))
            {
                expectedLength = 8 + payloadLength;
                return true;
            }
        }

        expectedLength = 0;
        return false;
    }

    private static byte[] PadAscii(string value, int size)
    {
        var padded = (value ?? string.Empty).PadRight(size, ' ');
        if (padded.Length > size)
        {
            padded = padded[..size];
        }

        return Encoding.ASCII.GetBytes(padded);
    }

    private static bool AreDigits(ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes)
        {
            if (b < '0' || b > '9')
            {
                return false;
            }
        }

        return true;
    }
}
