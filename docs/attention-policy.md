# Attention Policy

RimBridgeServer uses blocking attention to surface important async game state that arrives after a normal tool call boundary.

The practical goal is simple: if RimWorld logs a severe failure or a bridge operation fails after a tool already returned, the next ordinary game-bound call should stop and force inspection instead of letting the caller continue on stale assumptions.

## Current Built-In Policy

Today, RimBridgeServer opens blocking attention for:

- RimWorld or bridge log entries at `error` or `fatal`
- bridge operation lifecycle events:
  - `operation.failed`
  - `operation.cancelled`
  - `operation.timed_out`

It does not currently open blocking attention for:

- `info` logs
- `warning` logs by themselves
- successful or in-progress operation events

The current implementation lives in `RimBridgeServer.Core` and is intentionally centralized so future work has one seam to extend.

## What Tool Authors Need To Do

Most tool authors do not need to write any attention logic.

The normal pattern is:

- write an ordinary tool
- return normal success or failure data
- let RimBridgeServer's integration layer decide whether later async logs or operation failures should open attention

That keeps the attention protocol out of ordinary tool code.

## Diagnostics While Attention Is Open

When GABS is enforcing attention gating, ordinary game-bound calls are blocked until the current attention item is acknowledged.

Diagnostics still remain available:

- `rimbridge/get_bridge_status`
- `rimbridge/list_operation_events`
- `rimbridge/list_logs`
- `games.get_attention`
- `games.ack_attention`

This split is intentional:

- attention is the compact control-plane summary
- diagnostics remain the detailed pull-based inspection path

## Current Limitation For Third-Party Mods

There is not yet a public cross-mod API for another RimWorld mod to publish its own async attention item directly through RimBridgeServer.

That means third-party extension tools can already:

- expose tools through `RimBridgeServer.Annotations`
- benefit from the central attention system when severe logs or failed bridge operations occur

But they cannot yet:

- open, update, or clear a first-class attention item on their own

That integration surface should be designed deliberately later. For now, treat third-party attention publication as deferred work rather than part of the current extension contract.
