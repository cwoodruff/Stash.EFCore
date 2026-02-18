namespace Stash.EFCore.Sample.Models;

public class AuditLog
{
    public int Id { get; set; }
    public required string Action { get; set; }
    public required string EntityName { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
