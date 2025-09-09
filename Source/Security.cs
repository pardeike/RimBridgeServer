using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace RimBridgeServer;

public static class ApiKeys
{
	/// <summary>
	/// Tries to read ~/.api-keys (JSON) and return the value at top-level key "RIMBRIDGE_TOKEN".
	/// Returns true if a non-empty token was found.
	/// </summary>
	public static bool TryGetRimBridgeToken(out string token)
	{
		token = null;
		try
		{
			var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			if (string.IsNullOrEmpty(home)) home = Environment.GetEnvironmentVariable("HOME");
			if (string.IsNullOrEmpty(home)) return false;

			var path = Path.Combine(home, ".api-keys");
			if (!File.Exists(path)) return false;

			var json = File.ReadAllText(path);
			var obj = JObject.Parse(json);
			var val = obj.Value<string>("RIMBRIDGE_TOKEN");
			if (!string.IsNullOrWhiteSpace(val))
			{
				token = val.Trim();
				return true;
			}
		}
		catch
		{
			// Ignore; caller can decide how to proceed without a token
		}
		return false;
	}
}

