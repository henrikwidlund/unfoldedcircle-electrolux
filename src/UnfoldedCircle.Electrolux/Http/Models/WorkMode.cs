using Theodicean.SourceGenerators;

namespace UnfoldedCircle.Electrolux.Http;

[EnumJsonConverter<WorkMode>(CaseSensitive = false, PropertyName = "Workmode")]
[JsonConverter(typeof(WorkModeJsonConverter))]
public enum WorkMode
{
    PowerOff = 1,
    Auto = 2,
    Manual = 3
}

// ReSharper disable once RedundantExtendsListEntry For some reason code won't compile without adding this explicit inheritance on this specific converter - all other work
public partial class WorkModeJsonConverter : JsonConverter<WorkMode>;