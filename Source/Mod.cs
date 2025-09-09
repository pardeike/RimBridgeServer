using Verse;

namespace RimBridgeServer
{
	public class RimBridgeServerMod : Mod
	{
		public RimBridgeServerMod(ModContentPack content) : base(content)
		{
			Log.Message($"[RimBridgeServer] Loaded v{GetType().Assembly.GetName().Version}");
		}
	}

	[StaticConstructorOnStartup]
	static class Startup
	{
		static Startup()
		{
			// Initialization code at game startup goes here.
			// For now, we just confirm the static constructor runs.
			Log.Message("[RimBridgeServer] Startup initialized.");
		}
	}
}

