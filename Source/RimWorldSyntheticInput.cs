using System;
using UnityEngine;

namespace RimBridgeServer;

/// <summary>
/// Synthetic key presses for rimworld/press_key. The KeyDown and KeyUp
/// events go through the same Root.OnGUI event injection the map click
/// tools use, so handlers in the root pass see them, including the window
/// stack's accept and cancel handling for Return and Escape. Code inside a
/// window's own contents, focused text fields included, does not.
/// </summary>
internal static class RimWorldSyntheticInput
{
    public static object PressKeyResponse(string key, string character = null, string modifiers = null, int timeoutMs = 2000)
    {
        if (!TryParseKey(key, out var keyCode, out var keyError))
            return Failure("press_key", keyError);

        var injectedCharacter = '\0';
        if (!string.IsNullOrEmpty(character))
        {
            if (character.Length != 1)
                return Failure("press_key", "character must be a single character when provided.");

            injectedCharacter = character[0];
        }

        if (!ContextMenuCapabilityModule.TryParseModifiers(modifiers, out var parsedModifiers, out var normalizedModifiers, out var modifierError))
            return Failure("press_key", modifierError);

        var result = RimBridgeMapClickInjector.DispatchKeyPress(keyCode, injectedCharacter, parsedModifiers, timeoutMs);
        return new
        {
            success = result.Success,
            command = "press_key",
            message = result.Message,
            key = keyCode.ToString(),
            character = injectedCharacter == '\0' ? null : injectedCharacter.ToString(),
            modifiers = normalizedModifiers
        };
    }

    private static bool TryParseKey(string key, out KeyCode keyCode, out string failure)
    {
        keyCode = KeyCode.None;
        failure = null;

        var trimmed = key?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            failure = "key is required. Use a UnityEngine.KeyCode name such as Return, Escape, Tab, A, or Alpha1.";
            return false;
        }

        switch (trimmed.ToLowerInvariant())
        {
            case "enter":
            case "return":
                keyCode = KeyCode.Return;
                return true;

            case "esc":
            case "escape":
                keyCode = KeyCode.Escape;
                return true;

            case "space":
                keyCode = KeyCode.Space;
                return true;

            case "tab":
                keyCode = KeyCode.Tab;
                return true;

            case "backspace":
                keyCode = KeyCode.Backspace;
                return true;

            case "del":
            case "delete":
                keyCode = KeyCode.Delete;
                return true;

            case "up":
                keyCode = KeyCode.UpArrow;
                return true;

            case "down":
                keyCode = KeyCode.DownArrow;
                return true;

            case "left":
                keyCode = KeyCode.LeftArrow;
                return true;

            case "right":
                keyCode = KeyCode.RightArrow;
                return true;
        }

        if (trimmed.Length == 1 && trimmed[0] >= '0' && trimmed[0] <= '9')
        {
            keyCode = KeyCode.Alpha0 + (trimmed[0] - '0');
            return true;
        }

        if (Enum.TryParse(trimmed, ignoreCase: true, out KeyCode parsed))
        {
            keyCode = parsed;
            return true;
        }

        failure = $"Could not resolve key '{key}' to a UnityEngine.KeyCode name.";
        return false;
    }

    private static object Failure(string command, string message)
    {
        return new
        {
            success = false,
            command,
            message
        };
    }
}
