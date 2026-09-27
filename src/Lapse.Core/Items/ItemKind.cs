namespace Lapse.Core.Items;

public enum ItemKind
{
    Tls,
    Domain,
    File,
    Manual,
    Entra,
}

public static class ItemKindExtensions
{
    public static string Label(this ItemKind kind) => kind switch
    {
        ItemKind.Tls => "tls",
        ItemKind.Domain => "domain",
        ItemKind.File => "file",
        ItemKind.Manual => "manual",
        ItemKind.Entra => "entra",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
