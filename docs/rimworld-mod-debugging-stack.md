# RimWorld Mod Debugging Stack

This is the shortest practical setup for the RimWorld mod-debugging workflow used in live repro-and-fix sessions.

## What Each Repo Does

1. `Achtung2` or your target mod: the code you are actually debugging
2. `RimBridgeServer`: runs inside RimWorld and exposes live game tools
3. `GABS`: starts RimWorld and bridges the live tool surface into your MCP client
4. `DecompilerServer`: decompiles RimWorld and mod assemblies so you can inspect vanilla and patched code paths

Relevant repos:

- `https://github.com/pardeike/Achtung2`
- `https://github.com/pardeike/RimBridgeServer`
- `https://github.com/pardeike/GABS`
- `https://github.com/pardeike/DecompilerServer`

## Install Order

1. Install Harmony in RimWorld.
2. Install the target mod you want to debug.
3. Install RimBridgeServer into RimWorld's `Mods` folder and enable it.
4. Install GABS on the host machine and configure a `rimworld` game entry.
5. Install DecompilerServer in your MCP client if you want code inspection during the debug session.
6. Start RimWorld through GABS, wait for the game to connect, then begin using the live tools.

## Minimum Mental Model

- `RimBridgeServer` lives inside the game.
- `GABS` starts the game and connects to the in-game bridge.
- `DecompilerServer` is separate and only helps you read code.
- Your target mod stays an ordinary RimWorld mod in the normal load order.

## Reproducing A Mod Bug Cleanly

1. Prepare a save or a tiny scenario that reproduces the issue.
2. Use GABS to start RimWorld and wait for `rimbridge/wait_for_game_loaded`.
3. Use RimBridgeServer tools to inspect pawns, jobs, map state, UI state, saves, or screenshots.
4. Use DecompilerServer to inspect the RimWorld or mod code path you suspect.
5. Patch the target mod, rebuild it into the RimWorld `Mods` folder, restart through GABS, and rerun the same scenario.
