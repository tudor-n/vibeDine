namespace RestaurantApi.DTOs.Requests;

public class CreateZoneRequest
{
    public string Name { get; set; } = string.Empty;
}

public class UpdateZoneRequest
{
    public string? Name { get; set; }
    public int? SortOrder { get; set; }
}
