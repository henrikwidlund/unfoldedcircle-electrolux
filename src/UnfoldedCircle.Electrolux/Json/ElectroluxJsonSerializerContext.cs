using UnfoldedCircle.Electrolux.Http;

namespace UnfoldedCircle.Electrolux.Json;

[JsonSerializable(typeof(RefreshTokenRequest))]
[JsonSerializable(typeof(RefreshTokenResponse))]
[JsonSerializable(typeof(TokenResult))]
[JsonSerializable(typeof(Appliance[]))]
[JsonSerializable(typeof(ApplianceInfoResponse))]
[JsonSerializable(typeof(PurifierCommand))]
[JsonSerializable(typeof(ApplianceState))]
internal sealed partial class ElectroluxJsonSerializerContext : JsonSerializerContext
{
    static ElectroluxJsonSerializerContext()
    {
        Default = new ElectroluxJsonSerializerContext(new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
    }
}