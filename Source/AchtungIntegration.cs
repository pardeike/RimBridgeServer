using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimBridgeServer;

internal static class AchtungIntegration
{
    private static readonly FieldInfo FloatMenuOptionsField = AccessTools.Field(typeof(FloatMenu), "options");

    private static Assembly _assembly;
    private static Type _achtungType;
    private static Type _settingsType;
    private static Type _toolsType;
    private static Type _multiActionsType;
    private static FieldInfo _settingsField;
    private static FieldInfo _showDraftedOrdersWhenUndraftedField;
    private static MethodInfo _getSelectedColonists;
    private static MethodInfo _countMethod;
    private static MethodInfo _getWindowMethod;

    public static bool IsLoaded()
    {
        return ResolveAssembly() != null && ResolveTypes();
    }

    public static (FloatMenu menu, List<FloatMenuOption> options) BuildMenu(Vector3 clickPos)
    {
        if (!IsLoaded())
            throw new InvalidOperationException("Achtung is not loaded, so an Achtung-specific context menu cannot be built.");

        var colonists = _getSelectedColonists.Invoke(null, null);
        if (colonists is not ICollection collection || collection.Count == 0)
            throw new InvalidOperationException("Achtung could not build a menu because no valid selected colonists were found.");

        var multiActions = Activator.CreateInstance(_multiActionsType, colonists, clickPos);
        var actionCount = (int)_countMethod.Invoke(multiActions, [false]);
        if (actionCount == 0)
            return (null, []);

        var window = _getWindowMethod.Invoke(multiActions, null) as FloatMenu;
        if (window == null)
            throw new InvalidOperationException("Achtung returned a null float menu window.");

        var options = ((IEnumerable)FloatMenuOptionsField.GetValue(window))
            .Cast<FloatMenuOption>()
            .ToList();

        return (window, options);
    }

    public static object DescribeState()
    {
        if (!IsLoaded())
        {
            return new
            {
                loaded = false,
                showDraftedOrdersWhenUndrafted = false
            };
        }

        return new
        {
            loaded = true,
            showDraftedOrdersWhenUndrafted = GetShowDraftedOrdersWhenUndrafted()
        };
    }

    public static bool GetShowDraftedOrdersWhenUndrafted()
    {
        var settings = GetSettingsInstance();
        return settings != null && (bool)_showDraftedOrdersWhenUndraftedField.GetValue(settings);
    }

    public static bool SetShowDraftedOrdersWhenUndrafted(bool enabled)
    {
        var settings = GetSettingsInstance();
        if (settings == null)
            throw new InvalidOperationException("Achtung settings are not available.");

        _showDraftedOrdersWhenUndraftedField.SetValue(settings, enabled);
        return (bool)_showDraftedOrdersWhenUndraftedField.GetValue(settings);
    }

    private static Assembly ResolveAssembly()
    {
        if (_assembly != null)
            return _assembly;

        _assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, "Achtung", StringComparison.OrdinalIgnoreCase));

        return _assembly;
    }

    private static bool ResolveTypes()
    {
        if (_achtungType != null
            && _settingsType != null
            && _toolsType != null
            && _multiActionsType != null
            && _settingsField != null
            && _showDraftedOrdersWhenUndraftedField != null
            && _getSelectedColonists != null
            && _countMethod != null
            && _getWindowMethod != null)
            return true;

        var assembly = ResolveAssembly();
        if (assembly == null)
            return false;

        _achtungType = assembly.GetType("AchtungMod.Achtung");
        _settingsType = assembly.GetType("AchtungMod.AchtungSettings");
        _toolsType = assembly.GetType("AchtungMod.Tools");
        _multiActionsType = assembly.GetType("AchtungMod.MultiActions");
        _settingsField = _achtungType != null ? AccessTools.Field(_achtungType, "Settings") : null;
        _showDraftedOrdersWhenUndraftedField = _settingsType != null ? AccessTools.Field(_settingsType, "showDraftedOrdersWhenUndrafted") : null;
        _getSelectedColonists = _toolsType != null ? AccessTools.Method(_toolsType, "GetSelectedColonists") : null;
        _countMethod = _multiActionsType != null ? AccessTools.Method(_multiActionsType, "Count") : null;
        _getWindowMethod = _multiActionsType != null ? AccessTools.Method(_multiActionsType, "GetWindow") : null;

        return _achtungType != null
            && _settingsType != null
            && _toolsType != null
            && _multiActionsType != null
            && _settingsField != null
            && _showDraftedOrdersWhenUndraftedField != null
            && _getSelectedColonists != null
            && _countMethod != null
            && _getWindowMethod != null;
    }

    private static object GetSettingsInstance()
    {
        if (!IsLoaded())
            throw new InvalidOperationException("Achtung is not loaded.");

        return _settingsField.GetValue(null);
    }
}
