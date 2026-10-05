namespace Dnp.Transport.Windows;

internal static class WindowsUsbVidPidCatalog
{
    private static readonly Dictionary<string, string> PairToModel = new(StringComparer.OrdinalIgnoreCase)
    {
        ["1452:9201"] = "QW410",
        ["1343:FFFF"] = "DS620",
        ["1343:1001"] = "DS820",
        ["1452:9401"] = "DS40",
        ["1452:9001"] = "DS80",
        ["1343:0009"] = "RX1",
        ["1452:9301"] = "CX-02",
        ["1452:8B02"] = "CX-W",
        ["1452:8B01"] = "CZ-01",
        ["1343:0008"] = "DS-RX1HS",
        ["1343:0007"] = "DS40",
        ["1343:0006"] = "DS80",
        ["1343:0005"] = "CX",
        ["1343:0004"] = "DS80D",
        ["1343:0003"] = "DS80DX",
        ["1343:0002"] = "DS40D",
        ["1343:0001"] = "DS40DX",
    };

    private static readonly HashSet<string> AllowedPairs = new(PairToModel.Keys, StringComparer.OrdinalIgnoreCase)
    {
        "1343:FFFF",
        "1452:9201",
        "1343:1001",
        "1452:9401",
        "1452:9001",
        "1343:0009",
        "1452:9301",
        "1452:8B02",
        "1452:8B01",
        "1343:0008",
        "1343:0007",
        "1343:0006",
        "1343:0005",
        "1343:0004",
        "1343:0003",
        "1343:0002",
        "1343:0001",
    };

    public static bool IsKnownDnpPair(string? vendorId, string? productId)
        => !string.IsNullOrWhiteSpace(vendorId)
           && !string.IsNullOrWhiteSpace(productId)
           && AllowedPairs.Contains($"{vendorId.Trim().ToUpperInvariant()}:{productId.Trim().ToUpperInvariant()}");

    public static IReadOnlyCollection<string> GetKnownPairs() => AllowedPairs;

    public static string? TryGetModel(string? vendorId, string? productId)
    {
        if (string.IsNullOrWhiteSpace(vendorId) || string.IsNullOrWhiteSpace(productId))
        {
            return null;
        }

        return PairToModel.TryGetValue($"{vendorId.Trim().ToUpperInvariant()}:{productId.Trim().ToUpperInvariant()}", out var model)
            ? model
            : null;
    }
}
