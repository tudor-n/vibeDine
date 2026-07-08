using Microsoft.EntityFrameworkCore;
using RestaurantApi.Data;
using RestaurantApi.Models;

namespace RestaurantApi.Services;

public interface IHappyHourService
{
    // The restaurant's enabled happy hour IF the current moment falls inside its
    // daily window (restaurant-local time), else null. Product is eager-loaded.
    Task<HappyHourDeal?> GetActiveDealAsync(Guid restaurantId);

    // What a product should sell for given the active happy hour (or null):
    // the discounted price when it's the happy-hour product, otherwise normal price.
    decimal EffectiveUnitPrice(Product product, HappyHourDeal? activeDeal);
}

// Centralises the happy-hour window/timezone logic so order pricing, the customer
// banner endpoint, and any future caller all agree. Happy hour = a real price drop
// for one product during a daily wall-clock window (in the restaurant's timezone).
public class HappyHourService : IHappyHourService
{
    private readonly AppDbContext _context;
    private readonly ILogger<HappyHourService> _logger;

    public HappyHourService(AppDbContext context, ILogger<HappyHourService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<HappyHourDeal?> GetActiveDealAsync(Guid restaurantId)
    {
        // IgnoreQueryFilters + explicit restaurantId so this is correct from any
        // context, including unauthenticated customer routes where the ambient
        // tenant may not be resolved.
        var deal = await _context.HappyHourDeals
            .IgnoreQueryFilters()
            .Include(h => h.Product)
            .FirstOrDefaultAsync(h => h.RestaurantId == restaurantId && h.IsEnabled == true);

        if (deal == null) return null;

        var restaurant = await _context.Restaurants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == restaurantId);

        var tz = ResolveTimeZone(restaurant?.Timezone);
        var localNow = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));

        return IsWithinWindow(localNow, deal.StartTime, deal.EndTime) ? deal : null;
    }

    public decimal EffectiveUnitPrice(Product product, HappyHourDeal? activeDeal)
    {
        if (activeDeal != null && activeDeal.ProductId == product.Id)
            return DiscountedPrice(product.Price, activeDeal.DiscountPercent);
        return product.Price;
    }

    public static decimal DiscountedPrice(decimal price, int discountPercent)
        => Math.Round(price * (1 - discountPercent / 100m), 2);

    private TimeZoneInfo ResolveTimeZone(string? timezoneId)
    {
        if (string.IsNullOrWhiteSpace(timezoneId))
            return TimeZoneInfo.Utc;
        try
        {
            // .NET 8 resolves IANA ids (e.g. "Europe/Bucharest") on Linux and Windows.
            return TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unknown restaurant timezone '{Tz}'; falling back to UTC.", timezoneId);
            return TimeZoneInfo.Utc;
        }
    }

    // Inclusive of start, exclusive of end. Handles a window that wraps past
    // midnight (start > end, e.g. 22:00-02:00); a zero-length window never matches.
    public static bool IsWithinWindow(TimeOnly now, TimeOnly start, TimeOnly end)
    {
        if (start == end) return false;
        return start < end
            ? now >= start && now < end
            : now >= start || now < end;
    }
}
