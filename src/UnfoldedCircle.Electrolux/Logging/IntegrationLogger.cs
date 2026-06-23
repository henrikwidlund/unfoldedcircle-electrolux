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

    private static readonly Action<ILogger, Exception> FailureGetLiveStreamAction = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(4, nameof(FailureGetLiveStream)),
        "Failure to get live stream.");

    public static void FailureGetLiveStream(this ILogger logger, Exception exception) =>
        FailureGetLiveStreamAction(logger, exception);

    private static readonly Action<ILogger, string, Exception> FailureDuringBroadcastAction = LoggerMessage.Define<string>(
        LogLevel.Error,
        new EventId(5, nameof(FailureDuringBroadcast)),
        "[{WSId}] Failure during live stream broadcast.");

    public static void FailureDuringBroadcast(this ILogger logger, Exception exception, string wsId) =>
        FailureDuringBroadcastAction(logger, wsId, exception);

    [LoggerMessage(EventId = 6, EventName = nameof(TokenResultNullDuringBackup), Level = LogLevel.Error,
        Message = "TokenResult was null during backup creation.")]
    public static partial void TokenResultNullDuringBackup(this ILogger logger);

    [LoggerMessage(EventId = 7, EventName = nameof(BackupDataNullDuringRestore), Level = LogLevel.Error,
        Message = "[{WSId}] BackupData null during restore.")]
    public static partial void BackupDataNullDuringRestore(this ILogger logger, string wsId);

    private static readonly Action<ILogger, string, Exception> ExceptionDuringRestoreAction = LoggerMessage.Define<string>(
        LogLevel.Error,
        new EventId(8, nameof(ExceptionDuringRestore)),
        "[{WSId}] Exception during restore.");

    public static void ExceptionDuringRestore(this ILogger logger, Exception exception, string wsId) =>
        ExceptionDuringRestoreAction(logger, wsId, exception);

    [LoggerMessage(EventId = 9, EventName = nameof(NullLiveStream), Level = LogLevel.Warning,
        Message = "[{WSId}] Live stream was null, retrying after delay.")]
    public static partial void NullLiveStream(this ILogger logger, string wsId);

    [LoggerMessage(EventId = 10, EventName = nameof(ReceivedLiveStreamEvent), Level = LogLevel.Trace,
        Message = "Received live stream event {type}: {data}")]
    public static partial void ReceivedLiveStreamEvent(this ILogger logger, string type, string data);

    [LoggerMessage(EventId = 11, EventName = nameof(LiveStreamEnded), Level = LogLevel.Information,
        Message = "[{WSId}] Live stream was null, retrying after delay.")]
    public static partial void LiveStreamEnded(this ILogger logger, string wsId);
}
