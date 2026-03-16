using System.Collections.Generic;
using System.Threading.Tasks;
using RimBridgeServer.Contracts;
using RimBridgeServer.Core;
using RimBridgeServer.Extensions.Abstractions;
using Xunit;

namespace RimBridgeServer.Core.Tests;

public class CapabilityScriptRunnerTests
{
    [Fact]
    public void ExecutesStepsInOrderAndCollectsResults()
    {
        var registry = new CapabilityRegistry();
        registry.RegisterProvider(new ScriptTestProvider());
        var runner = new CapabilityScriptRunner(registry);

        var report = runner.Execute(new CapabilityScriptDefinition
        {
            Name = "success-path",
            Steps =
            [
                new CapabilityScriptStep
                {
                    Id = "ping",
                    Call = "rimbridge/ping"
                },
                new CapabilityScriptStep
                {
                    Id = "sum",
                    Call = "test/add",
                    Arguments = new Dictionary<string, object>
                    {
                        ["left"] = 2,
                        ["right"] = 3
                    }
                }
            ]
        });

        Assert.True(report.Success);
        Assert.False(report.Halted);
        Assert.Equal(2, report.StepCount);
        Assert.Equal(2, report.ExecutedStepCount);
        Assert.Equal(2, report.SucceededStepCount);
        Assert.Equal(0, report.FailedStepCount);
        Assert.Collection(report.Steps,
            first =>
            {
                Assert.Equal("ping", first.Id);
                Assert.True(first.Success);
                Assert.Equal("rimbridge.core/diagnostics/ping", first.CapabilityId);
                Assert.NotNull(first.Result);
            },
            second =>
            {
                Assert.Equal("sum", second.Id);
                Assert.True(second.Success);
                Assert.Equal("test.math/add", second.CapabilityId);
                var property = second.Result?.GetType().GetProperty("sum");
                Assert.NotNull(property);
                Assert.Equal(5, property!.GetValue(second.Result));
            });
    }

    [Fact]
    public void HaltsOnFirstFailureByDefault()
    {
        var registry = new CapabilityRegistry();
        registry.RegisterProvider(new ScriptTestProvider());
        var runner = new CapabilityScriptRunner(registry);

        var report = runner.Execute(new CapabilityScriptDefinition
        {
            Steps =
            [
                new CapabilityScriptStep
                {
                    Id = "fail",
                    Call = "test/fail"
                },
                new CapabilityScriptStep
                {
                    Id = "later",
                    Call = "rimbridge/ping"
                }
            ]
        });

        Assert.False(report.Success);
        Assert.True(report.Halted);
        Assert.Equal(2, report.StepCount);
        Assert.Equal(1, report.ExecutedStepCount);
        Assert.Equal(1, report.FailedStepCount);
        Assert.Single(report.Steps);
        Assert.Equal("fail", report.Steps[0].Id);
    }

    [Fact]
    public void CanContinueAfterFailures()
    {
        var registry = new CapabilityRegistry();
        registry.RegisterProvider(new ScriptTestProvider());
        var runner = new CapabilityScriptRunner(registry);

        var report = runner.Execute(new CapabilityScriptDefinition
        {
            ContinueOnError = true,
            Steps =
            [
                new CapabilityScriptStep
                {
                    Id = "fail",
                    Call = "test/fail"
                },
                new CapabilityScriptStep
                {
                    Id = "ping",
                    Call = "rimbridge/ping"
                }
            ]
        }, includeStepResults: false);

        Assert.False(report.Success);
        Assert.False(report.Halted);
        Assert.Equal(2, report.ExecutedStepCount);
        Assert.Equal(1, report.SucceededStepCount);
        Assert.Equal(1, report.FailedStepCount);
        Assert.Null(report.Steps[0].Result);
        Assert.Null(report.Steps[1].Result);
        Assert.True(report.Steps[1].Success);
    }

    [Fact]
    public void RejectsNestedRunScriptSteps()
    {
        var registry = new CapabilityRegistry();
        registry.RegisterProvider(new ScriptTestProvider());
        var runner = new CapabilityScriptRunner(registry);

        var report = runner.Execute(new CapabilityScriptDefinition
        {
            Steps =
            [
                new CapabilityScriptStep
                {
                    Call = "rimbridge/run_script"
                }
            ]
        });

        Assert.False(report.Success);
        Assert.True(report.Halted);
        Assert.Equal("script.invalid_step", report.Steps[0].Error.Code);
    }

    private sealed class ScriptTestProvider : IRimBridgeCapabilityProvider
    {
        public string ProviderId => "script.test";

        public IEnumerable<RimBridgeCapabilityRegistration> GetCapabilities()
        {
            yield return new RimBridgeCapabilityRegistration(
                new CapabilityDescriptor
                {
                    Id = "rimbridge.core/diagnostics/ping",
                    ProviderId = ProviderId,
                    Category = "diagnostics",
                    Aliases = ["rimbridge/ping"]
                },
                (_, _) => Task.FromResult(OperationEnvelope.Completed("op_ping", "rimbridge.core/diagnostics/ping", System.DateTimeOffset.UtcNow, new { message = "pong" })));

            yield return new RimBridgeCapabilityRegistration(
                new CapabilityDescriptor
                {
                    Id = "test.math/add",
                    ProviderId = ProviderId,
                    Category = "test",
                    Aliases = ["test/add"]
                },
                (invocation, _) =>
                {
                    var left = System.Convert.ToInt32(invocation.Arguments["left"], System.Globalization.CultureInfo.InvariantCulture);
                    var right = System.Convert.ToInt32(invocation.Arguments["right"], System.Globalization.CultureInfo.InvariantCulture);
                    return Task.FromResult(OperationEnvelope.Completed("op_add", "test.math/add", System.DateTimeOffset.UtcNow, new { sum = left + right }));
                });

            yield return new RimBridgeCapabilityRegistration(
                new CapabilityDescriptor
                {
                    Id = "test.failure/fail",
                    ProviderId = ProviderId,
                    Category = "test",
                    Aliases = ["test/fail"]
                },
                (_, _) => Task.FromResult(OperationEnvelope.Failed("op_fail", "test.failure/fail", System.DateTimeOffset.UtcNow, new OperationError
                {
                    Code = "test.failed",
                    Message = "boom"
                })));
        }
    }
}
