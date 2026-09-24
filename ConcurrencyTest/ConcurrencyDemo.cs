using ConcurrencyTest.Data;
using Microsoft.EntityFrameworkCore;

namespace ConcurrencyTest;

/// <summary>
/// Runs the same "many buyers at once" scenario against different purchase strategies:
/// - <see cref="PurchaseWithRaceConditionAsync"/>: unsafe read → modify → save (lost update).
/// - <see cref="PurchaseWithAtomicUpdateAsync"/>: a single conditional UPDATE executed by SQL Server.
/// </summary>
public static class ConcurrencyDemo
{
    public static Task RunRaceConditionAsync(int initialStock = 10, int concurrentBuyers = 50)
        => RunScenarioAsync("Race condition (read -> modify -> save)",
            PurchaseWithRaceConditionAsync, initialStock, concurrentBuyers);

    public static Task RunAtomicUpdateAsync(int initialStock = 10, int concurrentBuyers = 50)
        => RunScenarioAsync("Atomic update (ExecuteUpdateAsync)",
            PurchaseWithAtomicUpdateAsync, initialStock, concurrentBuyers);

    /// <summary>
    /// The unsafe "read → check → modify → save" pattern.
    /// Each call uses its own DbContext, just like separate HTTP requests would.
    /// </summary>
    public static async Task<bool> PurchaseWithRaceConditionAsync(int productId, int buyerId)
    {
        await using var db = new AppDbContext();

        // 1) READ: every concurrent buyer reads the same Stock value (e.g. 10).
        var product = await db.Products.SingleAsync(p => p.Id == productId);

        // 2) CHECK: all of them see stock > 0 and continue.
        if (product.Stock <= 0)
        {
            Console.WriteLine($"Buyer {buyerId,3}: out of stock");
            return false;
        }

        var stockSeen = product.Stock;

        // Simulates real work between read and write (payment, validation, ...).
        // This widens the race window so the problem shows up on every run.
        await Task.Delay(100);

        // 3) MODIFY + SAVE: the new value is computed from the stale in-memory copy.
        // SQL sent: UPDATE [Products] SET [Stock] = @p0 WHERE [Id] = @p1
        product.Stock -= 1;
        db.Orders.Add(new Order { ProductId = productId, BuyerId = buyerId, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        Console.WriteLine($"Buyer {buyerId,3}: bought 1 item (saw stock = {stockSeen}, wrote stock = {product.Stock})");
        return true;
    }

    /// <summary>
    /// The safe version: the stock check and the decrement are one SQL statement,
    /// so the database evaluates "Stock > 0" against the current value while holding the row lock.
    /// </summary>
    public static async Task<bool> PurchaseWithAtomicUpdateAsync(int productId, int buyerId)
    {
        // Same simulated work as the unsafe version, but done before touching the database
        // so no lock is held while waiting.
        await Task.Delay(100);

        await using var db = new AppDbContext();

        // ExecuteUpdateAsync runs immediately (outside SaveChanges), so the stock decrement
        // and the order insert are wrapped in one transaction to succeed or fail together.
        await using var transaction = await db.Database.BeginTransactionAsync();

        // SQL sent:
        // UPDATE [p] SET [p].[Stock] = [p].[Stock] - 1
        // FROM [Products] AS [p]
        // WHERE [p].[Id] = @productId AND [p].[Stock] > 0
        var affectedRows = await db.Products
            .Where(p => p.Id == productId && p.Stock > 0)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock - 1));

        // 0 rows means the WHERE clause failed: no stock left at the moment of the update.
        if (affectedRows == 0)
        {
            Console.WriteLine($"Buyer {buyerId,3}: out of stock");
            return false;
        }

        db.Orders.Add(new Order { ProductId = productId, BuyerId = buyerId, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        Console.WriteLine($"Buyer {buyerId,3}: bought 1 item");
        return true;
    }

    private static async Task RunScenarioAsync(
        string title,
        Func<int, int, Task<bool>> purchaseAsync,
        int initialStock,
        int concurrentBuyers)
    {
        Console.WriteLine();
        Console.WriteLine($"########## Scenario: {title} ##########");

        var productId = await ResetDatabaseAsync(initialStock);

        // All buyers wait on this gate so they hit the database at the same moment.
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var buyers = Enumerable.Range(1, concurrentBuyers)
            .Select(buyerId => Task.Run(async () =>
            {
                await startGate.Task;
                return await purchaseAsync(productId, buyerId);
            }))
            .ToArray();

        startGate.SetResult();
        var results = await Task.WhenAll(buyers);

        await using var db = new AppDbContext();
        var finalStock = await db.Products.Where(p => p.Id == productId).Select(p => p.Stock).SingleAsync();
        var ordersCount = await db.Orders.CountAsync(o => o.ProductId == productId);
        var successfulPurchases = results.Count(r => r);
        var expectedSales = Math.Min(initialStock, concurrentBuyers);
        var lostUpdates = ordersCount - (initialStock - finalStock);

        Console.WriteLine();
        Console.WriteLine($"=========== Result: {title} ===========");
        Console.WriteLine($"Initial stock           : {initialStock}");
        Console.WriteLine($"Concurrent buyers       : {concurrentBuyers}");
        Console.WriteLine($"Successful purchases    : {successfulPurchases}   (expected: {expectedSales})");
        Console.WriteLine($"Orders saved in DB      : {ordersCount}   (expected: {expectedSales})");
        Console.WriteLine($"Final stock in DB       : {finalStock}   (expected: {initialStock - expectedSales})");
        Console.WriteLine($"Lost stock updates      : {lostUpdates}");
        Console.WriteLine("=============================================");

        if (ordersCount > initialStock || lostUpdates > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"RACE CONDITION! {ordersCount} items sold while only {initialStock} were in stock " +
                              $"({Math.Max(0, ordersCount - initialStock)} oversold), and {lostUpdates} stock " +
                              "decrements were overwritten (lost update).");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"CONSISTENT: {ordersCount} orders saved and stock went from {initialStock} to {finalStock}.");
        }
        Console.ResetColor();
    }

    private static async Task<int> ResetDatabaseAsync(int initialStock)
    {
        await using var db = new AppDbContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        var product = new Product { Name = "Laptop", Stock = initialStock };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        Console.WriteLine($"Database reset. Product '{product.Name}' (Id={product.Id}) created with stock = {initialStock}.");
        return product.Id;
    }
}
