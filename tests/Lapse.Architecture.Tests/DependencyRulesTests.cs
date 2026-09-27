using Lapse.Core.Items;
using Lapse.Infrastructure.Sources;

namespace Lapse.Architecture.Tests;

public class DependencyRulesTests
{
    [Fact]
    public void Core_depends_only_on_the_base_library()
    {
        var references = typeof(Item).Assembly.GetReferencedAssemblies().Select(reference => reference.Name!);

        Assert.All(references, name => Assert.True(
            name.StartsWith("System", StringComparison.Ordinal),
            $"Lapse.Core must not depend on '{name}'."));
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_the_command_line()
    {
        var references = typeof(TlsSource).Assembly.GetReferencedAssemblies().Select(reference => reference.Name!);

        Assert.DoesNotContain(references, name => name.StartsWith("System.CommandLine", StringComparison.Ordinal) || name == "lapse");
    }
}
