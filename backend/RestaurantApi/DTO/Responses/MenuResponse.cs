using Microsoft.AspNetCore.SignalR;
using Microsoft.Net.Http.Headers;

namespace RestaurantApi.DTOs.Responses;

public class MenuCategoryResponse
{
    public Guid Id {get; set;}
    public string Name {get;set;}=string.Empty;
    public int SortOrder{get;set;}
    public List<MenuProductResponse> Products {get; set;} = [];
}

public class MenuProductResponse
{
    public Guid Id {get; set;}
    public string Name{get;set;}=string.Empty;
    public string? Description {get;set;}
    public decimal Price{get;set;}
    public string? ImageUrl{get;set;}
}