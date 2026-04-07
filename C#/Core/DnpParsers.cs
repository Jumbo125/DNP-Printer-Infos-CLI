using System.Globalization;
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
            if (!int.TryParse(normalized[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out status))
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

        var rawCode = $"0x{status:X8}";
        info = status switch
        {
            0x00000000 => new PrinterStatusInfo(rawCode, PrinterStatusKind.Idle, "Idle (compat)"),
            0x00010001 => new PrinterStatusInfo(rawCode, PrinterStatusKind.Idle, "Idle"),
            0x00010002 => new PrinterStatusInfo(rawCode, PrinterStatusKind.Printing, "Printing"),
            0x00010020 => new PrinterStatusInfo(rawCode, PrinterStatusKind.Idle, "Standstill"),
            0x00010040 => new PrinterStatusInfo(rawCode, PrinterStatusKind.Cooling, "Cooling"),
            0x00020001 => new PrinterStatusInfo(rawCode, PrinterStatusKind.CoverOpen, "Cover open"),
            0x00010008 => new PrinterStatusInfo(rawCode, PrinterStatusKind.PaperEnd, "Paper end"),
            0x00010010 => new PrinterStatusInfo(rawCode, PrinterStatusKind.RibbonEnd, "Ribbon end"),
            0x00020002 => new PrinterStatusInfo(rawCode, PrinterStatusKind.PaperJam, "Paper jam"),
            0x00020004 => new PrinterStatusInfo(rawCode, PrinterStatusKind.RibbonError, "Ribbon error"),
            0x00020008 => new PrinterStatusInfo(rawCode, PrinterStatusKind.PaperDefinitionError, "Paper error"),
            0x00020010 => new PrinterStatusInfo(rawCode, PrinterStatusKind.DataError, "Data error"),
            0x00020020 => new PrinterStatusInfo(rawCode, PrinterStatusKind.SystemError, "Scrap box error"),
            0x00040000 => new PrinterStatusInfo(rawCode, PrinterStatusKind.HardwareError, "Hardware error"),
            0x00080000 => new PrinterStatusInfo(rawCode, PrinterStatusKind.SystemError, "System error"),
            0x00100001 => new PrinterStatusInfo(rawCode, PrinterStatusKind.FlashProgramming, "Flash programming idle"),
            0x00100002 => new PrinterStatusInfo(rawCode, PrinterStatusKind.FlashProgramming, "Flash programming writing"),
            0x00100004 => new PrinterStatusInfo(rawCode, PrinterStatusKind.FlashProgramming, "Flash programming finished"),
            0x00100008 => new PrinterStatusInfo(rawCode, PrinterStatusKind.FlashProgramming, "Flash programming data error"),
            0x00100010 => new PrinterStatusInfo(rawCode, PrinterStatusKind.FlashProgramming, "Flash programming device error"),
            0x00100020 => new PrinterStatusInfo(rawCode, PrinterStatusKind.FlashProgramming, "Flash programming other error"),
            0x00200011 => new PrinterStatusInfo(rawCode, PrinterStatusKind.UnitError, "Unit error: jamming supply"),
            0x00200013 => new PrinterStatusInfo(rawCode, PrinterStatusKind.UnitError, "Unit error: jamming pass"),
            0x00200017 => new PrinterStatusInfo(rawCode, PrinterStatusKind.UnitError, "Unit error: jamming shell"),
            0x0020001B => new PrinterStatusInfo(rawCode, PrinterStatusKind.UnitError, "Unit error: jamming eject"),
            0x0020001E => new PrinterStatusInfo(rawCode, PrinterStatusKind.UnitError, "Unit error: jamming remove"),
            0x00200031 => new PrinterStatusInfo(rawCode, PrinterStatusKind.UnitError, "Unit error: capstan motor"),
            0x00200041 => new PrinterStatusInfo(rawCode, PrinterStatusKind.UnitError, "Unit error: shell motor"),
            0x00200051 => new PrinterStatusInfo(rawCode, PrinterStatusKind.UnitError, "Unit error: pinch"),
            0x00200061 => new PrinterStatusInfo(rawCode, PrinterStatusKind.UnitError, "Unit error: pass guide"),
            0x00200071 => new PrinterStatusInfo(rawCode, PrinterStatusKind.UnitError, "Unit error: skew guide"),
            0x00200081 => new PrinterStatusInfo(rawCode, PrinterStatusKind.UnitError, "Unit error: skew reject"),
            0x00200091 => new PrinterStatusInfo(rawCode, PrinterStatusKind.UnitError, "Unit error: shell rotate"),
            0x002000A1 => new PrinterStatusInfo(rawCode, PrinterStatusKind.UnitError, "Unit error: lever"),
            0x002000B1 => new PrinterStatusInfo(rawCode, PrinterStatusKind.UnitError, "Unit error: cutter"),
            0x002000C1 => new PrinterStatusInfo(rawCode, PrinterStatusKind.UnitError, "Unit error: tray out"),
            0x002000D1 => new PrinterStatusInfo(rawCode, PrinterStatusKind.UnitError, "Unit error: cover out"),
            0x002000F1 => new PrinterStatusInfo(rawCode, PrinterStatusKind.UnitError, "Unit error: system"),
            _ when (status & unchecked((int)0xFFF00000)) == 0x00200000 => new PrinterStatusInfo(rawCode, PrinterStatusKind.UnitError, "Unit error"),
            _ => new PrinterStatusInfo(rawCode, PrinterStatusKind.Unknown, "Unknown")
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
