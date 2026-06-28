using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RestaurantApi.Data;
using RestaurantApi.Hubs;
using RestaurantApi.Models;
using RestaurantApi.Services;


namespace RestaurantApi.Controllers;

[ApiController]
[Authorize]
[Route("api/restaurants/{restaurantId:guid}/admin")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IFlashDealService _flashDealService;
    private readonly IHubContext<CustomerHub> _customerHub;
    private readonly IHubContext<StaffHub> _staffHub;
    private readonly ILogger <AdminController> _logger;

    // Active-board statuses (the three kanban columns). Anything outside this
    // set is considered terminal and lives in the finalized log.
    private static readonly HashSet<string> ActiveStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "submitted", "preparing", "ready"
    };

    public AdminController(
        AppDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        IFlashDealService flashDealService,
        IHubContext<CustomerHub> customerHub,
        IHubContext<StaffHub> staffHub,
        ILogger<AdminController> logger)
    {
        _dbContext = dbContext;
        _httpClientFactory = httpClientFactory;
        _flashDealService = flashDealService;
        _customerHub = customerHub;
        _staffHub = staffHub;
        _logger = logger;
    }

    [HttpGet("directives")]
    public async Task<IActionResult> GetDirectives()
    {
        var directives = await _dbContext.AIDirectives
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync();
        
        return Ok(directives);
    }

    [HttpPost("directives")]
    public async Task<IActionResult> CreateDirective([FromBody] CreateDirectiveRequest request)
    {
        var client = _httpClientFactory.CreateClient("AiService");
        string? parsedRules = null;
        
        try
        {
            var aiResponse = await client.PostAsJsonAsync("/directives/parse", new { raw_prompt = request.RawPrompt });
            if (aiResponse.IsSuccessStatusCode)
            {
                var result = await aiResponse.Content.ReadFromJsonAsync<JsonElement>();
                if (result.TryGetProperty("parsed_rules", out var pr))
                {
                    parsedRules = pr.GetRawText();
                }
            }
        }
        catch(Exception ex)
        {
            _logger.LogWarning(ex, "Ai service failed to parse the directive");
        }

        var directive = new AIDirective
        {
            Id = Guid.NewGuid(),
            RawPrompt = request.RawPrompt,
            ParsedRules = parsedRules,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        _dbContext.AIDirectives.Add(directive);
        await _dbContext.SaveChangesAsync();

        return Ok(directive);
    }


    [HttpPatch("directives/{id}")]
    public async Task<IActionResult> ToggleDirective(Guid id, [FromBody] bool isActive)
    {
        var directive = await _dbContext.AIDirectives.FindAsync(id);
        if (directive == null) return NotFound();

        directive.IsActive = isActive;
        await _dbContext.SaveChangesAsync();
        return Ok(directive);
    }


    [HttpGet("recommendation-directives")]
    public async Task<IActionResult> GetRecommendationDirectives()
    {
        var directives = await _dbContext.RecommendationDirectives
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync();

        return Ok(directives);
    }

    [HttpPost("recommendation-directives")]
    public async Task<IActionResult> CreateRecommendationDirective(
        [FromBody] CreateRecommendationDirectiveRequest request)
    {
        var menuItems = await _dbContext.Products
            .Include(p => p.Category)
            .Where(p => p.IsAvailable == true)
            .Select(p => new
            {
                product_id = p.Id.ToString(),
                name = p.Name,
                category = p.Category != null ? p.Category.Name : null
            })
            .ToListAsync();

        var client = _httpClientFactory.CreateClient("AiService");
        string? parsedRules = null;

        try
        {
            var aiResponse = await client.PostAsJsonAsync(
                "/recommendation-directives/parse",
                new { raw_prompt = request.RawPrompt, menu_items = menuItems });

            if (aiResponse.IsSuccessStatusCode)
            {
                parsedRules = await aiResponse.Content.ReadAsStringAsync();
            }
            else
            {
                var body = await aiResponse.Content.ReadAsStringAsync();
                _logger.LogWarning(
                    "AI /recommendation-directives/parse returned non-success status {StatusCode}. Body: {Body}",
                    (int)aiResponse.StatusCode, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI service failed to parse recommendation directive");
        }

        var directive = new RecommendationDirective
        {
            Id = Guid.NewGuid(),
            RawPrompt = request.RawPrompt,
            ParsedRules = parsedRules,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        _dbContext.RecommendationDirectives.Add(directive);
        await _dbContext.SaveChangesAsync();

        return Ok(directive);
    }

    [HttpPatch("recommendation-directives/{id}")]
    public async Task<IActionResult> ToggleRecommendationDirective(Guid id, [FromBody] bool isActive)
    {
        var directive = await _dbContext.RecommendationDirectives.FindAsync(id);
        if (directive == null) return NotFound();

        directive.IsActive = isActive;
        await _dbContext.SaveChangesAsync();
        return Ok(directive);
    }

    [HttpDelete("recommendation-directives/{id}")]
    public async Task<IActionResult> DeleteRecommendationDirective(Guid id)
    {
        var directive = await _dbContext.RecommendationDirectives.FindAsync(id);
        if (directive == null) return NotFound();

        _dbContext.RecommendationDirectives.Remove(directive);
        await _dbContext.SaveChangesAsync();
        return NoContent();
    }


    [HttpGet("similarity-rules")]
    public async Task<IActionResult> GetSimilarityRules()
    {
        var rules = await _dbContext.SimilarityRules
            .Include(r => r.ProductA)
            .Include(r => r.ProductB)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new
            {
                r.Id,
                r.ProductAId,
                productAName = r.ProductA != null ? r.ProductA.Name : null,
                r.ProductBId,
                productBName = r.ProductB != null ? r.ProductB.Name : null,
                r.Score,
                r.RuleType,
                r.CreatedAt
            })
            .ToListAsync();

        return Ok(rules);
    }

    [HttpPost("similarity-rules/auto-generate")]
    public async Task<IActionResult> AutoGenerateRules()
    {
        var client = _httpClientFactory.CreateClient("AiService");
        
        var orders = await _dbContext.Orders.Include(o => o.Items).ToListAsync();
        var history = orders.Select(o => o.Items.Select(i => i.ProductId.ToString()).ToList()).ToList();

        try 
        {
            var aiResponse = await client.PostAsJsonAsync("/similarity/auto-generate", new { order_history = history });
            if (aiResponse.IsSuccessStatusCode)
            {
                var result = await aiResponse.Content.ReadFromJsonAsync<JsonElement>();
                return Ok(result);
            }
        }
        catch 
        {
            return StatusCode(503, new { message = "AI Service unavailable" });
        }

        return StatusCode(500);
    }


    [HttpGet("metrics")]
    public async Task <IActionResult> GetMetrics()
    {
        var totalDeals = await _dbContext.FlashDealOffers.CountAsync();
        var claimedDeals = await _dbContext.FlashDealOffers.CountAsync(d=>d.Status=="claimed");
        var expiredDeals = await _dbContext.FlashDealOffers.CountAsync(d=>d.Status=="expired");

        var upsellRevenue = await _dbContext.OrderItems
        .Where(i=>i.WasAiRecommended==true)
        .SumAsync(i => (decimal?)(i.Quantity * i.UnitPriceAtPurchase)) ?? 0;

        var flashDealRevenue = await _dbContext.OrderItems
        .Where(i=>i.IsFlashDealItem==true)
        .SumAsync(i => (decimal?)(i.Quantity * i.UnitPriceAtPurchase)) ?? 0;

        return Ok(new
        {
           flashDealsShown = totalDeals,        
           flashDealsClaimed = claimedDeals,    
           flashDealsExpired = expiredDeals,    
           upsellRevenue = upsellRevenue,
           flashDealRevenue = flashDealRevenue
        });
    }

    // Finalized log. Returns soft-deleted orders (the natural outcome of the
    // 45-second grace period) and explicitly cancelled ones. Active orders
    // surface via /admin/orders/active instead. Tenant scoping is handled by
    // the global query filter on Order.RestaurantId.
    [HttpGet("orders")]
    public async Task<IActionResult> GetOrders([FromQuery] int limit = 100)
    {
        var log = await _dbContext.Orders
            .Where(o => o.DeletedAt != null || o.Status == "cancelled" || o.Status == "completed")
            .Include(o => o.Table)
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
            .OrderByDescending(o => o.CreatedAt)
            .Take(limit)
            .Select(o => new
            {
                id = o.Id,
                orderNumber = o.OrderNumber,
                status = o.Status,
                paymentStatus = o.PaymentStatus,
                totalAmount = o.TotalAmount,
                createdAt = o.CreatedAt,
                deletedAt = o.DeletedAt,
                tableNumber = o.Table != null ? o.Table.TableNumber : "?",
                items = o.Items.Select(i => new
                {
                    id = i.Id,
                    productName = i.Product != null ? i.Product.Name : "Produs șters",
                    quantity = i.Quantity,
                    unitPrice = i.UnitPriceAtPurchase,
                    subtotal = i.Quantity * i.UnitPriceAtPurchase,
                    wasAiRecommended = i.WasAiRecommended ?? false,
                    isFlashDealItem = i.IsFlashDealItem ?? false
                }).ToList()
            })
            .ToListAsync();

        return Ok(log);
    }

    // Active kanban feed. The admin frontend polls this every few seconds and
    // bucket-sorts the result into the three columns (submitted / preparing / ready).
    [HttpGet("orders/active")]
    public async Task<IActionResult> GetActiveOrders()
    {
        var orders = await _dbContext.Orders
            .Where(o => o.DeletedAt == null
                        && (o.Status == "submitted" || o.Status == "preparing" || o.Status == "ready"))
            .Include(o => o.Table)
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
            .OrderBy(o => o.CreatedAt)
            .Select(o => new
            {
                id = o.Id,
                orderNumber = o.OrderNumber,
                status = o.Status,
                paymentStatus = o.PaymentStatus,
                totalAmount = o.TotalAmount,
                createdAt = o.CreatedAt,
                readyAt = o.ReadyAt,
                tableId = o.TableId,
                tableNumber = o.Table != null ? o.Table.TableNumber : "?",
                items = o.Items.Select(i => new
                {
                    id = i.Id,
                    productName = i.Product != null ? i.Product.Name : "Produs șters",
                    quantity = i.Quantity,
                    unitPrice = i.UnitPriceAtPurchase,
                    subtotal = i.Quantity * i.UnitPriceAtPurchase,
                    wasAiRecommended = i.WasAiRecommended ?? false,
                    isFlashDealItem = i.IsFlashDealItem ?? false
                }).ToList()
            })
            .ToListAsync();

        return Ok(orders);
    }

    public class UpdateOrderStatusRequest
    {
        public string Status { get; set; } = string.Empty;
    }

    // Kanban drag-drop target. Only transitions between submitted/preparing/ready
    // are allowed here — terminal transitions (completed) are owned by the
    // OrderFinalizationService once the grace period elapses.
    [HttpPatch("orders/{orderId:guid}/status")]
    public async Task<IActionResult> UpdateOrderStatus(
        Guid restaurantId,
        Guid orderId,
        [FromBody] UpdateOrderStatusRequest request)
    {
        var newStatus = request?.Status?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!ActiveStatuses.Contains(newStatus))
            return BadRequest(new { message = $"Status '{request?.Status}' nepermis. Folosește submitted/preparing/ready." });

        var order = await _dbContext.Orders
            .FirstOrDefaultAsync(o => o.Id == orderId && o.DeletedAt == null);

        if (order == null)
            return NotFound(new { message = "Comanda nu a fost găsită sau a fost deja finalizată." });

        if (!ActiveStatuses.Contains(order.Status))
            return Conflict(new { message = $"Comanda nu mai e activă (stare curentă: {order.Status})." });

        order.Status = newStatus;
        // Stamp ReadyAt when entering "ready" (starts the soft-delete countdown);
        // clear it when the admin drags back out, so the timer resets cleanly.
        order.ReadyAt = newStatus == "ready" ? DateTime.UtcNow : null;

        await _dbContext.SaveChangesAsync();

        // Broadcast to the customer at that table so the OrderSentView timeline
        // updates live.
        await _customerHub.Clients
            .Group(TenantGroups.Table(order.RestaurantId, order.TableId))
            .SendAsync("OrderStatusUpdated", new
            {
                orderId = order.Id,
                status = order.Status,
            });

        // And to other admin sessions so the kanban stays in sync across tabs.
        await _staffHub.Clients
            .Group(TenantGroups.Role(order.RestaurantId, "admin"))
            .SendAsync("OrderStatusUpdated", new
            {
                orderId = order.Id,
                orderNumber = order.OrderNumber,
                status = order.Status,
                readyAt = order.ReadyAt,
                tableId = order.TableId,
            });

        return Ok(new
        {
            id = order.Id,
            orderNumber = order.OrderNumber,
            status = order.Status,
            readyAt = order.ReadyAt,
        });
    }

    [HttpGet("flash-deals")]
    public async Task <IActionResult> GetFlashDeals()
    {
        var deals= await _dbContext.FlashDealOffers
        .Include(d=>d.Product)
        .OrderByDescending(d => d.ExpiresAt)
        .Select(d => new
        {
            d.Id,
            d.Status,
            d.DiscountPrice,
            d.ExpiresAt,
            d.OrderId,
            ProductName = d.Product != null ? d.Product.Name : "Unknown"
        }).ToListAsync();

     return Ok(deals);
    
    }
   
   [HttpPost("flash-deals/trigger")]
   public async Task<IActionResult> TriggerFlashDeal([FromBody] Guid orderId)
    {
        await _flashDealService.EvaluateEligibilityAsync(orderId);
        return Ok(new {message = "Flash deal triggered successfully."});
    }

}

public class CreateDirectiveRequest
{
    [JsonPropertyName("rawPrompt")]
    public string RawPrompt { get; set; } = string.Empty;
}

public class CreateRecommendationDirectiveRequest
{
    [JsonPropertyName("rawPrompt")]
    public string RawPrompt { get; set; } = string.Empty;
}