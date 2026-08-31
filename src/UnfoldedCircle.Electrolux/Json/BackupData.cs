using UnfoldedCircle.Electrolux.Http;
using UnfoldedCircle.Server.Configuration;

namespace UnfoldedCircle.Electrolux.Json;

public sealed record BackupData(UnfoldedCircleConfiguration<UnfoldedCircleGlobalConfiguration, UnfoldedCircleConfigurationItem> Configuration, TokenResult TokenResult);
