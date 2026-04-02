namespace Dnp.Core;

public enum PrinterStatusKind
{
    Unknown = 0,
    Idle,
    Printing,
    Cooling,
    CoverOpen,
    PaperEnd,
    RibbonEnd,
    PaperJam,
    RibbonError,
    PaperDefinitionError,
    DataError,
    RfidModuleError,
    SystemError
}

public sealed record PrinterStatusInfo(string RawCode, PrinterStatusKind Status, string Description);

public sealed record RemainingPrintsInfo(string RawValue, int? Count);

public sealed record MediaTypeInfo(string RawValue, string Name);

public sealed record FreeBufferInfo(string RawValue, int? Count);

public sealed record PrinterProbeResult(
    PrinterStatusInfo Status,
    RemainingPrintsInfo RemainingPrints,
    MediaTypeInfo MediaType,
    FreeBufferInfo FreeBuffer);

public sealed record WindowsPortProbeResult(IReadOnlyList<WindowsPortProbeEntry> Ports);

public sealed record WindowsPortProbeEntry(
    int Port,
    bool IsPlausible,
    bool QueryReady,
    string? Source,
    int? ModelCode,
    int? StatusRaw,
    string? StatusDescription,
    string? Media,
    int? RemainingPrints,
    int? RemainingPrintsAlt,
    int? FreeBuffer,
    string? SerialNo,
    string? Error)
{
    public int DeviceIndex => Port;
}
