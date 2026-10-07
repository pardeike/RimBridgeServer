using System.Linq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RimBridgeServer.Contracts;
using RimBridgeServer.Core;
using RimBridgeServer.Extensions.Abstractions;
using Xunit;

namespace RimBridgeServer.Core.Tests;

public class LuaScriptCompilerTests
{
    [Fact]
    public void CompilesAndExecutesCallResultsForeachAndBooleanArguments()
    {
        var compiler = new LuaScriptCompiler();
        var definition = compiler.Compile("""
            local snapshot = rb.call("test/echo", { colonists = { "Blue", "Dee", "Trigger" } })
            local names = snapshot.result.colonists

            for i, name in ipairs(names) do
              rb.call("test/echo", { index = i, name = name, append = i > 1 })
            end

            return names[1]
            """);

        var registry = new CapabilityRegistry();
        registry.RegisterProvider(new LuaScriptTestProvider());
        var runner = new CapabilityScriptRunner(registry);

        var report = runner.Execute(definition);

        Assert.True(report.Success);
        Assert.True(report.Returned);
        Assert.Equal("Blue", report.Result);
        Assert.Equal(4, report.ExecutedStepCount);
        Assert.Collection(report.Steps,
            snapshot =>
            {
                Assert.True(snapshot.Success);
            },
            firstLoop =>
            {
                var result = Assert.IsType<Dictionary<string, object>>(firstLoop.Result);
                Assert.Equal(1, Assert.IsType<int>(result["index"]));
                Assert.Equal("Blue", Assert.IsType<string>(result["name"]));
                Assert.False(Assert.IsType<bool>(result["append"]));
            },
            secondLoop =>
            {
                var result = Assert.IsType<Dictionary<string, object>>(secondLoop.Result);
                Assert.Equal(2, Assert.IsType<int>(result["index"]));
                Assert.True(Assert.IsType<bool>(result["append"]));
            },
            thirdLoop =>
            {
                var result = Assert.IsType<Dictionary<string, object>>(thirdLoop.Result);
                Assert.Equal(3, Assert.IsType<int>(result["index"]));
                Assert.True(Assert.IsType<bool>(result["append"]));
            });
    }

    [Fact]
    public void CompilesControlFlowOutputAndAssertions()
    {
        var compiler = new LuaScriptCompiler();
        var definition = compiler.Compile("""
            local count = 0

            while count < 2 do
              if count == 0 then
                print("count", count)
              end

              count = count + 1
            end

            rb.assert(count == 2, "Expected count to reach two.")
            return count
            """);

        var runner = new CapabilityScriptRunner(new CapabilityRegistry());
        var report = runner.Execute(definition);

        Assert.True(report.Success);
        Assert.True(report.Returned);
        Assert.Equal(2, report.Result);
        Assert.Single(report.Output);
        Assert.Equal("count", report.Output[0].Message);
        Assert.Equal(0, report.Output[0].Value);
        Assert.Empty(report.Steps);
    }

    [Fact]
    public void AcceptsEmptyTableAsCallArguments()
    {
        var definition = new LuaScriptCompiler().Compile("""
            rb.call("test/echo", {})
            return 1
            """);

        var registry = new CapabilityRegistry();
        registry.RegisterProvider(new LuaScriptTestProvider());
        var report = new CapabilityScriptRunner(registry).Execute(definition);

        Assert.True(report.Success);
        Assert.Equal(1, report.ExecutedStepCount);
    }

    [Fact]
    public void MissingFieldsReadAsNilForOrDefaultsAndNilComparisons()
    {
        var definition = new LuaScriptCompiler().Compile("""
            local item = { x = 4 }
            local width = item.w or 1
            local hasNoWidth = item.w == nil
            return { width = width, hasNoWidth = hasNoWidth, x = item.x or 9 }
            """);

        var report = new CapabilityScriptRunner(new CapabilityRegistry()).Execute(definition);

        Assert.True(report.Success);
        var result = Assert.IsType<Dictionary<string, object>>(report.Result);
        Assert.Equal(1, result["width"]);
        Assert.True(Assert.IsType<bool>(result["hasNoWidth"]));
        Assert.Equal(4, result["x"]);
    }

    [Fact]
    public void ConcatenatesStrings()
    {
        var definition = new LuaScriptCompiler().Compile("""
            local count = 3
            return "Wall " .. count .. "x" .. 1.5
            """);

        var report = new CapabilityScriptRunner(new CapabilityRegistry()).Execute(definition);

        Assert.True(report.Success);
        Assert.Equal("Wall 3x1.5", report.Result);
    }

    [Fact]
    public void PrintAcceptsNonLiteralFirstArgument()
    {
        var definition = new LuaScriptCompiler().Compile("""
            local label = "planned"
            print(label, 2)
            """);

        var report = new CapabilityScriptRunner(new CapabilityRegistry()).Execute(definition);

        Assert.True(report.Success);
        var row = Assert.Single(report.Output);
        var values = Assert.IsAssignableFrom<IEnumerable<object>>(row.Value).ToList();
        Assert.Equal("planned", values[0]);
        Assert.Equal(2, values[1]);
    }

    [Fact]
    public void PreservesLocalShadowingInsideNestedScopes()
    {
        var compiler = new LuaScriptCompiler();
        var definition = compiler.Compile("""
            local count = 10

            if true then
              local count = 1
              count = count + 1
            end

            return count
            """);

        var runner = new CapabilityScriptRunner(new CapabilityRegistry());
        var report = runner.Execute(definition);

        Assert.True(report.Success);
        Assert.Equal(10, report.Result);
    }

    [Fact]
    public void InjectsReadOnlyParamsBinding()
    {
        var compiler = new LuaScriptCompiler();
        var definition = compiler.Compile("""
            rb.assert(params.screenshotFileName ~= nil, "Expected screenshotFileName.")

            return {
              screenshotFileName = params.screenshotFileName,
              retryLimit = params.retryLimit,
              firstPawn = params.pawnNames[1]
            }
            """, new Dictionary<string, object>
            {
                ["screenshotFileName"] = "capture_001",
                ["retryLimit"] = 8,
                ["pawnNames"] = new List<object> { "Blue", "Dee", "Trigger" }
            });

        var runner = new CapabilityScriptRunner(new CapabilityRegistry());
        var report = runner.Execute(definition);

        Assert.True(report.Success);
        Assert.True(report.Returned);

        var result = Assert.IsType<Dictionary<string, object>>(report.Result);
        Assert.Equal("capture_001", Assert.IsType<string>(result["screenshotFileName"]));
        Assert.Equal(8, Convert.ToInt32(result["retryLimit"]));
        Assert.Equal("Blue", Assert.IsType<string>(result["firstPawn"]));
    }

    [Fact]
    public void MissingParamsFieldsResolveAsNilSoLuaDefaultsCanApply()
    {
        var compiler = new LuaScriptCompiler();
        var definition = compiler.Compile("""
            local saveName = params.saveName or "pr93"
            local retryLimit = params.retryLimit or 6
            local firstPawn = params.pawnNames[1] or "none"

            return {
              saveName = saveName,
              retryLimit = retryLimit,
              firstPawn = firstPawn
            }
            """);

        var runner = new CapabilityScriptRunner(new CapabilityRegistry());
        var report = runner.Execute(definition);

        Assert.True(report.Success);
        Assert.True(report.Returned);

        var result = Assert.IsType<Dictionary<string, object>>(report.Result);
        Assert.Equal("pr93", Assert.IsType<string>(result["saveName"]));
        Assert.Equal(6, Convert.ToInt32(result["retryLimit"]));
        Assert.Equal("none", Assert.IsType<string>(result["firstPawn"]));
    }

    [Fact]
    public void RejectsReassigningParamsBinding()
    {
        var compiler = new LuaScriptCompiler();
        var error = Assert.Throws<LuaScriptCompileException>(() => compiler.Compile("""
            params = {}
            """));

        Assert.Equal("lua.unsupported_statement", error.Code);
        Assert.Contains("read-only", error.Message);
    }

    [Fact]
    public void RejectsUnsupportedGlobalAssignmentWithLocation()
    {
        var compiler = new LuaScriptCompiler();
        var error = Assert.Throws<LuaScriptCompileException>(() => compiler.Compile("""
            count = 1
            """));

        Assert.Equal("lua.unsupported_statement", error.Code);
        Assert.Equal(1, error.Line);
        Assert.NotNull(error.Column);
    }

    private sealed class LuaScriptTestProvider : IRimBridgeCapabilityProvider
    {
        public string ProviderId => "lua.test";

        public IEnumerable<RimBridgeCapabilityRegistration> GetCapabilities()
        {
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
        }
    }
}
