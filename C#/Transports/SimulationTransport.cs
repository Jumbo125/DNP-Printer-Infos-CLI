using System.Text;
using Dnp.Core;

namespace Dnp.Transport.Windows;

public sealed class SimulationTransport : IDnpTransport
{
    public Task<byte[]> QueryAsync(DnpCommand command, CancellationToken cancellationToken = default)
    {
        var response = command switch
        {
            var c when string.Equals(c.Arg1, DnpCommands.Status.Arg1, StringComparison.OrdinalIgnoreCase) => "00000",
            var c when string.Equals(c.Arg1, DnpCommands.RemainingPrints.Arg1, StringComparison.OrdinalIgnoreCase) && string.Equals(c.Arg2, DnpCommands.RemainingPrints.Arg2, StringComparison.OrdinalIgnoreCase) => "0347",
            var c when string.Equals(c.Arg1, DnpCommands.Media.Arg1, StringComparison.OrdinalIgnoreCase) && string.Equals(c.Arg2, DnpCommands.Media.Arg2, StringComparison.OrdinalIgnoreCase) => "6x4",
            var c when string.Equals(c.Arg1, DnpCommands.FreeBuffer.Arg1, StringComparison.OrdinalIgnoreCase) && string.Equals(c.Arg2, DnpCommands.FreeBuffer.Arg2, StringComparison.OrdinalIgnoreCase) => "FBP1",
            _ => string.Empty
        };

        return Task.FromResult(Encoding.ASCII.GetBytes(response));
    }
}
