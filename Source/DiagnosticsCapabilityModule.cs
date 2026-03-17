using System;
using System.Linq;
using RimBridgeServer.Core;
using Verse;

namespace RimBridgeServer;

internal sealed class DiagnosticsCapabilityModule
{
    private readonly CapabilityRegistry _registry;
    private readonly OperationJournal _journal;
    private readonly LogJournal _logJournal;
    private readonly ConditionWaiter _waiter = new();

    public DiagnosticsCapabilityModule(OperationJournal journal, LogJournal logJournal, CapabilityRegistry registry)
    {
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _logJournal = logJournal ?? throw new ArgumentNullException(nameof(logJournal));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public object Ping()
    {
        return new { message = "pong", timestamp = DateTime.UtcNow };
    }

    public object GetGameInfo()
    {
        var currentGame = Current.Game;
        if (currentGame == null)
        {
            return new { status = "no_game", message = "No game is currently loaded" };
        }

        return new
        {
            status = "game_loaded",
            ticksGame = currentGame.tickManager.TicksGame,
            mapCount = currentGame.Maps?.Count ?? 0,
            selectedPawns = Find.Selector.SelectedPawns.Select(pawn => pawn.Name?.ToStringShort ?? pawn.LabelShort).ToList()
        };
    }

    public object GetOperation(string operationId)
    {
        var operation = _journal.GetOperation(operationId);
        if (operation == null)
            return new { success = false, message = $"Operation '{operationId}' was not found in the journal." };

        return new
        {
            success = true,
            trackedOperation = operation
        };
    }

    public object GetBridgeStatus()
    {
        return RimWorldWaits.GetBridgeStatus(_journal, _logJournal);
    }

    public object ListCapabilities(int limit = 200, string providerId = null, string category = null, string source = null, string query = null, bool includeParameters = true)
    {
        if (limit <= 0)
            return new { success = true, totalCount = 0, returnedCount = 0, capabilities = Array.Empty<object>() };

        var descriptors = _registry
            .GetCapabilities()
            .Where(descriptor => string.IsNullOrWhiteSpace(providerId) || string.Equals(descriptor.ProviderId, providerId, StringComparison.Ordinal))
            .Where(descriptor => string.IsNullOrWhiteSpace(category) || string.Equals(descriptor.Category, category, StringComparison.OrdinalIgnoreCase))
            .Where(descriptor => string.IsNullOrWhiteSpace(source) || string.Equals(descriptor.Source.ToString(), source, StringComparison.OrdinalIgnoreCase))
            .Where(descriptor => string.IsNullOrWhiteSpace(query) || MatchesCapabilityQuery(descriptor, query))
            .ToList();

        var returned = descriptors
            .Take(limit)
            .Select(descriptor => DescribeCapability(descriptor, includeParameters))
            .ToList();

        return new
        {
            success = true,
            totalCount = descriptors.Count,
            returnedCount = returned.Count,
            truncated = returned.Count < descriptors.Count,
            capabilities = returned
        };
    }

    public object GetCapability(string capabilityIdOrAlias)
    {
        try
        {
            var descriptor = _registry.ResolveDescriptor(capabilityIdOrAlias);
            return new
            {
                success = true,
                requestedId = capabilityIdOrAlias,
                capability = DescribeCapability(descriptor, includeParameters: true)
            };
        }
        catch (Exception ex)
        {
            return new
            {
                success = false,
                message = ex.Message,
                requestedId = capabilityIdOrAlias
            };
        }
    }

    public object ListOperations(int limit = 20, bool includeResults = false)
    {
        return new
        {
            operations = _journal.GetRecentOperations(limit, includeResults)
        };
    }

    public object ListOperationEvents(int limit = 50, string eventType = null, long afterSequence = 0, string operationId = null, bool includeDiagnostics = false)
    {
        var events = _journal.GetRecentEvents(Math.Max(limit * 4, limit), eventType, afterSequence, operationId);
        if (includeDiagnostics == false)
        {
            events = events
                .Where(entry => entry.CapabilityId.StartsWith("rimbridge.core/diagnostics/", StringComparison.Ordinal) == false)
                .ToList();
        }

        return new
        {
            events = events.Take(limit).ToList()
        };
    }

    public object ListLogs(int limit = 50, string minimumLevel = "info", long afterSequence = 0, string operationId = null, string rootOperationId = null, string capabilityId = null)
    {
        return new
        {
            logs = _logJournal.GetEntries(limit, minimumLevel, afterSequence, operationId, rootOperationId, capabilityId)
        };
    }

    public object WaitForOperation(string operationId, int timeoutMs = 10000, int pollIntervalMs = 50)
    {
        var outcome = _waiter.WaitUntil(() =>
        {
            var operation = _journal.GetOperation(operationId);
            if (operation == null)
            {
                return new WaitProbeResult
                {
                    IsSatisfied = false,
                    Message = $"Waiting for operation '{operationId}' to appear in the journal."
                };
            }

            var satisfied = operation.Status is Contracts.OperationStatus.Completed
                or Contracts.OperationStatus.Failed
                or Contracts.OperationStatus.Cancelled
                or Contracts.OperationStatus.TimedOut;

            return new WaitProbeResult
            {
                IsSatisfied = satisfied,
                Message = satisfied
                    ? $"Operation '{operationId}' reached status {operation.Status}."
                    : $"Waiting for operation '{operationId}' to reach a terminal status.",
                Snapshot = operation
            };
        }, new WaitOptions
        {
            TimeoutMs = timeoutMs,
            PollIntervalMs = pollIntervalMs,
            TimeoutMessage = $"Timed out waiting for operation '{operationId}'."
        });

        return new
        {
            success = outcome.Satisfied,
            satisfied = outcome.Satisfied,
            message = outcome.Message,
            elapsedMs = outcome.ElapsedMs,
            attempts = outcome.Attempts,
            probeFailureCount = outcome.ProbeFailureCount,
            lastProbeError = string.IsNullOrWhiteSpace(outcome.LastProbeError) ? null : outcome.LastProbeError,
            trackedOperation = outcome.Snapshot
        };
    }

    public object WaitForGameLoaded(int timeoutMs = 30000, int pollIntervalMs = 100, bool waitForScreenFade = true, bool pauseIfNeeded = false)
    {
        return RimWorldWaits.WaitForGameLoaded(timeoutMs, pollIntervalMs, waitForScreenFade, pauseIfNeeded);
    }

    public object WaitForLongEventIdle(int timeoutMs = 30000, int pollIntervalMs = 100)
    {
        return RimWorldWaits.WaitForLongEventIdle(timeoutMs, pollIntervalMs);
    }

    private static bool MatchesCapabilityQuery(Contracts.CapabilityDescriptor descriptor, string query)
    {
        var needle = query.Trim();
        if (needle.Length == 0)
            return true;

        return Contains(descriptor.Id, needle)
            || Contains(descriptor.ProviderId, needle)
            || Contains(descriptor.Category, needle)
            || Contains(descriptor.Title, needle)
            || Contains(descriptor.Summary, needle)
            || descriptor.Aliases.Any(alias => Contains(alias, needle))
            || descriptor.Parameters.Any(parameter => Contains(parameter.Name, needle) || Contains(parameter.Description, needle));
    }

    private static bool Contains(string value, string needle)
    {
        return string.IsNullOrWhiteSpace(value) == false
            && value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static object DescribeCapability(Contracts.CapabilityDescriptor descriptor, bool includeParameters)
    {
        return new
        {
            id = descriptor.Id,
            providerId = descriptor.ProviderId,
            category = descriptor.Category,
            title = descriptor.Title,
            summary = descriptor.Summary,
            source = descriptor.Source.ToString(),
            executionKind = descriptor.ExecutionKind.ToString(),
            supportedModes = ExpandSupportedModes(descriptor.SupportedModes),
            defaultRequestedMode = Contracts.CapabilityExecutionMode.Wait.ToString(),
            emitsEvents = descriptor.EmitsEvents,
            resultType = descriptor.ResultType,
            aliases = descriptor.Aliases,
            parameters = includeParameters
                ? descriptor.Parameters.Select(parameter => new
                {
                    name = parameter.Name,
                    parameterType = parameter.ParameterType,
                    description = parameter.Description,
                    required = parameter.Required,
                    defaultValue = parameter.DefaultValue
                }).ToList()
                : null
        };
    }

    private static string[] ExpandSupportedModes(Contracts.CapabilityExecutionMode supportedModes)
    {
        return Enum
            .GetValues(typeof(Contracts.CapabilityExecutionMode))
            .Cast<Contracts.CapabilityExecutionMode>()
            .Where(mode => mode != Contracts.CapabilityExecutionMode.None && supportedModes.SupportsMode(mode))
            .Select(mode => mode.ToString())
            .ToArray();
    }
}
