using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimBridgeServer;

internal static class RimBridgeVirtualPointer
{
    private sealed class PointerState
    {
        public string Kind { get; set; } = string.Empty;

        public string TargetId { get; set; } = string.Empty;

        public string Label { get; set; } = string.Empty;

        public Vector2 ScreenPosition { get; set; }

        public Vector2 ScreenPositionInverted { get; set; }

        public object Details { get; set; }
    }

    private sealed class TransientOverride
    {
        public int Token { get; set; }

        public Vector2 ScreenPosition { get; set; }

        public Vector2 ScreenPositionInverted { get; set; }

        public bool LeftMouseButton { get; set; }

        public bool LeftMouseButtonDown { get; set; }

        public bool LeftMouseButtonUp { get; set; }
    }

    private static readonly object Sync = new();
    private static readonly List<TransientOverride> TransientOverrides = [];
    private static PointerState _persistentPointer;
    private static int _nextToken = 1;

    public static int PushTransientOverride(Vector2 screenPositionInverted, bool leftMouseButton = false, bool leftMouseButtonDown = false, bool leftMouseButtonUp = false)
    {
        lock (Sync)
        {
            var token = _nextToken++;
            TransientOverrides.Add(new TransientOverride
            {
                Token = token,
                ScreenPosition = InvertedToBottomLeft(screenPositionInverted),
                ScreenPositionInverted = screenPositionInverted,
                LeftMouseButton = leftMouseButton,
                LeftMouseButtonDown = leftMouseButtonDown,
                LeftMouseButtonUp = leftMouseButtonUp
            });
            return token;
        }
    }

    public static void PopTransientOverride(int token)
    {
        if (token == 0)
            return;

        lock (Sync)
        {
            var index = TransientOverrides.FindLastIndex(entry => entry.Token == token);
            if (index >= 0)
                TransientOverrides.RemoveAt(index);
        }
    }

    public static void SetPersistentPointer(string kind, string targetId, string label, Vector2 screenPositionInverted, object details)
    {
        lock (Sync)
        {
            _persistentPointer = new PointerState
            {
                Kind = kind ?? string.Empty,
                TargetId = targetId ?? string.Empty,
                Label = label ?? string.Empty,
                ScreenPosition = InvertedToBottomLeft(screenPositionInverted),
                ScreenPositionInverted = screenPositionInverted,
                Details = details
            };
        }
    }

    public static void UpdatePersistentPointerPosition(Vector2 screenPositionInverted)
    {
        lock (Sync)
        {
            if (_persistentPointer == null)
                return;

            _persistentPointer.ScreenPositionInverted = screenPositionInverted;
            _persistentPointer.ScreenPosition = InvertedToBottomLeft(screenPositionInverted);
        }
    }

    public static void ClearPersistentPointer()
    {
        lock (Sync)
        {
            _persistentPointer = null;
        }
    }

    public static object DescribePersistentPointer()
    {
        lock (Sync)
        {
            if (_persistentPointer == null)
                return null;

            return new
            {
                kind = _persistentPointer.Kind,
                targetId = string.IsNullOrWhiteSpace(_persistentPointer.TargetId) ? null : _persistentPointer.TargetId,
                label = string.IsNullOrWhiteSpace(_persistentPointer.Label) ? null : _persistentPointer.Label,
                screenPosition = new
                {
                    x = _persistentPointer.ScreenPositionInverted.x,
                    y = _persistentPointer.ScreenPositionInverted.y
                },
                details = _persistentPointer.Details
            };
        }
    }

    public static bool TryGetMousePositionOnUi(out Vector2 position)
    {
        lock (Sync)
        {
            if (TryGetTransientOverride(out var transient))
            {
                position = transient.ScreenPosition;
                return true;
            }

            if (_persistentPointer != null)
            {
                position = _persistentPointer.ScreenPosition;
                return true;
            }
        }

        position = default;
        return false;
    }

    public static bool TryGetMousePositionOnUiInverted(out Vector2 position)
    {
        lock (Sync)
        {
            if (TryGetTransientOverride(out var transient))
            {
                position = transient.ScreenPositionInverted;
                return true;
            }

            if (_persistentPointer != null)
            {
                position = _persistentPointer.ScreenPositionInverted;
                return true;
            }
        }

        position = default;
        return false;
    }

    public static bool TryGetInputMousePosition(out Vector3 position)
    {
        if (!TryGetMousePositionOnUi(out var uiPosition))
        {
            position = default;
            return false;
        }

        position = new Vector3(uiPosition.x * Prefs.UIScale, uiPosition.y * Prefs.UIScale, 0f);
        return true;
    }

    private static bool TryGetTransientOverride(out TransientOverride transient)
    {
        transient = null;
        if (TransientOverrides.Count == 0)
            return false;

        transient = TransientOverrides[TransientOverrides.Count - 1];
        return transient != null;
    }

    private static Vector2 InvertedToBottomLeft(Vector2 screenPositionInverted)
    {
        var height = UI.screenHeight > 0 ? UI.screenHeight : Mathf.RoundToInt((float)Screen.height / Prefs.UIScale);
        return new Vector2(screenPositionInverted.x, height - screenPositionInverted.y);
    }
}

[HarmonyPatch(typeof(UI), nameof(UI.MousePositionOnUI), MethodType.Getter)]
internal static class UI_MousePositionOnUI_VirtualPointer_Patch
{
    [HarmonyPriority(Priority.First)]
    public static bool Prefix(ref Vector2 __result)
    {
        if (!RimBridgeVirtualPointer.TryGetMousePositionOnUi(out var position))
            return true;

        __result = position;
        return false;
    }
}

[HarmonyPatch(typeof(UI), nameof(UI.MousePosUIInvertedUseEventIfCan), MethodType.Getter)]
internal static class UI_MousePosUIInvertedUseEventIfCan_VirtualPointer_Patch
{
    [HarmonyPriority(Priority.First)]
    public static bool Prefix(ref Vector2 __result)
    {
        if (!RimBridgeVirtualPointer.TryGetMousePositionOnUiInverted(out var position))
            return true;

        __result = position;
        return false;
    }
}

[HarmonyPatch(typeof(Input), nameof(Input.mousePosition), MethodType.Getter)]
internal static class Input_MousePosition_VirtualPointer_Patch
{
    [HarmonyPriority(Priority.First)]
    public static bool Prefix(ref Vector3 __result)
    {
        if (!RimBridgeVirtualPointer.TryGetInputMousePosition(out var position))
            return true;

        __result = position;
        return false;
    }
}
