using System.Collections.Generic;
using System.Linq;
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

    [Fact]
    public void CanReferencePreviousStepResults()
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
                    Id = "first",
                    Call = "test/add",
                    Arguments = new Dictionary<string, object>
                    {
                        ["left"] = 2,
                        ["right"] = 3
                    }
                },
                new CapabilityScriptStep
                {
                    Id = "second",
                    Call = "test/add",
                    Arguments = new Dictionary<string, object>
                    {
                        ["left"] = new Dictionary<string, object>
                        {
                            ["$ref"] = "first",
                            ["path"] = "result.sum"
                        },
                        ["right"] = 4
                    }
                }
            ]
        });

        Assert.True(report.Success);
        var property = report.Steps[1].Result?.GetType().GetProperty("sum");
        Assert.NotNull(property);
        Assert.Equal(9, property!.GetValue(report.Steps[1].Result));
    }

    [Fact]
    public void ReferencesUseInternalResultsEvenWhenProjectedResultsAreSuppressed()
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
                    Id = "first",
                    Call = "test/add",
                    Arguments = new Dictionary<string, object>
                    {
                        ["left"] = 2,
                        ["right"] = 3
                    }
                },
                new CapabilityScriptStep
                {
                    Id = "second",
                    Call = "test/add",
                    Arguments = new Dictionary<string, object>
                    {
                        ["left"] = new Dictionary<string, object>
                        {
                            ["$ref"] = "first",
                            ["path"] = "result.sum"
                        },
                        ["right"] = 4
                    }
                }
            ]
        }, includeStepResults: false);

        Assert.True(report.Success);
        Assert.Null(report.Steps[0].Result);
        Assert.Null(report.Steps[1].Result);
    }

    [Fact]
    public void FailsWhenReferenceCannotBeResolved()
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
                    Id = "first",
                    Call = "test/add",
                    Arguments = new Dictionary<string, object>
                    {
                        ["left"] = 2,
                        ["right"] = 3
                    }
                },
                new CapabilityScriptStep
                {
                    Id = "second",
                    Call = "test/add",
                    Arguments = new Dictionary<string, object>
                    {
                        ["left"] = new Dictionary<string, object>
                        {
                            ["$ref"] = "first",
                            ["path"] = "result.missing"
                        },
                        ["right"] = 4
                    }
                }
            ]
        });

        Assert.False(report.Success);
        Assert.True(report.Halted);
        Assert.Equal("script.invalid_reference", report.Steps[1].Error.Code);
    }

    [Fact]
    public void CanContinueUntilNumericConditionIsSatisfied()
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
                    Id = "poll",
                    Call = "test/counter",
                    ContinueUntil = new CapabilityScriptContinuePolicy
                    {
                        TimeoutMs = 1000,
                        PollIntervalMs = 0,
                        Condition = new Dictionary<string, object>
                        {
                            ["path"] = "result.value",
                            ["greaterThanOrEqual"] = 3
                        }
                    }
                }
            ]
        });

        Assert.True(report.Success);
        Assert.Equal(3, report.Steps[0].Attempts);
        var property = report.Steps[0].Result?.GetType().GetProperty("value");
        Assert.NotNull(property);
        Assert.Equal(3, property!.GetValue(report.Steps[0].Result));
    }

    [Fact]
    public void CanContinueUntilCollectionConditionIsSatisfied()
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
                    Id = "grouped",
                    Call = "test/pawn_snapshot",
                    ContinueUntil = new CapabilityScriptContinuePolicy
                    {
                        TimeoutMs = 1000,
                        PollIntervalMs = 0,
                        Condition = new Dictionary<string, object>
                        {
                            ["all"] = new List<object>
                            {
                                new Dictionary<string, object>
                                {
                                    ["path"] = "result.colonists",
                                    ["countEquals"] = 3
                                },
                                new Dictionary<string, object>
                                {
                                    ["path"] = "result.colonists",
                                    ["allItems"] = new Dictionary<string, object>
                                    {
                                        ["path"] = "position.x",
                                        ["greaterThanOrEqual"] = 143,
                                        ["lessThanOrEqual"] = 144
                                    }
                                },
                                new Dictionary<string, object>
                                {
                                    ["path"] = "result.colonists",
                                    ["allItems"] = new Dictionary<string, object>
                                    {
                                        ["path"] = "job",
                                        ["in"] = new List<object> { "Wait_Combat", "Wait_MaintainPosture" }
                                    }
                                }
                            }
                        }
                    }
                }
            ]
        });

        Assert.True(report.Success);
        Assert.Equal(2, report.Steps[0].Attempts);
    }

    [Fact]
    public void ReferenceRootsExposePollingAttemptCount()
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
                    Id = "poll",
                    Call = "test/counter",
                    ContinueUntil = new CapabilityScriptContinuePolicy
                    {
                        TimeoutMs = 1000,
                        PollIntervalMs = 0,
                        Condition = new Dictionary<string, object>
                        {
                            ["path"] = "result.value",
                            ["greaterThanOrEqual"] = 3
                        }
                    }
                },
                new CapabilityScriptStep
                {
                    Id = "echo_attempts",
                    Call = "test/echo",
                    Arguments = new Dictionary<string, object>
                    {
                        ["attempts"] = new Dictionary<string, object>
                        {
                            ["$ref"] = "poll",
                            ["path"] = "attempts"
                        }
                    }
                }
            ]
        });

        Assert.True(report.Success);
        var result = Assert.IsType<Dictionary<string, object>>(report.Steps[1].Result);
        Assert.Equal(3, result["attempts"]);
    }

    [Fact]
    public void FailsWhenContinueConditionTimesOut()
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
                    Id = "poll",
                    Call = "test/counter",
                    ContinueUntil = new CapabilityScriptContinuePolicy
                    {
                        TimeoutMs = 0,
                        PollIntervalMs = 0,
                        Condition = new Dictionary<string, object>
                        {
                            ["path"] = "result.value",
                            ["greaterThanOrEqual"] = 2
                        }
                    }
                }
            ]
        });

        Assert.False(report.Success);
        Assert.True(report.Halted);
        Assert.Equal("script.continue_timeout", report.Steps[0].Error.Code);
        Assert.Equal(1, report.Steps[0].Attempts);
    }

    [Fact]
    public void CanDeclareVariablesAndBranchWithIf()
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
                    Type = "let",
                    Name = "base",
                    Value = 7
                },
                new CapabilityScriptStep
                {
                    Type = "if",
                    Condition = new Dictionary<string, object>
                    {
                        ["path"] = "vars.base",
                        ["equals"] = 7
                    },
                    Body =
                    [
                        new CapabilityScriptStep
                        {
                            Id = "branch",
                            Call = "test/echo",
                            Arguments = new Dictionary<string, object>
                            {
                                ["tag"] = "then",
                                ["value"] = new Dictionary<string, object>
                                {
                                    ["$var"] = "base"
                                }
                            }
                        }
                    ],
                    ElseBody =
                    [
                        new CapabilityScriptStep
                        {
                            Id = "branch",
                            Call = "test/echo",
                            Arguments = new Dictionary<string, object>
                            {
                                ["tag"] = "else",
                                ["value"] = 0
                            }
                        }
                    ]
                }
            ]
        });

        Assert.True(report.Success);
        Assert.Single(report.Steps);
        Assert.Equal("branch", report.Steps[0].Id);
        var result = Assert.IsType<Dictionary<string, object>>(report.Steps[0].Result);
        Assert.Equal("then", result["tag"]);
        Assert.Equal(7, result["value"]);
    }

    [Fact]
    public void CanIterateCollectionsAndReferenceLatestLoopCall()
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
                    Type = "let",
                    Name = "values",
                    Value = new List<object> { 2, 4, 6 }
                },
                new CapabilityScriptStep
                {
                    Type = "foreach",
                    ItemName = "item",
                    IndexName = "index",
                    Collection = new Dictionary<string, object>
                    {
                        ["$var"] = "values"
                    },
                    Body =
                    [
                        new CapabilityScriptStep
                        {
                            Id = "echo",
                            Call = "test/echo",
                            Arguments = new Dictionary<string, object>
                            {
                                ["value"] = new Dictionary<string, object>
                                {
                                    ["$var"] = "item"
                                },
                                ["index"] = new Dictionary<string, object>
                                {
                                    ["$var"] = "index"
                                }
                            }
                        }
                    ]
                },
                new CapabilityScriptStep
                {
                    Id = "sum",
                    Call = "test/add",
                    Arguments = new Dictionary<string, object>
                    {
                        ["left"] = new Dictionary<string, object>
                        {
                            ["$ref"] = "echo",
                            ["path"] = "result.value"
                        },
                        ["right"] = 1
                    }
                }
            ]
        });

        Assert.True(report.Success);
        Assert.Equal(new[] { "echo", "echo#2", "echo#3", "sum" }, report.Steps.Select(step => step.Id).ToArray());
        var firstEcho = Assert.IsType<Dictionary<string, object>>(report.Steps[0].Result);
        Assert.Equal(2, firstEcho["value"]);
        Assert.Equal(0, firstEcho["index"]);
        var thirdEcho = Assert.IsType<Dictionary<string, object>>(report.Steps[2].Result);
        Assert.Equal(6, thirdEcho["value"]);
        Assert.Equal(2, thirdEcho["index"]);
        var sumProperty = report.Steps[3].Result?.GetType().GetProperty("sum");
        Assert.NotNull(sumProperty);
        Assert.Equal(7, sumProperty!.GetValue(report.Steps[3].Result));
    }

    [Fact]
    public void CanExecuteBoundedWhileLoopWithSetAndArithmetic()
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
                    Type = "let",
                    Name = "count",
                    Value = 0
                },
                new CapabilityScriptStep
                {
                    Type = "while",
                    MaxIterations = 10,
                    Condition = new Dictionary<string, object>
                    {
                        ["path"] = "vars.count",
                        ["lessThan"] = 3
                    },
                    Body =
                    [
                        new CapabilityScriptStep
                        {
                            Type = "set",
                            Name = "count",
                            Value = new Dictionary<string, object>
                            {
                                ["$add"] = new List<object>
                                {
                                    new Dictionary<string, object> { ["$var"] = "count" },
                                    1
                                }
                            }
                        },
                        new CapabilityScriptStep
                        {
                            Id = "tick",
                            Call = "test/echo",
                            Arguments = new Dictionary<string, object>
                            {
                                ["value"] = new Dictionary<string, object>
                                {
                                    ["$var"] = "count"
                                }
                            }
                        }
                    ]
                },
                new CapabilityScriptStep
                {
                    Id = "done",
                    Call = "test/add",
                    Arguments = new Dictionary<string, object>
                    {
                        ["left"] = new Dictionary<string, object>
                        {
                            ["$var"] = "count"
                        },
                        ["right"] = 10
                    }
                }
            ]
        });

        Assert.True(report.Success);
        Assert.Equal(new[] { "tick", "tick#2", "tick#3", "done" }, report.Steps.Select(step => step.Id).ToArray());
        Assert.Equal(1, Assert.IsType<Dictionary<string, object>>(report.Steps[0].Result)["value"]);
        Assert.Equal(3, Assert.IsType<Dictionary<string, object>>(report.Steps[2].Result)["value"]);
        var sumProperty = report.Steps[3].Result?.GetType().GetProperty("sum");
        Assert.NotNull(sumProperty);
        Assert.Equal(13, sumProperty!.GetValue(report.Steps[3].Result));
    }

    [Fact]
    public void FailsWhenWhileLoopExceedsMaxIterations()
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
                    Type = "let",
                    Name = "keepGoing",
                    Value = true
                },
                new CapabilityScriptStep
                {
                    Id = "guard",
                    Type = "while",
                    MaxIterations = 2,
                    Condition = new Dictionary<string, object>
                    {
                        ["path"] = "vars.keepGoing",
                        ["equals"] = true
                    },
                    Body =
                    [
                        new CapabilityScriptStep
                        {
                            Id = "tick",
                            Call = "rimbridge/ping"
                        }
                    ]
                }
            ]
        });

        Assert.False(report.Success);
        Assert.True(report.Halted);
        Assert.Equal(new[] { "tick", "tick#2", "guard" }, report.Steps.Select(step => step.Id).ToArray());
        Assert.Equal("script.max_iterations", report.Steps[2].Error.Code);
    }

    [Fact]
    public void AssertFailureStopsScriptAndKeepsPrintedOutput()
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
                    Type = "let",
                    Name = "count",
                    Value = 3
                },
                new CapabilityScriptStep
                {
                    Id = "trace_count",
                    Type = "print",
                    Message = "Observed colonist count",
                    Value = new Dictionary<string, object>
                    {
                        ["$var"] = "count"
                    }
                },
                new CapabilityScriptStep
                {
                    Id = "assert_count",
                    Type = "assert",
                    Message = "Expected exactly four colonists.",
                    Condition = new Dictionary<string, object>
                    {
                        ["path"] = "vars.count",
                        ["equals"] = 4
                    }
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
        Assert.NotNull(report.Error);
        Assert.Equal("script.assertion_failed", report.Error.Code);
        Assert.Contains("Expected exactly four colonists.", report.Error.Message);
        Assert.Single(report.Output);
        Assert.Equal("trace_count", report.Output[0].StatementId);
        Assert.Equal("Observed colonist count", report.Output[0].Message);
        Assert.Equal(3, report.Output[0].Value);
        Assert.Single(report.Steps);
        Assert.Equal("assert_count", report.Steps[0].Id);
        Assert.Equal("script.assertion_failed", report.Steps[0].Error.Code);
    }

    [Fact]
    public void FailStatementStopsScriptEvenWhenContinueOnErrorIsTrue()
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
                    Id = "bail_out",
                    Type = "fail",
                    Message = "Could not find a safe rally cell.",
                    Value = new Dictionary<string, object>
                    {
                        ["reason"] = "dry-run rejected perimeter"
                    }
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
        Assert.NotNull(report.Error);
        Assert.Equal("script.failed", report.Error.Code);
        Assert.Equal("Could not find a safe rally cell.", report.Error.Message);
        var details = Assert.IsType<Dictionary<string, object>>(report.Error.Details);
        Assert.Equal("dry-run rejected perimeter", details["reason"]);
        Assert.Single(report.Steps);
        Assert.Equal("bail_out", report.Steps[0].Id);
    }

    [Fact]
    public void PrintAndReturnProduceScriptOutputAndFinalResult()
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
                    Type = "let",
                    Name = "count",
                    Value = 2
                },
                new CapabilityScriptStep
                {
                    Id = "trace_count",
                    Type = "print",
                    Message = "Loop count",
                    Value = new Dictionary<string, object>
                    {
                        ["$var"] = "count"
                    }
                },
                new CapabilityScriptStep
                {
                    Id = "return_count",
                    Type = "return",
                    Value = new Dictionary<string, object>
                    {
                        ["count"] = new Dictionary<string, object>
                        {
                            ["$var"] = "count"
                        }
                    }
                },
                new CapabilityScriptStep
                {
                    Id = "later",
                    Call = "rimbridge/ping"
                }
            ]
        });

        Assert.True(report.Success);
        Assert.False(report.Halted);
        Assert.True(report.Returned);
        Assert.Empty(report.Steps);
        Assert.Single(report.Output);
        Assert.Equal("trace_count", report.Output[0].StatementId);
        Assert.Equal("Loop count", report.Output[0].Message);
        Assert.Equal(2, report.Output[0].Value);
        var result = Assert.IsType<Dictionary<string, object>>(report.Result);
        Assert.Equal(2, result["count"]);
    }

    [Fact]
    public void FailsWhenScriptExceedsStatementExecutionBudget()
    {
        var registry = new CapabilityRegistry();
        registry.RegisterProvider(new ScriptTestProvider());
        var runner = new CapabilityScriptRunner(registry);

        var report = runner.Execute(new CapabilityScriptDefinition
        {
            MaxExecutedStatements = 2,
            Steps =
            [
                new CapabilityScriptStep
                {
                    Type = "let",
                    Name = "keepGoing",
                    Value = true
                },
                new CapabilityScriptStep
                {
                    Id = "guard",
                    Type = "while",
                    MaxIterations = 100,
                    Condition = new Dictionary<string, object>
                    {
                        ["path"] = "vars.keepGoing",
                        ["equals"] = true
                    }
                }
            ]
        });

        Assert.False(report.Success);
        Assert.True(report.Halted);
        Assert.NotNull(report.Error);
        Assert.Equal("script.statement_limit_exceeded", report.Error.Code);
        Assert.Single(report.Steps);
        Assert.Equal("guard", report.Steps[0].Id);
        Assert.Equal("script.statement_limit_exceeded", report.Steps[0].Error.Code);
    }

    [Fact]
    public void FailsWhenScriptExceedsDurationBudget()
    {
        var registry = new CapabilityRegistry();
        registry.RegisterProvider(new ScriptTestProvider());
        var runner = new CapabilityScriptRunner(registry);

        var report = runner.Execute(new CapabilityScriptDefinition
        {
            MaxDurationMs = 5,
            Steps =
            [
                new CapabilityScriptStep
                {
                    Id = "sleep",
                    Call = "test/sleep",
                    Arguments = new Dictionary<string, object>
                    {
                        ["durationMs"] = 20
                    }
                }
            ]
        });

        Assert.False(report.Success);
        Assert.True(report.Halted);
        Assert.NotNull(report.Error);
        Assert.Equal("script.timeout", report.Error.Code);
        Assert.Single(report.Steps);
        Assert.Equal("sleep", report.Steps[0].Id);
        Assert.Equal("script.timeout", report.Steps[0].Error.Code);
    }

    [Fact]
    public void FailsWhenScriptExceedsMaxControlDepth()
    {
        var registry = new CapabilityRegistry();
        registry.RegisterProvider(new ScriptTestProvider());
        var runner = new CapabilityScriptRunner(registry);

        var report = runner.Execute(new CapabilityScriptDefinition
        {
            MaxControlDepth = 1,
            Steps =
            [
                new CapabilityScriptStep
                {
                    Type = "let",
                    Name = "enter",
                    Value = true
                },
                new CapabilityScriptStep
                {
                    Id = "outer",
                    Type = "if",
                    Condition = new Dictionary<string, object>
                    {
                        ["path"] = "vars.enter",
                        ["equals"] = true
                    },
                    Body =
                    [
                        new CapabilityScriptStep
                        {
                            Id = "inner",
                            Type = "if",
                            Condition = new Dictionary<string, object>
                            {
                                ["path"] = "vars.enter",
                                ["equals"] = true
                            },
                            Body =
                            [
                                new CapabilityScriptStep
                                {
                                    Id = "later",
                                    Call = "rimbridge/ping"
                                }
                            ]
                        }
                    ]
                }
            ]
        });

        Assert.False(report.Success);
        Assert.True(report.Halted);
        Assert.NotNull(report.Error);
        Assert.Equal("script.max_depth_exceeded", report.Error.Code);
        Assert.Single(report.Steps);
        Assert.Equal("inner", report.Steps[0].Id);
        Assert.Equal("script.max_depth_exceeded", report.Steps[0].Error.Code);
    }

    [Fact]
    public void FailsWhenScriptDefinitionDeclaresInvalidLimits()
    {
        var registry = new CapabilityRegistry();
        registry.RegisterProvider(new ScriptTestProvider());
        var runner = new CapabilityScriptRunner(registry);

        var report = runner.Execute(new CapabilityScriptDefinition
        {
            MaxDurationMs = 0,
            Steps =
            [
                new CapabilityScriptStep
                {
                    Id = "later",
                    Call = "rimbridge/ping"
                }
            ]
        });

        Assert.False(report.Success);
        Assert.True(report.Halted);
        Assert.NotNull(report.Error);
        Assert.Equal("script.invalid_definition", report.Error.Code);
        Assert.Empty(report.Steps);
    }

    private sealed class ScriptTestProvider : IRimBridgeCapabilityProvider
    {
        private int _counter;
        private int _pawnSnapshotCallCount;

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

            yield return new RimBridgeCapabilityRegistration(
                new CapabilityDescriptor
                {
                    Id = "test.state/counter",
                    ProviderId = ProviderId,
                    Category = "test",
                    Aliases = ["test/counter"]
                },
                (_, _) =>
                {
                    _counter++;
                    return Task.FromResult(OperationEnvelope.Completed("op_counter", "test.state/counter", System.DateTimeOffset.UtcNow, new { value = _counter }));
                });

            yield return new RimBridgeCapabilityRegistration(
                new CapabilityDescriptor
                {
                    Id = "test.state/pawn_snapshot",
                    ProviderId = ProviderId,
                    Category = "test",
                    Aliases = ["test/pawn_snapshot"]
                },
                (_, _) =>
                {
                    _pawnSnapshotCallCount++;
                    var result = _pawnSnapshotCallCount switch
                    {
                        1 => new
                        {
                            colonists = new[]
                            {
                                new { name = "Blue", position = new { x = 140, z = 118 }, job = "Goto" },
                                new { name = "Dee", position = new { x = 143, z = 123 }, job = "Goto" },
                                new { name = "Trigger", position = new { x = 144, z = 117 }, job = "Wait_Combat" }
                            }
                        },
                        _ => new
                        {
                            colonists = new[]
                            {
                                new { name = "Blue", position = new { x = 143, z = 118 }, job = "Wait_Combat" },
                                new { name = "Dee", position = new { x = 143, z = 119 }, job = "Wait_MaintainPosture" },
                                new { name = "Trigger", position = new { x = 144, z = 117 }, job = "Wait_Combat" }
                            }
                        }
                    };

                    return Task.FromResult(OperationEnvelope.Completed("op_pawns", "test.state/pawn_snapshot", System.DateTimeOffset.UtcNow, result));
                });

            yield return new RimBridgeCapabilityRegistration(
                new CapabilityDescriptor
                {
                    Id = "test.state/echo",
                    ProviderId = ProviderId,
                    Category = "test",
                    Aliases = ["test/echo"]
                },
                (invocation, _) => Task.FromResult(OperationEnvelope.Completed(
                    "op_echo",
                    "test.state/echo",
                    System.DateTimeOffset.UtcNow,
                    new Dictionary<string, object>(invocation.Arguments))));

            yield return new RimBridgeCapabilityRegistration(
                new CapabilityDescriptor
                {
                    Id = "test.state/sleep",
                    ProviderId = ProviderId,
                    Category = "test",
                    Aliases = ["test/sleep"]
                },
                (invocation, _) =>
                {
                    var durationMs = System.Convert.ToInt32(invocation.Arguments["durationMs"], System.Globalization.CultureInfo.InvariantCulture);
                    System.Threading.Thread.Sleep(durationMs);
                    return Task.FromResult(OperationEnvelope.Completed(
                        "op_sleep",
                        "test.state/sleep",
                        System.DateTimeOffset.UtcNow,
                        new Dictionary<string, object>
                        {
                            ["durationMs"] = durationMs
                        }));
                });
        }
    }
}
