namespace Lapse.Core.Items;

public readonly record struct ItemKey(ItemKind Kind, string Value)
{
    public override string ToString() => $"{Kind.Label()}:{Value}";
}

public enum ItemStatus
{
    Active,
    Renewed,
    Failing,
}

public sealed record Item(
    ItemKey Key,
    ItemKey Target,
    string Name,
    string Owner,
    DateTimeOffset? ExpiresAt,
    ItemStatus Status,
    DateTimeOffset LastScannedAt,
    string? LastError);

public sealed record Observation(ItemKey Key, string Name, DateTimeOffset ExpiresAt, string? Fingerprint);

public sealed record WatchTarget(ItemKey Key, string Owner, DateTimeOffset? DeclaredExpiresAt = null, string? DisplayName = null)
{
    public string Name => DisplayName ?? Key.Value;
}
