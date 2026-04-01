namespace RestaurantApi.DTOs.Responses;


public class TableSessionResponse
{
    public Guid TableId {get;set;}
    public string TableNumber{get;set;}=string.Empty;
    public OrderResponse? ActiveOrder {get;set;}
}

public class OrderResponse
{
    public Guid Id {get;set;}
    public string Status {get; set;} =string.Empty;
    public decimal TotalAmount{get;set;}
    public List <OrderItemResponse> Items {get; set;} = [];
}


public class OrderItemResponse
{
    public Guid ProductId {get;set;}
    public string ProductName {get;set;}=string.Empty;
    public int Quantity {get;set;}
    public decimal UnitPrice {get;set;}
    public bool IsFlashDealItem {get;set;}
}