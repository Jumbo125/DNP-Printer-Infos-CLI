using System.Runtime.InteropServices;

namespace Dnp.Transport.Windows;

internal sealed class CspStatNative : IDisposable
{
    private static readonly string PreferredDllName = Environment.Is64BitProcess ? "cspstatx64.dll" : "cspstatx32.dll";
    private static readonly string FallbackDllName = Environment.Is64BitProcess ? "cspstatx32.dll" : "cspstatx64.dll";
    private static readonly string PreferredArchitectureLabel = Environment.Is64BitProcess ? "x64" : "x86";

    private readonly nint _handle;
    private readonly InitializePrinterDelegate _initializePrinter;
    private readonly GetPrinterPortNumDelegate _getPrinterPortNum;
    private readonly GetStatusDelegate _getStatus;
    private readonly GetStatusDelegate? _cvGetStatus;
    private readonly GetMediaDelegate _getMedia;
    private readonly GetPqtyDelegate _getPqty;
    private readonly GetMediaCounterDelegate _getMediaCounter;
    private readonly GetFreeBufferDelegate _getFreeBuffer;
    private readonly GetSerialNoDelegate _getSerialNo;

    public CspStatNative(string? explicitPath = null)
    {
        _handle = LoadLibrary(explicitPath, out var loadedPath);
        LoadedPath = loadedPath;
        _initializePrinter = GetFunction<InitializePrinterDelegate>("InitializePrinter");
        _getPrinterPortNum = GetFunction<GetPrinterPortNumDelegate>("GetPrinterPortNum");
        _getStatus = GetFunction<GetStatusDelegate>("GetStatus");
        _cvGetStatus = TryGetFunction<GetStatusDelegate>("CvGetStatus");
        _getMedia = GetFunction<GetMediaDelegate>("GetMedia");
        _getPqty = GetFunction<GetPqtyDelegate>("GetPQTY");
        _getMediaCounter = GetFunction<GetMediaCounterDelegate>("GetMediaCounter");
        _getFreeBuffer = GetFunction<GetFreeBufferDelegate>("GetFreeBuffer");
        _getSerialNo = GetFunction<GetSerialNoDelegate>("GetSerialNo");
    }

    public string LoadedPath { get; }

    public int InitializePrinter(int printerFilter = 0) => _initializePrinter(printerFilter);
    public int GetStatus(int port)
    {
        var status = _getStatus(port);
        if (status == unchecked((int)0x80000000) && _cvGetStatus is not null)
        {
            try
            {
                var cvStatus = _cvGetStatus(port);
                if (cvStatus != unchecked((int)0x80000000))
                {
                    return cvStatus;
                }
            }
            catch
            {
                // Fall back to the primary export result.
            }
        }

        return status;
    }
    public string GetMedia(int port) => PtrToAnsi(_getMedia(port));
    public int GetPQTY(int port) => _getPqty(port);
    public int GetMediaCounter(int port) => _getMediaCounter(port);
    public int GetFreeBuffer(int port) => _getFreeBuffer(port);
    public string GetSerialNo(int port) => PtrToAnsi(_getSerialNo(port));

    public IReadOnlyList<CspEnumeratedPrinter> GetPrinterPortPairs(int printerFilter)
    {
        const int bufferSize = 512;
        _ = InitializePrinter(printerFilter);

        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            Span<byte> cleared = stackalloc byte[bufferSize];
            Marshal.Copy(cleared.ToArray(), 0, buffer, bufferSize);

            var count = _getPrinterPortNum(buffer, bufferSize);
            if (count <= 0)
            {
                return Array.Empty<CspEnumeratedPrinter>();
            }

            var raw = new byte[bufferSize];
            Marshal.Copy(buffer, raw, 0, bufferSize);

            var results = new List<CspEnumeratedPrinter>();
            var maxPairs = Math.Min(count, bufferSize / 2);
            for (var i = 0; i < maxPairs; i++)
            {
                var offset = i * 2;
                var modelCode = raw[offset];
                var deviceIndex = raw[offset + 1];
                if (modelCode == 0 && deviceIndex == 0)
                {
                    break;
                }

                results.Add(new CspEnumeratedPrinter(modelCode, deviceIndex));
            }

            return results;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        if (_handle != nint.Zero)
        {
            NativeLibrary.Free(_handle);
        }
    }

    private T? TryGetFunction<T>(string name) where T : Delegate
    {
        if (!NativeLibrary.TryGetExport(_handle, name, out var address) || address == nint.Zero)
        {
            return null;
        }

        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    private T GetFunction<T>(string name) where T : Delegate
    {
        if (!NativeLibrary.TryGetExport(_handle, name, out var address) || address == nint.Zero)
        {
            throw new MissingMethodException($"Export '{name}' was not found in {LoadedPath}.");
        }

        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    private static nint LoadLibrary(string? explicitPath, out string loadedPath)
    {
        var candidates = BuildCandidates(explicitPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var failures = new List<string>();

        foreach (var candidate in candidates)
        {
            try
            {
                if (NativeLibrary.TryLoad(candidate, out var handle) && handle != nint.Zero)
                {
                    loadedPath = candidate;
                    return handle;
                }
            }
            catch (BadImageFormatException ex)
            {
                failures.Add($"{candidate}: wrong DLL architecture for this {PreferredArchitectureLabel} process ({ex.Message})");
            }
            catch (Exception ex)
            {
                failures.Add($"{candidate}: {ex.Message}");
            }
        }

        throw new DllNotFoundException(
            "Could not load cspstat library. Tried: " + string.Join(", ", candidates) + ". " +
            $@"This executable is running as {PreferredArchitectureLabel} and expects {PreferredDllName}, ideally in .\dll\{PreferredDllName}. " +
            $"You can also pass --dll-path <absolute-path-to-{PreferredDllName}>." +
            (failures.Count == 0 ? string.Empty : " Failures: " + string.Join(" | ", failures)));
    }

    private static IEnumerable<string> BuildCandidates(string? explicitPath)
    {
        foreach (var candidate in ExpandConfiguredPath(explicitPath))
        {
            yield return candidate;
        }

        foreach (var candidate in ExpandConfiguredPath(Environment.GetEnvironmentVariable("DNP_CSPSTAT_PATH")))
        {
            yield return candidate;
        }

        var baseDir = AppContext.BaseDirectory;
        var preferredNames = new[]
        {
            Path.Combine("dll", PreferredDllName),
            Path.Combine("dll", PreferredArchitectureLabel == "x64" ? "win-x64" : "win-x86", PreferredDllName),
            PreferredDllName,
            Path.Combine(PreferredArchitectureLabel == "x64" ? @"Native\win-x64" : @"Native\win-x86", PreferredDllName),
            Path.Combine("dll", FallbackDllName),
            FallbackDllName,
            Path.Combine(PreferredArchitectureLabel == "x64" ? @"Native\win-x86" : @"Native\win-x64", FallbackDllName)
        };

        foreach (var name in preferredNames)
        {
            yield return Path.Combine(baseDir, name);
        }

        foreach (var name in preferredNames)
        {
            yield return name;
        }
    }

    private static IEnumerable<string> ExpandConfiguredPath(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            yield break;
        }

        var trimmed = configuredPath.Trim();
        if (Directory.Exists(trimmed))
        {
            yield return Path.Combine(trimmed, PreferredDllName);
            yield return Path.Combine(trimmed, FallbackDllName);
            yield break;
        }

        yield return trimmed;
    }

    private static string PtrToAnsi(nint ptr)
        => ptr == nint.Zero ? string.Empty : (Marshal.PtrToStringAnsi(ptr) ?? string.Empty).Trim();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int InitializePrinterDelegate(int printerFilter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetPrinterPortNumDelegate(nint buffer, int bufferSize);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetStatusDelegate(int port);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate nint GetMediaDelegate(int port);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetPqtyDelegate(int port);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetMediaCounterDelegate(int port);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetFreeBufferDelegate(int port);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate nint GetSerialNoDelegate(int port);
}

internal sealed record CspEnumeratedPrinter(byte ModelCode, int DeviceIndex);
