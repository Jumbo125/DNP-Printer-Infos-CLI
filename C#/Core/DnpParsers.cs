using System.Text.RegularExpressions;

namespace Dnp.Core;

public static partial class DnpParsers
{
    public static PrinterStatusInfo ParseStatus(string response)
    {
        var raw = response.Trim();

        if (TryParseCspStatus(raw, out var cspStatus))
        {
            return cspStatus;
        }

        var code = ExtractFirstDigits(raw, 4) ?? raw;

        return code switch
        {
            "0" or "0000" or "00000" => new PrinterStatusInfo(code, PrinterStatusKind.Idle, "Idle"),
            "1" or "0001" or "00001" => new PrinterStatusInfo(code, PrinterStatusKind.Printing, "Printing"),
            "500" or "0500" or "00500" or "510" or "0510" or "00510" => new PrinterStatusInfo(code, PrinterStatusKind.Cooling, "Cooling"),
            "1000" or "01000" => new PrinterStatusInfo(code, PrinterStatusKind.CoverOpen, "Cover open"),
            "1100" or "01100" => new PrinterStatusInfo(code, PrinterStatusKind.PaperEnd, "Paper end"),
            "1200" or "01200" => new PrinterStatusInfo(code, PrinterStatusKind.RibbonEnd, "Ribbon end"),
            "1300" or "01300" => new PrinterStatusInfo(code, PrinterStatusKind.PaperJam, "Paper jam"),
            "1400" or "01400" => new PrinterStatusInfo(code, PrinterStatusKind.RibbonError, "Ribbon error"),
            "1500" or "01500" => new PrinterStatusInfo(code, PrinterStatusKind.PaperDefinitionError, "Paper definition error"),
            "1600" or "01600" => new PrinterStatusInfo(code, PrinterStatusKind.DataError, "Data error"),
            "2800" or "02800" => new PrinterStatusInfo(code, PrinterStatusKind.RfidModuleError, "RF-ID module error"),
            "3000" or "03000" => new PrinterStatusInfo(code, PrinterStatusKind.SystemError, "System error"),
            _ => new PrinterStatusInfo(code, PrinterStatusKind.Unknown, "Unknown")
        };
    }

    public static RemainingPrintsInfo ParseRemainingPrints(string response)
    {
        var raw = response.Trim();
        var count = ParseIntegerAfterPrefix(raw, 4);
        return new RemainingPrintsInfo(raw, count);
    }

    public static MediaTypeInfo ParseMediaType(string response, IMediaTypeMapper mapper)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        return mapper.Map(response.Trim());
    }

    public static FreeBufferInfo ParseFreeBuffer(string response)
    {
        var raw = response.Trim();
        var count = raw.StartsWith("FBP", StringComparison.OrdinalIgnoreCase)
            ? ParseIntegerAfterPrefix(raw, 3)
            : ExtractTrailingInt(raw);

        return new FreeBufferInfo(raw, count);
    }

    private static bool TryParseCspStatus(string raw, out PrinterStatusInfo info)
    {
        var normalized = raw.Trim();
        if (normalized.StartsWith("CSP:", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[4..];
        }

        int status;
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(normalized[2..], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out status))
            {
                info = new PrinterStatusInfo(raw, PrinterStatusKind.Unknown, "Unknown");
                return false;
            }
        }
        else if (!int.TryParse(normalized, out status))
        {
            info = new PrinterStatusInfo(raw, PrinterStatusKind.Unknown, "Unknown");
            return false;
        }

        info = status switch
        {
            0x00010001 => new PrinterStatusInfo($"0x{status:X8}", PrinterStatusKind.Idle, "Idle"),
            0x00010002 => new PrinterStatusInfo($"0x{status:X8}", PrinterStatusKind.Printing, "Printing"),
            0x00010020 => new PrinterStatusInfo($"0x{status:X8}", PrinterStatusKind.Cooling, "Cooling"),
            0x00020001 => new PrinterStatusInfo($"0x{status:X8}", PrinterStatusKind.CoverOpen, "Cover open"),
            0x00010008 => new PrinterStatusInfo($"0x{status:X8}", PrinterStatusKind.PaperEnd, "Paper end"),
            0x00010010 => new PrinterStatusInfo($"0x{status:X8}", PrinterStatusKind.RibbonEnd, "Ribbon end"),
            0x00020002 => new PrinterStatusInfo($"0x{status:X8}", PrinterStatusKind.PaperJam, "Paper jam"),
            0x00020004 => new PrinterStatusInfo($"0x{status:X8}", PrinterStatusKind.RibbonError, "Ribbon error"),
            0x00020008 => new PrinterStatusInfo($"0x{status:X8}", PrinterStatusKind.PaperDefinitionError, "Paper error"),
            0x00020020 => new PrinterStatusInfo($"0x{status:X8}", PrinterStatusKind.SystemError, "Scrapbox error"),
            _ => new PrinterStatusInfo($"0x{status:X8}", PrinterStatusKind.Unknown, "Unknown")
        };
        return true;
    }

    private static int? ParseIntegerAfterPrefix(string raw, int prefixLength)
    {
        if (raw.Length <= prefixLength)
        {
            return ExtractTrailingInt(raw);
        }

        return int.TryParse(new string(raw[prefixLength..].Where(char.IsDigit).ToArray()), out var value)
            ? value
            : ExtractTrailingInt(raw);
    }

    private static int? ExtractTrailingInt(string raw)
    {
        var match = TrailingNumberRegex().Match(raw);
        return match.Success && int.TryParse(match.Value, out var value)
            ? value
            : null;
    }

    private static string? ExtractFirstDigits(string raw, int minimumLength)
    {
        var match = DigitRunRegex().Match(raw);
        return match.Success && match.Value.Length >= minimumLength
            ? match.Value
            : null;
    }

    [GeneratedRegex(@"\d+")]
    private static partial Regex DigitRunRegex();

    [GeneratedRegex(@"\d+$")]
    private static partial Regex TrailingNumberRegex();
}
