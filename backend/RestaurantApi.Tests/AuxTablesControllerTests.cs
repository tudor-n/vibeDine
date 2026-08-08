using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantApi.Data;
using RestaurantApi.Models;
using RestaurantApi.Tests.Helpers;
using Testcontainers.PostgreSql;
using Xunit;

namespace RestaurantApi.Tests;

/// <summary>
/// Covers the aux-tables slice: parking active orders on a per-waiter aux table
/// (whole-order move and item split), the manager aux console (list, move back
/// to a regular table, reassign to another waiter), the waiter path restrictions
/// and tenant isolation. Asserts on the exact camelCased JSON the admin
/// frontend depends on.
/// </summary>
public class AuxTablesControllerTests : IAsyncLifetime
{
    private const decimal ProductPrice = 12m;
    private const string Waiter1Name = "Ana Ospătar";
    private const string Waiter2Name = "Bogdan Ospătar";

    // Orders carry a per-restaurant human-friendly number; a static counter keeps
    // every seeded number unique across the whole class run.
    private static int _orderNumberSeed = 7000;

    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private MenuImportTestFactory _factory = null!;
    private HttpClient _managerClientA = null!;
    private HttpClient _managerClientB = null!;
    private Guid _restA;
    private Guid _restB;
    private Guid _waiter1Id;
    private Guid _waiter2Id;

    private string AdminUrl(string suffix) => $"/api/restaurants/{_restA}/admin{suffix}";
    private string MoveToAuxUrl(Guid orderId) => AdminUrl($"/orders/{orderId}/move-to-aux");
    private string AuxTablesUrl => AdminUrl("/aux-tables");
    private string MoveToTableUrl(Guid orderId) => AdminUrl($"/aux-tables/orders/{orderId}/move-to-table");
    private string ReassignUrl(Guid orderId) => AdminUrl($"/aux-tables/orders/{orderId}/reassign");

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        _factory = new MenuImportTestFactory { PostgresConnectionString = _pg.GetConnectionString() };
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
        }

        var (restA, restB, clientA, clientB) = await MenuImportTestFixtures.SeedTwoTenantsAsync(_factory);
        _restA = restA;
        _restB = restB;
        _managerClientA = clientA;
        _managerClientB = clientB;

        _waiter1Id = await CreateWaiterAsync(Waiter1Name);
        _waiter2Id = await CreateWaiterAsync(Waiter2Name);
    }

    public async Task DisposeAsync()
    {
        _managerClientA.Dispose();
        _managerClientB.Dispose();
        _factory.Dispose();
        await _pg.DisposeAsync();
    }

    // ── Seeding helpers ──────────────────────────────────────────────────────
    // Test scopes have no ambient tenant, so every entity gets RestaurantId set
    // explicitly and every direct read goes through IgnoreQueryFilters().

    private async Task<Guid> CreateWaiterAsync(string fullName)
    {
        var waiterId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var user = new AppUser
        {
            Id = waiterId,
            UserName = $"waiter-{waiterId:N}@test.com",
            Email = $"waiter-{waiterId:N}@test.com",
            FullName = fullName,
        };
        var created = await userManager.CreateAsync(user, "TestPass1!");
        created.Succeeded.Should().BeTrue();

        var restaurant = await db.Restaurants.FirstAsync(r => r.Id == _restA);
        db.Memberships.Add(new Membership
        {
            Id = Guid.NewGuid(),
            UserId = waiterId,
            OrganizationId = restaurant.OrganizationId,
            RestaurantId = _restA,
            Role = MembershipRole.Waiter,
            Status = MembershipStatus.Active,
        });
        await db.SaveChangesAsync();
        return waiterId;
    }

    private sealed record SeededOrder(
        Guid OrderId,
        Guid TableId,
        string TableNumber,
        List<Guid> ItemIds,
        int OrderNumber,
        string ProductName);

    /// <summary>
    /// Seeds a regular table + category + product (12,00 lei) + one order with one
    /// OrderItem per entry in <paramref name="quantities"/> (all at the product
    /// price). TotalAmount is the exact items sum.
    /// </summary>
    private async Task<SeededOrder> SeedOrderAsync(
        string status = "submitted",
        Guid? claimedBy = null,
        int[]? quantities = null,
        DateTime? readyAt = null)
    {
        quantities ??= [1];
        var orderNumber = Interlocked.Increment(ref _orderNumberSeed);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tableNumber = "T-" + Guid.NewGuid().ToString("N")[..8];
        var table = new RestaurantTable
        {
            Id = Guid.NewGuid(),
            RestaurantId = _restA,
            TableNumber = tableNumber,
            QrToken = Guid.NewGuid(),
        };
        var category = new Category
        {
            Id = Guid.NewGuid(),
            RestaurantId = _restA,
            Name = "Cat-" + Guid.NewGuid().ToString("N")[..6],
        };
        var productName = "Produs-" + Guid.NewGuid().ToString("N")[..6];
        var product = new Product
        {
            Id = Guid.NewGuid(),
            RestaurantId = _restA,
            CategoryId = category.Id,
            Name = productName,
            Price = ProductPrice,
            IsAvailable = true,
        };
        db.Tables.Add(table);
        db.Categories.Add(category);
        db.Products.Add(product);

        var order = new Order
        {
            Id = Guid.NewGuid(),
            RestaurantId = _restA,
            TableId = table.Id,
            Status = status,
            OrderNumber = orderNumber,
            StockDecremented = true,
            ClaimedByWaiterId = claimedBy,
            ReadyAt = readyAt,
            TotalAmount = quantities.Sum() * ProductPrice,
        };
        db.Orders.Add(order);

        var itemIds = new List<Guid>();
        foreach (var quantity in quantities)
        {
            var item = new OrderItem
            {
                Id = Guid.NewGuid(),
                RestaurantId = _restA,
                OrderId = order.Id,
                ProductId = product.Id,
                Quantity = quantity,
                UnitPriceAtPurchase = ProductPrice,
            };
            itemIds.Add(item.Id);
            db.OrderItems.Add(item);
        }
        await db.SaveChangesAsync();

        return new SeededOrder(order.Id, table.Id, tableNumber, itemIds, orderNumber, productName);
    }

    private async Task<(Guid Id, string Number)> SeedRegularTableAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tableNumber = "T-" + Guid.NewGuid().ToString("N")[..8];
        var table = new RestaurantTable
        {
            Id = Guid.NewGuid(),
            RestaurantId = _restA,
            TableNumber = tableNumber,
            QrToken = Guid.NewGuid(),
        };
        db.Tables.Add(table);
        await db.SaveChangesAsync();
        return (table.Id, tableNumber);
    }

    private async Task ClaimInDbAsync(Guid orderId, Guid waiterId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var order = await db.Orders.IgnoreQueryFilters().FirstAsync(o => o.Id == orderId);
        order.ClaimedByWaiterId = waiterId;
        await db.SaveChangesAsync();
    }

    private async Task<Order> GetOrderAsync(Guid orderId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Orders.IgnoreQueryFilters()
            .AsNoTracking()
            .Include(o => o.Items)
            .FirstAsync(o => o.Id == orderId);
    }

    private async Task<RestaurantTable> GetTableAsync(Guid tableId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Tables.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(t => t.Id == tableId);
    }

    private async Task<int> CountOrdersOnTableAsync(Guid tableId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Orders.IgnoreQueryFilters().CountAsync(o => o.TableId == tableId);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage res)
    {
        var raw = await res.Content.ReadAsStringAsync();
        return JsonDocument.Parse(raw).RootElement.Clone();
    }

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Manager_moves_whole_order_to_waiter_aux_table()
    {
        var seed = await SeedOrderAsync(quantities: [2], readyAt: DateTime.UtcNow);

        var res = await _managerClientA.PostAsJsonAsync(MoveToAuxUrl(seed.OrderId), new { targetWaiterId = _waiter1Id });
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadJsonAsync(res);

        body.GetProperty("wholeOrderMoved").GetBoolean().Should().BeTrue();
        body.GetProperty("movedOrderId").GetGuid().Should().Be(seed.OrderId);
        body.GetProperty("sourceOrderId").GetGuid().Should().Be(seed.OrderId);
        body.GetProperty("movedOrderNumber").GetInt32().Should().Be(seed.OrderNumber);
        body.GetProperty("auxWaiterId").GetGuid().Should().Be(_waiter1Id);
        body.GetProperty("auxWaiterName").GetString().Should().Be(Waiter1Name);
        var auxTableId = body.GetProperty("auxTableId").GetGuid();
        var auxTableNumber = body.GetProperty("auxTableNumber").GetString()!;
        auxTableNumber.Should().StartWith("AUX").And.Contain(Waiter1Name);

        // Db: the order physically sits on the waiter's aux table now.
        var order = await GetOrderAsync(seed.OrderId);
        order.TableId.Should().Be(auxTableId);
        order.MovedFromTableId.Should().Be(seed.TableId);
        order.MovedToAuxAt.Should().NotBeNull();
        order.ReadyAt.Should().BeNull();
        order.ClaimedByWaiterId.Should().Be(_waiter1Id);

        // Db: the auto-created aux table row.
        var auxTable = await GetTableAsync(auxTableId);
        auxTable.IsAux.Should().BeTrue();
        auxTable.AssignedWaiterId.Should().Be(_waiter1Id);
        auxTable.QrToken.Should().BeNull();
        auxTable.RestaurantId.Should().Be(_restA);

        // The regular tables list must not leak aux tables.
        var tablesRes = await _managerClientA.GetAsync(AdminUrl("/tables"));
        tablesRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var tables = await ReadJsonAsync(tablesRes);
        tables.EnumerateArray().Any(t => t.GetProperty("id").GetGuid() == auxTableId).Should().BeFalse();

        // Manager console: waiter1's row carries the order; waiter2 gets an empty row.
        var listRes = await _managerClientA.GetAsync(AuxTablesUrl);
        listRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await ReadJsonAsync(listRes);

        var w1 = rows.EnumerateArray().Single(r => r.GetProperty("waiterId").GetGuid() == _waiter1Id);
        w1.GetProperty("waiterName").GetString().Should().Be(Waiter1Name);
        w1.GetProperty("isActiveStaff").GetBoolean().Should().BeTrue();
        w1.TryGetProperty("avatarUrl", out _).Should().BeTrue();
        w1.GetProperty("auxTableId").GetGuid().Should().Be(auxTableId);
        w1.GetProperty("auxTableNumber").GetString().Should().Be(auxTableNumber);

        var orderRow = w1.GetProperty("orders").EnumerateArray().Single();
        orderRow.GetProperty("id").GetGuid().Should().Be(seed.OrderId);
        orderRow.GetProperty("orderNumber").GetInt32().Should().Be(seed.OrderNumber);
        orderRow.GetProperty("status").GetString().Should().Be("submitted");
        orderRow.GetProperty("totalAmount").GetDecimal().Should().Be(24m);
        orderRow.TryGetProperty("createdAt", out _).Should().BeTrue();
        orderRow.GetProperty("movedToAuxAt").ValueKind.Should().NotBe(JsonValueKind.Null);
        orderRow.GetProperty("movedFromTableId").GetGuid().Should().Be(seed.TableId);
        orderRow.GetProperty("movedFromTableNumber").GetString().Should().Be(seed.TableNumber);
        orderRow.GetProperty("claimedByWaiterId").GetGuid().Should().Be(_waiter1Id);
        orderRow.GetProperty("claimedByName").GetString().Should().Be(Waiter1Name);

        var itemRow = orderRow.GetProperty("items").EnumerateArray().Single();
        itemRow.GetProperty("productName").GetString().Should().Be(seed.ProductName);
        itemRow.GetProperty("quantity").GetInt32().Should().Be(2);
        itemRow.GetProperty("unitPrice").GetDecimal().Should().Be(ProductPrice);
        itemRow.GetProperty("subtotal").GetDecimal().Should().Be(24m);

        var w2 = rows.EnumerateArray().Single(r => r.GetProperty("waiterId").GetGuid() == _waiter2Id);
        w2.GetProperty("auxTableId").ValueKind.Should().Be(JsonValueKind.Null);
        w2.GetProperty("orders").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Waiter_self_move_requires_claimed_order()
    {
        var seed = await SeedOrderAsync();
        using var waiterClient = _factory.CreateAuthenticatedClient(_waiter1Id, _restA);

        // Unclaimed → the waiter may not park it.
        var forbidden = await waiterClient.PostAsJsonAsync(MoveToAuxUrl(seed.OrderId), new { });
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Claimed by them → an empty body parks it on their own aux table.
        await ClaimInDbAsync(seed.OrderId, _waiter1Id);
        var res = await waiterClient.PostAsJsonAsync(MoveToAuxUrl(seed.OrderId), new { });
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadJsonAsync(res);
        body.GetProperty("wholeOrderMoved").GetBoolean().Should().BeTrue();
        body.GetProperty("auxWaiterId").GetGuid().Should().Be(_waiter1Id);
        var auxTableId = body.GetProperty("auxTableId").GetGuid();

        var order = await GetOrderAsync(seed.OrderId);
        order.TableId.Should().Be(auxTableId);
        order.ClaimedByWaiterId.Should().Be(_waiter1Id);

        var auxTable = await GetTableAsync(auxTableId);
        auxTable.IsAux.Should().BeTrue();
        auxTable.AssignedWaiterId.Should().Be(_waiter1Id);
    }

    [Fact]
    public async Task Waiter_cannot_target_a_colleague()
    {
        var seed = await SeedOrderAsync(claimedBy: _waiter1Id);
        using var waiterClient = _factory.CreateAuthenticatedClient(_waiter1Id, _restA);

        var res = await waiterClient.PostAsJsonAsync(MoveToAuxUrl(seed.OrderId), new { targetWaiterId = _waiter2Id });

        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // And the order stayed put.
        var order = await GetOrderAsync(seed.OrderId);
        order.TableId.Should().Be(seed.TableId);
    }

    [Fact]
    public async Task Waiter_blocked_from_manager_aux_endpoints()
    {
        using var waiterClient = _factory.CreateAuthenticatedClient(_waiter1Id, _restA);

        var list = await waiterClient.GetAsync(AuxTablesUrl);
        list.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var moveBack = await waiterClient.PostAsJsonAsync(MoveToTableUrl(Guid.NewGuid()), new { tableId = Guid.NewGuid() });
        moveBack.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var reassign = await waiterClient.PostAsJsonAsync(ReassignUrl(Guid.NewGuid()), new { waiterId = _waiter2Id });
        reassign.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Manager_must_supply_target_waiter()
    {
        var seed = await SeedOrderAsync();

        // Managers are not waiters, so an omitted targetWaiterId cannot default to "self".
        var res = await _managerClientA.PostAsJsonAsync(MoveToAuxUrl(seed.OrderId), new { });

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Split_moves_selected_quantities_to_new_order_on_aux_table()
    {
        var seed = await SeedOrderAsync(quantities: [2, 1]); // 2×12 + 1×12 = 36
        var item1 = seed.ItemIds[0];
        var item2 = seed.ItemIds[1];

        var res = await _managerClientA.PostAsJsonAsync(MoveToAuxUrl(seed.OrderId), new
        {
            targetWaiterId = _waiter1Id,
            items = new[] { new { orderItemId = item1, quantity = 1 } },
        });
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadJsonAsync(res);

        body.GetProperty("wholeOrderMoved").GetBoolean().Should().BeFalse();
        body.GetProperty("sourceOrderId").GetGuid().Should().Be(seed.OrderId);
        var movedOrderId = body.GetProperty("movedOrderId").GetGuid();
        movedOrderId.Should().NotBe(seed.OrderId);
        var auxTableId = body.GetProperty("auxTableId").GetGuid();

        // Source order: remaining quantities, recomputed total, still on its table.
        var source = await GetOrderAsync(seed.OrderId);
        source.TableId.Should().Be(seed.TableId);
        source.TotalAmount.Should().Be(24m);
        source.Items.Should().HaveCount(2);
        source.Items.Single(i => i.Id == item1).Quantity.Should().Be(1);
        source.Items.Single(i => i.Id == item2).Quantity.Should().Be(1);

        // Moved order: the split-off quantities, parked on the aux table.
        var moved = await GetOrderAsync(movedOrderId);
        moved.TableId.Should().Be(auxTableId);
        moved.Status.Should().Be("submitted");
        moved.StockDecremented.Should().BeTrue();
        moved.TotalAmount.Should().Be(12m);
        moved.OrderNumber.Should().NotBeNull();
        moved.OrderNumber.Should().NotBe(seed.OrderNumber);
        var movedItem = moved.Items.Should().ContainSingle().Which;
        movedItem.Quantity.Should().Be(1);
        movedItem.UnitPriceAtPurchase.Should().Be(ProductPrice);
    }

    [Fact]
    public async Task Full_selection_behaves_like_whole_order_move()
    {
        var seed = await SeedOrderAsync(quantities: [2, 1]);

        var res = await _managerClientA.PostAsJsonAsync(MoveToAuxUrl(seed.OrderId), new
        {
            targetWaiterId = _waiter1Id,
            items = new[]
            {
                new { orderItemId = seed.ItemIds[0], quantity = 2 },
                new { orderItemId = seed.ItemIds[1], quantity = 1 },
            },
        });
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadJsonAsync(res);

        body.GetProperty("wholeOrderMoved").GetBoolean().Should().BeTrue();
        body.GetProperty("movedOrderId").GetGuid().Should().Be(seed.OrderId);
        var auxTableId = body.GetProperty("auxTableId").GetGuid();

        var order = await GetOrderAsync(seed.OrderId);
        order.TableId.Should().Be(auxTableId);
        order.TotalAmount.Should().Be(36m);
        order.Items.Should().HaveCount(2);

        // No split order was created — the aux table holds exactly the original order.
        (await CountOrdersOnTableAsync(auxTableId)).Should().Be(1);
    }

    [Fact]
    public async Task Move_back_to_regular_table_clears_aux_fields()
    {
        var seed = await SeedOrderAsync();
        var park = await _managerClientA.PostAsJsonAsync(MoveToAuxUrl(seed.OrderId), new { targetWaiterId = _waiter1Id });
        park.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetOrderAsync(seed.OrderId)).MovedToAuxAt.Should().NotBeNull();

        var (targetTableId, targetTableNumber) = await SeedRegularTableAsync();
        var res = await _managerClientA.PostAsJsonAsync(MoveToTableUrl(seed.OrderId), new { tableId = targetTableId });
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadJsonAsync(res);
        body.GetProperty("orderId").GetGuid().Should().Be(seed.OrderId);
        body.GetProperty("orderNumber").GetInt32().Should().Be(seed.OrderNumber);
        body.GetProperty("tableId").GetGuid().Should().Be(targetTableId);
        body.GetProperty("tableNumber").GetString().Should().Be(targetTableNumber);

        var order = await GetOrderAsync(seed.OrderId);
        order.TableId.Should().Be(targetTableId);
        order.MovedFromTableId.Should().BeNull();
        order.MovedToAuxAt.Should().BeNull();
    }

    [Fact]
    public async Task Reassign_moves_order_to_other_waiters_aux_table()
    {
        var seed = await SeedOrderAsync();
        var park = await _managerClientA.PostAsJsonAsync(MoveToAuxUrl(seed.OrderId), new { targetWaiterId = _waiter1Id });
        park.StatusCode.Should().Be(HttpStatusCode.OK);
        var waiter1AuxId = (await ReadJsonAsync(park)).GetProperty("auxTableId").GetGuid();

        var res = await _managerClientA.PostAsJsonAsync(ReassignUrl(seed.OrderId), new { waiterId = _waiter2Id });
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadJsonAsync(res);
        body.GetProperty("orderId").GetGuid().Should().Be(seed.OrderId);
        body.GetProperty("orderNumber").GetInt32().Should().Be(seed.OrderNumber);
        body.GetProperty("auxWaiterId").GetGuid().Should().Be(_waiter2Id);
        body.GetProperty("auxWaiterName").GetString().Should().Be(Waiter2Name);
        var waiter2AuxId = body.GetProperty("auxTableId").GetGuid();
        waiter2AuxId.Should().NotBe(waiter1AuxId);

        var order = await GetOrderAsync(seed.OrderId);
        order.TableId.Should().Be(waiter2AuxId);
        order.ClaimedByWaiterId.Should().Be(_waiter2Id);
        // The original (pre-park) table is preserved across the reassignment.
        order.MovedFromTableId.Should().Be(seed.TableId);

        var auxTable = await GetTableAsync(waiter2AuxId);
        auxTable.IsAux.Should().BeTrue();
        auxTable.AssignedWaiterId.Should().Be(_waiter2Id);
    }

    [Fact]
    public async Task Move_to_aux_rejects_draft_orders_with_409()
    {
        var seed = await SeedOrderAsync(status: "draft");

        var res = await _managerClientA.PostAsJsonAsync(MoveToAuxUrl(seed.OrderId), new { targetWaiterId = _waiter1Id });

        res.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var order = await GetOrderAsync(seed.OrderId);
        order.TableId.Should().Be(seed.TableId);
    }

    [Fact]
    public async Task Cross_tenant_move_and_listing_are_isolated()
    {
        var seed = await SeedOrderAsync(); // belongs to restaurant A

        // A manager of B, on B's own route, cannot reach A's order: the global
        // tenant query filter turns it into a 404 — never a cross-tenant write.
        var moveRes = await _managerClientB.PostAsJsonAsync(
            $"/api/restaurants/{_restB}/admin/orders/{seed.OrderId}/move-to-aux",
            new { targetWaiterId = _waiter1Id });
        moveRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // And the order did not move.
        (await GetOrderAsync(seed.OrderId)).TableId.Should().Be(seed.TableId);

        // B's aux console never contains A's waiters.
        var listRes = await _managerClientB.GetAsync($"/api/restaurants/{_restB}/admin/aux-tables");
        listRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await ReadJsonAsync(listRes);
        rows.EnumerateArray()
            .Any(r => r.GetProperty("waiterId").GetGuid() == _waiter1Id
                   || r.GetProperty("waiterId").GetGuid() == _waiter2Id)
            .Should().BeFalse();
    }
}
