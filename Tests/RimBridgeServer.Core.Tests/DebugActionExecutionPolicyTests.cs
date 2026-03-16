using RimBridgeServer.Core;
using Xunit;

namespace RimBridgeServer.Core.Tests;

public class DebugActionExecutionPolicyTests
{
    [Fact]
    public void MarksSubmenusAsBrowseOnly()
    {
        var assessment = DebugActionExecutionPolicy.Evaluate(
            hasChildren: true,
            actionType: "Action",
            hasAction: true,
            hasPawnAction: false);

        Assert.Equal(DebugActionExecutionKind.BrowseOnly, assessment.Kind);
        Assert.False(assessment.Supported);
        Assert.Contains("submenu", assessment.Reason);
    }

    [Fact]
    public void MarksSimpleActionLeavesAsDirect()
    {
        var assessment = DebugActionExecutionPolicy.Evaluate(
            hasChildren: false,
            actionType: "Action",
            hasAction: true,
            hasPawnAction: false);

        Assert.Equal(DebugActionExecutionKind.Direct, assessment.Kind);
        Assert.True(assessment.Supported);
        Assert.True(string.IsNullOrEmpty(assessment.Reason));
    }

    [Fact]
    public void MarksPawnActionsAsUnsupportedPawnTargets()
    {
        var assessment = DebugActionExecutionPolicy.Evaluate(
            hasChildren: false,
            actionType: "Action",
            hasAction: false,
            hasPawnAction: true);

        Assert.Equal(DebugActionExecutionKind.PawnTarget, assessment.Kind);
        Assert.False(assessment.Supported);
        Assert.Contains("pawn target", assessment.Reason);
    }

    [Fact]
    public void MarksToolMapActionsAsUnsupportedMapTargets()
    {
        var assessment = DebugActionExecutionPolicy.Evaluate(
            hasChildren: false,
            actionType: "ToolMap",
            hasAction: false,
            hasPawnAction: false);

        Assert.Equal(DebugActionExecutionKind.MapTarget, assessment.Kind);
        Assert.False(assessment.Supported);
        Assert.Contains("map target", assessment.Reason);
    }
}
