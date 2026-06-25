# RimBridgeServer.Sdk

Compile-time SDK for RimBridgeServer companion tool assemblies.

Use this package from a companion DLL that is loaded by RimBridgeServer at runtime from a `BridgeTools` folder. The package supplies the `[Tool]` annotations and the optional async runtime interfaces that companion tools can request as injected parameters.

Typical companion tools use:

- `[Tool]`, `[ToolParameter]`, and `[ToolResponse]` for public tool metadata
- `IRimBridgeContext` for dynamic tool discovery and calls
- `IRimBridgeGameClock` through `ctx.Game` for real frame/tick waits
- `IRimBridgeToolClient` through `ctx.Tools` for `List`, `Get`, `CallAsync`, and `QueueAsync`

RimBridgeServer resolves this assembly to the copy shipped by the running mod. Companion projects should reference this package for compilation, but should not deploy `RimBridgeServer.Sdk.dll` beside the companion DLL.
