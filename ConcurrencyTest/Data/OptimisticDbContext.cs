using Microsoft.EntityFrameworkCore;

namespace ConcurrencyTest.Data;

/// <summary>
/// Same database and tables as <see cref="AppDbContext"/>, but Product.RowVersion is a concurrency token:
/// EF Core adds "AND [RowVersion] = @originalRowVersion" to every UPDATE/DELETE on Products and throws
/// <see cref="DbUpdateConcurrencyException"/> when no row matches (someone else changed it first).
/// </summary>
public class OptimisticDbContext : AppDbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Product>()
            .Property(p => p.RowVersion)
            .IsConcurrencyToken();
    }
}
