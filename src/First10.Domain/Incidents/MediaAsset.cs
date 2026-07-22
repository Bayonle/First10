namespace First10.Domain.Incidents;

/// <summary>
/// pHash corpus row (Stage 0). Every inbound image lands here; new images are compared
/// against recent hashes to catch re-sent/viral crash photos. Seed rows (known viral
/// images) have no TicketId.
/// </summary>
public class MediaAsset
{
    public Guid Id { get; set; }
    public string MediaRef { get; set; } = default!;
    public long PerceptualHash { get; set; } // ulong stored as long (bit pattern)
    public Guid? TicketId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
