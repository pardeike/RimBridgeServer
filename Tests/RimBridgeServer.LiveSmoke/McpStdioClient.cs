using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RimBridgeServer.LiveSmoke;

internal sealed class McpStdioClient : IAsyncDisposable
{
    private const string ProtocolVersion = "2024-11-05";
    private readonly Process _process;
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly Task _stderrPump;
    private readonly Queue<string> _stderrTail = new();
    private readonly object _stderrGate = new();
    private readonly SemaphoreSlim _rpcGate = new(1, 1);
    private int _nextId = 1;

    private McpStdioClient(Process process)
    {
        _process = process;
        _input = process.StandardInput.BaseStream;
        _output = process.StandardOutput.BaseStream;
        _stderrPump = PumpStandardErrorAsync(process.StandardError);
    }

    public static async Task<McpStdioClient> StartAsync(string gabsBinaryPath, string? configDir, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = gabsBinaryPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Environment.CurrentDirectory
        };

        startInfo.ArgumentList.Add("server");
        startInfo.ArgumentList.Add("stdio");
        startInfo.ArgumentList.Add("--log-level");
        startInfo.ArgumentList.Add("warn");

        if (!string.IsNullOrWhiteSpace(configDir))
        {
            startInfo.ArgumentList.Add("--configDir");
            startInfo.ArgumentList.Add(configDir);
        }

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start '{gabsBinaryPath}'.");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Could not start GABS using '{gabsBinaryPath}'. {ex.Message}", ex);
        }

        var client = new McpStdioClient(process);
        await client.InitializeAsync(cancellationToken);
        return client;
    }

    public async Task<ToolInvocationResult> CallToolAsync(string toolName, object arguments, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var resultNode = await SendRequestAsync("tools/call", new JsonObject
        {
            ["name"] = toolName,
            ["arguments"] = JsonSerializer.SerializeToNode(arguments)
        }, cancellationToken);

        var isError = JsonNodeHelpers.ReadBoolean(resultNode, "isError") ?? false;
        var rawStructuredContent = JsonNodeHelpers.CloneNode(JsonNodeHelpers.GetPath(resultNode, "structuredContent"));
        var text = JsonNodeHelpers.ReadTextContent(resultNode);
        var structuredContent = JsonNodeHelpers.NormalizeStructuredPayload(rawStructuredContent, text);
        var message = JsonNodeHelpers.ReadString(structuredContent, "message");
        if (string.IsNullOrWhiteSpace(message))
            message = string.IsNullOrWhiteSpace(text) ? toolName : text;

        if (!isError)
        {
            var successFlag = JsonNodeHelpers.ReadBoolean(structuredContent, "success");
            if (!successFlag.HasValue)
                successFlag = JsonNodeHelpers.ReadBoolean(rawStructuredContent, "success");
            if (successFlag.HasValue && successFlag.Value == false)
                isError = true;
        }

        return new ToolInvocationResult
        {
            ToolName = toolName,
            DurationMs = stopwatch.ElapsedMilliseconds,
            Success = !isError,
            IsError = isError,
            Text = text,
            Message = message,
            StructuredContent = structuredContent
        };
    }

    public IReadOnlyList<string> GetStderrTail()
    {
        lock (_stderrGate)
        {
            return _stderrTail.ToList();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _input.FlushAsync();
        }
        catch
        {
        }

        try
        {
            _process.StandardInput.Close();
        }
        catch
        {
        }

        if (!_process.HasExited)
        {
            try
            {
                if (!_process.WaitForExit(1000))
                    _process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
        }

        try
        {
            await _stderrPump;
        }
        catch
        {
        }

        _process.Dispose();
        _rpcGate.Dispose();
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await SendRequestAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = ProtocolVersion,
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject
            {
                ["name"] = "rimbridge-live-smoke",
                ["version"] = "0.1.0"
            }
        }, cancellationToken);

        await SendNotificationAsync("notifications/initialized", new JsonObject(), cancellationToken);
    }

    private async Task<JsonNode?> SendRequestAsync(string method, JsonNode? @params, CancellationToken cancellationToken)
    {
        await _rpcGate.WaitAsync(cancellationToken);
        try
        {
            var id = Interlocked.Increment(ref _nextId);
            await WriteMessageAsync(new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["method"] = method,
                ["params"] = @params
            }, cancellationToken);

            while (true)
            {
                var message = await ReadMessageAsync(cancellationToken)
                    ?? throw new InvalidOperationException("GABS closed the MCP stream before replying.");

                var responseId = JsonNodeHelpers.ReadInt32(message, "id");
                if (responseId is null)
                    continue;

                if (responseId.Value != id)
                    continue;

                var errorNode = JsonNodeHelpers.GetPath(message, "error");
                if (errorNode is not null)
                    throw new InvalidOperationException($"MCP request '{method}' failed. {JsonNodeHelpers.ReadString(errorNode, "message")}");

                return JsonNodeHelpers.GetPath(message, "result");
            }
        }
        finally
        {
            _rpcGate.Release();
        }
    }

    private async Task SendNotificationAsync(string method, JsonNode? @params, CancellationToken cancellationToken)
    {
        await _rpcGate.WaitAsync(cancellationToken);
        try
        {
            await WriteMessageAsync(new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = method,
                ["params"] = @params
            }, cancellationToken);
        }
        finally
        {
            _rpcGate.Release();
        }
    }

    private async Task WriteMessageAsync(JsonNode message, CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(message.ToJsonString());
        var header = Encoding.ASCII.GetBytes($"Content-Length: {payload.Length}\r\n\r\n");
        await _input.WriteAsync(header, cancellationToken);
        await _input.WriteAsync(payload, cancellationToken);
        await _input.FlushAsync(cancellationToken);
    }

    private async Task<JsonNode?> ReadMessageAsync(CancellationToken cancellationToken)
    {
        var headerBytes = new List<byte>(128);
        var singleByte = new byte[1];

        while (true)
        {
            var read = await _output.ReadAsync(singleByte.AsMemory(0, 1), cancellationToken);
            if (read == 0)
                return null;

            headerBytes.Add(singleByte[0]);
            if (headerBytes.Count >= 4
                && headerBytes[^4] == '\r'
                && headerBytes[^3] == '\n'
                && headerBytes[^2] == '\r'
                && headerBytes[^1] == '\n')
            {
                break;
            }
        }

        var headerText = Encoding.ASCII.GetString(headerBytes.ToArray());
        var contentLength = ParseContentLength(headerText);
        var payload = new byte[contentLength];
        await ReadExactlyAsync(_output, payload, cancellationToken);
        return JsonNode.Parse(payload);
    }

    private async Task PumpStandardErrorAsync(StreamReader reader)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (line is null)
                break;

            lock (_stderrGate)
            {
                _stderrTail.Enqueue(line);
                while (_stderrTail.Count > 40)
                    _stderrTail.Dequeue();
            }
        }
    }

    private static int ParseContentLength(string headerText)
    {
        foreach (var line in headerText.Split(["\r\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            const string prefix = "Content-Length:";
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var rawValue = line[prefix.Length..].Trim();
                if (int.TryParse(rawValue, out var contentLength) && contentLength >= 0)
                    return contentLength;
            }
        }

        throw new InvalidOperationException($"Missing Content-Length header in MCP response: {headerText}");
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken);
            if (read == 0)
                throw new EndOfStreamException("Unexpected end of stream while reading an MCP frame.");

            offset += read;
        }
    }
}
