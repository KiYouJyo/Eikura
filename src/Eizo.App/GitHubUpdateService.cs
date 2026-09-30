using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Eizo;

internal sealed record GitHubReleaseAsset(string Name, string BrowserDownloadUrl, long Size, string? Digest);

internal sealed record GitHubReleaseInfo(
    string TagName,
    string HtmlUrl,
    string? Name,
    string? Body,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<GitHubReleaseAsset> Assets)
{
    public string DisplayVersion => TagName.TrimStart('v', 'V');
}

internal sealed class GitHubAssetDownloadException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}

internal static class GitHubUpdateService
{
    private static readonly HttpClient Client = CreateApiClient(TimeSpan.FromSeconds(15));
    private static readonly HttpClient DownloadClient = CreateDownloadClient(TimeSpan.FromMinutes(5), useProxy: true);
    private static readonly HttpClient DirectDownloadClient = CreateDownloadClient(TimeSpan.FromMinutes(5), useProxy: false);

    public static async Task<GitHubReleaseInfo?> GetLatestReleaseAsync(
        string repository,
        bool includePrereleases = false,
        CancellationToken cancellationToken = default)
    {
        using var response = await Client.GetAsync($"https://api.github.com/repos/{repository}/releases?per_page=50", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (json.RootElement.ValueKind != JsonValueKind.Array) return null;

        GitHubReleaseInfo? latest = null;
        Version? latestVersion = null;
        foreach (var release in json.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) continue;
            if (!includePrereleases && release.TryGetProperty("prerelease", out var prerelease) && prerelease.ValueKind == JsonValueKind.True) continue;
            var tagName = release.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null;
            if (string.IsNullOrWhiteSpace(tagName) || !TryParseVersionTag(tagName, out var version)) continue;
            if (latestVersion is not null && version <= latestVersion) continue;
            latestVersion = version;
            latest = ParseRelease(release);
        }

        return latest;
    }

    public static bool IsNewer(string availableTag, Version current) =>
        TryParseVersionTag(availableTag, out var available) && available > current;

    public static bool TryParseVersionTag(string tag, out Version version)
    {
        var normalized = tag.Trim().TrimStart('v', 'V');
        var separator = normalized.IndexOfAny(['-', '+']);
        if (separator >= 0) normalized = normalized[..separator];
        return Version.TryParse(normalized, out version!);
    }

    public static async Task DownloadAssetAsync(
        GitHubReleaseAsset asset,
        string destinationPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (!Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out var downloadUri) || downloadUri.Scheme != Uri.UriSchemeHttps)
            throw new GitHubAssetDownloadException("InvalidAssetUrl", "The release asset URL is invalid.");
        if (!TryGetSha256Digest(asset.Digest, out var expectedDigest))
            throw new GitHubAssetDownloadException("MissingDigest", "The release asset does not provide a SHA-256 digest.");

        var directory = Path.GetDirectoryName(destinationPath) ?? throw new GitHubAssetDownloadException("StoragePath", "The destination directory is invalid.");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{destinationPath}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        Exception? lastFailure = null;
        var digestMismatch = false;
        try
        {
            var clients = new[] { DownloadClient, DownloadClient, DirectDownloadClient };
            for (var attempt = 0; attempt < clients.Length; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                try
                {
                    await DownloadToTemporaryFileAsync(clients[attempt], downloadUri, temporaryPath, asset.Size, progress, cancellationToken).ConfigureAwait(false);
                    if (!await VerifyDigestAsync(temporaryPath, expectedDigest, cancellationToken).ConfigureAwait(false))
                    {
                        digestMismatch = true;
                        lastFailure = new InvalidDataException("The release asset SHA-256 digest does not match GitHub metadata.");
                        if (attempt < clients.Length - 1)
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(350 * (attempt + 1)), cancellationToken).ConfigureAwait(false);
                            continue;
                        }
                        break;
                    }

                    lastFailure = null;
                    digestMismatch = false;
                    break;
                }
                catch (HttpRequestException exception) when (attempt < clients.Length - 1)
                {
                    lastFailure = exception;
                    await Task.Delay(TimeSpan.FromMilliseconds(350 * (attempt + 1)), cancellationToken).ConfigureAwait(false);
                }
                catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested && attempt < clients.Length - 1)
                {
                    lastFailure = exception;
                    await Task.Delay(TimeSpan.FromMilliseconds(350 * (attempt + 1)), cancellationToken).ConfigureAwait(false);
                }
            }

            if (digestMismatch)
                throw new GitHubAssetDownloadException("DigestMismatch", "The release asset failed SHA-256 verification through both the system proxy and a direct connection.", lastFailure);
            if (!File.Exists(temporaryPath))
                throw new GitHubAssetDownloadException("DownloadNetwork", "The release asset could not be downloaded through either the system proxy or a direct connection.", lastFailure);

            File.Move(temporaryPath, destinationPath, overwrite: true);
            progress?.Report(1d);
        }
        catch (HttpRequestException exception)
        {
            throw new GitHubAssetDownloadException("DownloadNetwork", exception.Message, exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GitHubAssetDownloadException("DownloadTimeout", exception.Message, exception);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static async Task DownloadToTemporaryFileAsync(
        HttpClient client,
        Uri downloadUri,
        string temporaryPath,
        long declaredSize,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var expectedLength = response.Content.Headers.ContentLength ?? declaredSize;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            total += read;
            if (expectedLength > 0) progress?.Report(Math.Clamp(total / (double)expectedLength, 0d, 1d));
        }

        if (declaredSize > 0 && total != declaredSize)
            throw new HttpRequestException($"The release asset length is incomplete: expected {declaredSize} bytes, received {total} bytes.");
    }

    private static async Task<bool> VerifyDigestAsync(string path, byte[] expectedDigest, CancellationToken cancellationToken)
    {
        await using var verificationStream = File.OpenRead(path);
        var actualDigest = await SHA256.HashDataAsync(verificationStream, cancellationToken).ConfigureAwait(false);
        return CryptographicOperations.FixedTimeEquals(actualDigest, expectedDigest);
    }

    private static GitHubReleaseInfo ParseRelease(JsonElement root)
    {
        var assets = root.TryGetProperty("assets", out var assetArray) && assetArray.ValueKind == JsonValueKind.Array
            ? assetArray.EnumerateArray().Select(ParseAsset).Where(static asset => asset is not null).Cast<GitHubReleaseAsset>().ToArray()
            : [];
        return new GitHubReleaseInfo(
            root.GetProperty("tag_name").GetString() ?? string.Empty,
            root.GetProperty("html_url").GetString() ?? string.Empty,
            root.TryGetProperty("name", out var name) ? name.GetString() : null,
            root.TryGetProperty("body", out var body) ? body.GetString() : null,
            root.TryGetProperty("published_at", out var published) && published.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(published.GetString(), out var timestamp) ? timestamp : null,
            assets);
    }

    private static GitHubReleaseAsset? ParseAsset(JsonElement asset)
    {
        var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
        var url = asset.TryGetProperty("browser_download_url", out var urlElement) ? urlElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url)) return null;
        var size = asset.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var parsedSize) ? parsedSize : 0;
        var digest = asset.TryGetProperty("digest", out var digestElement) ? digestElement.GetString() : null;
        return new GitHubReleaseAsset(name, url, size, digest);
    }

    private static bool TryGetSha256Digest(string? digest, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(digest) || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            bytes = Convert.FromHexString(digest[7..]);
            return bytes.Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static HttpClient CreateApiClient(TimeSpan timeout)
    {
        var client = new HttpClient { Timeout = timeout };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Eikura", AppVersionProvider.Version));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    private static HttpClient CreateDownloadClient(TimeSpan timeout, bool useProxy)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
            UseProxy = useProxy
        };
        var client = new HttpClient(handler) { Timeout = timeout };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Eikura", AppVersionProvider.Version));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        return client;
    }
}