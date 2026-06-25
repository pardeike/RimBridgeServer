using System;
using System.Collections.Generic;

namespace RimBridgeServer.Sdk;

public sealed class RimBridgeEvidenceManifest
{
    public bool success { get; set; }

    public string suite { get; set; } = string.Empty;

    public string runId { get; set; } = string.Empty;

    public DateTimeOffset startedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? completedAtUtc { get; set; }

    public string saveName { get; set; } = string.Empty;

    public string bridgeVersion { get; set; } = string.Empty;

    public string sdkVersion { get; set; } = string.Empty;

    public string gameVersion { get; set; } = string.Empty;

    public string modVersion { get; set; } = string.Empty;

    public RimBridgeEvidenceEnvironment environment { get; set; } = new();

    public List<RimBridgeEvidenceCapture> captures { get; set; } = [];

    public List<RimBridgeEvidenceAssertion> assertions { get; set; } = [];

    public List<RimBridgeEvidenceError> errors { get; set; } = [];

    public List<RimBridgeEvidenceLogEntry> logs { get; set; } = [];
}

public sealed class RimBridgeEvidenceEnvironment
{
    public string bridgeVersion { get; set; } = string.Empty;

    public string sdkVersion { get; set; } = string.Empty;

    public string gameVersion { get; set; } = string.Empty;

    public string modVersion { get; set; } = string.Empty;

    public string saveName { get; set; } = string.Empty;

    public string mapId { get; set; } = string.Empty;

    public object details { get; set; }
}

public sealed class RimBridgeEvidenceCapture
{
    public bool success { get; set; } = true;

    public string label { get; set; } = string.Empty;

    public string kind { get; set; } = string.Empty;

    public string path { get; set; } = string.Empty;

    public DateTimeOffset? capturedAtUtc { get; set; }

    public object details { get; set; }
}

public sealed class RimBridgeEvidenceAssertion
{
    public bool success { get; set; }

    public string name { get; set; } = string.Empty;

    public string message { get; set; } = string.Empty;

    public object expected { get; set; }

    public object actual { get; set; }

    public object details { get; set; }
}

public sealed class RimBridgeEvidenceError
{
    public string stage { get; set; } = string.Empty;

    public string message { get; set; } = string.Empty;

    public object details { get; set; }
}

public sealed class RimBridgeEvidenceLogEntry
{
    public string level { get; set; } = string.Empty;

    public string message { get; set; } = string.Empty;

    public object details { get; set; }
}

public static class RimBridgeEvidence
{
    public static RimBridgeEvidenceManifest CreateManifest(string suite, string runId = null)
    {
        return new RimBridgeEvidenceManifest
        {
            suite = suite ?? string.Empty,
            runId = string.IsNullOrWhiteSpace(runId) ? Guid.NewGuid().ToString("N") : runId,
            startedAtUtc = DateTimeOffset.UtcNow,
            sdkVersion = typeof(ToolAttribute).Assembly.GetName().Version?.ToString() ?? string.Empty,
            environment = new RimBridgeEvidenceEnvironment
            {
                sdkVersion = typeof(ToolAttribute).Assembly.GetName().Version?.ToString() ?? string.Empty
            }
        };
    }

    public static RimBridgeEvidenceAssertion Pass(string name, string message = null, object details = null)
    {
        return new RimBridgeEvidenceAssertion
        {
            success = true,
            name = name ?? string.Empty,
            message = message ?? string.Empty,
            details = details
        };
    }

    public static RimBridgeEvidenceAssertion Fail(string name, string message, object expected = null, object actual = null, object details = null)
    {
        return new RimBridgeEvidenceAssertion
        {
            success = false,
            name = name ?? string.Empty,
            message = message ?? string.Empty,
            expected = expected,
            actual = actual,
            details = details
        };
    }

    public static RimBridgeEvidenceAssertion IsTrue(string name, bool condition, string message = null, object details = null)
    {
        return condition
            ? Pass(name, message, details)
            : Fail(name, message ?? "Expected condition to be true.", expected: true, actual: false, details: details);
    }

    public static RimBridgeEvidenceAssertion AreEqual<T>(string name, T expected, T actual, string message = null, object details = null)
    {
        return EqualityComparer<T>.Default.Equals(expected, actual)
            ? Pass(name, message, details)
            : Fail(name, message ?? "Expected values to be equal.", expected, actual, details);
    }

    public static RimBridgeEvidenceAssertion ToolSucceeded(string name, IRimBridgeToolCallResult result, string message = null)
    {
        return result.Succeeded()
            ? Pass(name, message, new
            {
                operationId = result.OperationId,
                capabilityId = result.CapabilityId,
                status = result.Status
            })
            : Fail(name, message ?? "Expected RimBridge tool call to succeed.", expected: true, actual: result?.Success, details: result);
    }

    public static bool AllPassed(IEnumerable<RimBridgeEvidenceAssertion> assertions)
    {
        if (assertions == null)
            return true;

        foreach (var assertion in assertions)
        {
            if (assertion?.success == false)
                return false;
        }

        return true;
    }

    public static void Complete(RimBridgeEvidenceManifest manifest)
    {
        if (manifest == null)
            return;

        manifest.completedAtUtc = DateTimeOffset.UtcNow;
        manifest.success = AllPassed(manifest.assertions) && manifest.errors.Count == 0;
    }
}
