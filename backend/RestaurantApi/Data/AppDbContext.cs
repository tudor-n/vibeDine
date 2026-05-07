using Microsoft.EntityFrameworkCore;
using RestaurantApi.Models;

namespace RestaurantApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<RestaurantTable> Tables => Set<RestaurantTable>();
    public DbSet<FlashDealOffer> FlashDealOffers => Set<FlashDealOffer>();
    public DbSet<AIDirective> AIDirectives => Set<AIDirective>();
    public DbSet<RecommendationDirective> RecommendationDirectives => Set<RecommendationDirective>();
    public DbSet<SimilarityRule> SimilarityRules { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Category>(e =>
        {
            e.ToTable("categories");
            e.Property(c => c.Id).HasColumnName("id");
            e.Property(c => c.Name).HasColumnName("name");
            e.Property(c => c.SortOrder).HasColumnName("sort_order");
        });

        modelBuilder.Entity<Product>(e =>
        {
            e.ToTable("products");
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.CategoryId).HasColumnName("category_id");
            e.Property(p => p.Name).HasColumnName("name");
            e.Property(p => p.Description).HasColumnName("description");
            e.Property(p => p.Price).HasColumnName("price");
            e.Property(p => p.ImageUrl).HasColumnName("image_url");
            e.Property(p => p.IsAvailable).HasColumnName("is_available");

            e.HasOne(p => p.Category)
             .WithMany(c => c.Products)
             .HasForeignKey(p => p.CategoryId);
        });

        modelBuilder.Entity<RestaurantTable>(e =>
        {
            e.ToTable("tables");
            e.Property(t => t.Id).HasColumnName("id");
            e.Property(t => t.TableNumber).HasColumnName("table_number");
            e.Property(t => t.QrToken).HasColumnName("qr_token");
        });

        modelBuilder.Entity<Order>(e =>
        {
            e.ToTable("orders");
            e.Property(o => o.Id).HasColumnName("id");
            e.Property(o => o.TableId).HasColumnName("table_id");
            e.Property(o => o.Status).HasColumnName("status");
            e.Property(o => o.PaymentStatus).HasColumnName("payment_status");
            e.Property(o => o.TotalAmount).HasColumnName("total_amount");
            e.Property(o => o.CreatedAt).HasColumnName("created_at");

            e.HasOne(o => o.Table)
             .WithMany(t => t.Orders)
             .HasForeignKey(o => o.TableId);
        });

        modelBuilder.Entity<OrderItem>(e =>
        {
            e.ToTable("order_items");
            e.Property(oi => oi.Id).HasColumnName("id");
            e.Property(oi => oi.OrderId).HasColumnName("order_id");
            e.Property(oi => oi.ProductId).HasColumnName("product_id");
            e.Property(oi => oi.Quantity).HasColumnName("quantity");
            e.Property(oi => oi.UnitPriceAtPurchase).HasColumnName("unit_price_at_purchase");
            e.Property(oi => oi.WasAiRecommended).HasColumnName("was_ai_recommended");
            e.Property(oi => oi.IsFlashDealItem).HasColumnName("is_flash_deal_item");

            e.HasOne(oi => oi.Order)
             .WithMany(o => o.Items)
             .HasForeignKey(oi => oi.OrderId);

            // Restrict so deleting a Product cannot wipe historical OrderItems.
            // Admins should soft-delete via IsAvailable=false instead.
            e.HasOne(oi => oi.Product)
             .WithMany()
             .HasForeignKey(oi => oi.ProductId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FlashDealOffer>(e =>
        {
            e.ToTable("flash_deal_offers");
            e.Property(f => f.Id).HasColumnName("id");
            e.Property(f => f.OrderId).HasColumnName("order_id");
            e.Property(f => f.ProductId).HasColumnName("product_id");
            e.Property(f => f.DiscountPrice).HasColumnName("discount_price");
            e.Property(f => f.ExpiresAt).HasColumnName("expires_at");
            e.Property(f => f.Status).HasColumnName("status");

            e.HasOne(f => f.Order)
             .WithMany(o => o.FlashDeals)
             .HasForeignKey(f => f.OrderId)
             .IsRequired(false);

            e.HasOne(f => f.Product)
             .WithMany()
             .HasForeignKey(f => f.ProductId)
             .IsRequired(false);
        });

        modelBuilder.Entity<AIDirective>(e =>
        {
            e.ToTable("ai_directives");
            e.Property(a => a.Id).HasColumnName("id");
            e.Property(a => a.RawPrompt).HasColumnName("raw_prompt");
            e.Property(a => a.ParsedRules).HasColumnName("parsed_rules");
            e.Property(a => a.CreatedAt).HasColumnName("created_at");
            e.Property(a => a.IsActive).HasColumnName("is_active");
        });

        modelBuilder.Entity<RecommendationDirective>(e =>
        {
            e.ToTable("recommendation_directives");
            e.Property(a => a.Id).HasColumnName("id");
            e.Property(a => a.RawPrompt).HasColumnName("raw_prompt");
            e.Property(a => a.ParsedRules).HasColumnName("parsed_rules");
            e.Property(a => a.CreatedAt).HasColumnName("created_at");
            e.Property(a => a.IsActive).HasColumnName("is_active");
        });

        modelBuilder.Entity<SimilarityRule>(e =>
        {
            e.ToTable("similarity_rules");
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).HasColumnName("id");
            e.Property(r => r.ProductAId).HasColumnName("product_a_id");
            e.Property(r => r.ProductBId).HasColumnName("product_b_id");
            e.Property(r => r.Score).HasColumnName("score").HasColumnType("decimal(5,4)");
            e.Property(r => r.RuleType).HasColumnName("rule_type");
            e.Property(r => r.CreatedAt).HasColumnName("created_at");

            e.HasOne(r => r.ProductA)
             .WithMany()
             .HasForeignKey(r => r.ProductAId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(r => r.ProductB)
             .WithMany()
             .HasForeignKey(r => r.ProductBId)
             .OnDelete(DeleteBehavior.Restrict);
        });
    }
}