using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RimBridgeServer.Core;
using RimWorld;
using Verse;

namespace RimBridgeServer;

internal static class RimWorldArchitect
{
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
        if (!dryRun && acceptedCells.Count > 0)
        {
            try
            {
                if (acceptedCells.Count == 1)
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
                Index = rootIndex,
                ChildCount = childCount,
                Visible = visibleDesignators.Contains(root),
                Actionable = root is not Designator_Dropdown,
                SupportsCellApplication = root is not Designator_Dropdown
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
                    Index = rootIndex,
                    ChildIndex = childIndex,
                    Visible = visibleDesignators.Contains(child),
                    Actionable = true,
                    SupportsCellApplication = true
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
            actionable = descriptor.Actionable,
            supportsCellApplication = descriptor.SupportsCellApplication,
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
            things = things.Select(DescribeThingAtCell).ToList(),
            designations = designations.Select(DescribeDesignationAtCell).ToList()
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
