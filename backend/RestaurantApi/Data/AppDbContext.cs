using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RestaurantApi.Models;
using RestaurantApi.Tenancy;

namespace RestaurantApi.Data;

public class AppDbContext : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>
{
    private readonly ICurrentTenant _tenant;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenant tenant)
        : base(options)
    {
        _tenant = tenant;
    }

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Subcategory> Subcategories => Set<Subcategory>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<RestaurantTable> Tables => Set<RestaurantTable>();
    public DbSet<FlashDealOffer> FlashDealOffers => Set<FlashDealOffer>();
    public DbSet<HappyHourDeal> HappyHourDeals => Set<HappyHourDeal>();
    public DbSet<FlashDealRule> FlashDealRules => Set<FlashDealRule>();
    public DbSet<RecommendationDirective> RecommendationDirectives => Set<RecommendationDirective>();
    public DbSet<SimilarityRule> SimilarityRules => Set<SimilarityRule>();
    public DbSet<HelpRequest> HelpRequests => Set<HelpRequest>();
    public DbSet<TableGuest> TableGuests => Set<TableGuest>();
    public DbSet<MenuImportJob> MenuImportJobs => Set<MenuImportJob>();

    // ── Tenancy ──────────────────────────────────────────────────────────────
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Restaurant> Restaurants => Set<Restaurant>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<WaiterSignupLink> WaiterSignupLinks => Set<WaiterSignupLink>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Category>(e =>
        {
            e.ToTable("categories");
            e.Property(c => c.Id).HasColumnName("id");
            e.Property(c => c.RestaurantId).HasColumnName("restaurant_id");
            e.Property(c => c.Name).HasColumnName("name");
            e.Property(c => c.SortOrder).HasColumnName("sort_order");
            e.Property(c => c.ImageUrl).HasColumnName("image_url");
            e.HasIndex(c => c.RestaurantId);
        });

        modelBuilder.Entity<Subcategory>(e =>
        {
            e.ToTable("subcategories");
            e.Property(s => s.Id).HasColumnName("id");
            e.Property(s => s.RestaurantId).HasColumnName("restaurant_id");
            e.Property(s => s.CategoryId).HasColumnName("category_id");
            e.Property(s => s.Name).HasColumnName("name");
            e.Property(s => s.SortOrder).HasColumnName("sort_order");
            e.Property(s => s.ImageUrl).HasColumnName("image_url");
            e.HasIndex(s => s.RestaurantId);
            e.HasIndex(s => new { s.RestaurantId, s.CategoryId, s.Name }).IsUnique();

            e.HasOne(s => s.Category)
             .WithMany()
             .HasForeignKey(s => s.CategoryId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Product>(e =>
        {
            e.ToTable("products");
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.RestaurantId).HasColumnName("restaurant_id");
            e.Property(p => p.CategoryId).HasColumnName("category_id");
            e.Property(p => p.SubcategoryId).HasColumnName("subcategory_id");
            e.Property(p => p.Name).HasColumnName("name");
            e.Property(p => p.Description).HasColumnName("description");
            e.Property(p => p.Price).HasColumnName("price");
            e.Property(p => p.ImageUrl).HasColumnName("image_url");
            e.Property(p => p.IsAvailable).HasColumnName("is_available");
            e.Property(p => p.SortOrder).HasColumnName("sort_order");
            e.HasIndex(p => p.RestaurantId);

            e.HasOne(p => p.Category)
             .WithMany(c => c.Products)
             .HasForeignKey(p => p.CategoryId);

            e.HasOne(p => p.Subcategory)
             .WithMany(s => s.Products)
             .HasForeignKey(p => p.SubcategoryId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<RestaurantTable>(e =>
        {
            e.ToTable("tables");
            e.Property(t => t.Id).HasColumnName("id");
            e.Property(t => t.RestaurantId).HasColumnName("restaurant_id");
            e.Property(t => t.TableNumber).HasColumnName("table_number");
            e.Property(t => t.QrToken).HasColumnName("qr_token");
            e.Property(t => t.DeletedAt).HasColumnName("deleted_at");
            // qr_token is the customer's globally-unique entry point; it must resolve a
            // single table across the whole platform.
            e.HasIndex(t => t.QrToken).IsUnique();
            e.HasIndex(t => t.RestaurantId);
        });

        modelBuilder.Entity<Order>(e =>
        {
            e.ToTable("orders");
            e.Property(o => o.Id).HasColumnName("id");
            e.Property(o => o.RestaurantId).HasColumnName("restaurant_id");
            e.Property(o => o.TableId).HasColumnName("table_id");
            e.Property(o => o.Status).HasColumnName("status");
            e.Property(o => o.PaymentStatus).HasColumnName("payment_status");
            e.Property(o => o.TotalAmount).HasColumnName("total_amount");
            e.Property(o => o.CreatedAt).HasColumnName("created_at");
            e.Property(o => o.OrderNumber).HasColumnName("order_number");
            e.Property(o => o.ReadyAt).HasColumnName("ready_at");
            e.Property(o => o.DeletedAt).HasColumnName("deleted_at");
            e.Property(o => o.GuestId).HasColumnName("guest_id");
            e.Property(o => o.MergedIntoOrderId).HasColumnName("merged_into_order_id");
            e.HasIndex(o => o.RestaurantId);
            e.HasIndex(o => new { o.RestaurantId, o.OrderNumber });
            // Fast "is there a draft for this guest at this table" lookup.
            e.HasIndex(o => new { o.TableId, o.GuestId, o.Status });

            e.HasOne(o => o.Table)
             .WithMany(t => t.Orders)
             .HasForeignKey(o => o.TableId);
        });

        modelBuilder.Entity<OrderItem>(e =>
        {
            e.ToTable("order_items");
            e.Property(oi => oi.Id).HasColumnName("id");
            e.Property(oi => oi.RestaurantId).HasColumnName("restaurant_id");
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
            e.Property(f => f.RestaurantId).HasColumnName("restaurant_id");
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

        modelBuilder.Entity<HappyHourDeal>(e =>
        {
            e.ToTable("happy_hour_deals");
            e.Property(h => h.Id).HasColumnName("id");
            e.Property(h => h.RestaurantId).HasColumnName("restaurant_id");
            e.Property(h => h.ProductId).HasColumnName("product_id");
            e.Property(h => h.DiscountPercent).HasColumnName("discount_percent");
            e.Property(h => h.StartTime).HasColumnName("start_time");
            e.Property(h => h.EndTime).HasColumnName("end_time");
            e.Property(h => h.IsEnabled).HasColumnName("is_enabled");
            e.Property(h => h.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(h => h.RestaurantId);

            e.HasOne(h => h.Product)
             .WithMany()
             .HasForeignKey(h => h.ProductId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FlashDealRule>(e =>
        {
            e.ToTable("flash_deal_rules");
            e.Property(f => f.Id).HasColumnName("id");
            e.Property(f => f.RestaurantId).HasColumnName("restaurant_id");
            e.Property(f => f.ProductId).HasColumnName("product_id");
            e.Property(f => f.DiscountPercent).HasColumnName("discount_percent");
            e.Property(f => f.MinOrderTotal).HasColumnName("min_order_total");
            e.Property(f => f.IsEnabled).HasColumnName("is_enabled");
            e.Property(f => f.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(f => f.RestaurantId);

            e.HasOne(f => f.Product)
             .WithMany()
             .HasForeignKey(f => f.ProductId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RecommendationDirective>(e =>
        {
            e.ToTable("recommendation_directives");
            e.Property(a => a.Id).HasColumnName("id");
            e.Property(a => a.RestaurantId).HasColumnName("restaurant_id");
            e.Property(a => a.RawPrompt).HasColumnName("raw_prompt");
            e.Property(a => a.ParsedRules).HasColumnName("parsed_rules");
            e.Property(a => a.CreatedAt).HasColumnName("created_at");
            e.Property(a => a.IsActive).HasColumnName("is_active");
            e.HasIndex(a => a.RestaurantId);
        });

        modelBuilder.Entity<SimilarityRule>(e =>
        {
            e.ToTable("similarity_rules");
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).HasColumnName("id");
            e.Property(r => r.RestaurantId).HasColumnName("restaurant_id");
            e.Property(r => r.ProductAId).HasColumnName("product_a_id");
            e.Property(r => r.ProductBId).HasColumnName("product_b_id");
            e.Property(r => r.Score).HasColumnName("score").HasColumnType("decimal(5,4)");
            e.Property(r => r.RuleType).HasColumnName("rule_type");
            e.Property(r => r.CreatedAt).HasColumnName("created_at");
            e.HasIndex(r => r.RestaurantId);

            e.HasOne(r => r.ProductA)
             .WithMany()
             .HasForeignKey(r => r.ProductAId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(r => r.ProductB)
             .WithMany()
             .HasForeignKey(r => r.ProductBId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<HelpRequest>(e =>
        {
            e.ToTable("help_requests");
            e.Property(h => h.Id).HasColumnName("id");
            e.Property(h => h.RestaurantId).HasColumnName("restaurant_id");
            e.Property(h => h.TableId).HasColumnName("table_id");
            e.Property(h => h.CreatedAt).HasColumnName("created_at");
            e.Property(h => h.Status).HasColumnName("status");
            e.Property(h => h.AcknowledgedAt).HasColumnName("acknowledged_at");
            e.Property(h => h.AcknowledgedByUserId).HasColumnName("acknowledged_by_user_id");
            e.HasIndex(h => new { h.TableId, h.CreatedAt });

            e.HasOne(h => h.Table)
             .WithMany()
             .HasForeignKey(h => h.TableId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TableGuest>(e =>
        {
            e.ToTable("table_guests");
            e.Property(g => g.Id).HasColumnName("id");
            e.Property(g => g.RestaurantId).HasColumnName("restaurant_id");
            e.Property(g => g.TableId).HasColumnName("table_id");
            e.Property(g => g.DisplayName).HasColumnName("display_name");
            e.Property(g => g.CreatedAt).HasColumnName("created_at");
            e.Property(g => g.LastSeenAt).HasColumnName("last_seen_at");
            e.HasIndex(g => g.RestaurantId);
            e.HasIndex(g => g.TableId);

            e.HasOne(g => g.Table)
             .WithMany()
             .HasForeignKey(g => g.TableId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MenuImportJob>(e =>
        {
            e.ToTable("menu_import_jobs");
            e.Property(j => j.Id).HasColumnName("id");
            e.Property(j => j.RestaurantId).HasColumnName("restaurant_id");
            e.Property(j => j.Status).HasColumnName("status");
            e.Property(j => j.Phase).HasColumnName("phase");
            e.Property(j => j.TotalPages).HasColumnName("total_pages");
            e.Property(j => j.ProcessedPages).HasColumnName("processed_pages");
            e.Property(j => j.FailedPages).HasColumnName("failed_pages");
            e.Property(j => j.StorageKey).HasColumnName("storage_key");
            e.Property(j => j.OriginalFileName).HasColumnName("original_file_name");
            e.Property(j => j.FileSizeBytes).HasColumnName("file_size_bytes");
            e.Property(j => j.ExtractItemPhotos).HasColumnName("extract_item_photos").HasDefaultValue(false);
            e.Property(j => j.ProviderUsed).HasColumnName("provider_used");
            e.Property(j => j.ResultJson).HasColumnName("result_json").HasColumnType("jsonb");
            e.Property(j => j.ErrorMessage).HasColumnName("error_message");
            e.Property(j => j.CreatedAt).HasColumnName("created_at");
            e.Property(j => j.UpdatedAt).HasColumnName("updated_at");
            e.Property(j => j.CompletedAt).HasColumnName("completed_at");
            e.Property(j => j.LeaseId).HasColumnName("lease_id");
            e.Property(j => j.LeaseExpiresAt).HasColumnName("lease_expires_at");
            e.Property(j => j.LastHeartbeatAt).HasColumnName("last_heartbeat_at");
            e.Property(j => j.ProcessingAttempts).HasColumnName("processing_attempts");
            e.HasIndex(j => j.RestaurantId);
            e.HasIndex(j => new { j.Status, j.CreatedAt });
            e.HasIndex(j => new { j.Status, j.LeaseExpiresAt });
        });

        // ── Tenancy tables ───────────────────────────────────────────────────
        modelBuilder.Entity<Organization>(e =>
        {
            e.ToTable("organizations");
            e.Property(o => o.Id).HasColumnName("id");
            e.Property(o => o.Name).HasColumnName("name");
            e.Property(o => o.Slug).HasColumnName("slug");
            e.Property(o => o.CreatedByUserId).HasColumnName("created_by_user_id");
            e.Property(o => o.CreatedAt).HasColumnName("created_at");
            e.HasIndex(o => o.Slug).IsUnique();
        });

        modelBuilder.Entity<Restaurant>(e =>
        {
            e.ToTable("restaurants");
            e.Property(r => r.Id).HasColumnName("id");
            e.Property(r => r.OrganizationId).HasColumnName("organization_id");
            e.Property(r => r.Name).HasColumnName("name");
            e.Property(r => r.Slug).HasColumnName("slug");
            e.Property(r => r.Address).HasColumnName("address");
            e.Property(r => r.Timezone).HasColumnName("timezone");
            e.Property(r => r.Currency).HasColumnName("currency");
            e.Property(r => r.Locale).HasColumnName("locale").HasDefaultValue("ro-RO");
            e.Property(r => r.IsActive).HasColumnName("is_active");
            e.Property(r => r.CreatedAt).HasColumnName("created_at");
            e.HasIndex(r => r.OrganizationId);

            e.HasOne(r => r.Organization)
             .WithMany(o => o.Restaurants)
             .HasForeignKey(r => r.OrganizationId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Membership>(e =>
        {
            e.ToTable("memberships");
            e.Property(m => m.Id).HasColumnName("id");
            e.Property(m => m.UserId).HasColumnName("user_id");
            e.Property(m => m.OrganizationId).HasColumnName("organization_id");
            e.Property(m => m.RestaurantId).HasColumnName("restaurant_id");
            e.Property(m => m.Role).HasColumnName("role").HasConversion<int>();
            e.Property(m => m.Status).HasColumnName("status").HasDefaultValue("active");
            e.Property(m => m.InvitedByUserId).HasColumnName("invited_by_user_id");
            e.Property(m => m.CreatedAt).HasColumnName("created_at");
            e.HasIndex(m => m.UserId);
            e.HasIndex(m => new { m.UserId, m.OrganizationId, m.RestaurantId }).IsUnique();

            e.HasOne(m => m.User)
             .WithMany(u => u.Memberships)
             .HasForeignKey(m => m.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(m => m.Organization)
             .WithMany()
             .HasForeignKey(m => m.OrganizationId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(m => m.Restaurant)
             .WithMany()
             .HasForeignKey(m => m.RestaurantId)
             .OnDelete(DeleteBehavior.Cascade)
             .IsRequired(false);
        });

        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.ToTable("refresh_tokens");
            e.Property(t => t.Id).HasColumnName("id");
            e.Property(t => t.UserId).HasColumnName("user_id");
            e.Property(t => t.Token).HasColumnName("token");
            e.Property(t => t.ExpiresAt).HasColumnName("expires_at");
            e.Property(t => t.CreatedAt).HasColumnName("created_at");
            e.Property(t => t.RevokedAt).HasColumnName("revoked_at");
            e.Ignore(t => t.IsActive);
            e.HasIndex(t => t.Token).IsUnique();

            e.HasOne(t => t.User)
             .WithMany()
             .HasForeignKey(t => t.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Invitation>(e =>
        {
            e.ToTable("invitations");
            e.Property(i => i.Id).HasColumnName("id");
            e.Property(i => i.Email).HasColumnName("email");
            e.Property(i => i.OrganizationId).HasColumnName("organization_id");
            e.Property(i => i.RestaurantId).HasColumnName("restaurant_id");
            e.Property(i => i.Role).HasColumnName("role").HasConversion<int>();
            e.Property(i => i.Token).HasColumnName("token");
            e.Property(i => i.Status).HasColumnName("status");
            e.Property(i => i.ExpiresAt).HasColumnName("expires_at");
            e.Property(i => i.InvitedByUserId).HasColumnName("invited_by_user_id");
            e.Property(i => i.CreatedAt).HasColumnName("created_at");
            e.HasIndex(i => i.Token).IsUnique();
        });

        modelBuilder.Entity<WaiterSignupLink>(e =>
        {
            e.ToTable("waiter_signup_links");
            e.Property(l => l.Id).HasColumnName("id");
            e.Property(l => l.OrganizationId).HasColumnName("organization_id");
            e.Property(l => l.RestaurantId).HasColumnName("restaurant_id");
            e.Property(l => l.Token).HasColumnName("token");
            e.Property(l => l.IsActive).HasColumnName("is_active");
            e.Property(l => l.CreatedByUserId).HasColumnName("created_by_user_id");
            e.Property(l => l.CreatedAt).HasColumnName("created_at");
            e.HasIndex(l => l.Token).IsUnique();
            e.HasIndex(l => l.RestaurantId);

            e.HasOne(l => l.Restaurant)
             .WithMany()
             .HasForeignKey(l => l.RestaurantId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(l => l.Organization)
             .WithMany()
             .HasForeignKey(l => l.OrganizationId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // AppUser column naming: pre-existing columns (FullName, CreatedAt) were never
        // snake-cased in the initial migration, so we leave them as-is. Only the new
        // AvatarPath column is added here.
        modelBuilder.Entity<AppUser>(e =>
        {
            e.Property(u => u.AvatarPath).HasColumnName("avatar_path");
        });

        // ── Tenant isolation: global query filters ───────────────────────────
        // Every tenant-scoped read is automatically constrained to the active
        // restaurant. When no tenant is in scope, RestaurantId is null and the
        // comparison yields no rows (fail-closed). SuppressResolution() bypasses
        // it for deliberate cross-tenant work (seeding, org-owner listings).
        modelBuilder.Entity<Category>().HasQueryFilter(x => _tenant.IsResolutionSuppressed || x.RestaurantId == _tenant.RestaurantId);
        modelBuilder.Entity<Subcategory>().HasQueryFilter(x => _tenant.IsResolutionSuppressed || x.RestaurantId == _tenant.RestaurantId);
        modelBuilder.Entity<Product>().HasQueryFilter(x => _tenant.IsResolutionSuppressed || x.RestaurantId == _tenant.RestaurantId);
        modelBuilder.Entity<RestaurantTable>().HasQueryFilter(x => _tenant.IsResolutionSuppressed || x.RestaurantId == _tenant.RestaurantId);
        modelBuilder.Entity<Order>().HasQueryFilter(x => _tenant.IsResolutionSuppressed || x.RestaurantId == _tenant.RestaurantId);
        modelBuilder.Entity<OrderItem>().HasQueryFilter(x => _tenant.IsResolutionSuppressed || x.RestaurantId == _tenant.RestaurantId);
        modelBuilder.Entity<FlashDealOffer>().HasQueryFilter(x => _tenant.IsResolutionSuppressed || x.RestaurantId == _tenant.RestaurantId);
        modelBuilder.Entity<HappyHourDeal>().HasQueryFilter(x => _tenant.IsResolutionSuppressed || x.RestaurantId == _tenant.RestaurantId);
        modelBuilder.Entity<FlashDealRule>().HasQueryFilter(x => _tenant.IsResolutionSuppressed || x.RestaurantId == _tenant.RestaurantId);
        modelBuilder.Entity<RecommendationDirective>().HasQueryFilter(x => _tenant.IsResolutionSuppressed || x.RestaurantId == _tenant.RestaurantId);
        modelBuilder.Entity<SimilarityRule>().HasQueryFilter(x => _tenant.IsResolutionSuppressed || x.RestaurantId == _tenant.RestaurantId);
        modelBuilder.Entity<HelpRequest>().HasQueryFilter(x => _tenant.IsResolutionSuppressed || x.RestaurantId == _tenant.RestaurantId);
        modelBuilder.Entity<TableGuest>().HasQueryFilter(x => _tenant.IsResolutionSuppressed || x.RestaurantId == _tenant.RestaurantId);
        modelBuilder.Entity<MenuImportJob>().HasQueryFilter(x => _tenant.IsResolutionSuppressed || x.RestaurantId == _tenant.RestaurantId);
    }

    /// <summary>Stamps RestaurantId on new tenant-scoped entities from the ambient tenant.</summary>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampTenant();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampTenant();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void StampTenant()
    {
        if (_tenant.RestaurantId is not { } rid) return;

        foreach (var entry in ChangeTracker.Entries<ITenantScoped>())
        {
            if (entry.State == EntityState.Added && entry.Entity.RestaurantId == Guid.Empty)
                entry.Entity.RestaurantId = rid;
        }
    }
}
