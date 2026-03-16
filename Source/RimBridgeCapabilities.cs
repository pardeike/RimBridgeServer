using RimBridgeServer.Core;
using RimBridgeServer.Contracts;

namespace RimBridgeServer;

internal static class RimBridgeCapabilities
{
    public static CapabilityRegistry Registry { get; private set; }

    public static OperationJournal Journal { get; private set; }

    public static LogJournal LogJournal { get; private set; }

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
            module: new DiagnosticsCapabilityModule(journal, logJournal),
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

        Journal = journal;
        LogJournal = logJournal;
        Registry = registry;
        LegacyToolExecution.Initialize(registry);
    }
}
