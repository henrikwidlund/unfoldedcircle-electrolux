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

    [LoggerMessage(EventId = 4, EventName = nameof(FailedToGetLiveStream), Level = LogLevel.Error,
        Message = "[{WSId}] Failed to get live stream")]
    public static partial void FailedToGetLiveStream(this ILogger logger, string wsId);

    private static readonly Action<ILogger, Exception> FailureGetLiveStreamAction = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(5, nameof(FailureGetLiveStream)),
        "Failure to get live stream.");

    public static void FailureGetLiveStream(this ILogger logger, Exception exception) =>
        FailureGetLiveStreamAction(logger, exception);

    private static readonly Action<ILogger, string, Exception> FailureDuringBroadcastAction = LoggerMessage.Define<string>(
        LogLevel.Error,
        new EventId(6, nameof(FailureDuringBroadcast)),
        "[{WSId}] Failure during live stream broadcast.");

    public static void FailureDuringBroadcast(this ILogger logger, Exception exception, string wsId) =>
        FailureDuringBroadcastAction(logger, wsId, exception);
}