using System;
using System.Collections.Generic;

namespace RimBridgeServer.Core;

public sealed class CompanionDiscoveryDiagnostic
{
    public string AssemblyPath { get; set; } = string.Empty;

    public string AssemblyName { get; set; } = string.Empty;

    public string AssemblyVersion { get; set; } = string.Empty;

    public string BridgeToolsRoot { get; set; } = string.Empty;

    public string BundleDirectory { get; set; } = string.Empty;

    public string OwnerId { get; set; } = string.Empty;

    public string RootKind { get; set; } = string.Empty;

    public bool IsBundled { get; set; }

    public string ProviderId { get; set; } = string.Empty;

    public string Status { get; set; } = "discovered";

    public bool Success { get; set; }

    public string HostSdkVersion { get; set; } = string.Empty;

    public string HostSdkInformationalVersion { get; set; } = string.Empty;

    public string ReferencedSdkVersion { get; set; } = string.Empty;

    public int ToolClassCount { get; set; }

    public int ToolCount { get; set; }

    public List<string> LocalSdkPaths { get; set; } = [];

    public List<string> Warnings { get; set; } = [];

    public List<string> Errors { get; set; } = [];

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public CompanionDiscoveryDiagnostic Clone()
    {
        return new CompanionDiscoveryDiagnostic
        {
            AssemblyPath = AssemblyPath,
            AssemblyName = AssemblyName,
            AssemblyVersion = AssemblyVersion,
            BridgeToolsRoot = BridgeToolsRoot,
            BundleDirectory = BundleDirectory,
            OwnerId = OwnerId,
            RootKind = RootKind,
            IsBundled = IsBundled,
            ProviderId = ProviderId,
            Status = Status,
            Success = Success,
            HostSdkVersion = HostSdkVersion,
            HostSdkInformationalVersion = HostSdkInformationalVersion,
            ReferencedSdkVersion = ReferencedSdkVersion,
            ToolClassCount = ToolClassCount,
            ToolCount = ToolCount,
            LocalSdkPaths = new List<string>(LocalSdkPaths),
            Warnings = new List<string>(Warnings),
            Errors = new List<string>(Errors),
            UpdatedAtUtc = UpdatedAtUtc
        };
    }
}
