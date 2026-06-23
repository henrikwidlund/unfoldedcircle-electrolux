using System.Collections.Frozen;

using UnfoldedCircle.Models.Events;

namespace UnfoldedCircle.Electrolux.Configuration;

public static class ElectroluxEntitySettings
{
    public static readonly FrozenSet<ClimateFeature> ClimateFeatures =
    [
        ClimateFeature.CurrentTemperature,
        ClimateFeature.Fan,
        ClimateFeature.OnOff,
        ClimateFeature.Cool
    ];
}
