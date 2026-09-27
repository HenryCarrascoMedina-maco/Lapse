using System.Text.Json;
using Lapse.Core;

namespace Lapse.Infrastructure.Configuration;

public static class ConfigLoader
{
    public static async Task<Result<LapseConfiguration>> LoadAsync(
        string path,
        Func<string, string?> readEnvironment,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            return Result.Failure<LapseConfiguration>($"Configuration file {fullPath} does not exist. Create one with: lapse init");
        }

        ConfigDocument? document;
        try
        {
            await using var stream = File.OpenRead(fullPath);
            document = await JsonSerializer.DeserializeAsync(stream, ConfigJsonContext.Default.ConfigDocument, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Result.Failure<LapseConfiguration>($"{Path.GetFileName(fullPath)} is not valid: {ex.Message}");
        }

        return document is null
            ? Result.Failure<LapseConfiguration>("The configuration is empty.")
            : new ConfigValidator(document, Path.GetDirectoryName(fullPath)!, readEnvironment).Validate();
    }
}
