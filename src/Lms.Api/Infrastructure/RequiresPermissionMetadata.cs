namespace Lms.Api.Infrastructure;

/// <summary>Endpoint metadata marking the permission an endpoint enforces (read by the coverage test).</summary>
public sealed record RequiresPermissionMetadata(string Permission);