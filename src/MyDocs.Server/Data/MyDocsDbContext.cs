using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

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

    public DbSet<DocObject> Objects => Set<DocObject>();

    public DbSet<ObjectShare> ObjectShares => Set<ObjectShare>();

    public DbSet<ObjectHistoryEntry> ObjectHistory => Set<ObjectHistoryEntry>();

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

        builder.Entity<DocObject>(entity =>
        {
            entity.ToTable("objects");
            entity.Property(x => x.TypeKey).HasMaxLength(100);
            entity.Property(x => x.Title).HasMaxLength(200);
            entity.Property(x => x.Fields).HasColumnType("jsonb").HasConversion(Json<Dictionary<string, string>>(), JsonComparer<Dictionary<string, string>>());
            entity.Property(x => x.Locations).HasColumnType("jsonb").HasConversion(Json<Dictionary<string, Guid>>(), JsonComparer<Dictionary<string, Guid>>());
            entity.Property(x => x.Version).IsRowVersion();
            entity.HasIndex(x => x.TypeKey);
            entity.HasIndex(x => x.OwnerPersonId);
            entity.HasOne<Person>().WithMany().HasForeignKey(x => x.OwnerPersonId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Person>().WithMany().HasForeignKey(x => x.ResponsiblePersonId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ObjectShare>(entity =>
        {
            entity.ToTable("object_shares");
            entity.HasKey(x => new { x.ObjectId, x.UserId });
            entity.HasOne<DocObject>().WithMany().HasForeignKey(x => x.ObjectId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ObjectHistoryEntry>(entity =>
        {
            entity.ToTable("object_history");
            entity.Property(x => x.Field).HasMaxLength(200);
            entity.HasIndex(x => new { x.ObjectId, x.At });
            entity.HasOne<DocObject>().WithMany().HasForeignKey(x => x.ObjectId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<SystemSetting>(entity =>
        {
            entity.ToTable("system_settings");
            entity.HasKey(x => x.Key);
            entity.Property(x => x.Key).HasMaxLength(200);
        });
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<T, string> Json<T>() where T : new() =>
        new(value => JsonSerializer.Serialize(value, JsonOptions), json => JsonSerializer.Deserialize<T>(json, JsonOptions) ?? new T());

    private static ValueComparer<T> JsonComparer<T>() => new(
        (a, b) => JsonSerializer.Serialize(a, JsonOptions) == JsonSerializer.Serialize(b, JsonOptions),
        value => JsonSerializer.Serialize(value, JsonOptions).GetHashCode(StringComparison.Ordinal),
        value => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions)!);
}
