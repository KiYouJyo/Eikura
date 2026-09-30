using System.Net;
using System.Net.Http.Headers;

namespace Eizo.Models;

internal sealed record TmdbConnectionCheckResult(
    bool IsSuccess,
    HttpStatusCode? StatusCode,
    string State);

internal static class TmdbConnectionVerifier
{
    private static readonly Uri ConfigurationUri =
        new("https://api.themoviedb.org/3/configuration");

    private static readonly HttpClient SharedHttpClient =
        new()
        {
            Timeout = TimeSpan.FromSeconds(12),
        };

    public static async Task<TmdbConnectionCheckResult>
        CheckAsync(
            string? token,
            HttpClient? httpClient = null,
            CancellationToken cancellationToken = default)
    {
        var normalized = token?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new TmdbConnectionCheckResult(
                false,
                null,
                "MissingToken");
        }

        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                ConfigurationUri);
        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                normalized);
        request.Headers.UserAgent.ParseAdd(
            "KiYouJyo/Eikura/1.3.2");

        try
        {
            using var response =
                await (httpClient ?? SharedHttpClient)
                    .SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken)
                    .ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? new TmdbConnectionCheckResult(
                    true,
                    response.StatusCode,
                    "Connected")
                : new TmdbConnectionCheckResult(
                    false,
                    response.StatusCode,
                    response.StatusCode is
                        HttpStatusCode.Unauthorized or
                        HttpStatusCode.Forbidden
                            ? "Unauthorized"
                            : "HttpError");
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return new TmdbConnectionCheckResult(
                false,
                null,
                "Timeout");
        }
        catch (HttpRequestException)
        {
            return new TmdbConnectionCheckResult(
                false,
                null,
                "NetworkError");
        }
    }
}
