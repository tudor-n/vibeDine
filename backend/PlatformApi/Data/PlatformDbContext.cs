using Microsoft.EntityFrameworkCore;
using PlatformApi.Models;

namespace PlatformApi.Data;

/// <summary>
/// EF context for the platform console. It maps the existing main-schema tables
/// (organizations, restaurants, memberships, AspNetUsers) for read/write and adds two
/// HQ-owned tables. This context never runs EF migrations — the main backend owns the
/// shared schema, and HQ-owned tables are bootstrapped via idempotent SQL on startup.
/// </summary>
public class PlatformDbContext : DbContext
{
    public PlatformDbContext(DbContextOptions<PlatformDbContext> options) : base(options) { }

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Restaurant> Restaurants => Set<Restaurant>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<PlatformAdmin> PlatformAdmins => Set<PlatformAdmin>();
    public DbSet<RegistrationLink> RegistrationLinks => Set<RegistrationLink>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // ── Existing main-schema tables (read/write, NOT migrated here) ───────────
        b.Entity<Organization>(e =>
        {
            e.ToTable("organizations");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.Slug).HasColumnName("slug");
            e.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        b.Entity<Restaurant>(e =>
        {
            e.ToTable("restaurants");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.OrganizationId).HasColumnName("organization_id");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.Slug).HasColumnName("slug");
            e.Property(x => x.Address).HasColumnName("address");
            e.Property(x => x.Timezone).HasColumnName("timezone");
            e.Property(x => x.Currency).HasColumnName("currency");
            e.Property(x => x.Locale).HasColumnName("locale");
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasOne(x => x.Organization)
             .WithMany(o => o.Restaurants)
             .HasForeignKey(x => x.OrganizationId);
        });

        b.Entity<Membership>(e =>
        {
            e.ToTable("memberships");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.OrganizationId).HasColumnName("organization_id");
            e.Property(x => x.RestaurantId).HasColumnName("restaurant_id");
            e.Property(x => x.Role).HasColumnName("role").HasConversion<int>();
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.InvitedByUserId).HasColumnName("invited_by_user_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasOne(x => x.User)
             .WithMany(u => u.Memberships)
             .HasForeignKey(x => x.UserId);
            e.HasOne(x => x.Organization)
             .WithMany(o => o.Memberships)
             .HasForeignKey(x => x.OrganizationId);
            e.HasOne(x => x.Restaurant)
             .WithMany(r => r.Memberships)
             .HasForeignKey(x => x.RestaurantId)
             .IsRequired(false);
        });

        // AspNetUsers keeps Identity's PascalCase columns; only a subset is mapped.
        b.Entity<AppUser>(e =>
        {
            e.ToTable("AspNetUsers");
            e.Property(x => x.Id).HasColumnName("Id");
            e.Property(x => x.Email).HasColumnName("Email");
            e.Property(x => x.FullName).HasColumnName("FullName");
            e.Property(x => x.EmailConfirmed).HasColumnName("EmailConfirmed");
            e.Property(x => x.CreatedAt).HasColumnName("CreatedAt");
        });

        // ── HQ-owned tables (created via PlatformBootstrap) ───────────────────────
        b.Entity<PlatformAdmin>(e =>
        {
            e.ToTable("platform_admins");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Email).HasColumnName("email");
            e.Property(x => x.FullName).HasColumnName("full_name");
            e.Property(x => x.PasswordHash).HasColumnName("password_hash");
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.LastLoginAt).HasColumnName("last_login_at");
        });

        b.Entity<RegistrationLink>(e =>
        {
            e.ToTable("platform_registration_links");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Token).HasColumnName("token");
            e.Property(x => x.Type).HasColumnName("type").HasConversion<int>();
            e.Property(x => x.OrganizationId).HasColumnName("organization_id");
            e.Property(x => x.Label).HasColumnName("label");
            e.Property(x => x.IntendedEmail).HasColumnName("intended_email");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            e.Property(x => x.CreatedByPlatformAdminId).HasColumnName("created_by_platform_admin_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UsedAt).HasColumnName("used_at");
        });
    }
}
