namespace First10.Domain;

/// <summary>Tiny key/value store for singleton system state (e.g. the retention-sweep chain id).</summary>
public class SystemStateEntry
{
    public string Key { get; set; } = default!;
    public string Value { get; set; } = default!;
}
