using Eizo.Models;
using Microsoft.UI.Xaml;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Eizo;

internal sealed record ConfigBackupOperationResult(
    DateTimeOffset TimestampUtc,
    int SourceCount,
    int SkippedLocalSources = 0);

internal sealed class ConfigBackupService
{
    private const int BackupSchemaVersion = 2;

    private const string BackupFileName = ".eikura-config-backup-v2.json";

    private const string CurrentAppVersion = "1.3.5";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly MediaCredentialStore _credentials =
        MediaCredentialStore.Default;
    private readonly MediaSourceStore _sources =
        MediaSourceStore.Default;

    public async Task<ConfigBackupOperationResult> BackupAsync(
        MediaSourceDefinition destination,
        CancellationToken cancellationToken = default)
    {
        ValidateDestination(destination);
        var webDavPassword = GetWebDavEncryptionPassword(destination);

        var timestamp = DateTimeOffset.UtcNow;
        var payload = CapturePayload(destination, timestamp);
        var envelope = ConfigBackupCodec.Encrypt(payload, webDavPassword, timestamp);
        var json = JsonSerializer.Serialize(envelope, SerializerOptions);
        var backupUri = BuildBackupUri(destination);

        try
        {
            using var client = CreateClient(destination);
            using var request = new HttpRequestMessage(HttpMethod.Put, backupUri)
            {
                Content = new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/vnd.eikura.config-backup+json")
            };
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw BuildHttpFailure(
                    "WebDavWriteFailed",
                    response,
                    "WebDAV backup upload failed");
            }
        }
        catch (ConfigBackupException)
        {
            throw;
        }
        catch (TaskCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new ConfigBackupException(
                "Timeout",
                "The WebDAV backup upload timed out.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new ConfigBackupException(
                "NetworkError",
                "The WebDAV backup upload could not reach the server.",
                exception);
        }

        return new ConfigBackupOperationResult(
            timestamp,
            payload.MediaSources.Count);
    }

    public async Task<ConfigBackupOperationResult> RestoreAsync(
        MediaSourceDefinition destination,
        CancellationToken cancellationToken = default)
    {
        ValidateDestination(destination);
        var webDavPassword = GetWebDavEncryptionPassword(destination);

        var backupUri = BuildBackupUri(destination);
        string json;

        try
        {
            using var client = CreateClient(destination);
            using var request = new HttpRequestMessage(HttpMethod.Get, backupUri);
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // Leave separately encrypted v1 backups intact. They cannot be
                // transparently decrypted without the old user-supplied password.
                var legacyUri = new Uri(backupUri, ".eikura-config-backup-v1.json");
                using var legacyRequest = new HttpRequestMessage(HttpMethod.Get, legacyUri);
                using var legacyResponse = await client.SendAsync(legacyRequest,
                    HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (legacyResponse.IsSuccessStatusCode)
                    throw new ConfigBackupException("LegacyBackupRequiresMigration",
                        "Restore the separately encrypted legacy backup with the previous version, then create a new backup.");
                if (legacyResponse.StatusCode != HttpStatusCode.NotFound)
                    throw BuildHttpFailure("WebDavReadFailed", legacyResponse, "Legacy backup lookup failed");
                throw new ConfigBackupException(
                    "BackupNotFound",
                    "No Eikura configuration backup exists at the selected WebDAV source.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw BuildHttpFailure(
                    "WebDavReadFailed",
                    response,
                    "WebDAV backup download failed");
            }

            json = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (ConfigBackupException)
        {
            throw;
        }
        catch (TaskCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new ConfigBackupException(
                "Timeout",
                "The WebDAV backup download timed out.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new ConfigBackupException(
                "NetworkError",
                "The WebDAV backup download could not reach the server.",
                exception);
        }

        var payload = ConfigBackupCodec.Decrypt<ConfigBackupPayload>(json, webDavPassword);
        ValidatePayload(payload);

        var skippedLocalSources = RestoreSources(
            destination,
            payload.MediaSources,
            payload.WebDavCredentials);

        RestoreAppSettings(payload.AppSettings);
        RestoreTheme(payload.Theme);

        if (string.IsNullOrWhiteSpace(payload.TmdbReadAccessToken))
        {
            _credentials.RemoveTmdbReadAccessToken();
        }
        else
        {
            _credentials.SaveTmdbReadAccessToken(
                payload.TmdbReadAccessToken);
        }

        return new ConfigBackupOperationResult(
            payload.CreatedAtUtc,
            payload.MediaSources.Count,
            skippedLocalSources);
    }

    private ConfigBackupPayload CapturePayload(
        MediaSourceDefinition destination,
        DateTimeOffset timestamp)
    {
        var portableSources = new List<PortableMediaSource>();
        var portableCredentials = new List<PortableWebDavCredential>();

        foreach (var source in _sources.Snapshot())
        {
            if (source.IsBuiltIn)
                continue;

            MediaCredentialSnapshot? credential = null;
            if (source.Kind == MediaSourceKind.WebDav)
                credential = _credentials.GetWebDav(source);

            portableSources.Add(
                new PortableMediaSource(
                    source.Id,
                    source.Kind,
                    source.DisplayName,
                    source.RootLocation,
                    source.UserName,
                    source.Enabled,
                    credential is not null ||
                    !string.IsNullOrWhiteSpace(source.CredentialKey),
                    source.SelectedPaths?.ToList()));

            // The selected backup destination must remain usable independently of
            // the backup it stores. Never make its own secret recoverable only
            // from the file that requires that secret to download.
            if (source.Kind != MediaSourceKind.WebDav ||
                credential is null ||
                string.Equals(
                    source.Id,
                    destination.Id,
                    StringComparison.Ordinal))
            {
                continue;
            }

            portableCredentials.Add(
                new PortableWebDavCredential(
                    source.Id,
                    credential.UserName,
                    credential.Password));
        }

        var settings = AppSettingsStore.Current;
        var portableSettings = new PortableAppSettings(
            settings.PrimarySubtitleVerticalPosition,
            settings.SecondarySubtitleVerticalPosition,
            settings.PrimarySubtitleBackgroundOpacity,
            settings.SecondarySubtitleBackgroundOpacity,
            settings.MetadataAutoScrapeOnScan,
            settings.MetadataArtworkEnrichment,
            settings.AutoPlayNextEpisode,
            settings.FullscreenControlsTimeoutSeconds,
            settings.RememberPlaybackRate,
            settings.DefaultPlaybackRate,
            settings.LastPlaybackRate,
            settings.RememberSubtitleTrack,
            settings.PreferredAudioLanguage,
            settings.PreferredSubtitleLanguage,
            settings.PreferredSecondarySubtitleLanguage);

        return new ConfigBackupPayload(
            BackupSchemaVersion,
            CurrentAppVersion,
            timestamp,
            portableSettings,
            ThemePreferenceStore.Load().ToString(),
            portableSources,
            _credentials.GetTmdbReadAccessToken(),
            portableCredentials);
    }

    private static void ValidatePayload(ConfigBackupPayload payload)
    {
        if (payload.SchemaVersion != BackupSchemaVersion ||
            payload.AppSettings is null ||
            payload.MediaSources is null ||
            payload.WebDavCredentials is null)
        {
            throw new ConfigBackupException(
                "UnsupportedBackup",
                "The Eikura configuration backup payload is not supported by this version.");
        }

        foreach (var source in payload.MediaSources)
        {
            if (string.IsNullOrWhiteSpace(source.Id) ||
                string.IsNullOrWhiteSpace(source.DisplayName) ||
                string.IsNullOrWhiteSpace(source.RootLocation))
            {
                throw new ConfigBackupException(
                    "InvalidBackup",
                    "The Eikura configuration backup contains an invalid media source.");
            }

            if (source.Kind == MediaSourceKind.WebDav)
            {
                if (!Uri.TryCreate(
                        source.RootLocation,
                        UriKind.Absolute,
                        out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp &&
                     uri.Scheme != Uri.UriSchemeHttps) ||
                    !string.IsNullOrEmpty(uri.UserInfo))
                {
                    throw new ConfigBackupException(
                        "InvalidBackup",
                        "The Eikura configuration backup contains an invalid WebDAV source.");
                }
            }
        }
    }

    private int RestoreSources(
        MediaSourceDefinition destination,
        IReadOnlyList<PortableMediaSource> backupSources,
        IReadOnlyList<PortableWebDavCredential> backupCredentials)
    {
        var credentialMap = backupCredentials
            .GroupBy(static credential => credential.SourceId, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Last(),
                StringComparer.Ordinal);

        // Preserve the selected destination even when restoring a backup produced
        // from an older source list. Its locally stored credential is intentionally
        // not carried inside the backup file.
        foreach (var current in _sources.Snapshot())
        {
            if (!current.IsBuiltIn &&
                !string.Equals(
                    current.Id,
                    destination.Id,
                    StringComparison.Ordinal))
            {
                _sources.Remove(current.Id);
            }
        }

        var skippedLocalSources = 0;

        foreach (var source in backupSources)
        {
            if (!source.Enabled)
                continue;

            if (source.Kind == MediaSourceKind.Local)
            {
                try
                {
                    if (!Directory.Exists(source.RootLocation))
                    {
                        skippedLocalSources++;
                        continue;
                    }

                    _sources.AddLocalFolder(
                        source.RootLocation!,
                        source.DisplayName);
                }
                catch (Exception exception)
                    when (exception is ArgumentException or
                        IOException or
                        UnauthorizedAccessException or
                        NotSupportedException)
                {
                    skippedLocalSources++;
                }

                continue;
            }

            var rootUri = new Uri(source.RootLocation!, UriKind.Absolute);
            var restoredId = MediaSourceStore.BuildWebDavSourceId(
                rootUri,
                source.UserName);
            var credentialKey = source.HasCredential
                ? MediaCredentialStore.BuildWebDavCredentialKey(restoredId)
                : null;

            var restored = _sources.AddWebDav(
                source.DisplayName,
                rootUri,
                source.UserName,
                credentialKey,
                source.SelectedPaths);

            if (credentialMap.TryGetValue(source.Id, out var credential))
            {
                _credentials.SaveWebDav(
                    restored.Id,
                    credential.UserName,
                    credential.Password);
            }
        }

        return skippedLocalSources;
    }

    private static void RestoreAppSettings(PortableAppSettings restored)
    {
        AppSettingsStore.Update(current =>
            current with
            {
                PrimarySubtitleVerticalPosition = Math.Clamp(
                    restored.PrimarySubtitleVerticalPosition,
                    0d,
                    90d),
                SecondarySubtitleVerticalPosition = Math.Clamp(
                    restored.SecondarySubtitleVerticalPosition,
                    0d,
                    90d),
                PrimarySubtitleBackgroundOpacity = Math.Clamp(
                    restored.PrimarySubtitleBackgroundOpacity,
                    0d,
                    100d),
                SecondarySubtitleBackgroundOpacity = Math.Clamp(
                    restored.SecondarySubtitleBackgroundOpacity,
                    0d,
                    100d),
                MetadataAutoScrapeOnScan = restored.MetadataAutoScrapeOnScan,
                MetadataArtworkEnrichment = restored.MetadataArtworkEnrichment,
                AutoPlayNextEpisode = restored.AutoPlayNextEpisode,
                FullscreenControlsTimeoutSeconds =
                    AppSettingsStore.NormalizeFullscreenControlsTimeout(
                        restored.FullscreenControlsTimeoutSeconds),
                RememberPlaybackRate = restored.RememberPlaybackRate,
                DefaultPlaybackRate = restored.DefaultPlaybackRate is >= 0.5d and <= 2d
                    ? restored.DefaultPlaybackRate
                    : 1d,
                LastPlaybackRate = restored.LastPlaybackRate is >= 0.5d and <= 2d
                    ? restored.LastPlaybackRate
                    : 1d,
                RememberSubtitleTrack = restored.RememberSubtitleTrack,
                PreferredAudioLanguage = NormalizeLanguagePreference(
                    restored.PreferredAudioLanguage),
                PreferredSubtitleLanguage = NormalizeLanguagePreference(
                    restored.PreferredSubtitleLanguage),
                PreferredSecondarySubtitleLanguage = NormalizeLanguagePreference(
                    restored.PreferredSecondarySubtitleLanguage)
            });
    }

    private static string NormalizeLanguagePreference(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "ja" => "ja",
            "zh" => "zh",
            "en" => "en",
            _ => "auto"
        };

    private static void RestoreTheme(string? theme)
    {
        ThemePreferenceStore.Save(
            theme?.Trim() switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default
            });
    }

    private HttpClient CreateClient(MediaSourceDefinition destination)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression =
                DecompressionMethods.GZip |
                DecompressionMethods.Deflate,
            PreAuthenticate = true
        };

        if (_credentials.GetWebDav(destination) is { } credential)
        {
            handler.Credentials = new NetworkCredential(
                credential.UserName,
                credential.Password);
        }

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(90)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"Eikura/{CurrentAppVersion}");
        return client;
    }

    private static Uri BuildBackupUri(MediaSourceDefinition destination)
    {
        var rootUri = new Uri(destination.RootLocation!, UriKind.Absolute);
        var builder = new UriBuilder(rootUri)
        {
            Fragment = string.Empty
        };
        if (!builder.Path.EndsWith("/", StringComparison.Ordinal))
            builder.Path += "/";

        return new Uri(builder.Uri, BackupFileName);
    }

    private static void ValidateDestination(MediaSourceDefinition destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (destination.Kind != MediaSourceKind.WebDav ||
            string.IsNullOrWhiteSpace(destination.RootLocation) ||
            !Uri.TryCreate(
                destination.RootLocation,
                UriKind.Absolute,
                out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp &&
             uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ConfigBackupException(
                "InvalidDestination",
                "Select a valid WebDAV media source for configuration backup.");
        }
    }

    private string GetWebDavEncryptionPassword(MediaSourceDefinition destination)
    {
        var password = _credentials.GetWebDav(destination)?.Password;
        if (string.IsNullOrEmpty(password))
            throw new ConfigBackupException("MissingWebDavPassword", "Enter the WebDAV password.");
        return password;
    }
    private static ConfigBackupException BuildHttpFailure(
        string fallbackCode,
        HttpResponseMessage response,
        string message)
    {
        var code = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or
            HttpStatusCode.Forbidden => "AuthenticationFailed",
            _ => fallbackCode
        };

        return new ConfigBackupException(
            code,
            $"{message}: {(int)response.StatusCode} {response.ReasonPhrase}.");
    }

    private sealed record ConfigBackupPayload(
        int SchemaVersion,
        string AppVersion,
        DateTimeOffset CreatedAtUtc,
        PortableAppSettings AppSettings,
        string Theme,
        List<PortableMediaSource> MediaSources,
        string? TmdbReadAccessToken,
        List<PortableWebDavCredential> WebDavCredentials);

    private sealed record PortableAppSettings(
        double PrimarySubtitleVerticalPosition,
        double SecondarySubtitleVerticalPosition,
        double PrimarySubtitleBackgroundOpacity,
        double SecondarySubtitleBackgroundOpacity,
        bool MetadataAutoScrapeOnScan,
        bool MetadataArtworkEnrichment,
        bool AutoPlayNextEpisode,
        int FullscreenControlsTimeoutSeconds,
        bool RememberPlaybackRate,
        double DefaultPlaybackRate,
        double LastPlaybackRate,
        bool RememberSubtitleTrack,
        string PreferredAudioLanguage,
        string PreferredSubtitleLanguage,
        string PreferredSecondarySubtitleLanguage);

    private sealed record PortableMediaSource(
        string Id,
        MediaSourceKind Kind,
        string DisplayName,
        string? RootLocation,
        string? UserName,
        bool Enabled,
        bool HasCredential,
        List<string>? SelectedPaths);

    private sealed record PortableWebDavCredential(
        string SourceId,
        string UserName,
        string Password);
}
