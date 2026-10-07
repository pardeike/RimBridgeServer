using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using RimBridgeServer.Sdk;
using Verse;

namespace RimBridgeServer.MultiplayerTools;

/// <summary>Opt-in loader evidence. Never resolves or changes an assembly.</summary>
public sealed class LoadingDiagnostics
{
    private static readonly object gate = new();
    private static readonly List<object> requests = new();
    private static bool active;
    private static int overflow;

    [Tool("multiplayer/loading_diagnostics", Description = "Start at the main menu, reproduce loading, then read status and stop. Records up to 16 reflection-only InputLegacyModule dependency requests with requesting assembly and managed stack. Returns null to preserve native resolution/failure; does not preload or fix anything. Works without Multiplayer. Excluded from player ZIPs.")]
    public static Task<object> Inspect(IRimBridgeContext ctx, string action = "status") => ctx.MainThread.InvokeAsync<object>(() =>
    {
        lock (gate)
        {
            if (action == "start")
            {
                if (Current.Game != null)
                    throw new InvalidOperationException("Start loader diagnostics at the main menu.");
                requests.Clear();
                overflow = 0;
                if (!active)
                    AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += Record;
                active = true;
            }
            else if (action == "stop")
            {
                if (active)
                    AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve -= Record;
                active = false;
            }
            else if (action != "status")
                throw new ArgumentException("Use start, status or stop.", nameof(action));
            return new { success = true, active, overflow, requests = requests.ToArray() };
        }
    });

    private static Assembly Record(object sender, ResolveEventArgs args)
    {
        if (!args.Name.StartsWith("UnityEngine.InputLegacyModule,", StringComparison.Ordinal))
            return null;
        lock (gate)
        {
            if (!active)
                return null;
            if (requests.Count == 16)
                overflow++;
            else
                requests.Add(new
                {
                    dependency = args.Name,
                    requestingAssembly = args.RequestingAssembly?.FullName,
                    reflectionOnly = args.RequestingAssembly?.ReflectionOnly,
                    // Avoid formatting parameter types while the runtime is
                    // resolving an assembly dependency; that can recurse.
                    stack = new StackTrace(1, false).GetFrames()?.Select(frame =>
                    {
                        var method = frame.GetMethod();
                        return method?.DeclaringType?.FullName + "." + method?.Name;
                    }).ToArray()
                });
        }
        return null;
    }
}
