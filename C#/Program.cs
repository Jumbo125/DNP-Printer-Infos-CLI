using System.ComponentModel;
using System.Text.Json;
using Dnp.Core;
using Dnp.Transport.Linux;
using Dnp.Transport.Windows;

return await ProgramEntry.RunAsync(args).ConfigureAwait(false);

internal static class ProgramEntry
{
    public static async Task<int> RunAsync(string[] args)
    {
        var options = CliOptions.Parse(args);
        if (options.ShowHelp || string.IsNullOrWhiteSpace(options.Command))
        {
            PrintHelp();
            return 0;
        }

        try
        {
            if (string.Equals(options.Command, "detect", StringComparison.OrdinalIgnoreCase))
            {
                var detection = DetectPrinter(options);
                return WriteDetect(options.Json, detection);
            }

            IDnpTransport transportForQueries = options.Simulate
                ? new SimulationTransport()
                : CreateTransport(options);

            var client = new DnpProtocolClient(transportForQueries);

            return options.Command switch
            {
                "info" or "probe" => WriteInfo(options.Json, await client.ProbeAsync().ConfigureAwait(false), ResolvePrinterModel(options), ResolveDetectedModel(options)),
                "status" => WriteStatus(options.Json, await client.GetPrinterStatusAsync().ConfigureAwait(false)),
                "remaining" => Write(options.Json, await client.GetRemainingPrintsAsync().ConfigureAwait(false)),
                "media" => Write(options.Json, await client.GetMediaTypeAsync().ConfigureAwait(false)),
                "free-buffer" => Write(options.Json, await client.GetFreeBufferAsync().ConfigureAwait(false)),
                _ => UnknownCommand(options.Command)
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or PlatformNotSupportedException or DllNotFoundException or MissingMethodException or BadImageFormatException or Win32Exception)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }

    private static PrinterDetectionResult DetectPrinter(CliOptions options)
    {
        if (options.Simulate)
        {
            var simulatedTransport = options.Transport == "auto"
                ? (OperatingSystem.IsWindows() ? "windows" : "linux")
                : options.Transport;

            return string.Equals(simulatedTransport, "linux", StringComparison.OrdinalIgnoreCase)
                ? new PrinterDetectionResult(
                    Success: true,
                    QueryReady: true,
                    Transport: "linux",
                    QueryArgumentName: "--device",
                    QueryValue: options.Device ?? "usb:vid=1343,pid=000c,serial=SIM-LINUX-0001",
                    Port: null,
                    PrinterName: "DNP DS620",
                    DevicePath: "/dev/usb/lp0",
                    Model: options.Model ?? "DS620",
                    ProductName: "DNP DS620",
                    Manufacturer: "DNP",
                    LoadedDllPath: null,
                    VendorId: "1343",
                    ProductId: "000c",
                    SerialNumber: "SIM-LINUX-0001",
                    BusNumber: 1,
                    DeviceAddress: 1,
                    MatchReason: "Simulation mode.",
                    UsbInstanceId: null,
                    UsbPrintInstanceId: null)
                : new PrinterDetectionResult(
                    Success: true,
                    QueryReady: true,
                    Transport: "windows",
                    QueryArgumentName: "--device",
                    QueryValue: options.Device ?? @"\\?\usb#vid_1452&pid_9201#SIM-WIN-0001#{a5dcbf10-6530-11d2-901f-00c04fb951ed}",
                    Port: null,
                    PrinterName: options.Printer ?? "DNP QW410",
                    DevicePath: options.Device ?? @"\\?\usb#vid_1452&pid_9201#SIM-WIN-0001#{a5dcbf10-6530-11d2-901f-00c04fb951ed}",
                    Model: options.Model ?? "QW410",
                    ProductName: "DNP QW410",
                    Manufacturer: "DNP",
                    LoadedDllPath: null,
                    VendorId: "1452",
                    ProductId: "9201",
                    SerialNumber: "SIM-WIN-0001",
                    BusNumber: null,
                    DeviceAddress: null,
                    MatchReason: "Simulation mode.",
                    UsbInstanceId: @"USB\VID_1452&PID_9201\SIM-WIN-0001",
                    UsbPrintInstanceId: @"USBPRINT\DNPQW410\SIM-WIN-0001");
        }

        return options.Transport switch
        {
            "windows" => WindowsPrinterDetector.Detect(options.Printer, options.Model),
            "linux" => LinuxPrinterDetector.Detect(options.Device, options.Model),
            _ => OperatingSystem.IsWindows()
                ? WindowsPrinterDetector.Detect(options.Printer, options.Model)
                : OperatingSystem.IsLinux()
                    ? LinuxPrinterDetector.Detect(options.Device, options.Model)
                    : throw new PlatformNotSupportedException("Only Windows and Linux are currently supported.")
        };
    }

    private static IDnpTransport CreateTransport(CliOptions options) => options.Transport switch
    {
        "windows" => new WindowsUsbPrinterTransport(
            printerName: options.Printer,
            selectionHint: options.Model,
            devicePath: options.Device,
            readTimeoutMs: options.ReadTimeoutMs,
            postWriteDelayMs: options.PostWriteDelayMs),
        "linux" => new LinuxUsbPrinterTransport(
            devicePath: options.Device,
            readTimeoutMs: options.ReadTimeoutMs,
            postWriteDelayMs: options.PostWriteDelayMs),
        _ => OperatingSystem.IsWindows()
            ? new WindowsUsbPrinterTransport(
                printerName: options.Printer,
                selectionHint: options.Model,
                devicePath: options.Device,
                readTimeoutMs: options.ReadTimeoutMs,
                postWriteDelayMs: options.PostWriteDelayMs)
            : OperatingSystem.IsLinux()
                ? new LinuxUsbPrinterTransport(
                    devicePath: options.Device,
                    readTimeoutMs: options.ReadTimeoutMs,
                    postWriteDelayMs: options.PostWriteDelayMs)
                : throw new PlatformNotSupportedException("Only Windows and Linux are currently supported.")
    };

    private static int WriteDetect(bool json, PrinterDetectionResult detection)
    {
        var success = detection.Success && !string.IsNullOrWhiteSpace(detection.QueryValue);
        if (json)
        {
            object payload = success
                ? new Dictionary<string, object?>
                {
                    ["message"] = "succes",
                    ["VID"] = detection.VendorId,
                    ["PID"] = detection.ProductId,
                    ["Printermodel"] = detection.Model ?? ResolvePrinterModelFromVidPid(detection.VendorId, detection.ProductId),
                    ["device_id"] = detection.QueryValue
                }
                : new Dictionary<string, object?>
                {
                    ["message"] = "fail",
                    ["error"] = "kein drucker gefunden"
                };

            Console.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
            return success ? 0 : 1;
        }

        Console.WriteLine($"message: {(success ? "succes" : "fail")}");
        if (!success)
        {
            Console.WriteLine("kein drucker gefunden");
            return 1;
        }

        Console.WriteLine($"VID: {detection.VendorId ?? "n/a"}");
        Console.WriteLine($"PID: {detection.ProductId ?? "n/a"}");
        Console.WriteLine($"Printermodel: {detection.Model ?? ResolvePrinterModelFromVidPid(detection.VendorId, detection.ProductId) ?? "n/a"}");
        Console.WriteLine($"device_id: {detection.QueryValue}");
        return 0;
    }

    private static int WriteInfo(bool json, PrinterProbeResult probe, string? printerModel, string? printerModelRaw = null)
    {
        object? remainingValue = probe.RemainingPrints.Count.HasValue
            ? probe.RemainingPrints.Count.Value
            : probe.RemainingPrints.RawValue;
        object? freeBufferValue = probe.FreeBuffer.Count.HasValue
            ? probe.FreeBuffer.Count.Value
            : probe.FreeBuffer.RawValue;
        var statusText = FormatStatus(probe.Status);
        var mediaText = string.IsNullOrWhiteSpace(probe.MediaType.Name) ? probe.MediaType.RawValue : probe.MediaType.Name;

        if (json)
        {
            var payload = new Dictionary<string, object?>
            {
                ["message"] = "succes",
                ["Printermodel"] = printerModel ?? "unknown",
                ["Printermodel_raw"] = printerModelRaw,
                ["status"] = statusText,
                ["status_raw"] = probe.Status.RawCode,
                ["Remaining prints"] = remainingValue,
                ["Remaining prints_raw"] = probe.RemainingPrints.RawValue,
                ["Media"] = mediaText,
                ["Media_raw"] = probe.MediaType.RawValue,
                ["Free buffer"] = freeBufferValue,
                ["Free buffer_raw"] = probe.FreeBuffer.RawValue
            };

            Console.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
            return 0;
        }

        Console.WriteLine("message: succes");
        Console.WriteLine($"Printermodel: {printerModel ?? "unknown"}");
        Console.WriteLine($"Printermodel_raw: {printerModelRaw ?? "n/a"}");
        Console.WriteLine($"status: {statusText}");
        Console.WriteLine($"status_raw: {probe.Status.RawCode}");
        Console.WriteLine($"Remaining prints: {remainingValue}");
        Console.WriteLine($"Remaining prints_raw: {probe.RemainingPrints.RawValue}");
        Console.WriteLine($"Media: {mediaText}");
        Console.WriteLine($"Media_raw: {probe.MediaType.RawValue}");
        Console.WriteLine($"Free buffer: {freeBufferValue}");
        Console.WriteLine($"Free buffer_raw: {probe.FreeBuffer.RawValue}");
        return 0;
    }

    private static int WriteStatus(bool json, PrinterStatusInfo status)
    {
        var statusText = FormatStatus(status);
        if (json)
        {
            var payload = new Dictionary<string, object?>
            {
                ["message"] = "succes",
                ["status"] = statusText,
                ["status_code"] = status.RawCode,
                ["status_kind"] = status.Status.ToString()
            };

            Console.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
            return 0;
        }

        Console.WriteLine("message: succes");
        Console.WriteLine($"status: {statusText}");
        return 0;
    }

    private static string FormatStatus(PrinterStatusInfo status)
        => $"{status.Description} ({status.RawCode})";

    private static string? ResolvePrinterModel(CliOptions options)
    {
        var fromHint = DnpModelResolver.TryDetectFromText(options.Model, options.Printer, options.Device);
        if (!string.IsNullOrWhiteSpace(fromHint))
        {
            return fromHint;
        }

        if (OperatingSystem.IsWindows())
        {
            var fromDevicePath = ResolvePrinterModelFromWindowsDevicePath(options.Device);
            if (!string.IsNullOrWhiteSpace(fromDevicePath))
            {
                return fromDevicePath;
            }

            var identity = WindowsUsbIdentityProbe.TryFind(options.Printer, options.Model);
            var fromIdentity = DnpModelResolver.TryDetectFromText(
                identity?.FriendlyName,
                identity?.DeviceDescription,
                identity?.PrinterName,
                ResolvePrinterModelFromVidPid(identity?.VendorId, identity?.ProductId));
            if (!string.IsNullOrWhiteSpace(fromIdentity))
            {
                return fromIdentity;
            }
        }

        return ResolveDetectedModel(options);
    }

    private static string? ResolveDetectedModel(CliOptions options)
    {
        if (options.Simulate)
        {
            return options.Model ?? (OperatingSystem.IsWindows() ? "QW410" : "DS620");
        }

        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            return null;
        }

        try
        {
            var detection = DetectPrinter(options);
            return detection.Model ?? ResolvePrinterModelFromVidPid(detection.VendorId, detection.ProductId);
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolvePrinterModelFromWindowsDevicePath(string? devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath))
        {
            return null;
        }

        var normalized = devicePath.Trim();
        const string vidToken = "vid_";
        const string pidToken = "pid_";
        var vidIndex = normalized.IndexOf(vidToken, StringComparison.OrdinalIgnoreCase);
        var pidIndex = normalized.IndexOf(pidToken, StringComparison.OrdinalIgnoreCase);
        if (vidIndex < 0 || pidIndex < 0 || vidIndex + 8 > normalized.Length || pidIndex + 8 > normalized.Length)
        {
            return DnpModelResolver.TryDetectFromText(normalized);
        }

        var vid = normalized.Substring(vidIndex + vidToken.Length, 4).ToUpperInvariant();
        var pid = normalized.Substring(pidIndex + pidToken.Length, 4).ToUpperInvariant();
        return ResolvePrinterModelFromVidPid(vid, pid) ?? DnpModelResolver.TryDetectFromText(normalized);
    }

    private static string? ResolvePrinterModelFromVidPid(string? vendorId, string? productId)
        => WindowsUsbVidPidCatalog.TryGetModel(vendorId, productId);

    private static int Write(bool json, object value)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
            return 0;
        }

        switch (value)
        {
            case PrinterStatusInfo status:
                Console.WriteLine($"Status: {status.Description} ({status.RawCode})");
                break;
            case RemainingPrintsInfo remaining:
                Console.WriteLine($"Remaining prints: {remaining.Count?.ToString() ?? "n/a"} [{remaining.RawValue}]");
                break;
            case MediaTypeInfo media:
                Console.WriteLine($"Media: {media.Name} [{media.RawValue}]");
                break;
            case FreeBufferInfo freeBuffer:
                Console.WriteLine($"Free buffer: {freeBuffer.Count?.ToString() ?? "n/a"} [{freeBuffer.RawValue}]");
                break;
            case WindowsPortProbeResult ports:
                foreach (var port in ports.Ports.OrderBy(static x => x.Port))
                {
                    Console.WriteLine(
                        $"DeviceIndex {port.Port}: plausible={port.IsPlausible}, queryReady={port.QueryReady}, source={port.Source ?? ""}, modelCode={FormatInt(port.ModelCode)}, status={FormatInt(port.StatusRaw)}, media={port.Media ?? ""}, remaining={FormatInt(port.RemainingPrints)}, freeBuffer={FormatInt(port.FreeBuffer)}, serial={port.SerialNo ?? ""}{(string.IsNullOrWhiteSpace(port.Error) ? string.Empty : ", error=" + port.Error)}");
                }
                return ports.Ports.Any(static p => p.IsPlausible) ? 0 : 1;
            default:
                Console.WriteLine(value);
                break;
        }

        return 0;
    }

    private static string FormatInt(int? value) => value?.ToString() ?? "n/a";

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintHelp();
        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("dnp_info.exe <command> [model-hint] [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  detect       Detects a USB printer and prints VID, PID, printer model and device_id.");
        Console.WriteLine("  info         Reads printer model, status, remaining prints, media and free buffer.");
        Console.WriteLine("  status       Reads only printer status.");
        Console.WriteLine("  remaining    Reads only remaining prints.");
        Console.WriteLine("  media        Reads only media type.");
        Console.WriteLine("  free-buffer  Reads only free buffer.");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dnp_info.exe detect");
        Console.WriteLine("  dnp_info.exe detect --json");
        Console.WriteLine(@"  dnp_info.exe status --device \\?\usb#vid_1452&pid_9201#...");
        Console.WriteLine(@"  dnp_info.exe info --device \\?\usb#vid_1452&pid_9201#... --json");
        Console.WriteLine("  dnp_info.exe info QW410 --printer \"DP-QW410\"");
        Console.WriteLine();
        Console.WriteLine("Outputs:");
        Console.WriteLine("  detect  -> message, VID, PID, Printermodel, device_id.");
        Console.WriteLine("  info    -> message, Printermodel, Printermodel_raw, status, status_raw, Remaining prints, Remaining prints_raw, Media, Media_raw, Free buffer, Free buffer_raw.");
        Console.WriteLine("  status  -> message, status.");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --json");
        Console.WriteLine("  --simulate");
        Console.WriteLine("  --transport auto|windows|linux");
        Console.WriteLine("  --model <text>               Optional free-form model hint.");
        Console.WriteLine("  --printer \"DP-QW410\"       Windows printer name. Also via DNP_PRINTER_NAME.");
        Console.WriteLine("  --device /dev/usb/lp0        Linux device path or USB selector. Also via DNP_PRINTER_DEVICE.");
        Console.WriteLine(@"  --device \\?\usb#...      Windows raw USB device path. Also via DNP_PRINTER_DEVICE.");
        Console.WriteLine("  --read-timeout-ms 5000");
        Console.WriteLine("  --post-write-delay-ms 75");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}

internal sealed record CliOptions(
    string? Command,
    string? Model,
    bool Json,
    bool Simulate,
    bool ShowHelp,
    string Transport,
    string? Printer,
    string? Device,
    int ReadTimeoutMs,
    int PostWriteDelayMs)
{
    private static readonly string[] KnownCommands = ["detect", "info", "probe", "status", "remaining", "media", "free-buffer"];
    private static readonly string[] OptionsWithValues =
    [
        "--model",
        "-m",
        "--transport",
        "--printer",
        "--device",
        "--read-timeout-ms",
        "--post-write-delay-ms"
    ];

    public static CliOptions Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return new CliOptions(null, null, Json: false, Simulate: false, ShowHelp: true, "auto", null, null, 5000, 75);
        }

        var normalizedArgs = args.Where(static x => !string.Equals(x, "-", StringComparison.Ordinal)).ToArray();
        var json = normalizedArgs.Any(static x => string.Equals(x, "--json", StringComparison.OrdinalIgnoreCase));
        var simulate = normalizedArgs.Any(static x => string.Equals(x, "--simulate", StringComparison.OrdinalIgnoreCase));
        var showHelp = normalizedArgs.Any(static x => x is "-h" or "--help" or "/?");
        var command = GetCommand(normalizedArgs);
        var model = DnpModelResolver.Normalize(
            GetOption(normalizedArgs, "--model")
            ?? GetOption(normalizedArgs, "-m")
            ?? GetPositionalModelHint(normalizedArgs));

        return new CliOptions(
            command,
            model,
            Json: json,
            Simulate: simulate,
            ShowHelp: showHelp,
            Transport: GetOption(normalizedArgs, "--transport") ?? "auto",
            Printer: GetOption(normalizedArgs, "--printer") ?? Environment.GetEnvironmentVariable("DNP_PRINTER_NAME"),
            Device: GetOption(normalizedArgs, "--device") ?? Environment.GetEnvironmentVariable("DNP_PRINTER_DEVICE"),
            ReadTimeoutMs: GetIntOption(normalizedArgs, "--read-timeout-ms", 5000),
            PostWriteDelayMs: GetIntOption(normalizedArgs, "--post-write-delay-ms", 75));
    }

    private static string? GetCommand(IReadOnlyList<string> args)
    {
        foreach (var arg in args)
        {
            if (TryNormalizeCommand(arg, out var command))
            {
                return command;
            }
        }

        return null;
    }

    private static string? GetPositionalModelHint(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (TryNormalizeCommand(arg, out _))
            {
                continue;
            }

            if (string.Equals(arg, "--json", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "--simulate", StringComparison.OrdinalIgnoreCase)
                || arg is "-h" or "--help" or "/?")
            {
                continue;
            }

            if (OptionsWithValues.Contains(arg, StringComparer.OrdinalIgnoreCase))
            {
                i++;
                continue;
            }

            if (arg.StartsWith("-", StringComparison.Ordinal))
            {
                continue;
            }

            return arg;
        }

        return null;
    }

    private static string? GetOption(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase))
            {
                return i + 1 < args.Count ? args[i + 1] : null;
            }

            if (arg.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
            {
                return arg[(name.Length + 1)..];
            }
        }

        return null;
    }

    private static int GetIntOption(IReadOnlyList<string> args, string name, int defaultValue)
        => GetNullableIntOption(args, name) ?? defaultValue;

    private static int? GetNullableIntOption(IReadOnlyList<string> args, string name)
        => int.TryParse(GetOption(args, name), out var value) ? value : null;

    private static bool TryNormalizeCommand(string arg, out string command)
    {
        command = KnownCommands.FirstOrDefault(candidate => string.Equals(candidate, arg, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        return command.Length > 0;
    }
}
