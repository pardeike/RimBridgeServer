# RimBridgeServer

RimBridgeServer runs an MCP server inside RimWorld so AI agents and external tools can remotely control and observe a running game. The goal is to make automated testing of in-development mods straightforward: load saves, perform actions, query world state, and validate outcomes via a stable protocol.

Key points:
- Modular design: core hosting with pluggable capability modules (features, transports, and message formats can evolve independently).
- Targets RimWorld 1.6.
- SDK-style C# project using `Krafs.Rimworld.Ref`, `Lib.Harmony.Ref`, and `TaskPubliciser`.

Status: early skeleton. The core is prepared; add your own server transport and capability modules as needed.

## Build

- `dotnet build` (Debug or Release)
- Optionally set `RIMWORLD_MOD_DIR` to copy the built mod to your local Mods folder and zip it (see `Source/RimBridgeServer.csproj`).

## Layout

- `About/` mod metadata (description updated to reflect MCP server + modular design)
- `1.6/Assemblies/` build output
- `Source/` project and minimal mod entry (`Mod.cs`)
- `.vscode/` quick build tasks

## License

MIT — see `LICENSE`.
