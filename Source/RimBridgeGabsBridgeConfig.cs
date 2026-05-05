using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace RimBridgeServer;

internal sealed class RimBridgeGabsBridgeConfig
{
    public int Port { get; private set; }

    public string Token { get; private set; }

    public string GameId { get; private set; }

    public static bool TryRead(string gameId, out RimBridgeGabsBridgeConfig config, out string error)
    {
        config = null;
        error = null;

        if (string.IsNullOrWhiteSpace(gameId))
        {
            error = "game id is empty";
            return false;
        }

        var configDirectory = Environment.GetEnvironmentVariable("GABS_CONFIG_DIR");
        if (string.IsNullOrWhiteSpace(configDirectory))
        {
            var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(homeDirectory))
                homeDirectory = Environment.GetEnvironmentVariable("HOME");
            if (string.IsNullOrWhiteSpace(homeDirectory))
            {
                error = "home directory could not be resolved";
                return false;
            }

            configDirectory = Path.Combine(homeDirectory, ".gabs");
        }

        var bridgePath = Path.Combine(configDirectory, gameId, "bridge.json");
        if (!File.Exists(bridgePath))
            return false;

        try
        {
            var json = JObject.Parse(File.ReadAllText(bridgePath));
            var port = json.Value<int?>("port") ?? 0;
            var token = json.Value<string>("token");
            var configuredGameId = json.Value<string>("gameId");

            if (port <= 0)
            {
                error = $"invalid port in {bridgePath}";
                return false;
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                error = $"missing token in {bridgePath}";
                return false;
            }

            config = new RimBridgeGabsBridgeConfig
            {
                Port = port,
                Token = token,
                GameId = string.IsNullOrWhiteSpace(configuredGameId) ? gameId : configuredGameId
            };
            return true;
        }
        catch (Exception ex)
        {
            error = $"{bridgePath}: {ex.Message}";
            return false;
        }
    }
}
