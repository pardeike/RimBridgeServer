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
            skills = pawn.skills?.skills?.Select(DescribeSkill).ToList(),
            job = DescribeJob(pawn.jobs?.curJob, pawn.jobs?.curJob?.GetReport(pawn)),
            jobQueue = pawn.jobs?.jobQueue?.Select(queued => DescribeJob(queued.job, report: null)).ToList(),
            needs = pawn.needs?.AllNeeds?.Select(DescribeNeed).ToList(),
            hediffs = pawn.health?.hediffSet?.hediffs?.Select(DescribeHediff).ToList()
        };
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
