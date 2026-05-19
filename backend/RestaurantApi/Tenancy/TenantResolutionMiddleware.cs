using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RestaurantApi.Data;

namespace RestaurantApi.Tenancy;

/// <summary>
/// Resolves the active restaurant for the request and enforces tenant isolation.
///
/// Resolution order:
///   1. Route value <c>restaurantId</c>  → dashboard &amp; public restaurant-scoped endpoints.
///   2. Route value <c>qrToken</c>       → customer entry; table → restaurant.
///
/// For authenticated requests carrying a <c>restaurantId</c>, the caller's membership is
/// verified (OrgOwner of the org, or RestaurantAdmin of that restaurant) before the
/// request is allowed to proceed.
/// </summary>
public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext ctx,
        ICurrentTenant tenant,
        AppDbContext db,
        ITenantAccess access)
    {
        var routeValues = ctx.Request.RouteValues;

        if (routeValues.TryGetValue("restaurantId", out var ridRaw) &&
            Guid.TryParse(ridRaw?.ToString(), out var restaurantId))
        {
            using (tenant.SuppressResolution())
            {
                var restaurant = await db.Restaurants
                    .FirstOrDefaultAsync(r => r.Id == restaurantId);

                if (restaurant is null)
                {
                    ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                    await ctx.Response.WriteAsJsonAsync(new { message = "Restaurant not found." });
                    return;
                }

                if (!restaurant.IsActive)
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await ctx.Response.WriteAsJsonAsync(new { message = "This restaurant is not active." });
                    return;
                }
            }

            // Authenticated dashboard request → must hold a valid membership.
            if (ctx.User?.Identity?.IsAuthenticated == true)
            {
                var userId = GetUserId(ctx.User);
                if (userId is null || !await access.CanAccessRestaurantAsync(userId.Value, restaurantId))
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await ctx.Response.WriteAsJsonAsync(new { message = "You do not have access to this restaurant." });
                    return;
                }
            }

            tenant.SetRestaurant(restaurantId);
        }
        else if (routeValues.TryGetValue("qrToken", out var qrRaw) &&
                 Guid.TryParse(qrRaw?.ToString(), out var qrToken))
        {
            using (tenant.SuppressResolution())
            {
                var table = await db.Tables
                    .FirstOrDefaultAsync(t => t.QrToken == qrToken);

                if (table is not null)
                    tenant.SetRestaurant(table.RestaurantId);
            }
        }

        await _next(ctx);
    }

    private static Guid? GetUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? user.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
