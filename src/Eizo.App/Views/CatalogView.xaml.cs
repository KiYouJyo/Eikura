using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Eizo.Localization;
using Eizo.Models;
using Eizo.Recognition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace Eizo.Views;

public sealed partial class CatalogView : UserControl
{
    private readonly AppLocalizationService _localization = AppLocalizationService.Default;
    private readonly MediaCatalogStore _catalog = MediaCatalogStore.Default;
    private readonly MediaSourceStore _sources = MediaSourceStore.Default;
    private readonly MediaCategoryKind? _categoryFilter;
    private readonly DispatcherQueueTimer _searchTimer;

    public CatalogView()
        : this(categoryFilter: null)
    {
    }

    public CatalogView(MediaCategoryKind categoryFilter)
        : this((MediaCategoryKind?)categoryFilter)
    {
    }

    private CatalogView(MediaCategoryKind? categoryFilter)
    {
        _categoryFilter = categoryFilter;
        InitializeComponent();

        _searchTimer = DispatcherQueue.CreateTimer();
        _searchTimer.Interval = TimeSpan.FromMilliseconds(180);
        _searchTimer.IsRepeating = false;
        _searchTimer.Tick += SearchTimer_Tick;

        ApplyText();
        Loaded += CatalogView_Loaded;
        Unloaded += CatalogView_Unloaded;
    }

    public event EventHandler<CatalogMediaItemModel>? MediaRequested;
    public event EventHandler<CatalogSubjectModel>? SubjectRequested;

    private string T(string key) => _localization.GetString(key);

    private string L(string zhCn, string jaJp, string enUs) =>
        _localization.CurrentLanguage switch
        {
            "ja-JP" => jaJp,
            "en-US" => enUs,
            _ => zhCn
        };

    private void ApplyText()
    {
        PageTitle.Text = _categoryFilter switch
        {
            MediaCategoryKind.Anime => T("Nav_Anime"),
            MediaCategoryKind.Movies => T("Nav_Movies"),
            MediaCategoryKind.Series => T("Nav_Series"),
            _ => T("Nav_Library")
        };
        PageSubtitle.Text = T("Catalog_Subtitle");
        SearchBox.PlaceholderText = T("Catalog_SearchPlaceholder");
        ClearSearchButton.Content = T("Catalog_Clear");
        ExportRecognitionButton.Content = L(
            "导出识别报告",
            "認識レポートを出力",
            "Export recognition report");
    }

    private void CatalogView_Loaded(object sender, RoutedEventArgs e)
    {
        _catalog.Changed -= Catalog_Changed;
        _catalog.Changed += Catalog_Changed;
        RefreshResults();
    }

    private void CatalogView_Unloaded(object sender, RoutedEventArgs e)
    {
        _catalog.Changed -= Catalog_Changed;
        _searchTimer.Stop();
    }

    private void Catalog_Changed(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(RefreshResults);

    private void SearchBox_TextChanged(
        AutoSuggestBox sender,
        AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.ProgrammaticChange)
            return;

        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void SearchTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        RefreshResults();
    }

    private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
    {
        _searchTimer.Stop();
        SearchBox.Text = string.Empty;
        RefreshResults();
    }

    private void RefreshResults()
    {
        if (ResultsList is null)
            return;

        var query = SearchBox?.Text?.Trim() ?? string.Empty;
        var snapshot = _catalog.SnapshotForDisplay();
        var aggregation = CatalogSubjectAggregator.Build(snapshot);
        var displayEntries = aggregation.Subjects
            .Select(static subject =>
                CatalogDisplayEntry.FromSubject(subject))
            .Concat(
                aggregation.StandaloneItems.Select(static item =>
                    CatalogDisplayEntry.FromItem(item)))
            .Where(entry =>
                _categoryFilter is null ||
                entry.Category == _categoryFilter)
            .Where(entry => Matches(entry, query))
            .OrderBy(entry => CategoryOrder(entry.Category))
            .ThenBy(PreferredMetadataOrder)
            .ThenBy(entry => entry.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        if (displayEntries.Length == 0)
        {
            ((CollectionViewSource)Resources["GroupedCatalogItems"]).Source =
                Array.Empty<CatalogGroup>();
            EmptyStateText.Text = string.IsNullOrWhiteSpace(query)
                ? T("Catalog_Empty")
                : T("Catalog_NoResults");
            EmptyStateText.Visibility = Visibility.Visible;
            ResultsList.Visibility = Visibility.Collapsed;
            return;
        }

        var sourceLabels = _sources
            .Snapshot()
            .ToDictionary(
                source => source.Id,
                source => source.IsBuiltIn
                    ? T("Sources_OpenedLocalFiles")
                    : source.DisplayName,
                StringComparer.Ordinal);

        var groups = displayEntries
            .GroupBy(entry => entry.Category, CatalogCategoryComparer.Default)
            .Select(group =>
                new CatalogGroup(
                    CategoryLabel(group.Key),
                    group.Select(entry =>
                        entry.Subject is { } subject
                            ? CreateSubjectListItem(subject)
                            : CreateListItem(entry.Item!, sourceLabels))))
            .ToArray();

        ((CollectionViewSource)Resources["GroupedCatalogItems"]).Source = groups;
        EmptyStateText.Visibility = Visibility.Collapsed;
        ResultsList.Visibility = Visibility.Visible;
    }

    private CatalogListItemViewModel CreateSubjectListItem(
        CatalogSubjectModel subject)
    {
        var presentation =
            CatalogSubjectPresentation.Create(subject);

        var metaParts = new List<string>();
        if (presentation.ReleaseYear is { } year)
        {
            metaParts.Add(
                year.ToString(
                    CultureInfo.InvariantCulture));
        }

        metaParts.Add(
            presentation.IsMovie
                ? L("电影", "映画", "Movie")
                : L(
                    $"{presentation.EpisodeCount} 集",
                    $"{presentation.EpisodeCount} 話",
                    $"{presentation.EpisodeCount} episodes"));

        if (presentation.Genres.Count > 0)
        {
            metaParts.Add(
                string.Join(
                    " / ",
                    presentation.Genres.Take(2)));
        }

        var sourceKind =
            (presentation.HasLocalSource,
             presentation.HasRemoteSource) switch
            {
                (true, true) =>
                    L("本地 + 网盘", "ローカル + リモート", "Local + remote"),
                (true, false) =>
                    L("本地", "ローカル", "Local"),
                (false, true) =>
                    L("网盘", "リモート", "Remote"),
                _ =>
                    L("未知来源", "不明なソース", "Unknown source"),
            };

        var sourceLabel =
            presentation.SourceCount > 0
                ? L(
                    $"{sourceKind} · {presentation.SourceCount} 个来源",
                    $"{sourceKind} · {presentation.SourceCount} ソース",
                    $"{sourceKind} · {presentation.SourceCount} sources")
                : sourceKind;

        return new CatalogListItemViewModel(
            subject,
            null,
            subject.Category,
            subject.Category switch
            {
                MediaCategoryKind.Anime => "\uE8B2",
                MediaCategoryKind.Series => "\uE8FD",
                MediaCategoryKind.Movies => "\uE714",
                _ => "\uE8FD",
            },
            CreateArtwork(
                presentation.PosterUrl,
                360),
            presentation.Title,
            presentation.SecondaryTitle,
            string.Join(" · ", metaParts),
            sourceLabel,
            presentation.IsMovie
                ? L("电影", "映画", "Movie")
                : L(
                    $"{presentation.EpisodeCount} 集",
                    $"{presentation.EpisodeCount} 話",
                    $"{presentation.EpisodeCount} eps"));
    }

    private CatalogListItemViewModel CreateListItem(
        CatalogMediaItemModel item,
        IReadOnlyDictionary<string, string> sourceLabels)
    {
        var category = CatalogCategoryClassifier.Resolve(item);

        var subtitle = item.IsParsed &&
                       !string.IsNullOrWhiteSpace(item.SecondaryTitle) &&
                       !string.Equals(
                           item.SecondaryTitle,
                           item.DisplayTitle,
                           StringComparison.CurrentCultureIgnoreCase)
            ? item.SecondaryTitle
            : string.Empty;

        var metaParts = new List<string>();
        if (item.Metadata is { IsResolved: true } metadata &&
            DateOnly.TryParse(metadata.ReleaseDate, out var releaseDate))
        {
            metaParts.Add(releaseDate.Year.ToString(CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(item.Meta))
        {
            metaParts.Add(item.Meta);
        }

        var sourceLabel = string.Empty;
        if (item.Location is { } location)
        {
            var extensionSource = location.Locator;
            if (location.Kind == MediaLocationKind.RemoteUri &&
                Uri.TryCreate(location.Locator, UriKind.Absolute, out var remoteUri))
            {
                extensionSource = Uri.UnescapeDataString(remoteUri.AbsolutePath);
            }

            var locationParts = new List<string>();
            var extension = Path.GetExtension(extensionSource)
                .TrimStart('.')
                .ToUpperInvariant();

            if (!string.IsNullOrWhiteSpace(extension))
            {
                locationParts.Add(extension);
            }

            if (location.SizeBytes is > 0)
            {
                locationParts.Add(FormatBytes(location.SizeBytes.Value));
            }

            if (sourceLabels.TryGetValue(location.SourceId, out var label))
            {
                locationParts.Add(label);
            }

            sourceLabel = string.Join(" · ", locationParts);
        }

        if (!item.IsParsed && metaParts.Count == 0)
        {
            metaParts.Add(T("Catalog_Unparsed"));
        }

        return new CatalogListItemViewModel(
            null,
            item,
            category,
            category switch
            {
                MediaCategoryKind.Anime => "\uE8B2",
                MediaCategoryKind.Series => "\uE8FD",
                MediaCategoryKind.Movies => "\uE714",
                _ => "\uE8A5"
            },
            CreateArtwork(item.Metadata?.PosterUrl, 360),
            item.DisplayTitle,
            subtitle,
            string.Join(" · ", metaParts),
            sourceLabel,
            CategoryLabel(category));
    }

    private static BitmapImage? CreateArtwork(
        string? url,
        int decodePixelWidth)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        try
        {
            return new BitmapImage
            {
                UriSource = uri,
                DecodePixelWidth = decodePixelWidth,
            };
        }
        catch
        {
            return null;
        }
    }

    private void ResultsList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not CatalogListItemViewModel viewModel)
        {
            return;
        }

        if (viewModel.Subject is { } subject)
        {
            SubjectRequested?.Invoke(this, subject);
            return;
        }

        if (viewModel.Item is { } item)
        {
            MediaRequested?.Invoke(this, item);
        }
    }

    private void ResultsList_ContainerContentChanging(
        ListViewBase sender,
        ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer is not GridViewItem container ||
            args.Item is not CatalogListItemViewModel viewModel)
        {
            return;
        }

        var flyout = new MenuFlyout();

        if (viewModel.Subject is { } subject)
        {
            AddSubjectMetadataActions(
                flyout,
                subject);
        }

        container.ContextFlyout =
            flyout.Items.Count > 0
                ? flyout
                : null;
    }

    private void AddSubjectMetadataActions(
        MenuFlyout flyout,
        CatalogSubjectModel subject)
    {
        var refreshItem = new MenuFlyoutItem
        {
            Text = L(
                "重新刮削",
                "メタデータを再取得",
                "Re-scrape metadata"),
            Icon = new FontIcon
            {
                Glyph = "\uE72C",
            },
            Tag = subject,
        };
        refreshItem.Click += SubjectRefresh_Click;
        flyout.Items.Add(refreshItem);

        var matchItem = new MenuFlyoutItem
        {
            Text = L(
                "手动匹配…",
                "手動で照合…",
                "Manual match…"),
            Icon = new FontIcon
            {
                Glyph = "\uE8A7",
            },
            Tag = subject,
        };
        matchItem.Click += SubjectManualMatch_Click;
        flyout.Items.Add(matchItem);

        var binding =
            CatalogSubjectMetadataActions.GetBinding(
                subject);
        if (binding is
            {
                IsManual: true,
                PrimaryProvider.Length: > 0,
            })
        {
            var clearItem = new MenuFlyoutItem
            {
                Text = L(
                    "清除手动匹配",
                    "手動照合を解除",
                    "Clear manual match"),
                Icon = new FontIcon
                {
                    Glyph = "\uE711",
                },
                Tag = new SubjectProviderTag(
                    subject,
                    binding.PrimaryProvider),
            };
            clearItem.Click +=
                SubjectClearManualMatch_Click;
            flyout.Items.Add(clearItem);
        }
    }

    private async void SubjectRefresh_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem
            {
                Tag: CatalogSubjectModel subject,
            })
        {
            return;
        }

        ShowActionStatus(
            L("正在重新刮削此作品…", "この作品のメタデータを再取得しています…", "Refreshing this title…"),
            InfoBarSeverity.Informational);

        try
        {
            var refreshed =
                await CatalogSubjectMetadataActions
                    .RefreshAsync(subject);

            ShowActionStatus(
                refreshed is null
                    ? L("重新刮削已完成，但未找到更新后的作品。", "再取得は完了しましたが、更新後の作品を確認できませんでした。", "Refresh finished, but the updated title could not be found.")
                    : L("作品信息已更新。", "作品情報を更新しました。", "Title metadata updated."),
                refreshed is null
                    ? InfoBarSeverity.Warning
                    : InfoBarSeverity.Success);
        }
        catch
        {
            ShowActionStatus(
                L("重新刮削失败，请稍后重试。", "メタデータの再取得に失敗しました。後でもう一度お試しください。", "Metadata refresh failed. Try again later."),
                InfoBarSeverity.Error);
        }
    }

    private async void SubjectManualMatch_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (XamlRoot is null ||
            sender is not MenuFlyoutItem
            {
                Tag: CatalogSubjectModel subject,
            })
        {
            return;
        }

        var candidate =
            await MetadataMatchDialog.ShowAsync(
                XamlRoot,
                subject);
        if (candidate is null)
            return;

        ShowActionStatus(
            L("正在应用手动匹配…", "手動照合を適用しています…", "Applying manual match…"),
            InfoBarSeverity.Informational);

        try
        {
            var refreshed =
                await CatalogSubjectMetadataActions
                    .ApplyManualMatchAsync(
                        subject,
                        candidate);

            ShowActionStatus(
                refreshed is null
                    ? L("匹配已保存，但未找到更新后的作品。", "照合は保存されましたが、更新後の作品を確認できませんでした。", "The match was saved, but the updated title could not be found.")
                    : L("手动匹配已应用。", "手動照合を適用しました。", "Manual match applied."),
                refreshed is null
                    ? InfoBarSeverity.Warning
                    : InfoBarSeverity.Success);
        }
        catch
        {
            ShowActionStatus(
                L("应用手动匹配失败，请稍后重试。", "手動照合の適用に失敗しました。後でもう一度お試しください。", "Could not apply the manual match. Try again later."),
                InfoBarSeverity.Error);
        }
    }

    private async void SubjectClearManualMatch_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem
            {
                Tag: SubjectProviderTag tag,
            })
        {
            return;
        }

        ShowActionStatus(
            L("正在清除手动匹配…", "手動照合を解除しています…", "Clearing manual match…"),
            InfoBarSeverity.Informational);

        try
        {
            var refreshed =
                await CatalogSubjectMetadataActions
                    .ClearManualMatchAsync(
                        tag.Subject,
                        tag.Provider);

            ShowActionStatus(
                refreshed is null
                    ? L("手动匹配已清除，但未找到更新后的作品。", "手動照合は解除されましたが、更新後の作品を確認できませんでした。", "The manual match was cleared, but the updated title could not be found.")
                    : L("手动匹配已清除。", "手動照合を解除しました。", "Manual match cleared."),
                refreshed is null
                    ? InfoBarSeverity.Warning
                    : InfoBarSeverity.Success);
        }
        catch
        {
            ShowActionStatus(
                L("清除手动匹配失败，请稍后重试。", "手動照合の解除に失敗しました。後でもう一度お試しください。", "Could not clear the manual match. Try again later."),
                InfoBarSeverity.Error);
        }
    }

    private void ShowActionStatus(
        string message,
        InfoBarSeverity severity)
    {
        ActionStatusBar.Message = message;
        ActionStatusBar.Severity = severity;
        ActionStatusBar.IsOpen = true;
    }

    private async void ExportRecognitionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (App.MainWindow is null)
            return;

        await _catalog.EnsureRecognitionRuntimeCurrentAsync();
        var snapshot = _catalog.SnapshotForDisplay();
        if (snapshot.Count == 0)
            return;

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"Eikura-Recognition-Report-{DateTime.Now:yyyyMMdd-HHmmss}"
        };
        picker.FileTypeChoices.Add(
            "CSV",
            new List<string> { ".csv" });

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSaveFileAsync();
        if (file is null)
            return;

        var sourceLabels = _sources
            .Snapshot()
            .ToDictionary(
                source => source.Id,
                source => source.IsBuiltIn
                    ? T("Sources_OpenedLocalFiles")
                    : source.DisplayName,
                StringComparer.Ordinal);

        var csv = await Task.Run(
            () => BuildRecognitionCsv(
                snapshot,
                sourceLabels,
                aggregation: CatalogSubjectAggregator.Build(snapshot)));

        await FileIO.WriteTextAsync(file, csv, UnicodeEncoding.Utf8);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = L("识别报告已导出", "認識レポートを出力しました", "Recognition report exported"),
            Content = L(
                "CSV 已同时包含 Eikura 内部媒体模型、Recognition 与 Metadata 诊断：可核对 Eikura Media ID、作品聚合 ID、GroupingKey、MediaFormat、ContentDomain、External IDs，以及 Provider 搜索词、候选分数和解析原因。",
                "CSV には Eikura 内部メディアモデル、Recognition、Metadata の診断情報を統合しています。Eikura Media ID、作品 ID、GroupingKey、MediaFormat、ContentDomain、External IDs と Provider 診断を確認できます。",
                "The CSV combines the Eikura internal media model with Recognition and Metadata diagnostics, including Eikura media/subject IDs, grouping evidence, format, domain, external IDs and provider resolution details."),
            CloseButtonText = L("关闭", "閉じる", "Close")
        };
        await dialog.ShowAsync();
    }

    private static string BuildRecognitionCsv(
        IReadOnlyList<CatalogMediaItemModel> items,
        IReadOnlyDictionary<string, string> sourceLabels,
        CatalogLibraryAggregation aggregation)
    {
        var subjectByItemKey = aggregation.Subjects
            .SelectMany(subject => subject.Items.Select(item =>
                new
                {
                    ItemKey = MediaCatalogStore.ItemKey(item),
                    Subject = subject,
                }))
            .GroupBy(static value => value.ItemKey, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.First().Subject,
                StringComparer.Ordinal);

        var builder = new StringBuilder();
        builder.AppendLine(
            "NeedsReview,ReviewPriority,ReviewReason,EikuraItemMediaId,EikuraItemFormat,EikuraItemDomain,EikuraItemPrimaryOrigin,EikuraItemOrigins,EikuraItemExternalIds,EikuraSubjectId,EikuraSubjectGroupingKey,EikuraSubjectGroupingBasis,EikuraSubjectFormat,EikuraSubjectDomain,EikuraSubjectExternalIds,ProviderSeasonId,ProviderEpisodeId,MetadataEpisodeSeason,SeasonTitle,SeasonAirDate,SeasonPosterUrl,Genres,RuntimeMinutes,ProductionCompanies,OriginCountryCodes,ProductionStatus,OriginalLanguage,CastCount,CrewCount,RoutingPrimaryProvider,RoutingFallbackProviders,RoutingReason,SeasonExternalIds,EpisodeExternalIds,MergeProfile,MergeContributors,FieldSources,IdentityBindingProvider,IdentityBindingManual,MetadataRefreshState,LastRefreshAttemptUtc,RefreshErrors,RuntimeVersion,Source,OriginalName,LogicalPath,Status,ConfidenceLevel,Confidence,IsAmbiguous,AppliedDisplayTitle,RecognizedTitle,EpisodeTitle,MediaKind,SpecialKind,EpisodePart,IsFinalEpisode,Season,Cour,Episode,EpisodeEnd,Special,Year,ErrorCode,TitleCandidates,Evidence,MetadataRuntimeVersion,MetadataRecognitionRuntimeVersion,MetadataRecognitionRuntimeMatch,MetadataStatus,MetadataResolutionReason,MetadataSearchTitles,MetadataCandidateCount,MetadataAutoResolveThreshold,MetadataMinimumLead,MetadataBestScore,MetadataSecondScore,MetadataLead,MetadataTopCandidates,MetadataProvider,MetadataSubjectId,MetadataSubjectKind,MetadataContentKind,MetadataConfidence,MetadataCanonicalTitle,MetadataOriginalTitle,MetadataLocalizedTitles,MetadataAliases,MetadataReleaseDate,MetadataEpisodeCount,MetadataEpisodeNumber,MetadataEpisodeTitle,MetadataEpisodeOriginalTitle,MetadataEpisodeAirDate,MetadataPosterUrl,MetadataBackdropUrl,MetadataExternalIds,MetadataErrors,MetadataUpdatedAtUtc");

        foreach (var item in items)
        {
            var recognition = item.Recognition;
            var source = item.Location is { } location &&
                         sourceLabels.TryGetValue(location.SourceId, out var sourceLabel)
                ? sourceLabel
                : string.Empty;

            var reviewPriority = ReviewPriority(recognition);
            var needsReview = !string.IsNullOrEmpty(reviewPriority);
            var titleCandidates = recognition is null
                ? string.Empty
                : string.Join(
                    " || ",
                    recognition.TitleCandidates.Select(candidate =>
                        $"{candidate.Title} [{candidate.Confidence:0.000}; {candidate.Source}; primary={candidate.IsPrimary}]"));
            var evidence = recognition is null
                ? string.Empty
                : string.Join(
                    " || ",
                    recognition.Evidence.Select(itemEvidence =>
                        $"{itemEvidence.Code}={itemEvidence.Value ?? "-"} [{itemEvidence.Weight:0.000}]"));

            var media = item.Media;
            var itemExternalIds = media is null
                ? string.Empty
                : string.Join(
                    " || ",
                    media.ExternalIds
                        .OrderBy(static pair => pair.Key)
                        .Select(static pair => $"{pair.Key}={pair.Value}"));
            var itemOrigins = media is null
                ? string.Empty
                : string.Join(
                    " || ",
                    media.Origin.CountryCodes
                        .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase));

            subjectByItemKey.TryGetValue(
                MediaCatalogStore.ItemKey(item),
                out var subject);
            var subjectExternalIds = subject is null
                ? string.Empty
                : string.Join(
                    " || ",
                    subject.Media.ExternalIds
                        .OrderBy(static pair => pair.Key)
                        .Select(static pair => $"{pair.Key}={pair.Value}"));

            var metadata = item.Metadata;
            var metadataLocalizedTitles = metadata is null
                ? string.Empty
                : string.Join(
                    " || ",
                    metadata.LocalizedTitles
                        .OrderBy(static pair => pair.Key)
                        .Select(static pair => $"{pair.Key}={pair.Value}"));
            var metadataAliases = metadata is null
                ? string.Empty
                : string.Join(" || ", metadata.Aliases);
            var metadataExternalIds = metadata is null
                ? string.Empty
                : string.Join(
                    " || ",
                    metadata.ExternalIds
                        .OrderBy(static pair => pair.Key)
                        .Select(static pair => $"{pair.Key}={pair.Value}"));
            var metadataErrors = metadata is null
                ? string.Empty
                : string.Join(
                    " || ",
                    metadata.Errors.Select(static error =>
                        $"{error.Provider}|{error.ErrorType}|{error.Message}"));
            var metadataStatus = metadata?.Status.ToString()
                ?? MetadataDiagnosticState(recognition);
            var metadataSearchTitles = metadata is null
                ? string.Empty
                : string.Join(" || ", metadata.SearchTitles);
            var metadataTopCandidates = metadata is null
                ? string.Empty
                : string.Join(
                    " || ",
                    metadata.TopCandidates.Select(static candidate =>
                        $"{candidate.Provider}:{candidate.ProviderSubjectId}|{candidate.SubjectKind}|{candidate.Title}|year={candidate.Year?.ToString(CultureInfo.InvariantCulture) ?? "-"}|rank={candidate.ProviderRank}|score={candidate.Score:0.000}|evidence={string.Join(";", candidate.Evidence)}"));

            AppendCsvRow(
                builder,
                needsReview ? "true" : "false",
                reviewPriority,
                ReviewReason(recognition),
                media?.Id ?? string.Empty,
                media?.Format.ToString() ?? string.Empty,
                media?.Domain.ToString() ?? string.Empty,
                media?.Origin.PrimaryCountryCode ?? string.Empty,
                itemOrigins,
                itemExternalIds,
                subject?.Media.Id ?? string.Empty,
                subject?.GroupingKey ?? string.Empty,
                subject?.GroupingBasis ?? string.Empty,
                subject?.Media.Format.ToString() ?? string.Empty,
                subject?.Media.Domain.ToString() ?? string.Empty,
                subjectExternalIds,
                metadata?.ProviderSeasonId ?? string.Empty,
                metadata?.ProviderEpisodeId ?? string.Empty,
                metadata?.EpisodeSeasonNumber?.ToString(
                    CultureInfo.InvariantCulture) ?? string.Empty,
                metadata?.SeasonTitle ?? string.Empty,
                metadata?.SeasonAirDate ?? string.Empty,
                metadata?.SeasonPosterUrl ?? string.Empty,
                metadata is null
                    ? string.Empty
                    : string.Join(" || ", metadata.Genres),
                metadata?.RuntimeMinutes?.ToString(
                    CultureInfo.InvariantCulture) ?? string.Empty,
                metadata is null
                    ? string.Empty
                    : string.Join(
                        " || ",
                        metadata.ProductionCompanies),
                metadata is null
                    ? string.Empty
                    : string.Join(
                        " || ",
                        metadata.OriginCountryCodes),
                metadata?.ProductionStatus ?? string.Empty,
                metadata?.OriginalLanguage ?? string.Empty,
                metadata is null
                    ? string.Empty
                    : metadata.Cast.Count.ToString(
                        CultureInfo.InvariantCulture),
                metadata is null
                    ? string.Empty
                    : metadata.Crew.Count.ToString(
                        CultureInfo.InvariantCulture),
                metadata?.RoutingPrimaryProvider ?? string.Empty,
                metadata is null
                    ? string.Empty
                    : string.Join(
                        " || ",
                        metadata.RoutingFallbackProviders),
                metadata?.RoutingReason ?? string.Empty,
                metadata is null
                    ? string.Empty
                    : string.Join(
                        " || ",
                        metadata.SeasonExternalIds
                            .OrderBy(static pair => pair.Key)
                            .Select(static pair =>
                                $"{pair.Key}={pair.Value}")),
                metadata is null
                    ? string.Empty
                    : string.Join(
                        " || ",
                        metadata.EpisodeExternalIds
                            .OrderBy(static pair => pair.Key)
                            .Select(static pair =>
                                $"{pair.Key}={pair.Value}")),
                metadata?.MergeProfile ?? string.Empty,
                metadata is null
                    ? string.Empty
                    : string.Join(
                        " || ",
                        metadata.MergeContributors),
                metadata is null
                    ? string.Empty
                    : string.Join(
                        " || ",
                        metadata.FieldSources
                            .OrderBy(static pair => pair.Key)
                            .Select(static pair =>
                                $"{pair.Key}={pair.Value}")),
                metadata?.IdentityBindingProvider ?? string.Empty,
                metadata?.IdentityBindingManual == true
                    ? "true"
                    : "false",
                metadata?.RefreshState ?? string.Empty,
                metadata?.LastRefreshAttemptUtc?.ToString(
                    "O",
                    CultureInfo.InvariantCulture) ?? string.Empty,
                metadata is null
                    ? string.Empty
                    : string.Join(
                        " || ",
                        metadata.RefreshErrors.Select(
                            static error =>
                                $"{error.Provider}:{error.ErrorType}:{error.Message}")),
                recognition?.RuntimeVersion ?? string.Empty,
                source,
                item.SourceTitle,
                recognition?.LogicalPath ?? string.Empty,
                recognition?.Status.ToString() ?? "Missing",
                recognition?.ConfidenceLevel ?? string.Empty,
                recognition?.Confidence.ToString("0.000", CultureInfo.InvariantCulture) ?? string.Empty,
                recognition?.IsAmbiguous.ToString() ?? string.Empty,
                recognition?.ShouldApplyDisplayTitle.ToString() ?? "false",
                recognition?.Title ?? string.Empty,
                recognition?.EpisodeTitle ?? string.Empty,
                recognition?.MediaKind ?? string.Empty,
                recognition?.SpecialKind ?? string.Empty,
                recognition?.EpisodePart ?? string.Empty,
                recognition?.IsFinalEpisode.ToString() ?? string.Empty,
                recognition?.SeasonNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                recognition?.CourNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                recognition is null ? string.Empty : FormatNullableNumber(recognition.EpisodeNumber),
                recognition is null ? string.Empty : FormatNullableNumber(recognition.EpisodeEndNumber),
                recognition is null ? string.Empty : FormatNullableNumber(recognition.SpecialNumber),
                recognition?.Year?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                recognition?.ErrorCode ?? string.Empty,
                titleCandidates,
                evidence,
                metadata?.RuntimeVersion ?? string.Empty,
                metadata?.RecognitionRuntimeVersion ?? string.Empty,
                metadata is null || recognition is null
                    ? string.Empty
                    : metadata.MatchesRecognitionRuntime(recognition.RuntimeVersion).ToString(),
                metadataStatus,
                metadata?.ResolutionReason ?? string.Empty,
                metadataSearchTitles,
                metadata?.CandidateCount.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                metadata?.AutoResolveThreshold.ToString("0.000", CultureInfo.InvariantCulture) ?? string.Empty,
                metadata?.MinimumLead.ToString("0.000", CultureInfo.InvariantCulture) ?? string.Empty,
                metadata?.BestScore?.ToString("0.000", CultureInfo.InvariantCulture) ?? string.Empty,
                metadata?.SecondScore?.ToString("0.000", CultureInfo.InvariantCulture) ?? string.Empty,
                metadata?.Lead?.ToString("0.000", CultureInfo.InvariantCulture) ?? string.Empty,
                metadataTopCandidates,
                metadata?.Provider ?? string.Empty,
                metadata?.ProviderSubjectId ?? string.Empty,
                metadata?.SubjectKind ?? string.Empty,
                metadata?.ContentKind ?? string.Empty,
                metadata?.Confidence.ToString("0.000", CultureInfo.InvariantCulture) ?? string.Empty,
                metadata?.CanonicalTitle ?? string.Empty,
                metadata?.OriginalTitle ?? string.Empty,
                metadataLocalizedTitles,
                metadataAliases,
                metadata?.ReleaseDate ?? string.Empty,
                metadata?.EpisodeCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                metadata is null ? string.Empty : FormatNullableNumber(metadata.EpisodeNumber),
                metadata?.EpisodeTitle ?? string.Empty,
                metadata?.EpisodeOriginalTitle ?? string.Empty,
                metadata?.EpisodeAirDate ?? string.Empty,
                metadata?.PosterUrl ?? string.Empty,
                metadata?.BackdropUrl ?? string.Empty,
                metadataExternalIds,
                metadataErrors,
                metadata?.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty);
        }

        return builder.ToString();
    }

    private static string MetadataDiagnosticState(
        MediaRecognitionSnapshot? recognition)
    {
        if (recognition is null)
            return "NotAttempted:MissingRecognition";

        if (recognition.Status != MediaRecognitionStatus.Recognized ||
            recognition.IsAmbiguous)
        {
            return $"NotAttempted:{recognition.Status}";
        }

        if (recognition.ConfidenceLevel is not ("Medium" or "High"))
            return $"NotAttempted:{recognition.ConfidenceLevel}Confidence";

        if (string.IsNullOrWhiteSpace(recognition.Title))
            return "NotAttempted:MissingTitle";

        return "MissingAfterScan";
    }

    private static void AppendCsvRow(
        StringBuilder builder,
        params string[] values)
    {
        builder.AppendLine(string.Join(',', values.Select(EscapeCsv)));
    }

    private static string EscapeCsv(string value)
    {
        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
            return value;

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static string ReviewPriority(MediaRecognitionSnapshot? recognition)
    {
        if (recognition is null)
            return "High";

        if (recognition.Status is MediaRecognitionStatus.Error or
            MediaRecognitionStatus.Unresolved or
            MediaRecognitionStatus.Ambiguous ||
            recognition.IsAmbiguous)
        {
            return "High";
        }

        if (string.IsNullOrWhiteSpace(recognition.Title) ||
            string.Equals(recognition.ConfidenceLevel, "Low", StringComparison.OrdinalIgnoreCase))
        {
            return "Medium";
        }

        if (string.Equals(recognition.ConfidenceLevel, "Medium", StringComparison.OrdinalIgnoreCase))
            return "Low";

        return string.Empty;
    }

    private static string ReviewReason(MediaRecognitionSnapshot? recognition)
    {
        if (recognition is null)
            return "MissingSnapshot";
        if (recognition.Status == MediaRecognitionStatus.Error)
            return string.IsNullOrWhiteSpace(recognition.ErrorCode)
                ? "Error"
                : $"Error:{recognition.ErrorCode}";
        if (recognition.Status == MediaRecognitionStatus.Unresolved)
            return "Unresolved";
        if (recognition.Status == MediaRecognitionStatus.Ambiguous || recognition.IsAmbiguous)
            return "Ambiguous";
        if (string.IsNullOrWhiteSpace(recognition.Title))
            return "MissingTitle";
        if (string.Equals(recognition.ConfidenceLevel, "Low", StringComparison.OrdinalIgnoreCase))
            return "LowConfidence";
        if (string.Equals(recognition.ConfidenceLevel, "Medium", StringComparison.OrdinalIgnoreCase))
            return "MediumConfidence";
        return string.Empty;
    }

    private static bool Matches(
        CatalogDisplayEntry entry,
        string query)
    {
        if (entry.Subject is { } subject)
        {
            return Matches(subject, query);
        }

        return entry.Item is { } item && Matches(item, query);
    }

    private static bool Matches(
        CatalogSubjectModel subject,
        string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        if (subject.Title.Contains(
                query,
                StringComparison.CurrentCultureIgnoreCase) ||
            subject.NativeTitle.Contains(
                query,
                StringComparison.CurrentCultureIgnoreCase) ||
            subject.Meta.Contains(
                query,
                StringComparison.CurrentCultureIgnoreCase))
        {
            return true;
        }

        if (subject.Metadata is { } metadata &&
            (metadata.LocalizedTitles.Values.Any(title =>
                 title.Contains(
                     query,
                     StringComparison.CurrentCultureIgnoreCase)) ||
             metadata.Aliases.Any(title =>
                 title.Contains(
                     query,
                     StringComparison.CurrentCultureIgnoreCase))))
        {
            return true;
        }

        return subject.Episodes.Any(episode =>
            episode.Title.Contains(
                query,
                StringComparison.CurrentCultureIgnoreCase) ||
            episode.NativeTitle.Contains(
                query,
                StringComparison.CurrentCultureIgnoreCase));
    }

    private static bool Matches(CatalogMediaItemModel item, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;

        return item.DisplayTitle.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
               item.SourceTitle.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
               (!string.IsNullOrWhiteSpace(item.NativeTitle) &&
                item.NativeTitle.Contains(query, StringComparison.CurrentCultureIgnoreCase)) ||
               item.Meta.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
               (item.Metadata is { } metadata &&
                ((!string.IsNullOrWhiteSpace(metadata.CanonicalTitle) &&
                  metadata.CanonicalTitle.Contains(query, StringComparison.CurrentCultureIgnoreCase)) ||
                 (!string.IsNullOrWhiteSpace(metadata.OriginalTitle) &&
                  metadata.OriginalTitle.Contains(query, StringComparison.CurrentCultureIgnoreCase)) ||
                 metadata.LocalizedTitles.Values.Any(title =>
                    title.Contains(query, StringComparison.CurrentCultureIgnoreCase)) ||
                 metadata.Aliases.Any(title =>
                    title.Contains(query, StringComparison.CurrentCultureIgnoreCase)) ||
                 (!string.IsNullOrWhiteSpace(metadata.EpisodeTitle) &&
                  metadata.EpisodeTitle.Contains(query, StringComparison.CurrentCultureIgnoreCase)))) ||
               (item.Recognition is { } recognition &&
                ((!string.IsNullOrWhiteSpace(recognition.Title) &&
                  recognition.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)) ||
                 (!string.IsNullOrWhiteSpace(recognition.EpisodeTitle) &&
                  recognition.EpisodeTitle.Contains(query, StringComparison.CurrentCultureIgnoreCase)) ||
                 recognition.LogicalPath.Contains(query, StringComparison.CurrentCultureIgnoreCase)));
    }

    private string CategoryLabel(MediaCategoryKind? category) => category switch
    {
        MediaCategoryKind.Anime => T("Nav_Anime"),
        MediaCategoryKind.Series => T("Nav_Series"),
        MediaCategoryKind.Movies => T("Nav_Movies"),
        _ => T("Catalog_Unparsed")
    };

    private static int CategoryOrder(MediaCategoryKind? category) => category switch
    {
        MediaCategoryKind.Anime => 0,
        MediaCategoryKind.Series => 1,
        MediaCategoryKind.Movies => 2,
        _ => 3
    };

    private static int PreferredMetadataOrder(CatalogDisplayEntry entry)
    {
        if (entry.Subject is { } subject)
        {
            return subject.Items.Any(HasPreferredMetadata)
                ? 0
                : 1;
        }

        return entry.Item is { } item &&
               HasPreferredMetadata(item)
            ? 0
            : 1;
    }

    private static bool HasPreferredMetadata(CatalogMediaItemModel item) =>
        item.Metadata is { IsResolved: true } metadata &&
        !string.IsNullOrWhiteSpace(metadata.PosterUrl) &&
        (!string.IsNullOrWhiteSpace(metadata.CanonicalTitle) ||
         !string.IsNullOrWhiteSpace(metadata.OriginalTitle) ||
         !string.IsNullOrWhiteSpace(metadata.ReleaseDate));

    private static string FormatNullableNumber(decimal? value) =>
        value?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = Math.Max(0d, bytes);
        var unit = 0;

        while (value >= 1024d && unit < units.Length - 1)
        {
            value /= 1024d;
            unit++;
        }

        return unit == 0
            ? $"{value:0} {units[unit]}"
            : $"{value:0.#} {units[unit]}";
    }

    private sealed record CatalogDisplayEntry(
        CatalogSubjectModel? Subject,
        CatalogMediaItemModel? Item,
        MediaCategoryKind? Category,
        string Title)
    {
        public static CatalogDisplayEntry FromSubject(
            CatalogSubjectModel subject) =>
            new(
                subject,
                null,
                subject.Category,
                subject.Title);

        public static CatalogDisplayEntry FromItem(
            CatalogMediaItemModel item) =>
            new(
                null,
                item,
                CatalogCategoryClassifier.Resolve(item),
                item.DisplayTitle);
    }

    private sealed record SubjectProviderTag(
        CatalogSubjectModel Subject,
        string Provider);

    private sealed record CatalogListItemViewModel(
        CatalogSubjectModel? Subject,
        CatalogMediaItemModel? Item,
        MediaCategoryKind? Category,
        string IconGlyph,
        BitmapImage? Artwork,
        string Title,
        string Subtitle,
        string MetaLine,
        string SourceLabel,
        string TypeLabel);

    private sealed class CatalogGroup : ObservableCollection<CatalogListItemViewModel>
    {
        public CatalogGroup(
            string key,
            IEnumerable<CatalogListItemViewModel> items)
            : base(items)
        {
            Key = key;
        }

        public string Key { get; }
    }

    private sealed class CatalogCategoryComparer : IEqualityComparer<MediaCategoryKind?>
    {
        public static CatalogCategoryComparer Default { get; } = new();

        public bool Equals(MediaCategoryKind? x, MediaCategoryKind? y) => x == y;

        public int GetHashCode(MediaCategoryKind? obj) =>
            obj.HasValue ? (int)obj.Value + 1 : 0;
    }
}
