using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace RimBridgeServer;

[McpPlugin]
public sealed class CorePlugin : IMcpPlugin
{
	public string Id => "rimbridge.core";
	public string Version => "0.1.0";
	public void Initialize(IMcpServerApi api) { /* no-op */ }

	public IEnumerable<IMcpTool> GetTools()
	{
		yield return new PingTool();
	}

	private sealed class PingTool : IMcpTool
	{
		public string Name => "rimbridge.core/ping";
		public string Description => "Connectivity test. Returns 'pong'.";
		public JObject InputSchema => new()
		{
			["type"] = "object",
			["properties"] = new JObject(),
			["required"] = new JArray()
		};
		public Task<ToolResult> CallAsync(JObject args, ToolContext ctx, CancellationToken ct)
			 => Task.FromResult(ToolResult.Text("pong"));
	}
}
