namespace OpsApi.Providers.Railway;

/// <summary>
/// GraphQL documents for the Railway public API. Only queries marked [VERIFICAT] in
/// docs/railway-api.md are used for real features; [DE CONFIRMAT] ones (audit, metrics,
/// live subscriptions, network-flow logs) are intentionally left unimplemented and degrade to
/// empty results rather than guessing field names (per the doc's explicit instruction).
/// </summary>
internal static class RailwayQueries
{
    public const string Me = "query { me { name email } }";

    public const string WorkspaceById =
        "query workspace($workspaceId: String!) { workspace(workspaceId: $workspaceId) { id name } }";

    public const string ProjectToken =
        "query { projectToken { projectId environmentId } }";

    public const string Projects =
        "query { projects { edges { node { id name description } } } }";

    public const string Project = """
        query project($id: String!) {
          project(id: $id) {
            id
            name
            description
            services { edges { node { id name } } }
            environments { edges { node { id name } } }
          }
        }
        """;

    public const string Deployments = """
        query deployments($input: DeploymentListInput!, $first: Int) {
          deployments(input: $input, first: $first) {
            edges { node { id status createdAt url staticUrl } }
          }
        }
        """;

    public const string Deployment = """
        query deployment($id: String!) {
          deployment(id: $id) {
            id status createdAt url staticUrl canRedeploy canRollback
          }
        }
        """;

    public const string BuildLogs = """
        query buildLogs($deploymentId: String!, $limit: Int) {
          buildLogs(deploymentId: $deploymentId, limit: $limit) { timestamp message severity }
        }
        """;

    public const string DeploymentLogs = """
        query deploymentLogs($deploymentId: String!, $limit: Int, $filter: String) {
          deploymentLogs(deploymentId: $deploymentId, limit: $limit, filter: $filter) {
            timestamp message severity
          }
        }
        """;

    public const string HttpLogs = """
        query httpLogs($deploymentId: String!, $limit: Int) {
          httpLogs(deploymentId: $deploymentId, limit: $limit) {
            timestamp requestId method path httpStatus totalDuration srcIp
          }
        }
        """;

    public const string Variables =
        "query variables($projectId: String!, $environmentId: String!, $serviceId: String) { " +
        "variables(projectId: $projectId, environmentId: $environmentId, serviceId: $serviceId) }";

    public const string VariableCollectionUpsert =
        "mutation variableCollectionUpsert($input: VariableCollectionUpsertInput!) { " +
        "variableCollectionUpsert(input: $input) }";

    // Deployment mutations (doc §7). Redeploy/rollback return { id status }; the rest return a scalar.
    public const string DeploymentRedeploy = "mutation deploymentRedeploy($id: String!) { deploymentRedeploy(id: $id) { id status } }";
    public const string DeploymentRollback = "mutation deploymentRollback($id: String!) { deploymentRollback(id: $id) { id status } }";
    public const string DeploymentRestart = "mutation deploymentRestart($id: String!) { deploymentRestart(id: $id) }";
    public const string DeploymentStop = "mutation deploymentStop($id: String!) { deploymentStop(id: $id) }";
    public const string DeploymentCancel = "mutation deploymentCancel($id: String!) { deploymentCancel(id: $id) }";
    public const string DeploymentRemove = "mutation deploymentRemove($id: String!) { deploymentRemove(id: $id) }";

    public const string ServiceInstanceDeploy =
        "mutation serviceInstanceDeploy($serviceId: String!, $environmentId: String!) { " +
        "serviceInstanceDeploy(serviceId: $serviceId, environmentId: $environmentId) }";

    // ── Metrics [VERIFIED 2026-07-09] ────────────────────────────────────────────────
    // Confirmed working against a live Railway account: CPU_USAGE / MEMORY_USAGE_GB / NETWORK_RX_GB /
    // NETWORK_TX_GB all returned real `values { ts value }` data. If Railway ever changes the schema and
    // this errors, re-confirm the field/enum names in GraphiQL and fix them HERE (the one place they live).
    public const string Metrics = """
        query metrics($startDate: DateTime!, $measurements: [MetricMeasurement!]!, $environmentId: String, $serviceId: String, $sampleRateSeconds: Int) {
          metrics(startDate: $startDate, measurements: $measurements, environmentId: $environmentId, serviceId: $serviceId, sampleRateSeconds: $sampleRateSeconds) {
            measurement
            values { ts value }
          }
        }
        """;

    /// <summary>Measurements requested for the metrics graph. Adjust to match Railway's MetricMeasurement enum.</summary>
    public static readonly string[] MetricMeasurements =
        ["CPU_USAGE", "MEMORY_USAGE_GB", "NETWORK_RX_GB", "NETWORK_TX_GB"];
}
