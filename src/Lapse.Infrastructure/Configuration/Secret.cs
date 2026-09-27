using Lapse.Core;

namespace Lapse.Infrastructure.Configuration;

public sealed class Secret(string value)
{
    public string Reveal() => value;

    public override string ToString() => "***";
}

public static class SecretResolver
{
    private const string EnvironmentPrefix = "${env:";
    private const string FilePrefix = "${file:";

    public static bool IsReference(string value) =>
        value.StartsWith("${", StringComparison.Ordinal) && value.EndsWith('}');

    public static Result<Secret> Resolve(string reference, string baseDirectory, Func<string, string?> readEnvironment)
    {
        if (reference.StartsWith(EnvironmentPrefix, StringComparison.Ordinal) && reference.EndsWith('}'))
        {
            var name = reference[EnvironmentPrefix.Length..^1];
            return readEnvironment(name) is { Length: > 0 } value
                ? Result.Success(new Secret(value))
                : Result.Failure<Secret>($"environment variable {name} is not set");
        }

        if (reference.StartsWith(FilePrefix, StringComparison.Ordinal) && reference.EndsWith('}'))
        {
            var path = Path.GetFullPath(Path.Combine(baseDirectory, reference[FilePrefix.Length..^1]));
            return File.Exists(path)
                ? Result.Success(new Secret(File.ReadAllText(path).TrimEnd('\r', '\n')))
                : Result.Failure<Secret>($"secret file {path} does not exist");
        }

        return Result.Failure<Secret>("unrecognized reference; use ${env:NAME} or ${file:path}");
    }
}
