namespace RestaurantApi.DTOs.Responses;


public class TableSessionResponse
{
    public Guid RestaurantId {get;set;}
    public string RestaurantName {get;set;}=string.Empty;
    public string? RestaurantAddress {get;set;}
    public string OrganizationName {get;set;}=string.Empty;
    public Guid TableId {get;set;}
    public string TableNumber{get;set;}=string.Empty;
    public OrderResponse? ActiveOrder {get;set;}
    public BrandingInfo? Branding { get; set; }
}

public class BrandingInfo
{
    public bool Enabled { get; set; }
    public string? Primary { get; set; }
    public string? Secondary { get; set; }
    public string? Background { get; set; }
    public string? LogoUrl { get; set; }
    public string? PhotoUrl { get; set; }
    public string? MenuBackgroundUrl { get; set; }
    public string? MenuBackgroundStyle { get; set; }
    public string? LogoBackground { get; set; }
    public bool? LogoIsDark { get; set; }
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
    public string? ImageUrl {get;set;}
}