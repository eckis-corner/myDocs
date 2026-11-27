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

    public DbSet<Person> Persons => Set<Person>();

    public DbSet<OneTimeToken> OneTimeTokens => Set<OneTimeToken>();

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

        builder.Entity<Person>(entity =>
        {
            entity.ToTable("persons");
            entity.Property(x => x.DisplayName).HasMaxLength(200);
            entity.Property(x => x.Note).HasMaxLength(2000);
            entity.HasIndex(x => x.UserId).IsUnique().HasFilter("user_id IS NOT NULL");
            entity.HasOne<AppUser>().WithOne().HasForeignKey<Person>(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        builder.Entity<OneTimeToken>(entity =>
        {
            entity.ToTable("one_time_tokens");
            entity.Property(x => x.Purpose).HasMaxLength(50);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<SystemSetting>(entity =>
        {
            entity.ToTable("system_settings");
            entity.HasKey(x => x.Key);
            entity.Property(x => x.Key).HasMaxLength(200);
        });
    }
}
