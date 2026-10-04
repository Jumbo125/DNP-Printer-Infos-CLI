namespace Dnp.Core;

public sealed class DnpProtocolClient
{
    private readonly IDnpTransport _transport;
    private readonly IMediaTypeMapper _mediaTypeMapper;

    public DnpProtocolClient(IDnpTransport transport, IMediaTypeMapper? mediaTypeMapper = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _mediaTypeMapper = mediaTypeMapper ?? new DefaultMediaTypeMapper();
    }

    public async Task<PrinterStatusInfo> GetPrinterStatusAsync(CancellationToken cancellationToken = default)
    {
        var response = await QueryAsync(DnpCommands.Status, cancellationToken).ConfigureAwait(false);
        return DnpParsers.ParseStatus(response);
    }

    public async Task<RemainingPrintsInfo> GetRemainingPrintsAsync(CancellationToken cancellationToken = default)
    {
        var response = await QueryAsync(DnpCommands.RemainingPrints, cancellationToken).ConfigureAwait(false);
        return DnpParsers.ParseRemainingPrints(response);
    }

    public async Task<MediaTypeInfo> GetMediaTypeAsync(CancellationToken cancellationToken = default)
    {
        var response = await QueryAsync(DnpCommands.Media, cancellationToken).ConfigureAwait(false);
        return DnpParsers.ParseMediaType(response, _mediaTypeMapper);
    }

    public async Task<FreeBufferInfo> GetFreeBufferAsync(CancellationToken cancellationToken = default)
    {
        var response = await QueryAsync(DnpCommands.FreeBuffer, cancellationToken).ConfigureAwait(false);
        return DnpParsers.ParseFreeBuffer(response);
    }

    public async Task<PrinterProbeResult> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var status = await GetPrinterStatusAsync(cancellationToken).ConfigureAwait(false);
        var remaining = await GetRemainingPrintsAsync(cancellationToken).ConfigureAwait(false);
        var media = await GetMediaTypeAsync(cancellationToken).ConfigureAwait(false);
        var freeBuffer = await GetFreeBufferAsync(cancellationToken).ConfigureAwait(false);
        return new PrinterProbeResult(status, remaining, media, freeBuffer);
    }

    private async Task<string> QueryAsync(DnpCommand command, CancellationToken cancellationToken)
    {
        var rawResponse = await _transport.QueryAsync(command, cancellationToken).ConfigureAwait(false);
        return DnpPacketCodec.DecodeAsciiPayload(rawResponse);
    }
}
