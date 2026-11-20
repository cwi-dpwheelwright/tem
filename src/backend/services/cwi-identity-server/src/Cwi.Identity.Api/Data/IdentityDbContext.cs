using Microsoft.EntityFrameworkCore;

namespace Cwi.Identity.Api.Data;

public sealed class IdentityDbContext : DbContext
{
    public IdentityDbContext(DbContextOptions<IdentityDbContext> options)
        : base(options)
    {
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<UserSession> Sessions => Set<UserSession>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<SigningKey> SigningKeys => Set<SigningKey>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<InviteToken> InviteTokens => Set<InviteToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.ToTable("tenants");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TenantId).IsUnique();
            entity.HasIndex(x => x.Status);
        });

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TenantId);
            entity.HasIndex(x => x.Username);
            entity.HasIndex(x => x.Email);
            entity.HasIndex(x => new { x.TenantId, x.Email }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.Username })
                .IsUnique()
                .HasFilter("\"Username\" IS NOT NULL");
            entity.Property(x => x.DisplayName).HasMaxLength(200);
            entity.HasIndex(x => x.LockoutUntilUtc);
        });

        modelBuilder.Entity<Membership>(entity =>
        {
            entity.ToTable("memberships");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TenantId);
            entity.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
            entity.HasIndex(x => x.Status);
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.ToTable("roles");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.ToTable("user_roles");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TenantId);
            entity.HasIndex(x => new { x.TenantId, x.UserId, x.RoleId }).IsUnique();
        });

        modelBuilder.Entity<UserSession>(entity =>
        {
            entity.ToTable("sessions");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TenantId);
            entity.HasIndex(x => x.UserId);
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.ExpiresUtc);
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("refresh_tokens");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TenantId);
            entity.HasIndex(x => x.UserId);
            entity.HasIndex(x => x.SessionId);
            entity.HasIndex(x => x.ExpiresUtc);
            entity.HasIndex(x => x.TokenHash).IsUnique();
        });

        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.ToTable("audit_events");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TenantId);
            entity.HasIndex(x => x.UserId);
            entity.HasIndex(x => x.EventType);
            entity.HasIndex(x => x.OccurredUtc);
            entity.Property(x => x.PayloadJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<SigningKey>(entity =>
        {
            entity.ToTable("signing_keys");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.KeyId).IsUnique();
            entity.HasIndex(x => x.Status);
            entity.Property(x => x.PublicJwkJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<PasswordResetToken>(entity =>
        {
            entity.ToTable("password_reset_tokens");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TenantId);
            entity.HasIndex(x => x.UserId);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => x.ExpiresUtc);
            entity.HasIndex(x => x.UsedUtc);
        });

        modelBuilder.Entity<InviteToken>(entity =>
        {
            entity.ToTable("invite_tokens");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TenantId);
            entity.HasIndex(x => x.UserId);
            entity.HasIndex(x => x.Email);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => x.ExpiresUtc);
            entity.HasIndex(x => x.ApprovedUtc);
            entity.HasIndex(x => x.UsedUtc);
        });
    }
}

public sealed class Tenant
{
    public Guid Id { get; init; }
    public string TenantId { get; init; } = "";
    public string Name { get; set; } = "";
    public string Status { get; set; } = "active";
    public DateTime CreatedUtc { get; init; }
    public DateTime UpdatedUtc { get; set; }
}

public sealed class AppUser
{
    public Guid Id { get; init; }
    public string TenantId { get; set; } = "";
    public string? Username { get; set; }
    public string Email { get; init; } = "";
    public string DisplayName { get; set; } = "";
    public string Status { get; set; } = "active";
    public int TokenVersion { get; set; }
    public string PasswordHash { get; set; } = "";
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutUntilUtc { get; set; }
    public DateTime CreatedUtc { get; init; }
    public DateTime UpdatedUtc { get; set; }
}

public sealed class Membership
{
    public Guid Id { get; init; }
    public string TenantId { get; set; } = "";
    public Guid UserId { get; init; }
    public string Status { get; set; } = "active";
    public DateTime CreatedUtc { get; init; }
    public DateTime UpdatedUtc { get; set; }
}

public sealed class Role
{
    public Guid Id { get; init; }
    public string TenantId { get; init; } = "";
    public string Name { get; init; } = "";
    public DateTime CreatedUtc { get; init; }
}

public sealed class UserRole
{
    public Guid Id { get; init; }
    public string TenantId { get; set; } = "";
    public Guid UserId { get; init; }
    public Guid RoleId { get; init; }
    public DateTime CreatedUtc { get; init; }
}

public sealed class UserSession
{
    public Guid Id { get; init; }
    public string TenantId { get; init; } = "";
    public Guid UserId { get; init; }
    public string Status { get; set; } = "active";
    public DateTime CreatedUtc { get; init; }
    public DateTime ExpiresUtc { get; init; }
}

public sealed class RefreshToken
{
    public Guid Id { get; init; }
    public string TenantId { get; init; } = "";
    public Guid UserId { get; init; }
    public Guid SessionId { get; init; }
    public string TokenHash { get; init; } = "";
    public DateTime IssuedUtc { get; init; }
    public DateTime ExpiresUtc { get; init; }
    public DateTime? RevokedUtc { get; set; }
}

public sealed class AuditEvent
{
    public Guid Id { get; init; }
    public string TenantId { get; init; } = "";
    public Guid? UserId { get; init; }
    public string EventType { get; init; } = "";
    public string PayloadJson { get; init; } = "{}";
    public DateTime OccurredUtc { get; init; }
}

public sealed class SigningKey
{
    public Guid Id { get; init; }
    public string KeyId { get; init; } = "";
    public string Status { get; set; } = "active";
    public byte[] EncryptedPrivateKey { get; init; } = Array.Empty<byte>();
    public byte[] Nonce { get; init; } = Array.Empty<byte>();
    public string PublicJwkJson { get; init; } = "{}";
    public DateTime CreatedUtc { get; init; }
}

public sealed class PasswordResetToken
{
    public Guid Id { get; init; }
    public string TenantId { get; init; } = "";
    public Guid UserId { get; init; }
    public string TokenHash { get; init; } = "";
    public DateTime IssuedUtc { get; init; }
    public DateTime ExpiresUtc { get; init; }
    public DateTime? UsedUtc { get; set; }
}

public sealed class InviteToken
{
    public Guid Id { get; init; }
    public string TenantId { get; init; } = "";
    public Guid UserId { get; init; }
    public string Email { get; init; } = "";
    public string TokenHash { get; init; } = "";
    public DateTime IssuedUtc { get; init; }
    public DateTime ExpiresUtc { get; init; }
    public DateTime? ApprovedUtc { get; set; }
    public DateTime? UsedUtc { get; set; }
}
