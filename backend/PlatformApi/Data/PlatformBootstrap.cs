using Microsoft.EntityFrameworkCore;
using PlatformApi.Auth;
using PlatformApi.Models;

namespace PlatformApi.Data;

/// <summary>
/// Creates the HQ-owned tables (idempotently) and seeds a default platform admin.
/// Runs on startup. Existing main-schema tables are owned/created by the main backend.
/// </summary>
public static class PlatformBootstrap
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<PlatformDbContext>();
        var config = services.GetRequiredService<IConfiguration>();
        var hasher = services.GetRequiredService<IPasswordHasher>();

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS platform_admins (
                id            uuid PRIMARY KEY,
                email         text NOT NULL,
                full_name     text NOT NULL,
                password_hash text NOT NULL,
                is_active     boolean NOT NULL DEFAULT true,
                created_at    timestamptz NOT NULL DEFAULT now(),
                last_login_at timestamptz NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ix_platform_admins_email ON platform_admins (lower(email));

            CREATE TABLE IF NOT EXISTS platform_registration_links (
                id                           uuid PRIMARY KEY,
                token                        uuid NOT NULL,
                type                         integer NOT NULL,
                organization_id              uuid NULL,
                label                        text NULL,
                intended_email               text NULL,
                status                       text NOT NULL DEFAULT 'active',
                expires_at                   timestamptz NOT NULL,
                created_by_platform_admin_id uuid NOT NULL,
                created_at                   timestamptz NOT NULL DEFAULT now(),
                used_at                      timestamptz NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ix_platform_registration_links_token ON platform_registration_links (token);
            """);

        if (!await db.PlatformAdmins.AnyAsync())
        {
            var email = config["PlatformSeed:Email"] ?? "admin@vibedine.local";
            var password = config["PlatformSeed:Password"] ?? "ChangeMe123!";
            db.PlatformAdmins.Add(new PlatformAdmin
            {
                Email = email,
                FullName = "Platform Admin",
                PasswordHash = hasher.Hash(password),
            });
            await db.SaveChangesAsync();
        }
    }
}
