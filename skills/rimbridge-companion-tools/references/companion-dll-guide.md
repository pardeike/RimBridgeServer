# Companion DLL Guide

Use this reference when adding or migrating RimBridgeServer 2.x companion tools in an existing RimWorld mod.

## Load Model

- RimBridgeServer starts late enough that normal mod assemblies are already loaded.
- RimBridgeServer then loads companions from explicit `BridgeTools` folders.
- Global companions load first from the game-root `BridgeTools` folder. Loose DLLs are allowed; first-level bundle folders isolate private helper DLL lookup.
- Mod-specific companions load from each active mod load folder's `BridgeTools` folder, for example `SomeMod/1.6/BridgeTools`.
- `RimBridgeServer.Sdk` always resolves to the SDK assembly shipped by RimBridgeServer. Companion projects reference the SDK for compilation, but should not deploy that DLL.
- Companion dependencies are resolved from the companion bundle directory, the owning `BridgeTools` root, and already loaded mod assemblies.

## SDK Package

Create the local SDK package from the RimBridgeServer checkout:

```bash
scripts/pack-sdk.sh
```

The default output is the sibling NuGet source:

```text
../.nuget-local/RimBridgeServer.Sdk.2.0.0.nupkg
```

For a mod project that consumes local packages, enable the local source in the mod's restore/build flow or add the sibling source to `NuGet.config`.

Recommended SDK package reference in a companion project:

```xml
<PackageReference Include="RimBridgeServer.Sdk" Version="2.0.0" PrivateAssets="all" ExcludeAssets="runtime" />
```

When the companion project lives next to a RimBridgeServer checkout during early development, a `ProjectReference` is also acceptable:

```xml
<ProjectReference Include="..\..\..\RimBridgeServer\Source\RimBridgeServer.Sdk\RimBridgeServer.Sdk.csproj" Private="false" />
```

Keep `Private="false"` or `ExcludeAssets="runtime"` so the companion output does not bundle `RimBridgeServer.Sdk.dll`.

## Companion Project Pattern

Typical layout:

```text
SomeMod/
├── 1.6/
│   ├── Assemblies/SomeMod.dll
│   └── BridgeTools/SomeMod.BridgeTools.dll
└── Source/
    ├── SomeMod.csproj
    ├── SomeModBridgeTools.cs
    └── BridgeTools/SomeMod.BridgeTools.csproj
```

Example companion csproj:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
    <LangVersion>latest</LangVersion>
    <AssemblyName>SomeMod.BridgeTools</AssemblyName>
    <RootNamespace>SomeMod.BridgeTools</RootNamespace>
    <OutputPath>..\..\1.6\BridgeTools\</OutputPath>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="..\SomeModBridgeTools.cs" Link="SomeModBridgeTools.cs" />
  </ItemGroup>

  <ItemGroup>
    <Reference Include="SomeMod">
      <HintPath>$(SomeModAssemblyPath)</HintPath>
      <Private>false</Private>
    </Reference>
    <PackageReference Include="RimBridgeServer.Sdk" Version="2.0.0" PrivateAssets="all" ExcludeAssets="runtime" />
  </ItemGroup>
</Project>
```

If the companion also uses RimWorld APIs directly, copy the owning mod's existing RimWorld reference/publicizer pattern rather than inventing a second one.

## Main Mod Project Wiring

Prevent the main mod project from compiling companion sources:

```xml
<ItemGroup>
  <Compile Remove="BridgeTools\**\*.cs" />
  <None Include="BridgeTools\**\*" />
</ItemGroup>
```

If the companion source file is kept beside the main sources, exclude that file from the main compile:

```xml
<Compile Remove="SomeModBridgeTools.cs" />
```

Build the companion after the main mod DLL exists and before local deploy/copy targets run:

```xml
<Target Name="BuildBridgeTools" AfterTargets="PostBuildAction" BeforeTargets="CopyToRimworld">
  <MSBuild
    Projects="BridgeTools\SomeMod.BridgeTools.csproj"
    Properties="Configuration=$(Configuration);SomeModAssemblyPath=$(TargetPath)" />
</Target>
```

Adjust `AfterTargets` and `BeforeTargets` to match the mod's actual build/deploy target names. The important ordering is:

1. build main mod DLL
2. build companion with a reference to that DLL
3. copy/deploy/zip both `Assemblies` and `BridgeTools`

## Tool Authoring Pattern

```csharp
using System.Threading;
using System.Threading.Tasks;
using RimBridgeServer.Sdk;

public sealed class SomeModBridgeTools
{
    [Tool(
        "somemod/render_pose_sweep",
        Description = "Run a real in-game render sweep and capture screenshots.",
        ResultDescription = "Returns success, pose records, and screenshot paths.")]
    public static async Task<object> RenderPoseSweep(
        IRimBridgeContext ctx,
        CancellationToken cancellationToken,
        [ToolParameter(Description = "Save name to load first", DefaultValue = "fixture")] string saveName = "fixture",
        [ToolParameter(Description = "Ticks to advance per pose", DefaultValue = 1)] int poseTicks = 1)
    {
        var load = await ctx.Tools.CallAsync<object>(
            "rimworld/load_game_ready",
            new { saveName },
            cancellationToken: cancellationToken);
        if (!load.Success)
            return new { success = false, stage = "load", error = load.Error };

        var screenshotTool = ctx.Tools.Get("rimworld/screenshot_cell_rect");
        var companionTools = ctx.Tools.List(new RimBridgeToolQuery { Text = "somemod/" });

        await ctx.Game.StepTicksAsync(poseTicks, cancellationToken: cancellationToken);

        var shot = await ctx.Tools.CallAsync<object>(
            screenshotTool.Id,
            new { fileName = "somemod-pose-01", cellX = 100, cellZ = 100, paddingCells = 2 },
            cancellationToken: cancellationToken);

        return new
        {
            success = shot.Success,
            toolQuery = new { matchingCapabilityCount = companionTools.Count, screenshotTool = screenshotTool.Id },
            screenshot = shot.Result,
            ticked = poseTicks
        };
    }
}
```

Notes:

- `IRimBridgeContext` and `CancellationToken` are injected and are not exposed in the public tool schema.
- `ctx.Tools.CallAsync<T>` returns `RimBridgeToolCallResult<T>` with `Success`, `Result`, and `Error`.
- `ctx.Tools.Get` throws if the tool is missing; use `Exists` when optional behavior is acceptable.
- `ctx.Tools.QueueAsync` is for starting a registered operation in the background and returning an operation id.
- Use `ctx.Game.StepTicksAsync` for paused deterministic ticks and `ctx.Game.RunForTicksAsync` or `RunUntilAsync` when the game needs real running time.
- Avoid arbitrary sleeps. Let RimBridgeServer advance frames/ticks through the SDK.

## Validation Checklist

Source validation:

```bash
dotnet restore Source/BridgeTools/SomeMod.BridgeTools.csproj
dotnet build Source/SomeMod.csproj -c Release /p:RIMWORLD_MOD_DIR=
```

Deploy validation:

- Confirm `1.6/BridgeTools/SomeMod.BridgeTools.dll` exists in the repo output.
- Confirm the deployed mod folder contains the same `BridgeTools` DLL.
- Confirm `RimBridgeServer.Sdk.dll` is not copied into the mod-specific `BridgeTools` folder.

Live validation through GABS:

1. Start or connect to the configured RimWorld game.
2. Use `games_tool_names` with a query or prefix for the mod namespace.
3. Confirm the companion tool appears.
4. Inspect it with `games_tool_detail` and verify defaults/schema.
5. Call the high-level harness tool with a generous timeout.
6. Verify returned `success`, artifact paths, and any expected screenshots/logs/state.

If tools are missing, inspect RimWorld logs for `[RimBridge]` companion discovery warnings and verify the mod is enabled in the active `ModsConfig.xml`.
