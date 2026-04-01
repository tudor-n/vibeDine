using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantApi.Data;
using RestaurantApi.DTOs.Responses;
using RestaurantApi.Models;
using RestaurantApi.DTOs.Requests;

namespace RestaurantApi.Controllers;

[ApiController]
[Route("api")]
public class CustomerController : ControllerBase
{
    private readonly AppDbContext _context;

    public CustomerController(AppDbContext context)
    {
        _context = context;
    }


    [HttpPatch("orders/{orderId:guid}/submit")]
    public async Task<IActionResult> SubmitOrder(Guid orderId)
    {
        var order = await _context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.Status == "draft");

        if (order == null)
        {
            return NotFound(new { message = "Draft order not found or it has already been submitted." });
        }

        if (order.Items == null || order.Items.Count == 0)
        {
            return BadRequest(new { message = "Cannot submit an empty order. Please add items to your cart first." });
        }
        order.Status = "submitted";
        await _context.SaveChangesAsync();

        return Ok(new 
        { 
            message = "Order successfully submitted to the kitchen!",
            orderId = order.Id,
            status = order.Status,
            totalAmount = order.TotalAmount
        });
    }
    

   [HttpPatch("orders/{orderId:guid}/items")]
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

        var itemsToRemove = order.Items.Where(i => !incomingProductIds.Contains(i.ProductId)).ToList();
        foreach (var item in itemsToRemove)
        {
            _context.OrderItems.Remove(item);
        }

        foreach (var reqItem in request.Items.Where(i => i.Quantity > 0))
        {
            var product = products.FirstOrDefault(p => p.Id == reqItem.ProductId);
            if (product == null) continue;

            var existingItem = order.Items.FirstOrDefault(i => i.ProductId == reqItem.ProductId);
            if (existingItem != null)
            {
                existingItem.Quantity = reqItem.Quantity; 
            }
            else
            {
                order.Items.Add(new OrderItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    ProductId = product.Id,
                    Quantity = reqItem.Quantity,
                    UnitPriceAtPurchase = product.Price,
                    IsFlashDealItem = false
                });
            }
        }

        order.TotalAmount = order.Items.Sum(i => i.Quantity * i.UnitPriceAtPurchase);
        
        try 
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
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
            .FirstOrDefaultAsync(t => t.QrToken == qrToken);

        if (table == null)
        {
            return NotFound(new { message = "Invalid QR Code. Table not found." });
        }

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
        
        var response = new TableSessionResponse
        {
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
}