using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using RimWorld;
using RimWorld.Planet;
using RimBridgeServer.Sdk;
using Verse;

namespace RimBridgeServer.MultiplayerTools;

public sealed class MultiplayerTools
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    [Tool("multiplayer/status", Description = "Read the optional Multiplayer session, native player states, desync flag and game tick. No connection or gameplay mutation. A started connection is not a joined session.")]
    public static Task<object> Status(IRimBridgeContext ctx) => ctx.MainThread.InvokeAsync(StatusCore);

    [Tool("multiplayer/host_local", Description = "Host a loaded single-player map or native saved replay through Multiplayer's own hosting flow. Binds only 127.0.0.1; Steam, LAN advertisement and arbiter are disabled. Returns initiation, not completed hosting. Poll multiplayer/status.")]
    public static Task<object> HostLocal(IRimBridgeContext ctx, string username = "FogHost", int port = 30502, bool syncConfigs = true, bool asyncTime = false, bool multifaction = false, bool desyncTraces = false)
        => ctx.MainThread.InvokeAsync<object>(() =>
        {
            ValidateConnection(username, port);
            var api = RequiredType("Multiplayer.Client.Multiplayer");
            var fromReplay = Read(api, null, "IsReplay") is true;
            if (Read(api, null, "session") != null && !fromReplay) return Failure("Leave the current Multiplayer session before hosting.");
            if (fromReplay && (Read(api, null, "LocalServer") != null || Get(Read(api, null, "session"), "desynced") is not false))
                return Failure("Load a non-desynced native saved replay without a server before hosting.");
            if (Current.ProgramState != ProgramState.Playing || Find.Maps.Count == 0 || Read(typeof(LongEventHandler), null, "currentEvent") != null)
                return Failure("Load a playable single-player map or native saved replay before hosting.");
            var settingsType = RequiredType("Multiplayer.Common.ServerSettings");
            var hostWindow = RequiredType("Multiplayer.Client.HostWindow");
            var host = hostWindow.GetMethod("HostProgrammatically", Flags, null, new[] { settingsType }, null)
                ?? throw new MissingMethodException("Multiplayer.HostWindow.HostProgrammatically");
            var start = fromReplay ? hostWindow.GetMethod("TryStartLocalServer", Flags, null, new[] { settingsType }, null)
                ?? throw new MissingMethodException("Multiplayer.HostWindow.TryStartLocalServer") : null;
            var replayHost = fromReplay ? RequiredType("Multiplayer.Client.HostUtil").GetMethod("HostServer", Flags, null, new[] { settingsType, typeof(bool) }, null)
                ?? throw new MissingMethodException("Multiplayer.HostUtil.HostServer") : null;
            var settings = Activator.CreateInstance(settingsType);
            Set(settings, "gameName", "RimBridge local test");
            Set(settings, "direct", true);
            Set(settings, "directAddress", "127.0.0.1:" + port);
            Set(settings, "lan", false);
            Set(settings, "steam", false);
            Set(settings, "arbiter", false);
            Set(settings, "syncConfigs", syncConfigs);
            Set(settings, "asyncTime", asyncTime);
            Set(settings, "multifaction", multifaction);
            Set(settings, "desyncTraces", desyncTraces);
            Set(settings, "pauseOnJoin", true);
            Set(settings, "pauseOnDesync", true);
            RequiredField(api, "username").SetValue(null, username);
            var accepted = (bool)(fromReplay ? start : host).Invoke(null, new[] { settings });
            if (accepted && fromReplay) replayHost.Invoke(null, new[] { settings, (object)true });
            return new { success = accepted, phase = accepted ? "hosting-started" : "hosting-rejected", address = "127.0.0.1", port, syncConfigs, asyncTime, multifaction, desyncTraces, fromReplay };
        });

    [Tool("multiplayer/join_local", Description = "Join a local Multiplayer host through its native LiteNet connector and connecting flow. Requires the main menu. Changes only the in-memory Multiplayer username. Returns initiation; poll multiplayer/status for native player states and desyncs.")]
    public static Task<object> JoinLocal(IRimBridgeContext ctx, string username = "FogClient", int port = 30502)
        => ctx.MainThread.InvokeAsync<object>(() =>
        {
            ValidateConnection(username, port);
            var api = RequiredType("Multiplayer.Client.Multiplayer");
            if (Read(api, null, "session") != null) return Failure("Leave the current Multiplayer session before joining.");
            if (Current.ProgramState != ProgramState.Entry || Current.Game != null)
                return Failure("Return to the main menu before joining.");
            var connectorType = RequiredType("Multiplayer.Client.Util.IConnector");
            var create = RequiredType("Multiplayer.Client.Util.ConnectorRegistry").GetMethod("LiteNet", Flags, null, new[] { typeof(string), typeof(int) }, null)
                ?? throw new MissingMethodException("Multiplayer.ConnectorRegistry.LiteNet");
            var connect = RequiredType("Multiplayer.Client.ClientUtil").GetMethod("TryConnectWithWindow", Flags, null, new[] { connectorType, typeof(bool) }, null)
                ?? throw new MissingMethodException("Multiplayer.ClientUtil.TryConnectWithWindow");
            var connector = create.Invoke(null, new object[] { "127.0.0.1", port });
            RequiredField(api, "username").SetValue(null, username);
            connect.Invoke(null, new[] { connector, (object)false });
            return new { success = true, phase = "joining-started", address = "127.0.0.1", port };
        });

    [Tool("multiplayer/leave", Description = "Stop the current Multiplayer session through its native cleanup and return to the main menu without saving. Does not shut down RimWorld or delete saves. Poll status before loading or joining again. An idle single-player game is left alone.")]
    public static Task<object> Leave(IRimBridgeContext ctx) => ctx.MainThread.InvokeAsync<object>(() =>
    {
        var api = RequiredType("Multiplayer.Client.Multiplayer");
        var disconnected = Find.WindowStack.Windows.Any(w => w.GetType().FullName == "Multiplayer.Client.DisconnectedWindow");
        if (Read(api, null, "session") == null && Read(api, null, "LocalServer") == null && !disconnected) return StatusCore();
        var stop = api.GetMethod("StopMultiplayerAndClearAllWindows", Flags, null, Type.EmptyTypes, null)
            ?? throw new MissingMethodException("Multiplayer.StopMultiplayerAndClearAllWindows");
        stop.Invoke(null, null);
        GenScene.GoToMainMenu();
        return new { success = true, phase = "leaving-started" };
    });

    [Tool("multiplayer/change_faction", Description = "Request an existing player faction through Multiplayer's native faction packet. Requires a joined, non-desynced live multifaction session. This submits the same request as Join faction; poll status for the result. Does not reassign pawns or create a faction.")]
    public static Task<object> ChangeFaction(IRimBridgeContext ctx, int factionId) => ctx.MainThread.InvokeAsync<object>(() =>
    {
        var api = RequiredType("Multiplayer.Client.Multiplayer");
        if (!LiveMultifaction(api)) return Failure("Join a live, non-desynced multifaction session first.");
        var faction = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(f => f.IsPlayer && f.loadID == factionId);
        if (faction == null) return Failure("Select an existing player faction from multiplayer/status.");
        var session = Read(api, null, "session");
        var client = Get(session, "client");
        var packetType = RequiredType("Multiplayer.Common.Networking.Packet.ClientSetFactionPacket");
        var packet = Activator.CreateInstance(packetType, Get(session, "playerId"), factionId);
        var send = client.GetType().GetMethods(Flags).Single(m => m.Name == "Send" && m.IsGenericMethodDefinition
            && m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType == typeof(bool));
        var map = Find.Maps.FirstOrDefault(m => m.ParentFaction == faction);
        if (map != null)
        {
            Current.Game.CurrentMap = map;
            Find.World.renderer.wantedMode = WorldRenderMode.None;
        }
        send.MakeGenericMethod(packetType).Invoke(client, new[] { packet, (object)true });
        return new { success = true, phase = "faction-request-submitted", factionId };
    });

    [Tool("multiplayer/open_faction_setup", Description = "Open Multiplayer's native second-colony setup pages with a unique faction name and Crashlanded scenario. Requires a live multifaction session. A negative tile selects a native random starting tile using isolated UI randomness. Finish the native ideology/pawn pages to submit synchronized creation; opening pages alone creates no faction.")]
    public static Task<object> OpenFactionSetup(IRimBridgeContext ctx, string factionName, int tileId = -1) => ctx.MainThread.InvokeAsync<object>(() =>
    {
        var api = RequiredType("Multiplayer.Client.Multiplayer");
        if (!LiveMultifaction(api)) return Failure("Join a live, non-desynced multifaction session first.");
        if (string.IsNullOrWhiteSpace(factionName) || factionName.Length > 40)
            throw new ArgumentException("Use a faction name of 1..40 characters.", nameof(factionName));
        if (Find.FactionManager.AllFactionsListForReading.Any(f => f.Name == factionName))
            return Failure("Use a unique faction name.");
        if (LongEventHandler.AnyEventNowOrWaiting || Find.WindowStack.Windows.Any(w => w is Page))
            return Failure("Finish the current long event or setup page first.");
        var sidebar = RequiredType("Multiplayer.Client.FactionSidebar");
        var validate = sidebar.GetMethod("FactionCreationCanBeStarted", Flags, null, Type.EmptyTypes, null)
            ?? throw new MissingMethodException("Multiplayer.FactionSidebar.FactionCreationCanBeStarted");
        var open = sidebar.GetMethod("OpenConfigurationPages", Flags, null, Type.EmptyTypes, null)
            ?? throw new MissingMethodException("Multiplayer.FactionSidebar.OpenConfigurationPages");
        Rand.PushState();
        try
        {
            Find.WorldInterface.SelectedTile = tileId < 0 ? TileFinder.RandomStartingTile() : (PlanetTile)tileId;
            RequiredField(sidebar, "factionNameTextField").SetValue(null, factionName);
            RequiredField(sidebar, "chosenScenario").SetValue(null, ScenarioDefOf.Crashlanded);
            if (validate.Invoke(null, null) is not true) return Failure("The native faction setup rejected the selected site.");
            open.Invoke(null, null);
        }
        finally { Rand.PopState(); }
        return new { success = true, phase = "faction-setup-opened", factionName, tileId = (int)Find.WorldInterface.SelectedTile };
    });

    [Tool("multiplayer/set_time_speed", Description = "Request Paused, Normal, Fast, Superfast or Ultrafast through Multiplayer's native synchronized time command. Requires a joined, non-desynced live session without lowest-wins voting. A negative mapId controls shared/world time; an explicit mapId controls that map in asynchronous time. Does not directly write TickManager or step one client. Returns submission; poll native clocks on both clients.")]
    public static Task<object> SetTimeSpeed(IRimBridgeContext ctx, string speed = "Normal", int mapId = -1) => ctx.MainThread.InvokeAsync<object>(() =>
    {
        if (!Enum.TryParse<TimeSpeed>(speed, true, out var parsed) || parsed < TimeSpeed.Paused || parsed > TimeSpeed.Ultrafast)
            throw new ArgumentException("Use Paused, Normal, Fast, Superfast or Ultrafast.", nameof(speed));
        var api = RequiredType("Multiplayer.Client.Multiplayer");
        var session = Read(api, null, "session");
        var client = Get(session, "client");
        if (client == null || Get(client, "State")?.ToString() != "ClientPlaying" || Get(session, "desynced") is not false || Read(api, null, "IsReplay") is not false)
            return Failure("Join a live, non-desynced Multiplayer session before changing time.");
        var gameComp = Get(Read(api, null, "game"), "gameComp");
        if (Get(gameComp, "IsLowestWins") is not false)
            return Failure("This control requires time without lowest-wins voting.");
        if (mapId >= 0 && Get(gameComp, "asyncTime") is not true)
            return Failure("An explicit map clock requires asynchronous time.");
        var tickable = mapId < 0 ? Read(api, null, "AsyncWorldTime")
            : MapClocks(api)?.FirstOrDefault(clock => (Get(clock, "map") as Map)?.uniqueID == mapId);
        if (tickable == null) return Failure("The requested native Multiplayer clock is unavailable.");
        var send = RequiredType("Multiplayer.Client.AsyncTime.MpTimeControls").GetMethod("SendTimeChange", Flags)
            ?? throw new MissingMethodException("Multiplayer.MpTimeControls.SendTimeChange");
        send.Invoke(null, new object[] { tickable, parsed });
        return new { success = true, phase = "time-command-submitted", requestedSpeed = parsed.ToString(), mapId };
    });

    [Tool("multiplayer/save", Description = "Save the current paused live Multiplayer game using its native save implementation. World and all asynchronous map clocks must be paused. Uses a new 1..30 character name and refuses existing files. Returns file verification; the save does not change or reload the game.")]
    public static Task<object> Save(IRimBridgeContext ctx, string saveName) => ctx.MainThread.InvokeAsync<object>(() =>
    {
        var api = RequiredType("Multiplayer.Client.Multiplayer");
        var session = Read(api, null, "session");
        var client = Get(session, "client");
        if (client == null || Get(client, "State")?.ToString() != "ClientPlaying" || Get(session, "desynced") is not false || Read(api, null, "IsReplay") is not false)
            return Failure("Join a live, non-desynced Multiplayer session before saving.");
        var game = Read(api, null, "game");
        if (Get(Get(game, "asyncWorldTimeComp"), "DesiredTimeSpeed")?.ToString() != "Paused")
            return Failure("Pause the native Multiplayer session before saving.");
        if (Get(Get(game, "gameComp"), "asyncTime") is true
            && (MapClocks(api) is not { } clocks || clocks.Any(clock => Get(clock, "DesiredTimeSpeed")?.ToString() != "Paused")))
            return Failure("Pause every native Multiplayer map clock before saving.");
        var file = SaveFile(api, saveName);
        if (file.Exists || File.Exists(Path.Combine(file.DirectoryName, saveName + ".tmp.zip")))
            return Failure("Use a new Multiplayer save name; existing files are preserved.");
        var save = RequiredType("Multiplayer.Client.Autosaving").GetMethod("SaveGameToFile_Overwrite", Flags, null, new[] { typeof(string), typeof(bool) }, null)
            ?? throw new MissingMethodException("Multiplayer.Autosaving.SaveGameToFile_Overwrite");
        save.Invoke(null, new object[] { saveName, false });
        file.Refresh();
        return new { success = file.Exists && file.Length > 0, phase = "save-verified", saveName, path = file.FullName, bytes = file.Exists ? file.Length : 0L };
    });

    [Tool("multiplayer/load_save", Description = "Load an existing native Multiplayer ZIP through its replay loader at the saved endpoint. Requires the main menu with no session. Returns initiation, not completed load; poll status for replay/game state and ticks. Does not host or overwrite the save.")]
    public static Task<object> LoadSave(IRimBridgeContext ctx, string saveName) => ctx.MainThread.InvokeAsync<object>(() =>
    {
        var api = RequiredType("Multiplayer.Client.Multiplayer");
        if (Read(api, null, "session") != null || Current.ProgramState != ProgramState.Entry || Current.Game != null)
            return Failure("Return to the main menu without a Multiplayer session before loading.");
        var file = SaveFile(api, saveName);
        if (!file.Exists) return Failure("The native Multiplayer save does not exist.");
        var load = RequiredType("Multiplayer.Client.Replay").GetMethod("LoadReplay", Flags, null,
            new[] { typeof(FileInfo), typeof(bool), typeof(Action), typeof(Action), typeof(string), typeof(bool) }, null)
            ?? throw new MissingMethodException("Multiplayer.Replay.LoadReplay");
        load.Invoke(null, new object[] { file, true, null, null, "MpLoading", false });
        return new { success = true, phase = "save-loading-started", saveName, path = file.FullName };
    });

    private static FileInfo SaveFile(Type api, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 30 || name.Any(c => !(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') && c != '_' && c != '-'))
            throw new ArgumentException("Use a save name of 1..30 ASCII letters, numbers, underscores or hyphens.", nameof(name));
        return new FileInfo(Path.Combine((string)Read(api, null, "ReplaysDir"), name + ".zip"));
    }

    private static object StatusCore()
    {
        var api = FindType("Multiplayer.Client.Multiplayer");
        if (api == null) return new { success = true, available = false, sessionActive = false };
        var session = Read(api, null, "session");
        var localServer = Read(api, null, "LocalServer");
        var gameComp = Get(Read(api, null, "game"), "gameComp");
        var client = Get(session, "client");
        var players = (Get(session, "players") as IEnumerable)?.Cast<object>().Select(p => new
        {
            id = Get(p, "id"), username = Get(p, "username"), status = Get(p, "status")?.ToString(),
            type = Get(p, "type")?.ToString(), factionId = Get(p, "factionId")
        }).ToArray();
        return new
        {
            success = true, available = true, assemblyMvid = api.Module.ModuleVersionId.ToString(),
            sessionActive = session != null, username = Read(api, null, "username"),
            isReplay = session == null ? (bool?)null : (bool)Read(api, null, "IsReplay"),
            gameName = Get(session, "gameName"), playerId = Get(session, "playerId"),
            myFactionId = Get(session, "myFactionId"), desynced = Get(session, "desynced"),
            multifaction = Get(gameComp, "multifaction"), asyncTime = Get(gameComp, "asyncTime"),
            currentMapId = Current.Game?.CurrentMap?.uniqueID,
            worldTicks = Get(Get(Read(api, null, "game"), "asyncWorldTimeComp"), "worldTicks"),
            mapClocks = MapClocks(api)?.Select(clock => new
            {
                mapId = (Get(clock, "map") as Map)?.uniqueID,
                ticks = Get(clock, "mapTicks"), desiredTimeSpeed = Get(clock, "DesiredTimeSpeed")?.ToString(),
                nativeRateMultiplier = clock.GetType().GetMethod("TickRateMultiplier", new[] { typeof(TimeSpeed) })
                    ?.Invoke(clock, new[] { Get(clock, "DesiredTimeSpeed") })
            }).ToArray(),
            factions = Current.Game == null || Find.World == null ? null : Find.FactionManager.AllFactionsListForReading.Where(f => f.IsPlayer)
                .Select(f => new { id = f.loadID, name = f.Name }).ToArray(),
            desyncTraces = Get(Get(Read(api, null, "game"), "gameComp"), "logDesyncTraces"),
            clientType = client?.GetType().FullName, players,
            hosting = localServer != null,
            hostReady = Get(localServer, "running") is true && Get(session, "dataSnapshot") != null,
            desiredTimeSpeed = Get(Get(Read(api, null, "game"), "asyncWorldTimeComp"), "DesiredTimeSpeed")?.ToString(),
            programState = Current.ProgramState.ToString(), mapCount = Current.Game == null ? 0 : Find.Maps.Count,
            ticksGame = Current.Game == null ? (int?)null : Find.TickManager?.TicksGame,
            windows = Find.WindowStack?.Windows.Select(w => w.GetType().FullName).ToArray()
        };
    }

    private static bool LiveMultifaction(Type api)
    {
        var session = Read(api, null, "session");
        return Current.ProgramState == ProgramState.Playing && Get(Get(session, "client"), "State")?.ToString() == "ClientPlaying"
            && Get(session, "desynced") is false && Read(api, null, "IsReplay") is false
            && Get(Get(Read(api, null, "game"), "gameComp"), "multifaction") is true;
    }

    private static object[] MapClocks(Type api) => (Get(Read(api, null, "game"), "asyncTimeComps") as IEnumerable)?.Cast<object>().ToArray();

    private static Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies()
        .Where(a => !a.IsDynamic && !a.ReflectionOnly && (a.GetName().Name == "Multiplayer" || a.GetName().Name == "MultiplayerCommon"))
        .Select(a => a.GetType(name, false)).FirstOrDefault(t => t != null);

    private static Type RequiredType(string name) => FindType(name) ?? throw new InvalidOperationException("Multiplayer is unavailable or lacks " + name);
    private static FieldInfo RequiredField(Type type, string name) => type.GetField(name, Flags) ?? throw new MissingFieldException(type.FullName, name);
    private static void Set(object target, string name, object value) => RequiredField(target.GetType(), name).SetValue(target, value);
    private static object Get(object target, string name) => target == null ? null : Read(target.GetType(), target, name);
    private static object Read(Type type, object target, string name)
        => type.GetField(name, Flags)?.GetValue(target) ?? type.GetProperty(name, Flags)?.GetValue(target, null);
    private static object Failure(string message) => new { success = false, message };
    private static void ValidateConnection(string username, int port)
    {
        if (string.IsNullOrWhiteSpace(username) || username.Length > 15 || username.Any(c => !char.IsLetterOrDigit(c) && c != '_'))
            throw new ArgumentException("Use a username of 1..15 letters, numbers or underscores.", nameof(username));
        if (port < 1024 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port), "Use port 1024..65535.");
    }
}
