using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;

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
    private static readonly ConcurrentDictionary<string, ClimateState> ReportedEntityIdStates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, short> ReportedEntityIdTemperature = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, sbyte> ReportedEntityIdSelect = new(StringComparer.OrdinalIgnoreCase);
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
        using var periodicTimer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            do
            {
                await Parallel.ForEachAsync(subscribedEntitiesHolder.SubscribedEntities.Values.SelectMany(static x => x), cancellationToken,
                    async (subscribedEntity, token) =>
                    {
                        try
                        {
                            var applianceState = await _electroluxClient.GetApplianceStateAsync(subscribedEntity.EntityId.GetBaseIdentifier(), token);
                            if (applianceState is null)
                            {
                                _logger.CouldNotGetApplianceState(wsId, subscribedEntity.EntityId);
                                return;
                            }

                            await Task.WhenAll(
                                ReportClimate(socket, wsId, applianceState, subscribedEntity.EntityId, token),
                                ReportSelect(socket, wsId, applianceState, subscribedEntity.EntityId, token));
                        }
                        catch (Exception e)
                        {
                            // This is expected from control flow, no need to spam logs
                            if (e is OperationCanceledException)
                                return;
                            _logger.FailureDuringEvent(e, wsId, subscribedEntity.EntityId);
                        }
                    });
            } while (!cancellationToken.IsCancellationRequested && await periodicTimer.WaitForNextTickAsync(cancellationToken));
        }
        finally
        {
            foreach (var entityId in subscribedEntitiesHolder.SubscribedEntities.Keys)
            {
                ReportedEntityIdStates.TryRemove(entityId, out _);
                ReportedEntityIdTemperature.TryRemove(entityId, out _);
                ReportedEntityIdSelect.TryRemove(entityId, out _);
            }
        }
    }

    private async Task ReportClimate(System.Net.WebSockets.WebSocket socket,
        string wsId,
        ApplianceState applianceState,
        string entityId,
        CancellationToken cancellationToken)
    {
        var reportedState = ReportedEntityIdStates.GetValueOrDefault(entityId, ClimateState.Unknown);
        var currentState = GetClimateState(applianceState.Properties.Reported.WorkMode);
        var reportedTemperature = ReportedEntityIdTemperature.GetValueOrDefault(entityId, (short)0);
        var currentTemperature = applianceState.Properties.Reported.Temperature;
        ClimateState? stateToReport = currentState != reportedState ? currentState : null;
        short? temperatureToReport = currentTemperature != reportedTemperature ? currentTemperature : null;
        if (stateToReport is not null || temperatureToReport is not null)
        {
            await SendMessageAsync(socket,
                ResponsePayloadHelpers.CreateClimateStateChangedResponsePayload(
                    new ClimateStateChangedEventMessageDataAttributes
                    {
                        State = stateToReport,
                        CurrentTemperature = temperatureToReport
                    },
                    entityId),
                wsId,
                cancellationToken);
            ReportedEntityIdStates[entityId] = currentState;
            ReportedEntityIdTemperature[entityId] = currentTemperature;
        }
    }

    private async Task ReportSelect(System.Net.WebSockets.WebSocket socket,
        string wsId,
        ApplianceState applianceState,
        string entityId,
        CancellationToken cancellationToken)
    {
        var reportedSelect = ReportedEntityIdSelect.GetValueOrDefault(entityId, (sbyte)0);
        if (reportedSelect == 0 || applianceState.Properties.Reported.FanSpeed != reportedSelect)
        {
            var currentSelect = applianceState.Properties.Reported.FanSpeed;
            await SendMessageAsync(socket,
                ResponsePayloadHelpers.CreateSelectStateChangedResponsePayload(
                    new SelectStateChangedEventMessageDataAttributes
                    {
                        CurrentOption = currentSelect.ToString(NumberFormatInfo.InvariantInfo),
                        Options = null
                    },
                    entityId,
                    ElectroluxServerConstants.SelectSuffix),
                wsId,
                cancellationToken);
            ReportedEntityIdSelect[entityId] = currentSelect;
        }
    }

    private static ClimateState GetClimateState(WorkMode workMode)
        => workMode switch
        {
            WorkMode.PowerOff => ClimateState.Off,
            WorkMode.Auto => ClimateState.Auto,
            WorkMode.Manual => ClimateState.Fan,
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
            var baseEntityId = msgDataEntityId.GetBaseIdentifier();
            var entityType = msgDataEntityId.GetEntityTypeFromIdentifier();
            if (!responseCache.TryGetValue(baseEntityId, out ApplianceState? applianceState))
            {
                applianceState = await _electroluxClient.GetApplianceStateAsync(baseEntityId, commandCancellationToken);
                responseCache[baseEntityId] = applianceState;
            }

            switch (entityType)
            {
                case EntityType.Climate:
                    await SendMessageAsync(socket,
                        ResponsePayloadHelpers.CreateClimateStateChangedResponsePayload(
                            new ClimateStateChangedEventMessageDataAttributes
                            {
                                State = applianceState?.Properties.Reported.WorkMode == null ? ClimateState.Unknown :
                                    GetClimateState(applianceState.Properties.Reported.WorkMode),
                                CurrentTemperature = applianceState?.Properties.Reported.Temperature
                            },
                            msgDataEntityId),
                        wsId,
                        commandCancellationToken);
                    break;
                case EntityType.Select:
                    await SendMessageAsync(socket,
                        ResponsePayloadHelpers.CreateSelectStateChangedResponsePayload(
                            new SelectStateChangedEventMessageDataAttributes
                            {
                                State = SelectState.On,
                                CurrentOption = applianceState?.Properties.Reported.FanSpeed.ToString(NumberFormatInfo.InvariantInfo),
                                Options = SelectOptions
                            },
                            msgDataEntityId,
                            ElectroluxServerConstants.SelectSuffix),
                        wsId,
                        commandCancellationToken);
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

    protected override FrozenSet<EntityType> SupportedEntityTypes { get; } = [EntityType.Climate, EntityType.Select];
}