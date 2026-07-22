using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PlatformApi.Auth;
using PlatformApi.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<PlatformDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddSingleton<PlatformJwtService>();
builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });
// PLAT-04: require the platform_admin role claim on every authorized endpoint, not just a valid
// signature. Defense-in-depth against token confusion with RestaurantApi/OpsApi if an operator ever
// reuses a signing key or aligns issuer/audience across the services sharing this Postgres DB.
// Set as both the default policy (bare [Authorize]) and the fallback policy (endpoints with no
// [Authorize] at all); the anonymous login action opts out with [AllowAnonymous].
builder.Services.AddAuthorization(options =>
{
    var platformAdminOnly = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireClaim("role", "platform_admin")
        .Build();
    options.DefaultPolicy = platformAdminOnly;
    options.FallbackPolicy = platformAdminOnly;
});

// PLAT-05: throttle the platform login. Two chained limiters gate /api/platform/auth/login — one
// per client IP (single-source brute force) and one per submitted email (credential stuffing against
// one super-admin account across many IPs). A request must pass BOTH. All other paths are unlimited.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
        PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        {
            if (!IsPlatformLogin(ctx)) return RateLimitPartition.GetNoLimiter("no-limit");
            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return RateLimitPartition.GetFixedWindowLimiter(
                $"ip:{ip}",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
        }),
        PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        {
            if (!IsPlatformLogin(ctx)) return RateLimitPartition.GetNoLimiter("no-limit");
            var email = ctx.Items["login-email"] as string ?? "unknown";
            return RateLimitPartition.GetFixedWindowLimiter(
                $"email:{email}",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 });
        }));

    static bool IsPlatformLogin(HttpContext ctx) =>
        HttpMethods.IsPost(ctx.Request.Method) &&
        ctx.Request.Path.StartsWithSegments("/api/platform/auth/login", StringComparison.OrdinalIgnoreCase);
});

var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:3002"];
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// PLAT-01 / INFRA-02: fail fast outside Development if the JWT signing key or DB connection string is
// missing or still a committed dev/test placeholder. Otherwise a prod deploy that forgets the secrets
// would run on a publicly-known (forgeable) HMAC key granting full super-admin. (Mirrors OpsApi's
// RequireSecret guard; the base appsettings.json ships these blank and the dev values live only in the
// dockerignored appsettings.Development.json.)
if (!app.Environment.IsDevelopment())
{
    RequireSecret(app.Configuration["Jwt:Key"], "Jwt:Key");
    RequireConnectionString(app.Configuration.GetConnectionString("Default"), "ConnectionStrings:Default");

    static void RequireSecret(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)
            || Encoding.UTF8.GetByteCount(value) < 32
            || value.StartsWith("dev-only", StringComparison.Ordinal)
            || value.StartsWith("test-only", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{name} must be set to a non-default secret of at least 32 bytes outside Development " +
                $"(set it via the {name.Replace(":", "__")} environment variable).");
        }
    }

    static void RequireConnectionString(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{name} must be configured outside Development.");
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

// PLAT-05 support: buffer the login request body so the per-email rate limiter can partition on the
// submitted email. Scoped to the login path only; the body is rewound so MVC model binding is intact.
app.Use(async (context, next) =>
{
    if (HttpMethods.IsPost(context.Request.Method) &&
        context.Request.Path.StartsWithSegments("/api/platform/auth/login", StringComparison.OrdinalIgnoreCase))
    {
        context.Request.EnableBuffering();
        try
        {
            using var reader = new StreamReader(
                context.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (string.Equals(prop.Name, "email", StringComparison.OrdinalIgnoreCase)
                            && prop.Value.ValueKind == JsonValueKind.String)
                        {
                            context.Items["login-email"] = prop.Value.GetString()?.Trim().ToLowerInvariant();
                            break;
                        }
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Malformed JSON — leave login-email unset; MVC model binding will return the 400.
        }
    }
    await next();
});

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// PLAT-06: re-validate the platform admin (exists + IsActive) on every authenticated request so that
// deactivating or deleting an admin revokes their outstanding bearer token immediately, instead of the
// token staying valid for its full lifetime on signature alone (there is no refresh/jti denylist).
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true)
    {
        var adminId = context.User.PlatformAdminId();
        var db = context.RequestServices.GetRequiredService<PlatformDbContext>();
        var active = adminId is Guid id
            && await db.PlatformAdmins.AsNoTracking().AnyAsync(a => a.Id == id && a.IsActive);
        if (!active)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
    }
    await next();
});

app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    await PlatformBootstrap.InitializeAsync(scope.ServiceProvider);
}

app.Run();
