# RimBridgeServer.Sdk

Compile-time SDK for RimBridgeServer companion tool assemblies.

Use this package from a companion DLL that is loaded by RimBridgeServer at runtime from a `BridgeTools` folder. The package supplies the `[Tool]` annotations and the optional async runtime interfaces that companion tools can request as injected parameters.

Typical companion tools use:

- `[Tool]`, `[ToolParameter]`, and `[ToolResponse]` for public tool metadata
- `IRimBridgeContext` for dynamic tool discovery and calls
- `IRimBridgeGameClock` through `ctx.Game` for real frame/tick waits
- `IRimBridgeToolClient` through `ctx.Tools` for `List`, `Get`, dynamic `CallAsync`, typed `CallAsync<T>`, and `QueueAsync`
- `RimBridgeToolCallResult` helpers such as `Succeeded()`, `PayloadSuccess()`, `ReadResult<T>(...)`, and `TryReadResult<T>(...)`
- `RimBridgeEvidenceManifest` plus `RimBridgeEvidence` assertion helpers for repeatable evidence suites that return screenshots, assertions, errors, and environment details

RimBridgeServer resolves this assembly to the copy shipped by the running mod. Companion projects should reference this package for compilation, but should not deploy `RimBridgeServer.Sdk.dll` beside the companion DLL.

Dynamic calls are useful when the called tool returns a dictionary-shaped or anonymous-object payload:

```csharp
var load = await ctx.Tools.CallAsync(
    "rimworld/load_game_ready",
    new { saveName = "fixture", readiness = "visual", pauseIfNeeded = true },
    cancellationToken: cancellationToken);
if (!load.Succeeded())
    return new { success = false, error = load.Error, load = load.Result };

var programState = load.ReadResult<string>("state", "programState");
```

Typed calls are better when the companion owns a stable DTO:

```csharp
var load = await ctx.Tools.CallAsync<LoadGameReadyResult>(
    "rimworld/load_game_ready",
    new { saveName = "fixture", readiness = "visual", pauseIfNeeded = true },
    cancellationToken: cancellationToken);
if (!load.Succeeded())
    return new { success = false, error = load.Error, load = load.Result };
```

After GABS reports the bridge tool surface, companion harnesses can call `rimworld/load_game_ready` for a prepared save or `rimworld/start_debug_game_ready` for a fresh dev colony directly. Those tools queue RimWorld long-event work and wait to the requested readiness target, so a separate pre-call to `rimbridge/wait_for_game_loaded` is only useful when a game should already be loaded.

Evidence suites can return a stable manifest:

```csharp
var manifest = RimBridgeEvidence.CreateManifest("mymod/render-sweep", runId);
manifest.saveName = saveName;
manifest.assertions.Add(RimBridgeEvidence.ToolSucceeded("load save", load));
manifest.captures.Add(new RimBridgeEvidenceCapture
{
    label = "north",
    kind = "cell_rect",
    path = shot.TryReadResult<string>(out var path, "path") ? path : string.Empty,
    details = shot.Result
});
RimBridgeEvidence.Complete(manifest);
return manifest;
```

If a companion tool fails after rebuilding against a different SDK shape, call `rimbridge/get_bridge_status` in the running game. RimBridgeServer reports the host SDK version and per-companion discovery diagnostics, including local SDK-copy warnings and load/register errors.
