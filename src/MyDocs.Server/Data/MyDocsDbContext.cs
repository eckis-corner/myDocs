using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;

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

    public DbSet<Blob> Blobs => Set<Blob>();

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();

    public DbSet<DocumentObject> DocumentObjects => Set<DocumentObject>();

    public DbSet<AuditEntry> AuditLog => Set<AuditEntry>();

    public DbSet<WorkTask> Tasks => Set<WorkTask>();

    public DbSet<TaskObject> TaskObjects => Set<TaskObject>();

    public DbSet<Comment> Comments => Set<Comment>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasPostgresExtension("pg_trgm");
        builder.HasDbFunction(typeof(Infrastructure.Search.SearchFunctions).GetMethod(nameof(Infrastructure.Search.SearchFunctions.WordSimilarity))!)
            .HasName("word_similarity").IsBuiltIn();
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
            SearchColumns(entity);
            entity.Property(x => x.LifecycleState).HasMaxLength(30);
            entity.HasOne<DocObject>().WithMany().HasForeignKey(x => x.ReplacedById).OnDelete(DeleteBehavior.SetNull);
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

        builder.Entity<Blob>(entity =>
        {
            entity.ToTable("blobs");
            entity.HasKey(x => x.Hash);
            entity.Property(x => x.Hash).HasMaxLength(64);
        });

        builder.Entity<Document>(entity =>
        {
            entity.ToTable("documents");
            entity.Property(x => x.Title).HasMaxLength(200);
            entity.Property(x => x.Note).HasMaxLength(2000);
            SearchColumns(entity);
        });

        builder.Entity<DocumentVersion>(entity =>
        {
            entity.ToTable("document_versions");
            entity.Property(x => x.FileName).HasMaxLength(255);
            entity.Property(x => x.ContentType).HasMaxLength(200);
            entity.HasIndex(x => new { x.DocumentId, x.Number }).IsUnique();
            entity.HasOne<Document>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Blob>().WithMany().HasForeignKey(x => x.BlobHash).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<DocumentObject>(entity =>
        {
            entity.ToTable("document_objects");
            entity.HasKey(x => new { x.DocumentId, x.ObjectId });
            entity.HasIndex(x => x.ObjectId);
            entity.HasOne<Document>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<DocObject>().WithMany().HasForeignKey(x => x.ObjectId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<WorkTask>(entity =>
        {
            entity.ToTable("tasks");
            entity.Property(x => x.Kind).HasMaxLength(30);
            entity.Property(x => x.Title).HasMaxLength(200);
            entity.Property(x => x.TitleKey).HasMaxLength(100);
            entity.Property(x => x.RuleKey).HasMaxLength(200);
            entity.Property(x => x.Note).HasMaxLength(4000);
            entity.Property(x => x.Priority).HasMaxLength(20);
            entity.Property(x => x.State).HasMaxLength(20);
            entity.Property(x => x.Outcome).HasMaxLength(30);
            entity.Property(x => x.OutcomeNote).HasMaxLength(2000);
            entity.Property(x => x.Version).IsRowVersion();
            entity.HasIndex(x => new { x.State, x.DueOn });
            entity.HasIndex(x => x.RuleKey);
            entity.HasOne<Person>().WithMany().HasForeignKey(x => x.ResponsiblePersonId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<TaskObject>(entity =>
        {
            entity.ToTable("task_objects");
            entity.HasKey(x => new { x.TaskId, x.ObjectId });
            entity.HasIndex(x => x.ObjectId);
            entity.HasOne<WorkTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<DocObject>().WithMany().HasForeignKey(x => x.ObjectId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Comment>(entity =>
        {
            entity.ToTable("comments");
            entity.Property(x => x.Text).HasMaxLength(4000);
            entity.HasIndex(x => x.TaskId);
            entity.HasIndex(x => x.ObjectId);
            entity.HasOne<WorkTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<DocObject>().WithMany().HasForeignKey(x => x.ObjectId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AuditEntry>(entity =>
        {
            entity.ToTable("audit_log");
            entity.Property(x => x.Action).HasMaxLength(50);
            entity.Property(x => x.Title).HasMaxLength(200);
            entity.Property(x => x.Ip).HasMaxLength(50);
            entity.Property(x => x.Details).HasMaxLength(500);
            entity.HasIndex(x => x.At);
            entity.HasIndex(x => x.ActorId);
            entity.HasIndex(x => x.OwnerPersonId);
        });

        builder.Entity<SystemSetting>(entity =>
        {
            entity.ToTable("system_settings");
            entity.HasKey(x => x.Key);
            entity.Property(x => x.Key).HasMaxLength(200);
        });
    }

    /// <summary>Suchtext, Volltextindex (german) und Trigramm-Index (25).</summary>
    private static void SearchColumns<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> entity) where T : class
    {
        entity.Property<NpgsqlTypes.NpgsqlTsVector?>("SearchVector")
            .HasComputedColumnSql("to_tsvector('german', coalesce(search_text, ''))", stored: true);
        entity.HasIndex("SearchVector").HasMethod("gin");
        entity.HasIndex("SearchText").HasMethod("gin").HasOperators("gin_trgm_ops");
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<T, string> Json<T>() where T : new() =>
        new(value => JsonSerializer.Serialize(value, JsonOptions), json => JsonSerializer.Deserialize<T>(json, JsonOptions) ?? new T());

    private static ValueComparer<T> JsonComparer<T>() => new(
        (a, b) => JsonSerializer.Serialize(a, JsonOptions) == JsonSerializer.Serialize(b, JsonOptions),
        value => JsonSerializer.Serialize(value, JsonOptions).GetHashCode(StringComparison.Ordinal),
        value => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions)!);
}

/// <summary>
/// Kontext außerhalb der laufenden App, etwa für die Rücksicherung: mit denselben Einstellungen wie in Program.cs
/// (snake_case, Identity-Schema Version 3), sonst baut EF Core ein anderes Modell.
/// </summary>
public static class MyDocsDbContextFactory
{
    public static Microsoft.Extensions.DependencyInjection.ServiceProvider CreateServices(string connectionString)
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.LoggingServiceCollectionExtensions.AddLogging(services);
        Configure(services, connectionString);
        services.AddIdentityCore<AppUser>(identity => identity.Stores.SchemaVersion = IdentitySchemaVersions.Version3)
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<MyDocsDbContext>();
        return Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
    }

    /// <summary>Die Datenbank-Einstellungen, gemeinsam für App und Werkzeuge.</summary>
    public static void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services, string? connectionString) =>
        Microsoft.Extensions.DependencyInjection.EntityFrameworkServiceCollectionExtensions.AddDbContext<MyDocsDbContext>(services,
            db => db.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());
}
