namespace Stash.EFCore.Sample.Models;

public class Supplier
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string ContactEmail { get; set; }

    public List<Product> Products { get; set; } = [];
}
