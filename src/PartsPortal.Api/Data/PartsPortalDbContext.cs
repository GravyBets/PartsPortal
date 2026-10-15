using Microsoft.EntityFrameworkCore;
using PartsPortal.Api.Data.Entities;

namespace PartsPortal.Api.Data;

public sealed class PartsPortalDbContext : DbContext
{
    public PartsPortalDbContext(DbContextOptions<PartsPortalDbContext> options)
        : base(options)
    {
    }

    public DbSet<CatalogPart> Parts => Set<CatalogPart>();

    public DbSet<Warehouse> Warehouses => Set<Warehouse>();

    public DbSet<PartWarehouse> PartWarehouses => Set<PartWarehouse>();

    public DbSet<CatalogTemplate> Templates => Set<CatalogTemplate>();

    public DbSet<CatalogTemplateLine> TemplateLines => Set<CatalogTemplateLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<CatalogPart>(entity =>
        {
            entity.ToTable("parts");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id)
                .HasColumnName("id");

            entity.Property(x => x.MaterialNumber)
                .HasColumnName("material_number")
                .HasMaxLength(64)
                .IsRequired();

            entity.Property(x => x.Description)
                .HasColumnName("description")
                .HasMaxLength(255)
                .IsRequired();

            entity.Property(x => x.IsActive)
                .HasColumnName("is_active")
                .IsRequired();

            entity.Property(x => x.CreatedAtUtc)
                .HasColumnName("created_at_utc")
                .HasColumnType("datetime(6)")
                .IsRequired();

            entity.Property(x => x.UpdatedAtUtc)
                .HasColumnName("updated_at_utc")
                .HasColumnType("datetime(6)")
                .IsRequired();

            entity.Property(x => x.UpdatedBy)
                .HasColumnName("updated_by")
                .HasMaxLength(128);

            entity.HasIndex(x => x.MaterialNumber)
                .IsUnique()
                .HasDatabaseName("ux_parts_material_number");

            entity.HasIndex(x => new { x.IsActive, x.Description })
                .HasDatabaseName("ix_parts_active_description");
        });

        modelBuilder.Entity<Warehouse>(entity =>
        {
            entity.ToTable("warehouses");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id)
                .HasColumnName("id");

            entity.Property(x => x.Code)
                .HasColumnName("code")
                .HasMaxLength(32)
                .IsRequired();

            entity.Property(x => x.Name)
                .HasColumnName("name")
                .HasMaxLength(128)
                .IsRequired();

            entity.Property(x => x.Subtitle)
                .HasColumnName("subtitle")
                .HasMaxLength(128);

            entity.Property(x => x.IsActive)
                .HasColumnName("is_active")
                .IsRequired();

            entity.Property(x => x.SortOrder)
                .HasColumnName("sort_order")
                .IsRequired();

            entity.HasIndex(x => x.Code)
                .IsUnique()
                .HasDatabaseName("ux_warehouses_code");

            entity.HasData(
                new Warehouse
                {
                    Id = 1,
                    Code = "BUILDING_D",
                    Name = "Warehouse - Building D",
                    Subtitle = "Meter Lab",
                    IsActive = true,
                    SortOrder = 10
                },
                new Warehouse
                {
                    Id = 2,
                    Code = "BUILDING_A",
                    Name = "Warehouse - Building A",
                    Subtitle = "Central",
                    IsActive = true,
                    SortOrder = 20
                },
                new Warehouse
                {
                    Id = 3,
                    Code = "BUILDING_B",
                    Name = "Warehouse - Building B",
                    Subtitle = "Substation",
                    IsActive = true,
                    SortOrder = 30
                });
        });

        modelBuilder.Entity<CatalogTemplate>(entity =>
        {
            entity.ToTable("templates");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id)
                .HasColumnName("id");

            entity.Property(x => x.Name)
                .HasColumnName("name")
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.WarehouseId)
                .HasColumnName("warehouse_id");

            entity.Property(x => x.IsActive)
                .HasColumnName("is_active")
                .IsRequired();

            entity.Property(x => x.CreatedAtUtc)
                .HasColumnName("created_at_utc")
                .HasColumnType("datetime(6)")
                .IsRequired();

            entity.Property(x => x.UpdatedAtUtc)
                .HasColumnName("updated_at_utc")
                .HasColumnType("datetime(6)")
                .IsRequired();

            entity.Property(x => x.UpdatedBy)
                .HasColumnName("updated_by")
                .HasMaxLength(128);

            entity.HasOne(x => x.Warehouse)
                .WithMany()
                .HasForeignKey(x => x.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(x => new { x.WarehouseId, x.Name })
                .IsUnique()
                .HasDatabaseName("ux_templates_warehouse_name");

            entity.HasIndex(x => new { x.WarehouseId, x.IsActive, x.Name })
                .HasDatabaseName("ix_templates_warehouse_active_name");
        });

        modelBuilder.Entity<CatalogTemplateLine>(entity =>
        {
            entity.ToTable("template_lines");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id)
                .HasColumnName("id");

            entity.Property(x => x.TemplateId)
                .HasColumnName("template_id");

            entity.Property(x => x.PartId)
                .HasColumnName("part_id");

            entity.Property(x => x.SortOrder)
                .HasColumnName("sort_order")
                .IsRequired();

            entity.Property(x => x.Quantity)
                .HasColumnName("quantity")
                .IsRequired();

            entity.HasOne(x => x.Template)
                .WithMany(x => x.Lines)
                .HasForeignKey(x => x.TemplateId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Part)
                .WithMany()
                .HasForeignKey(x => x.PartId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(x => new { x.TemplateId, x.SortOrder })
                .HasDatabaseName("ix_template_lines_template_sort");

            entity.HasIndex(x => new { x.TemplateId, x.PartId })
                .IsUnique()
                .HasDatabaseName("ux_template_lines_template_part");
        });

        modelBuilder.Entity<PartWarehouse>(entity =>
        {
            entity.ToTable("part_warehouses");

            entity.HasKey(x => new { x.PartId, x.WarehouseId });

            entity.Property(x => x.PartId)
                .HasColumnName("part_id");

            entity.Property(x => x.WarehouseId)
                .HasColumnName("warehouse_id");

            entity.HasOne(x => x.Part)
                .WithMany(x => x.PartWarehouses)
                .HasForeignKey(x => x.PartId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Warehouse)
                .WithMany(x => x.PartWarehouses)
                .HasForeignKey(x => x.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(x => x.WarehouseId)
                .HasDatabaseName("ix_part_warehouses_warehouse_id");
        });
    }
}
