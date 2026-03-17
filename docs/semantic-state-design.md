# Semantic Inspection Design

## Goal

The next bridge layer should make RimWorld state easier for an AI to validate semantically while staying deterministic and close to real game behavior. The immediate focus is not broader generic remote control. It is:

- selection-scoped inspect data
- semantic gizmo discovery and execution
- structured messages, letters, and alerts

This should let an agent verify mod behavior without relying on screenshots for every state transition.

## Design Choices

### 1. Model-backed state, not UI scraping

For the first semantic layer, the bridge should read canonical game objects instead of scraping window text or screen geometry.

Verified seams from the real RimWorld 1.6 assembly:

- `ISelectable.GetInspectString()`
- `ISelectable.GetGizmos()`
- `LetterStack.LettersListForReading`
- `Messages.liveMessages`
- `AlertsReadout.activeAlerts`

These are more stable than screenshot parsing and preserve the exact gameplay semantics already used by the game.

### 2. Selection-scoped gizmos

Gizmos are fundamentally contextual. A gizmo id only makes sense relative to the current selection. The bridge should therefore expose selection-scoped gizmo ids instead of pretending gizmos are globally stable entities.

The id strategy should:

- bind to the current selection fingerprint
- be deterministic for the same selection and gizmo ordering
- fail clearly if the selection or gizmo set changed before execution

Recommended shape:

- compute a selection fingerprint from the selected objects in order
- recompute the grouped gizmo list on each call
- assign each representative gizmo a synthetic id derived from the selection fingerprint plus a stable ordinal and compact semantic fingerprint

This avoids stale execution against the wrong selection while still giving the agent an opaque handle it can pass back.

### 3. Mirror RimWorld's gizmo grouping behavior

The bridge should not expose raw per-object gizmos and call that "what the player sees." RimWorld groups gizmos across the selection before drawing them, merges compatible commands, applies special representative selection rules for toggles, and fans interactions back out across the grouped commands.

The bridge should mirror that behavior closely enough that:

- `list_selected_gizmos` corresponds to the actionable commands in the UI
- `execute_gizmo` produces the same grouped side effects that the UI would

Directly exposing every raw `GetGizmos()` result would be simpler, but it would be wrong for multi-selection and would give an AI a surface that does not match the real game.

### 4. Notifications should be pollable state

Messages, letters, and alerts should be exposed as structured state that can be polled and diffed:

- live messages for short-lived feedback
- letter stack contents for long-form notifications
- active alerts for colony-wide state validation

This is better than parsing logs because many mod-relevant outcomes are user-facing UI signals rather than bridge logs.

## Scope

### Slice 1

- `rimworld/get_selection_semantics`
- `rimworld/list_selected_gizmos`
- `rimworld/execute_gizmo`

### Slice 2

- `rimworld/list_messages`
- `rimworld/list_letters`
- `rimworld/open_letter`
- `rimworld/dismiss_letter`
- `rimworld/list_alerts`
- `rimworld/activate_alert`

## Intentionally Deferred

- Lua-layer changes
- richer inspect-tab introspection
- semantic mod-settings editing
- generalized window scraping
- broader remote-control surface expansion

Those can build on this layer later, but they are not required to make autonomous mod validation materially better right now.
