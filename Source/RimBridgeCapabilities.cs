using RimBridgeServer.Core;
using RimBridgeServer.Contracts;

namespace RimBridgeServer;

internal static class RimBridgeCapabilities
{
    public static CapabilityRegistry Registry { get; private set; }

    public static void Initialize()
    {
        if (Registry != null)
            return;

        var registry = new CapabilityRegistry();
        registry.RegisterProvider(new BuiltInCapabilityModuleProvider(
            providerId: "rimbridge.core/diagnostics",
            category: "diagnostics",
            module: new DiagnosticsCapabilityModule(),
            aliasMetadataType: typeof(RimBridgeTools),
            source: CapabilitySourceKind.Core));
        registry.RegisterProvider(new BuiltInCapabilityModuleProvider(
            providerId: "rimbridge.core/lifecycle",
            category: "lifecycle",
            module: new LifecycleCapabilityModule(),
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

        Registry = registry;
        LegacyToolExecution.Initialize(registry);
    }
}
