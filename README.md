# RimBridgeServer

RimBridgeServer runs a GABP (Game Agent Bridge Protocol) server inside RimWorld so AI agents and external tools can remotely control and observe a running game. The goal is to make automated testing of in‑development mods straightforward: load saves, perform actions, query world state, and validate outcomes via a stable protocol.

Key points:
- Uses **Lib.GAB** for GABP 1.0 compliant server implementation
- **GABS Integration**: Automatically detects and integrates with [GABS](https://github.com/pardeike/GABS) environment
- **TCP Transport**: Listens on 127.0.0.1 with automatic port configuration  
- **Attribute-Based Tools**: Simple tool registration using C# attributes
- **Minimal Codebase**: Leverages Lib.GAB to eliminate complex protocol handling
- Targets RimWorld 1.6

## Quick Start (GABP over TCP)

### Running with GABS

When launched by GABS, RimBridgeServer automatically configures itself using environment variables:

- `GABS_GAME_ID`: Game identifier
- `GABP_SERVER_PORT`: Port to listen on  
- `GABP_TOKEN`: Authentication token

No manual configuration needed!

### Standalone Usage

When running standalone, RimBridgeServer will:
1. Automatically select an available port (default fallback: 5174)
2. Generate a random authentication token
3. Log the connection details to RimWorld's console

Check the RimWorld log for connection information:
```
[RimBridge] GABP server running standalone on port 5174
[RimBridge] Bridge token: abc123...
```

## Available Tools

RimBridgeServer provides these built-in tools:

### Core Tools
- `rimbridge.core/ping` - Connectivity test, returns "pong"

### RimWorld Tools  
- `rimworld/get_game_info` - Get current game status and basic information
- `rimworld/pause_game` - Pause or unpause the game

## Protocol

RimBridgeServer implements GABP 1.0 specification:

1. **Connect** via TCP to the server port
2. **Authenticate** using `session/hello` with the token
3. **List tools** using `tools/list` 
4. **Call tools** using `tools/call`
5. **Subscribe to events** using `events/subscribe`

See the [GABP specification](https://github.com/pardeike/GABS) for complete protocol details.

## Build

- `dotnet build` (Debug or Release)
- Optionally set `RIMWORLD_MOD_DIR` to copy the built mod to your local Mods folder and zip it (see `Source/RimBridgeServer.csproj`).

## Layout

- `About/` mod metadata
- `1.6/Assemblies/` build output including Lib.GAB.dll
- `Source/` project: simplified mod entry (`Mod.cs`) and project file
- `lib/` local build artifacts (Lib.GAB.dll)
- `.vscode/` quick build tasks

## License

MIT — see `LICENSE`.

## Dependencies

This mod uses [Lib.GAB](https://github.com/pardeike/Lib.GAB) as a local build artifact to provide GABP server functionality. Lib.GAB is included in the mod's assemblies directory.

## Migration from MCP

Previous versions used a custom MCP (Model Context Protocol) implementation over HTTP. This version replaces it with:

- **GABP over TCP** instead of MCP over HTTP
- **Lib.GAB** instead of custom protocol implementation  
- **Port 5174** (fallback) instead of fixed port
- **Token-based auth** instead of bearer tokens from ~/.api-keys
- **GABS integration** for automated AI control scenarios

The core functionality remains the same - AI agents can still control RimWorld remotely.
