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
    // Known committed placeholder that must never become a live production credential.
    private const string DefaultSeedPassword = "ChangeMe123!";

    public static async Task InitializeAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<PlatformDbContext>();
        var config = services.GetRequiredService<IConfiguration>();
        var hasher = services.GetRequiredService<IPasswordHasher>();
        var env = services.GetRequiredService<IHostEnvironment>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("PlatformBootstrap");

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
            // PLAT-02: only seed a super-admin from an explicitly configured, non-default credential.
            // Never fall back to a committed default (that would ship a public super-admin login), and
            // outside Development refuse to seed a blank/known-default password — skip and warn instead,
            // so the first admin must be provisioned via a real PlatformSeed:Email/Password secret.
            var email = config["PlatformSeed:Email"];
            var password = config["PlatformSeed:Password"];
            var isDefaultOrEmpty = string.IsNullOrWhiteSpace(email)
                || string.IsNullOrWhiteSpace(password)
                || password == DefaultSeedPassword;

            if (isDefaultOrEmpty && !env.IsDevelopment())
            {
                logger.LogWarning(
                    "Skipping platform super-admin seed: PlatformSeed:Email/Password are unset or still the " +
                    "committed default. Set PlatformSeed__Email and a strong PlatformSeed__Password to provision " +
                    "the first admin.");
                return;
            }

            if (isDefaultOrEmpty)
            {
                // Development-only convenience so local login keeps working.
                email = string.IsNullOrWhiteSpace(email) ? "admin@vibedine.local" : email;
                password = string.IsNullOrWhiteSpace(password) ? DefaultSeedPassword : password;
            }

            db.PlatformAdmins.Add(new PlatformAdmin
            {
                Email = email!,
                FullName = "Platform Admin",
                PasswordHash = hasher.Hash(password!),
            });
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded initial platform super-admin {Email}.", email);
        }
    }
}
