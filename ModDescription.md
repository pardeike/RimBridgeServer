RimBridgeServer turns a running RimWorld game into a live automation bridge.

It is built for external tools, test harnesses, and AI agents that need to inspect and drive the real game instead of guessing from source code.

What it can expose:

- game, map, camera, selection, and UI state
- debug actions, main tabs, saves, and screenshots
- mod settings and load-order control
- automation through JSON or Lua-style front ends
- companion tools from other mods through the RimBridge SDK

For most workflows, use it with GABS so RimWorld can be launched, connected, and controlled cleanly.
