using System;
using RimBridgeServer.Core;
using UnityEngine;

namespace RimBridgeServer;

internal static class RimBridgeLogs
{
    private static LogJournal _journal;

    public static void Initialize(LogJournal journal)
    {
        if (_journal != null)
            return;

        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        Application.logMessageReceivedThreaded += HandleLogMessage;
    }

    private static void HandleLogMessage(string condition, string stackTrace, LogType type)
    {
        _journal?.Record(MapLevel(type), condition, stackTrace, source: "unity");
    }

    private static string MapLevel(LogType type)
    {
        return type switch
        {
            LogType.Warning => "warning",
            LogType.Error => "error",
            LogType.Assert => "error",
            LogType.Exception => "fatal",
            _ => "info"
        };
    }
}
