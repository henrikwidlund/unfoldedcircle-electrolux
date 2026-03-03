using UnfoldedCircle.Electrolux.Configuration;
using UnfoldedCircle.Electrolux.Http;
using UnfoldedCircle.Models.Shared;
using UnfoldedCircle.Models.Sync;
using UnfoldedCircle.Server.Extensions;

namespace UnfoldedCircle.Electrolux.Response;

internal static class ElectroluxResponsePayloadHelpers
{
    private static readonly SensorEntityAttribute[] SensorAttributes = [SensorEntityAttribute.State, SensorEntityAttribute.Unit, SensorEntityAttribute.Value];

    internal static async IAsyncEnumerable<EntityStateChanged> GetEntityStatesAsync(IAsyncEnumerable<ApplianceResult> appliances)
    {
        await foreach (var appliance in appliances)
        {
            yield return new ClimateEntityStateChanged
            {
                EntityId = appliance.ApplianceId.GetIdentifier(EntityType.Climate),
                EntityType = EntityType.Climate,
                Attributes = [ClimateEntityAttribute.CurrentTemperature, ClimateEntityAttribute.State]
            };
            yield return new SelectEntityStateChanged
            {
                EntityId = appliance.ApplianceId.GetIdentifier(EntityType.Select, ElectroluxServerConstants.SelectSuffix),
                EntityType = EntityType.Select,
                Attributes = [SelectEntityAttribute.CurrentOption, SelectEntityAttribute.Options, SelectEntityAttribute.State]
            };
            yield return new SensorEntityStateChanged
            {
                EntityId = appliance.ApplianceId.GetIdentifier(EntityType.Sensor, ElectroluxServerConstants.TemperatureSuffix),
                EntityType = EntityType.Select,
                Attributes = SensorAttributes
            };
            yield return new SensorEntityStateChanged
            {
                EntityId = appliance.ApplianceId.GetIdentifier(EntityType.Sensor, ElectroluxServerConstants.HumiditySuffix),
                EntityType = EntityType.Sensor,
                Attributes = SensorAttributes
            };
            yield return new SensorEntityStateChanged
            {
                EntityId = appliance.ApplianceId.GetIdentifier(EntityType.Sensor, ElectroluxServerConstants.TVOCSuffix),
                EntityType = EntityType.Sensor,
                Attributes = SensorAttributes
            };
            yield return new SensorEntityStateChanged
            {
                EntityId = appliance.ApplianceId.GetIdentifier(EntityType.Sensor, ElectroluxServerConstants.CO2Suffix),
                EntityType = EntityType.Sensor,
                Attributes = SensorAttributes
            };
            yield return new SensorEntityStateChanged
            {
                EntityId = appliance.ApplianceId.GetIdentifier(EntityType.Sensor, ElectroluxServerConstants.PM1Suffix),
                EntityType = EntityType.Sensor,
                Attributes = SensorAttributes
            };
            yield return new SensorEntityStateChanged
            {
                EntityId = appliance.ApplianceId.GetIdentifier(EntityType.Sensor, ElectroluxServerConstants.PM25Suffix),
                EntityType = EntityType.Sensor,
                Attributes = SensorAttributes
            };
            yield return new SensorEntityStateChanged
            {
                EntityId = appliance.ApplianceId.GetIdentifier(EntityType.Sensor, ElectroluxServerConstants.PM10Suffix),
                EntityType = EntityType.Sensor,
                Attributes = SensorAttributes
            };
            yield return new SensorEntityStateChanged
            {
                EntityId = appliance.ApplianceId.GetIdentifier(EntityType.Sensor, ElectroluxServerConstants.ECO2Suffix),
                EntityType = EntityType.Sensor,
                Attributes = SensorAttributes
            };
        }
    }
}