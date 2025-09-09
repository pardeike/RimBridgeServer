using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RimBridgeServer;

public sealed class McpServerOptions
{
	public string[] Prefixes { get; set; } = ["http://127.0.0.1:5174/mcp/"];
	public string[] AllowedOrigins { get; set; } = ["null", "file://", "app://"];
	public string[] SupportedProtocolVersions { get; set; } = [Mcp.V20250618, Mcp.V20250326];
	public string ServerName { get; set; } = "RimBridgeServer";
	public string ServerVersion { get; set; } = "0.1.0";
	public bool RequireBearerToken { get; set; } = false;
	public string StaticBearerToken { get; set; } = null; // set via Mod settings if desired
}

public sealed class McpHttpServer
{
	private readonly HttpListener _listener = new();
	private readonly McpServerOptions _opts;
	private readonly PluginManager _plugins;
	private readonly GameThreadDispatcher _dispatcher;
	private readonly ILogger _log;

	public McpHttpServer(McpServerOptions opts)
	{
		_opts = opts;
		_log = new SimpleLogger();
		_dispatcher = new GameThreadDispatcher();
		_plugins = new PluginManager(_dispatcher, _log);
		foreach (var p in _opts.Prefixes) _listener.Prefixes.Add(p);
	}

	public void Start()
	{
		_plugins.DiscoverAndLoad(); // loads built-in Core plugin too
		_listener.Start();
		_log.Info($"MCP HTTP server listening: {string.Join(", ", _opts.Prefixes)}");
		_ = Task.Run(AcceptLoop);
		// Hook dispatcher into a GameComponent; see §5.6
		RimBridgeGameComponent.InstallDispatcher(_dispatcher);
	}

	public void Stop() { try { _listener.Stop(); } catch { } }

	private async Task AcceptLoop()
	{
		while (_listener.IsListening)
		{
			HttpListenerContext ctx = null;
			try { ctx = await _listener.GetContextAsync().ConfigureAwait(false); }
			catch { if (!_listener.IsListening) break; continue; }

			_ = Task.Run(() => Handle(ctx));
		}
	}

	private void SetCommonHeaders(HttpListenerResponse resp) => resp.AddHeader("Cache-Control", "no-store");

    private bool CheckAuth(HttpListenerRequest req, HttpListenerResponse resp)
    {
        if (!_opts.RequireBearerToken)
        {
            if (string.IsNullOrEmpty(_opts.StaticBearerToken))
                _log.Warn("Auth disabled: no bearer token found in ~/.api-keys (key RIMBRIDGE_TOKEN). Requests are not authenticated.");
            return true;
        }

        var auth = req.Headers["Authorization"];
        if (auth == null || !auth.StartsWith("Bearer ") || auth[7..] != _opts.StaticBearerToken)
        {
            resp.StatusCode = 401;
            // Minimal WWW-Authenticate hint; full OAuth flows are out-of-scope here
            resp.AddHeader("WWW-Authenticate", "Bearer realm=\"RimBridgeServer\"");
            return false;
        }
        return true;
    }

	private bool CheckOrigin(HttpListenerRequest req, HttpListenerResponse resp)
	{
		var origin = req.Headers["Origin"];
		if (string.IsNullOrEmpty(origin)) return true; // native clients often omit
		foreach (var allow in _opts.AllowedOrigins)
			if (origin.StartsWith(allow, StringComparison.OrdinalIgnoreCase)) return true;

		resp.StatusCode = 403;
		return false;
	}

	private async Task Handle(HttpListenerContext ctx)
	{
		var req = ctx.Request;
		var resp = ctx.Response;
		SetCommonHeaders(resp);

		if (req.HttpMethod == "GET") { resp.StatusCode = 405; resp.Close(); return; } // no SSE for minimal
		if (req.HttpMethod != "POST") { resp.StatusCode = 405; resp.Close(); return; }
		if (!CheckOrigin(req, resp)) { resp.Close(); return; }
		if (!CheckAuth(req, resp)) { resp.Close(); return; }

		string body;
		using (var sr = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8))
			body = await sr.ReadToEndAsync().ConfigureAwait(false);

		JsonRpcRequest rpc;
		try { rpc = JsonConvert.DeserializeObject<JsonRpcRequest>(body); }
		catch
		{
			resp.StatusCode = 400; await WriteJson(resp, JsonRpcResponse.Err(null, -32700, "Parse error")); return;
		}

		JObject result = null;
		JsonRpcResponse response;

		try
		{
			switch (rpc.Method)
			{
				case "initialize":
				{
					var requested = rpc.Params?["protocolVersion"]?.Value<string>();
					var negotiated = Array.Exists(_opts.SupportedProtocolVersions, v => v == requested)
						 ? requested : _opts.SupportedProtocolVersions[0];
					result = Mcp.InitializeResult(negotiated, toolsListChanged: false);
					// Optionally add instructions
					result["instructions"] = "RimBridge MCP server; tools: ping only.";
					response = JsonRpcResponse.Ok(rpc.Id, result);
					break;
				}
				case "notifications/initialized":
					// no response (but JSON-RPC notifications have no id; tolerate if sent as request)
					response = JsonRpcResponse.Ok(rpc.Id, []);
					break;

				case "ping":
					_log.Info($"rpc: ping from {req.RemoteEndPoint}");
					response = JsonRpcResponse.Ok(rpc.Id, []); // empty result
					break;

				case "tools/list":
				{
					var arr = new JArray();
					foreach (var t in _plugins.Tools)
					{
						var annotations = new JObject
						{
							["pluginId"] = t.Name.Contains("/") ? t.Name.Split('/')[0] : "unknown",
						};
						arr.Add(Mcp.ToolDef(t.Name, t.Description, t.InputSchema, annotations));
					}
					response = JsonRpcResponse.Ok(rpc.Id, Mcp.ToolsListResult(arr));
					break;
				}

				case "tools/call":
				{
					var name = rpc.Params?["name"]?.Value<string>();
					var args = (JObject)(rpc.Params?["arguments"] ?? new JObject());
					if (name == null) { response = JsonRpcResponse.Err(rpc.Id, -32602, "Missing tool name"); break; }

					var tool = FindTool(name);
					if (tool == null) { response = JsonRpcResponse.Err(rpc.Id, -32602, $"Unknown tool: {name}"); break; }

					var ctx2 = new ToolContext
					{
						ClientId = req.RemoteEndPoint?.ToString(),
						ProtocolVersion = req.Headers["MCP-Protocol-Version"] ?? Mcp.V20250326,
						Dispatcher = _plugins.Dispatcher,
						Logger = _plugins.Logger
					};

					var resultObj = await tool.CallAsync(args, ctx2, CancellationToken.None).ConfigureAwait(false);
					var content = new JArray();
					foreach (var c in resultObj.Content)
						content.Add(new JObject { ["type"] = c.Type, ["text"] = c.Text });

					response = JsonRpcResponse.Ok(rpc.Id, Mcp.CallToolResult(resultObj.IsError, content));
					break;
				}

				default:
					response = JsonRpcResponse.Err(rpc.Id, -32601, $"Method not found: {rpc.Method}");
					break;
			}
		}
		catch (Exception ex)
		{
			response = JsonRpcResponse.Err(rpc.Id, -32603, "Internal error", new JObject { ["detail"] = ex.ToString() });
		}

		await WriteJson(resp, response);
	}

	private IMcpTool FindTool(string name)
	{
		foreach (var t in _plugins.Tools)
			if (string.Equals(t.Name, name, StringComparison.Ordinal)) return t;
		return null;
	}

	private static async Task WriteJson(HttpListenerResponse resp, object obj)
	{
		var json = JsonConvert.SerializeObject(obj);
		var bytes = Encoding.UTF8.GetBytes(json);
		resp.ContentType = "application/json";
		resp.ContentEncoding = Encoding.UTF8;
		resp.ContentLength64 = bytes.Length;
		await resp.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
		resp.OutputStream.Close();
	}
}
