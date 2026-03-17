namespace RimBridgeServer;

internal sealed class NotificationCapabilityModule
{
    public object ListMessages(int limit = 12)
    {
        return RimWorldNotifications.ListMessagesResponse(limit);
    }

    public object ListLetters(int limit = 40)
    {
        return RimWorldNotifications.ListLettersResponse(limit);
    }

    public object OpenLetter(string letterId)
    {
        return RimWorldNotifications.OpenLetterResponse(letterId);
    }

    public object DismissLetter(string letterId)
    {
        return RimWorldNotifications.DismissLetterResponse(letterId);
    }

    public object ListAlerts(int limit = 40)
    {
        return RimWorldNotifications.ListAlertsResponse(limit);
    }

    public object ActivateAlert(string alertId)
    {
        return RimWorldNotifications.ActivateAlertResponse(alertId);
    }
}
