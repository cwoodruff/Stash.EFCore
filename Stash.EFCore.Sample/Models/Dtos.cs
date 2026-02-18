namespace Stash.EFCore.Sample.Models;

public record CreateProductDto(string Name, decimal Price, int CategoryId, int SupplierId);
public record UpdateProductDto(string Name, decimal Price, bool IsActive);
