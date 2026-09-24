using ConcurrencyTest;

// Scenario can be passed as an argument (see names below) or picked from the menu.
var choice = args.FirstOrDefault();

if (choice is null)
{
    Console.WriteLine("Choose a scenario:");
    Console.WriteLine("  1) race                                 (unsafe read -> modify -> save)");
    Console.WriteLine("  2) atomic-fixes-simple-update           (ExecuteUpdateAsync)");
    Console.WriteLine("  3) atomic-not-fixes-conditional-update  (per-customer limit: write skew)");
    Console.Write("> ");
    choice = Console.ReadLine();
}

switch (choice?.Trim().ToLowerInvariant())
{
    case "1" or "race":
        await ConcurrencyDemo.RunRaceConditionAsync();
        break;
    case "2" or "atomic-fixes-simple-update":
        await ConcurrencyDemo.RunAtomicUpdateAsync();
        break;
    case "3" or "atomic-not-fixes-conditional-update":
        await PurchaseLimitDemo.RunAsync();
        break;
    default:
        Console.WriteLine($"Unknown option '{choice}'. Use 1/race, 2/atomic-fixes-simple-update " +
                          "or 3/atomic-not-fixes-conditional-update.");
        break;
}
