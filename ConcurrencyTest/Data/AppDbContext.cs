using Microsoft.EntityFrameworkCore;

namespace ConcurrencyTest.Data;

public class AppDbContext : DbContext
{
    public const string ConnectionString =
        @"Server=.\SQLEXPRESS;Database=ConcurrencyTestDb;Trusted_Connection=True;TrustServerCertificate=True;Max Pool Size=200";

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseSqlServer(ConnectionString);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(b =>
        {
            b.Property(p => p.Name).HasMaxLength(100);

            // The column exists for every context, but here it is NOT a concurrency token,
            // so the race-condition demo keeps its lost-update behavior.
            // OptimisticDbContext turns the check on.
            b.Property(p => p.RowVersion)
                .HasColumnType("rowversion")
                .ValueGeneratedOnAddOrUpdate();
        });

        modelBuilder.Entity<Order>(b =>
        {
            b.HasOne<Product>().WithMany().HasForeignKey(o => o.ProductId);
        });
    }
}
