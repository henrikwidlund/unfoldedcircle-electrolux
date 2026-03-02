using UnfoldedCircle.Electrolux.Configuration;
using UnfoldedCircle.Electrolux.Http;
using UnfoldedCircle.Models.Shared;
using UnfoldedCircle.Models.Sync;
using UnfoldedCircle.Server.Extensions;

namespace UnfoldedCircle.Electrolux.Response;

internal static class ElectroluxResponsePayloadHelpers
{
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
        }
    }
}