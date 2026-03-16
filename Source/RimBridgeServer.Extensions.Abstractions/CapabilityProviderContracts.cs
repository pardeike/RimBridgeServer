using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimBridgeServer.Contracts;

namespace RimBridgeServer.Extensions.Abstractions;

public delegate Task<OperationEnvelope> RimBridgeCapabilityHandler(CapabilityInvocation invocation, CancellationToken cancellationToken);

public sealed class RimBridgeCapabilityRegistration
{
    public RimBridgeCapabilityRegistration(CapabilityDescriptor descriptor, RimBridgeCapabilityHandler handler = null)
    {
        Descriptor = descriptor;
        Handler = handler;
    }

    public CapabilityDescriptor Descriptor { get; }

    public RimBridgeCapabilityHandler Handler { get; }

    public bool HasHandler => Handler != null;
}

public interface IRimBridgeCapabilityProvider
{
    string ProviderId { get; }

    IEnumerable<RimBridgeCapabilityRegistration> GetCapabilities();
}
