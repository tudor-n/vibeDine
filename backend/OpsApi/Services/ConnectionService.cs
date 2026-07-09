using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpsApi.Data;
using OpsApi.Models;
using OpsApi.Providers;
using OpsApi.Providers.Abstractions;
using OpsApi.Security;

namespace OpsApi.Services;

/// <summary>
/// Central place that turns a stored <see cref="Connection"/> into an actionable
/// (provider, decrypted context) pair. The token is decrypted here and only here, and never
/// leaves the process. Every controller that touches a provider goes through this.
/// </summary>
public sealed class ConnectionService
{
    private readonly OpsDbContext _db;
    private readonly ITokenProtector _protector;
    private readonly ProviderRegistry _registry;

    public ConnectionService(OpsDbContext db, ITokenProtector protector, ProviderRegistry registry)
    {
        _db = db;
        _protector = protector;
        _registry = registry;
    }

    public async Task<(Connection connection, IInfraProvider provider, ProviderContext ctx)> ResolveAsync(
        Guid connectionId, CancellationToken ct)
    {
        var conn = await _db.Connections.FirstOrDefaultAsync(c => c.Id == connectionId, ct)
            ?? throw new ProviderException("Connection not found.", 404);

        var provider = _registry.Get(conn.Provider);
        var token = _protector.Unprotect(conn.TokenCipher);
        var metadata = ParseMetadata(conn.MetadataJson);
        var ctx = new ProviderContext(conn.Id, conn.Provider,
            new ProviderCredentials(token, conn.TokenKind), metadata);
        return (conn, provider, ctx);
    }

    public static IReadOnlyDictionary<string, string?> ParseMetadata(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string?>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(json)
                   ?? new Dictionary<string, string?>();
        }
        catch
        {
            return new Dictionary<string, string?>();
        }
    }
}
