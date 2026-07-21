using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RestaurantApi.Data;
using RestaurantApi.Models;
using RestaurantApi.Options;
using RestaurantApi.Tenancy;
using RestaurantApi.Tests.Helpers;
using Testcontainers.PostgreSql;
using Xunit;

namespace RestaurantApi.Tests;

// Task 2: PartyController.Join resolves a device-bound session (Task 1's
// DeviceSession) when the client sends a deviceId, restores the visit's
// cumulative submitted orders + running total, and restores any still-pending
// flash deal. Also covers CustomerController.SubmitOrder bumping the session's
// LastSeenAt. Follows the direct-DB-seeding + MenuImportTestFactory pattern
// used by FlashDealRuleSaveTests / CustomerBootstrapBrandingTests.
public class DeviceSessionTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private MenuImportTestFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        _factory = new MenuImportTestFactory { PostgresConnectionString = _pg.GetConnectionString() };

        using (var migrateScope = _factory.Services.CreateScope())
        {
            var migrateDb = migrateScope.ServiceProvider.GetRequiredService<AppDbContext>();
            await migrateDb.Database.MigrateAsync();
        }

        // Anonymous customer endpoints, scoped only by qrToken/restaurantId route
        // values — no membership/JWT needed.
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        await _pg.DisposeAsync();
    }

    private static void SetTenant(IServiceScope scope, Guid restaurantId) =>
        scope.ServiceProvider.GetRequiredService<ICurrentTenant>().SetRestaurant(restaurantId);

    private int GetLinkExpirySeconds()
    {
        using var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IOptions<CustomerSessionOptions>>().Value.LinkExpirySeconds;
    }

    private int GetSessionLifetimeMinutes()
    {
        using var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IOptions<CustomerSessionOptions>>().Value.SessionLifetimeMinutes;
    }

    private async Task<(Guid QrToken, Guid RestaurantId, Guid TableId)> SeedTableAsync()
    {
        var restaurantId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var qrToken = Guid.NewGuid();
        var tableId = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.Set<Organization>().Add(new Organization
        {
            Id = orgId,
            Name = "Device Session Org",
            Slug = "dso-" + orgId.ToString("N")[..6],
            CreatedByUserId = Guid.NewGuid(),
        });
        db.Restaurants.Add(new Restaurant
        {
            Id = restaurantId,
            OrganizationId = orgId,
            Name = "Device Session Restaurant",
            Slug = "dsr-" + restaurantId.ToString("N")[..6],
            IsActive = true,
        });
        db.Tables.Add(new RestaurantTable
        {
            Id = tableId,
            RestaurantId = restaurantId,
            TableNumber = "T1",
            QrToken = qrToken,
        });
        await db.SaveChangesAsync();

        return (qrToken, restaurantId, tableId);
    }

    private async Task<Guid> AddTableAsync(Guid restaurantId, string tableNumber)
    {
        var qrToken = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Tables.Add(new RestaurantTable
        {
            Id = Guid.NewGuid(),
            RestaurantId = restaurantId,
            TableNumber = tableNumber,
            QrToken = qrToken,
        });
        await db.SaveChangesAsync();
        return qrToken;
    }

    private async Task<(Guid ProductId, decimal Price)> SeedProductAsync(
        Guid restaurantId, string name = "Test Product", decimal price = 15.50m)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var category = new Category { Id = Guid.NewGuid(), RestaurantId = restaurantId, Name = "Category", SortOrder = 0 };
        db.Set<Category>().Add(category);

        var product = new Product
        {
            Id = Guid.NewGuid(),
            RestaurantId = restaurantId,
            CategoryId = category.Id,
            Name = name,
            Price = price,
            IsAvailable = true,
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        return (product.Id, product.Price);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage res)
    {
        var stream = await res.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }

    private Task<HttpResponseMessage> JoinAsync(Guid qrToken, string? deviceId) =>
        _client.PostAsJsonAsync($"/api/tables/{qrToken}/join", new { deviceId });

    private Task<HttpResponseMessage> JoinAsync(Guid qrToken, object body) =>
        _client.PostAsJsonAsync($"/api/tables/{qrToken}/join", body);

    private Task<HttpResponseMessage> AddItemAsync(Guid restaurantId, Guid orderId, Guid productId, int quantity) =>
        _client.PatchAsJsonAsync(
            $"/api/restaurants/{restaurantId}/orders/{orderId}/items",
            new { items = new[] { new { productId, quantity } } });

    private Task<HttpResponseMessage> SubmitAsync(Guid restaurantId, Guid orderId) =>
        _client.PostAsync($"/api/restaurants/{restaurantId}/orders/{orderId}/submit", null);

    [Fact]
    public async Task Join_WithDeviceId_CreatesSession()
    {
        var (qrToken, restaurantId, tableId) = await SeedTableAsync();
        var deviceId = "device-" + Guid.NewGuid().ToString("N");

        var res = await JoinAsync(qrToken, deviceId);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await ReadJsonAsync(res);
        var session = json.GetProperty("session");
        session.ValueKind.Should().NotBe(JsonValueKind.Null);

        session.GetProperty("linkExpirySeconds").GetInt32().Should().Be(GetLinkExpirySeconds());
        session.GetProperty("cumulativeTotal").GetDecimal().Should().Be(0m);
        session.GetProperty("orders").GetArrayLength().Should().Be(0);
        session.GetProperty("pendingDeal").ValueKind.Should().Be(JsonValueKind.Null);

        var sessionId = session.GetProperty("id").GetGuid();

        using var scope = _factory.Services.CreateScope();
        SetTenant(scope, restaurantId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.DeviceSessions.SingleOrDefaultAsync(d => d.Id == sessionId);
        row.Should().NotBeNull();
        row!.DeviceId.Should().Be(deviceId);
        row.TableId.Should().Be(tableId);
    }

    [Fact]
    public async Task Join_WithoutDeviceId_ReturnsNullSession()
    {
        var (qrToken, restaurantId, _) = await SeedTableAsync();

        async Task AssertNullSessionAsync(object body)
        {
            var res = await JoinAsync(qrToken, body);
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var json = await ReadJsonAsync(res);
            json.GetProperty("session").ValueKind.Should().Be(JsonValueKind.Null);
        }

        await AssertNullSessionAsync(new { });
        await AssertNullSessionAsync(new { deviceId = "" });
        await AssertNullSessionAsync(new { deviceId = new string('x', 65) });

        using var scope = _factory.Services.CreateScope();
        SetTenant(scope, restaurantId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.DeviceSessions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Join_SameDevice_ReusesSession()
    {
        var (qrToken, restaurantId, tableId) = await SeedTableAsync();
        var deviceId = "device-" + Guid.NewGuid().ToString("N");

        var res1 = await JoinAsync(qrToken, deviceId);
        var json1 = await ReadJsonAsync(res1);
        var sessionId1 = json1.GetProperty("session").GetProperty("id").GetGuid();

        // Age the row so the LastSeenAt bump on rejoin is observable.
        var old = DateTime.UtcNow.AddMinutes(-5);
        using (var scope = _factory.Services.CreateScope())
        {
            SetTenant(scope, restaurantId);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.DeviceSessions.Where(d => d.Id == sessionId1)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.LastSeenAt, old));
        }

        var res2 = await JoinAsync(qrToken, deviceId);
        var json2 = await ReadJsonAsync(res2);
        var sessionId2 = json2.GetProperty("session").GetProperty("id").GetGuid();

        sessionId2.Should().Be(sessionId1);

        using var verifyScope = _factory.Services.CreateScope();
        SetTenant(verifyScope, restaurantId);
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await verifyDb.DeviceSessions.CountAsync(d => d.TableId == tableId)).Should().Be(1);
        var row = await verifyDb.DeviceSessions.SingleAsync(d => d.Id == sessionId1);
        row.LastSeenAt.Should().BeAfter(old);
    }

    [Fact]
    public async Task Join_AfterSubmit_ReturnsCumulativeOrders()
    {
        var (qrToken, restaurantId, _) = await SeedTableAsync();
        var (productId, price) = await SeedProductAsync(restaurantId);
        var deviceId = "device-" + Guid.NewGuid().ToString("N");

        var joinRes = await JoinAsync(qrToken, deviceId);
        var joinJson = await ReadJsonAsync(joinRes);
        var draftOrderId = joinJson.GetProperty("activeOrder").GetProperty("id").GetGuid();
        var sessionId = joinJson.GetProperty("session").GetProperty("id").GetGuid();

        (await AddItemAsync(restaurantId, draftOrderId, productId, 2)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SubmitAsync(restaurantId, draftOrderId)).StatusCode.Should().Be(HttpStatusCode.OK);

        var rejoinRes = await JoinAsync(qrToken, deviceId);
        var rejoinJson = await ReadJsonAsync(rejoinRes);
        var session = rejoinJson.GetProperty("session");
        session.GetProperty("id").GetGuid().Should().Be(sessionId);

        var orders = session.GetProperty("orders");
        orders.GetArrayLength().Should().Be(1);
        var order = orders[0];
        order.GetProperty("id").GetGuid().Should().Be(draftOrderId);
        order.GetProperty("status").GetString().Should().BeOneOf("pending_dispatch", "submitted");

        session.GetProperty("cumulativeTotal").GetDecimal().Should().Be(price * 2);

        var newDraftId = rejoinJson.GetProperty("activeOrder").GetProperty("id").GetGuid();
        newDraftId.Should().NotBe(draftOrderId);

        using var scope = _factory.Services.CreateScope();
        SetTenant(scope, restaurantId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var newDraft = await db.Orders.SingleAsync(o => o.Id == newDraftId);
        newDraft.DeviceSessionId.Should().Be(sessionId);
    }

    [Fact]
    public async Task Join_TwoRounds_AccumulatesTotal()
    {
        var (qrToken, restaurantId, _) = await SeedTableAsync();
        var (productId, price) = await SeedProductAsync(restaurantId);
        var deviceId = "device-" + Guid.NewGuid().ToString("N");

        async Task SubmitRoundAsync(int quantity)
        {
            var joinRes = await JoinAsync(qrToken, deviceId);
            var joinJson = await ReadJsonAsync(joinRes);
            var draftOrderId = joinJson.GetProperty("activeOrder").GetProperty("id").GetGuid();

            (await AddItemAsync(restaurantId, draftOrderId, productId, quantity)).StatusCode.Should().Be(HttpStatusCode.OK);
            (await SubmitAsync(restaurantId, draftOrderId)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        await SubmitRoundAsync(1);
        await SubmitRoundAsync(2);

        var rejoinRes = await JoinAsync(qrToken, deviceId);
        var rejoinJson = await ReadJsonAsync(rejoinRes);
        var session = rejoinJson.GetProperty("session");

        session.GetProperty("orders").GetArrayLength().Should().Be(2);
        session.GetProperty("cumulativeTotal").GetDecimal().Should().Be(price * 3);
    }

    [Fact]
    public async Task Join_ExpiredSession_IsWipedAndRecreated()
    {
        var (qrToken, restaurantId, _) = await SeedTableAsync();
        var (productId, _) = await SeedProductAsync(restaurantId);
        var deviceId = "device-" + Guid.NewGuid().ToString("N");

        var joinRes = await JoinAsync(qrToken, deviceId);
        var joinJson = await ReadJsonAsync(joinRes);
        var draftOrderId = joinJson.GetProperty("activeOrder").GetProperty("id").GetGuid();
        var sessionId = joinJson.GetProperty("session").GetProperty("id").GetGuid();

        (await AddItemAsync(restaurantId, draftOrderId, productId, 1)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SubmitAsync(restaurantId, draftOrderId)).StatusCode.Should().Be(HttpStatusCode.OK);

        var lifetimeMinutes = GetSessionLifetimeMinutes();
        var expiredAt = DateTime.UtcNow.AddMinutes(-(lifetimeMinutes + 5));

        using (var scope = _factory.Services.CreateScope())
        {
            SetTenant(scope, restaurantId);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.DeviceSessions.Where(d => d.Id == sessionId)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.LastSeenAt, expiredAt));
        }

        var rejoinRes = await JoinAsync(qrToken, deviceId);
        var rejoinJson = await ReadJsonAsync(rejoinRes);
        var newSession = rejoinJson.GetProperty("session");
        var newSessionId = newSession.GetProperty("id").GetGuid();

        newSessionId.Should().NotBe(sessionId);
        newSession.GetProperty("orders").GetArrayLength().Should().Be(0);
        newSession.GetProperty("cumulativeTotal").GetDecimal().Should().Be(0m);

        using var verifyScope = _factory.Services.CreateScope();
        SetTenant(verifyScope, restaurantId);
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await verifyDb.DeviceSessions.AnyAsync(d => d.Id == sessionId)).Should().BeFalse();

        var oldOrder = await verifyDb.Orders.SingleAsync(o => o.Id == draftOrderId);
        oldOrder.DeviceSessionId.Should().BeNull();
    }

    [Fact]
    public async Task Join_SameDeviceOtherTable_SeparateSession()
    {
        var (qrToken1, restaurantId, _) = await SeedTableAsync();
        var qrToken2 = await AddTableAsync(restaurantId, "T2");
        var (productId, _) = await SeedProductAsync(restaurantId);
        var deviceId = "device-" + Guid.NewGuid().ToString("N");

        var join1Res = await JoinAsync(qrToken1, deviceId);
        var join1Json = await ReadJsonAsync(join1Res);
        var draftOrderId1 = join1Json.GetProperty("activeOrder").GetProperty("id").GetGuid();
        var session1Id = join1Json.GetProperty("session").GetProperty("id").GetGuid();

        (await AddItemAsync(restaurantId, draftOrderId1, productId, 1)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SubmitAsync(restaurantId, draftOrderId1)).StatusCode.Should().Be(HttpStatusCode.OK);

        var join2Res = await JoinAsync(qrToken2, deviceId);
        var join2Json = await ReadJsonAsync(join2Res);
        var session2 = join2Json.GetProperty("session");
        var session2Id = session2.GetProperty("id").GetGuid();

        session2Id.Should().NotBe(session1Id);
        session2.GetProperty("orders").GetArrayLength().Should().Be(0);
        session2.GetProperty("cumulativeTotal").GetDecimal().Should().Be(0m);
    }

    [Fact]
    public async Task Join_PendingDeal_RestoredUntilExpiry()
    {
        var (qrToken, restaurantId, _) = await SeedTableAsync();
        var (productId, _) = await SeedProductAsync(restaurantId, "Main Item", 30m);
        var (dealProductId, _) = await SeedProductAsync(restaurantId, "Deal Item", 20m);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Set<FlashDealRule>().Add(new FlashDealRule
            {
                Id = Guid.NewGuid(),
                RestaurantId = restaurantId,
                ProductId = dealProductId,
                DiscountPercent = 20,
                IsEnabled = true,
            });
            await db.SaveChangesAsync();
        }

        var deviceId = "device-" + Guid.NewGuid().ToString("N");

        var joinRes = await JoinAsync(qrToken, deviceId);
        var joinJson = await ReadJsonAsync(joinRes);
        var draftOrderId = joinJson.GetProperty("activeOrder").GetProperty("id").GetGuid();

        (await AddItemAsync(restaurantId, draftOrderId, productId, 1)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SubmitAsync(restaurantId, draftOrderId)).StatusCode.Should().Be(HttpStatusCode.OK);

        var rejoinRes = await JoinAsync(qrToken, deviceId);
        var rejoinJson = await ReadJsonAsync(rejoinRes);
        var pendingDeal = rejoinJson.GetProperty("session").GetProperty("pendingDeal");
        pendingDeal.ValueKind.Should().NotBe(JsonValueKind.Null);
        pendingDeal.GetProperty("orderId").GetGuid().Should().Be(draftOrderId);
        pendingDeal.GetProperty("productId").GetGuid().Should().Be(dealProductId);
        // Mirrors the FlashDealAvailable WebSocket push's DiscountPercent field,
        // derived from the FlashDealRule seeded above (20%) rather than a
        // hardcoded frontend fallback.
        pendingDeal.GetProperty("discountPercent").GetInt32().Should().Be(20);
        var dealId = pendingDeal.GetProperty("dealId").GetGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            SetTenant(scope, restaurantId);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.FlashDealOffers.Where(d => d.Id == dealId)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
        }

        var rejoinRes2 = await JoinAsync(qrToken, deviceId);
        var rejoinJson2 = await ReadJsonAsync(rejoinRes2);
        rejoinJson2.GetProperty("session").GetProperty("pendingDeal").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
