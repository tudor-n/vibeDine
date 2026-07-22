using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpsApi.Auth;
using OpsApi.Data;
using OpsApi.Hubs;
using OpsApi.Providers;
using OpsApi.Providers.Abstractions;
using OpsApi.Providers.Railway;
using OpsApi.Providers.Vps;
using OpsApi.Security;
using OpsApi.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Data ────────────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<OpsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// ── Auth / crypto ─────────────────────────────────────────────────────────────────
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddSingleton<OpsJwtService>();
builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
builder.Services.AddSingleton<ITokenProtector, AesGcmTokenProtector>();

// ── Providers (the modular core) ───────────────────────────────────────────────────
builder.Services.AddHttpClient<RailwayGraphQLClient>();
builder.Services.AddScoped<IInfraProvider, RailwayProvider>();
builder.Services.AddScoped<IInfraProvider, VpsProvider>();
builder.Services.AddScoped<ProviderRegistry>();
builder.Services.AddScoped<ConnectionService>();
builder.Services.AddScoped<AuditLogger>();
builder.Services.AddSingleton<LogTailManager>();

// ── JWT bearer (also reads the access token from the query string for the SignalR hub) ─
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
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            },
            // Access tokens are stateless with a long lifetime, so re-check on every authenticated request
            // that the admin still exists and is active. This gives immediate cutoff when an ops admin is
            // deactivated/deleted (no revocation list needed) and covers both controllers and the hub.
            OnTokenValidated = async context =>
            {
                var adminId = context.Principal?.OpsAdminId();
                if (adminId is null)
                {
                    context.Fail("Token is missing a valid subject.");
                    return;
                }

                var db = context.HttpContext.RequestServices.GetRequiredService<OpsDbContext>();
                var isActive = await db.Admins
                    .Where(a => a.Id == adminId.Value && a.IsActive)
                    .AnyAsync(context.HttpContext.RequestAborted);
                if (!isActive)
                    context.Fail("Ops admin is no longer active.");
            },
        };
    });
builder.Services.AddAuthorization();

// ── CORS ────────────────────────────────────────────────────────────────────────
var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:3003"];
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        // Bearer-token auth only (no cookies) — credentials are not needed and would loosen CORS.
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

// ── MVC + SignalR + Swagger ─────────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Basic brute-force protection on login: fixed window per client IP.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var app = builder.Build();

// Fail-fast outside Development if the JWT/encryption secrets are missing or still the committed dev
// defaults — otherwise a prod deploy that forgets the env vars would ship forgeable ops-admin JWTs and
// provider tokens encrypted under a publicly known key. (Mirrors RestaurantApi's Jwt:Key guard.)
if (!app.Environment.IsDevelopment())
{
    RequireSecret(app.Configuration["Jwt:Key"], "Jwt:Key");
    RequireSecret(app.Configuration["Ops:Encryption:Key"], "Ops:Encryption:Key");
    // The seed password bootstraps a full super-admin over all infra; never let a prod deploy ship the
    // committed default (or an unset value) which would seed a login-able admin with public credentials.
    RequireSeedPassword(app.Configuration["OpsSeed:Password"]);

    static void RequireSecret(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith("dev-only", StringComparison.Ordinal))
            throw new InvalidOperationException($"{name} must be set to a non-default secret outside Development.");
    }

    static void RequireSeedPassword(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "ChangeMe123!")
            throw new InvalidOperationException(
                "OpsSeed:Password must be set to a non-default secret outside Development.");
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapHub<LogStreamHub>("/hubs/logs");
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "ops-api" }));

using (var scope = app.Services.CreateScope())
{
    await OpsBootstrap.InitializeAsync(scope.ServiceProvider);
}

app.Run();
