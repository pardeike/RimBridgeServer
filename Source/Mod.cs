using Verse;

namespace RimBridgeServer;

public class RimBridgeServerMod : Mod
{
	private static McpHttpServer _server;

    public RimBridgeServerMod(ModContentPack content) : base(content)
    {
        if (_server == null)
        {
            var opts = new McpServerOptions
            {
                Prefixes = ["http://127.0.0.1:5174/mcp/"]
            };

            if (ApiKeys.TryGetRimBridgeToken(out var token))
            {
                opts.RequireBearerToken = true;
                opts.StaticBearerToken = token;
            }

            _server = new McpHttpServer(opts);
            _server.Start();
        }
    }
}

public sealed class RimBridgeGameComponent : GameComponent
{
	private static GameThreadDispatcher _dispatcher;
	public static void InstallDispatcher(GameThreadDispatcher d) => _dispatcher = d;
	//public RimBridgeGameComponent(Game game) { }
	public override void GameComponentUpdate() => _dispatcher?.Drain();
}
