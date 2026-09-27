namespace Lapse.Core.Items;

public enum ItemKind
{
    Tls,
    Domain,
    File,
    Manual,
}

public static class ItemKindExtensions
{
    public static string Label(this ItemKind kind) => kind switch
    {
        ItemKind.Tls => "tls",
        ItemKind.Domain => "domain",
        ItemKind.File => "file",
        ItemKind.Manual => "manual",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
