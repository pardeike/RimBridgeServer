using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using RimBridgeServer.Contracts;
using RimBridgeServer.Extensions.Abstractions;

namespace RimBridgeServer.Core;

public sealed class CapabilityRegistry
{
    private readonly Dictionary<string, RimBridgeCapabilityRegistration> _registrationsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _idsByAlias = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterProvider(IRimBridgeCapabilityProvider provider)
    {
        if (provider == null)
            throw new ArgumentNullException(nameof(provider));

        foreach (var registration in provider.GetCapabilities() ?? Array.Empty<RimBridgeCapabilityRegistration>())
        {
            Register(registration);
        }
    }

    public IReadOnlyList<CapabilityDescriptor> GetCapabilities()
    {
        return _registrationsById.Values
            .Select(registration => registration.Descriptor)
            .OrderBy(descriptor => descriptor.Id, StringComparer.Ordinal)
            .ToList();
    }

    public CapabilityDescriptor ResolveDescriptor(string idOrAlias)
    {
        return ResolveRegistration(idOrAlias).Descriptor;
    }

    public OperationEnvelope Invoke(string idOrAlias, IDictionary<string, object> arguments = null, CancellationToken cancellationToken = default)
    {
        var registration = ResolveRegistration(idOrAlias);
        if (!registration.HasHandler)
        {
            var startedAtUtc = DateTimeOffset.UtcNow;
            return OperationEnvelope.Failed("op_" + Guid.NewGuid().ToString("N"), registration.Descriptor.Id, startedAtUtc, new OperationError
            {
                Code = "capability.unavailable",
                Message = $"Capability '{registration.Descriptor.Id}' has no registered handler.",
                ExceptionType = typeof(InvalidOperationException).FullName ?? nameof(InvalidOperationException)
            });
        }

        var invocation = new CapabilityInvocation
        {
            CapabilityId = registration.Descriptor.Id,
            RequestedMode = CapabilityExecutionMode.Wait,
            RequestedAtUtc = DateTimeOffset.UtcNow,
            Arguments = arguments != null
                ? new Dictionary<string, object>(arguments, StringComparer.Ordinal)
                : []
        };

        return registration.Handler(invocation, cancellationToken).GetAwaiter().GetResult();
    }

    private void Register(RimBridgeCapabilityRegistration registration)
    {
        if (registration == null)
            throw new ArgumentNullException(nameof(registration));

        var descriptor = registration.Descriptor ?? throw new InvalidOperationException("Capability registration must have a descriptor.");
        if (string.IsNullOrWhiteSpace(descriptor.Id))
            throw new InvalidOperationException("Capability registration must have a non-empty id.");

        if (_registrationsById.ContainsKey(descriptor.Id))
            throw new InvalidOperationException($"Capability id '{descriptor.Id}' is already registered.");

        _registrationsById.Add(descriptor.Id, registration);

        RegisterAlias(descriptor.Id, descriptor.Id);
        foreach (var alias in descriptor.Aliases.Where(alias => string.IsNullOrWhiteSpace(alias) == false))
        {
            RegisterAlias(alias, descriptor.Id);
        }
    }

    private void RegisterAlias(string alias, string descriptorId)
    {
        if (_idsByAlias.TryGetValue(alias, out var existingId) && string.Equals(existingId, descriptorId, StringComparison.Ordinal) == false)
            throw new InvalidOperationException($"Capability alias '{alias}' is already registered for '{existingId}'.");

        _idsByAlias[alias] = descriptorId;
    }

    private RimBridgeCapabilityRegistration ResolveRegistration(string idOrAlias)
    {
        if (string.IsNullOrWhiteSpace(idOrAlias))
            throw new ArgumentException("A capability id or alias is required.", nameof(idOrAlias));

        if (!_idsByAlias.TryGetValue(idOrAlias, out var descriptorId))
            throw new InvalidOperationException($"Capability '{idOrAlias}' is not registered.");

        return _registrationsById[descriptorId];
    }
}
