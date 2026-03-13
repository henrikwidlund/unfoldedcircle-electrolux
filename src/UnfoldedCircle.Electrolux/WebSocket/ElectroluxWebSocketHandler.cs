using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;
using System.Runtime.CompilerServices;

using Microsoft.Extensions.Options;

using UnfoldedCircle.Electrolux.Configuration;
using UnfoldedCircle.Electrolux.Http;
using UnfoldedCircle.Electrolux.Logging;
using UnfoldedCircle.Electrolux.Response;
using UnfoldedCircle.Models.Events;
using UnfoldedCircle.Models.Shared;
using UnfoldedCircle.Models.Sync;
using UnfoldedCircle.Server.Configuration;
using UnfoldedCircle.Server.DependencyInjection;
using UnfoldedCircle.Server.Extensions;
using UnfoldedCircle.Server.Response;
using UnfoldedCircle.Server.WebSocket;

namespace UnfoldedCircle.Electrolux.WebSocket;

internal sealed class ElectroluxWebSocketHandler(
    IConfigurationService<UnfoldedCircleConfigurationItem> configurationService,
    ElectroluxClient electroluxClient,
    IOptions<UnfoldedCircleOptions> options,
    ILogger<ElectroluxWebSocketHandler> logger) : UnfoldedCircleWebSocketHandler<MediaPlayerCommandId, UnfoldedCircleConfigurationItem>(configurationService, options, logger)
{
    private readonly ElectroluxClient _electroluxClient = electroluxClient;
    private static readonly ClimateOptions ClimateOptions = new() { TemperatureUnit = TemperatureUnit.Celsius };
    private static readonly string[] SelectOptions = ["1", "2", "3", "4", "5", "6", "7", "8", "9"];
    private const int SelectLevelsMaxIndex = 8;
    private static readonly ConcurrentDictionary<string, string> EntityIdSelectedOption = new(StringComparer.OrdinalIgnoreCase);

    protected override ValueTask<EntityCommandResult> OnRemoteCommandAsync(
        System.Net.WebSockets.WebSocket socket,
        RemoteEntityCommandMsgData payload,
        string command,
        string wsId,
        CancellationTokenWrapper cancellationTokenWrapper,
        CancellationToken commandCancellationToken) =>
        ValueTask.FromResult(EntityCommandResult.Failure);

    protected override async ValueTask<EntityCommandResult> OnClimateHvacModeCommandAsync(System.Net.WebSockets.WebSocket socket, ClimateEntityCommandMsgData payload, HvacMode hvacMode, string wsId, CancellationTokenWrapper cancellationTokenWrapper,
        CancellationToken commandCancellationToken)
    {
        var identifier = payload.MsgData.EntityId.GetBaseIdentifier();
        var workMode = hvacMode switch
        {
            HvacMode.Auto => WorkMode.Auto,
            HvacMode.Fan => WorkMode.Manual,
            HvacMode.Off => WorkMode.PowerOff,
            _ => throw new ArgumentOutOfRangeException(nameof(hvacMode), hvacMode, null)
        };
        await _electroluxClient.SendCommandAsync(identifier, workMode, null, commandCancellationToken);
        return EntityCommandResult.Other;
    }

    protected override async ValueTask<EntityCommandResult> OnClimatePowerCommandAsync(System.Net.WebSockets.WebSocket socket, ClimateEntityCommandMsgData payload, bool powerOn, string wsId, CancellationTokenWrapper cancellationTokenWrapper,
        CancellationToken commandCancellationToken)
    {
        var identifier = payload.MsgData.EntityId.GetBaseIdentifier();
        await _electroluxClient.SendCommandAsync(identifier, powerOn ? WorkMode.Manual : WorkMode.PowerOff, null, commandCancellationToken);
        return powerOn ? EntityCommandResult.PowerOn : EntityCommandResult.PowerOff;
    }

    protected override ValueTask<EntityCommandResult> OnClimateTargetTemperatureCommandAsync(
        System.Net.WebSockets.WebSocket socket,
        ClimateEntityCommandMsgData payload,
        float targetTemperature,
        string wsId,
        CancellationTokenWrapper cancellationTokenWrapper,
        CancellationToken commandCancellationToken) =>
        ValueTask.FromResult(EntityCommandResult.Other);

    protected override async ValueTask<SelectCommandResult> OnSelectOptionCommandAsync(System.Net.WebSockets.WebSocket socket, SelectEntityCommandMsgData payload, string option, string wsId, CancellationTokenWrapper cancellationTokenWrapper,
        CancellationToken commandCancellationToken)
    {
        if (!SelectOptions.Contains(option, StringComparer.OrdinalIgnoreCase))
            return new SelectCommandResult(EntityCommandResult.Failure, string.Empty);

        var identifier = payload.MsgData.EntityId.GetBaseIdentifier();
        await _electroluxClient.SendCommandAsync(identifier, WorkMode.Manual, sbyte.Parse(option, NumberFormatInfo.InvariantInfo), commandCancellationToken);
        EntityIdSelectedOption[payload.MsgData.EntityId] = option;
        return new SelectCommandResult(EntityCommandResult.Other, option);
    }

    protected override async ValueTask<SelectCommandResult> OnSelectFirstLastCommandAsync(System.Net.WebSockets.WebSocket socket, SelectEntityCommandMsgData payload, bool first, string wsId, CancellationTokenWrapper cancellationTokenWrapper,
        CancellationToken commandCancellationToken)
    {
        var option = first ? SelectOptions[0] : SelectOptions[^1];
        var identifier = payload.MsgData.EntityId.GetBaseIdentifier();
        await _electroluxClient.SendCommandAsync(identifier, WorkMode.Manual, sbyte.Parse(option, NumberFormatInfo.InvariantInfo), commandCancellationToken);
        EntityIdSelectedOption[payload.MsgData.EntityId] = option;
        return new SelectCommandResult(EntityCommandResult.Other, option);
    }

    protected override async ValueTask<SelectCommandResult> OnSelectNextPreviousCommandAsync(System.Net.WebSockets.WebSocket socket, SelectEntityCommandMsgData payload, bool next, bool cycle, string wsId,
        CancellationTokenWrapper cancellationTokenWrapper, CancellationToken commandCancellationToken)
    {
        var identifier = payload.MsgData.EntityId.GetBaseIdentifier();
        if (EntityIdSelectedOption.TryGetValue(payload.MsgData.EntityId, out var option))
        {
            var index = SelectOptions.IndexOf(option);
            index += next ? 1 : -1;
            switch (index)
            {
                case < 0 or > SelectLevelsMaxIndex when !cycle:
                    // do nothing
                    return new SelectCommandResult(EntityCommandResult.Other, string.Empty);
                case > SelectLevelsMaxIndex:
                    index = 0;
                    break;
                case < 0:
                    index = cycle ? SelectOptions.Length - 1 : 0;
                    break;
            }
            option = SelectOptions[index];
            await _electroluxClient.SendCommandAsync(identifier, WorkMode.Manual, sbyte.Parse(option, NumberFormatInfo.InvariantInfo), commandCancellationToken);
            EntityIdSelectedOption[payload.MsgData.EntityId] = option;
            return new SelectCommandResult(EntityCommandResult.Other, option);
        }

        option = SelectOptions[0];
        await _electroluxClient.SendCommandAsync(identifier, WorkMode.Manual, sbyte.Parse(option, NumberFormatInfo.InvariantInfo), commandCancellationToken);
        EntityIdSelectedOption[payload.MsgData.EntityId] = option;
        return new SelectCommandResult(EntityCommandResult.Other, option);
    }

    protected override async ValueTask<bool> IsEntityReachableAsync(string wsId, string entityId, CancellationToken cancellationToken)
        => await _electroluxClient.GetApplianceStateAsync(entityId.GetBaseIdentifier(), cancellationToken) is not null;

    protected override ValueTask<EntityCommandResult> OnMediaPlayerCommandAsync(System.Net.WebSockets.WebSocket socket,
        MediaPlayerEntityCommandMsgData<MediaPlayerCommandId> payload,
        string wsId,
        CancellationTokenWrapper cancellationTokenWrapper,
        CancellationToken commandCancellationToken)
        => ValueTask.FromResult(EntityCommandResult.Failure);

    protected override ValueTask OnConnectAsync(ConnectEvent payload, string wsId, CancellationToken cancellationToken)
        => ValueTask.CompletedTask;

    protected override ValueTask<bool> OnDisconnectAsync(DisconnectEvent payload, string wsId, CancellationToken cancellationToken)
        => ValueTask.FromResult(true);

    protected override ValueTask OnAbortDriverSetupAsync(AbortDriverSetupEvent payload, string wsId, CancellationToken cancellationToken)
        => ValueTask.CompletedTask;

    protected override ValueTask OnEnterStandbyAsync(EnterStandbyEvent payload, string wsId, CancellationToken cancellationToken)
        => ValueTask.CompletedTask;

    protected override ValueTask OnExitStandbyAsync(ExitStandbyEvent payload, string wsId, CancellationToken cancellationToken)
        => ValueTask.CompletedTask;

    protected override async Task HandleEventUpdatesAsync(System.Net.WebSockets.WebSocket socket, string wsId, SubscribedEntitiesHolder subscribedEntitiesHolder, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var liveStream = await _electroluxClient.GetLiveStreamAsync(cancellationToken);
                if (liveStream is null)
                {
                    _logger.FailedToGetLiveStream(wsId);
                    // treat null livestream as transient: wait a bit before trying again
                    await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                    continue;
                }

                await using var stream = await liveStream.GetStreamAsync(cancellationToken);
                await foreach (var liveStreamEvent in ElectroluxClient.GetLiveStreamEventsAsync(stream, cancellationToken))
                {
                    if (subscribedEntitiesHolder.SubscribedEntities.TryGetValue(liveStreamEvent.ApplianceId, out var subscribedEntities))
                    {
                        await HandleElectroluxEvent(socket, wsId, subscribedEntities, liveStreamEvent, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Normal shutdown: cancellation requested, exit without logging an error or delaying.
                return;
            }
            catch (Exception e)
            {
                _logger.FailureDuringBroadcast(e, wsId);
                // something went wrong during the live stream, wait a bit before trying again to avoid tight error loops
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Task HandleElectroluxEvent(System.Net.WebSockets.WebSocket socket, string wsId, HashSet<SubscribedEntity> subscribedEntities, LiveStreamEvent liveStreamEvent, CancellationToken cancellationToken)
    {
        return liveStreamEvent.Property switch
        {
            "Workmode" => SendClimateAsync(socket, wsId, subscribedEntities.FirstOrDefault(static x => x.EntityType == EntityType.Climate)?.EntityId,
                ((LiveStreamEvent<string>)liveStreamEvent).Value, null, cancellationToken),
            "Fanspeed" => SendSelectAsync(socket, wsId, subscribedEntities.FirstOrDefault(static x => x.EntityType == EntityType.Select)?.EntityId,
                (sbyte?)((LiveStreamEvent<int>)liveStreamEvent).Value, cancellationToken),
            "TVOC" => SendTvocSensorAsync(socket, wsId, subscribedEntities.FirstOrDefault(static x =>
                    x.EntityType == EntityType.Sensor && x.EntityId.EndsWith(ElectroluxServerConstants.TvocSuffix, StringComparison.OrdinalIgnoreCase))
                ?.EntityId, (ushort)((LiveStreamEvent<int>)liveStreamEvent).Value, cancellationToken),
            "CO2" => SendCo2SensorAsync(socket, wsId, subscribedEntities.FirstOrDefault(static x =>
                    x.EntityType == EntityType.Sensor && x.EntityId.EndsWith(ElectroluxServerConstants.Co2Suffix, StringComparison.OrdinalIgnoreCase))
                ?.EntityId, (ushort)((LiveStreamEvent<int>)liveStreamEvent).Value, cancellationToken),
            "Temp" => HandleTemperatureEvent(socket, wsId, subscribedEntities, liveStreamEvent, cancellationToken),
            "Humidity" => SendHumiditySensorAsync(socket, wsId, subscribedEntities.FirstOrDefault(static x =>
                    x.EntityType == EntityType.Sensor && x.EntityId.EndsWith(ElectroluxServerConstants.HumiditySuffix, StringComparison.OrdinalIgnoreCase))
                ?.EntityId, (sbyte)((LiveStreamEvent<int>)liveStreamEvent).Value, cancellationToken),
            "PM1" => SendPm1SensorAsync(socket, wsId, subscribedEntities.FirstOrDefault(static x =>
                    x.EntityType == EntityType.Sensor && x.EntityId.EndsWith(ElectroluxServerConstants.Pm1Suffix, StringComparison.OrdinalIgnoreCase))
                ?.EntityId, (ushort)((LiveStreamEvent<int>)liveStreamEvent).Value, cancellationToken),
            "PM2_5" => SendPm25SensorAsync(socket, wsId, subscribedEntities.FirstOrDefault(static x =>
                    x.EntityType == EntityType.Sensor && x.EntityId.EndsWith(ElectroluxServerConstants.Pm25Suffix, StringComparison.OrdinalIgnoreCase))
                ?.EntityId, (ushort)((LiveStreamEvent<int>)liveStreamEvent).Value, cancellationToken),
            "PM10" => SendPm10SensorAsync(socket, wsId, subscribedEntities.FirstOrDefault(static x =>
                    x.EntityType == EntityType.Sensor && x.EntityId.EndsWith(ElectroluxServerConstants.Pm10Suffix, StringComparison.OrdinalIgnoreCase))
                ?.EntityId, (ushort)((LiveStreamEvent<int>)liveStreamEvent).Value, cancellationToken),
            "ECO2" => SendEco2SensorAsync(socket, wsId, subscribedEntities.FirstOrDefault(static x =>
                    x.EntityType == EntityType.Sensor && x.EntityId.EndsWith(ElectroluxServerConstants.Eco2Suffix, StringComparison.OrdinalIgnoreCase))
                ?.EntityId, (ushort)((LiveStreamEvent<int>)liveStreamEvent).Value, cancellationToken),
            _ => Task.CompletedTask
        };
    }

    private Task HandleTemperatureEvent(System.Net.WebSockets.WebSocket socket, string wsId, HashSet<SubscribedEntity> subscribedEntities, LiveStreamEvent liveStreamEvent, CancellationToken cancellationToken)
    {
        var tempValue = (short)((LiveStreamEvent<int>)liveStreamEvent).Value;
        return Task.WhenAll(
            SendTemperatureSensorAsync(socket, wsId, subscribedEntities.FirstOrDefault(static x =>
                    x.EntityType == EntityType.Sensor && x.EntityId.EndsWith(ElectroluxServerConstants.TemperatureSuffix, StringComparison.OrdinalIgnoreCase))
                ?.EntityId, tempValue, cancellationToken),
            SendClimateAsync<string>(socket, wsId, subscribedEntities.FirstOrDefault(static x => x.EntityType == EntityType.Climate)?.EntityId,
                null, tempValue, cancellationToken)
        );
    }

    private async Task SendClimateAsync<TWorkMode>(System.Net.WebSockets.WebSocket socket, string wsId, string? entityId,
        TWorkMode? workMode, short? temperature, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(entityId))
            return;

        ClimateState? climateState = workMode switch
        {
            WorkMode wm => GetClimateState(wm),
            string wmStr => GetClimateState(wmStr),
            _ => null
        };
        await SendMessageAsync(socket,
            ResponsePayloadHelpers.CreateClimateStateChangedResponsePayload(
                new ClimateStateChangedEventMessageDataAttributes
                {
                    State = climateState,
                    CurrentTemperature =  temperature
                },
                entityId),
            wsId,
            cancellationToken);
    }

    private Task SendSelectAsync(System.Net.WebSockets.WebSocket socket, string wsId, string? entityId, sbyte? fanSpeed, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(entityId))
            return Task.CompletedTask;

        if (fanSpeed == null)
        {
            return SendMessageAsync(socket,
                ResponsePayloadHelpers.CreateSelectStateChangedResponsePayload(
                    new SelectStateChangedEventMessageDataAttributes { State = SelectState.Unknown },
                    entityId,
                    ElectroluxServerConstants.SelectSuffix),
                wsId,
                cancellationToken);
        }
        return SendMessageAsync(socket,
            ResponsePayloadHelpers.CreateSelectStateChangedResponsePayload(
                new SelectStateChangedEventMessageDataAttributes
                {
                    State = SelectState.On,
                    CurrentOption = fanSpeed.Value.ToString(NumberFormatInfo.InvariantInfo),
                    Options = SelectOptions
                },
                entityId,
                ElectroluxServerConstants.SelectSuffix),
            wsId,
            cancellationToken);
    }

    private Task ReportSensorsAsync(System.Net.WebSockets.WebSocket socket,
        string wsId,
        ApplianceState? applianceState,
        HashSet<SubscribedEntity> subscribedEntities,
        CancellationToken cancellationToken)
    {
        var tasks = (subscribedEntities.Where(static x => x.EntityType == EntityType.Sensor)
            .Select(subscribedEntity => subscribedEntity switch
            {
                _ when subscribedEntity.EntityId.EndsWith(ElectroluxServerConstants.TemperatureSuffix, StringComparison.OrdinalIgnoreCase) => SendTemperatureSensorAsync(socket, wsId, subscribedEntity.EntityId, applianceState?.Properties.Reported.Temperature, cancellationToken),
                _ when subscribedEntity.EntityId.EndsWith(ElectroluxServerConstants.HumiditySuffix, StringComparison.OrdinalIgnoreCase) => SendHumiditySensorAsync(socket, wsId, subscribedEntity.EntityId, applianceState?.Properties.Reported.Humidity, cancellationToken),
                _ when subscribedEntity.EntityId.EndsWith(ElectroluxServerConstants.TvocSuffix, StringComparison.OrdinalIgnoreCase) => SendTvocSensorAsync(socket, wsId, subscribedEntity.EntityId, applianceState?.Properties.Reported.Tvoc, cancellationToken),
                _ when subscribedEntity.EntityId.EndsWith(ElectroluxServerConstants.Eco2Suffix, StringComparison.OrdinalIgnoreCase) => SendEco2SensorAsync(socket, wsId, subscribedEntity.EntityId, applianceState?.Properties.Reported.Eco2, cancellationToken),
                _ when subscribedEntity.EntityId.EndsWith(ElectroluxServerConstants.Co2Suffix, StringComparison.OrdinalIgnoreCase) => SendCo2SensorAsync(socket, wsId, subscribedEntity.EntityId, applianceState?.Properties.Reported.Co2, cancellationToken),
                _ when subscribedEntity.EntityId.EndsWith(ElectroluxServerConstants.Pm1Suffix, StringComparison.OrdinalIgnoreCase) => SendPm1SensorAsync(socket, wsId, subscribedEntity.EntityId, applianceState?.Properties.Reported.Pm1, cancellationToken),
                _ when subscribedEntity.EntityId.EndsWith(ElectroluxServerConstants.Pm25Suffix, StringComparison.OrdinalIgnoreCase) => SendPm25SensorAsync(socket, wsId, subscribedEntity.EntityId, applianceState?.Properties.Reported.Pm25, cancellationToken),
                _ when subscribedEntity.EntityId.EndsWith(ElectroluxServerConstants.Pm10Suffix, StringComparison.OrdinalIgnoreCase) => SendPm10SensorAsync(socket, wsId, subscribedEntity.EntityId, applianceState?.Properties.Reported.Pm10, cancellationToken),
                _ => Task.CompletedTask
            }));

        return Task.WhenAll(tasks);
    }

    private Task SendTemperatureSensorAsync(System.Net.WebSockets.WebSocket socket, string wsId, string? entityId, short? temperature, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(entityId))
            return Task.CompletedTask;

        if (temperature == null)
        {
            return SendMessageAsync(socket,
                ResponsePayloadHelpers.CreateSensorStateChangedResponsePayload(
                    new SensorStateChangedEventMessageDataAttributes<string> { State = SensorState.Unknown, Value = null },
                    entityId,
                    ElectroluxServerConstants.TemperatureSuffix),
                wsId,
                cancellationToken);
        }
        return SendMessageAsync(socket,
            ResponsePayloadHelpers.CreateSensorStateChangedResponsePayload(
                new SensorStateChangedEventMessageDataAttributes<short>
                {
                    State = SensorState.On,
                    Value = temperature.Value
                },
                entityId,
                ElectroluxServerConstants.TemperatureSuffix),
            wsId,
            cancellationToken);
    }

    private Task SendHumiditySensorAsync(System.Net.WebSockets.WebSocket socket, string wsId, string? entityId, sbyte? humidity, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(entityId))
            return Task.CompletedTask;

        if (humidity == null)
        {
            return SendMessageAsync(socket,
                ResponsePayloadHelpers.CreateSensorStateChangedResponsePayload(
                    new SensorStateChangedEventMessageDataAttributes<string> { State = SensorState.Unknown, Value = null },
                    entityId,
                    ElectroluxServerConstants.HumiditySuffix),
                wsId,
                cancellationToken);
        }
        return SendMessageAsync(socket,
            ResponsePayloadHelpers.CreateSensorStateChangedResponsePayload(
                new SensorStateChangedEventMessageDataAttributes<sbyte>
                {
                    State = SensorState.On,
                    Value = humidity.Value
                },
                entityId,
                ElectroluxServerConstants.HumiditySuffix),
            wsId,
            cancellationToken);
    }

    private Task SendTvocSensorAsync(System.Net.WebSockets.WebSocket socket, string wsId, string? entityId, ushort? tvoc, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(entityId))
            return Task.CompletedTask;

        if (tvoc == null)
        {
            return SendMessageAsync(socket,
                ResponsePayloadHelpers.CreateSensorStateChangedResponsePayload(
                    new SensorStateChangedEventMessageDataAttributes<string> { State = SensorState.Unknown, Value = null },
                    entityId,
                    ElectroluxServerConstants.TvocSuffix),
                wsId,
                cancellationToken);
        }
        return SendMessageAsync(socket,
            ResponsePayloadHelpers.CreateSensorStateChangedResponsePayload(
                new SensorStateChangedEventMessageDataAttributes<ushort> { State = SensorState.On, Value = tvoc.Value },
                entityId,
                ElectroluxServerConstants.TvocSuffix),
            wsId,
            cancellationToken);
    }

    private Task SendCo2SensorAsync(System.Net.WebSockets.WebSocket socket, string wsId, string? entityId, ushort? co2, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(entityId))
            return Task.CompletedTask;

        if (co2 == null)
        {
            return SendMessageAsync(socket,
                ResponsePayloadHelpers.CreateSensorStateChangedResponsePayload(
                    new SensorStateChangedEventMessageDataAttributes<string> { State = SensorState.Unknown, Value = null },
                    entityId,
                    ElectroluxServerConstants.Co2Suffix),
                wsId,
                cancellationToken);
        }
        return SendMessageAsync(socket,
                ResponsePayloadHelpers.CreateSensorStateChangedResponsePayload(
                    new SensorStateChangedEventMessageDataAttributes<ushort> { State = SensorState.On, Value = co2.Value },
                    entityId,
                    ElectroluxServerConstants.Co2Suffix),
                wsId,
                cancellationToken);
    }

    private Task SendPm1SensorAsync(System.Net.WebSockets.WebSocket socket, string wsId, string? entityId, ushort? pm1, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(entityId))
            return Task.CompletedTask;

        if (pm1 == null)
        {
            return SendMessageAsync(socket,
                ResponsePayloadHelpers.CreateSensorStateChangedResponsePayload(
                    new SensorStateChangedEventMessageDataAttributes<string> { State = SensorState.Unknown, Value = null },
                    entityId,
                    ElectroluxServerConstants.Pm1Suffix),
                wsId,
                cancellationToken);
        }
        return SendMessageAsync(socket,
            ResponsePayloadHelpers.CreateSensorStateChangedResponsePayload(
                new SensorStateChangedEventMessageDataAttributes<ushort> { State = SensorState.On, Value = pm1.Value },
                entityId,
                ElectroluxServerConstants.Pm1Suffix),
            wsId,
            cancellationToken);
    }

    private Task SendPm25SensorAsync(System.Net.WebSockets.WebSocket socket, string wsId, string? entityId, ushort? pm25, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(entityId))
            return Task.CompletedTask;

        if (pm25 == null)
        {
            return SendMessageAsync(socket,
                ResponsePayloadHelpers.CreateSensorStateChangedResponsePayload(
                    new SensorStateChangedEventMessageDataAttributes<string> { State = SensorState.Unknown, Value = null },
                    entityId,
                    ElectroluxServerConstants.Pm25Suffix),
                wsId,
                cancellationToken);
        }
        return SendMessageAsync(socket,
            ResponsePayloadHelpers.CreateSensorStateChangedResponsePayload(
                new SensorStateChangedEventMessageDataAttributes<ushort> { State = SensorState.On, Value = pm25.Value },
                entityId,
                ElectroluxServerConstants.Pm25Suffix),
            wsId,
            cancellationToken);
    }

    private Task SendPm10SensorAsync(System.Net.WebSockets.WebSocket socket, string wsId, string? entityId, ushort? pm10, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(entityId))
            return Task.CompletedTask;

        if (pm10 == null)
        {
            return SendMessageAsync(socket,
                ResponsePayloadHelpers.CreateSensorStateChangedResponsePayload(
                    new SensorStateChangedEventMessageDataAttributes<string> { State = SensorState.Unknown, Value = null },
                    entityId,
                    ElectroluxServerConstants.Pm10Suffix),
                wsId,
                cancellationToken);
        }
        return SendMessageAsync(socket,
            ResponsePayloadHelpers.CreateSensorStateChangedResponsePayload(
                new SensorStateChangedEventMessageDataAttributes<ushort> { State = SensorState.On, Value = pm10.Value },
                entityId,
                ElectroluxServerConstants.Pm10Suffix),
            wsId,
            cancellationToken);
    }

    private Task SendEco2SensorAsync(System.Net.WebSockets.WebSocket socket, string wsId, string? entityId, ushort? eco2, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(entityId))
            return Task.CompletedTask;

        if (eco2 == null)
        {
            return SendMessageAsync(socket,
                ResponsePayloadHelpers.CreateSensorStateChangedResponsePayload(
                    new SensorStateChangedEventMessageDataAttributes<string> { State = SensorState.Unknown, Value = null },
                    entityId,
                    ElectroluxServerConstants.Eco2Suffix),
                wsId,
                cancellationToken);
        }
        return SendMessageAsync(socket,
            ResponsePayloadHelpers.CreateSensorStateChangedResponsePayload(
                new SensorStateChangedEventMessageDataAttributes<ushort> { State = SensorState.On, Value = eco2.Value },
                entityId,
                ElectroluxServerConstants.Eco2Suffix),
            wsId,
            cancellationToken);
    }

    private static ClimateState GetClimateState(in WorkMode workMode)
        => workMode switch
        {
            WorkMode.PowerOff => ClimateState.Off,
            WorkMode.Auto => ClimateState.Auto,
            WorkMode.Manual => ClimateState.Fan,
            _ => ClimateState.Unknown
        };

    private static ClimateState GetClimateState(string workMode)
        => workMode switch
        {
            nameof(WorkMode.PowerOff) => ClimateState.Off,
            nameof(WorkMode.Auto) => ClimateState.Auto,
            nameof(WorkMode.Manual) => ClimateState.Fan,
            _ => ClimateState.Unknown
        };

    protected override ValueTask<DeviceState> OnGetDeviceStateAsync(
        GetDeviceStateMsg payload,
        string wsId,
        CancellationToken cancellationToken)
        => ValueTask.FromResult(DeviceState.Connected);

    protected override async ValueTask<IReadOnlyCollection<AvailableEntity>> OnGetAvailableEntitiesAsync(
        GetAvailableEntitiesMsg payload,
        string wsId,
        CancellationToken cancellationToken)
        => GetAvailableEntities(await GetEntitiesAsync(wsId, cancellationToken)).ToArray();

    private async Task<List<UnfoldedCircleConfigurationItem>?> GetEntitiesAsync(
        string wsId,
        CancellationToken cancellationToken)
    {
        var configuration = await _configurationService.GetConfigurationAsync(cancellationToken);
        if (configuration.Entities.Count != 0)
            return configuration.Entities;

        _logger.NoConfigurationsFound(wsId);
        return null;
    }

    private static IEnumerable<AvailableEntity> GetAvailableEntities(List<UnfoldedCircleConfigurationItem>? entities)
    {
        if (entities is not { Count: > 0 })
            yield break;

        foreach (var configurationItem in entities)
        {
            yield return new ClimateAvailableEntity
            {
                EntityId = configurationItem.EntityId.GetIdentifier(EntityType.Climate),
                EntityType = EntityType.Climate,
                Name = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["en"] = configurationItem.EntityName },
                Features = ElectroluxEntitySettings.ClimateFeatures,
                Options = ClimateOptions
            };

            RegisterSelect(configurationItem.EntityId.GetBaseIdentifier(), ElectroluxServerConstants.SelectSuffix);
            yield return new SelectAvailableEntity
            {
                EntityId = configurationItem.EntityId.GetIdentifier(EntityType.Select, ElectroluxServerConstants.SelectSuffix),
                EntityType = EntityType.Select,
                Name = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["en"] = $"{configurationItem.EntityName} Fan Speed" }
            };
            yield return new SensorAvailableEntity
            {
                DeviceClass = DeviceClass.Temperature,
                EntityId = configurationItem.EntityId.GetIdentifier(EntityType.Sensor, ElectroluxServerConstants.TemperatureSuffix),
                EntityType = EntityType.Sensor,
                Name = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["en"] = $"{configurationItem.EntityName} Temperature" },
                Options = new SensorOptions { CustomUnit = "°C", Decimals = 0 }
            };
            yield return new SensorAvailableEntity
            {
                DeviceClass = DeviceClass.Humidity,
                EntityId = configurationItem.EntityId.GetIdentifier(EntityType.Sensor, ElectroluxServerConstants.HumiditySuffix),
                EntityType = EntityType.Sensor,
                Name = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["en"] = $"{configurationItem.EntityName} Humidity" },
                Options = new SensorOptions { CustomUnit = "%", Decimals = 0 }
            };
            yield return new SensorAvailableEntity
            {
                DeviceClass = DeviceClass.Custom,
                EntityId = configurationItem.EntityId.GetIdentifier(EntityType.Sensor, ElectroluxServerConstants.TvocSuffix),
                EntityType = EntityType.Sensor,
                Name = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["en"] = $"{configurationItem.EntityName} TVOC" },
                Options = new SensorOptions { CustomUnit = "ppb", Decimals = 0 }
            };
            yield return new SensorAvailableEntity
            {
                DeviceClass = DeviceClass.Custom,
                EntityId = configurationItem.EntityId.GetIdentifier(EntityType.Sensor, ElectroluxServerConstants.Co2Suffix),
                EntityType = EntityType.Sensor,
                Name = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["en"] = $"{configurationItem.EntityName} CO2" },
                Options = new SensorOptions { Decimals = 0 }
            };
            yield return new SensorAvailableEntity
            {
                DeviceClass = DeviceClass.Custom,
                EntityId = configurationItem.EntityId.GetIdentifier(EntityType.Sensor, ElectroluxServerConstants.Pm1Suffix),
                EntityType = EntityType.Sensor,
                Name = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["en"] = $"{configurationItem.EntityName} PM1" },
                Options = new SensorOptions { CustomUnit = "μg/m3", Decimals = 0 }
            };
            yield return new SensorAvailableEntity
            {
                DeviceClass = DeviceClass.Custom,
                EntityId = configurationItem.EntityId.GetIdentifier(EntityType.Sensor, ElectroluxServerConstants.Pm25Suffix),
                EntityType = EntityType.Sensor,
                Name = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["en"] = $"{configurationItem.EntityName} PM2.5" },
                Options = new SensorOptions { CustomUnit = "μg/m3", Decimals = 0 }
            };
            yield return new SensorAvailableEntity
            {
                DeviceClass = DeviceClass.Custom,
                EntityId = configurationItem.EntityId.GetIdentifier(EntityType.Sensor, ElectroluxServerConstants.Pm10Suffix),
                EntityType = EntityType.Sensor,
                Name = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["en"] = $"{configurationItem.EntityName} PM10" },
                Options = new SensorOptions { CustomUnit = "μg/m3", Decimals = 0 }
            };
            yield return new SensorAvailableEntity
            {
                DeviceClass = DeviceClass.Custom,
                EntityId = configurationItem.EntityId.GetIdentifier(EntityType.Sensor, ElectroluxServerConstants.Eco2Suffix),
                EntityType = EntityType.Sensor,
                Name = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["en"] = $"{configurationItem.EntityName} ECO2" },
                Options = new SensorOptions { Decimals = 0 }
            };
        }
    }

    protected override async ValueTask OnSubscribeEventsAsync(System.Net.WebSockets.WebSocket socket,
        SubscribeEventsMsg payload,
        string wsId,
        CancellationTokenWrapper cancellationTokenWrapper,
        CancellationToken commandCancellationToken)
    {
        if (payload.MsgData?.EntityIds is not { Length: > 0 })
            return;

        var responseCache = new Dictionary<string, ApplianceState?>(StringComparer.OrdinalIgnoreCase);
        foreach (string msgDataEntityId in payload.MsgData.EntityIds)
        {
            cancellationTokenWrapper.AddSubscribedEntity(msgDataEntityId);
            var baseEntityId = msgDataEntityId.GetBaseIdentifier();
            var entityType = msgDataEntityId.GetEntityTypeFromIdentifier();
            if (!responseCache.TryGetValue(baseEntityId, out var applianceState))
            {
                applianceState = await _electroluxClient.GetApplianceStateAsync(baseEntityId, commandCancellationToken);
                responseCache[baseEntityId] = applianceState;
            }

            switch (entityType)
            {
                case EntityType.Climate:
                    await SendClimateAsync(socket, wsId, msgDataEntityId, applianceState?.Properties.Reported.WorkMode, applianceState?.Properties.Reported.Temperature, commandCancellationToken);
                    break;
                case EntityType.Select:
                    await SendSelectAsync(socket, wsId, msgDataEntityId, applianceState?.Properties.Reported.FanSpeed, commandCancellationToken);
                    break;
                case EntityType.Sensor:
                    await ReportSensorsAsync(socket, wsId, applianceState, [new SubscribedEntity(msgDataEntityId, EntityType.Sensor)], commandCancellationToken);
                    break;
            }
        }
    }

    protected override ValueTask OnUnsubscribeEventsAsync(UnsubscribeEventsMsg payload, string wsId, CancellationTokenWrapper cancellationTokenWrapper)
    {
        if (payload.MsgData?.EntityIds is { Length: > 0 })
        {
            foreach (string msgDataEntityId in payload.MsgData.EntityIds)
            {
                cancellationTokenWrapper.RemoveSubscribedEntity(msgDataEntityId);
            }
        }
        // If no specific device or entity was specified, dispose all clients for this websocket ID.
        else if (payload.MsgData is { DeviceId: null, EntityIds: null })
            cancellationTokenWrapper.RemoveAllSubscribedEntities();

        return ValueTask.CompletedTask;
    }

    protected override async ValueTask<EntityStateChanged[]> OnGetEntityStatesAsync(GetEntityStatesMsg payload, string wsId, CancellationToken cancellationToken)
    {
        var entityStates = new List<EntityStateChanged>();
        await foreach (var entityStateChanged in ElectroluxResponsePayloadHelpers.GetEntityStatesAsync(_electroluxClient.GetAirPurifiersAsync(cancellationToken)).WithCancellation(cancellationToken))
            entityStates.Add(entityStateChanged);

        return entityStates.ToArray();
    }

    protected override ValueTask<SetupDriverUserDataResult> OnSetupDriverUserDataConfirmAsync(System.Net.WebSockets.WebSocket socket, SetDriverUserDataMsg payload, string wsId, CancellationToken cancellationToken)
        => ValueTask.FromResult(SetupDriverUserDataResult.Finalized);

    protected override ValueTask<SetupDriverUserDataResult> HandleEntityReconfigured(System.Net.WebSockets.WebSocket socket,
        SetDriverUserDataMsg payload,
        string wsId,
        UnfoldedCircleConfigurationItem configurationItem,
        CancellationToken cancellationToken) =>
        AddSetupConfiguration(payload, cancellationToken);

    protected override ValueTask<SetupDriverUserDataResult> HandleCreateNewEntity(System.Net.WebSockets.WebSocket socket,
        SetDriverUserDataMsg payload,
        string wsId,
        CancellationToken cancellationToken)
        => AddSetupConfiguration(payload, cancellationToken);

    private async ValueTask<SetupDriverUserDataResult> AddSetupConfiguration(SetDriverUserDataMsg payload, CancellationToken cancellationToken)
    {
        var configuration = await _configurationService.GetConfigurationAsync(cancellationToken);
        var apiKey = payload.MsgData.InputValues![ElectroluxServerConstants.ApiKeyKey];
        var refreshToken = payload.MsgData.InputValues[ElectroluxServerConstants.RefreshTokenKey];

        await _electroluxClient.SetTokenAsync(new TokenResult(null, refreshToken, DateTimeOffset.MinValue, apiKey),
            cancellationToken);
        await foreach (var applianceResult in _electroluxClient.GetAirPurifiersAsync(cancellationToken))
        {
            var entity = configuration.Entities.FirstOrDefault(x => x.EntityId.Equals(applianceResult.ApplianceId, StringComparison.OrdinalIgnoreCase));
            if (entity is null)
            {
                _logger.AddingConfigurationForDevice(applianceResult.ApplianceId);
                entity = new UnfoldedCircleConfigurationItem
                {
                    Host = "N/A",
                    EntityName = $"{applianceResult.Brand} {applianceResult.Model}",
                    EntityId = applianceResult.ApplianceId
                };
                configuration.Entities.Add(entity);
            }
        }

        await _configurationService.UpdateConfigurationAsync(configuration, cancellationToken);

        return SetupDriverUserDataResult.Finalized;
    }

    protected override MediaPlayerEntityCommandMsgData<MediaPlayerCommandId>? DeserializeMediaPlayerCommandPayload(JsonDocument jsonDocument)
        => null;

    protected override ValueTask<SettingsPage> CreateNewEntitySettingsPageAsync(CancellationToken cancellationToken)
        => ValueTask.FromResult(CreateSettingsPage(null));

    protected override ValueTask<SettingsPage> CreateReconfigureEntitySettingsPageAsync(
        UnfoldedCircleConfigurationItem configurationItem,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(CreateSettingsPage(configurationItem));

    private static SettingsPage CreateSettingsPage(UnfoldedCircleConfigurationItem? configurationItem)
    {
        const string regexPattern = "\\S+";
        return new SettingsPage
        {
            Title = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["en"] = configurationItem is null ? "Add a new device" : "Reconfigure device" },
            Settings = [
                new Setting
                {
                    Id = ElectroluxServerConstants.ApiKeyKey,
                    Field = new SettingTypeText
                    {
                        Text = new ValueRegex
                        {
                            RegEx = regexPattern
                        }
                    },
                    Label = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["en"] = "Enter API Key (mandatory)" }
                },
                new Setting
                {
                    Id = ElectroluxServerConstants.RefreshTokenKey,
                    Field = new SettingTypeText
                    {
                        Text = new ValueRegex
                        {
                            RegEx = regexPattern
                        }
                    },
                    Label = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["en"] = "Enter Refresh Token (mandatory)" }
                }
            ]
        };
    }

    protected override FrozenSet<EntityType> SupportedEntityTypes { get; } =
    [
        EntityType.Climate,
        EntityType.Select,
        EntityType.Sensor
    ];
}