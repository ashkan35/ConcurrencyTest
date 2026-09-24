namespace ConcurrencyTest.Data;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Stock { get; set; }

    // SQL Server rowversion: changes automatically on every update of the row.
    public byte[] RowVersion { get; set; } = null!;
}

public class Order
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int BuyerId { get; set; }
    public DateTime CreatedAt { get; set; }
}
