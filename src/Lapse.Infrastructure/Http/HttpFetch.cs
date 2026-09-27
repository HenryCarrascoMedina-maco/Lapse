using System.Globalization;
using System.Net;
using Lapse.Core;

namespace Lapse.Infrastructure.Http;

internal static class HttpFetch
{
    private const int MaxAttempts = 3;

    public static Task<Result<string>> GetStringAsync(HttpClient http, Uri uri, CancellationToken cancellationToken) =>
        GetStringAsync(http, uri, describeFailure: null, cancellationToken);

    public static async Task<Result<string>> GetStringAsync(
        HttpClient http,
        Uri uri,
        Func<HttpStatusCode, string?>? describeFailure,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var (result, transient) = await TryGetAsync(http, uri, describeFailure, cancellationToken);
            if (result.IsSuccess || !transient || attempt == MaxAttempts)
            {
                return result;
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
        }
    }

    private static async Task<(Result<string> Result, bool Transient)> TryGetAsync(
        HttpClient http,
        Uri uri,
        Func<HttpStatusCode, string?>? describeFailure,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(uri, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return (Result.Success(await response.Content.ReadAsStringAsync(cancellationToken)), false);
            }

            var status = (int)response.StatusCode;
            var transient = status >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests;
            var error = describeFailure?.Invoke(response.StatusCode)
                ?? (response.StatusCode == HttpStatusCode.NotFound
                    ? "the service did not find the resource (HTTP 404)"
                    : string.Create(CultureInfo.InvariantCulture, $"the service answered HTTP {status}"));
            return (Result.Failure<string>(error), transient);
        }
        catch (HttpRequestException)
        {
            return (Result.Failure<string>($"could not connect to {uri.Host}"), true);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (Result.Failure<string>($"{uri.Host} did not answer in time"), true);
        }
    }
}
