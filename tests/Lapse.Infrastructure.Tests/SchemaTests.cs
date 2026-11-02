using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lapse.Infrastructure.Configuration;

namespace Lapse.Infrastructure.Tests;

public class SchemaTests
{
    private static readonly string Root = FindRepositoryRoot();

    public static TheoryData<string, Type, bool> Sections => new()
    {
        { "", typeof(ConfigDocument), true },
        { "properties/owners/additionalProperties", typeof(OwnerDocument), true },
        { "properties/defaults", typeof(DefaultsDocument), true },
        { "properties/watch", typeof(WatchDocument), true },
        { "properties/watch/properties/entra/items", typeof(EntraDocument), true },
        { "properties/watch/properties/manual/items", typeof(TargetDocument), false },
        { "properties/watch/properties/files/items/oneOf/1", typeof(TargetDocument), false },
        { "$defs/target/oneOf/1", typeof(TargetDocument), false },
        { "properties/notify", typeof(NotifyDocument), true },
        { "properties/notify/properties/email", typeof(EmailDocument), true },
        { "properties/notify/properties/telegram", typeof(TelegramDocument), true },
        { "$defs/postUrl", typeof(WebhookDocument), true },
    };

    [Theory]
    [MemberData(nameof(Sections))]
    public void Schema_describes_the_properties_the_configuration_accepts(string path, Type document, bool complete)
    {
        using var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "lapse.schema.json")));
        var section = path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Aggregate(schema.RootElement, (element, part) => int.TryParse(part, out var index) ? element[index] : element.GetProperty(part));

        var described = section.GetProperty("properties").EnumerateObject().Select(property => property.Name).ToHashSet();
        var accepted = document.GetProperties().Select(JsonName).ToHashSet();

        Assert.Subset(accepted, described);
        if (complete)
        {
            Assert.Equal(accepted.Order(), described.Order());
        }
    }

    [Fact]
    public async Task Example_configuration_loads_as_is()
    {
        var result = await ConfigLoader.LoadAsync(Path.Combine(Root, "lapse.example.json"), _ => null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
    }

    private static string JsonName(PropertyInfo property) =>
        property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name);

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Lapse.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Lapse.slnx was not found above the test folder.");
    }
}
