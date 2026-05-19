using Microsoft.EntityFrameworkCore;
using RestaurantApi.Data;
using RestaurantApi.Hubs;
using RestaurantApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

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

builder.Services.AddScoped<RecommendationService>();
builder.Services.AddScoped<SimilarityService>();
builder.Services.AddScoped<IFlashDealService, FlashDealService>();

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

app.UseCors();

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
    await SeedData.InitializeAsync(db);
}

app.Run();

public partial class Program;