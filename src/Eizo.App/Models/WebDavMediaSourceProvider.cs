using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;

namespace Eizo.Models;

public sealed record WebDavMediaProbeResult(
    bool IsAvailable,
    bool SupportsRanges,
    long? ContentLength = null,
    string? ErrorCode = null,
    string? Detail = null,
    string? EntityTag = null,
    DateTimeOffset? LastModified = null);

public sealed class WebDavMediaSourceProvider(
    IMediaCredentialProvider credentialStore)
    : IMediaSourceProvider
{
    private const int MaxDirectoryRequestAttempts = 4;
    private static readonly HttpMethod PropFindMethod = new("PROPFIND");
    private static readonly XNamespace Dav = "DAV:";

    private readonly IMediaCredentialProvider _credentialStore =
        credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
    private readonly object _clientSync = new();
    private readonly Dictionary<string, HttpClient> _sharedClients =
        new(StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool>
        _knownRangeSupport =
            new(StringComparer.Ordinal);

    public MediaSourceKind Kind => MediaSourceKind.WebDav;

    public bool TryGetKnownRangeSupport(
        string sourceId,
        out bool supportsRanges)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            supportsRanges = false;
            return false;
        }

        return _knownRangeSupport.TryGetValue(
            sourceId,
            out supportsRanges);
    }

    public async ValueTask<MediaSourceConnectionResult> TestConnectionAsync(
        MediaSourceDefinition source,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ValidateSource(source);
            using var client = CreateClient(source);
            using var request = CreatePropFindRequest(
                new Uri(source.RootLocation!, UriKind.Absolute),
                depth: 0);
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.MultiStatus ||
                response.IsSuccessStatusCode)
            {
                return new MediaSourceConnectionResult(true);
            }

            return new MediaSourceConnectionResult(
                false,
                MapStatusCode(response.StatusCode),
                $"{(int)response.StatusCode} {response.ReasonPhrase}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MediaSourceConnectionResult(
                false,
                "Timeout");
        }
        catch (HttpRequestException exception)
        {
            return new MediaSourceConnectionResult(
                false,
                "NetworkError",
                DescribeNetworkFailure(exception));
        }
        catch (MediaSourceException exception)
        {
            return new MediaSourceConnectionResult(
                false,
                exception.ErrorCode,
                exception.Message);
        }
    }

    public async IAsyncEnumerable<MediaSourceEntry> ListAsync(
        MediaSourceDefinition source,
        string relativePath = "",
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        ValidateSource(source);

        var rootUri = new Uri(source.RootLocation!, UriKind.Absolute);
        var requestUri = BuildRequestUri(rootUri, relativePath);
        var xml = await ReadDirectoryXmlWithRetryAsync(
            source,
            requestUri,
            cancellationToken);

        XDocument document;

        try
        {
            document = XDocument.Parse(xml, LoadOptions.None);
        }
        catch (Exception exception) when (
            exception is System.Xml.XmlException or InvalidOperationException)
        {
            throw new MediaSourceException(
                "InvalidWebDavResponse",
                "The WebDAV server returned an invalid multistatus document.",
                exception);
        }

        foreach (var responseElement in document.Descendants(Dav + "response"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var href = responseElement.Element(Dav + "href")?.Value;
            if (string.IsNullOrWhiteSpace(href))
                continue;

            var itemUri = ResolveHref(requestUri, href);
            if (itemUri is null ||
                !IsUriWithinRoot(rootUri, itemUri) ||
                SameResource(itemUri, requestUri))
            {
                continue;
            }

            var prop = SelectSuccessfulPropertySet(responseElement);
            if (prop is null)
                continue;

            var isDirectory =
                prop.Element(Dav + "resourcetype")?
                    .Element(Dav + "collection") is not null;

            var relative = GetRelativePath(rootUri, itemUri);
            if (string.IsNullOrWhiteSpace(relative))
                continue;

            if (isDirectory && !relative.EndsWith("/", StringComparison.Ordinal))
                relative += "/";

            var trimmed = relative.TrimEnd('/');
            var separator = trimmed.LastIndexOf('/');
            var name = separator >= 0
                ? trimmed[(separator + 1)..]
                : trimmed;

            long? size = null;
            if (long.TryParse(
                    prop.Element(Dav + "getcontentlength")?.Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parsedSize))
            {
                size = parsedSize;
            }

            DateTimeOffset? modified = null;
            if (DateTimeOffset.TryParse(
                    prop.Element(Dav + "getlastmodified")?.Value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out var parsedModified))
            {
                modified = parsedModified;
            }

            yield return new MediaSourceEntry(
                Uri.UnescapeDataString(name),
                relative,
                isDirectory,
                size,
                modified,
                itemUri.AbsoluteUri,
                prop.Element(Dav + "getetag")?.Value);

            await Task.Yield();
        }
    }

    public async Task<WebDavMediaProbeResult> ProbeMediaAsync(
        MediaSourceDefinition source,
        Uri mediaUri,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ValidateSource(source);

            var rootUri = new Uri(source.RootLocation!, UriKind.Absolute);
            if (!mediaUri.IsAbsoluteUri ||
                !IsUriWithinRoot(rootUri, mediaUri))
            {
                return new WebDavMediaProbeResult(
                    false,
                    false,
                    ErrorCode: "RemoteUriOutsideSource");
            }

            using var client = CreateClient(source);
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                mediaUri);
            request.Headers.Range = new RangeHeaderValue(0, 0);

            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.PartialContent)
            {
                _knownRangeSupport[source.Id] = true;

                var total = response.Content.Headers.ContentRange?.Length;
                return new WebDavMediaProbeResult(
                    true,
                    true,
                    total ?? response.Content.Headers.ContentLength,
                    EntityTag: response.Headers.ETag?.Tag,
                    LastModified: response.Content.Headers.LastModified);
            }

            if (response.IsSuccessStatusCode)
            {
                var acceptsRanges =
                    response.Headers.AcceptRanges.Any(value =>
                        string.Equals(
                            value,
                            "bytes",
                            StringComparison.OrdinalIgnoreCase));

                _knownRangeSupport[source.Id] =
                    acceptsRanges;

                return new WebDavMediaProbeResult(
                    true,
                    acceptsRanges,
                    response.Content.Headers.ContentLength,
                    EntityTag: response.Headers.ETag?.Tag,
                    LastModified: response.Content.Headers.LastModified);
            }

            return new WebDavMediaProbeResult(
                false,
                false,
                ErrorCode: MapStatusCode(response.StatusCode),
                Detail: $"{(int)response.StatusCode} {response.ReasonPhrase}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new WebDavMediaProbeResult(
                false,
                false,
                ErrorCode: "Timeout");
        }
        catch (HttpRequestException exception)
        {
            return new WebDavMediaProbeResult(
                false,
                false,
                ErrorCode: "NetworkError",
                Detail: DescribeNetworkFailure(exception));
        }
        catch (MediaSourceException exception)
        {
            return new WebDavMediaProbeResult(
                false,
                false,
                ErrorCode: exception.ErrorCode,
                Detail: exception.Message);
        }
    }

    public async Task<byte[]> DownloadRangeAsync(
        MediaSourceDefinition source,
        Uri fileUri,
        long offset,
        int count,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(fileUri);

        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        ValidateSource(source);

        var rootUri = new Uri(
            source.RootLocation!,
            UriKind.Absolute);

        if (!fileUri.IsAbsoluteUri ||
            !IsUriWithinRoot(rootUri, fileUri))
        {
            throw new MediaSourceException(
                "RemoteUriOutsideSource",
                "The requested WebDAV file is outside the configured media source.");
        }

        var end = checked(
            offset +
            count -
            1L);
        Exception? lastFailure = null;

        for (var attempt = 1;
             attempt <= MaxDirectoryRequestAttempts;
             attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    fileUri);
                request.Headers.Range =
                    new RangeHeaderValue(
                        offset,
                        end);

                using var response =
                    await GetSharedClient(source).SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken);

                if (response.StatusCode ==
                    HttpStatusCode.PartialContent)
                {
                    if (response.Content.Headers.ContentRange?.From is long from &&
                        from != offset)
                    {
                        throw new MediaSourceException(
                            "InvalidRangeResponse",
                            $"WebDAV returned range offset {from} instead of {offset}.");
                    }

                    var bytes =
                        await response.Content.ReadAsByteArrayAsync(
                            cancellationToken);

                    if (bytes.Length > count)
                    {
                        Array.Resize(
                            ref bytes,
                            count);
                    }

                    return bytes;
                }

                var failure = new MediaSourceException(
                    response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable
                        ? "RangeNotSatisfiable"
                        : "RangeNotSupported",
                    $"WebDAV range GET failed with {(int)response.StatusCode} {response.ReasonPhrase}.");

                if (!IsTransientStatus(response.StatusCode) ||
                    attempt == MaxDirectoryRequestAttempts)
                {
                    throw failure;
                }

                lastFailure = failure;
            }
            catch (TaskCanceledException exception)
                when (!cancellationToken.IsCancellationRequested &&
                      attempt < MaxDirectoryRequestAttempts)
            {
                lastFailure = exception;
            }
            catch (HttpRequestException exception)
                when (attempt < MaxDirectoryRequestAttempts)
            {
                lastFailure = exception;
            }

            await Task.Delay(
                RetryDelay(attempt),
                cancellationToken);
        }

        throw new MediaSourceException(
            "WebDavRangeDownloadFailed",
            $"WebDAV range GET failed after {MaxDirectoryRequestAttempts} attempts.",
            lastFailure);
    }

    public async Task<byte[]> DownloadFileAsync(
        MediaSourceDefinition source,
        Uri fileUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(fileUri);
        ValidateSource(source);

        var rootUri = new Uri(
            source.RootLocation!,
            UriKind.Absolute);

        if (!fileUri.IsAbsoluteUri ||
            !IsUriWithinRoot(rootUri, fileUri))
        {
            throw new MediaSourceException(
                "RemoteUriOutsideSource",
                "The requested WebDAV file is outside the configured media source.");
        }

        Exception? lastFailure = null;

        for (var attempt = 1;
             attempt <= MaxDirectoryRequestAttempts;
             attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    fileUri);
                using var response = await GetSharedClient(source).SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsByteArrayAsync(
                        cancellationToken);
                }

                var failure = new MediaSourceException(
                    MapStatusCode(response.StatusCode),
                    $"WebDAV GET failed with {(int)response.StatusCode} {response.ReasonPhrase}.");

                if (!IsTransientStatus(response.StatusCode) ||
                    attempt == MaxDirectoryRequestAttempts)
                {
                    throw failure;
                }

                lastFailure = failure;
            }
            catch (TaskCanceledException exception)
                when (!cancellationToken.IsCancellationRequested &&
                      attempt < MaxDirectoryRequestAttempts)
            {
                lastFailure = exception;
            }
            catch (HttpRequestException exception)
                when (attempt < MaxDirectoryRequestAttempts)
            {
                lastFailure = exception;
            }

            await Task.Delay(
                RetryDelay(attempt),
                cancellationToken);
        }

        throw new MediaSourceException(
            "WebDavDownloadFailed",
            $"WebDAV GET failed after {MaxDirectoryRequestAttempts} attempts.",
            lastFailure);
    }

    private async Task<string> ReadDirectoryXmlWithRetryAsync(
        MediaSourceDefinition source,
        Uri requestUri,
        CancellationToken cancellationToken)
    {
        Exception? lastFailure = null;

        for (var attempt = 1; attempt <= MaxDirectoryRequestAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var request = CreatePropFindRequest(requestUri, depth: 1);
                using var response = await GetSharedClient(source).SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (response.StatusCode == HttpStatusCode.MultiStatus ||
                    response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync(cancellationToken);
                }

                if (!IsTransientStatus(response.StatusCode) ||
                    attempt == MaxDirectoryRequestAttempts)
                {
                    throw new MediaSourceException(
                        MapStatusCode(response.StatusCode),
                        $"WebDAV PROPFIND failed with {(int)response.StatusCode} {response.ReasonPhrase}.");
                }

                lastFailure = new HttpRequestException(
                    $"Transient WebDAV response {(int)response.StatusCode} {response.ReasonPhrase}.");
            }
            catch (TaskCanceledException exception)
                when (!cancellationToken.IsCancellationRequested)
            {
                lastFailure = exception;
                if (attempt == MaxDirectoryRequestAttempts)
                {
                    throw new MediaSourceException(
                        "Timeout",
                        "The WebDAV directory request timed out after retrying.",
                        exception);
                }
            }
            catch (HttpRequestException exception)
            {
                lastFailure = exception;
                if (attempt == MaxDirectoryRequestAttempts)
                {
                    throw new MediaSourceException(
                        "NetworkError",
                        DescribeNetworkFailure(exception),
                        exception);
                }
            }

            await Task.Delay(
                RetryDelay(attempt),
                cancellationToken);
        }

        throw new MediaSourceException(
            "NetworkError",
            lastFailure is HttpRequestException requestFailure
                ? DescribeNetworkFailure(requestFailure)
                : lastFailure?.Message ?? "The WebDAV directory request failed.",
            lastFailure);
    }

    private HttpClient GetSharedClient(MediaSourceDefinition source)
    {
        lock (_clientSync)
        {
            if (_sharedClients.TryGetValue(source.Id, out var client))
                return client;

            client = CreateClient(source);
            _sharedClients[source.Id] = client;
            return client;
        }
    }

    private HttpClient CreateClient(MediaSourceDefinition source)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression =
                DecompressionMethods.GZip |
                DecompressionMethods.Deflate,
            PreAuthenticate = true,
            Credentials = new RefreshingWebDavCredentials(
                _credentialStore,
                source)
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(90)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Eikura/1.3.2");
        return client;
    }

    private static HttpRequestMessage CreatePropFindRequest(
        Uri uri,
        int depth)
    {
        const string body =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<d:propfind xmlns:d=\"DAV:\">" +
            "<d:prop>" +
            "<d:resourcetype/>" +
            "<d:getcontentlength/>" +
            "<d:getlastmodified/>" +
            "<d:getetag/>" +
            "</d:prop>" +
            "</d:propfind>";

        var request = new HttpRequestMessage(
            PropFindMethod,
            uri);
        request.Headers.TryAddWithoutValidation(
            "Depth",
            depth.ToString(CultureInfo.InvariantCulture));
        request.Content = new StringContent(
            body,
            Encoding.UTF8,
            "application/xml");
        return request;
    }

    private static XElement? SelectSuccessfulPropertySet(
        XElement responseElement)
    {
        foreach (var propStat in responseElement.Elements(Dav + "propstat"))
        {
            var status = propStat.Element(Dav + "status")?.Value;
            if (status?.Contains(
                    " 200 ",
                    StringComparison.Ordinal) == true)
            {
                return propStat.Element(Dav + "prop");
            }
        }

        return responseElement.Element(Dav + "prop");
    }

    private static Uri? ResolveHref(Uri requestUri, string href)
    {
        try
        {
            if (Uri.TryCreate(
                    href,
                    UriKind.Absolute,
                    out var absolute) &&
                (absolute.Scheme == Uri.UriSchemeHttp ||
                 absolute.Scheme == Uri.UriSchemeHttps))
            {
                return absolute;
            }

            return new Uri(requestUri, href);
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    private static Uri BuildRequestUri(
        Uri rootUri,
        string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return rootUri;

        var isDirectory =
            relativePath.EndsWith("/", StringComparison.Ordinal);
        var escapedSegments = relativePath
            .Trim('/')
            .Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString);

        var escaped = string.Join("/", escapedSegments);
        if (isDirectory)
            escaped += "/";

        return new Uri(rootUri, escaped);
    }

    private static string GetRelativePath(
        Uri rootUri,
        Uri itemUri)
    {
        var rootPath = Uri.UnescapeDataString(rootUri.AbsolutePath);
        if (!rootPath.EndsWith("/", StringComparison.Ordinal))
            rootPath += "/";

        var itemPath = Uri.UnescapeDataString(itemUri.AbsolutePath);
        if (!itemPath.StartsWith(
                rootPath,
                StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return itemPath[rootPath.Length..]
            .TrimStart('/');
    }

    private static bool IsUriWithinRoot(
        Uri rootUri,
        Uri itemUri)
    {
        if (!string.Equals(
                rootUri.Scheme,
                itemUri.Scheme,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                rootUri.Authority,
                itemUri.Authority,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rootPath = Uri.UnescapeDataString(rootUri.AbsolutePath);
        if (!rootPath.EndsWith("/", StringComparison.Ordinal))
            rootPath += "/";

        var itemPath = Uri.UnescapeDataString(itemUri.AbsolutePath);
        return itemPath.StartsWith(
            rootPath,
            StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                itemPath.TrimEnd('/'),
                rootPath.TrimEnd('/'),
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameResource(Uri left, Uri right) =>
        string.Equals(
            left.GetLeftPart(UriPartial.Path).TrimEnd('/'),
            right.GetLeftPart(UriPartial.Path).TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsTransientStatus(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.RequestTimeout ||
        statusCode == HttpStatusCode.TooManyRequests ||
        statusCode == HttpStatusCode.InternalServerError ||
        statusCode == HttpStatusCode.BadGateway ||
        statusCode == HttpStatusCode.ServiceUnavailable ||
        statusCode == HttpStatusCode.GatewayTimeout;

    private static TimeSpan RetryDelay(int attempt) =>
        TimeSpan.FromMilliseconds(attempt switch
        {
            1 => 350,
            2 => 800,
            _ => 1600
        });

    private static string DescribeNetworkFailure(HttpRequestException exception)
    {
        var inner = exception.InnerException?.Message;
        return string.IsNullOrWhiteSpace(inner)
            ? exception.Message
            : $"{exception.Message} {inner}";
    }

    private static void ValidateSource(MediaSourceDefinition source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.Kind != MediaSourceKind.WebDav)
        {
            throw new MediaSourceException(
                "SourceKindMismatch",
                "The media source is not WebDAV.");
        }

        if (string.IsNullOrWhiteSpace(source.RootLocation) ||
            !Uri.TryCreate(
                source.RootLocation,
                UriKind.Absolute,
                out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp &&
             uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new MediaSourceException(
                "SourceRootInvalid",
                "The WebDAV source root is not a valid HTTP(S) URI.");
        }
    }

    private static string MapStatusCode(HttpStatusCode statusCode) =>
        statusCode switch
        {
            HttpStatusCode.Unauthorized => "AuthenticationFailed",
            HttpStatusCode.Forbidden => "Forbidden",
            HttpStatusCode.NotFound => "NotFound",
            HttpStatusCode.RequestTimeout => "Timeout",
            _ => "HttpError"
        };

    private sealed class RefreshingWebDavCredentials(
        IMediaCredentialProvider provider,
        MediaSourceDefinition source)
        : ICredentials
    {
        public NetworkCredential? GetCredential(Uri uri, string authType)
        {
            var credential = provider.GetWebDav(source);
            return credential is null
                ? null
                : new NetworkCredential(
                    credential.UserName,
                    credential.Password);
        }
    }
}
