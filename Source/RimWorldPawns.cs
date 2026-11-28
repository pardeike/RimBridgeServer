using System;
using System.Linq;
using RimWorld;
using Verse;

namespace RimBridgeServer;

internal static class RimWorldPawns
{
    private static readonly string[] Roles = ["colonist", "prisoner", "slave", "animal", "mech", "hostile", "other"];

    public static object ListPawnsResponse(string role, int offset)
    {
        if (Current.Game == null)
            return new { success = false, message = "No game is currently loaded" };

        var map = RimWorldState.CurrentMapOrThrow();
        var filter = string.IsNullOrWhiteSpace(role) ? null : role.Trim().ToLowerInvariant();
        if (filter != null && Roles.Contains(filter) == false)
            return new { success = false, message = $"Unknown role '{role}'. Use colonist, prisoner, slave, animal, mech, hostile or other." };

        var pawns = RimWorldState.AllCurrentMapPawns(map)
            .Select(pawn => (Pawn: pawn, Role: RoleOf(pawn)))
            .Where(entry => filter == null || entry.Role == filter)
            .OrderBy(entry => Array.IndexOf(Roles, entry.Role))
            .ThenBy(entry => NameOf(entry.Pawn), StringComparer.Ordinal)
            .ThenBy(entry => RimWorldState.GetThingId(entry.Pawn), StringComparer.Ordinal)
            .ToList();
        var rows = ResponseBudget.TakePage(pawns, offset, entry => DescribePawnRow(entry.Pawn, entry.Role), out var page);

        return new
        {
            success = true,
            mapId = RimWorldState.GetMapId(map),
            pawns = rows,
            page
        };
    }

    // A secure slave is a free colonist to the game, so slave comes before colonist. A hostile pawn is hostile before
    // it is an animal or a mech, so a manhunter is hostile and a colony mech is mech.
    public static string RoleOf(Pawn pawn)
    {
        if (pawn.IsPrisonerOfColony)
            return "prisoner";
        if (pawn.IsSlaveOfColony)
            return "slave";
        if (pawn.IsFreeColonist)
            return "colonist";
        if (Faction.OfPlayer != null && pawn.HostileTo(Faction.OfPlayer))
            return "hostile";
        if (pawn.RaceProps?.IsMechanoid == true)
            return "mech";
        if (pawn.RaceProps?.Animal == true)
            return "animal";
        return "other";
    }

    private static string NameOf(Pawn pawn)
    {
        return pawn.Name?.ToStringShort ?? pawn.LabelShort;
    }

    private static object DescribePawnRow(Pawn pawn, string role)
    {
        return new
        {
            pawnId = RimWorldState.GetThingId(pawn),
            name = NameOf(pawn),
            kindDef = pawn.kindDef?.defName,
            role,
            faction = pawn.Faction?.Name,
            factionIsPlayer = pawn.Faction?.IsPlayer ?? false,
            position = pawn.Position.IsValid ? new { x = pawn.Position.x, z = pawn.Position.z } : null,
            job = pawn.CurJob?.def?.defName,
            drafted = pawn.drafter?.Drafted ?? false,
            downed = pawn.Downed,
            mentalState = pawn.MentalStateDef?.defName
        };
    }
}
