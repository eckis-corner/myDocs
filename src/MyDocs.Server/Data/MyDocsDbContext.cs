using Microsoft.EntityFrameworkCore;

namespace MyDocs.Server.Data;

/// <summary>Datenbank von myDocs (PostgreSQL, eigener Container, 42). Tabellen und Spalten in snake_case wie bei myLife.</summary>
public sealed class MyDocsDbContext(DbContextOptions<MyDocsDbContext> options) : DbContext(options)
{
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SystemSetting>(entity =>
        {
            entity.ToTable("system_settings");
            entity.HasKey(x => x.Key);
            entity.Property(x => x.Key).HasColumnName("key").HasMaxLength(200);
            entity.Property(x => x.Value).HasColumnName("value");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });
    }
}
