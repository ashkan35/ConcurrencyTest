using ConcurrencyTest;

// Scenario can be passed as an argument (race | atomic) or picked from the menu.
var choice = args.FirstOrDefault();

if (choice is null)
{
    Console.WriteLine("Choose a scenario:");
    Console.WriteLine("  1) Race condition  (unsafe read -> modify -> save)");
    Console.WriteLine("  2) Atomic update   (ExecuteUpdateAsync)");
    Console.Write("> ");
    choice = Console.ReadLine();
}

switch (choice?.Trim().ToLowerInvariant())
{
    case "1" or "race":
        await ConcurrencyDemo.RunRaceConditionAsync();
        break;
    case "2" or "atomic":
        await ConcurrencyDemo.RunAtomicUpdateAsync();
        break;
    default:
        Console.WriteLine($"Unknown option '{choice}'. Use 1/race or 2/atomic.");
        break;
}
