using System;
using System.Collections.Generic;
using System.Linq;
using RimBridgeServer.Core;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimBridgeServer;

internal static class RimWorldSelectionSemantics
{
    private const int MaxDetailedSelectionObjects = 12;

    private sealed class RawSelectionGizmo
    {
        public object Owner { get; set; }

        public string OwnerToken { get; set; } = string.Empty;

        public object OwnerReference { get; set; }

        public Gizmo Gizmo { get; set; }

        public bool ReverseDesignator { get; set; }
    }

    private sealed class GroupedSelectionGizmo
    {
        public int Ordinal { get; set; }

        public string Id { get; set; } = string.Empty;

        public string SelectionFingerprint { get; set; } = string.Empty;

        public List<RawSelectionGizmo> Entries { get; } = [];

        public List<Gizmo> GroupedGizmos { get; } = [];

        public Gizmo Representative { get; set; }
    }

    public static object GetSelectionSemanticsResponse()
    {
        if (Current.Game == null)
            return Failure("No game is currently loaded.");

        var selectedObjects = GetSelectedObjects();
        var selectionFingerprint = CreateSelectionFingerprint(selectedObjects);
        var groupedGizmos = BuildGroupedGizmos(selectedObjects, selectionFingerprint);

        return new
        {
            success = true,
            hasSelection = selectedObjects.Count > 0,
            selectionFingerprint,
            selectedCount = selectedObjects.Count,
            detailedObjectCount = Math.Min(selectedObjects.Count, MaxDetailedSelectionObjects),
            selectionDetailsTruncated = selectedObjects.Count > MaxDetailedSelectionObjects,
            selectedKinds = selectedObjects.Select(GetObjectKind).Distinct(StringComparer.Ordinal).ToList(),
            selectedObjects = selectedObjects
                .Take(MaxDetailedSelectionObjects)
                .Select(objectRef => DescribeSelectedObject(objectRef, includeInspectDetails: true))
                .ToList(),
            visibleGizmoCount = groupedGizmos.Count(group => group.Representative?.Visible == true),
            uiState = RimWorldInput.GetUiState()
        };
    }

    public static object ListSelectedGizmosResponse()
    {
        if (Current.Game == null)
            return Failure("No game is currently loaded.");

        var selectedObjects = GetSelectedObjects();
        var selectionFingerprint = CreateSelectionFingerprint(selectedObjects);
        var gizmos = BuildGroupedGizmos(selectedObjects, selectionFingerprint)
            .Where(group => group.Representative?.Visible == true)
            .Select(DescribeGroupedGizmo)
            .ToList();

        return new
        {
            success = true,
            hasSelection = selectedObjects.Count > 0,
            selectionFingerprint,
            selectedCount = selectedObjects.Count,
            gizmoCount = gizmos.Count,
            gizmos
        };
    }

    public static object ExecuteGizmoResponse(string gizmoId)
    {
        if (Current.Game == null)
            return Failure("No game is currently loaded.");
        if (string.IsNullOrWhiteSpace(gizmoId))
            return Failure("A gizmo id is required.");

        var selectedObjectsBefore = GetSelectedObjects();
        if (selectedObjectsBefore.Count == 0)
        {
            return new
            {
                success = false,
                message = "No objects are currently selected.",
                requestedGizmoId = gizmoId
            };
        }

        var selectionFingerprintBefore = CreateSelectionFingerprint(selectedObjectsBefore);
        var groupedGizmos = BuildGroupedGizmos(selectedObjectsBefore, selectionFingerprintBefore);
        var target = groupedGizmos.FirstOrDefault(group => string.Equals(group.Id, gizmoId, StringComparison.Ordinal));
        if (target == null)
        {
            var staleSelection = SelectionGizmoIds.TryReadSelectionFingerprint(gizmoId, out var requestedSelectionFingerprint)
                && string.Equals(requestedSelectionFingerprint, selectionFingerprintBefore, StringComparison.Ordinal) == false;

            return new
            {
                success = false,
                message = staleSelection
                    ? "The requested gizmo id no longer matches the current selection."
                    : $"Could not find selected gizmo '{gizmoId}'.",
                requestedGizmoId = gizmoId,
                selectionFingerprint = selectionFingerprintBefore,
                availableGizmoIds = groupedGizmos
                    .Where(group => group.Representative?.Visible == true)
                    .Select(group => group.Id)
                    .ToList()
            };
        }

        var representative = target.Representative;
        if (representative == null || representative.Visible == false)
        {
            return new
            {
                success = false,
                message = $"Gizmo '{gizmoId}' is no longer visible for the current selection.",
                requestedGizmoId = gizmoId,
                selectionFingerprint = selectionFingerprintBefore
            };
        }

        if (representative.Disabled)
        {
            return new
            {
                success = false,
                message = string.IsNullOrWhiteSpace(representative.disabledReason)
                    ? $"Gizmo '{gizmoId}' is disabled."
                    : representative.disabledReason,
                requestedGizmoId = gizmoId,
                selectionFingerprint = selectionFingerprintBefore,
                gizmo = DescribeGroupedGizmo(target)
            };
        }

        var uiBefore = RimWorldInput.GetUiState();
        var selectionBefore = DescribeSelectionSnapshot(selectedObjectsBefore, selectionFingerprintBefore, includeInspectDetails: false);

        try
        {
            ExecuteGroupedGizmo(target);
        }
        catch (Exception ex)
        {
            var selectedObjectsAfterFailure = GetSelectedObjects();
            var selectionFingerprintAfterFailure = CreateSelectionFingerprint(selectedObjectsAfterFailure);
            return new
            {
                success = false,
                message = $"Executing gizmo '{gizmoId}' failed: {ex.Message}",
                exceptionType = ex.GetType().FullName,
                requestedGizmoId = gizmoId,
                gizmo = DescribeGroupedGizmo(target),
                selectionBefore,
                selectionAfter = DescribeSelectionSnapshot(selectedObjectsAfterFailure, selectionFingerprintAfterFailure, includeInspectDetails: false),
                uiBefore,
                uiAfter = RimWorldInput.GetUiState()
            };
        }

        var selectedObjectsAfter = GetSelectedObjects();
        var selectionFingerprintAfter = CreateSelectionFingerprint(selectedObjectsAfter);

        return new
        {
            success = true,
            requestedGizmoId = gizmoId,
            gizmo = DescribeGroupedGizmo(target),
            selectionBefore,
            selectionAfter = DescribeSelectionSnapshot(selectedObjectsAfter, selectionFingerprintAfter, includeInspectDetails: false),
            selectionChanged = string.Equals(selectionFingerprintBefore, selectionFingerprintAfter, StringComparison.Ordinal) == false,
            uiBefore,
            uiAfter = RimWorldInput.GetUiState()
        };
    }

    private static object DescribeSelectionSnapshot(IReadOnlyList<object> selectedObjects, string selectionFingerprint, bool includeInspectDetails)
    {
        return new
        {
            hasSelection = selectedObjects.Count > 0,
            selectionFingerprint,
            selectedCount = selectedObjects.Count,
            selectedObjects = selectedObjects
                .Take(MaxDetailedSelectionObjects)
                .Select(objectRef => DescribeSelectedObject(objectRef, includeInspectDetails))
                .ToList(),
            selectionDetailsTruncated = selectedObjects.Count > MaxDetailedSelectionObjects
        };
    }

    private static List<object> GetSelectedObjects()
    {
        return Find.Selector?.SelectedObjectsListForReading?
            .Where(objectRef => objectRef != null)
            .Cast<object>()
            .ToList()
            ?? [];
    }

    private static string CreateSelectionFingerprint(IEnumerable<object> selectedObjects)
    {
        return SelectionGizmoIds.CreateSelectionFingerprint(selectedObjects.Select(GetSelectionToken));
    }

    private static List<GroupedSelectionGizmo> BuildGroupedGizmos(IReadOnlyList<object> selectedObjects, string selectionFingerprint)
    {
        var rawGizmos = CollectRawGizmos(selectedObjects)
            .OrderBy(entry => entry.Gizmo.Order)
            .ToList();

        var groups = new List<List<RawSelectionGizmo>>();
        foreach (var rawGizmo in rawGizmos)
        {
            var matched = false;
            foreach (var group in groups)
            {
                if (group[0].Gizmo.GroupsWith(rawGizmo.Gizmo) == false)
                    continue;

                matched = true;
                group.Add(rawGizmo);
                group[0].Gizmo.MergeWith(rawGizmo.Gizmo);
                break;
            }

            if (matched == false)
                groups.Add([rawGizmo]);
        }

        var result = new List<GroupedSelectionGizmo>(groups.Count);
        for (var index = 0; index < groups.Count; index++)
        {
            var group = groups[index];
            var groupedGizmos = group.Select(entry => entry.Gizmo).ToList();
            var representative = SelectRepresentative(groupedGizmos);
            if (representative is Command_Ability abilityCommand)
                abilityCommand.GroupAbilityCommands(groupedGizmos);

            var gizmoId = SelectionGizmoIds.CreateGizmoId(
                selectionFingerprint,
                index + 1,
                CreateGizmoSignatureParts(groupedGizmos, representative, group));

            var snapshot = new GroupedSelectionGizmo
            {
                Ordinal = index + 1,
                Id = gizmoId,
                SelectionFingerprint = selectionFingerprint,
                Representative = representative
            };

            snapshot.GroupedGizmos.AddRange(groupedGizmos);
            snapshot.Entries.AddRange(group);
            result.Add(snapshot);
        }

        return result;
    }

    private static List<RawSelectionGizmo> CollectRawGizmos(IEnumerable<object> selectedObjects)
    {
        var result = new List<RawSelectionGizmo>();
        foreach (var selectedObject in selectedObjects)
        {
            if (selectedObject is ISelectable selectable)
            {
                foreach (var gizmo in selectable.GetGizmos().OfType<Gizmo>())
                    result.Add(CreateRawSelectionGizmo(selectedObject, gizmo, reverseDesignator: false));
            }

            if (selectedObject is Gizmo gizmoObject)
                result.Add(CreateRawSelectionGizmo(selectedObject, gizmoObject, reverseDesignator: false));
        }

        var reverseDesignators = Find.ReverseDesignatorDatabase?.AllDesignators;
        if (reverseDesignators == null || reverseDesignators.Count == 0)
            return result;

        foreach (var thing in selectedObjects.OfType<Thing>())
        {
            for (var i = 0; i < reverseDesignators.Count; i++)
            {
                var reverseGizmo = reverseDesignators[i].CreateReverseDesignationGizmo(thing);
                if (reverseGizmo != null)
                    result.Add(CreateRawSelectionGizmo(thing, reverseGizmo, reverseDesignator: true));
            }
        }

        return result;
    }

    private static RawSelectionGizmo CreateRawSelectionGizmo(object owner, Gizmo gizmo, bool reverseDesignator)
    {
        return new RawSelectionGizmo
        {
            Owner = owner,
            OwnerToken = GetSelectionToken(owner),
            OwnerReference = DescribeOwnerReference(owner),
            Gizmo = gizmo,
            ReverseDesignator = reverseDesignator
        };
    }

    private static Gizmo SelectRepresentative(IReadOnlyList<Gizmo> groupedGizmos)
    {
        var representative = groupedGizmos.FirstOrDefault(gizmo => gizmo.Disabled == false) ?? groupedGizmos.FirstOrDefault();
        if (representative is not Command_Toggle toggle)
            return representative;

        if (toggle.activateIfAmbiguous == false && toggle.isActive != null && toggle.isActive() == false)
        {
            foreach (var groupedGizmo in groupedGizmos)
            {
                if (groupedGizmo is Command_Toggle { Disabled: false } candidate
                    && candidate.isActive != null
                    && candidate.isActive())
                {
                    representative = candidate;
                    break;
                }
            }
        }

        if (representative is Command_Toggle activeToggle
            && activeToggle.activateIfAmbiguous
            && activeToggle.isActive != null
            && activeToggle.isActive())
        {
            foreach (var groupedGizmo in groupedGizmos)
            {
                if (groupedGizmo is Command_Toggle { Disabled: false } candidate
                    && candidate.isActive != null
                    && candidate.isActive() == false)
                {
                    representative = candidate;
                    break;
                }
            }
        }

        return representative;
    }

    private static IEnumerable<string> CreateGizmoSignatureParts(
        IReadOnlyCollection<Gizmo> groupedGizmos,
        Gizmo representative,
        IReadOnlyCollection<RawSelectionGizmo> entries)
    {
        var command = representative as Command;
        var ownerTokens = entries
            .Select(entry => entry.OwnerToken)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(token => token, StringComparer.Ordinal)
            .ToList();

        return
        [
            representative?.GetType().FullName ?? "unknown",
            representative?.Order.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) ?? "0",
            command?.Label ?? string.Empty,
            command?.Desc ?? string.Empty,
            command?.TopRightLabel ?? string.Empty,
            groupedGizmos.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            string.Join("|", ownerTokens)
        ];
    }

    private static object DescribeGroupedGizmo(GroupedSelectionGizmo group)
    {
        var representative = group.Representative;
        var command = representative as Command;
        var owners = group.Entries
            .GroupBy(entry => entry.OwnerToken, StringComparer.Ordinal)
            .Select(ownerGroup => ownerGroup.First().OwnerReference)
            .ToList();

        return new
        {
            id = group.Id,
            selectionFingerprint = group.SelectionFingerprint,
            ordinal = group.Ordinal,
            gizmoType = representative?.GetType().FullName,
            label = command?.LabelCap ?? GetFallbackGizmoLabel(representative),
            description = command?.Desc,
            descriptionPostfix = command?.DescPostfix,
            topRightLabel = command?.TopRightLabel,
            disabled = representative?.Disabled ?? true,
            disabledReason = representative?.Disabled == true && string.IsNullOrWhiteSpace(representative.disabledReason) == false
                ? representative.disabledReason
                : null,
            visible = representative?.Visible ?? false,
            order = representative?.Order ?? 0f,
            groupSize = group.GroupedGizmos.Count,
            ownerCount = owners.Count,
            owners,
            hotKeyDef = command?.hotKey?.defName,
            hotKey = command?.hotKey == null ? null : command.hotKey.MainKey.ToString(),
            supportsRightClick = representative?.RightClickFloatMenuOptions?.Any() == true,
            reverseDesignator = group.Entries.Any(entry => entry.ReverseDesignator)
        };
    }

    private static object DescribeSelectedObject(object selectedObject, bool includeInspectDetails)
    {
        var inspectLabel = GetInspectLabel(selectedObject);
        var inspectString = includeInspectDetails ? GetInspectString(selectedObject) : null;
        var inspectStringLines = includeInspectDetails ? SplitInspectString(inspectString) : null;
        var inspectTabTypes = includeInspectDetails ? GetInspectTabTypes(selectedObject) : null;

        return new
        {
            kind = GetObjectKind(selectedObject),
            type = selectedObject?.GetType().FullName,
            id = GetUniqueId(selectedObject),
            label = GetObjectLabel(selectedObject),
            inspectLabel = string.IsNullOrWhiteSpace(inspectLabel) ? null : inspectLabel,
            inspectString = string.IsNullOrWhiteSpace(inspectString) ? null : inspectString,
            inspectStringLines,
            inspectTabTypes,
            reference = DescribeOwnerReference(selectedObject),
            details = DescribeSelectedObjectDetails(selectedObject)
        };
    }

    private static object DescribeSelectedObjectDetails(object selectedObject)
    {
        return selectedObject switch
        {
            Pawn pawn => RimWorldState.DescribePawn(pawn),
            Thing thing => new
            {
                thingId = RimWorldState.GetThingId(thing),
                thingIdNumber = thing.thingIDNumber,
                defName = thing.def?.defName,
                label = thing.LabelCap,
                spawned = thing.Spawned,
                stackCount = thing.stackCount,
                hitPoints = thing.HitPoints,
                maxHitPoints = thing.MaxHitPoints,
                mapId = RimWorldState.GetMapId(thing.Map),
                mapIndex = thing.Map?.Index,
                position = thing.Position.IsValid ? new { x = thing.Position.x, z = thing.Position.z } : null
            },
            Zone zone => new
            {
                zoneId = zone.GetUniqueLoadID(),
                runtimeId = zone.ID,
                label = zone.label,
                inspectLabel = zone.InspectLabel,
                mapId = RimWorldState.GetMapId(zone.Map),
                mapIndex = zone.Map?.Index,
                cellCount = zone.CellCount,
                hidden = zone.Hidden,
                position = zone.Position.IsValid ? new { x = zone.Position.x, z = zone.Position.z } : null
            },
            Plan plan => new
            {
                planId = plan.GetUniqueLoadID(),
                label = plan.label,
                inspectLabel = plan.InspectLabel,
                mapId = RimWorldState.GetMapId(plan.Map),
                mapIndex = plan.Map?.Index,
                cellCount = plan.CellCount,
                hidden = plan.Hidden
            },
            _ => null
        };
    }

    private static object DescribeOwnerReference(object selectedObject)
    {
        return new
        {
            kind = GetObjectKind(selectedObject),
            id = GetUniqueId(selectedObject),
            label = GetObjectLabel(selectedObject),
            mapId = GetMapId(selectedObject),
            mapIndex = GetMapIndex(selectedObject),
            position = GetPosition(selectedObject)
        };
    }

    private static string GetSelectionToken(object selectedObject)
    {
        return selectedObject switch
        {
            Thing thing => thing.GetUniqueLoadID(),
            Zone zone => zone.GetUniqueLoadID(),
            Plan plan => plan.GetUniqueLoadID(),
            _ => (selectedObject?.GetType().FullName ?? "null") + ":" + GetObjectLabel(selectedObject)
        };
    }

    private static string GetUniqueId(object selectedObject)
    {
        return selectedObject switch
        {
            Thing thing => thing.GetUniqueLoadID(),
            Zone zone => zone.GetUniqueLoadID(),
            Plan plan => plan.GetUniqueLoadID(),
            _ => null
        };
    }

    private static string GetObjectKind(object selectedObject)
    {
        return selectedObject switch
        {
            Pawn => "pawn",
            Thing => "thing",
            Zone => "zone",
            Plan => "plan",
            _ => "selection"
        };
    }

    private static string GetObjectLabel(object selectedObject)
    {
        return selectedObject switch
        {
            Pawn pawn => pawn.Name?.ToStringShort ?? pawn.LabelShort,
            Thing thing => thing.LabelCap,
            Zone zone => zone.InspectLabel,
            Plan plan => plan.InspectLabel,
            _ => selectedObject?.ToString()
        };
    }

    private static string GetInspectLabel(object selectedObject)
    {
        return selectedObject switch
        {
            Thing thing => thing.LabelCap,
            Zone zone => zone.InspectLabel,
            Plan plan => plan.InspectLabel,
            ISelectable selectable => GetObjectLabel(selectable),
            _ => null
        };
    }

    private static string GetInspectString(object selectedObject)
    {
        return selectedObject is ISelectable selectable
            ? selectable.GetInspectString()
            : null;
    }

    private static List<string> SplitInspectString(string inspectString)
    {
        if (string.IsNullOrWhiteSpace(inspectString))
            return [];

        return inspectString
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();
    }

    private static List<string> GetInspectTabTypes(object selectedObject)
    {
        if (selectedObject is not ISelectable selectable)
            return [];

        return selectable
            .GetInspectTabs()
            .Cast<object>()
            .Where(tab => tab != null)
            .Select(tab => tab.GetType().FullName)
            .Where(typeName => string.IsNullOrWhiteSpace(typeName) == false)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static string GetMapId(object selectedObject)
    {
        return selectedObject switch
        {
            Thing thing => RimWorldState.GetMapId(thing.Map),
            Zone zone => RimWorldState.GetMapId(zone.Map),
            Plan plan => RimWorldState.GetMapId(plan.Map),
            _ => null
        };
    }

    private static int? GetMapIndex(object selectedObject)
    {
        return selectedObject switch
        {
            Thing thing => thing.Map?.Index,
            Zone zone => zone.Map?.Index,
            Plan plan => plan.Map?.Index,
            _ => null
        };
    }

    private static object GetPosition(object selectedObject)
    {
        return selectedObject switch
        {
            Thing thing when thing.Position.IsValid => new { x = thing.Position.x, z = thing.Position.z },
            Zone zone when zone.Position.IsValid => new { x = zone.Position.x, z = zone.Position.z },
            _ => null
        };
    }

    private static string GetFallbackGizmoLabel(Gizmo gizmo)
    {
        return gizmo?.GetType().Name ?? "Gizmo";
    }

    private static void ExecuteGroupedGizmo(GroupedSelectionGizmo group)
    {
        var representative = group.Representative ?? throw new InvalidOperationException("The requested gizmo no longer has a representative.");
        var interactionEvent = new Event
        {
            type = EventType.MouseDown,
            button = 0,
            clickCount = 1
        };

        var previousCurrentEvent = Event.current;
        try
        {
            Event.current = interactionEvent;

            foreach (var groupedGizmo in group.GroupedGizmos)
            {
                if (ReferenceEquals(groupedGizmo, representative))
                    continue;
                if (groupedGizmo.Disabled)
                    continue;
                if (representative.InheritInteractionsFrom(groupedGizmo) == false)
                    continue;

                groupedGizmo.ProcessInput(interactionEvent);
            }

            representative.ProcessInput(interactionEvent);
            representative.ProcessGroupInput(interactionEvent, group.GroupedGizmos);
            Event.current?.Use();
        }
        finally
        {
            Event.current = previousCurrentEvent;
        }
    }

    private static object Failure(string message)
    {
        return new
        {
            success = false,
            message
        };
    }
}
