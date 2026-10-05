using System.Text.RegularExpressions;

namespace Dnp.Core;

public static partial class DnpModelResolver
{
    private static readonly string[] ReservedTokens =
    [
        "DNP",
        "CITIZEN",
        "PRINTER",
        "PHOTO",
        "SYSTEM",
        "STATUS",
        "MEDIA",
        "USB"
    ];

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().TrimStart('-');
    }

    public static bool IsPotentialDnpPrinterText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var upper = value.ToUpperInvariant();
        return upper.Contains("DNP", StringComparison.Ordinal)
               || upper.Contains("CITIZEN", StringComparison.Ordinal)
               || TryDetectKnownModels(value) is not null;
    }

    public static bool MatchesHint(string? hint, params string?[] values)
    {
        var normalizedHint = Compact(hint);
        if (string.IsNullOrWhiteSpace(normalizedHint))
        {
            return false;
        }

        foreach (var value in values)
        {
            var candidate = Compact(value);
            if (candidate.Contains(normalizedHint, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static string? TryDetectFromText(params string?[] values)
    {
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var known = TryDetectKnownModels(value);
            if (known is not null)
            {
                return known;
            }

            var generic = TryExtractGenericModel(value);
            if (generic is not null)
            {
                return generic;
            }
        }

        return null;
    }

    private static string? TryDetectKnownModels(string value)
    {
        var compact = Compact(value);
        if (compact.Contains("DS620", StringComparison.Ordinal))
        {
            return "DS620";
        }

        if (compact.Contains("QW410", StringComparison.Ordinal))
        {
            return "QW410";
        }

        if (compact.Contains("CZ01", StringComparison.Ordinal))
        {
            return "CZ-01";
        }

        return null;
    }

    private static string? TryExtractGenericModel(string value)
    {
        if (!IsPotentialDnpPrinterText(value))
        {
            return null;
        }

        foreach (Match match in GenericModelRegex().Matches(value.ToUpperInvariant()))
        {
            var token = match.Groups[1].Value.Trim();
            if (string.IsNullOrWhiteSpace(token) || ReservedTokens.Contains(token, StringComparer.Ordinal))
            {
                continue;
            }

            return token;
        }

        return null;
    }

    private static string Compact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value
            .Where(static ch => char.IsLetterOrDigit(ch))
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    [GeneratedRegex(@"\b([A-Z]{1,5}-?\d{1,5}[A-Z0-9-]*)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex GenericModelRegex();
}
