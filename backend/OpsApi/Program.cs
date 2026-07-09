using System.Text;
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
        };
    });
builder.Services.AddAuthorization();

// ── CORS ────────────────────────────────────────────────────────────────────────
var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:3003"];
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

// ── MVC + SignalR + Swagger ─────────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<LogStreamHub>("/hubs/logs");
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "ops-api" }));

using (var scope = app.Services.CreateScope())
{
    await OpsBootstrap.InitializeAsync(scope.ServiceProvider);
}

app.Run();
