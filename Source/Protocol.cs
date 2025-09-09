// Minimal DTOs for JSON-RPC and MCP messages
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RimBridgeServer;

public sealed class JsonRpcRequest
{
	[JsonProperty("jsonrpc")] public string JsonRpc => "2.0";
	[JsonProperty("id")] public JToken Id { get; set; }   // string or number
	[JsonProperty("method")] public string Method { get; set; }
	[JsonProperty("params")] public JObject Params { get; set; }
}

public sealed class JsonRpcResponse
{
	[JsonProperty("jsonrpc")] public string JsonRpc => "2.0";
	[JsonProperty("id")] public JToken Id { get; set; }
	[JsonProperty("result")] public JObject Result { get; set; }
	[JsonProperty("error")] public JsonRpcError Error { get; set; }

	public static JsonRpcResponse Ok(JToken id, JObject result) => new() { Id = id, Result = result };
	public static JsonRpcResponse Err(JToken id, int code, string message, JObject data = null) =>
		 new()
		 { Id = id, Error = new JsonRpcError { Code = code, Message = message, Data = data } };
}

public sealed class JsonRpcError
{
	[JsonProperty("code")] public int Code { get; set; }
	[JsonProperty("message")] public string Message { get; set; }
	[JsonProperty("data")] public JObject Data { get; set; }
}

// initialize params/result
public static class Mcp
{
	public const string V20250326 = "2025-03-26";
	public const string V20250618 = "2025-06-18";

	public static readonly string[] Supported = [V20250618, V20250326];

	public static JObject InitializeResult(string negotiatedVersion, bool toolsListChanged)
	{
		return new JObject
		{
			["protocolVersion"] = negotiatedVersion,
			["capabilities"] = new JObject
			{
				["tools"] = new JObject { ["listChanged"] = toolsListChanged }
				// add logging/resources/prompts later
			},
			["serverInfo"] = new JObject
			{
				["name"] = "RimBridgeServer",
				["version"] = "0.1.0"
			}
		};
	}

	public static JObject ToolsListResult(JArray tools, string nextCursor = null)
	{
		var res = new JObject { ["tools"] = tools };
		if (!string.IsNullOrEmpty(nextCursor)) res["nextCursor"] = nextCursor;
		return res;
	}

	public static JObject ToolDef(string name, string description, JObject inputSchema, JObject annotations = null)
	{
		var o = new JObject
		{
			["name"] = name,
			["description"] = description,
			["inputSchema"] = inputSchema
		};
		if (annotations != null) o["annotations"] = annotations;
		return o;
	}

	public static JObject CallToolResult(bool isError, JArray content)
		 => new()
		 { ["isError"] = isError, ["content"] = content };
}
