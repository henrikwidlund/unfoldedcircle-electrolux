namespace UnfoldedCircle.Electrolux.Logging;

internal static partial class IntegrationLogger
{
    [LoggerMessage(EventId = 1, EventName = nameof(NoConfigurationsFound), Level = LogLevel.Information,
        Message = "[{WSId}] WS: No configurations found")]
    public static partial void NoConfigurationsFound(this ILogger logger, string wsId);

    [LoggerMessage(EventId = 2, EventName = nameof(AddingConfigurationForDevice), Level = LogLevel.Information,
        Message = "Adding configuration for device ID '{EntityId}'")]
    public static partial void AddingConfigurationForDevice(this ILogger logger, string entityId);

    private static readonly Action<ILogger, string, string, Exception> FailureDuringEventAction = LoggerMessage.Define<string, string>(
        LogLevel.Error,
        new EventId(3, nameof(FailureDuringEvent)),
        "{WSId} Failure during event for {Key}.");

    public static void FailureDuringEvent(this ILogger logger, Exception exception, string wsId, string key) =>
        FailureDuringEventAction(logger, wsId, key, exception);

    [LoggerMessage(EventId = 4, EventName = nameof(CouldNotGetApplianceState), Level = LogLevel.Information,
        Message = "[{WSId}] Could not get appliance state for entity ID '{EntityId}'")]
    public static partial void CouldNotGetApplianceState(this ILogger logger, string wsId, string entityId);
}