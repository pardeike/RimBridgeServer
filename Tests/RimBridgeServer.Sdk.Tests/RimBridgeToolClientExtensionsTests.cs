using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimBridgeServer.Sdk;
using Xunit;

namespace RimBridgeServer.Sdk.Tests;

public class RimBridgeToolClientExtensionsTests
{
    [Fact]
    public async Task NonGenericCallAsyncUsesObjectResult()
    {
        var client = new RecordingToolClient();

        var result = await client.CallAsync("rimbridge/ping", new { label = "pong" });

        Assert.Equal(typeof(object), client.RequestedResultType);
        Assert.Equal("rimbridge/ping", client.RequestedId);
        Assert.NotNull(client.RequestedArgs);
        Assert.True(result.Succeeded());
        Assert.Equal("pong", result.ReadResult<string>("label"));
    }

    [Fact]
    public void SucceededHonorsTransportAndPayloadSuccess()
    {
        Assert.False(new RimBridgeToolCallResult<object>
        {
            Success = false,
            Result = new { success = true }
        }.Succeeded());

        Assert.False(new RimBridgeToolCallResult<object>
        {
            Success = true,
            Result = new { success = false }
        }.Succeeded());

        Assert.True(new RimBridgeToolCallResult<object>
        {
            Success = true,
            Result = new { success = true }
        }.Succeeded());

        Assert.True(new RimBridgeToolCallResult<object>
        {
            Success = true,
            Result = new { message = "done" }
        }.Succeeded());
    }

    [Fact]
    public void ReadResultReadsAnonymousNestedValuesCaseInsensitively()
    {
        var result = new RimBridgeToolCallResult<object>
        {
            Success = true,
            Result = new
            {
                Success = "true",
                State = new
                {
                    ProgramState = "Playing",
                    MapCount = "2"
                }
            }
        };

        Assert.True(result.PayloadSuccess());
        Assert.Equal("Playing", result.ReadResult<string>("state", "programstate"));
        Assert.Equal(2, result.ReadResult<int>("STATE", "mapCount"));
    }

    [Fact]
    public void ReadResultReadsDictionariesNestedValuesAndNullableValues()
    {
        var result = new RimBridgeToolCallResult<Dictionary<string, object>>
        {
            Success = true,
            Result = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["success"] = true,
                ["state"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["hasCurrentGame"] = "true",
                    ["ticksGame"] = 42L,
                    ["missingNullable"] = null
                }
            }
        };

        Assert.True(result.Succeeded());
        Assert.True(result.ReadResult<bool>("STATE", "hascurrentgame"));
        Assert.Equal(42, result.ReadResult<int>("state", "ticksGame"));
        Assert.Null(result.ReadResult<int?>("state", "missingNullable"));
    }

    [Fact]
    public void TryReadResultReturnsFalseForMissingOrUnconvertibleValues()
    {
        var result = new RimBridgeToolCallResult<object>
        {
            Success = true,
            Result = new { count = "many" }
        };

        Assert.False(result.TryReadResult<int>(out _, "missing"));
        Assert.False(result.TryReadResult<int>(out _, "count"));
    }

    [Fact]
    public void ReadResultCanReadTypedDtoPayloads()
    {
        var result = new RimBridgeToolCallResult<LoadGameReadyResult>
        {
            Success = true,
            Result = new LoadGameReadyResult
            {
                Success = true,
                SaveName = "zeflammenwerfer walkthrough",
                State = new RuntimeState
                {
                    ProgramState = "Playing",
                    VisualReady = true
                }
            }
        };

        Assert.True(result.Succeeded());
        Assert.Equal("zeflammenwerfer walkthrough", result.ReadResult<string>("saveName"));
        Assert.True(result.ReadResult<bool>("state", "visualReady"));
    }

    private sealed class RecordingToolClient : IRimBridgeToolClient
    {
        public Type RequestedResultType { get; private set; }

        public string RequestedId { get; private set; }

        public object RequestedArgs { get; private set; }

        public IReadOnlyList<RimBridgeToolDescriptor> List(RimBridgeToolQuery query = null)
        {
            throw new NotSupportedException();
        }

        public RimBridgeToolDescriptor Get(string idOrAlias)
        {
            throw new NotSupportedException();
        }

        public bool Exists(string idOrAlias)
        {
            throw new NotSupportedException();
        }

        public Task<RimBridgeToolCallResult<object>> CallAsync(
            string idOrAlias,
            object args = null,
            RimBridgeToolCallOptions options = null,
            CancellationToken cancellationToken = default)
        {
            return CallAsync<object>(idOrAlias, args, options, cancellationToken);
        }

        public Task<RimBridgeToolCallResult<T>> CallAsync<T>(
            string idOrAlias,
            object args = null,
            RimBridgeToolCallOptions options = null,
            CancellationToken cancellationToken = default)
        {
            RequestedResultType = typeof(T);
            RequestedId = idOrAlias;
            RequestedArgs = args;

            object result = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["success"] = true,
                ["label"] = "pong"
            };

            return Task.FromResult(new RimBridgeToolCallResult<T>
            {
                Success = true,
                Result = result is T typed ? typed : default
            });
        }

        public Task<RimBridgeOperationInfo> QueueAsync(
            string idOrAlias,
            object args = null,
            RimBridgeToolCallOptions options = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class LoadGameReadyResult
    {
        public bool Success { get; set; }

        public string SaveName { get; set; }

        public RuntimeState State { get; set; }
    }

    private sealed class RuntimeState
    {
        public string ProgramState { get; set; }

        public bool VisualReady { get; set; }
    }
}
