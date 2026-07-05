using Microsoft.EntityFrameworkCore;
using PesaControl.Core.Models;

namespace PesaControl.Data.Data;

public class AppDbContext : DbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<WeighingRecord> WeighingRecords => Set<WeighingRecord>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<SystemConfig> SystemConfigs => Set<SystemConfig>();
    public DbSet<CameraConfig> CameraConfigs => Set<CameraConfig>();
    public DbSet<BarrierConfig> BarrierConfigs => Set<BarrierConfig>();
    public DbSet<TrafficLightConfig> TrafficLightConfigs => Set<TrafficLightConfig>();
    public DbSet<TicketTemplate> TicketTemplates => Set<TicketTemplate>();
    public DbSet<PlateRecord> PlateRecords => Set<PlateRecord>();
    public DbSet<Operator> Operators => Set<Operator>();
    public DbSet<Driver> Drivers => Set<Driver>();

    private readonly string _dbPath;

    public AppDbContext()
    {
        _dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pesacontrol.db");
    }

    public AppDbContext(string dbPath)
    {
        _dbPath = dbPath;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite($"Data Source={_dbPath}");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WeighingRecord>(entity =>
        {
            entity.ToTable("WeighingRecords");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TicketNumber).HasMaxLength(20);
            entity.Property(e => e.LicensePlate).HasMaxLength(20);
            entity.Property(e => e.PlateCountry).HasMaxLength(2).HasDefaultValue("CL");
            entity.Property(e => e.OperatorName).HasMaxLength(100);
            entity.Property(e => e.DriverName).HasMaxLength(100);
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.Property(e => e.PhotoPath).HasMaxLength(500);
            entity.Property(e => e.PhotoPathExit).HasMaxLength(500);
            entity.Property(e => e.GrossWeight).HasColumnType("decimal(18,3)");
            entity.Property(e => e.TareWeight).HasColumnType("decimal(18,3)");
            entity.Property(e => e.NetWeight).HasColumnType("decimal(18,3)");
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasOne(e => e.Product).WithMany().HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(e => e.TicketNumber).IsUnique();
            entity.HasIndex(e => e.WeighInTime);
            entity.HasIndex(e => e.LicensePlate);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Username).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Password).HasMaxLength(200).IsRequired();
            entity.Property(e => e.FullName).HasMaxLength(100);
            entity.Property(e => e.Email).HasMaxLength(100);
            entity.Property(e => e.Role).HasMaxLength(20).HasDefaultValue("Operator");
            entity.HasIndex(e => e.Username).IsUnique();
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Products");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<Company>(entity =>
        {
            entity.ToTable("Companies");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.RUT).HasMaxLength(20);
            entity.Property(e => e.Address).HasMaxLength(300);
            entity.Property(e => e.Phone).HasMaxLength(20);
        });

        modelBuilder.Entity<Operator>(entity =>
        {
            entity.ToTable("Operators");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FullName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Phone).HasMaxLength(20);
            entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Driver>(entity =>
        {
            entity.ToTable("Drivers");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FullName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Phone).HasMaxLength(20);
            entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SystemConfig>(entity =>
        {
            entity.ToTable("SystemConfigs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Key).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Value).HasMaxLength(500);
            entity.HasIndex(e => e.Key).IsUnique();
        });

        modelBuilder.Entity<CameraConfig>(entity =>
        {
            entity.ToTable("CameraConfigs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Url).HasMaxLength(500).IsRequired();
            entity.Property(e => e.Type).HasConversion<string>().HasMaxLength(10);
        });

        modelBuilder.Entity<BarrierConfig>(entity =>
        {
            entity.ToTable("BarrierConfigs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.PortName).HasMaxLength(20);
            entity.Property(e => e.IpAddress).HasMaxLength(50);
            entity.Property(e => e.ConnectionType).HasConversion<string>().HasMaxLength(15);
        });

        modelBuilder.Entity<TrafficLightConfig>(entity =>
        {
            entity.ToTable("TrafficLightConfigs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.PortName).HasMaxLength(20);
            entity.Property(e => e.IpAddress).HasMaxLength(50);
            entity.Property(e => e.ConnectionType).HasConversion<string>().HasMaxLength(15);
        });

        modelBuilder.Entity<TicketTemplate>(entity =>
        {
            entity.ToTable("TicketTemplates");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Header).HasMaxLength(500);
            entity.Property(e => e.Footer).HasMaxLength(500);
        });

        modelBuilder.Entity<PlateRecord>(entity =>
        {
            entity.ToTable("PlateRecords");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Plate).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Region).HasMaxLength(5);
            entity.Property(e => e.Source).HasMaxLength(30);
            entity.HasIndex(e => e.Plate);
            entity.HasIndex(e => e.Timestamp);
            entity.HasOne(e => e.Camera).WithMany().HasForeignKey(e => e.CameraConfigId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.WeighingRecord).WithMany().HasForeignKey(e => e.WeighingRecordId).OnDelete(DeleteBehavior.SetNull);
        });
    }
}
