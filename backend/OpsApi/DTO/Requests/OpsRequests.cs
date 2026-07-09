using System.ComponentModel.DataAnnotations;

namespace OpsApi.DTO.Requests;

public sealed class LoginRequest
{
    [Required, EmailAddress] public string Email { get; set; } = string.Empty;
    [Required] public string Password { get; set; } = string.Empty;
}

/// <summary>
/// The "easy connect" payload. For Railway: Token = API token, TokenKind = account|workspace|project.
/// For a VPS: Token = SSH key/password, Metadata carries host/port/user. The token is validated and
/// encrypted server-side — it is never stored or returned in plaintext.
/// </summary>
public sealed class CreateConnectionRequest
{
    [Required] public string Provider { get; set; } = "railway";
    [Required] public string Name { get; set; } = string.Empty;
    public string? Role { get; set; }
    [Required] public string Token { get; set; } = string.Empty;
    public string? TokenKind { get; set; }
    public Dictionary<string, string?> Metadata { get; set; } = new();
}

public sealed class UpdateConnectionRequest
{
    public string? Name { get; set; }
    public string? Role { get; set; }
}

public sealed class UpsertVariablesRequest
{
    [Required] public string ProjectId { get; set; } = string.Empty;
    [Required] public string EnvironmentId { get; set; } = string.Empty;
    public string? ServiceId { get; set; }
    public Dictionary<string, string> Variables { get; set; } = new();

    /// <summary>Replaces the ENTIRE variable set — destructive; the UI must confirm.</summary>
    public bool Replace { get; set; }
    public bool SkipDeploys { get; set; }
}

public sealed class TriggerDeployRequest
{
    [Required] public string ServiceId { get; set; } = string.Empty;
    [Required] public string EnvironmentId { get; set; } = string.Empty;
}
