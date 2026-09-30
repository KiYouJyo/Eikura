using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Eizo.Views;

public sealed partial class BangumiPublicView
{
    private async void PageScrollViewer_ViewChanged(
        object sender,
        ScrollViewerViewChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer ||
            _kind != BangumiPublicPageKind.Discover ||
            DiscoverScope == BangumiDiscoverScope.CurrentSeason ||
            LoadingRing.IsActive ||
            _items.Count == 0 ||
            LoadMoreButton.Visibility != Visibility.Visible)
        {
            return;
        }

        const double preloadDistance = 520d;
        if (scrollViewer.VerticalOffset <
            Math.Max(
                0d,
                scrollViewer.ScrollableHeight - preloadDistance))
        {
            return;
        }

        // Hide the internal has-more sentinel immediately so repeated ViewChanged
        // notifications cannot start duplicate page requests before SetBusy runs.
        LoadMoreButton.Visibility = Visibility.Collapsed;

        await LoadDiscoverAsync(
            forceRefresh: false,
            append: true);

        // LoadDiscoverAsync reports append errors through StatusText. Restore the
        // sentinel in that case so another bottom reach can retry the same page.
        if (string.Equals(
                StatusText.Text,
                T("Bangumi_LoadMoreError"),
                StringComparison.Ordinal))
        {
            LoadMoreButton.Visibility = Visibility.Visible;
        }
    }
}
