using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
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
    private readonly RecommendationService _recommendationService;
    private readonly IHubContext<StaffHub> _staffHub;
    private readonly IHubContext<CustomerHub> _customerHub;

    public CustomerController(
        AppDbContext context,
        IFlashDealService flashDealService,
        RecommendationService recommendationService,
        IHubContext<StaffHub> staffHub,
        IHubContext<CustomerHub> customerHub)
    {
        _context = context;
        _flashDealService = flashDealService;
        _recommendationService = recommendationService;
        _staffHub = staffHub;
        _customerHub = customerHub;
    }

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
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
    {
        var table = await _context.Tables.FindAsync(request.TableId);
        if (table is null)
            return NotFound(new { message = "Table not found." });

        var order = new Order
        {
            TableId = request.TableId,
            Status = "draft",
            PaymentStatus = "unpaid",
            TotalAmount = 0
        };

        var fetchedProducts = new List<Product>();

        foreach (var item in request.Items)
        {
            var product = await _context.Products.FindAsync(item.ProductId);
            if (product is null)
                return BadRequest(new { message = $"Product {item.ProductId} not found." });

            fetchedProducts.Add(product);

            order.Items.Add(new OrderItem
            {
                OrderId = order.Id,
                ProductId = item.ProductId,
                Quantity = item.Quantity,
                UnitPriceAtPurchase = product.Price,
                IsFlashDealItem = false
            });
        }

        order.TotalAmount = order.Items.Sum(i => i.Quantity * i.UnitPriceAtPurchase);

        var itemsForResponse = order.Items.Select(i => new OrderItemResponse
        {
            ProductId = i.ProductId,
            ProductName = fetchedProducts.FirstOrDefault(p => p.Id == i.ProductId)?.Name ?? "Unknown Item",
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

        var producIds = request.Items.Select(i => i.ProductId).ToList();
        var products = await _context.Products.Where(p => producIds.Contains(p.Id)).ToListAsync();

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
                    UnitPriceAtPurchase = product.Price,
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
            return StatusCode(500, new { message = $"Internal error claiming deal: {ex.Message}" });
        }
    }
   
    [HttpGet("restaurants/{restaurantId:guid}/orders/{id:guid}/recommendations")]
    public async Task<IActionResult> GetRecommendation(Guid id, [FromQuery] string? language)
    {
        var order = await _context.Orders.FindAsync(id);

        if (order == null)
        {
            return NotFound(new { message = "Order not found." });
        }

        var rawRecommendations = await _recommendationService.GetRecommendationsAsync(id, language ?? "ro");

        if (rawRecommendations == null || !rawRecommendations.Any())
        {
            return Ok(new { pairings = new List<object>(), upsells = new List<object>(), flashDeal = (object?)null });        
        }

        // Safely convert string ProductIds from AI into valid Guids for the database query
        var recommendedProductIds = rawRecommendations
            .Select(r => Guid.TryParse(r.ProductId, out var guid) ? guid : Guid.Empty)
            .Where(g => g != Guid.Empty)
            .ToList();

        var products = await _context.Products
            .Where(p => recommendedProductIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        var mappedItems = rawRecommendations
            .Where(r => Guid.TryParse(r.ProductId, out var g) && products.ContainsKey(g))
            .Select(r => 
            {
                var pId = Guid.Parse(r.ProductId);
                return new
                {
                    reason = r.Reason,
                    confidence = r.Confidence,
                    menuItem = new
                    {
                        id = products[pId].Id,
                        name = products[pId].Name,
                        price = products[pId].Price,
                        imageUrl = products[pId].ImageUrl 
                    }
                };
            })
            .OrderByDescending(r => r.confidence)
            .ToList();
            
        var splitIndex = mappedItems.Count / 2;
        var pairings = mappedItems.Take(splitIndex).ToList();
        var upsells = mappedItems.Skip(splitIndex).ToList();

        var activeFlashDeal = await _context.FlashDealOffers
            .Include(d => d.Product)
            .Where(d => d.OrderId == id && d.Status == "pending" && d.ExpiresAt > DateTime.UtcNow)
            .FirstOrDefaultAsync();

        object? flashDealResponse = null;
        if (activeFlashDeal != null && activeFlashDeal.Product != null)
        {
            flashDealResponse = new 
            {
                id = activeFlashDeal.Id,
                discountPrice = activeFlashDeal.DiscountPrice,
                expiresAt = activeFlashDeal.ExpiresAt,
                menuItem = new 
                {
                    id = activeFlashDeal.Product.Id,
                    name = activeFlashDeal.Product.Name,
                    price = activeFlashDeal.Product.Price,
                    imageUrl = activeFlashDeal.Product.ImageUrl
                }
            };
        }

        return Ok(new
        {
            pairings = pairings,
            upsells = upsells,
            flashDeal = flashDealResponse
        });
    }
}