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
    private readonly OperationJournal _journal;

    public CapabilityRegistry(OperationJournal journal = null)
    {
        _journal = journal;
    }

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
        var descriptor = registration.Descriptor;
        var operationId = "op_" + Guid.NewGuid().ToString("N");
        var requestedAtUtc = DateTimeOffset.UtcNow;
        var correlation = OperationContext.Capture();
        var invocationArguments = arguments != null
            ? new Dictionary<string, object>(arguments, StringComparer.Ordinal)
            : [];

        var metadata = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["requestedId"] = idOrAlias,
            ["providerId"] = descriptor.ProviderId,
            ["category"] = descriptor.Category,
            ["arguments"] = invocationArguments
        };

        if (string.IsNullOrWhiteSpace(correlation?.OperationId) == false)
            metadata["parentOperationId"] = correlation.OperationId;
        if (string.IsNullOrWhiteSpace(correlation?.CapabilityId) == false)
            metadata["parentCapabilityId"] = correlation.CapabilityId;
        if (string.IsNullOrWhiteSpace(correlation?.RootOperationId) == false)
            metadata["rootOperationId"] = correlation.RootOperationId;
        if (string.IsNullOrWhiteSpace(correlation?.ScriptStatementId) == false)
            metadata["scriptStatementId"] = correlation.ScriptStatementId;
        if (string.IsNullOrWhiteSpace(correlation?.ScriptStepId) == false)
            metadata["scriptStepId"] = correlation.ScriptStepId;
        if (string.IsNullOrWhiteSpace(correlation?.ScriptCall) == false)
            metadata["scriptCall"] = correlation.ScriptCall;

        _journal?.RecordStarted(operationId, descriptor.Id, metadata, requestedAtUtc);

        if (!registration.HasHandler)
        {
            var failed = OperationEnvelope.Failed(operationId, descriptor.Id, requestedAtUtc, new OperationError
            {
                Code = "capability.unavailable",
                Message = $"Capability '{descriptor.Id}' has no registered handler.",
                ExceptionType = typeof(InvalidOperationException).FullName ?? nameof(InvalidOperationException)
            });
            _journal?.RecordCompleted(failed);
            return failed;
        }

        var invocation = new CapabilityInvocation
        {
            CapabilityId = descriptor.Id,
            OperationId = operationId,
            RequestedMode = CapabilityExecutionMode.Wait,
            RequestedAtUtc = requestedAtUtc,
            Arguments = invocationArguments
        };

        using var scope = OperationContext.PushOperation(operationId, descriptor.Id);
        try
        {
            var envelope = registration.Handler(invocation, cancellationToken).GetAwaiter().GetResult()
                ?? OperationEnvelope.Completed(operationId, descriptor.Id, requestedAtUtc, result: null);

            NormalizeEnvelope(envelope, operationId, descriptor.Id, requestedAtUtc);
            _journal?.RecordCompleted(envelope);
            return envelope;
        }
        catch (OperationCanceledException ex)
        {
            var cancelled = OperationEnvelope.Cancelled(operationId, descriptor.Id, requestedAtUtc, CreateError("capability.cancelled", ex));
            _journal?.RecordCompleted(cancelled);
            return cancelled;
        }
        catch (TimeoutException ex)
        {
            var timedOut = OperationEnvelope.TimedOut(operationId, descriptor.Id, requestedAtUtc, CreateError("capability.timed_out", ex));
            _journal?.RecordCompleted(timedOut);
            return timedOut;
        }
        catch (Exception ex)
        {
            var failed = OperationEnvelope.Failed(operationId, descriptor.Id, requestedAtUtc, CreateError("capability.failed", ex));

            _journal?.RecordCompleted(failed);
            return failed;
        }
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

    private static void NormalizeEnvelope(OperationEnvelope envelope, string operationId, string capabilityId, DateTimeOffset startedAtUtc)
    {
        envelope.OperationId = operationId;
        envelope.CapabilityId = capabilityId;

        if (envelope.StartedAtUtc == default)
            envelope.StartedAtUtc = startedAtUtc;

        if (envelope.Status is OperationStatus.Completed or OperationStatus.Failed or OperationStatus.Cancelled or OperationStatus.TimedOut)
        {
            envelope.CompletedAtUtc ??= DateTimeOffset.UtcNow;
            envelope.DurationMs ??= (long)(envelope.CompletedAtUtc.Value - envelope.StartedAtUtc).TotalMilliseconds;
        }
    }

    private static OperationError CreateError(string code, Exception exception)
    {
        var root = exception.InnerException ?? exception;
        return new OperationError
        {
            Code = code,
            Message = root.Message,
            ExceptionType = root.GetType().FullName ?? root.GetType().Name,
            Details = exception.ToString()
        };
    }
}
