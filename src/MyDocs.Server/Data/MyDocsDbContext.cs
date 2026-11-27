using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MyDocs.Server.Data;

/// <summary>
/// Datenbank von myDocs (PostgreSQL, eigener Container, 42). Namen in snake_case (EFCore.NamingConventions), die
/// Tabellen von ASP.NET Core Identity ohne Präfix „AspNet“.
/// </summary>
public sealed class MyDocsDbContext(DbContextOptions<MyDocsDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<AppUser>(entity =>
        {
            entity.ToTable("users");
            entity.Property(x => x.DisplayName).HasMaxLength(200);
        });
        builder.Entity<IdentityRole<Guid>>().ToTable("roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");
        // Passkeys (Schema-Version 3 von Identity, M1.2b)
        builder.Entity<IdentityUserPasskey<Guid>>().ToTable("user_passkeys");

        builder.Entity<SystemSetting>(entity =>
        {
            entity.ToTable("system_settings");
            entity.HasKey(x => x.Key);
            entity.Property(x => x.Key).HasMaxLength(200);
        });
    }
}
