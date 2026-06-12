using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using RestaurantApi.Auth;
using RestaurantApi.Data;
using RestaurantApi.Hubs;
using RestaurantApi.Models;
using RestaurantApi.Services;
using RestaurantApi.Services.Email;
using RestaurantApi.Storage;
using RestaurantApi.Tenancy;

var builder = WebApplication.CreateBuilder(args);

// Increase Kestrel's request body limit for PDF uploads (up to 110 MB).
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 110_000_000);

// ── Tenancy ──────────────────────────────────────────────────────────────────
builder.Services.AddScoped<ICurrentTenant, CurrentTenant>();
builder.Services.AddScoped<ITenantAccess, TenantAccess>();
builder.Services.AddSingleton<IFileStorage, LocalDiskFileStorage>();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// ── Auth (ASP.NET Identity + JWT) ────────────────────────────────────────────
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddSingleton<JwtTokenService>();

builder.Services
    .AddIdentityCore<AppUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

// HMAC-SHA256 needs a >= 256-bit (32-byte) key. Fail fast at startup with a clear
// message instead of a confusing per-request 500 from the auth middleware.
if (System.Text.Encoding.UTF8.GetByteCount(jwt.Key) < 32)
{
    throw new InvalidOperationException(
        "Jwt:Key is missing or shorter than 32 bytes. Set it via the Jwt__Key environment " +
        "variable (appsettings.Development.json is excluded from the Docker image by .dockerignore).");
}

// MenuImport HMAC secret fail-fast: require at least 32 characters.
var menuImportHmacSecret = builder.Configuration["MenuImport:HmacSecret"];
if (!builder.Environment.IsEnvironment("Testing") &&
    (string.IsNullOrWhiteSpace(menuImportHmacSecret) || menuImportHmacSecret.Length < 32))
{
    throw new InvalidOperationException(
        "MenuImport:HmacSecret is missing or shorter than 32 characters. Set it via " +
        "appsettings.Development.json or the MenuImport__HmacSecret environment variable.");
}

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

        // Allow SignalR clients to authenticate via the access_token query string.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// ── Email ────────────────────────────────────────────────────────────────────
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection(SmtpOptions.SectionName));
builder.Services.Configure<AppUrlOptions>(builder.Configuration.GetSection(AppUrlOptions.SectionName));
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();

// ── AI microservice clients ──────────────────────────────────────────────────
builder.Services.AddHttpClient("AiService", (sp, client) =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var url = cfg["AiService:BaseUrl"];
    if (string.IsNullOrWhiteSpace(url))
    {
        if (env.IsDevelopment() || env.IsEnvironment("Testing"))
        {
            url = "http://localhost:8000";
        }
        else
        {
            throw new InvalidOperationException(
                "AiService:BaseUrl is not configured. Set it via appsettings or the AiService__BaseUrl environment variable.");
        }
    }
    client.BaseAddress = new Uri(url);
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddHttpClient<MenuExtractionClient>((sp, client) =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var url = cfg["AiService:BaseUrl"];
    if (string.IsNullOrWhiteSpace(url))
    {
        if (env.IsDevelopment() || env.IsEnvironment("Testing"))
        {
            url = "http://localhost:8000";
        }
        else
        {
            throw new InvalidOperationException(
                "AiService:BaseUrl is not configured. Set it via appsettings or the AiService__BaseUrl environment variable.");
        }
    }
    client.BaseAddress = new Uri(url);
    client.Timeout = TimeSpan.FromMinutes(6);
});

// ── MenuImport Python client (long-running extraction; 30-min timeout) ────────
builder.Services.AddHttpClient<MenuImportPythonClient>((sp, client) =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var url = cfg["AiService:BaseUrl"];
    if (string.IsNullOrWhiteSpace(url))
    {
        if (env.IsDevelopment() || env.IsEnvironment("Testing"))
        {
            url = "http://localhost:8000";
        }
        else
        {
            throw new InvalidOperationException(
                "AiService:BaseUrl is not configured. Set it via appsettings or the AiService__BaseUrl environment variable.");
        }
    }
    client.BaseAddress = new Uri(url);
    client.Timeout = TimeSpan.FromMinutes(30);
});

// Register MenuImportPythonClient as IPdfExtractor for the background service.
builder.Services.AddTransient<IPdfExtractor, MenuImportPythonClient>(sp =>
    sp.GetRequiredService<MenuImportPythonClient>());

builder.Services.Configure<MenuImportOptions>(builder.Configuration.GetSection("MenuImport"));
builder.Services.AddSingleton<MenuImportProgressNotifier>();
builder.Services.AddSingleton<ItemPhotoStorage>();
builder.Services.AddScoped<MenuImportPhotoMerger>();
builder.Services.AddHostedService<MenuImportBackgroundService>();

builder.Services.AddScoped<RecommendationService>();
builder.Services.AddScoped<SimilarityService>();
builder.Services.AddScoped<IFlashDealService, FlashDealService>();
builder.Services.AddHostedService<OrderFinalizationService>();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy =
            System.Text.Json.JsonNamingPolicy.CamelCase;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.PropertyNamingPolicy =
            System.Text.Json.JsonNamingPolicy.CamelCase;
    });

var allowedOrigins = builder.Configuration
    .GetSection("AllowedOrigins")
    .Get<string[]>()
    ?? ["http://localhost:3000", "http://localhost:3001"];

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();
app.UseCors();

// Serve uploaded files out of ContentRoot/wwwroot even when the published
// container starts without a checked-in wwwroot directory.
var webRoot = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
Directory.CreateDirectory(Path.Combine(webRoot, "uploads", "avatars"));
Directory.CreateDirectory(Path.Combine(webRoot, "uploads", "menu-items"));
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(webRoot),
});

app.UseAuthentication();
app.UseAuthorization();

// Resolves the active restaurant (route restaurantId / qrToken) and enforces
// membership before any tenant-scoped endpoint runs.
app.UseMiddleware<TenantResolutionMiddleware>();

app.MapControllers();

app.MapHub<CustomerHub>("/hubs/customer");
app.MapHub<StaffHub>("/hubs/staff");

// Auto-migrate and seed only in Development. In production, run
// `dotnet ef database update` as part of the deploy pipeline so a bad
// migration never blocks the API from starting.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    await SeedData.InitializeAsync(scope.ServiceProvider);
}

app.Run();

public partial class Program;
