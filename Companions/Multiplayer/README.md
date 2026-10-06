# Multiplayer controls

This optional companion exposes the installed Multiplayer mod's native local
hosting and joining flow through RimBridgeServer. Total Fog does not reference
it. It has no hard Multiplayer dependency and reports `available: false` when
Multiplayer is not loaded.

Build with `scripts/build-mod.sh`. Deploy with
`scripts/deploy-local-mod.sh --mods-dir <active Mods folder>` after stopping
RimWorld. The main build also builds this companion; deployment puts only its
DLL in the sibling `BridgeTools/Multiplayer` folder. SDK and game reference
DLLs are excluded. These controls are test tools, not player mod payload.

| Tool | Behavior |
| --- | --- |
| `multiplayer/status` | Read availability, assembly identity, native session/player states, desync flag, windows and game tick. |
| `multiplayer/host_local` | Host the loaded single-player map on loopback. Optional port, in-memory username, config synchronization, asynchronous time, multiple factions and diagnostic stack capture. Steam, LAN advertisement and arbiter are off. |
| `multiplayer/join_local` | Join loopback from the main menu using a distinct in-memory username. |
| `multiplayer/leave` | Run native session cleanup and return to the main menu without saving. Retain saves and the process; leave an idle single-player game alone. |
| `multiplayer/set_time_speed` | Submit a native synchronized time command. Supports Paused, Normal, Fast and Superfast with shared synchronous time and no lowest-wins voting. Poll both clients for the result. |
| `multiplayer/save` | Save the paused live session through native Autosaving, verify the new ZIP exists and refuse existing names. |
| `multiplayer/load_save` | Load an existing ZIP through native Replay at its saved endpoint from the main menu. Returns initiation; poll status for replay/game readiness. |

Use separate savedata folders and GABS endpoints for the two processes. Load a
fixture on the host, call `host_local`, and poll `status` until `hostReady` is
true before calling `join_local` on the client. Poll
`status` until the native player states report both players playing. Initiation
success does not prove connection, simulation agreement or compatibility.
Do not use bridge tick stepping or directly mutate gameplay state on one
client to simulate synchronized Multiplayer commands.
Saving requires a joined, non-desynced paused session. Save/load names accept
1..30 ASCII letters, numbers, underscores and hyphens; paths stay in the native
Multiplayer save directory. Loading does not host a server. The resulting
replay can be inspected before choosing a separate hosting action.

Native cleanup reapplies game preferences. RimBridgeServer preserves its existing
runtime background execution setting after those refreshes so an unfocused game
continues serving controls. The player's saved preference is unchanged.

Diagnostic desync stack capture defaults off. Enabling it in the first native
Mac Arm64 session threw `Deferred stack tracing: Unknown function header` from
Multiplayer's RNG postfix and caused subsequent null references. The failed
run is retained separately. This option controls stack capture; native desync
state is still exposed by `status`.

The first implementation targets the native API inspected in Multiplayer
0.11.5 (4a3be27). Missing entry points fail explicitly. All native calls and
state reads run on the game thread. No private network protocol is implemented.
