using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RestaurantApi.Data;
using RestaurantApi.Models;

namespace RestaurantApi.Tenancy;

/// <summary>
/// Resolves the active restaurant for the request and enforces tenant isolation.
///
/// Resolution order:
///   1. Route value <c>restaurantId</c>  → dashboard &amp; public restaurant-scoped endpoints.
///   2. Route value <c>qrToken</c>       → customer entry; table → restaurant.
///
/// For authenticated requests carrying a <c>restaurantId</c>, the caller's membership is
/// verified (OrgOwner of the org, RestaurantAdmin of that restaurant, or active Waiter).
/// Active Waiters are additionally restricted to the orders-related sub-paths.
/// </summary>
public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;

    // Path suffixes (after /api/restaurants/{rid}) that a Waiter is allowed to hit.
    // Anything outside this set returns 403 even though they pass the membership check.
    private static readonly Regex[] WaiterAllowedPaths =
    [
        new(@"^/admin/orders/?$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^/admin/orders/active/?$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^/admin/orders/[0-9a-fA-F-]{36}/status/?$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

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

                // Waiters are further scoped to orders endpoints. If the caller has a
                // manager-level membership for this restaurant, skip the restriction —
                // a user can hold both manager and waiter rows (rare but legal).
                var canManage = await access.CanManageRestaurantAsync(userId.Value, restaurantId);
                if (!canManage && await access.IsActiveWaiterAsync(userId.Value, restaurantId))
                {
                    var suffix = SuffixAfterRestaurantSegment(ctx.Request.Path);
                    if (!WaiterAllowedPaths.Any(rx => rx.IsMatch(suffix)))
                    {
                        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await ctx.Response.WriteAsJsonAsync(new { message = "Waiters can only access orders for this restaurant." });
                        return;
                    }
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

    private static string SuffixAfterRestaurantSegment(PathString path)
    {
        // /api/restaurants/{guid}/<suffix>
        var value = path.Value ?? string.Empty;
        var marker = "/api/restaurants/";
        var idx = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return value;
        var afterMarker = value[(idx + marker.Length)..];
        var slash = afterMarker.IndexOf('/');
        return slash < 0 ? string.Empty : afterMarker[slash..];
    }
}
