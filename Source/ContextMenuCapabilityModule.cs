using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimBridgeServer;

internal sealed class ContextMenuCapabilityModule
{
    private const int DefaultTimeoutMs = 2000;

    private sealed class MapClickTarget
    {
        public IntVec3 Cell { get; set; } = IntVec3.Invalid;

        public string Label { get; set; } = string.Empty;
    }

    private sealed class PreparedMapClick
    {
        public bool Success { get; set; }

        public object Failure { get; set; }

        public MapClickTarget Target { get; set; }

        public List<string> SelectedPawnLabels { get; set; } = [];
    }

    private sealed class PreparedMapDrag
    {
        public bool Success { get; set; }

        public object Failure { get; set; }

        public MapClickTarget StartTarget { get; set; }

        public MapClickTarget EndTarget { get; set; }
    }

    public object OpenContextMenu(string targetPawnName = null, string targetPawnId = null, int x = 0, int z = 0, string mode = "vanilla", string button = "right", int holdDurationMs = 0, string modifiers = null)
    {
        var prepared = PrepareMapClick(targetPawnName, targetPawnId, x, z, requireSelectedPawns: true);
        if (!prepared.Success)
            return prepared.Failure;

        var normalizedMode = (mode ?? "vanilla").Trim().ToLowerInvariant();
        if (normalizedMode == "auto")
            normalizedMode = "live";

        if (normalizedMode != "vanilla" && normalizedMode != "live")
        {
            return new
            {
                success = false,
                message = $"Unsupported context menu mode '{mode}'. Supported values are 'vanilla', 'auto', and 'live'."
            };
        }

        if (!TryParseDispatchOptions(button, holdDurationMs, modifiers, out var dispatchOptions, out var failure))
            return failure;

        var dispatch = RimBridgeMapClickInjector.DispatchClick(prepared.Target.Cell, prepared.Target.Label, dispatchOptions, DefaultTimeoutMs);
        if (!dispatch.Success)
            return new { success = false, message = dispatch.Message };

        var snapshot = dispatch.Snapshot;
        if (snapshot == null)
        {
            return new
            {
                success = true,
                menuId = 0,
                provider = "ui_event",
                clickCell = new { x = prepared.Target.Cell.x, z = prepared.Target.Cell.z },
                target = prepared.Target.Label,
                button = dispatchOptions.ButtonName,
                holdDurationMs = dispatchOptions.HoldDurationMs,
                modifiers = dispatchOptions.ModifiersText,
                selectedPawns = prepared.SelectedPawnLabels,
                optionCount = 0,
                options = new List<object>(),
                message = dispatch.Message
            };
        }

        var options = DescribeOptionsOnMainThread(snapshot.Options);
        return new
        {
            success = true,
            menuId = snapshot.Id,
            provider = snapshot.Provider,
            clickCell = new { x = prepared.Target.Cell.x, z = prepared.Target.Cell.z },
            target = prepared.Target.Label,
            button = dispatchOptions.ButtonName,
            holdDurationMs = dispatchOptions.HoldDurationMs,
            modifiers = dispatchOptions.ModifiersText,
            selectedPawns = prepared.SelectedPawnLabels,
            optionCount = options.Count,
            options,
            message = dispatch.Message
        };
    }

    public object RightClickCell(string targetPawnName = null, string targetPawnId = null, int x = 0, int z = 0, string button = "right", int holdDurationMs = 0, string modifiers = null)
    {
        var prepared = PrepareMapClick(targetPawnName, targetPawnId, x, z, requireSelectedPawns: true);
        if (!prepared.Success)
            return prepared.Failure;

        if (!TryParseDispatchOptions(button, holdDurationMs, modifiers, out var dispatchOptions, out var failure))
            return failure;

        var dispatch = RimBridgeMapClickInjector.DispatchClick(prepared.Target.Cell, prepared.Target.Label, dispatchOptions, DefaultTimeoutMs);
        if (!dispatch.Success)
            return new { success = false, message = dispatch.Message };

        if (dispatch.Snapshot != null)
        {
            var snapshot = dispatch.Snapshot;
            var options = DescribeOptionsOnMainThread(snapshot.Options);
            return new
            {
                success = true,
                actionKind = "menu_opened",
                menuId = snapshot.Id,
                provider = snapshot.Provider,
                clickCell = new { x = prepared.Target.Cell.x, z = prepared.Target.Cell.z },
                target = prepared.Target.Label,
                button = dispatchOptions.ButtonName,
                holdDurationMs = dispatchOptions.HoldDurationMs,
                modifiers = dispatchOptions.ModifiersText,
                selectedPawns = prepared.SelectedPawnLabels,
                optionCount = options.Count,
                options,
                message = dispatch.Message
            };
        }

        return new
        {
            success = true,
            actionKind = "click_dispatched",
            clickCell = new { x = prepared.Target.Cell.x, z = prepared.Target.Cell.z },
            target = prepared.Target.Label,
            button = dispatchOptions.ButtonName,
            holdDurationMs = dispatchOptions.HoldDurationMs,
            modifiers = dispatchOptions.ModifiersText,
            selectedPawns = prepared.SelectedPawnLabels,
            message = dispatch.Message
        };
    }

    public object ClickCell(int x = 0, int z = 0, string button = "left", int holdDurationMs = 0, string modifiers = null)
    {
        var prepared = PrepareCellClick(x, z);
        if (!prepared.Success)
            return prepared.Failure;

        if (!TryParseDispatchOptions(button, holdDurationMs, modifiers, out var dispatchOptions, out var failure))
            return failure;

        var selectionBefore = DescribeSelectionSnapshotOnMainThread();
        var dispatch = RimBridgeMapClickInjector.DispatchClick(prepared.Target.Cell, prepared.Target.Label, dispatchOptions, DefaultTimeoutMs);
        var selectionAfter = DescribeSelectionSnapshotOnMainThread();
        if (!dispatch.Success)
        {
            return new
            {
                success = false,
                command = "click_cell",
                message = dispatch.Message,
                clickCell = new { x = prepared.Target.Cell.x, z = prepared.Target.Cell.z },
                button = dispatchOptions.ButtonName,
                holdDurationMs = dispatchOptions.HoldDurationMs,
                modifiers = dispatchOptions.ModifiersText,
                selectionBefore,
                selectionAfter
            };
        }

        return CreateMapGestureResponse(
            command: "click_cell",
            actionKind: dispatch.Snapshot == null ? "click_dispatched" : "menu_opened",
            prepared.Target,
            endTarget: null,
            dispatch,
            dispatchOptions,
            selectionBefore,
            selectionAfter);
    }

    public object DragCell(int fromX = 0, int fromZ = 0, int toX = 0, int toZ = 0, string button = "left", int holdDurationMs = 0, string modifiers = null)
    {
        var prepared = PrepareCellDrag(fromX, fromZ, toX, toZ);
        if (!prepared.Success)
            return prepared.Failure;

        if (!TryParseDispatchOptions(button, holdDurationMs, modifiers, out var dispatchOptions, out var failure))
            return failure;

        var selectionBefore = DescribeSelectionSnapshotOnMainThread();
        var dispatch = RimBridgeMapClickInjector.DispatchDrag(
            prepared.StartTarget.Cell,
            prepared.EndTarget.Cell,
            $"{prepared.StartTarget.Label} to {prepared.EndTarget.Label}",
            dispatchOptions,
            DefaultTimeoutMs);
        var selectionAfter = DescribeSelectionSnapshotOnMainThread();
        if (!dispatch.Success)
        {
            return new
            {
                success = false,
                command = "drag_cell",
                message = dispatch.Message,
                startCell = new { x = prepared.StartTarget.Cell.x, z = prepared.StartTarget.Cell.z },
                endCell = new { x = prepared.EndTarget.Cell.x, z = prepared.EndTarget.Cell.z },
                button = dispatchOptions.ButtonName,
                holdDurationMs = dispatchOptions.HoldDurationMs,
                modifiers = dispatchOptions.ModifiersText,
                selectionBefore,
                selectionAfter
            };
        }

        return CreateMapGestureResponse(
            command: "drag_cell",
            actionKind: dispatch.Snapshot == null ? "drag_dispatched" : "menu_opened",
            prepared.StartTarget,
            prepared.EndTarget,
            dispatch,
            dispatchOptions,
            selectionBefore,
            selectionAfter);
    }

    public object GetContextMenuOptions()
    {
        var snapshot = RimBridgeContextMenus.Current;
        if (snapshot == null || snapshot.Menu == null)
            return new { success = false, message = "No debug context menu has been opened yet." };
        if (Find.WindowStack.FloatMenu != snapshot.Menu)
        {
            RimBridgeContextMenus.Clear();
            return new { success = false, message = "No debug context menu has been opened yet." };
        }

        return new
        {
            success = true,
            menuId = snapshot.Id,
            provider = snapshot.Provider,
            target = snapshot.TargetLabel,
            clickCell = new { x = snapshot.ClickCell.x, z = snapshot.ClickCell.z },
            optionCount = snapshot.Options.Count,
            options = DescribeOptions(snapshot.Options)
        };
    }

    public object ExecuteContextMenuOption(int optionIndex = -1, string label = null)
    {
        var execution = RimWorldContextMenuActions.ExecuteOption(optionIndex, label);
        if (!execution.Success)
            return new { success = false, message = execution.Message, label = execution.Label };

        return new
        {
            success = true,
            executedIndex = execution.ResolvedIndex,
            label = execution.Label
        };
    }

    public object CloseContextMenu()
    {
        if (Find.WindowStack.FloatMenu != null)
            Find.WindowStack.TryRemove(Find.WindowStack.FloatMenu, doCloseSound: false);

        RimBridgeContextMenus.Clear();
        return new { success = true };
    }

    private static List<object> DescribeOptions(IEnumerable<FloatMenuOption> options)
    {
        return options.Select((option, index) => (object)new
        {
            index = index + 1,
            label = option.Label,
            disabled = option.Disabled,
            priority = option.Priority.ToString(),
            orderInPriority = option.orderInPriority,
            autoTakeable = option.autoTakeable,
            hasAction = option.action != null
        }).ToList();
    }

    private static List<object> DescribeOptionsOnMainThread(IEnumerable<FloatMenuOption> options)
    {
        return RimBridgeMainThread.Invoke(
            () => DescribeOptions(options ?? Enumerable.Empty<FloatMenuOption>()),
            timeoutMs: 5000);
    }

    private static object DescribeSelectionSnapshotOnMainThread()
    {
        return RimBridgeMainThread.Invoke(
            () => RimWorldSelectionSemantics.DescribeCurrentSelectionSnapshot(includeInspectDetails: false),
            timeoutMs: 5000);
    }

    private static PreparedMapClick PrepareMapClick(string targetPawnName, string targetPawnId, int x, int z, bool requireSelectedPawns)
    {
        return RimBridgeMainThread.Invoke(() =>
        {
            if (Current.Game == null)
                return new PreparedMapClick { Success = false, Failure = new { success = false, message = "No game is currently loaded." } };

            var selectedPawns = Find.Selector.SelectedPawns.ToList();
            if (requireSelectedPawns && selectedPawns.Count == 0)
                return new PreparedMapClick { Success = false, Failure = new { success = false, message = "No pawns are currently selected." } };

            var map = RimWorldState.CurrentMapOrThrow();
            if (!TryResolveMapClickTarget(map, targetPawnName, targetPawnId, x, z, out var target, out var failure))
                return new PreparedMapClick { Success = false, Failure = failure };

            return new PreparedMapClick
            {
                Success = true,
                Target = target,
                SelectedPawnLabels = selectedPawns.Select(pawn => pawn.Name?.ToStringShort ?? pawn.LabelShort).ToList()
            };
        }, timeoutMs: 5000);
    }

    private static PreparedMapClick PrepareCellClick(int x, int z)
    {
        return RimBridgeMainThread.Invoke(() =>
        {
            if (Current.Game == null)
                return new PreparedMapClick { Success = false, Failure = new { success = false, message = "No game is currently loaded." } };

            var map = RimWorldState.CurrentMapOrThrow();
            if (!TryResolveMapCell(map, x, z, out var target, out var failure))
                return new PreparedMapClick { Success = false, Failure = failure };

            return new PreparedMapClick { Success = true, Target = target };
        }, timeoutMs: 5000);
    }

    private static PreparedMapDrag PrepareCellDrag(int fromX, int fromZ, int toX, int toZ)
    {
        return RimBridgeMainThread.Invoke(() =>
        {
            if (Current.Game == null)
                return new PreparedMapDrag { Success = false, Failure = new { success = false, message = "No game is currently loaded." } };

            var map = RimWorldState.CurrentMapOrThrow();
            if (!TryResolveMapCell(map, fromX, fromZ, out var startTarget, out var failure))
                return new PreparedMapDrag { Success = false, Failure = failure };
            if (!TryResolveMapCell(map, toX, toZ, out var endTarget, out failure))
                return new PreparedMapDrag { Success = false, Failure = failure };

            return new PreparedMapDrag
            {
                Success = true,
                StartTarget = startTarget,
                EndTarget = endTarget
            };
        }, timeoutMs: 5000);
    }

    private static object CreateMapGestureResponse(
        string command,
        string actionKind,
        MapClickTarget target,
        MapClickTarget endTarget,
        MapClickDispatchResult dispatch,
        MapClickDispatchOptions dispatchOptions,
        object selectionBefore,
        object selectionAfter)
    {
        if (dispatch.Snapshot != null)
        {
            var snapshot = dispatch.Snapshot;
            var options = DescribeOptionsOnMainThread(snapshot.Options);
            return new
            {
                success = true,
                command,
                actionKind,
                gestureKind = dispatch.GestureKind,
                menuId = snapshot.Id,
                provider = snapshot.Provider,
                clickCell = new { x = target.Cell.x, z = target.Cell.z },
                startCell = endTarget == null ? null : new { x = target.Cell.x, z = target.Cell.z },
                endCell = endTarget == null ? null : new { x = endTarget.Cell.x, z = endTarget.Cell.z },
                target = endTarget == null ? target.Label : $"{target.Label} to {endTarget.Label}",
                button = dispatchOptions.ButtonName,
                holdDurationMs = dispatchOptions.HoldDurationMs,
                modifiers = dispatchOptions.ModifiersText,
                optionCount = options.Count,
                options,
                selectionBefore,
                selectionAfter,
                message = dispatch.Message
            };
        }

        return new
        {
            success = true,
            command,
            actionKind,
            gestureKind = dispatch.GestureKind,
            clickCell = endTarget == null ? new { x = target.Cell.x, z = target.Cell.z } : null,
            startCell = endTarget == null ? null : new { x = target.Cell.x, z = target.Cell.z },
            endCell = endTarget == null ? null : new { x = endTarget.Cell.x, z = endTarget.Cell.z },
            target = endTarget == null ? target.Label : $"{target.Label} to {endTarget.Label}",
            button = dispatchOptions.ButtonName,
            holdDurationMs = dispatchOptions.HoldDurationMs,
            modifiers = dispatchOptions.ModifiersText,
            selectionBefore,
            selectionAfter,
            message = dispatch.Message
        };
    }

    private static bool TryResolveMapClickTarget(Map map, string targetPawnName, string targetPawnId, int x, int z, out MapClickTarget target, out object failure)
    {
        target = null;
        failure = null;

        if (!string.IsNullOrWhiteSpace(targetPawnName) || !string.IsNullOrWhiteSpace(targetPawnId))
        {
            var targetPawn = RimWorldState.ResolveCurrentMapPawn(targetPawnName, targetPawnId);
            if (!targetPawn.Spawned || targetPawn.Map != map)
            {
                var identifier = string.IsNullOrWhiteSpace(targetPawnId) ? targetPawnName : targetPawnId;
                failure = new { success = false, message = $"Pawn '{identifier}' is not spawned on the current map." };
                return false;
            }

            target = new MapClickTarget
            {
                Cell = targetPawn.Position,
                Label = targetPawn.Name?.ToStringShort ?? targetPawn.LabelShort
            };
            return true;
        }

        return TryResolveMapCell(map, x, z, out target, out failure);
    }

    private static bool TryResolveMapCell(Map map, int x, int z, out MapClickTarget target, out object failure)
    {
        target = null;
        failure = null;

        var clickCell = new IntVec3(x, 0, z);
        if (!clickCell.InBounds(map))
        {
            failure = new { success = false, message = $"Cell ({x}, {z}) is out of bounds for the current map." };
            return false;
        }

        target = new MapClickTarget
        {
            Cell = clickCell,
            Label = $"cell {x},{z}"
        };
        return true;
    }

    private static bool TryParseDispatchOptions(string button, int holdDurationMs, string modifiers, out MapClickDispatchOptions options, out object failure)
    {
        options = null;
        failure = null;

        if (!TryParseMouseButton(button, out var parsedButton, out var normalizedButton))
        {
            failure = new
            {
                success = false,
                message = $"Unsupported mouse button '{button}'. Supported values are 'left', 'right', and 'middle'."
            };
            return false;
        }

        if (holdDurationMs < 0)
        {
            failure = new
            {
                success = false,
                message = "holdDurationMs must be zero or greater."
            };
            return false;
        }

        if (!TryParseModifiers(modifiers, out var parsedModifiers, out var normalizedModifiers, out var modifierError))
        {
            failure = new
            {
                success = false,
                message = modifierError
            };
            return false;
        }

        options = new MapClickDispatchOptions
        {
            Button = parsedButton,
            ButtonName = normalizedButton,
            HoldDurationMs = holdDurationMs,
            Modifiers = parsedModifiers,
            ModifiersText = normalizedModifiers
        };
        return true;
    }

    private static bool TryParseMouseButton(string button, out int parsedButton, out string normalizedButton)
    {
        normalizedButton = (button ?? "right").Trim().ToLowerInvariant();
        switch (normalizedButton)
        {
            case "0":
            case "left":
                parsedButton = 0;
                normalizedButton = "left";
                return true;

            case "1":
            case "right":
                parsedButton = 1;
                normalizedButton = "right";
                return true;

            case "2":
            case "middle":
                parsedButton = 2;
                normalizedButton = "middle";
                return true;

            default:
                parsedButton = 1;
                return false;
        }
    }

    private static bool TryParseModifiers(string modifiers, out EventModifiers parsedModifiers, out string normalizedModifiers, out string failure)
    {
        parsedModifiers = EventModifiers.None;
        normalizedModifiers = "none";
        failure = null;

        if (string.IsNullOrWhiteSpace(modifiers))
            return true;

        var seen = new List<string>();
        foreach (var token in modifiers.Split(new[] { ',', '+', '|', ' ' }, System.StringSplitOptions.RemoveEmptyEntries))
        {
            var normalizedToken = token.Trim().ToLowerInvariant();
            switch (normalizedToken)
            {
                case "none":
                    continue;

                case "shift":
                    parsedModifiers |= EventModifiers.Shift;
                    seen.Add("shift");
                    break;

                case "ctrl":
                case "control":
                    parsedModifiers |= EventModifiers.Control;
                    seen.Add("ctrl");
                    break;

                case "alt":
                case "option":
                    parsedModifiers |= EventModifiers.Alt;
                    seen.Add("alt");
                    break;

                case "cmd":
                case "command":
                case "meta":
                    parsedModifiers |= EventModifiers.Command;
                    seen.Add("command");
                    break;

                default:
                    failure = $"Unsupported modifier '{token}'. Supported values are shift, ctrl, alt, and command.";
                    normalizedModifiers = "none";
                    parsedModifiers = EventModifiers.None;
                    return false;
            }
        }

        if (seen.Count > 0)
            normalizedModifiers = string.Join(",", seen.Distinct());

        return true;
    }
}
