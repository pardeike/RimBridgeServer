using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using RimBridgeServer.Core;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimBridgeServer;

internal static class RimWorldNotifications
{
    private const int MaxDetailedTargets = 12;
    private const int MaxDetailedSelectionObjects = 12;

    private static readonly FieldInfo LiveMessagesField = GetRequiredField(
        typeof(Messages),
        "liveMessages",
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly FieldInfo ActiveAlertsField = GetRequiredField(
        typeof(AlertsReadout),
        "activeAlerts",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly FieldInfo DiaOptionTextField = GetRequiredField(
        typeof(DiaOption),
        "text",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private sealed class AlertSnapshot
    {
        public Alert Alert { get; set; }

        public int OriginalIndex { get; set; }

        public int DisplayOrdinal { get; set; }

        public string Id { get; set; } = string.Empty;

        public string AlertType { get; set; } = string.Empty;

        public string PriorityName { get; set; } = string.Empty;

        public int PrioritySortValue { get; set; }

        public string Label { get; set; } = string.Empty;

        public string Explanation { get; set; }

        public string JumpToTargetsText { get; set; }

        public bool Active { get; set; }

        public bool EnabledWithActiveExpansions { get; set; }

        public bool AnyCulpritValid { get; set; }

        public List<GlobalTargetInfo> Culprits { get; } = [];

        public List<string> CulpritTokens { get; } = [];

        public string Error { get; set; }

        public string ExceptionType { get; set; }
    }

    private sealed class StateCapture
    {
        public object State { get; set; }

        public UiStateSnapshot UiState { get; set; }

        public string CameraSignature { get; set; } = string.Empty;

        public List<string> SelectionTokens { get; } = [];
    }

    public static object ListMessagesResponse(int limit = 12)
    {
        if (Current.Game == null)
            return Failure("No game is currently loaded.");

        var maxCount = Math.Max(limit, 0);
        var allMessages = GetLiveMessagesInDisplayOrder();
        var messages = allMessages
            .Take(maxCount)
            .Select(DescribeMessage)
            .ToList();

        return new
        {
            success = true,
            currentGameTick = Find.TickManager?.TicksGame ?? 0,
            totalCount = allMessages.Count,
            returnedCount = messages.Count,
            truncated = messages.Count < allMessages.Count,
            messages
        };
    }

    public static object ListLettersResponse(int limit = 40)
    {
        if (Current.Game == null)
            return Failure("No game is currently loaded.");

        var maxCount = Math.Max(limit, 0);
        var allLetters = GetLettersInDisplayOrder();
        var letters = allLetters
            .Take(maxCount)
            .Select(DescribeLetter)
            .ToList();

        return new
        {
            success = true,
            currentGameTick = Find.TickManager?.TicksGame ?? 0,
            totalCount = allLetters.Count,
            returnedCount = letters.Count,
            truncated = letters.Count < allLetters.Count,
            letters
        };
    }

    public static object OpenLetterResponse(string letterId)
    {
        if (Current.Game == null)
            return Failure("No game is currently loaded.");
        if (string.IsNullOrWhiteSpace(letterId))
            return Failure("A letter id is required.");
        if (!TryResolveLetter(letterId, out var letter, out var error))
            return Failure(error);

        var before = CaptureState();
        try
        {
            letter.OpenLetter();
        }
        catch (Exception ex)
        {
            var failure = UnwrapInvocationException(ex);
            var afterFailure = CaptureState();
            return new
            {
                success = false,
                message = $"Opening letter '{letterId}' failed: {failure.Message}",
                exceptionType = failure.GetType().FullName,
                requestedLetterId = letterId,
                letter = DescribeLetter(letter),
                stateBefore = before.State,
                stateAfter = afterFailure.State,
                effects = DescribeEffects(before, afterFailure)
            };
        }

        var after = CaptureState();
        return new
        {
            success = true,
            requestedLetterId = letterId,
            letter = DescribeLetter(letter),
            stillPresentInLetterStack = IsLetterStillPresent(letterId),
            stateBefore = before.State,
            stateAfter = after.State,
            effects = DescribeEffects(before, after)
        };
    }

    public static object DismissLetterResponse(string letterId)
    {
        if (Current.Game == null)
            return Failure("No game is currently loaded.");
        if (string.IsNullOrWhiteSpace(letterId))
            return Failure("A letter id is required.");
        if (!TryResolveLetter(letterId, out var letter, out var error))
            return Failure(error);
        if (letter.CanDismissWithRightClick == false)
        {
            return new
            {
                success = false,
                message = $"Letter '{letterId}' cannot be dismissed through the letter stack.",
                requestedLetterId = letterId,
                letter = DescribeLetter(letter)
            };
        }

        var before = CaptureState();
        try
        {
            Find.LetterStack.RemoveLetter(letter);
        }
        catch (Exception ex)
        {
            var failure = UnwrapInvocationException(ex);
            var afterFailure = CaptureState();
            return new
            {
                success = false,
                message = $"Dismissing letter '{letterId}' failed: {failure.Message}",
                exceptionType = failure.GetType().FullName,
                requestedLetterId = letterId,
                letter = DescribeLetter(letter),
                stateBefore = before.State,
                stateAfter = afterFailure.State,
                effects = DescribeEffects(before, afterFailure)
            };
        }

        var after = CaptureState();
        return new
        {
            success = true,
            requestedLetterId = letterId,
            dismissed = IsLetterStillPresent(letterId) == false,
            letterId,
            stateBefore = before.State,
            stateAfter = after.State,
            effects = DescribeEffects(before, after)
        };
    }

    public static object ListAlertsResponse(int limit = 40)
    {
        if (Current.Game == null)
            return Failure("No game is currently loaded.");
        if (Find.Alerts == null)
            return Failure("RimWorld alerts are not available.");

        var maxCount = Math.Max(limit, 0);
        var alerts = BuildOrderedActiveAlerts();
        var returnedAlerts = alerts
            .Take(maxCount)
            .Select(DescribeAlert)
            .ToList();

        return new
        {
            success = true,
            currentGameTick = Find.TickManager?.TicksGame ?? 0,
            alertSnapshotFingerprint = CreateAlertSnapshotFingerprint(alerts),
            totalCount = alerts.Count,
            returnedCount = returnedAlerts.Count,
            truncated = returnedAlerts.Count < alerts.Count,
            alerts = returnedAlerts
        };
    }

    public static object ActivateAlertResponse(string alertId)
    {
        if (Current.Game == null)
            return Failure("No game is currently loaded.");
        if (Find.Alerts == null)
            return Failure("RimWorld alerts are not available.");
        if (string.IsNullOrWhiteSpace(alertId))
            return Failure("An alert id is required.");

        var alerts = BuildOrderedActiveAlerts();
        var target = alerts.FirstOrDefault(snapshot => string.Equals(snapshot.Id, alertId, StringComparison.Ordinal));
        if (target == null)
        {
            var currentFingerprint = CreateAlertSnapshotFingerprint(alerts);
            var staleSnapshot = NotificationIds.TryReadAlertSnapshotFingerprint(alertId, out var requestedFingerprint)
                && string.Equals(requestedFingerprint, currentFingerprint, StringComparison.Ordinal) == false;

            return new
            {
                success = false,
                message = staleSnapshot
                    ? "The requested alert id no longer matches the current active alert snapshot."
                    : $"Could not find active alert '{alertId}'.",
                requestedAlertId = alertId,
                alertSnapshotFingerprint = currentFingerprint,
                availableAlertIds = alerts.Select(snapshot => snapshot.Id).ToList()
            };
        }

        if (string.IsNullOrWhiteSpace(target.Error) == false)
        {
            return new
            {
                success = false,
                message = $"Alert '{alertId}' cannot be activated because it failed during inspection: {target.Error}",
                exceptionType = target.ExceptionType,
                requestedAlertId = alertId,
                alert = DescribeAlert(target)
            };
        }

        var before = CaptureState();
        try
        {
            InvokeAlertClick(target.Alert);
        }
        catch (Exception ex)
        {
            var failure = UnwrapInvocationException(ex);
            var afterFailure = CaptureState();
            return new
            {
                success = false,
                message = $"Activating alert '{alertId}' failed: {failure.Message}",
                exceptionType = failure.GetType().FullName,
                requestedAlertId = alertId,
                alert = DescribeAlert(target),
                stateBefore = before.State,
                stateAfter = afterFailure.State,
                effects = DescribeEffects(before, afterFailure)
            };
        }

        var after = CaptureState();
        return new
        {
            success = true,
            requestedAlertId = alertId,
            alert = DescribeAlert(target),
            stateBefore = before.State,
            stateAfter = after.State,
            effects = DescribeEffects(before, after)
        };
    }

    private static List<Message> GetLiveMessagesInDisplayOrder()
    {
        var messages = (LiveMessagesField.GetValue(null) as IEnumerable<Message>)?
            .Where(message => message != null)
            .ToList()
            ?? [];

        messages.Reverse();
        return messages;
    }

    private static List<Letter> GetLettersInDisplayOrder()
    {
        var letters = Find.LetterStack?.LettersListForReading?
            .Where(letter => letter != null)
            .ToList()
            ?? [];

        letters.Reverse();
        return letters;
    }

    private static List<AlertSnapshot> BuildOrderedActiveAlerts()
    {
        if (Find.Alerts == null)
            return [];

        var activeAlerts = (ActiveAlertsField.GetValue(Find.Alerts) as IEnumerable<Alert>)?
            .Where(alert => alert != null)
            .Select(BuildAlertSnapshot)
            .ToList()
            ?? [];

        var ordered = activeAlerts
            .OrderByDescending(snapshot => snapshot.PrioritySortValue)
            .ThenBy(snapshot => snapshot.OriginalIndex)
            .ToList();

        for (var index = 0; index < ordered.Count; index++)
            ordered[index].DisplayOrdinal = index + 1;

        var snapshotFingerprint = CreateAlertSnapshotFingerprint(ordered);
        for (var index = 0; index < ordered.Count; index++)
        {
            ordered[index].Id = NotificationIds.CreateAlertId(
                snapshotFingerprint,
                index + 1,
                CreateAlertSignatureParts(ordered[index]));
        }

        return ordered;
    }

    private static AlertSnapshot BuildAlertSnapshot(Alert alert, int originalIndex)
    {
        var snapshot = new AlertSnapshot
        {
            Alert = alert,
            OriginalIndex = originalIndex,
            AlertType = alert.GetType().FullName ?? alert.GetType().Name
        };

        try
        {
            snapshot.PrioritySortValue = (int)alert.Priority;
            snapshot.PriorityName = alert.Priority.ToString();
            snapshot.Label = alert.Label ?? string.Empty;
            snapshot.Explanation = StringifyTaggedString(alert.GetExplanation());
            snapshot.JumpToTargetsText = alert.GetJumpToTargetsText;
            snapshot.Active = alert.Active;
            snapshot.EnabledWithActiveExpansions = alert.EnabledWithActiveExpansions;

            var report = alert.GetReport();
            snapshot.AnyCulpritValid = report.AnyCulpritValid;
            if (report.AllCulprits != null)
            {
                foreach (var culprit in report.AllCulprits)
                {
                    snapshot.Culprits.Add(culprit);
                    snapshot.CulpritTokens.Add(CreateTargetToken(culprit));
                }
            }
        }
        catch (Exception ex)
        {
            var failure = UnwrapInvocationException(ex);
            snapshot.Error = failure.Message;
            snapshot.ExceptionType = failure.GetType().FullName;
            snapshot.PriorityName = string.IsNullOrWhiteSpace(snapshot.PriorityName) ? "Unknown" : snapshot.PriorityName;
            snapshot.Label = string.IsNullOrWhiteSpace(snapshot.Label)
                ? snapshot.AlertType
                : snapshot.Label;
        }

        return snapshot;
    }

    private static object DescribeMessage(Message message)
    {
        var currentTick = Find.TickManager?.TicksGame ?? 0;
        return new
        {
            id = message.GetUniqueLoadID(),
            text = message.text,
            messageType = message.def?.defName,
            alpha = Math.Round(message.Alpha, 4),
            expired = message.Expired,
            startingTick = message.startingTick,
            startingFrame = message.startingFrame,
            ageTicks = Math.Max(currentTick - message.startingTick, 0),
            hasQuest = message.quest != null,
            lookTargets = DescribeLookTargets(message.lookTargets)
        };
    }

    private static object DescribeLetter(Letter letter)
    {
        var currentTick = Find.TickManager?.TicksGame ?? 0;
        var choiceLetter = letter as ChoiceLetter;
        var letterText = choiceLetter == null
            ? null
            : StringifyTaggedString(choiceLetter.Text);
        var choices = choiceLetter == null
            ? null
            : choiceLetter.Choices
                .Cast<DiaOption>()
                .Where(option => option != null)
                .Select((option, index) => DescribeDiaOption(option, index + 1))
                .ToList();

        return new
        {
            id = letter.GetUniqueLoadID(),
            type = letter.GetType().FullName,
            letterDef = letter.def?.defName,
            label = StringifyTaggedString(letter.Label),
            text = string.IsNullOrWhiteSpace(letterText) ? null : letterText,
            arrivalTick = letter.arrivalTick,
            ageTicks = Math.Max(currentTick - letter.arrivalTick, 0),
            canDismissWithRightClick = letter.CanDismissWithRightClick,
            shouldAutomaticallyOpenLetter = letter.ShouldAutomaticallyOpenLetter,
            relatedFaction = letter.relatedFaction?.Name,
            debugInfo = string.IsNullOrWhiteSpace(letter.debugInfo) ? null : letter.debugInfo,
            lookTargets = DescribeLookTargets(letter.lookTargets),
            choiceCount = choices?.Count ?? 0,
            choices
        };
    }

    private static object DescribeDiaOption(DiaOption option, int index)
    {
        return new
        {
            index,
            text = (DiaOptionTextField.GetValue(option) as string)?.Trim(),
            disabled = option.disabled,
            disabledReason = string.IsNullOrWhiteSpace(option.disabledReason) ? null : option.disabledReason,
            closesDialog = option.resolveTree,
            hasAction = option.action != null,
            hasLink = option.link != null || option.linkLateBind != null
        };
    }

    private static object DescribeAlert(AlertSnapshot snapshot)
    {
        return new
        {
            id = snapshot.Id,
            ordinal = snapshot.DisplayOrdinal,
            type = snapshot.AlertType,
            priority = snapshot.PriorityName,
            label = snapshot.Label,
            explanation = string.IsNullOrWhiteSpace(snapshot.Explanation) ? null : snapshot.Explanation,
            jumpToTargetsText = string.IsNullOrWhiteSpace(snapshot.JumpToTargetsText) ? null : snapshot.JumpToTargetsText,
            active = snapshot.Active,
            enabledWithActiveExpansions = snapshot.EnabledWithActiveExpansions,
            anyCulpritValid = snapshot.AnyCulpritValid,
            targetCount = snapshot.Culprits.Count,
            targetsTruncated = snapshot.Culprits.Count > MaxDetailedTargets,
            targets = snapshot.Culprits.Take(MaxDetailedTargets).Select(DescribeGlobalTarget).ToList(),
            activatable = string.IsNullOrWhiteSpace(snapshot.Error),
            error = string.IsNullOrWhiteSpace(snapshot.Error) ? null : snapshot.Error,
            exceptionType = string.IsNullOrWhiteSpace(snapshot.ExceptionType) ? null : snapshot.ExceptionType
        };
    }

    private static object DescribeLookTargets(LookTargets lookTargets)
    {
        if (lookTargets == null)
        {
            return new
            {
                isValid = false,
                any = false,
                targetCount = 0,
                targetsTruncated = false,
                primaryTarget = (object)null,
                targets = Array.Empty<object>()
            };
        }

        var targets = lookTargets.targets?
            .Where(target => target.IsValid)
            .ToList()
            ?? [];

        var primaryTarget = lookTargets.PrimaryTarget;
        return new
        {
            isValid = lookTargets.IsValid,
            any = lookTargets.Any,
            targetCount = targets.Count,
            targetsTruncated = targets.Count > MaxDetailedTargets,
            primaryTarget = primaryTarget.IsValid ? DescribeGlobalTarget(primaryTarget) : null,
            targets = targets.Take(MaxDetailedTargets).Select(DescribeGlobalTarget).ToList()
        };
    }

    private static object DescribeGlobalTarget(GlobalTargetInfo target)
    {
        if (target.Thing != null)
        {
            var thing = target.Thing;
            return new
            {
                kind = thing is Pawn ? "pawn" : "thing",
                id = thing.GetUniqueLoadID(),
                label = target.Label,
                thingType = thing.GetType().FullName,
                defName = thing.def?.defName,
                mapId = RimWorldState.GetMapId(target.Map),
                mapIndex = target.Map?.Index,
                position = target.Cell.IsValid ? new { x = target.Cell.x, z = target.Cell.z } : null
            };
        }

        if (target.WorldObject != null)
        {
            return new
            {
                kind = "world_object",
                id = target.WorldObject.GetUniqueLoadID(),
                label = target.Label,
                worldObjectType = target.WorldObject.GetType().FullName,
                tile = target.Tile.Valid ? target.Tile.ToString() : null
            };
        }

        if (target.Cell.IsValid)
        {
            return new
            {
                kind = "cell",
                label = target.Label,
                mapId = RimWorldState.GetMapId(target.Map),
                mapIndex = target.Map?.Index,
                position = new { x = target.Cell.x, z = target.Cell.z },
                tile = target.Tile.Valid ? target.Tile.ToString() : null
            };
        }

        if (target.Tile.Valid)
        {
            return new
            {
                kind = "tile",
                label = target.Label,
                tile = target.Tile.ToString()
            };
        }

        return new
        {
            kind = "target",
            label = target.Label
        };
    }

    private static bool TryResolveLetter(string letterId, out Letter letter, out string error)
    {
        letter = Find.LetterStack?.LettersListForReading?
            .FirstOrDefault(candidate => string.Equals(candidate.GetUniqueLoadID(), letterId, StringComparison.Ordinal));

        if (letter != null)
        {
            error = string.Empty;
            return true;
        }

        error = $"Could not find letter '{letterId}' in the current letter stack.";
        return false;
    }

    private static bool IsLetterStillPresent(string letterId)
    {
        return Find.LetterStack?.LettersListForReading?
            .Any(candidate => string.Equals(candidate.GetUniqueLoadID(), letterId, StringComparison.Ordinal))
            == true;
    }

    private static void InvokeAlertClick(Alert alert)
    {
        var method = FindAlertOnClickMethod(alert?.GetType())
            ?? throw new InvalidOperationException($"Could not resolve an OnClick handler for alert type '{alert?.GetType().FullName ?? "null"}'.");

        var clickEvent = new Event
        {
            type = EventType.MouseDown,
            button = 0,
            clickCount = 1
        };

        var previousEvent = Event.current;
        try
        {
            Event.current = clickEvent;
            method.Invoke(alert, Array.Empty<object>());
            Event.current?.Use();
        }
        finally
        {
            Event.current = previousEvent;
        }
    }

    private static MethodInfo FindAlertOnClickMethod(Type type)
    {
        while (type != null)
        {
            var method = type.GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (method != null)
                return method;

            type = type.BaseType;
        }

        return null;
    }

    private static string CreateAlertSnapshotFingerprint(IEnumerable<AlertSnapshot> alerts)
    {
        return NotificationIds.CreateAlertSnapshotFingerprint(alerts.Select(CreateAlertSnapshotToken));
    }

    private static string CreateAlertSnapshotToken(AlertSnapshot snapshot)
    {
        return string.Join(
            "\n",
            CreateAlertSignatureParts(snapshot)
                .Concat([snapshot.DisplayOrdinal.ToString(CultureInfo.InvariantCulture)]));
    }

    private static IEnumerable<string> CreateAlertSignatureParts(AlertSnapshot snapshot)
    {
        var targetSignature = snapshot.CulpritTokens.Count == 0
            ? snapshot.Label
            : string.Join("|", snapshot.CulpritTokens);

        return
        [
            snapshot.AlertType,
            snapshot.PriorityName,
            targetSignature ?? string.Empty
        ];
    }

    private static string CreateTargetToken(GlobalTargetInfo target)
    {
        return target.ToString();
    }

    private static StateCapture CaptureState()
    {
        var uiState = RimWorldInput.GetUiState();
        var selectionTokens = GetCurrentSelectionTokens();
        var selectionPayload = DescribeCurrentSelection();
        var cameraPayload = TryDescribeCameraPayload();

        var capture = new StateCapture
        {
            UiState = uiState,
            CameraSignature = CreateCameraSignature(),
            State = new
            {
                bridge = RimWorldState.ToolStateSnapshot(),
                uiState,
                camera = cameraPayload,
                selection = selectionPayload
            }
        };

        capture.SelectionTokens.AddRange(selectionTokens);
        return capture;
    }

    private static object DescribeEffects(StateCapture before, StateCapture after)
    {
        var openedWindowTypes = after.UiState.Windows
            .Select(window => window.Type)
            .Except(before.UiState.Windows.Select(window => window.Type), StringComparer.Ordinal)
            .ToList();

        var closedWindowTypes = before.UiState.Windows
            .Select(window => window.Type)
            .Except(after.UiState.Windows.Select(window => window.Type), StringComparer.Ordinal)
            .ToList();

        return new
        {
            windowCountDelta = after.UiState.WindowCount - before.UiState.WindowCount,
            openedWindowTypes,
            closedWindowTypes,
            selectionChanged = before.SelectionTokens.SequenceEqual(after.SelectionTokens, StringComparer.Ordinal) == false,
            cameraChanged = string.Equals(before.CameraSignature, after.CameraSignature, StringComparison.Ordinal) == false,
            topWindowChanged = string.Equals(before.UiState.TopWindowType, after.UiState.TopWindowType, StringComparison.Ordinal) == false,
            focusedWindowChanged = string.Equals(before.UiState.FocusedWindowType, after.UiState.FocusedWindowType, StringComparison.Ordinal) == false
        };
    }

    private static object DescribeCurrentSelection()
    {
        var selectedObjects = Find.Selector?.SelectedObjectsListForReading?
            .Where(objectRef => objectRef != null)
            .Cast<object>()
            .ToList()
            ?? [];

        return new
        {
            count = selectedObjects.Count,
            truncated = selectedObjects.Count > MaxDetailedSelectionObjects,
            selectedObjects = selectedObjects
                .Take(MaxDetailedSelectionObjects)
                .Select(DescribeSelectedObject)
                .ToList()
        };
    }

    private static List<string> GetCurrentSelectionTokens()
    {
        return Find.Selector?.SelectedObjectsListForReading?
            .Where(objectRef => objectRef != null)
            .Cast<object>()
            .Select(GetSelectionToken)
            .ToList()
            ?? [];
    }

    private static object DescribeSelectedObject(object selectedObject)
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

    private static object TryDescribeCameraPayload()
    {
        try
        {
            if (Current.ProgramState != ProgramState.Playing || Current.Game == null || Find.CurrentMap == null)
                return null;

            return RimWorldState.DescribeCamera();
        }
        catch
        {
            return null;
        }
    }

    private static string CreateCameraSignature()
    {
        try
        {
            if (Current.ProgramState != ProgramState.Playing || Current.Game == null || Find.CurrentMap == null)
                return string.Empty;

            var driver = Find.CameraDriver;
            var mapId = RimWorldState.GetMapId(Find.CurrentMap) ?? string.Empty;
            return string.Join(
                ":",
                mapId,
                driver.MapPosition.x.ToString("0.###", CultureInfo.InvariantCulture),
                driver.MapPosition.z.ToString("0.###", CultureInfo.InvariantCulture),
                driver.RootSize.ToString("0.###", CultureInfo.InvariantCulture));
        }
        catch
        {
            return string.Empty;
        }
    }

    private static Exception UnwrapInvocationException(Exception ex)
    {
        return ex is TargetInvocationException { InnerException: not null } invocation
            ? invocation.InnerException
            : ex;
    }

    private static string StringifyTaggedString(TaggedString taggedString)
    {
        var text = taggedString.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static FieldInfo GetRequiredField(Type type, string name, BindingFlags bindingFlags)
    {
        return type.GetField(name, bindingFlags)
            ?? throw new InvalidOperationException($"Could not resolve required field '{type.FullName}.{name}'.");
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
