using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace RimBridgeServer;

public interface IMcpTool
{
	string Name { get; }              // e.g., "rimbridge.core/ping"
	string Description { get; }
	JObject InputSchema { get; }      // JSON Schema object
	Task<ToolResult> CallAsync(JObject args, ToolContext ctx, CancellationToken ct);
}

public sealed class ToolResult
{
	public List<ToolContent> Content { get; set; } = [];
	public bool IsError { get; set; }
	public static ToolResult Text(string text, bool isError = false)
		=> new() { IsError = isError, Content = [ToolContent.Create(text)] };
}

public sealed class ToolContent
{
	public string Type { get; set; }  // "text" (others reserved by spec)
	public string Text { get; set; }
	public static ToolContent Create(string text) => new() { Type = "text", Text = text };
}

public sealed class ToolContext
{
	public string ClientId { get; set; }    // freeform; not required for minimal server
	public string ProtocolVersion { get; set; }
	public IMainThreadDispatcher Dispatcher { get; set; }
	public ILogger Logger { get; set; }
}

public interface IMcpPlugin
{
	string Id { get; }               // namespace prefix, e.g., "rimbridge.core"
	string Version { get; }
	IEnumerable<IMcpTool> GetTools();
	void Initialize(IMcpServerApi api);
}

public interface IMcpServerApi
{
	IMainThreadDispatcher Dispatcher { get; }
	ILogger Logger { get; }
	void RegisterTool(IMcpTool tool); // optional secondary registration path
}

public interface IMainThreadDispatcher
{
	void Post(System.Action action);
}

public interface ILogger
{
	void Info(string msg);
	void Warn(string msg);
	void Error(string msg);
}

[System.AttributeUsage(System.AttributeTargets.Class)]
public sealed class McpPluginAttribute : System.Attribute { }
