namespace RestaurantApi.DTOs.Requests;

public class UpdateBrandingRequest
{
    public bool Enabled { get; set; }
    public string? Primary { get; set; }
    public string? Secondary { get; set; }
    public string? Background { get; set; }
    public string? PresetKey { get; set; }
}
