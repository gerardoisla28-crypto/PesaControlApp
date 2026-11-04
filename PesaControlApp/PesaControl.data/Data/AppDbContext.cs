using Microsoft.EntityFrameworkCore;
using PesaControl.Core.Models;

namespace PesaControl.Data.Data;

// Modelo compartido por el almacén local de cada puesto (SQLite) y el servidor central (PostgreSQL).
public abstract class AppDbContext : DbContext
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
    public DbSet<ScaleConfig> ScaleConfigs => Set<ScaleConfig>();
    public DbSet<DvrConfig> DvrConfigs => Set<DvrConfig>();
    public DbSet<TicketCounter> TicketCounters => Set<TicketCounter>();

    protected AppDbContext(DbContextOptions options) : base(options)
    {
    }

    // Las fechas se guardan como hora local: los valores en UTC se convierten al escribir.
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<LocalDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<LocalDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WeighingRecord>(entity =>
        {
            entity.ToTable("WeighingRecords");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TicketNumber).HasMaxLength(20);
            entity.Property(e => e.StationCode).HasMaxLength(4);
            entity.Property(e => e.LicensePlate).HasMaxLength(20);
            entity.Property(e => e.PlateCountry).HasMaxLength(2).HasDefaultValue("CL");
            entity.Property(e => e.OperatorName).HasMaxLength(100);
            entity.Property(e => e.DriverName).HasMaxLength(100);
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.Property(e => e.PhotoPath).HasMaxLength(500);
            entity.Property(e => e.PhotoPathExit).HasMaxLength(500);
            entity.Property(e => e.EntryScaleName).HasMaxLength(100);
            entity.Property(e => e.ExitScaleName).HasMaxLength(100);
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

        modelBuilder.Entity<TicketCounter>(entity =>
        {
            entity.ToTable("TicketCounters");
            entity.HasKey(e => e.Prefix);
            entity.Property(e => e.Prefix).HasMaxLength(20);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Username).HasMaxLength(50).IsRequired();
            entity.Property(e => e.PasswordHash).HasMaxLength(512).IsRequired();
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
            entity.Property(e => e.Subtype).HasDefaultValue(0);
            entity.HasOne<DvrConfig>().WithMany().HasForeignKey(e => e.DvrConfigId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<DvrConfig>(entity =>
        {
            entity.ToTable("DvrConfigs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Manufacturer).HasMaxLength(50).HasDefaultValue("Dahua");
            entity.Property(e => e.Model).HasMaxLength(50).HasDefaultValue("DH-XVR1B04-I");
            entity.Property(e => e.IpAddress).HasMaxLength(50).IsRequired();
            entity.Property(e => e.HttpPort).HasDefaultValue(80);
            entity.Property(e => e.RtspPort).HasDefaultValue(554);
            entity.Property(e => e.Username).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Password).HasMaxLength(100);
            entity.Property(e => e.MaxChannels).HasDefaultValue(4);
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

        modelBuilder.Entity<ScaleConfig>(entity =>
        {
            entity.ToTable("ScaleConfigs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.PortName).HasMaxLength(20);
            entity.Property(e => e.IpAddress).HasMaxLength(50);
            entity.Property(e => e.ProtocolType).HasConversion<string>().HasMaxLength(15);
        });
    }
}
