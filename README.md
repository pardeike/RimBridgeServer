# RimBridgeServer

RimBridgeServer runs an MCP server inside RimWorld so AI agents and external tools can remotely control and observe a running game. The goal is to make automated testing of in‑development mods straightforward: load saves, perform actions, query world state, and validate outcomes via a stable protocol.

Key points:
- Modular design: core hosting with pluggable capability modules (features, transports, and message formats can evolve independently).
- Includes a basic MCP implementation with a built‑in `ping` tool for connectivity testing.
- HTTP endpoint (default): `http://127.0.0.1:5174/mcp/`
- Targets RimWorld 1.6.

## Quick Start (MCP over HTTP)

1) Initialize session

curl example:

```
curl -sS -X POST \
  -H "Content-Type: application/json" \
  -d '{
        "jsonrpc": "2.0",
        "id": 1,
        "method": "initialize",
        "params": { "protocolVersion": "2025-06-18" }
      }' \
  http://127.0.0.1:5174/mcp/
```

2) List tools

```
curl -sS -X POST \
  -H "Content-Type: application/json" \
  -d '{ "jsonrpc": "2.0", "id": 2, "method": "tools/list", "params": {} }' \
  http://127.0.0.1:5174/mcp/
```

3) Call built‑in ping tool

```
curl -sS -X POST \
  -H "Content-Type: application/json" \
  -H "MCP-Protocol-Version: 2025-06-18" \
  -d '{
        "jsonrpc": "2.0",
        "id": 3,
        "method": "tools/call",
        "params": { "name": "rimbridge.core/ping", "arguments": {} }
      }' \
  http://127.0.0.1:5174/mcp/
```

Expected response includes content with text "pong".

Notes:
- A legacy convenience method `ping` also exists and returns an empty result.
- Optional auth: bearer token support is stubbed; by default no token is required.
- CORS/Origin checks allow `null`, `file://`, `app://` by default.

## Build

- `dotnet build` (Debug or Release)
- Optionally set `RIMWORLD_MOD_DIR` to copy the built mod to your local Mods folder and zip it (see `Source/RimBridgeServer.csproj`).

## Layout

- `About/` mod metadata
- `1.6/Assemblies/` build output
- `Source/` project: MCP server (`Net.cs`), protocol types (`Protocol.cs`), abstractions (`Abstractions.cs`), plugins (`Plugins.cs`), mod entry (`Mod.cs`)
- `.vscode/` quick build tasks

## License

MIT — see `LICENSE`.
