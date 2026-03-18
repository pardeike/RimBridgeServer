using System;
using System.Collections.Generic;
using RimBridgeServer.Core;
using RimBridgeServer.Contracts;

namespace RimBridgeServer;

internal static class RimBridgeCapabilities
{
    public static CapabilityRegistry Registry { get; private set; }

    public static OperationJournal Journal { get; private set; }

    public static LogJournal LogJournal { get; private set; }

    public static IReadOnlyList<AnnotatedExtensionCapabilityProvider.DiscoveredTool> ExtensionTools { get; private set; } = [];

    public static void Initialize()
    {
        if (Registry != null)
            return;

        var journal = new OperationJournal();
        var logJournal = new LogJournal();
        var registry = new CapabilityRegistry(journal);
        registry.RegisterProvider(new BuiltInCapabilityModuleProvider(
            providerId: "rimbridge.core/diagnostics",
            category: "diagnostics",
            module: new DiagnosticsCapabilityModule(journal, logJournal, registry),
            aliasMetadataType: typeof(RimBridgeTools),
            source: CapabilitySourceKind.Core));
        registry.RegisterProvider(new BuiltInCapabilityModuleProvider(
            providerId: "rimbridge.core/scripting",
            category: "scripting",
            module: new ScriptingCapabilityModule(registry),
            aliasMetadataType: typeof(RimBridgeTools),
            source: CapabilitySourceKind.Core));
        registry.RegisterProvider(new BuiltInCapabilityModuleProvider(
            providerId: "rimbridge.core/lifecycle",
            category: "lifecycle",
            module: new LifecycleCapabilityModule(),
            aliasMetadataType: typeof(RimBridgeTools),
            source: CapabilitySourceKind.Core));
        registry.RegisterProvider(new BuiltInCapabilityModuleProvider(
            providerId: "rimbridge.core/debug_actions",
            category: "debug_actions",
            module: new DebugActionsCapabilityModule(),
            aliasMetadataType: typeof(RimBridgeTools),
            source: CapabilitySourceKind.Core));
        registry.RegisterProvider(new BuiltInCapabilityModuleProvider(
            providerId: "rimbridge.core/mod_configuration",
            category: "mod_configuration",
            module: new ModConfigurationCapabilityModule(),
            aliasMetadataType: typeof(RimBridgeTools),
            source: CapabilitySourceKind.Core));
        registry.RegisterProvider(new BuiltInCapabilityModuleProvider(
            providerId: "rimbridge.core/mod_settings",
            category: "mod_settings",
            module: new ModSettingsCapabilityModule(),
            aliasMetadataType: typeof(RimBridgeTools),
            source: CapabilitySourceKind.Core));
        registry.RegisterProvider(new BuiltInCapabilityModuleProvider(
            providerId: "rimbridge.optional/architect",
            category: "architect",
            module: new ArchitectCapabilityModule(),
            aliasMetadataType: typeof(RimBridgeTools),
            source: CapabilitySourceKind.Optional));
        registry.RegisterProvider(new BuiltInCapabilityModuleProvider(
            providerId: "rimbridge.core/input",
            category: "input",
            module: new InputCapabilityModule(),
            aliasMetadataType: typeof(RimBridgeTools),
            source: CapabilitySourceKind.Core));
        registry.RegisterProvider(new BuiltInCapabilityModuleProvider(
            providerId: "rimbridge.core/selection",
            category: "selection",
            module: new SelectionCapabilityModule(),
            aliasMetadataType: typeof(RimBridgeTools),
            source: CapabilitySourceKind.Core));
        registry.RegisterProvider(new BuiltInCapabilityModuleProvider(
            providerId: "rimbridge.core/inspection",
            category: "inspection",
            module: new SelectionSemanticsCapabilityModule(),
            aliasMetadataType: typeof(RimBridgeTools),
            source: CapabilitySourceKind.Core));
        registry.RegisterProvider(new BuiltInCapabilityModuleProvider(
            providerId: "rimbridge.core/notifications",
            category: "notifications",
            module: new NotificationCapabilityModule(),
            aliasMetadataType: typeof(RimBridgeTools),
            source: CapabilitySourceKind.Core));
        registry.RegisterProvider(new BuiltInCapabilityModuleProvider(
            providerId: "rimbridge.core/view",
            category: "view",
            module: new ViewCapabilityModule(),
            aliasMetadataType: typeof(RimBridgeTools),
            source: CapabilitySourceKind.Core));
        registry.RegisterProvider(new BuiltInCapabilityModuleProvider(
            providerId: "rimbridge.optional/context_menu",
            category: "context_menu",
            module: new ContextMenuCapabilityModule(),
            aliasMetadataType: typeof(RimBridgeTools),
            source: CapabilitySourceKind.Optional));
        var extensionProviders = RimBridgeExtensionDiscovery.DiscoverProviders();
        var extensionTools = new List<AnnotatedExtensionCapabilityProvider.DiscoveredTool>();

        foreach (var provider in extensionProviders)
        {
            try
            {
                registry.RegisterProvider(provider);
                extensionTools.AddRange(provider.Tools);
            }
            catch (Exception ex)
            {
                Verse.Log.Error($"[RimBridge] Failed to register annotated extension provider '{provider.ProviderId}': {ex}");
            }
        }

        Journal = journal;
        LogJournal = logJournal;
        ExtensionTools = extensionTools;
        Registry = registry;
        LegacyToolExecution.Initialize(registry);
    }
}
