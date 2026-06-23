using UnfoldedCircle.Electrolux.Http;
using UnfoldedCircle.Server.Configuration;

namespace UnfoldedCircle.Electrolux.Json;

public sealed record BackupData(UnfoldedCircleConfiguration<UnfoldedCircleConfigurationItem> Configuration, TokenResult TokenResult);
