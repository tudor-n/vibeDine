namespace OpsApi.Models;

/// <summary>Provider kinds. Add a value + an IInfraProvider implementation to support a new backend.</summary>
public static class ProviderKind
{
    public const string Railway = "railway";
    public const string Vps = "vps";
}

/// <summary>Connection health.</summary>
public static class ConnectionStatus
{
    public const string Active = "active";
    public const string Invalid = "invalid";   // credentials rejected on last check
    public const string Error = "error";        // provider unreachable / transient failure
}

/// <summary>
/// Free-form role tag so a fleet of servers can be labelled by purpose. The future setup is
/// "a VPS for the DB, a VPS for the app" — each is its own connection with a distinct role.
/// </summary>
public static class ServerRole
{
    public const string App = "app";
    public const string Db = "db";
    public const string Worker = "worker";
    public const string Other = "other";
}

/// <summary>
/// A stored connection to an infrastructure provider. Credentials are AES-GCM encrypted in
/// <see cref="TokenCipher"/> and never returned to the client. HQ-owned table `ops_connections`.
/// </summary>
public sealed class Connection
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>One of <see cref="ProviderKind"/>.</summary>
    public string Provider { get; set; } = ProviderKind.Railway;

    /// <summary>Human label shown in the UI.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Purpose tag for fleet grouping (one of <see cref="ServerRole"/>). Optional.</summary>
    public string? Role { get; set; }

    /// <summary>Encrypted credential blob (Railway API token / SSH key). Server-side only.</summary>
    public string TokenCipher { get; set; } = string.Empty;

    /// <summary>Railway token flavour ("account" | "workspace" | "project") or provider-specific hint.</summary>
    public string? TokenKind { get; set; }

    /// <summary>Discovered account/workspace label for display (e.g. Railway account name).</summary>
    public string? AccountLabel { get; set; }

    /// <summary>Extra non-secret config as JSON (host/port/user for VPS, workspaceId for Railway, etc.).</summary>
    public string? MetadataJson { get; set; }

    /// <summary>Capabilities snapshot as JSON, captured at validation time (drives the UI).</summary>
    public string? CapabilitiesJson { get; set; }

    public string Status { get; set; } = ConnectionStatus.Active;
    public string? StatusDetail { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastCheckedAt { get; set; }
    public Guid CreatedByAdminId { get; set; }
}
