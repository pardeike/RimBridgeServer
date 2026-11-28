using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimBridgeServer;

// Raw pawn state for assertions. The Character tab shows display strings: "-" for a disabled skill and the level with
// aptitudes, so a caller cannot tell the base level, the aptitude and the disabled state apart from it.
internal static class RimWorldPawnDetails
{
    public static object GetPawnDetailsResponse(string pawnName, string pawnId)
    {
        if (Current.Game == null)
            return new { success = false, message = "No game is currently loaded" };

        var pawn = RimWorldState.ResolveAnyPawn(pawnName, pawnId);
        return new
        {
            success = true,
            pawn = RimWorldState.DescribePawn(pawn),
            identity = DescribeIdentity(pawn),
            skills = pawn.skills?.skills?.Select(DescribeSkill).ToList(),
            job = DescribeJob(pawn.jobs?.curJob, pawn.jobs?.curJob?.GetReport(pawn)),
            jobQueue = pawn.jobs?.jobQueue?.Select(queued => DescribeJob(queued.job, report: null)).ToList(),
            needs = pawn.needs?.AllNeeds?.Select(DescribeNeed).ToList(),
            hediffs = pawn.health?.hediffSet?.hediffs?.Select(DescribeHediff).ToList(),
            traits = pawn.story?.traits?.allTraits?.Select(DescribeTrait).ToList(),
            useWorkPriorities = pawn.workSettings?.EverWork == true ? Find.PlaySettings?.useWorkPriorities : null,
            workPriorities = DescribeWorkPriorities(pawn)
        };
    }

    // Who the pawn is beyond skills and health, compact. A field is null when it does not apply, for example without
    // the DLC that adds it. Tests of genes, ideoligion roles, titles, psycasts, children and mutants read it here.
    private static object DescribeIdentity(Pawn pawn)
    {
        var genes = ModsConfig.BiotechActive ? pawn.genes : null;
        var ideo = ModsConfig.IdeologyActive ? pawn.Ideo : null;
        var role = ideo?.GetRole(pawn);
        var titles = ModsConfig.RoyaltyActive ? pawn.royalty?.AllTitlesForReading : null;
        var abilities = pawn.abilities?.AllAbilitiesForReading;
        var overseer = ModsConfig.BiotechActive && pawn.RaceProps?.IsMechanoid == true ? pawn.GetOverseer() : null;

        return new
        {
            role = RimWorldPawns.RoleOf(pawn),
            factionLeader = pawn.Faction != null && pawn.Faction.leader == pawn ? true : (bool?)null,
            gender = pawn.gender.ToString(),
            ageBiologicalYears = pawn.ageTracker?.AgeBiologicalYears,
            ageChronologicalYears = pawn.ageTracker?.AgeChronologicalYears,
            lifeStage = pawn.ageTracker?.CurLifeStage?.defName,
            developmentalStage = pawn.RaceProps?.Humanlike == true ? pawn.DevelopmentalStage.ToString() : null,
            guestStatus = pawn.guest?.GuestStatus is GuestStatus status && (pawn.IsPrisoner || pawn.IsSlave) ? status.ToString() : null,
            xenotype = genes?.Xenotype?.defName,
            xenotypeLabel = genes == null ? null : genes.XenotypeLabel,
            genes = genes?.GenesListForReading?
                .Where(gene => gene?.def != null)
                .Select(gene => new
                {
                    defName = gene.def.defName,
                    xenogene = genes.IsXenogene(gene),
                    active = gene.Active
                })
                .ToList(),
            ideo = ideo?.name,
            ideoCertainty = ideo == null || pawn.ideo == null ? null : (float?)System.Math.Round(pawn.ideo.Certainty, 3),
            ideoRole = role?.def?.defName,
            ideoRoleLabel = role?.LabelCap.ToString(),
            royalTitles = titles == null || titles.Count == 0
                ? null
                : titles.Select(title => new { title = title.def?.defName, faction = title.faction?.Name }).ToList(),
            psylinkLevel = ModsConfig.RoyaltyActive && pawn.RaceProps?.Humanlike == true ? pawn.GetPsylinkLevel() : (int?)null,
            abilities = abilities == null || abilities.Count == 0 ? null : abilities.Select(ability => ability.def?.defName).ToList(),
            mutant = ModsConfig.AnomalyActive ? pawn.mutant?.Def?.defName : null,
            overseerPawnId = overseer == null ? null : RimWorldState.GetThingId(overseer)
        };
    }

    private static object DescribeTrait(Trait trait)
    {
        return new
        {
            defName = trait.def?.defName,
            label = trait.LabelCap,
            degree = trait.Degree,
            suppressed = trait.Suppressed
        };
    }

    // The Work tab shows the visible work types in this order. For a humanlike pawn, GetPriority reports 3 for every
    // active work type when manual priorities are off, which is what the pawn works with. Other pawns, such as worker
    // mechs, report the stored value.
    private static List<object> DescribeWorkPriorities(Pawn pawn)
    {
        var settings = pawn.workSettings;
        if (settings?.EverWork != true)
            return null;

        // The game's permanent list. skills[].permanentlyDisabled adds the pawn's disabled work tags to it.
        var permanentlyDisabled = pawn.GetDisabledWorkTypes(permanentOnly: true);
        return WorkTypeDefsUtility.WorkTypeDefsInPriorityOrder
            .Select(workType => (object)new
            {
                defName = workType.defName,
                label = workType.labelShort,
                priority = settings.GetPriority(workType),
                disabled = pawn.WorkTypeIsDisabled(workType),
                permanentlyDisabled = permanentlyDisabled.Contains(workType),
                // The Work tab column reads VisibleCurrently, which also covers visibleOnlyWithChildrenInColony.
                visible = workType.VisibleCurrently
            })
            .ToList();
    }

    private static object DescribeSkill(SkillRecord skill)
    {
        return new
        {
            defName = skill.def?.defName,
            label = skill.def?.LabelCap.ToString(),
            level = skill.GetLevel(includeAptitudes: true),
            levelInt = skill.levelInt,
            aptitude = skill.Aptitude,
            totallyDisabled = skill.TotallyDisabled,
            permanentlyDisabled = skill.PermanentlyDisabled,
            passion = skill.passion.ToString(),
            xpSinceLastLevel = skill.xpSinceLastLevel,
            xpRequiredForLevelUp = skill.XpRequiredForLevelUp,
            xpSinceMidnight = skill.xpSinceMidnight
        };
    }

    private static object DescribeJob(Job job, string report)
    {
        if (job == null)
            return null;

        return new
        {
            defName = job.def?.defName,
            report,
            playerForced = job.playerForced,
            targetA = DescribeTarget(job.targetA),
            targetB = DescribeTarget(job.targetB),
            targetC = DescribeTarget(job.targetC)
        };
    }

    private static object DescribeTarget(LocalTargetInfo target)
    {
        if (target.IsValid == false)
            return null;

        var thing = target.Thing;
        var cell = thing?.PositionHeld ?? target.Cell;
        return new
        {
            thingId = RimWorldState.GetThingId(thing),
            defName = thing?.def?.defName,
            label = thing?.LabelCap.ToString(),
            cell = cell.IsValid ? new { x = cell.x, z = cell.z } : null
        };
    }

    private static object DescribeNeed(Need need)
    {
        return new
        {
            defName = need.def?.defName,
            label = need.LabelCap.ToString(),
            curLevel = need.CurLevel,
            curLevelPercentage = need.CurLevelPercentage,
            maxLevel = need.MaxLevel
        };
    }

    private static object DescribeHediff(Hediff hediff)
    {
        return new
        {
            defName = hediff.def?.defName,
            label = hediff.LabelCap,
            severity = hediff.Severity,
            part = hediff.Part?.def?.defName,
            partLabel = hediff.Part?.LabelCap,
            visible = hediff.Visible,
            permanent = hediff.IsPermanent(),
            tended = hediff.IsTended()
        };
    }
}
