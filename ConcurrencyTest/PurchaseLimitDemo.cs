using ConcurrencyTest.Data;
using Microsoft.EntityFrameworkCore;

namespace ConcurrencyTest;

/// <summary>
/// Shows a case where <c>ExecuteUpdateAsync</c> is NOT enough (write skew):
/// a flash-sale rule says one customer may buy at most <see cref="MaxUnitsPerCustomer"/> units.
/// The stock decrement is atomic, but the rule is checked against *other rows* (the customer's orders),
/// so concurrent requests from the same customer all pass the check before any of them inserts an order.
/// </summary>
public static class PurchaseLimitDemo
{
    public const int MaxUnitsPerCustomer = 2;

    public static async Task RunAsync(int initialStock = 10, int concurrentRequests = 10)
    {
        const string title = "Per-customer purchase limit (ExecuteUpdateAsync is not enough)";
        const int customerId = 1;

        Console.WriteLine();
        Console.WriteLine($"########## Scenario: {title} ##########");

        var productId = await ConcurrencyDemo.ResetDatabaseAsync(initialStock);
        Console.WriteLine($"Rule: each customer may buy at most {MaxUnitsPerCustomer} units. " +
                          $"Customer {customerId} sends {concurrentRequests} requests at once.");

        // All requests wait on this gate so they hit the database at the same moment.
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var requests = Enumerable.Range(1, concurrentRequests)
            .Select(requestNo => Task.Run(async () =>
            {
                await startGate.Task;
                return await PurchaseWithLimitAsync(productId, customerId, requestNo);
            }))
            .ToArray();

        startGate.SetResult();
        await Task.WhenAll(requests);

        await using var db = new AppDbContext();
        var finalStock = await db.Products.Where(p => p.Id == productId).Select(p => p.Stock).SingleAsync();
        var customerOrders = await db.Orders.CountAsync(o => o.ProductId == productId && o.BuyerId == customerId);
        var expectedOrders = Math.Min(MaxUnitsPerCustomer, initialStock);
        var stockIsConsistent = initialStock - finalStock == customerOrders;

        Console.WriteLine();
        Console.WriteLine($"=========== Result: {title} ===========");
        Console.WriteLine($"Initial stock               : {initialStock}");
        Console.WriteLine($"Concurrent requests         : {concurrentRequests} (all from customer {customerId})");
        Console.WriteLine($"Orders for this customer    : {customerOrders}   (allowed: {expectedOrders})");
        Console.WriteLine($"Final stock in DB           : {finalStock}   (expected: {initialStock - expectedOrders})");
        Console.WriteLine($"Stock matches orders        : {(stockIsConsistent ? "yes" : "no")}");
        Console.WriteLine("=============================================");

        if (stockIsConsistent)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Stock is consistent: ExecuteUpdateAsync protected the Stock column (no lost update).");
        }

        if (customerOrders > MaxUnitsPerCustomer)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"BUSINESS RULE VIOLATED! Customer {customerId} bought {customerOrders} units " +
                              $"while the limit is {MaxUnitsPerCustomer} (write skew).");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Limit respected: customer {customerId} bought {customerOrders} units.");
        }
        Console.ResetColor();
    }

    /// <summary>
    /// Looks safe: it uses a transaction and an atomic, guarded ExecuteUpdateAsync for the stock.
    /// But the limit check (COUNT over Orders) and the order INSERT are separate statements,
    /// and the rows being counted are not the row being locked by the UPDATE.
    /// </summary>
    public static async Task<bool> PurchaseWithLimitAsync(int productId, int customerId, int requestNo)
    {
        await using var db = new AppDbContext();

        // Default isolation level is READ COMMITTED: the COUNT below takes no lasting locks,
        // so the transaction does not stop other requests from reading the same count.
        await using var transaction = await db.Database.BeginTransactionAsync();

        // 1) CHECK the business rule. It reads OTHER rows (this customer's orders),
        //    and every concurrent request sees the same count (0).
        var alreadyBought = await db.Orders.CountAsync(o => o.ProductId == productId && o.BuyerId == customerId);
        if (alreadyBought >= MaxUnitsPerCustomer)
        {
            Console.WriteLine($"Request {requestNo,3}: limit reached ({alreadyBought}/{MaxUnitsPerCustomer})");
            return false;
        }

        // Simulates work between the check and the write (pricing, validation, ...).
        await Task.Delay(100);

        // 2) Atomic, guarded stock decrement. This part IS safe: Stock never goes wrong.
        //    But it only locks the Products row; it knows nothing about the per-customer limit.
        var affectedRows = await db.Products
            .Where(p => p.Id == productId && p.Stock > 0)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock - 1));

        if (affectedRows == 0)
        {
            Console.WriteLine($"Request {requestNo,3}: out of stock");
            return false;
        }

        // 3) INSERT the order. The limit decided in step 1 may already be stale.
        db.Orders.Add(new Order { ProductId = productId, BuyerId = customerId, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        Console.WriteLine($"Request {requestNo,3}: bought 1 item (customer had {alreadyBought}/{MaxUnitsPerCustomer} at check time)");
        return true;
    }
}
