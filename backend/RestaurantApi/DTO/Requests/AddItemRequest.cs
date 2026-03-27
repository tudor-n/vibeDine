namespace RestaurantApi.DTOs.Requests;


public class CreatOrderRequest
{
    public Guid TableId {get;set;}
    public List<OrderItemRequest> Items {get;set;} =[];
}

public class PatchOrderItemsRequest
{
    public List<OrderItemRequest> Items {get;set;}=[];
}


public class OrderItemRequest
{
    public Guid ProductId {get;set;}
    public int Quantity{get;set;}
}