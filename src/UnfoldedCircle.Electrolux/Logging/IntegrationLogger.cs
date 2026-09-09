namespace UnfoldedCircle.Electrolux.Logging;

internal static partial class IntegrationLogger
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "[{WSId}] WS: No configurations found")]
    public static partial void NoConfigurationsFound(this ILogger logger, string wsId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Adding configuration for device ID '{EntityId}'")]
    public static partial void AddingConfigurationForDevice(this ILogger logger, string entityId);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "Failure to get live stream.")]
    public static partial void FailureGetLiveStream(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "[{WSId}] Failure during live stream broadcast.")]
    public static partial void FailureDuringBroadcast(this ILogger logger, string wsId, Exception exception);

    [LoggerMessage(EventId = 6, Level = LogLevel.Error, Message = "TokenResult was null during backup creation.")]
    public static partial void TokenResultNullDuringBackup(this ILogger logger);

    [LoggerMessage(EventId = 7, Level = LogLevel.Error, Message = "[{WSId}] BackupData null during restore.")]
    public static partial void BackupDataNullDuringRestore(this ILogger logger, string wsId);

    [LoggerMessage(EventId = 8, Level = LogLevel.Error, Message = "[{WSId}] Exception during restore.")]
    public static partial void ExceptionDuringRestore(this ILogger logger, string wsId, Exception exception);

    [LoggerMessage(EventId = 9, Level = LogLevel.Warning, Message = "[{WSId}] Live stream was null, retrying after delay.")]
    public static partial void NullLiveStream(this ILogger logger, string wsId);

    [LoggerMessage(EventId = 10, Level = LogLevel.Trace, Message = "Received live stream event {type}: {data}")]
    public static partial void ReceivedLiveStreamEvent(this ILogger logger, string type, string data);

    [LoggerMessage(EventId = 11, Level = LogLevel.Information, Message = "[{WSId}] Live stream was null, retrying after delay.")]
    public static partial void LiveStreamEnded(this ILogger logger, string wsId);
}
