using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RimBridgeServer.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimBridgeServer;

internal static class RimWorldArchitect
{
    private sealed class CellSearchCriteria
    {
        public int Width { get; set; } = 1;

        public int Height { get; set; } = 1;

        public string FootprintAnchor { get; set; } = "top_left";

        public bool RequireWalkable { get; set; }

        public bool RequireStandable { get; set; }

        public bool RequireNotFogged { get; set; }

        public bool RequireNoImpassableThings { get; set; }

        public Pawn ReachablePawn { get; set; }

        public string ReachablePawnName { get; set; }

        public ArchitectDesignatorDescriptor DesignatorDescriptor { get; set; }
    }

    private sealed class ArchitectSelectionState
    {
        public Designator SelectedRootDesignator { get; set; }

        public Designator SelectedActionDesignator { get; set; }
    }

    private sealed class ArchitectCategoryDescriptor
    {
        public DesignationCategoryDef Definition { get; set; }

        public string Id { get; set; } = string.Empty;

        public bool Visible { get; set; }

        public List<ArchitectDesignatorDescriptor> Designators { get; set; } = [];
    }

    private sealed class ArchitectDesignatorDescriptor
    {
        public DesignationCategoryDef Category { get; set; }

        public Designator Designator { get; set; }

        public string Id { get; set; } = string.Empty;

        public string CategoryId { get; set; } = string.Empty;

        public string ParentId { get; set; }

        public Designator ContainerDesignator { get; set; }

        public string Kind { get; set; } = string.Empty;

        public int Index { get; set; }

        public int? ChildIndex { get; set; }

        public int ChildCount { get; set; }

        public bool Visible { get; set; }

        public bool Actionable { get; set; }

        public bool SupportsCellApplication { get; set; }

        public bool SupportsRectangleApplication { get; set; }

        public string ApplicationKind { get; set; } = string.Empty;

        public Designator EffectiveRootDesignator => ContainerDesignator ?? Designator;
    }

    public static object GetDesignatorStateResponse()
    {
        return new
        {
            success = true,
            devModeEnabled = Prefs.DevMode,
            godMode = DebugSettings.godMode,
            designatorState = CreateDesignatorStatePayload(Current.Game == null ? [] : null),
            state = RimWorldState.ToolStateSnapshot()
        };
    }

    public static object SetGodModeResponse(bool enabled)
    {
        var previous = DebugSettings.godMode;
        DebugSettings.godMode = enabled;

        bool? selectionStillValid = null;
        var manager = TryGetDesignatorManager();
        if (manager != null)
            selectionStillValid = manager.CheckSelectedDesignatorValid();

        return new
        {
            success = true,
            changed = previous != DebugSettings.godMode,
            previousGodMode = previous,
            godMode = DebugSettings.godMode,
            devModeEnabled = Prefs.DevMode,
            selectionStillValid,
            designatorState = CreateDesignatorStatePayload(),
            state = RimWorldState.ToolStateSnapshot()
        };
    }

    public static object ListArchitectCategoriesResponse(bool includeHidden = false, bool includeEmpty = false)
    {
        if (!TryGetMapContext(out _, out var error))
            return Failure(error);

        var categories = EnumerateArchitectCategories(includeHidden, includeEmpty);
        return new
        {
            success = true,
            devModeEnabled = Prefs.DevMode,
            godMode = DebugSettings.godMode,
            categoryCount = categories.Count,
            categories = categories.Select(DescribeCategory).ToList(),
            state = RimWorldState.ToolStateSnapshot()
        };
    }

    public static object ListArchitectDesignatorsResponse(string categoryId, bool includeHidden = false)
    {
        if (!TryGetMapContext(out _, out var error))
            return Failure(error);

        if (!TryResolveCategory(categoryId, includeHidden, out var category, out error))
            return Failure(error);

        var selectionState = CaptureSelectionState();
        return new
        {
            success = true,
            devModeEnabled = Prefs.DevMode,
            godMode = DebugSettings.godMode,
            category = DescribeCategory(category),
            designatorCount = category.Designators.Count,
            designators = category.Designators.Select(descriptor => DescribeDesignator(descriptor, selectionState)).ToList(),
            designatorState = CreateDesignatorStatePayload(category.Designators, selectionState),
            state = RimWorldState.ToolStateSnapshot()
        };
    }

    public static object SelectArchitectDesignatorResponse(string designatorId)
    {
        if (!TryGetMapContext(out _, out var error))
            return Failure(error);

        if (!TryResolveDesignator(designatorId, out var descriptor, out error))
            return Failure(error);

        if (!TrySelectDescriptor(descriptor, out error))
            return Failure(error);

        var selectionState = CaptureSelectionState();
        return new
        {
            success = true,
            designator = DescribeDesignator(descriptor, selectionState),
            designatorState = CreateDesignatorStatePayload(GetAllDesignatorDescriptors(), selectionState),
            state = RimWorldState.ToolStateSnapshot()
        };
    }

    public static object ApplyArchitectDesignatorResponse(string designatorId, int x, int z, int width = 1, int height = 1, bool dryRun = false, bool keepSelected = true)
    {
        if (!TryGetMapContext(out var map, out var error))
            return Failure(error);
        if (width <= 0 || height <= 0)
            return Failure("Width and height must be positive.");
        if (!TryResolveDesignator(designatorId, out var descriptor, out error))
            return Failure(error);
        if (!descriptor.Actionable || !descriptor.SupportsCellApplication)
            return Failure($"Architect designator '{designatorId}' does not support direct cell application.");
        if (!TrySelectDescriptor(descriptor, out error))
            return Failure(error);

        var requestedCells = EnumerateCells(x, z, width, height).ToList();
        var acceptedCells = new List<IntVec3>(requestedCells.Count);
        var rejectedCells = new List<object>();

        foreach (var cell in requestedCells)
        {
            if (!cell.InBounds(map))
            {
                rejectedCells.Add(new
                {
                    x = cell.x,
                    z = cell.z,
                    accepted = false,
                    reason = "Cell is out of bounds."
                });
                continue;
            }

            var report = descriptor.Designator.CanDesignateCell(cell);
            if (report.Accepted)
            {
                acceptedCells.Add(cell);
                continue;
            }

            rejectedCells.Add(new
            {
                x = cell.x,
                z = cell.z,
                accepted = false,
                reason = string.IsNullOrWhiteSpace(report.Reason) ? "The designator rejected this cell." : report.Reason
            });
        }

        Exception applyException = null;
        var explicitTargetZone = descriptor.Designator is Designator_ZoneAdd zoneAddWithTarget ? zoneAddWithTarget.SelectedZone : null;
        if (!dryRun && acceptedCells.Count > 0)
        {
            try
            {
                // RimWorld's visible stockpile tool can still create a fresh zone on empty cells even when an
                // existing zone target is selected. For automation we need deterministic expand semantics, so
                // explicit zone targets apply directly to the chosen zone instance.
                if (explicitTargetZone != null)
                {
                    foreach (var cell in acceptedCells)
                        explicitTargetZone.AddCell(cell);
                }
                else if (acceptedCells.Count == 1)
                    descriptor.Designator.DesignateSingleCell(acceptedCells[0]);
                else
                    descriptor.Designator.DesignateMultiCell(acceptedCells);

                descriptor.Designator.Finalize(true);
            }
            catch (Exception ex)
            {
                applyException = ex;
                descriptor.Designator.Finalize(false);
            }
        }

        if (!keepSelected)
            Find.DesignatorManager?.Deselect();

        var selectionState = CaptureSelectionState();
        var sampleCell = requestedCells.Count == 1 ? CreateCellInfoPayload(map, requestedCells[0]) : null;
        if (applyException != null)
        {
            return new
            {
                success = false,
                message = $"Applying architect designator '{designatorId}' failed: {applyException.Message}",
                dryRun,
                designator = DescribeDesignator(descriptor, selectionState),
                requestedCellCount = requestedCells.Count,
                acceptedCellCount = acceptedCells.Count,
                rejectedCellCount = rejectedCells.Count,
                acceptedCells = acceptedCells.Select(ToCellPayload).ToList(),
                rejectedCells,
                keepSelected,
                designatorState = CreateDesignatorStatePayload(GetAllDesignatorDescriptors(), selectionState),
                sampleCell,
                state = RimWorldState.ToolStateSnapshot()
            };
        }

        return new
        {
            success = acceptedCells.Count > 0,
            dryRun,
            designator = DescribeDesignator(descriptor, selectionState),
            requestedCellCount = requestedCells.Count,
            acceptedCellCount = acceptedCells.Count,
            appliedCellCount = dryRun ? 0 : acceptedCells.Count,
            rejectedCellCount = rejectedCells.Count,
            acceptedCells = acceptedCells.Select(ToCellPayload).ToList(),
            rejectedCells,
            keepSelected,
            designatorState = CreateDesignatorStatePayload(GetAllDesignatorDescriptors(), selectionState),
            sampleCell,
            state = RimWorldState.ToolStateSnapshot()
        };
    }

    public static object GetCellInfoResponse(int x, int z)
    {
        if (!TryGetMapContext(out var map, out var error))
            return Failure(error);
        var cell = new IntVec3(x, 0, z);
        if (!cell.InBounds(map))
            return Failure($"Cell ({x}, {z}) is out of bounds for the current map.");

        return new
        {
            success = true,
            cell = CreateCellInfoPayload(map, cell),
            state = RimWorldState.ToolStateSnapshot()
        };
    }

    public static object FindRandomCellNearResponse(
        int x,
        int z,
        int startingSearchRadius = 5,
        int maxSearchRadius = 60,
        int width = 1,
        int height = 1,
        string footprintAnchor = "top_left",
        bool requireWalkable = false,
        bool requireStandable = false,
        bool requireNotFogged = false,
        bool requireNoImpassableThings = false,
        string reachablePawnName = null,
        string designatorId = null)
    {
        if (!TryGetMapContext(out var map, out var error))
            return Failure(error);
        if (startingSearchRadius <= 0)
            return Failure("startingSearchRadius must be positive.");
        if (maxSearchRadius < startingSearchRadius)
            return Failure("maxSearchRadius must be greater than or equal to startingSearchRadius.");
        if (!TryCreateCellSearchCriteria(
                map,
                width,
                height,
                footprintAnchor,
                requireWalkable,
                requireStandable,
                requireNotFogged,
                requireNoImpassableThings,
                reachablePawnName,
                designatorId,
                out var criteria,
                out error))
        {
            return Failure(error);
        }

        var near = new IntVec3(x, 0, z);
        if (!near.InBounds(map))
            return Failure($"Cell ({x}, {z}) is out of bounds for the current map.");

        var found = RCellFinder.TryFindRandomCellNearWith(
            near,
            cell => CellSatisfiesCriteria(map, cell, criteria),
            map,
            out var result,
            startingSearchRadius,
            maxSearchRadius);

        if (!found)
        {
            return new
            {
                success = false,
                message = $"Could not find a nearby cell that satisfied the requested criteria near ({x}, {z}).",
                method = "RimWorld.RCellFinder.TryFindRandomCellNearWith",
                near = ToCellPayload(near),
                startingSearchRadius,
                maxSearchRadius,
                criteria = CreateCellSearchCriteriaPayload(criteria),
                state = RimWorldState.ToolStateSnapshot()
            };
        }

        return new
        {
            success = true,
            method = "RimWorld.RCellFinder.TryFindRandomCellNearWith",
            near = ToCellPayload(near),
            cell = ToCellPayload(result),
            cellInfo = CreateCellInfoPayload(map, result),
            footprint = CreateFootprintPayload(result, criteria.Width, criteria.Height, criteria.FootprintAnchor),
            startingSearchRadius,
            maxSearchRadius,
            criteria = CreateCellSearchCriteriaPayload(criteria),
            state = RimWorldState.ToolStateSnapshot()
        };
    }

    public static object FloodFillCellsResponse(
        int x,
        int z,
        int maxCellsToProcess = 256,
        int minimumCellCount = 0,
        int maxReturnedCells = 64,
        int width = 1,
        int height = 1,
        string footprintAnchor = "top_left",
        bool requireWalkable = false,
        bool requireStandable = false,
        bool requireNotFogged = false,
        bool requireNoImpassableThings = false,
        string reachablePawnName = null,
        string designatorId = null)
    {
        if (!TryGetMapContext(out var map, out var error))
            return Failure(error);
        if (maxCellsToProcess <= 0)
            return Failure("maxCellsToProcess must be positive.");
        if (minimumCellCount < 0)
            return Failure("minimumCellCount cannot be negative.");
        if (maxReturnedCells < 0)
            return Failure("maxReturnedCells cannot be negative.");
        if (!TryCreateCellSearchCriteria(
                map,
                width,
                height,
                footprintAnchor,
                requireWalkable,
                requireStandable,
                requireNotFogged,
                requireNoImpassableThings,
                reachablePawnName,
                designatorId,
                out var criteria,
                out error))
        {
            return Failure(error);
        }

        var root = new IntVec3(x, 0, z);
        if (!root.InBounds(map))
            return Failure($"Cell ({x}, {z}) is out of bounds for the current map.");

        var rootAccepted = CellSatisfiesCriteria(map, root, criteria);
        var returnedCells = new List<IntVec3>(Math.Min(maxReturnedCells, maxCellsToProcess));
        var visitedCellCount = 0;
        var maxTraversalDistance = 0;
        var stoppedEarly = false;
        var boundsInitialized = false;
        var minX = 0;
        var maxX = 0;
        var minZ = 0;
        var maxZ = 0;

        if (rootAccepted)
        {
            map.floodFiller.FloodFill(
                root,
                cell => CellSatisfiesCriteria(map, cell, criteria),
                (cell, traversalDistance) =>
                {
                    visitedCellCount++;
                    if (returnedCells.Count < maxReturnedCells)
                        returnedCells.Add(cell);

                    maxTraversalDistance = Math.Max(maxTraversalDistance, traversalDistance);
                    if (!boundsInitialized)
                    {
                        minX = maxX = cell.x;
                        minZ = maxZ = cell.z;
                        boundsInitialized = true;
                    }
                    else
                    {
                        minX = Math.Min(minX, cell.x);
                        maxX = Math.Max(maxX, cell.x);
                        minZ = Math.Min(minZ, cell.z);
                        maxZ = Math.Max(maxZ, cell.z);
                    }

                    if (minimumCellCount > 0 && visitedCellCount >= minimumCellCount)
                    {
                        stoppedEarly = true;
                        return true;
                    }

                    return false;
                },
                maxCellsToProcess,
                rememberParents: false,
                extraRoots: null);
        }

        var meetsMinimum = minimumCellCount <= 0
            ? visitedCellCount > 0
            : visitedCellCount >= minimumCellCount;
        var message = rootAccepted
            ? (meetsMinimum
                ? null
                : $"Flood fill found {visitedCellCount} matching cells, below the requested minimum of {minimumCellCount}.")
            : "The root cell did not satisfy the requested criteria.";

        return new
        {
            success = meetsMinimum,
            message,
            method = "Verse.FloodFiller.FloodFill",
            root = ToCellPayload(root),
            rootAccepted,
            visitedCellCount,
            minimumCellCount,
            maxCellsToProcess,
            hitCellProcessingLimit = rootAccepted && !stoppedEarly && visitedCellCount >= maxCellsToProcess,
            maxTraversalDistance,
            stoppedEarly,
            returnedCellCount = returnedCells.Count,
            returnedCellsTruncated = returnedCells.Count < visitedCellCount,
            cells = returnedCells.Select(ToCellPayload).ToList(),
            bounds = boundsInitialized
                ? new
                {
                    minX,
                    maxX,
                    minZ,
                    maxZ,
                    width = maxX - minX + 1,
                    height = maxZ - minZ + 1
                }
                : null,
            criteria = CreateCellSearchCriteriaPayload(criteria),
            state = RimWorldState.ToolStateSnapshot()
        };
    }

    public static object ListZonesResponse(bool includeHidden = false, bool includeEmpty = false)
    {
        if (!TryGetMapContext(out var map, out var error))
            return Failure(error);

        var zones = map.zoneManager?.AllZones
            ?.Where(zone => zone != null)
            .Where(zone => includeHidden || !zone.Hidden)
            .Where(zone => includeEmpty || zone.CellCount > 0)
            .OrderBy(zone => zone.BaseLabel, StringComparer.Ordinal)
            .ThenBy(zone => zone.GetUniqueLoadID(), StringComparer.Ordinal)
            .ToList() ?? [];

        return new
        {
            success = true,
            count = zones.Count,
            zones = zones.Select(DescribeZone).ToList(),
            state = RimWorldState.ToolStateSnapshot()
        };
    }

    public static object ListAreasResponse(bool includeEmpty = false, bool includeAssignableOnly = false)
    {
        if (!TryGetMapContext(out var map, out var error))
            return Failure(error);

        var areas = map.areaManager?.AllAreas
            ?.Where(area => area != null)
            .Where(area => includeEmpty || area.TrueCount > 0)
            .Where(area => !includeAssignableOnly || area.AssignableAsAllowed())
            .OrderBy(area => area.ListPriority)
            .ThenBy(area => area.Label, StringComparer.Ordinal)
            .ThenBy(area => area.GetUniqueLoadID(), StringComparer.Ordinal)
            .ToList() ?? [];

        return new
        {
            success = true,
            count = areas.Count,
            selectedAllowedArea = DescribeArea(Designator_AreaAllowed.SelectedArea),
            areas = areas.Select(DescribeArea).ToList(),
            state = RimWorldState.ToolStateSnapshot()
        };
    }

    public static object CreateAllowedAreaResponse(string label = null, bool select = true)
    {
        if (!TryGetMapContext(out var map, out var error))
            return Failure(error);

        var areaManager = map.areaManager;
        if (areaManager == null)
            return Failure("The current map does not expose an area manager.");
        if (!areaManager.TryMakeNewAllowed(out var area) || area == null)
            return Failure("RimWorld could not create a new allowed area for the current map.");

        if (!string.IsNullOrWhiteSpace(label))
            area.SetLabel(label.Trim());

        if (select)
            Designator_AreaAllowed.selectedArea = area;

        return new
        {
            success = true,
            created = true,
            area = DescribeArea(area),
            selectedAllowedArea = DescribeArea(Designator_AreaAllowed.SelectedArea),
            designatorState = CreateDesignatorStatePayload(),
            state = RimWorldState.ToolStateSnapshot()
        };
    }

    public static object SelectAllowedAreaResponse(string areaId = null)
    {
        if (!TryGetMapContext(out var map, out var error))
            return Failure(error);

        if (string.IsNullOrWhiteSpace(areaId))
        {
            Designator_AreaAllowed.ClearSelectedArea();
            return new
            {
                success = true,
                cleared = true,
                selectedAllowedArea = DescribeArea(Designator_AreaAllowed.SelectedArea),
                designatorState = CreateDesignatorStatePayload(),
                state = RimWorldState.ToolStateSnapshot()
            };
        }

        if (!TryResolveArea(map, areaId, out var area, out error))
            return Failure(error);
        if (!area.AssignableAsAllowed())
            return Failure($"Area '{areaId}' cannot be used as an allowed area.");

        Designator_AreaAllowed.selectedArea = area;
        return new
        {
            success = true,
            selectedAllowedArea = DescribeArea(Designator_AreaAllowed.SelectedArea),
            area = DescribeArea(area),
            designatorState = CreateDesignatorStatePayload(),
            state = RimWorldState.ToolStateSnapshot()
        };
    }

    public static object SetZoneTargetResponse(string designatorId, string zoneId = null)
    {
        if (!TryGetMapContext(out var map, out var error))
            return Failure(error);
        if (!TryResolveDesignator(designatorId, out var descriptor, out error))
            return Failure(error);
        if (descriptor.Designator is not Designator_ZoneAdd zoneAdd)
            return Failure($"Architect designator '{designatorId}' does not support explicit existing-zone targeting.");
        if (!TrySelectDescriptor(descriptor, out error))
            return Failure(error);

        if (string.IsNullOrWhiteSpace(zoneId))
        {
            zoneAdd.SelectedZone = null;
            var clearedSelectionState = CaptureSelectionState();
            return new
            {
                success = true,
                cleared = true,
                designator = DescribeDesignator(descriptor, clearedSelectionState),
                designatorState = CreateDesignatorStatePayload(GetAllDesignatorDescriptors(), clearedSelectionState),
                state = RimWorldState.ToolStateSnapshot()
            };
        }

        if (!TryResolveZone(map, zoneId, out var zone, out error))
            return Failure(error);
        if (zoneAdd.zoneTypeToPlace != null && !zoneAdd.zoneTypeToPlace.IsInstanceOfType(zone))
            return Failure($"Zone '{zoneId}' is not compatible with architect designator '{designatorId}'.");

        zoneAdd.SelectedZone = zone;
        var selectionState = CaptureSelectionState();
        return new
        {
            success = true,
            designator = DescribeDesignator(descriptor, selectionState),
            zone = DescribeZone(zone),
            designatorState = CreateDesignatorStatePayload(GetAllDesignatorDescriptors(), selectionState),
            state = RimWorldState.ToolStateSnapshot()
        };
    }

    public static object ClearAreaResponse(string areaId)
    {
        if (!TryGetMapContext(out var map, out var error))
            return Failure(error);
        if (!TryResolveArea(map, areaId, out var area, out error))
            return Failure(error);
        if (!area.Mutable)
            return Failure($"Area '{areaId}' is not mutable and cannot be cleared through this helper.");

        var previousCellCount = area.TrueCount;
        area.Clear();

        return new
        {
            success = true,
            previousCellCount,
            area = DescribeArea(area),
            selectedAllowedArea = DescribeArea(Designator_AreaAllowed.SelectedArea),
            designatorState = CreateDesignatorStatePayload(),
            state = RimWorldState.ToolStateSnapshot()
        };
    }

    public static object DeleteAreaResponse(string areaId)
    {
        if (!TryGetMapContext(out var map, out var error))
            return Failure(error);
        if (!TryResolveArea(map, areaId, out var area, out error))
            return Failure(error);
        if (!area.Mutable)
            return Failure($"Area '{areaId}' is not mutable and cannot be deleted through this helper.");

        var deletedArea = DescribeArea(area);
        var wasSelectedAllowedArea = ReferenceEquals(Designator_AreaAllowed.SelectedArea, area);
        area.Delete();
        if (wasSelectedAllowedArea)
            Designator_AreaAllowed.ClearSelectedArea();

        return new
        {
            success = true,
            deletedArea,
            selectedAllowedArea = DescribeArea(Designator_AreaAllowed.SelectedArea),
            designatorState = CreateDesignatorStatePayload(),
            state = RimWorldState.ToolStateSnapshot()
        };
    }

    public static object DeleteZoneResponse(string zoneId)
    {
        if (!TryGetMapContext(out var map, out var error))
            return Failure(error);
        if (!TryResolveZone(map, zoneId, out var zone, out error))
            return Failure(error);

        var deletedZone = DescribeZone(zone);
        foreach (var zoneAdd in GetAllDesignatorDescriptors()
                     .Select(descriptor => descriptor.Designator)
                     .OfType<Designator_ZoneAdd>()
                     .Where(zoneAdd => ReferenceEquals(zoneAdd.SelectedZone, zone)))
        {
            zoneAdd.SelectedZone = null;
        }

        zone.Delete();
        var selectionState = CaptureSelectionState();
        return new
        {
            success = true,
            deletedZone,
            designatorState = CreateDesignatorStatePayload(GetAllDesignatorDescriptors(), selectionState),
            state = RimWorldState.ToolStateSnapshot()
        };
    }

    private static object Failure(string message)
    {
        return new
        {
            success = false,
            message,
            devModeEnabled = Prefs.DevMode,
            godMode = DebugSettings.godMode,
            state = RimWorldState.ToolStateSnapshot()
        };
    }

    private static bool TryResolveCategory(string categoryId, bool includeHidden, out ArchitectCategoryDescriptor category, out string error)
    {
        category = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(categoryId))
        {
            error = "A category id is required.";
            return false;
        }

        category = EnumerateArchitectCategories(includeHidden, includeEmpty: true)
            .FirstOrDefault(candidate => ArchitectDesignatorIds.CategoryMatches(categoryId, candidate.Definition.defName));
        if (category != null)
            return true;

        error = $"Could not find architect category '{categoryId}'.";
        return false;
    }

    private static bool TryResolveDesignator(string designatorId, out ArchitectDesignatorDescriptor descriptor, out string error)
    {
        descriptor = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(designatorId))
        {
            error = "A designator id is required.";
            return false;
        }

        descriptor = GetAllDesignatorDescriptors()
            .FirstOrDefault(candidate => string.Equals(candidate.Id, designatorId.Trim(), StringComparison.Ordinal));
        if (descriptor != null)
            return true;

        error = $"Could not find architect designator '{designatorId}'.";
        return false;
    }

    private static bool TryResolveZone(Map map, string zoneId, out Zone zone, out string error)
    {
        zone = null;
        error = string.Empty;
        if (map.zoneManager == null)
        {
            error = "The current map does not expose a zone manager.";
            return false;
        }

        if (!TryResolveByIdentity(
                map.zoneManager.AllZones?.Where(candidate => candidate != null).Cast<Zone>() ?? [],
                zoneId,
                candidate => candidate.GetUniqueLoadID(),
                candidate => candidate.RenamableLabel,
                candidate => candidate.BaseLabel,
                out zone,
                out error))
        {
            return false;
        }

        return true;
    }

    private static bool TryResolveArea(Map map, string areaId, out Area area, out string error)
    {
        area = null;
        error = string.Empty;
        if (map.areaManager == null)
        {
            error = "The current map does not expose an area manager.";
            return false;
        }

        if (!TryResolveByIdentity(
                map.areaManager.AllAreas?.Where(candidate => candidate != null).Cast<Area>() ?? [],
                areaId,
                candidate => candidate.GetUniqueLoadID(),
                candidate => candidate.RenamableLabel,
                candidate => candidate.Label,
                out area,
                out error))
        {
            return false;
        }

        return true;
    }

    private static bool TryResolveByIdentity<T>(
        IEnumerable<T> candidates,
        string requestedId,
        Func<T, string> identitySelector,
        Func<T, string> primaryLabelSelector,
        Func<T, string> secondaryLabelSelector,
        out T value,
        out string error)
        where T : class
    {
        value = default;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(requestedId))
        {
            error = "An id is required.";
            return false;
        }

        var trimmed = requestedId.Trim();
        var allCandidates = candidates.ToList();
        var exact = allCandidates.FirstOrDefault(candidate => string.Equals(identitySelector(candidate), trimmed, StringComparison.Ordinal));
        if (exact != null)
        {
            value = exact;
            return true;
        }

        var labelMatches = allCandidates
            .Where(candidate =>
                string.Equals(primaryLabelSelector(candidate), trimmed, StringComparison.OrdinalIgnoreCase)
                || string.Equals(secondaryLabelSelector(candidate), trimmed, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (labelMatches.Count == 1)
        {
            value = labelMatches[0];
            return true;
        }

        if (labelMatches.Count > 1)
        {
            error = $"'{requestedId}' matched multiple entries. Use the exact unique id instead.";
            return false;
        }

        error = $"Could not find '{requestedId}'.";
        return false;
    }

    private static bool TrySelectDescriptor(ArchitectDesignatorDescriptor descriptor, out string error)
    {
        error = string.Empty;
        var manager = TryGetDesignatorManager();
        if (manager == null)
        {
            error = "RimWorld's designator manager is not available.";
            return false;
        }

        if (descriptor.ContainerDesignator is Designator_Dropdown dropdown)
            dropdown.SetActiveDesignator(descriptor.Designator, true);

        manager.Select(descriptor.EffectiveRootDesignator);
        manager.CheckSelectedDesignatorValid();
        return true;
    }

    private static List<ArchitectCategoryDescriptor> EnumerateArchitectCategories(bool includeHidden, bool includeEmpty)
    {
        var categories = new List<ArchitectCategoryDescriptor>();
        foreach (var category in DefDatabase<DesignationCategoryDef>.AllDefsListForReading
                     .Where(definition => definition != null)
                     .OrderBy(definition => definition.order)
                     .ThenBy(definition => definition.defName, StringComparer.Ordinal))
        {
            var visibleRoots = GetVisibleRootDesignators(category);
            var visibleDesignators = CreateVisibleDesignatorSet(visibleRoots);
            var roots = includeHidden ? GetAllRootDesignators(category) : visibleRoots;
            var designators = BuildDesignatorDescriptors(category, roots, visibleDesignators);
            var categoryVisible = category.Visible;

            if (!includeHidden && !categoryVisible)
                continue;
            if (!includeEmpty && designators.Count == 0)
                continue;

            categories.Add(new ArchitectCategoryDescriptor
            {
                Definition = category,
                Id = ArchitectDesignatorIds.CreateCategoryId(category.defName),
                Visible = categoryVisible,
                Designators = designators
            });
        }

        return categories;
    }

    private static List<ArchitectDesignatorDescriptor> GetAllDesignatorDescriptors()
    {
        if (Current.Game == null)
            return [];

        return EnumerateArchitectCategories(includeHidden: true, includeEmpty: true)
            .SelectMany(category => category.Designators)
            .ToList();
    }

    private static List<Designator> GetVisibleRootDesignators(DesignationCategoryDef category)
    {
        return category.ResolvedAllowedDesignators
            .Cast<Designator>()
            .Concat(category.AllIdeoDesignators.Cast<Designator>())
            .Where(designator => designator != null)
            .Distinct()
            .ToList();
    }

    private static List<Designator> GetAllRootDesignators(DesignationCategoryDef category)
    {
        return category.AllResolvedAndIdeoDesignators
            .Cast<Designator>()
            .Where(designator => designator != null)
            .Distinct()
            .ToList();
    }

    private static HashSet<Designator> CreateVisibleDesignatorSet(IEnumerable<Designator> visibleRoots)
    {
        var visible = new HashSet<Designator>();
        foreach (var root in visibleRoots)
        {
            if (root == null)
                continue;

            visible.Add(root);
            if (root is not Designator_Dropdown dropdown)
                continue;

            foreach (var child in dropdown.Elements ?? [])
            {
                if (child != null)
                    visible.Add(child);
            }
        }

        return visible;
    }

    private static List<ArchitectDesignatorDescriptor> BuildDesignatorDescriptors(DesignationCategoryDef category, IEnumerable<Designator> roots, HashSet<Designator> visibleDesignators)
    {
        var results = new List<ArchitectDesignatorDescriptor>();
        var rootKeys = new Dictionary<string, int>(StringComparer.Ordinal);
        var rootIndex = 0;
        foreach (var root in roots)
        {
            if (root == null)
                continue;

            rootIndex++;
            var rootKey = CreateUniqueKey(rootKeys, BuildStableDesignatorKey(root));
            var rootId = ArchitectDesignatorIds.CreateDesignatorId(category.defName, rootKey);
            var childCount = root is Designator_Dropdown dropdown ? dropdown.Elements?.Count ?? 0 : 0;

            results.Add(new ArchitectDesignatorDescriptor
            {
                Category = category,
                Designator = root,
                Id = rootId,
                CategoryId = ArchitectDesignatorIds.CreateCategoryId(category.defName),
                Kind = ResolveDesignatorKind(root),
                ApplicationKind = ResolveDesignatorApplicationKind(root),
                Index = rootIndex,
                ChildCount = childCount,
                Visible = visibleDesignators.Contains(root),
                Actionable = root is not Designator_Dropdown,
                SupportsCellApplication = root is not Designator_Dropdown,
                SupportsRectangleApplication = ResolveSupportsRectangleApplication(root)
            });

            if (root is not Designator_Dropdown dropdownWithChildren || childCount == 0)
                continue;

            var childKeys = new Dictionary<string, int>(StringComparer.Ordinal);
            var childIndex = 0;
            foreach (var child in dropdownWithChildren.Elements ?? [])
            {
                if (child == null)
                    continue;

                childIndex++;
                var childKey = CreateUniqueKey(childKeys, BuildStableDesignatorKey(child));
                results.Add(new ArchitectDesignatorDescriptor
                {
                    Category = category,
                    Designator = child,
                    ContainerDesignator = dropdownWithChildren,
                    Id = ArchitectDesignatorIds.CreateDropdownChildDesignatorId(category.defName, rootKey, childKey),
                    CategoryId = ArchitectDesignatorIds.CreateCategoryId(category.defName),
                    ParentId = rootId,
                    Kind = ResolveDesignatorKind(child),
                    ApplicationKind = ResolveDesignatorApplicationKind(child),
                    Index = rootIndex,
                    ChildIndex = childIndex,
                    Visible = visibleDesignators.Contains(child),
                    Actionable = true,
                    SupportsCellApplication = true,
                    SupportsRectangleApplication = ResolveSupportsRectangleApplication(child)
                });
            }
        }

        return results;
    }

    private static string CreateUniqueKey(Dictionary<string, int> counts, string baseKey)
    {
        if (!counts.TryGetValue(baseKey, out var count))
        {
            counts[baseKey] = 1;
            return baseKey;
        }

        count++;
        counts[baseKey] = count;
        return baseKey + "-" + count.ToString(CultureInfo.InvariantCulture);
    }

    private static string BuildStableDesignatorKey(Designator designator)
    {
        if (designator is Designator_Build build && !string.IsNullOrWhiteSpace(build.PlacingDef?.defName))
            return "build-" + build.PlacingDef.defName;

        if (designator is Designator_Dropdown)
            return "dropdown-" + BuildStableDesignatorLabelKey(designator);

        return BuildStableDesignatorLabelKey(designator);
    }

    private static string BuildStableDesignatorLabelKey(Designator designator)
    {
        if (!string.IsNullOrWhiteSpace(designator.HighlightTag))
            return "highlight-" + designator.HighlightTag;
        if (!string.IsNullOrWhiteSpace(designator.defaultLabel))
            return "label-" + designator.defaultLabel;
        if (!string.IsNullOrWhiteSpace(designator.Label))
            return "label-" + designator.Label;

        return "type-" + designator.GetType().Name;
    }

    private static string ResolveDesignatorKind(Designator designator)
    {
        return designator switch
        {
            Designator_Dropdown => "dropdown",
            Designator_Build => "build",
            _ => "designator"
        };
    }

    private static string ResolveDesignatorApplicationKind(Designator designator)
    {
        if (designator is Designator_Build)
            return "build";

        var typeName = designator.GetType().Name;
        if (typeName.StartsWith("Designator_Zone", StringComparison.Ordinal))
            return "zone";
        if (typeName.StartsWith("Designator_Area", StringComparison.Ordinal))
            return "area";

        return "designation";
    }

    private static bool ResolveSupportsRectangleApplication(Designator designator)
    {
        if (designator is Designator_Dropdown)
            return false;

        if (designator.DragDrawMeasurements)
            return true;

        return ResolveDesignatorApplicationKind(designator) is "build" or "zone" or "area";
    }

    private static ArchitectSelectionState CaptureSelectionState()
    {
        var manager = TryGetDesignatorManager();
        var selectedRoot = manager?.SelectedDesignator;
        var selectedAction = selectedRoot is Designator_Dropdown dropdown && dropdown.activeDesignator != null
            ? dropdown.activeDesignator
            : selectedRoot;

        return new ArchitectSelectionState
        {
            SelectedRootDesignator = selectedRoot,
            SelectedActionDesignator = selectedAction
        };
    }

    private static object CreateDesignatorStatePayload(IEnumerable<ArchitectDesignatorDescriptor> knownDesignators = null, ArchitectSelectionState selectionState = null)
    {
        var descriptors = knownDesignators?.ToList() ?? GetAllDesignatorDescriptors();
        var currentSelection = selectionState ?? CaptureSelectionState();
        ArchitectDesignatorDescriptor selected = null;
        ArchitectDesignatorDescriptor selectedContainer = null;

        foreach (var descriptor in descriptors)
        {
            if (selected == null && IsSelectedDescriptor(descriptor, currentSelection))
                selected = descriptor;

            if (selectedContainer == null && IsSelectedContainerDescriptor(descriptor, currentSelection))
                selectedContainer = descriptor;

            if (selected != null && selectedContainer != null)
                break;
        }

        var selectedRoot = currentSelection.SelectedRootDesignator;
        return new
        {
            hasSelection = selectedRoot != null,
            selectedDesignatorId = selected?.Id,
            selectedDesignator = selected == null ? null : DescribeDesignator(selected, currentSelection),
            selectedContainerDesignatorId = selectedContainer?.Id,
            selectedContainerDesignator = selectedContainer == null ? null : DescribeDesignator(selectedContainer, currentSelection),
            selectedAllowedArea = DescribeArea(Designator_AreaAllowed.SelectedArea),
            unmappedSelectedDesignator = selected == null && selectedRoot != null ? DescribeUnmappedSelectedDesignator(selectedRoot) : null
        };
    }

    private static bool IsSelectedDescriptor(ArchitectDesignatorDescriptor descriptor, ArchitectSelectionState selectionState)
    {
        if (descriptor.ContainerDesignator != null)
        {
            return ReferenceEquals(selectionState.SelectedRootDesignator, descriptor.ContainerDesignator)
                && ReferenceEquals(selectionState.SelectedActionDesignator, descriptor.Designator);
        }

        return ReferenceEquals(selectionState.SelectedActionDesignator, descriptor.Designator);
    }

    private static bool IsSelectedContainerDescriptor(ArchitectDesignatorDescriptor descriptor, ArchitectSelectionState selectionState)
    {
        return ReferenceEquals(selectionState.SelectedRootDesignator, descriptor.Designator);
    }

    private static object DescribeCategory(ArchitectCategoryDescriptor category)
    {
        return new
        {
            id = category.Id,
            categoryDefName = category.Definition.defName,
            label = category.Definition.LabelCap.ToString(),
            visible = category.Visible,
            order = category.Definition.order,
            preferredColumn = category.Definition.preferredColumn,
            showPowerGrid = category.Definition.showPowerGrid,
            designatorCount = category.Designators.Count(designator => designator.ParentId == null),
            actionableDesignatorCount = category.Designators.Count(designator => designator.Actionable),
            dropdownCount = category.Designators.Count(designator => designator.Kind == "dropdown"),
            buildDesignatorCount = category.Designators.Count(designator => designator.Kind == "build")
        };
    }

    private static object DescribeDesignator(ArchitectDesignatorDescriptor descriptor, ArchitectSelectionState selectionState)
    {
        var build = descriptor.Designator as Designator_Build;
        var activeChildId = descriptor.Designator is Designator_Dropdown dropdown
            ? ResolveActiveDropdownChildId(descriptor, dropdown)
            : null;

        return new
        {
            id = descriptor.Id,
            parentId = descriptor.ParentId,
            categoryId = descriptor.CategoryId,
            categoryDefName = descriptor.Category.defName,
            categoryLabel = descriptor.Category.LabelCap.ToString(),
            kind = descriptor.Kind,
            applicationKind = descriptor.ApplicationKind,
            actionable = descriptor.Actionable,
            supportsCellApplication = descriptor.SupportsCellApplication,
            supportsRectangleApplication = descriptor.SupportsRectangleApplication,
            dragDrawMeasurements = descriptor.Designator.DragDrawMeasurements,
            drawStyleCategoryDefName = descriptor.Designator.DrawStyleCategory?.defName,
            visible = descriptor.Visible,
            selected = IsSelectedDescriptor(descriptor, selectionState),
            selectedContainer = IsSelectedContainerDescriptor(descriptor, selectionState),
            index = descriptor.Index,
            childIndex = descriptor.ChildIndex,
            childCount = descriptor.ChildCount,
            label = descriptor.Designator.Label,
            description = descriptor.Designator.Desc,
            className = descriptor.Designator.GetType().FullName ?? descriptor.Designator.GetType().Name,
            highlightTag = descriptor.Designator.HighlightTag,
            hotKey = descriptor.Designator.hotKey?.defName,
            designationDefName = descriptor.Designator.Designation?.defName,
            buildableDefName = build?.PlacingDef?.defName,
            buildableLabel = build?.PlacingDef?.LabelCap.ToString(),
            stuffDefName = build?.StuffDef?.defName,
            zoneTypeName = descriptor.Designator is Designator_ZoneAdd zoneAdd && zoneAdd.zoneTypeToPlace != null
                ? zoneAdd.zoneTypeToPlace.FullName ?? zoneAdd.zoneTypeToPlace.Name
                : null,
            selectedZone = descriptor.Designator is Designator_ZoneAdd activeZoneAdd
                ? DescribeZone(activeZoneAdd.SelectedZone)
                : null,
            selectedAllowedArea = descriptor.ApplicationKind == "area"
                ? DescribeArea(Designator_AreaAllowed.SelectedArea)
                : null,
            dropdownActiveChildId = activeChildId
        };
    }

    private static string ResolveActiveDropdownChildId(ArchitectDesignatorDescriptor parentDescriptor, Designator_Dropdown dropdown)
    {
        var activeChild = dropdown.activeDesignator;
        if (activeChild == null)
            return string.Empty;

        return GetAllDesignatorDescriptors()
            .FirstOrDefault(candidate =>
                string.Equals(candidate.ParentId, parentDescriptor.Id, StringComparison.Ordinal)
                && ReferenceEquals(candidate.Designator, activeChild))
            ?.Id ?? string.Empty;
    }

    private static object DescribeUnmappedSelectedDesignator(Designator designator)
    {
        return new
        {
            label = designator.Label,
            className = designator.GetType().FullName ?? designator.GetType().Name,
            highlightTag = designator.HighlightTag,
            designationDefName = designator.Designation?.defName
        };
    }

    private static bool TryCreateCellSearchCriteria(
        Map map,
        int width,
        int height,
        string footprintAnchor,
        bool requireWalkable,
        bool requireStandable,
        bool requireNotFogged,
        bool requireNoImpassableThings,
        string reachablePawnName,
        string designatorId,
        out CellSearchCriteria criteria,
        out string error)
    {
        criteria = null;
        error = string.Empty;

        if (width <= 0 || height <= 0)
        {
            error = "Width and height must be positive.";
            return false;
        }

        if (!TryNormalizeFootprintAnchor(footprintAnchor, out var normalizedAnchor, out error))
            return false;

        Pawn reachablePawn = null;
        if (!string.IsNullOrWhiteSpace(reachablePawnName))
        {
            try
            {
                reachablePawn = RimWorldState.ResolveCurrentMapPawn(reachablePawnName);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        ArchitectDesignatorDescriptor designatorDescriptor = null;
        if (!string.IsNullOrWhiteSpace(designatorId))
        {
            if (!TryResolveDesignator(designatorId, out designatorDescriptor, out error))
                return false;
            if (!designatorDescriptor.Actionable || !designatorDescriptor.SupportsCellApplication)
            {
                error = $"Architect designator '{designatorId}' does not support direct cell validation.";
                return false;
            }
        }

        criteria = new CellSearchCriteria
        {
            Width = width,
            Height = height,
            FootprintAnchor = normalizedAnchor,
            RequireWalkable = requireWalkable,
            RequireStandable = requireStandable,
            RequireNotFogged = requireNotFogged,
            RequireNoImpassableThings = requireNoImpassableThings,
            ReachablePawn = reachablePawn,
            ReachablePawnName = reachablePawnName,
            DesignatorDescriptor = designatorDescriptor
        };
        return true;
    }

    private static bool TryNormalizeFootprintAnchor(string footprintAnchor, out string normalizedAnchor, out string error)
    {
        error = string.Empty;
        normalizedAnchor = "top_left";
        if (string.IsNullOrWhiteSpace(footprintAnchor))
            return true;

        var trimmed = footprintAnchor.Trim();
        if (string.Equals(trimmed, "top_left", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "top-left", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "origin", StringComparison.OrdinalIgnoreCase))
        {
            normalizedAnchor = "top_left";
            return true;
        }

        if (string.Equals(trimmed, "center", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "centre", StringComparison.OrdinalIgnoreCase))
        {
            normalizedAnchor = "center";
            return true;
        }

        error = $"Unsupported footprintAnchor '{footprintAnchor}'. Use 'top_left' or 'center'.";
        return false;
    }

    private static bool CellSatisfiesCriteria(Map map, IntVec3 anchorCell, CellSearchCriteria criteria)
    {
        var footprintOrigin = GetFootprintOrigin(anchorCell, criteria.Width, criteria.Height, criteria.FootprintAnchor);
        if (criteria.ReachablePawn != null)
        {
            var traverseParms = TraverseParms.For(criteria.ReachablePawn);
            if (!map.reachability.CanReach(criteria.ReachablePawn.Position, anchorCell, PathEndMode.OnCell, traverseParms))
                return false;
        }

        foreach (var footprintCell in EnumerateCells(footprintOrigin.x, footprintOrigin.z, criteria.Width, criteria.Height))
        {
            if (!footprintCell.InBounds(map))
                return false;
            if (criteria.RequireNotFogged && footprintCell.Fogged(map))
                return false;
            if (criteria.RequireWalkable && !footprintCell.Walkable(map))
                return false;
            if (criteria.RequireStandable && !footprintCell.Standable(map))
                return false;
            if (criteria.RequireNoImpassableThings && footprintCell.Impassable(map))
                return false;

            if (criteria.DesignatorDescriptor != null)
            {
                var report = criteria.DesignatorDescriptor.Designator.CanDesignateCell(footprintCell);
                if (!report.Accepted)
                    return false;
            }
        }

        return true;
    }

    private static IntVec3 GetFootprintOrigin(IntVec3 anchorCell, int width, int height, string footprintAnchor)
    {
        if (string.Equals(footprintAnchor, "center", StringComparison.Ordinal))
        {
            return new IntVec3(
                anchorCell.x - (width / 2),
                0,
                anchorCell.z - (height / 2));
        }

        return anchorCell;
    }

    private static object CreateFootprintPayload(IntVec3 anchorCell, int width, int height, string footprintAnchor)
    {
        var origin = GetFootprintOrigin(anchorCell, width, height, footprintAnchor);
        var cells = EnumerateCells(origin.x, origin.z, width, height)
            .Select(ToCellPayload)
            .ToList();

        return new
        {
            anchor = ToCellPayload(anchorCell),
            anchorMode = footprintAnchor,
            origin = ToCellPayload(origin),
            width,
            height,
            cellCount = cells.Count,
            cells
        };
    }

    private static object CreateCellSearchCriteriaPayload(CellSearchCriteria criteria)
    {
        return new
        {
            width = criteria.Width,
            height = criteria.Height,
            footprintAnchor = criteria.FootprintAnchor,
            requireWalkable = criteria.RequireWalkable,
            requireStandable = criteria.RequireStandable,
            requireNotFogged = criteria.RequireNotFogged,
            requireNoImpassableThings = criteria.RequireNoImpassableThings,
            reachablePawnName = criteria.ReachablePawnName,
            designator = criteria.DesignatorDescriptor == null
                ? null
                : new
                {
                    id = criteria.DesignatorDescriptor.Id,
                    label = criteria.DesignatorDescriptor.Designator.Label,
                    className = criteria.DesignatorDescriptor.Designator.GetType().FullName ?? criteria.DesignatorDescriptor.Designator.GetType().Name,
                    applicationKind = criteria.DesignatorDescriptor.ApplicationKind
                }
        };
    }

    private static IEnumerable<IntVec3> EnumerateCells(int x, int z, int width, int height)
    {
        for (var offsetZ = 0; offsetZ < height; offsetZ++)
        {
            for (var offsetX = 0; offsetX < width; offsetX++)
                yield return new IntVec3(x + offsetX, 0, z + offsetZ);
        }
    }

    private static object ToCellPayload(IntVec3 cell)
    {
        return new
        {
            x = cell.x,
            z = cell.z
        };
    }

    private static object CreateCellInfoPayload(Map map, IntVec3 cell)
    {
        var things = map.thingGrid.ThingsListAt(cell)
            .Where(thing => thing != null)
            .ToList();
        var designations = map.designationManager.AllDesignationsAt(cell)
            .Where(designation => designation != null)
            .ToList();
        var blueprintBuildDefs = things
            .OfType<Blueprint_Build>()
            .Select(blueprint => blueprint.BuildDef?.defName)
            .Where(defName => string.IsNullOrWhiteSpace(defName) == false)
            .Distinct()
            .OrderBy(defName => defName, StringComparer.Ordinal)
            .ToList();
        var frameBuildDefs = things
            .OfType<Frame>()
            .Select(frame => frame.BuildDef?.defName)
            .Where(defName => string.IsNullOrWhiteSpace(defName) == false)
            .Distinct()
            .OrderBy(defName => defName, StringComparer.Ordinal)
            .ToList();
        var solidThingDefs = things
            .Where(thing => thing is not Blueprint && thing is not Frame)
            .Select(thing => thing.def?.defName)
            .Where(defName => string.IsNullOrWhiteSpace(defName) == false)
            .Distinct()
            .OrderBy(defName => defName, StringComparer.Ordinal)
            .ToList();
        var zone = map.zoneManager?.ZoneAt(cell);
        var areas = map.areaManager?.AllAreas
            ?.Where(area => area != null && area[cell])
            .OrderBy(area => area.ListPriority)
            .ThenBy(area => area.Label, StringComparer.Ordinal)
            .ToList() ?? [];

        return new
        {
            x = cell.x,
            z = cell.z,
            terrainDefName = map.terrainGrid.TerrainAt(cell)?.defName,
            roofDefName = map.roofGrid.RoofAt(cell)?.defName,
            fogged = cell.Fogged(map),
            walkable = cell.Walkable(map),
            thingCount = things.Count,
            designationCount = designations.Count,
            blueprintBuildDefs,
            frameBuildDefs,
            solidThingDefs,
            zone = DescribeZone(zone),
            areaCount = areas.Count,
            areas = areas.Select(DescribeArea).ToList(),
            things = things.Select(DescribeThingAtCell).ToList(),
            designations = designations.Select(DescribeDesignationAtCell).ToList()
        };
    }

    private static object DescribeZone(Zone zone)
    {
        if (zone == null)
            return null;

        return new
        {
            id = zone.GetUniqueLoadID(),
            label = zone.RenamableLabel,
            baseLabel = zone.BaseLabel,
            inspectLabel = zone.InspectLabel,
            className = zone.GetType().FullName ?? zone.GetType().Name,
            cellCount = zone.CellCount,
            hidden = zone.Hidden,
            position = new
            {
                x = zone.Position.x,
                z = zone.Position.z
            }
        };
    }

    private static object DescribeArea(Area area)
    {
        if (area == null)
            return null;

        return new
        {
            id = area.GetUniqueLoadID(),
            label = area.Label,
            baseLabel = area.BaseLabel,
            renamableLabel = area.RenamableLabel,
            className = area.GetType().FullName ?? area.GetType().Name,
            cellCount = area.TrueCount,
            listPriority = area.ListPriority,
            mutable = area.Mutable,
            assignableAsAllowed = area.AssignableAsAllowed()
        };
    }

    private static object DescribeThingAtCell(Thing thing)
    {
        var blueprint = thing as Blueprint_Build;
        var frame = thing as Frame;
        return new
        {
            defName = thing.def?.defName,
            label = thing.LabelCap.ToString(),
            className = thing.GetType().FullName ?? thing.GetType().Name,
            stuffDefName = thing.Stuff?.defName,
            isBlueprint = thing is Blueprint,
            isBlueprintBuild = blueprint != null,
            blueprintBuildDefName = blueprint?.BuildDef?.defName,
            isFrame = frame != null,
            frameBuildDefName = frame?.BuildDef?.defName,
            hitPoints = thing.HitPoints
        };
    }

    private static object DescribeDesignationAtCell(Designation designation)
    {
        return new
        {
            defName = designation.def?.defName,
            targetCell = designation.target.Cell.IsValid
                ? new { x = designation.target.Cell.x, z = designation.target.Cell.z }
                : null,
            targetThingDefName = designation.target.HasThing ? designation.target.Thing?.def?.defName : null
        };
    }

    private static bool TryGetMapContext(out Map map, out string error)
    {
        map = null;
        error = string.Empty;

        if (Current.Game == null)
        {
            error = "No game is currently loaded.";
            return false;
        }

        if (Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null)
        {
            error = "Architect tools require an active map.";
            return false;
        }

        map = Find.CurrentMap;
        return true;
    }

    private static DesignatorManager TryGetDesignatorManager()
    {
        if (!TryGetMapContext(out _, out _))
            return null;

        try
        {
            return Find.DesignatorManager;
        }
        catch
        {
            return null;
        }
    }
}
