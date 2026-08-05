using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RestaurantApi.Data;
using RestaurantApi.DTOs.Responses;
using RestaurantApi.Hubs;
using RestaurantApi.Models;
using RestaurantApi.DTOs.Requests;
using RestaurantApi.Services;

namespace RestaurantApi.Controllers;

[ApiController]
[Route("api")]
public class CustomerController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IFlashDealService _flashDealService;
    private readonly IHappyHourService _happyHourService;
    private readonly RecommendationService _recommendationService;
    private readonly IHubContext<StaffHub> _staffHub;
    private readonly IHubContext<CustomerHub> _customerHub;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<CustomerController> _logger;

    public CustomerController(
        AppDbContext context,
        IFlashDealService flashDealService,
        IHappyHourService happyHourService,
        RecommendationService recommendationService,
        IHubContext<StaffHub> staffHub,
        IHubContext<CustomerHub> customerHub,
        IMemoryCache memoryCache,
        ILogger<CustomerController> logger)
    {
        _context = context;
        _flashDealService = flashDealService;
        _happyHourService = happyHourService;
        _recommendationService = recommendationService;
        _staffHub = staffHub;
        _customerHub = customerHub;
        _memoryCache = memoryCache;
        _logger = logger;
    }

    // Order-write input caps. These endpoints are anonymous (tenant-scoped only by
    // the route), so a negative/oversized quantity would corrupt the bill and stock
    // at dispatch, and an unbounded item list is a memory/DB DoS. Cart removal is by
    // omission from the list, never a quantity of 0, so a hard [1, 99] floor is safe.
    private const int MaxOrderItems = 100;
    private const int MaxLineQuantity = 99;

    [HttpPost("restaurants/{restaurantId:guid}/orders/{orderId:guid}/submit")]
    public async Task<IActionResult> SubmitOrder(Guid restaurantId, Guid orderId)
    {
        var order = await _context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.Status == "draft");

        if (order == null)
            return NotFound(new { message = "Draft order not found or it has already been submitted." });

        if (order.Items == null || order.Items.Count == 0)
            return BadRequest(new { message = "Cannot submit an empty order. Please add items to your cart first." });

        // A submit proves the diner is still at the table — extend their device
        // session's wipe window so the visit (and its running total) survives
        // through the kitchen wait instead of expiring mid-order.
        if (order.DeviceSessionId is Guid deviceSessionId)
        {
            await _context.DeviceSessions
                .Where(d => d.Id == deviceSessionId)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.LastSeenAt, DateTime.UtcNow));
        }

        order.TotalAmount = order.Items.Sum(i => i.Quantity * i.UnitPriceAtPurchase);
        // Hold the order in pending_dispatch until the 2-minute flash deal window
        // elapses (or the customer claims the deal). The active-orders kanban
        // filter only includes submitted/preparing/ready, so the kitchen won't
        // see the order yet. DispatchToKitchenAsync transitions it to "submitted".
        order.Status = "pending_dispatch";

        // Per-restaurant sequence. The (RestaurantId, OrderNumber) index isn't unique
        // because a race between two concurrent submits could collide here; for the
        // single-host dev setup this is fine, and a unique index would convert a
        // collision into a 500 with no easy retry path.
        var lastNumber = await _context.Orders
            .IgnoreQueryFilters()
            .Where(o => o.RestaurantId == order.RestaurantId && o.OrderNumber != null)
            .MaxAsync(o => (int?)o.OrderNumber) ?? 0;
        order.OrderNumber = lastNumber + 1;

        // If a waiter has taken this table, the round belongs on their bill — otherwise
        // the QR order would never be billed by the session and the fiscal receipt cut at
        // close would be short. Nothing else about the diner flow changes: the order stays
        // source "qr" and behaves identically at an unclaimed table (session stays null).
        var openSessionId = await _context.TableServiceSessions
            .Where(s => s.TableId == order.TableId && s.Status == "open")
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync();

        if (openSessionId is Guid sessionId)
            order.TableServiceSessionId = sessionId;

        await _context.SaveChangesAsync();

        // This guest's round is done — free their seat so the party panel (and
        // the next phone that scans the QR) only shows people still composing an
        // order. Guests whose drafts were merged into this order leave too: their
        // phones are parked on the merged screen and they re-join as fresh guests
        // when they start another round. Anyone who already opened a new draft
        // (e.g. a merged guest who tapped "start a separate order") keeps their seat.
        if (order.GuestId is Guid ownerGuestId)
        {
            var candidateIds = await _context.Orders
                .Where(o => o.MergedIntoOrderId == order.Id && o.GuestId != null)
                .Select(o => o.GuestId!.Value)
                .ToListAsync();
            candidateIds.Add(ownerGuestId);

            var stillDrafting = await _context.Orders
                .Where(o => o.TableId == order.TableId && o.Status == "draft"
                            && o.GuestId != null && candidateIds.Contains(o.GuestId.Value))
                .Select(o => o.GuestId!.Value)
                .ToListAsync();

            var leavingIds = candidateIds.Except(stillDrafting).ToList();
            if (leavingIds.Count > 0)
            {
                var removed = await _context.TableGuests
                    .Where(g => g.TableId == order.TableId && leavingIds.Contains(g.Id))
                    .ExecuteDeleteAsync();

                if (removed > 0)
                {
                    await _customerHub.Clients
                        .Group(TenantGroups.Table(order.RestaurantId, order.TableId))
                        .SendAsync("PartyMemberLeft", new { guestIds = leavingIds });
                }
            }
        }

        var dealCreated = await _flashDealService.EvaluateEligibilityAsync(orderId);

        if (!dealCreated)
        {
            // No deal means nothing to wait on — release the order to the kitchen
            // immediately. When a deal is created, ClaimDealAsync or the 2-minute
            // expiry task in FlashDealService is responsible for the dispatch.
            await _flashDealService.DispatchToKitchenAsync(orderId);
        }

        return Ok(new
        {
            message = "Order successfully submitted to the kitchen!",
            orderId = order.Id,
            orderNumber = order.OrderNumber,
            status = order.Status,
            totalAmount = order.TotalAmount
        });
    }

    [HttpPost("restaurants/{restaurantId:guid}/orders")]
    public async Task<IActionResult> CreateOrder(Guid restaurantId, [FromBody] CreateOrderRequest request)
    {
        // Guard the anonymous write surface: bound the payload size and every line
        // quantity before touching the DB. Reject the whole request on any bad line
        // rather than silently dropping it, so the bill/stock can't be steered.
        if (request.Items.Count > MaxOrderItems)
            return BadRequest(new { message = $"An order cannot contain more than {MaxOrderItems} items." });

        if (request.Items.Any(i => i.Quantity < 1 || i.Quantity > MaxLineQuantity))
            return BadRequest(new { message = $"Each item quantity must be between 1 and {MaxLineQuantity}." });

        var table = await _context.Tables.FindAsync(request.TableId);
        if (table is null)
            return NotFound(new { message = "Table not found." });

        // Happy hour: if the configured product is inside its daily window now, it
        // sells at the discounted price. The server is authoritative for the bill.
        var happyHour = await _happyHourService.GetActiveDealAsync(restaurantId);

        // Batch-load every referenced product in a single round-trip (was a per-item
        // FindAsync). The tenant query filter still scopes these to this restaurant.
        var requestedIds = request.Items.Select(i => i.ProductId).Distinct().ToList();
        var productsById = await _context.Products
            .Where(p => requestedIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        var order = new Order
        {
            TableId = request.TableId,
            Status = "draft",
            PaymentStatus = "unpaid",
            TotalAmount = 0
        };

        foreach (var item in request.Items)
        {
            if (!productsById.TryGetValue(item.ProductId, out var product))
                return BadRequest(new { message = $"Product {item.ProductId} not found." });

            // Don't let a diner order a staff-hidden item; the menu only hides these
            // for display, the write path must reject them.
            if (product.IsAvailable == false)
                return BadRequest(new { message = $"Product '{product.Name}' is not available." });

            order.Items.Add(new OrderItem
            {
                OrderId = order.Id,
                ProductId = item.ProductId,
                Quantity = item.Quantity,
                UnitPriceAtPurchase = _happyHourService.EffectiveUnitPrice(product, happyHour),
                IsFlashDealItem = false
            });
        }

        order.TotalAmount = order.Items.Sum(i => i.Quantity * i.UnitPriceAtPurchase);

        var itemsForResponse = order.Items.Select(i => new OrderItemResponse
        {
            ProductId = i.ProductId,
            ProductName = productsById.TryGetValue(i.ProductId, out var p) ? p.Name : "Unknown Item",
            Quantity = i.Quantity,
            UnitPrice = i.UnitPriceAtPurchase,
            IsFlashDealItem = false
        }).ToList();

        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        return Created($"/api/orders/{order.Id}", new OrderResponse
        {
            Id = order.Id,
            Status = order.Status ?? "draft",
            TotalAmount = order.TotalAmount,
            Items = itemsForResponse
        });
    }

    [HttpPatch("restaurants/{restaurantId:guid}/orders/{orderId:guid}/items")]
    public async Task<IActionResult> AddItemsOrder(Guid orderId, [FromBody] PatchOrderItemsRequest request)
    {
        var order = await _context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.Status == "draft");

        if(order == null)
            return NotFound(new { message = "Draft order not found or it has already been submitted." });

        order.Items ??= new List<OrderItem>();
        if (request?.Items == null) return BadRequest(new { message = "Invalid request payload." });

        // Same anonymous-write caps as CreateOrder: bound the payload and reject the
        // whole request on any out-of-range line rather than silently dropping it.
        if (request.Items.Count > MaxOrderItems)
            return BadRequest(new { message = $"An order cannot contain more than {MaxOrderItems} items." });

        if (request.Items.Any(i => i.Quantity < 1 || i.Quantity > MaxLineQuantity))
            return BadRequest(new { message = $"Each item quantity must be between 1 and {MaxLineQuantity}." });

        var producIds = request.Items.Select(i => i.ProductId).ToList();
        var products = await _context.Products.Where(p => producIds.Contains(p.Id)).ToListAsync();

        // Reject staff-hidden items on the write path (the menu only hides them for display).
        var unavailable = products.FirstOrDefault(p => p.IsAvailable == false);
        if (unavailable is not null)
            return BadRequest(new { message = $"Product '{unavailable.Name}' is not available." });

        // Happy-hour pricing for this restaurant right now (null outside the window).
        var happyHour = await _happyHourService.GetActiveDealAsync(order.RestaurantId);

        var incomingProductIds = request.Items.Where(i => i.Quantity > 0).Select(i => i.ProductId).ToList();

        var itemsToRemove = order.Items
            .Where(i => !incomingProductIds.Contains(i.ProductId) && i.IsFlashDealItem != true)
            .ToList();
            
        foreach (var item in itemsToRemove)
        {
            _context.OrderItems.Remove(item);
        }

        foreach (var reqItem in request.Items.Where(i => i.Quantity > 0))
        {
            var product = products.FirstOrDefault(p => p.Id == reqItem.ProductId);
            if (product == null) continue;

            var existingItem = order.Items.FirstOrDefault(i => i.ProductId == reqItem.ProductId && i.IsFlashDealItem != true);
            
            if (existingItem != null)
            {
                existingItem.Quantity = reqItem.Quantity;
                // Re-price to the current effective price so a happy-hour discount
                // (and its expiry when the window closes) tracks on every cart sync.
                existingItem.UnitPriceAtPurchase = _happyHourService.EffectiveUnitPrice(product, happyHour);
                // Don't overwrite WasAiRecommended — it's set on first add
            }
            else
            {
                var newItem = new OrderItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    ProductId = product.Id,
                    Quantity = reqItem.Quantity,
                    UnitPriceAtPurchase = _happyHourService.EffectiveUnitPrice(product, happyHour),
                    IsFlashDealItem = false,
                    WasAiRecommended = reqItem.WasAiRecommended
                };
                _context.OrderItems.Add(newItem);
                order.Items.Add(newItem);
            }
        }

        order.TotalAmount = order.Items.Sum(i => i.Quantity * i.UnitPriceAtPurchase);
        
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
                return Conflict(new { message = "The order was modified by someone else. Please refresh and try again." });
        }

        // Tell the other phones at this table their party view is stale so they
        // refetch /party. This is the deferred "PartyMemberUpdated" signal: it
        // makes peer carts/totals live and the "Merge our bills" button appear
        // as soon as a guest adds items (the frontend debounces this PATCH, so
        // the broadcast fires at most ~twice/second per phone). Only guest-owned
        // drafts participate in the party panel; legacy table-wide drafts skip it.
        if (order.GuestId is not null)
        {
            // Cart activity proves the phone is still at the table — keep its
            // seat inside the presence window even if the periodic /party
            // heartbeat is failing or throttled.
            await _context.TableGuests
                .Where(g => g.Id == order.GuestId.Value)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.LastSeenAt, DateTime.UtcNow));

            await _customerHub.Clients
                .Group(TenantGroups.Table(order.RestaurantId, order.TableId))
                .SendAsync("PartyMemberUpdated", new { guestId = order.GuestId });
        }

        return Ok(new
        {
            message = "Items successfully updated in your order!",
            totalAmount = order.TotalAmount
        });
    }
    
    // The restaurant's happy hour IF it's live right now (inside its daily window),
    // else null. The customer app polls this to show the menu banner and the
    // discounted price; the discount is applied authoritatively at order time.
    [HttpGet("restaurants/{restaurantId:guid}/happy-hour")]
    public async Task<IActionResult> GetActiveHappyHour(Guid restaurantId)
    {
        var deal = await _happyHourService.GetActiveDealAsync(restaurantId);
        if (deal?.Product == null) return NoContent();

        return Ok(new
        {
            productId = deal.ProductId,
            productName = deal.Product.Name,
            discountPercent = deal.DiscountPercent,
            originalPrice = deal.Product.Price,
            discountedPrice = HappyHourService.DiscountedPrice(deal.Product.Price, deal.DiscountPercent),
            startTime = deal.StartTime.ToString("HH:mm"),
            endTime = deal.EndTime.ToString("HH:mm"),
        });
    }

    [HttpGet("tables/{qrToken:guid}/session")]
    public async Task<IActionResult> GetTableSession(Guid qrToken)
    {
        var table = await _context.Tables
            .FirstOrDefaultAsync(t => t.QrToken == qrToken && t.DeletedAt == null);

        if (table == null)
            return NotFound(new { message = "Invalid QR Code. Table not found." });

        var draftOrder = await _context.Orders
            .Include(o => o.Items)
            .ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(o => o.TableId == table.Id && o.Status == "draft");

        if (draftOrder == null)
        {
            draftOrder = new Order
            {
                TableId = table.Id,
                Status = "draft",
                PaymentStatus = "unpaid",
                TotalAmount = 0
            };

            _context.Orders.Add(draftOrder);
            await _context.SaveChangesAsync();
        }

        var restaurant = await _context.Restaurants
            .Include(r => r.Organization)
            .FirstOrDefaultAsync(r => r.Id == table.RestaurantId);

        var response = new TableSessionResponse
        {
            RestaurantId = table.RestaurantId,
            RestaurantName = restaurant?.Name ?? "Restaurant",
            RestaurantAddress = restaurant?.Address,
            OrganizationName = restaurant?.Organization?.Name ?? string.Empty,
            TableId = table.Id,
            TableNumber = table.TableNumber ?? "Unknown Table",
            Branding = new BrandingInfo
            {
                Enabled = restaurant?.BrandingEnabled ?? false,
                Primary = restaurant?.BrandPrimary,
                Secondary = restaurant?.BrandSecondary,
                Background = restaurant?.BrandBackground,
                LogoUrl = restaurant?.LogoUrl,
                PhotoUrl = restaurant?.PhotoUrl,
                MenuBackgroundUrl = restaurant?.MenuBackgroundUrl,
                MenuBackgroundStyle = restaurant?.MenuBackgroundStyle,
                LogoBackground = restaurant?.LogoBackground,
                LogoIsDark = restaurant?.LogoIsDark,
            },
            ActiveOrder = new OrderResponse
            {
                Id = draftOrder.Id,
                Status = draftOrder.Status ?? "draft",
                TotalAmount = draftOrder.TotalAmount,
                Items = draftOrder.Items?.Select(item => new OrderItemResponse
                {
                    ProductId = item.ProductId,
                    ProductName = item.Product?.Name ?? "Unknown Item",
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPriceAtPurchase,
                    IsFlashDealItem = item.IsFlashDealItem ?? false
                }).ToList() ?? new List<OrderItemResponse>()
            }
        };

        return Ok(response);
    }

    [HttpPost("restaurants/{restaurantId:guid}/orders/{orderId:guid}/claim-deal/{dealId:guid}")]
    public async Task <IActionResult> ClaimFlashDeal (Guid orderId, Guid dealId)
    {
        try
        {
            var success = await _flashDealService.ClaimDealAsync(orderId, dealId);

            if(!success)
            {
                return BadRequest(new {message ="Deal is invalid, expired, or already claimed."});
            }

            return Ok(new {message = "Flash deal successfully added to your order!" });
        }
        catch (Exception ex)
        {
            // Anonymous endpoint: log the detail server-side, never echo ex.Message.
            _logger.LogError(ex, "Error claiming flash deal {DealId} for order {OrderId}", dealId, orderId);
            return StatusCode(500, new { message = "A apărut o eroare la revendicarea ofertei." });
        }
    }
   
    // Vibey: lazy, tap-triggered in-cart recommendations. Anonymous customer
    // endpoint, tenant-scoped by the restaurantId ROUTE param only (resolved by
    // TenantResolutionMiddleware) — the request body's cart is never trusted for
    // tenancy. Cached briefly per (restaurant, locale, cart) so re-opening the
    // Vibey sheet on an unchanged cart never re-hits the model.
    // A real diner cart never approaches this; an oversized payload is abusive.
    // Since this endpoint is anonymous and a cache miss triggers a paid model
    // call that forwards the whole cart, cap the input defensively.
    private const int MaxCartItems = 50;

    [HttpPost("restaurants/{restaurantId:guid}/recommendations")]
    public async Task<IActionResult> GetVibeyRecommendations(
        Guid restaurantId, [FromBody] VibeyRecommendationRequest request)
    {
        if (request?.Items == null || request.Items.Count == 0 || request.Items.Count > MaxCartItems)
        {
            return Ok(new VibeyRecommendationResponse("", []));
        }

        var locale = request.Locale ?? "ro";
        var sortedIds = string.Join(',', request.Items.Select(i => i.Id).OrderBy(id => id, StringComparer.Ordinal));
        var cacheKey = $"vibey:{restaurantId}:{locale}:{sortedIds}";

        if (_memoryCache.TryGetValue(cacheKey, out VibeyRecommendationResponse? cached) && cached is not null)
        {
            return Ok(cached);
        }

        var response = await _recommendationService.GetVibeyRecommendationsAsync(request.Items, locale);

        // Never cache a graceful-empty result (transient AI outage, exhausted
        // candidate pool) — that would poison this cart's cache with a false
        // "nothing to suggest" for up to the full sliding expiration.
        if (response.Recommendations.Count > 0)
        {
            _memoryCache.Set(cacheKey, response, new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(2)
            });
        }

        return Ok(response);
    }
}
