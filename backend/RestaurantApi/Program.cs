using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RestaurantApi.Auth;
using RestaurantApi.Data;
using RestaurantApi.Hubs;
using RestaurantApi.Models;
using RestaurantApi.Options;
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
// Where user uploads live on disk. Overridable so production can point it at a
// persistent volume (docs/railway-persistent-uploads.md) — without that, every
// redeploy wipes branding/avatars/menu-item images. Resolved lazily so test
// hosts' content-root and config overrides are already in effect.
builder.Services.AddSingleton(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    return new UploadPaths(
        cfg["Uploads:Root"] ?? Path.Combine(env.ContentRootPath, "wwwroot", "uploads"));
});

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

// The JWT signing key must be a real secret in Production/Staging. A length-only check
// is not enough: the committed placeholders are long enough to pass it, so a prod deploy
// that forgets Jwt__Key would boot on a publicly-known key and every token would be
// forgeable. Reject empty/short values AND the known dev/test placeholders. Development
// and Testing run with their committed dev keys by design. (Mirrors OpsApi's RequireSecret.)
if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
{
    if (string.IsNullOrWhiteSpace(jwt.Key)
        || System.Text.Encoding.UTF8.GetByteCount(jwt.Key) < 32
        || jwt.Key.StartsWith("test-only", StringComparison.Ordinal)
        || jwt.Key.StartsWith("dev-only", StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            "Jwt:Key must be set to a non-default secret of at least 32 bytes outside Development. " +
            "Set it via the Jwt__Key environment variable (never commit it).");
    }
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
    .AddJwtBearer();

// Bind bearer validation from JwtOptions LAZILY (when the options are first
// built) rather than from the eager `jwt` snapshot above. The snapshot is fine
// for real runs (env/appsettings exist at process start) but misses configuration
// added late by test hosts (WebApplicationFactory's in-memory collection), which
// left the validation key empty and 500'd every authenticated request in tests —
// token issuing (JwtTokenService) already binds the same section lazily.
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((options, jwtOptions) =>
    {
        var j = jwtOptions.Value;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = j.Issuer,
            ValidAudience = j.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(j.Key)),
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
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));

// Railway (and most PaaS) block outbound SMTP ports, so production sends through Resend's
// HTTP API (port 443). Local dev keeps the MailKit SMTP sender (e.g. mailpit). Selected
// by Email:Provider. The HttpClient timeout caps the register request so a slow/blocked
// provider can never freeze it (the old SMTP path hung for the full ~2-minute timeout).
var emailProvider = builder.Configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>()?.Provider ?? "smtp";
var usingResend = string.Equals(emailProvider, "resend", StringComparison.OrdinalIgnoreCase);
if (usingResend)
{
    builder.Services.AddHttpClient<IEmailSender, ResendEmailSender>(c =>
        c.Timeout = TimeSpan.FromSeconds(15));
}
else
{
    builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
}

// Warn loudly if Production is about to send mail through the SMTP dev defaults —
// a misconfigured SMTP host/From silently breaks confirmation emails. Skipped when
// using Resend, where Smtp:Host/From are irrelevant.
if (builder.Environment.IsProduction() && !usingResend)
{
    var smtpCheck = builder.Configuration.GetSection(SmtpOptions.SectionName).Get<SmtpOptions>() ?? new SmtpOptions();
    if (smtpCheck.Host is "localhost" or "127.0.0.1"
        || smtpCheck.FromAddress.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine(
            "[WARN] SMTP is using development defaults in Production — confirmation emails will NOT be delivered. " +
            "Set Email__Provider=resend (recommended on Railway) or Smtp__Host/Smtp__FromAddress (see docs/email-deliverability-setup.md).");
    }
}

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
builder.Services.Configure<CustomerSessionOptions>(builder.Configuration.GetSection(CustomerSessionOptions.SectionName));
builder.Services.AddSingleton<MenuImportProgressNotifier>();
builder.Services.AddSingleton<ItemPhotoStorage>();
builder.Services.AddScoped<MenuImportPhotoMerger>();
builder.Services.AddHostedService<MenuImportBackgroundService>();

builder.Services.AddMemoryCache();

builder.Services.AddScoped<RecommendationService>();
builder.Services.AddScoped<SimilarityService>();
builder.Services.AddScoped<IStockService, StockService>();
builder.Services.AddScoped<IOrderEventWriter, OrderEventWriter>();
builder.Services.AddScoped<ITableSessionService, TableSessionService>();
builder.Services.AddScoped<IFlashDealService, FlashDealService>();
builder.Services.AddScoped<IHappyHourService, HappyHourService>();
builder.Services.AddHostedService<OrderFinalizationService>();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy =
            System.Text.Json.JsonNamingPolicy.CamelCase;
    });

// Produce a consistent, detail-free ProblemDetails body for unhandled errors
// (paired with app.UseExceptionHandler() below in non-Development environments).
builder.Services.AddProblemDetails();

// Brute-force / abuse protection on the credential-testing auth endpoints:
// fixed window per client IP. Applied via [EnableRateLimiting("auth")].
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
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
else
{
    // Production/Staging: turn unhandled exceptions into a detail-free ProblemDetails
    // response instead of leaking a stack trace or emitting a bare bodyless 500.
    // Development keeps the framework's developer exception page.
    app.UseExceptionHandler();
}

app.UseRouting();
app.UseCors();
app.UseRateLimiter();

// Serve uploaded files at /uploads from the (possibly volume-backed) uploads
// root, creating it on first start — the published container has no checked-in
// wwwroot, and a freshly attached volume starts empty.
var uploads = app.Services.GetRequiredService<UploadPaths>();
Directory.CreateDirectory(uploads.Dir("avatars"));
Directory.CreateDirectory(uploads.Dir("menu-items"));
Directory.CreateDirectory(uploads.Dir("branding"));
app.UseStaticFiles(new StaticFileOptions
{
    RequestPath = "/uploads",
    FileProvider = new PhysicalFileProvider(uploads.Root),
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
