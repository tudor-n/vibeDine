using OpsApi.Providers.Abstractions;

namespace OpsApi.DTO.Responses;

public record OpsAdminDto(Guid Id, string Email, string FullName);

public record AuthResponse(string AccessToken, DateTime ExpiresAt, OpsAdminDto Admin);

/// <summary>A stored connection as seen by the UI. Never carries the credential.</summary>
public record ConnectionDto(
    Guid Id,
    string Provider,
    string Name,
    string? Role,
    string? AccountLabel,
    string Status,
    string? StatusDetail,
    DateTime CreatedAt,
    DateTime? LastCheckedAt,
    ProviderCapabilities Capabilities,
    IReadOnlyDictionary<string, string?> Metadata);

/// <summary>An available provider + what it can do — drives the connect wizard.</summary>
public record ProviderInfoDto(string Key, ProviderCapabilities Capabilities);

/// <summary>Result of validating credentials during connect / re-test.</summary>
public record ConnectionTestResult(bool Success, string? AccountLabel, string? Detail);

public record ProjectDto(string Id, string Name, string? Description);

public record ServiceDto(string Id, string Name);

public record EnvironmentDto(string Id, string Name);

public record ProjectDetailDto(
    string Id, string Name, string? Description,
    IReadOnlyList<ServiceDto> Services, IReadOnlyList<EnvironmentDto> Environments);

public record DeploymentDto(
    string Id, string Status, DateTime? CreatedAt,
    string? Url, string? StaticUrl, bool CanRedeploy, bool CanRollback);

public record LogEntryDto(
    DateTime Timestamp, string Message, string? Severity, string LogType,
    IReadOnlyDictionary<string, object?>? Attributes);

public record VariableDto(string Key, string Value);

public record AuditEntryDto(
    Guid Id, string? ActorEmail, string Action, string? Provider, Guid? ConnectionId,
    string? TargetType, string? TargetId, string? Summary, string Result, DateTime CreatedAt);

/// <summary>One row in the fleet overview — a server/connection at a glance.</summary>
public record FleetItemDto(
    Guid ConnectionId, string Provider, string Name, string? Role,
    string Status, string? StatusDetail, string? AccountLabel, DateTime? LastCheckedAt);

public record FleetOverviewDto(int Total, int Active, int Unhealthy, IReadOnlyList<FleetItemDto> Servers);

/// <summary>Aggregated (server-level) metrics for one connection, plus support/error state.</summary>
public record ServerMetricSummary(
    Guid ConnectionId, string Name, string Provider, string? Role,
    bool Supported, string? Error, IReadOnlyList<MetricPoint> Points);

/// <summary>Metrics for the whole fleet — every server side by side.</summary>
public record FleetMetricsDto(int WindowMinutes, IReadOnlyList<ServerMetricSummary> Servers);
