using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantApi.Data;
using RestaurantApi.Models;
using RestaurantApi.Services;


namespace RestaurantApi.Controllers;

[ApiController]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IFlashDealService _flashDealService;
    private readonly ILogger <AdminController> _logger;
 
    public AdminController(AppDbContext dbContext, IHttpClientFactory httpClientFactory, IFlashDealService flashDealService, ILogger <AdminController> logger)
    {
        _dbContext = dbContext;
        _httpClientFactory = httpClientFactory;
        _flashDealService = flashDealService;
        _logger=logger;
    }

    [HttpGet("directives")]
    public async Task<IActionResult> GetDirectives()
    {
        var directives = await _dbContext.AIDirectives
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync();
        
        return Ok(directives);
    }

    [HttpPost("directives")]
    public async Task<IActionResult> CreateDirective([FromBody] CreateDirectiveRequest request)
    {
        var client = _httpClientFactory.CreateClient("AiService");
        string? parsedRules = null;
        
        try
        {
            var aiResponse = await client.PostAsJsonAsync("/directives/parse", new { raw_prompt = request.RawPrompt });
            if (aiResponse.IsSuccessStatusCode)
            {
                var result = await aiResponse.Content.ReadFromJsonAsync<JsonElement>();
                if (result.TryGetProperty("parsed_rules", out var pr))
                {
                    parsedRules = pr.GetRawText();
                }
            }
        }
        catch(Exception ex)
        {
            _logger.LogWarning(ex, "Ai service failed to praise the directive");
        }

        var directive = new AIDirective
        {
            Id = Guid.NewGuid(),
            RawPrompt = request.RawPrompt,
            ParsedRules = parsedRules,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        _dbContext.AIDirectives.Add(directive);
        await _dbContext.SaveChangesAsync();

        return Ok(directive);
    }


    [HttpPatch("directives/{id}")]
    public async Task<IActionResult> ToggleDirective(Guid id, [FromBody] bool isActive)
    {
        var directive = await _dbContext.AIDirectives.FindAsync(id);
        if (directive == null) return NotFound();

        directive.IsActive = isActive;
        await _dbContext.SaveChangesAsync();
        return Ok(directive);
    }


    [HttpGet("similarity-rules")]
    public async Task<IActionResult> GetSimilarityRules()
    {
        var rules = await _dbContext.SimilarityRules
            .Include(r => r.ProductA)
            .Include(r => r.ProductB)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new
            {
                r.Id,
                r.ProductAId,
                productAName = r.ProductA != null ? r.ProductA.Name : null,
                r.ProductBId,
                productBName = r.ProductB != null ? r.ProductB.Name : null,
                r.Score,
                r.RuleType,
                r.CreatedAt
            })
            .ToListAsync();

        return Ok(rules);
    }

    [HttpPost("similarity-rules/auto-generate")]
    public async Task<IActionResult> AutoGenerateRules()
    {
        var client = _httpClientFactory.CreateClient("AiService");
        
        var orders = await _dbContext.Orders.Include(o => o.Items).ToListAsync();
        var history = orders.Select(o => o.Items.Select(i => i.ProductId.ToString()).ToList()).ToList();

        try 
        {
            var aiResponse = await client.PostAsJsonAsync("/similarity/auto-generate", new { order_history = history });
            if (aiResponse.IsSuccessStatusCode)
            {
                var result = await aiResponse.Content.ReadFromJsonAsync<JsonElement>();
                return Ok(result);
            }
        }
        catch 
        {
            return StatusCode(503, new { message = "AI Service unavailable" });
        }

        return StatusCode(500);
    }


    [HttpGet("metrics")]
    public async Task <IActionResult> GetMetrics()
    {
        var totalDeals = await _dbContext.FlashDealOffers.CountAsync();
        var claimedDeals = await _dbContext.FlashDealOffers.CountAsync(d=>d.Status=="claimed");
        var expiredDeals = await _dbContext.FlashDealOffers.CountAsync(d=>d.Status=="expired");

        var upSellRevenue = await _dbContext.OrderItems
        .Where(i=>i.IsFlashDealItem==true)
        .SumAsync(i => i.Quantity * i.UnitPriceAtPurchase);


        return Ok(new
        {
           flashDealsShown = totalDeals,        
           flashDealsClaimed = claimedDeals,    
           flashDealsExpired = expiredDeals,    
           upsellRevenue = upSellRevenue 
        });
    }

    [HttpGet("flash-deals")]
    public async Task <IActionResult> GetFlashDeals()
    {
        var deals= await _dbContext.FlashDealOffers
        .Include(d=>d.Product)
        .OrderByDescending(d => d.ExpiresAt)
        .Select(d => new
        {
            d.Id,
            d.Status,
            d.DiscountPrice,
            d.ExpiresAt,
            d.OrderId,
            ProductName = d.Product != null ? d.Product.Name : "Unknown"
        }).ToListAsync();

     return Ok(deals);
    
    }
   
   [HttpPost("flash-deals/trigger")]
   public async Task<IActionResult> TriggerFlashDeal([FromBody] Guid orderId)
    {
        await _flashDealService.EvaluateEligibilityAsync(orderId);
        return Ok(new {message = "Flash deal triggered successfully."});
    }

}

public class CreateDirectiveRequest
{
    [JsonPropertyName("rawPrompt")]
    public string RawPrompt { get; set; } = string.Empty;
}