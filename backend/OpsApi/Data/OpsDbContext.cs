using Microsoft.EntityFrameworkCore;
using OpsApi.Models;

namespace OpsApi.Data;

/// <summary>
/// EF context for the ops console. This context NEVER runs EF migrations — it owns only the
/// `ops_*` tables, which are bootstrapped via idempotent SQL on startup (see OpsBootstrap).
/// The main backend owns the shared `restaurant` schema; we do not touch it.
/// </summary>
public sealed class OpsDbContext : DbContext
{
    public OpsDbContext(DbContextOptions<OpsDbContext> options) : base(options) { }

    public DbSet<OpsAdmin> Admins => Set<OpsAdmin>();
    public DbSet<Connection> Connections => Set<Connection>();
    public DbSet<AuditEntry> AuditLog => Set<AuditEntry>();
    public DbSet<LogArchiveEntry> LogArchive => Set<LogArchiveEntry>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<OpsAdmin>(e =>
        {
            e.ToTable("ops_admins");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Email).HasColumnName("email");
            e.Property(x => x.FullName).HasColumnName("full_name");
            e.Property(x => x.PasswordHash).HasColumnName("password_hash");
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.LastLoginAt).HasColumnName("last_login_at");
        });

        b.Entity<Connection>(e =>
        {
            e.ToTable("ops_connections");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Provider).HasColumnName("provider");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.Role).HasColumnName("role");
            e.Property(x => x.TokenCipher).HasColumnName("token_cipher");
            e.Property(x => x.TokenKind).HasColumnName("token_kind");
            e.Property(x => x.AccountLabel).HasColumnName("account_label");
            e.Property(x => x.MetadataJson).HasColumnName("metadata_json").HasColumnType("jsonb");
            e.Property(x => x.CapabilitiesJson).HasColumnName("capabilities_json").HasColumnType("jsonb");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.StatusDetail).HasColumnName("status_detail");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.LastCheckedAt).HasColumnName("last_checked_at");
            e.Property(x => x.CreatedByAdminId).HasColumnName("created_by_admin_id");
        });

        b.Entity<AuditEntry>(e =>
        {
            e.ToTable("ops_audit_log");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ActorAdminId).HasColumnName("actor_admin_id");
            e.Property(x => x.ActorEmail).HasColumnName("actor_email");
            e.Property(x => x.Action).HasColumnName("action");
            e.Property(x => x.Provider).HasColumnName("provider");
            e.Property(x => x.ConnectionId).HasColumnName("connection_id");
            e.Property(x => x.TargetType).HasColumnName("target_type");
            e.Property(x => x.TargetId).HasColumnName("target_id");
            e.Property(x => x.Summary).HasColumnName("summary");
            e.Property(x => x.DetailJson).HasColumnName("detail_json").HasColumnType("jsonb");
            e.Property(x => x.Result).HasColumnName("result");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        b.Entity<LogArchiveEntry>(e =>
        {
            e.ToTable("ops_log_archive");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.ConnectionId).HasColumnName("connection_id");
            e.Property(x => x.Provider).HasColumnName("provider");
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.ServiceId).HasColumnName("service_id");
            e.Property(x => x.EnvironmentId).HasColumnName("environment_id");
            e.Property(x => x.DeploymentId).HasColumnName("deployment_id");
            e.Property(x => x.LogType).HasColumnName("log_type");
            e.Property(x => x.Severity).HasColumnName("severity");
            e.Property(x => x.Message).HasColumnName("message");
            e.Property(x => x.AttributesJson).HasColumnName("attributes_json").HasColumnType("jsonb");
            e.Property(x => x.Timestamp).HasColumnName("timestamp");
            e.Property(x => x.IngestedAt).HasColumnName("ingested_at");
        });
    }
}
