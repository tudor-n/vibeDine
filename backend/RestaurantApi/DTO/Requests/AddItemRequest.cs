using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace RestaurantApi.DTOs.Requests;


public class CreateOrderRequest
{
    public Guid TableId {get;set;}
    public List<OrderItemRequest> Items {get;set;} =[];
}

public class PatchOrderItemsRequest
{
   
    [JsonPropertyName("items")]
    public List<OrderItemRequest> Items { get; set; } = [];
}


public class OrderItemRequest
{
    public Guid ProductId {get;set;}

    // A single order line is capped at 99 units; anything outside [1, 99] is
    // rejected (negative quantities would undercharge the bill and inflate stock
    // at dispatch, huge quantities are an availability DoS). [ApiController]
    // auto-rejects out-of-range values with 400; the controllers re-check too.
    [Range(1, 99, ErrorMessage = "Quantity must be between 1 and 99.")]
    public int Quantity{get;set;}
    public bool WasAiRecommended { get; set; } = false;
}