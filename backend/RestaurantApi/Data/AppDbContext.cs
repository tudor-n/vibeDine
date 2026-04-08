using Microsoft.EntityFrameworkCore;
using RestaurantApi.Models;

namespace RestaurantApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<RestaurantTable> Tables => Set<RestaurantTable>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<FlashDealOffer> FlashDealOffers => Set<FlashDealOffer>();
    public DbSet<AIDirective> AIDirectives => Set<AIDirective>();
    public DbSet<SimilarityRule> SimilarityRules => Set<SimilarityRule>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Categories ───────────────────────────────────────────────────────────
        modelBuilder.Entity<Category>(e =>
        {
            e.ToTable("categories");
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).HasColumnName("id");
            e.Property(c => c.Name).HasColumnName("name").IsRequired();
            e.Property(c => c.SortOrder).HasColumnName("sort_order");
        });

        // ── Products ─────────────────────────────────────────────────────────────
        modelBuilder.Entity<Product>(e =>
        {
            e.ToTable("products");
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.CategoryId).HasColumnName("category_id");
            e.Property(p => p.Name).HasColumnName("name").IsRequired();
            e.Property(p => p.Description).HasColumnName("description");
            e.Property(p => p.Price).HasColumnName("price").HasColumnType("decimal(10,2)");
            e.Property(p => p.ImageUrl).HasColumnName("image_url");
            e.Property(p => p.IsAvailable).HasColumnName("is_available");

            e.HasOne(p => p.Category)
             .WithMany(c => c.Products)
             .HasForeignKey(p => p.CategoryId);
        });

        // ── Tables ───────────────────────────────────────────────────────────────
        modelBuilder.Entity<RestaurantTable>(e =>
        {
            e.ToTable("tables");
            e.HasKey(t => t.Id);
            e.Property(t => t.Id).HasColumnName("id");
            e.Property(t => t.TableNumber).HasColumnName("table_number");
            e.Property(t => t.QrToken).HasColumnName("qr_token");
        });

        // ── Orders ───────────────────────────────────────────────────────────────
        modelBuilder.Entity<Order>(e =>
        {
            e.ToTable("orders");
            e.HasKey(o => o.Id);
            e.Property(o => o.Id).HasColumnName("id");
            e.Property(o => o.TableId).HasColumnName("table_id");
            e.Property(o => o.Status).HasColumnName("status").IsRequired();
            e.Property(o => o.PaymentStatus).HasColumnName("payment_status");
            e.Property(o => o.TotalAmount).HasColumnName("total_amount").HasColumnType("decimal(10,2)");
            e.Property(o => o.CreatedAt).HasColumnName("created_at");

            e.HasOne(o => o.Table)
             .WithMany(t => t.Orders)
             .HasForeignKey(o => o.TableId);
        });

        // ── OrderItems ───────────────────────────────────────────────────────────
        modelBuilder.Entity<OrderItem>(e =>
        {
            e.ToTable("order_items");
            e.HasKey(oi => oi.Id);
            e.Property(oi => oi.Id).HasColumnName("id");
            e.Property(oi => oi.OrderId).HasColumnName("order_id");
            e.Property(oi => oi.ProductId).HasColumnName("product_id");
            e.Property(oi => oi.Quantity).HasColumnName("quantity");
            e.Property(oi => oi.UnitPriceAtPurchase).HasColumnName("unit_price_at_purchase").HasColumnType("decimal(10,2)");
            e.Property(oi => oi.WasAiRecommended).HasColumnName("was_ai_recommended");
            e.Property(oi => oi.IsFlashDealItem).HasColumnName("is_flash_deal_item");

            e.HasOne(oi => oi.Order)
             .WithMany(o => o.Items)
             .HasForeignKey(oi => oi.OrderId);

            e.HasOne(oi => oi.Product)
             .WithMany()
             .HasForeignKey(oi => oi.ProductId);
        });

        // ── FlashDealOffers ──────────────────────────────────────────────────────
        modelBuilder.Entity<FlashDealOffer>(e =>
        {
            e.ToTable("flash_deal_offers");
            e.HasKey(f => f.Id);
            e.Property(f => f.Id).HasColumnName("id");
            e.Property(f => f.OrderId).HasColumnName("order_id");
            e.Property(f => f.ProductId).HasColumnName("product_id");
            e.Property(f => f.DiscountPrice).HasColumnName("discount_price").HasColumnType("decimal(10,2)");
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

        // ── AIDirectives ─────────────────────────────────────────────────────────
        modelBuilder.Entity<AIDirective>(e =>
        {
            e.ToTable("ai_directives");
            e.HasKey(a => a.Id);
            e.Property(a => a.Id).HasColumnName("id");
            e.Property(a => a.RawPrompt).HasColumnName("raw_prompt");
            e.Property(a => a.ParsedRules).HasColumnName("parsed_rules").HasColumnType("jsonb");
            e.Property(a => a.CreatedAt).HasColumnName("created_at");
            e.Property(a => a.IsActive).HasColumnName("is_active");
        });

        // ── SimilarityRules ──────────────────────────────────────────────────
        modelBuilder.Entity<SimilarityRule>(e =>
        {
            e.ToTable("similarity_rules");
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).HasColumnName("id");
            e.Property(s => s.ProductAId).HasColumnName("product_a_id");
            e.Property(s => s.ProductBId).HasColumnName("product_b_id");
            e.Property(s => s.Score).HasColumnName("score").HasColumnType("decimal(5,4)");
            e.Property(s => s.RuleType).HasColumnName("rule_type");
            e.Property(s => s.CreatedAt).HasColumnName("created_at");

            e.HasOne(s => s.ProductA)
             .WithMany()
             .HasForeignKey(s => s.ProductAId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(s => s.ProductB)
             .WithMany()
             .HasForeignKey(s => s.ProductBId)
             .OnDelete(DeleteBehavior.Cascade);
        });
    }
}