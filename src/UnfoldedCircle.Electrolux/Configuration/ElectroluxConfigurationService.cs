using System.Text.Json.Serialization.Metadata;

using UnfoldedCircle.Server.Configuration;
using UnfoldedCircle.Server.Json;

namespace UnfoldedCircle.Electrolux.Configuration;

public class ElectroluxConfigurationService(IConfiguration configuration) : ConfigurationService<UnfoldedCircleConfigurationItem>(configuration)
{
    protected override JsonTypeInfo<UnfoldedCircleConfiguration<UnfoldedCircleConfigurationItem>> GetSerializer()
        => UnfoldedCircleJsonSerializerContext.Default.UnfoldedCircleConfigurationUnfoldedCircleConfigurationItem;
}