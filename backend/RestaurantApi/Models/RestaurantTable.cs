namespace RestaurantApi.Models;

public class RestaurantTable : ITenantScoped {
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RestaurantId { get; set; }

    public string? TableNumber { get; set; }

    public Guid? QrToken { get; set; }

    public ICollection<Order> Orders { get; set; } = [];
}