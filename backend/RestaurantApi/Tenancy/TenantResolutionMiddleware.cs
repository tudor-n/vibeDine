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

    // (HTTP method, path-suffix) pairs a Waiter is allowed to hit. The suffix is
    // the part after /api/restaurants/{rid}. Anything outside this set returns 403
    // even though they pass the membership check. Waiters get: the orders board and
    // log (read + advance status), the floor plan (read-only zones/tables layout +
    // claim/release their own orders), and the waiter-call queue. Write endpoints
    // for menu, zones, tables, AI config, etc. stay manager-only — both the verb
    // and the path must match, so e.g. POST /admin/zones (create zone) is rejected
    // while GET /admin/zones (read layout) is allowed.
    private static readonly (string Method, Regex Path)[] WaiterAllowedPaths =
    [
        ("GET",   new(@"^/admin/orders/?$", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("GET",   new(@"^/admin/orders/active/?$", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("PATCH", new(@"^/admin/orders/[0-9a-fA-F-]{36}/status/?$", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("POST",  new(@"^/admin/orders/[0-9a-fA-F-]{36}/claim/?$", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("POST",  new(@"^/admin/orders/[0-9a-fA-F-]{36}/release/?$", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("GET",   new(@"^/admin/zones/?$", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("GET",   new(@"^/admin/tables/?$", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("GET",   new(@"^/help-requests/?$", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("POST",  new(@"^/help-requests/[0-9a-fA-F-]{36}/acknowledge/?$", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
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
                    var method = ctx.Request.Method;
                    var allowed = WaiterAllowedPaths.Any(rule =>
                        string.Equals(rule.Method, method, StringComparison.OrdinalIgnoreCase) &&
                        rule.Path.IsMatch(suffix));
                    if (!allowed)
                    {
                        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await ctx.Response.WriteAsJsonAsync(new { message = "Waiters do not have access to this resource." });
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
                    .FirstOrDefaultAsync(t => t.QrToken == qrToken && t.DeletedAt == null);

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
