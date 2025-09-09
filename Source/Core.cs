using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace RimBridgeServer;

public sealed class SimpleLogger : ILogger
{
	public void Info(string msg) => Verse.Log.Message($"[RimBridge] {msg}");
	public void Warn(string msg) => Verse.Log.Warning($"[RimBridge] {msg}");
	public void Error(string msg) => Verse.Log.Error($"[RimBridge] {msg}");
}

public sealed class GameThreadDispatcher : IMainThreadDispatcher
{
	private readonly ConcurrentQueue<Action> _q = new();
	public void Post(Action action) => _q.Enqueue(action);
	// Call this from a GameComponent update (see §5.6)
	internal void Drain()
	{
		while (_q.TryDequeue(out var act))
			try { act(); } catch (Exception e) { Verse.Log.Error($"[RimBridge] dispatcher: {e}"); }
	}
}

public sealed class PluginManager(IMainThreadDispatcher dispatcher, ILogger logger) : IMcpServerApi
{
	private readonly IDictionary<string, IMcpTool> _tools = new Dictionary<string, IMcpTool>(StringComparer.Ordinal);
	public IEnumerable<IMcpTool> Tools => _tools.Values;

	public IMainThreadDispatcher Dispatcher { get; } = dispatcher;
	public ILogger Logger { get; } = logger;

	public void RegisterTool(IMcpTool tool)
	{
		if (_tools.ContainsKey(tool.Name))
			throw new InvalidOperationException($"Duplicate tool name: {tool.Name}");
		_tools[tool.Name] = tool;
	}

	public void DiscoverAndLoad()
	{
		// Scan all loaded assemblies for [McpPlugin] classes
		var asms = AppDomain.CurrentDomain.GetAssemblies();
		foreach (var asm in asms)
		{
			Type[] types;
			try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException ex) { types = [.. ex.Types.Where(t => t != null)]; }
			foreach (var t in types)
			{
				if (t == null || t.IsAbstract) continue;
				if (typeof(IMcpPlugin).IsAssignableFrom(t) &&
					 t.GetCustomAttributes(typeof(McpPluginAttribute), inherit: false).Any())
					try
					{
						var plugin = (IMcpPlugin)Activator.CreateInstance(t);
						plugin.Initialize(this);
						foreach (var tool in plugin.GetTools()) RegisterTool(tool);
						Logger.Info($"Loaded plugin {plugin.Id} {plugin.Version}");
					}
					catch (Exception e)
					{
						Logger.Error($"Failed to load plugin {t.FullName}: {e}");
					}
			}
		}
	}
}
