using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OpsApi.Providers.Abstractions;

namespace OpsApi.Providers.Railway;

/// <summary>
/// Thin GraphQL client for backboard.railway.com. Holds no token itself — credentials are passed
/// per call so a single client instance serves every connection. Rate-limit aware: honours
/// Retry-After and retries 429/5xx with exponential backoff (doc §2, §11).
/// </summary>
public sealed class RailwayGraphQLClient
{
    private readonly HttpClient _http;
    private readonly ILogger<RailwayGraphQLClient> _logger;
    private readonly string _endpoint;

    public RailwayGraphQLClient(HttpClient http, IConfiguration config, ILogger<RailwayGraphQLClient> logger)
    {
        _http = http;
        _logger = logger;
        _endpoint = config["Ops:Railway:GraphqlEndpoint"] ?? "https://backboard.railway.com/graphql/v2";
    }

    /// <summary>Executes a GraphQL query/mutation and returns the <c>data</c> element (cloned, detached).</summary>
    public async Task<JsonElement> QueryAsync(string query, object? variables, ProviderCredentials creds, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new { query, variables });
        const int maxAttempts = 4;

        for (var attempt = 1; ; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };

            // Project tokens use a different header (doc capcana #1).
            if (string.Equals(creds.TokenKind, "project", StringComparison.OrdinalIgnoreCase))
                req.Headers.TryAddWithoutValidation("Project-Access-Token", creds.Token);
            else
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", creds.Token);

            HttpResponseMessage resp;
            try
            {
                resp = await _http.SendAsync(req, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (attempt >= maxAttempts) throw new ProviderException($"Railway unreachable: {ex.Message}");
                await Task.Delay(Backoff(attempt), ct);
                continue;
            }

            var remaining = resp.Headers.TryGetValues("X-RateLimit-Remaining", out var r) ? r.FirstOrDefault() : null;
            if (remaining is not null) _logger.LogDebug("Railway rate-limit remaining: {Remaining}", remaining);

            if ((int)resp.StatusCode == 429 || (int)resp.StatusCode >= 500)
            {
                if (attempt >= maxAttempts)
                    throw new ProviderException($"Railway API error ({(int)resp.StatusCode}).",
                        (int)resp.StatusCode == 429 ? 429 : 502);
                var wait = RetryAfter(resp) ?? Backoff(attempt);
                _logger.LogWarning("Railway {Status}; retry in {Ms}ms (attempt {Attempt})",
                    (int)resp.StatusCode, wait.TotalMilliseconds, attempt);
                await Task.Delay(wait, ct);
                continue;
            }

            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new ProviderException("Railway rejected the token (401/403). Check the token and its scope.", 401);

            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                throw new ProviderException($"Railway API error ({(int)resp.StatusCode}): {Truncate(body)}");

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("errors", out var errors) &&
                errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
            {
                var msg = errors[0].TryGetProperty("message", out var m) ? m.GetString() : "unknown GraphQL error";
                throw new ProviderException($"Railway GraphQL error: {msg}");
            }

            if (!root.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null)
                throw new ProviderException("Railway response contained no data.");

            return data.Clone();
        }
    }

    private static TimeSpan Backoff(int attempt) => TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt - 1));

    private static TimeSpan? RetryAfter(HttpResponseMessage resp)
    {
        if (resp.Headers.RetryAfter?.Delta is { } delta) return delta;
        if (resp.Headers.TryGetValues("Retry-After", out var vals) &&
            int.TryParse(vals.FirstOrDefault(), out var secs))
            return TimeSpan.FromSeconds(secs);
        return null;
    }

    private static string Truncate(string s) => s.Length <= 300 ? s : s[..300];
}
