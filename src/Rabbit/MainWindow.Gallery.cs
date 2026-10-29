using System.Collections;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Rabbit.Models;
using Rabbit.Services;

namespace Rabbit;

public partial class MainWindow
{
    private readonly Dictionary<string, GalleryFeedState> _aggregateFeeds =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _aggregateImageScheduled =
        new(StringComparer.Ordinal);

    private CancellationTokenSource? _aggregateCancellation;
    private bool _aggregateLoading;
    private bool _aggregateInitialized;
    private bool _aggregateImageLoading;
    private int _aggregateGeneration;
    private int _aggregateImageGeneration;
    private GalleryPost? _browserPost;
    private bool _galleryBrowserInitialized;
    private string? _lastGalleryBrowserUri;

    private const string MobileBrowserUserAgent =
        "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/156.0.0.0 Mobile Safari/537.36";

    private const int GalleryPostBatchLimit = 100;

    private void MainTabs_AggregateSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, MainTabs) || !_galleryToolbarReady)
        {
            return;
        }

        if (MainTabs.SelectedIndex == 0)
        {
            QueueAutoTileSizeUpdate();
            return;
        }

        if (MainTabs.SelectedIndex != 1)
        {
            return;
        }

        if (!_aggregateInitialized)
        {
            _ = ReloadAllGalleriesAsync();
        }
    }

    private void GallerySourceSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_galleryToolbarReady)
        {
            return;
        }

        if (GallerySourceCombo.SelectedItem is GallerySource source)
        {
            _settings.SelectedGalleryKey = source.Key;
        }
        else
        {
            _settings.SelectedGalleryKey = null;
        }

        SaveSettings();
    }

    private void GalleryRecommendationFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_galleryToolbarReady || _syncingRecommendationFilter)
        {
            return;
        }

        _galleryMinimumRecommendations = int.TryParse(RecommendFilterBox.Text, out var minimum)
            ? Math.Clamp(minimum, 0, 9999)
            : 0;

        if (GallerySourceCombo.SelectedItem is GallerySource source)
        {
            source.MinimumRecommendations = _galleryMinimumRecommendations;
            SaveGallerySources();
        }

        _galleryView.Refresh();
        UpdateAggregateGalleryStatus();
    }

    private void GallerySortButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag } ||
            !Enum.TryParse<GallerySortMode>(tag, out var mode))
        {
            return;
        }

        _settings.GallerySortMode = mode;
        SaveSettings();

        if (_galleryView.CustomSort is GalleryPostComparer comparer)
        {
            comparer.Mode = mode;
        }

        _galleryView.Refresh();
        UpdateAggregateGalleryStatus();
    }

    private void SelectGallerySort(GallerySortMode mode)
    {
        var button = mode switch
        {
            GallerySortMode.RecommendationGrade => GallerySortRecommendButton,
            GallerySortMode.WeightedViews => GallerySortViewsButton,
            _ => GallerySortTimeButton,
        };

        button.IsChecked = true;
    }

    private void GalleryViewButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag } ||
            !Enum.TryParse<GalleryViewMode>(tag, out var mode))
        {
            return;
        }

        ApplyGalleryView(mode);
        _settings.GalleryViewMode = mode;
        SaveSettings();
    }

    private void SelectGalleryView(GalleryViewMode mode)
    {
        var button = mode == GalleryViewMode.List
            ? GalleryListViewButton
            : GalleryCardViewButton;

        button.IsChecked = true;
        ApplyGalleryView(mode);
    }

    private void ApplyGalleryView(GalleryViewMode mode)
    {
        var selected = GalleryList.SelectedItem ?? GalleryRowList.SelectedItem;
        var showCards = mode == GalleryViewMode.Cards;

        GalleryList.Visibility = showCards ? Visibility.Visible : Visibility.Collapsed;
        GalleryRowList.Visibility = showCards ? Visibility.Collapsed : Visibility.Visible;

        if (selected is not null)
        {
            var target = showCards ? GalleryList : GalleryRowList;
            target.SelectedItem = selected;
            target.ScrollIntoView(selected);
        }
    }

    private void GallerySourceCombo_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        AddGalleryFromSourceBox();
        e.Handled = true;
    }

    private void AddGalleryToAggregate_Click(object sender, RoutedEventArgs e) =>
        AddGalleryFromSourceBox();

    private void AddGalleryFromSourceBox()
    {
        var input = GallerySourceCombo.Text.Trim();
        var selected = GallerySourceCombo.SelectedItem as GallerySource;
        var isSelectedDisplayText = selected is not null &&
            string.Equals(input, selected.DisplayName, StringComparison.Ordinal);
        var parsed = isSelectedDisplayText
            ? selected
            : GallerySource.Parse(input, DcGalleryKind.MinorGallery);

        if (parsed is null)
        {
            GalleryStatusText.Text = "갤러리 ID나 주소를 확인해 주세요. 예: duneawakening";
            return;
        }

        var source = _gallerySources.FirstOrDefault(candidate =>
            string.Equals(candidate.Key, parsed.Key, StringComparison.OrdinalIgnoreCase));

        if (source is null)
        {
            source = parsed;
            _gallerySources.Add(source);
        }

        _galleryToolbarReady = false;
        GallerySourceCombo.SelectedItem = source;
        GallerySourceCombo.Text = source.DisplayName;
        _galleryToolbarReady = true;
        SaveGallerySources();

        if (MainTabs.SelectedIndex == 1)
        {
            _ = ReloadAllGalleriesAsync();
        }
        else
        {
            _aggregateInitialized = false;
        }
    }

    private void RemoveGalleryFromAggregate_Click(object sender, RoutedEventArgs e)
    {
        if (GallerySourceCombo.SelectedItem is not GallerySource source)
        {
            return;
        }

        var index = GallerySourceCombo.SelectedIndex;
        _galleryToolbarReady = false;
        _gallerySources.Remove(source);
        GallerySourceCombo.SelectedItem = _gallerySources.Count > 0
            ? _gallerySources[Math.Clamp(index, 0, _gallerySources.Count - 1)]
            : null;
        _galleryToolbarReady = true;
        SaveGallerySources();

        if (MainTabs.SelectedIndex == 1)
        {
            _ = ReloadAllGalleriesAsync();
        }
        else
        {
            _aggregateInitialized = false;
        }
    }

    private void RefreshAllGalleries_Click(object sender, RoutedEventArgs e)
    {
        if (_gallerySources.Count == 0)
        {
            GalleryStatusText.Text = "추가한 갤러리가 없습니다. 갤러리 ID를 입력해 주세요.";
            return;
        }

        _ = ReloadAllGalleriesAsync();
    }

    private async Task ReloadAllGalleriesAsync()
    {
        if (_gallerySources.Count == 0)
        {
            ClearAggregateGallery();
            return;
        }

        _aggregateCancellation?.Cancel();
        _aggregateCancellation?.Dispose();
        _aggregateCancellation = new CancellationTokenSource();

        _aggregateGeneration++;
        _aggregateImageGeneration++;
        _aggregateLoading = false;
        _aggregateImageLoading = false;
        _aggregateImageScheduled.Clear();
        _aggregateFeeds.Clear();

        foreach (var source in _gallerySources)
        {
            _aggregateFeeds[source.Key] = new GalleryFeedState(source);
        }

        _aggregateInitialized = true;
        _galleryPosts.Clear();
        _galleryView.Refresh();
        UpdateAggregateGalleryStatus("등록된 갤러리의 글을 불러오는 중…");

        await LoadNextPagesForAllGalleriesAsync(
            _aggregateGeneration,
            _aggregateCancellation.Token);
    }

    private async Task LoadNextPagesForAllGalleriesAsync(int generation, CancellationToken token)
    {
        if (_aggregateLoading ||
            generation != _aggregateGeneration ||
            !_aggregateFeeds.Values.Any(state => state.HasMore))
        {
            return;
        }

        _aggregateLoading = true;

        try
        {
            var states = _aggregateFeeds.Values.Where(state => state.HasMore).ToList();
            using var limit = new SemaphoreSlim(2);
            var postsPerGallery = Math.Max(1, GalleryPostBatchLimit / states.Count);

            foreach (var state in states)
            {
                state.BatchLimit = postsPerGallery;
            }

            var loads = states.Select(async state =>
            {
                token.ThrowIfCancellationRequested();
                await limit.WaitAsync(token);

                try
                {
                    UpdateAggregateGalleryStatus(
                        $"{states.Count}개 갤러리 · 다음 페이지를 함께 불러오는 중…");

                    await LoadAggregateFeedPageAsync(state, generation, token);
                    await Task.Delay(350, token);
                }
                finally
                {
                    limit.Release();
                }
            });

            await Task.WhenAll(loads);
        }
        catch (OperationCanceledException)
        {
            // 갤러리 목록을 새로 읽거나 창을 닫았습니다.
        }
        finally
        {
            if (generation == _aggregateGeneration)
            {
                _aggregateLoading = false;
                UpdateAggregateGalleryStatus();

                if (!token.IsCancellationRequested)
                {
                    StartAggregateImageLoader(token, _aggregateImageGeneration);
                }
            }
        }
    }

    private async Task LoadAggregateFeedPageAsync(
        GalleryFeedState state,
        int generation,
        CancellationToken token)
    {
        var added = new List<GalleryPost>();
        var remaining = Math.Max(1, state.BatchLimit);
        remaining -= DrainPendingPosts(state, added, remaining);

        try
        {
            if (remaining > 0)
            {
                var page = state.NextPage;
                var posts = await DcGalleryClient.FetchPostsAsync(state.Source, page, token);

                if (token.IsCancellationRequested || generation != _aggregateGeneration)
                {
                    return;
                }

                var knownKeys = _galleryPosts
                    .Concat(added)
                    .Concat(state.PendingPosts)
                    .Select(post => post.UniqueKey)
                    .ToHashSet(StringComparer.Ordinal);
                var uniquePagePosts = posts
                    .Where(post => knownKeys.Add(post.UniqueKey))
                    .ToList();

                foreach (var post in uniquePagePosts)
                {
                    state.PendingPosts.Enqueue(post);
                }

                state.NextPage = page + 1;
                state.PagesLoaded++;
                state.HasMore =
                    state.PendingPosts.Count > 0 ||
                    (page == 1 && posts.Count > 0) ||
                    uniquePagePosts.Count > 0;
                state.LastError = null;

                DrainPendingPosts(state, added, remaining);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            state.HasMore = state.PendingPosts.Count > 0;
            state.LastError = ex.Message;
        }

        if (added.Count == 0)
        {
            _galleryView.Refresh();
            UpdateAggregateGalleryStatus();
            return;
        }

        foreach (var post in added)
        {
            _galleryPosts.Add(post);
        }

        state.LoadedPostCount += added.Count;
        UpdateGalleryScores(state.Source.Key);
        await LoadCachedThumbnailsAsync(added, token);

        _galleryView.Refresh();
        UpdateAggregateGalleryStatus();
    }

    private static int DrainPendingPosts(
        GalleryFeedState state,
        List<GalleryPost> added,
        int maximum)
    {
        var drained = 0;

        while (drained < maximum && state.PendingPosts.TryDequeue(out var post))
        {
            added.Add(post);
            drained++;
        }

        return drained;
    }

    private void UpdateGalleryScores(string galleryKey)
    {
        var posts = _galleryPosts
            .Where(post => string.Equals(post.GalleryKey, galleryKey, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var maximumViews = posts.Count > 0 ? posts.Max(post => post.ViewCount) : 0;

        foreach (var post in posts)
        {
            post.UpdateScores(maximumViews);
        }
    }

    private async Task LoadCachedThumbnailsAsync(
        IReadOnlyList<GalleryPost> posts,
        CancellationToken token)
    {
        var picturePosts = posts.Where(post => post.HasImageHint).ToList();
        if (picturePosts.Count == 0)
        {
            return;
        }

        var thumbnails = await Task.Run(() =>
        {
            var loaded = new Dictionary<string, System.Windows.Media.Imaging.BitmapSource>(
                StringComparer.Ordinal);

            foreach (var post in picturePosts)
            {
                token.ThrowIfCancellationRequested();
                if (ThumbnailCache.TryLoad(post.GalleryKey, post.Number) is { } thumbnail)
                {
                    loaded[post.UniqueKey] = thumbnail;
                }
            }

            return loaded;
        }, token);

        foreach (var post in picturePosts)
        {
            if (thumbnails.TryGetValue(post.UniqueKey, out var thumbnail))
            {
                post.Image = thumbnail;
                post.ImageState = GalleryImageState.Ready;
            }
        }
    }

    private void StartAggregateImageLoader(CancellationToken token, int imageGeneration)
    {
        if (_aggregateImageLoading ||
            imageGeneration != _aggregateImageGeneration ||
            token.IsCancellationRequested)
        {
            return;
        }

        _aggregateImageLoading = true;
        _ = LoadAggregateImagesAsync(token, imageGeneration);
    }

    private async Task LoadAggregateImagesAsync(CancellationToken token, int imageGeneration)
    {
        try
        {
            while (!token.IsCancellationRequested && imageGeneration == _aggregateImageGeneration)
            {
                var next = _galleryPosts.FirstOrDefault(post =>
                    post.HasImageHint &&
                    post.ImageState == GalleryImageState.Pending &&
                    !_aggregateImageScheduled.Contains(post.UniqueKey));

                if (next is null)
                {
                    break;
                }

                _aggregateImageScheduled.Add(next.UniqueKey);
                next.ImageState = GalleryImageState.Loading;

                try
                {
                    var result = await DcGalleryClient.FetchFirstImageAsync(next, token);
                    if (token.IsCancellationRequested ||
                        imageGeneration != _aggregateImageGeneration)
                    {
                        return;
                    }

                    next.ThumbnailUrl = result.ImageUrl;
                    next.Image = result.Image;
                    next.ImageState = result.Image is null
                        ? GalleryImageState.Missing
                        : GalleryImageState.Ready;

                    if (result.Image is not null)
                    {
                        await Task.Run(
                            () => ThumbnailCache.Store(
                                next.GalleryKey,
                                next.Number,
                                result.Image),
                            token);
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception)
                {
                    next.Image = null;
                    next.ImageState = GalleryImageState.Missing;
                }

                // 상세 HTML과 이미지 요청을 합쳐 분당 약 4회 수준으로 제한합니다.
                await Task.Delay(Random.Shared.Next(20, 36) * 1000, token);
            }
        }
        catch (OperationCanceledException)
        {
            // 새로고침 또는 창 종료 중입니다.
        }
        finally
        {
            if (imageGeneration == _aggregateImageGeneration)
            {
                _aggregateImageLoading = false;

                if (!token.IsCancellationRequested &&
                    _galleryPosts.Any(post =>
                        post.HasImageHint &&
                        post.ImageState == GalleryImageState.Pending &&
                        !_aggregateImageScheduled.Contains(post.UniqueKey)))
                {
                    StartAggregateImageLoader(token, imageGeneration);
                }
            }
        }
    }

    private async void GalleryAggregateScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_aggregateCancellation is null ||
            _aggregateLoading ||
            !_aggregateFeeds.Values.Any(state => state.HasMore) ||
            e.ExtentHeight <= 0 ||
            e.VerticalOffset + e.ViewportHeight < e.ExtentHeight - 320)
        {
            return;
        }

        await LoadNextPagesForAllGalleriesAsync(
            _aggregateGeneration,
            _aggregateCancellation.Token);
    }

    private async void GalleryAggregateEmpty_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (_aggregateCancellation is null ||
            _aggregateLoading ||
            !_aggregateFeeds.Values.Any(state => state.HasMore))
        {
            return;
        }

        await LoadNextPagesForAllGalleriesAsync(
            _aggregateGeneration,
            _aggregateCancellation.Token);
    }

    private void GalleryRowList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(
                GalleryRowList,
                e.OriginalSource as DependencyObject) is ListBoxItem
            {
                DataContext: GalleryPost post,
            })
        {
            GalleryRowList.SelectedItem = post;
            OpenGalleryPostInApp(post);
        }
    }

    private void GalleryRowList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        OpenSelectedAggregatePost();
        e.Handled = true;
    }

    private void OpenSelectedAggregatePost()
    {
        var post = GalleryList.Visibility == Visibility.Visible
            ? GalleryList.SelectedItem as GalleryPost
            : GalleryRowList.SelectedItem as GalleryPost;

        if (post is not null)
        {
            OpenGalleryPostInApp(post);
        }
    }

    private async void OpenGalleryPostInApp(GalleryPost post)
    {
        _browserPost = post;
        GalleryBrowserHost.Visibility = Visibility.Visible;
        GalleryBrowserStatus.Visibility = Visibility.Visible;
        GalleryBrowserStatus.Text = $"{post.Title}\n불러오는 중…";
        GalleryBrowserAddress.Text = post.MobileUrl;

        try
        {
            var runtimeVersion = CoreWebView2Environment.GetAvailableBrowserVersionString();
            if (string.IsNullOrWhiteSpace(runtimeVersion))
            {
                throw new InvalidOperationException(
                    "Microsoft Edge WebView2 Runtime이 필요합니다. " +
                    "Windows 업데이트 또는 Microsoft 공식 설치 파일로 설치해 주세요.");
            }

            await EnsureGalleryBrowserInitializedAsync();
            GalleryBrowser.CoreWebView2.Navigate(post.MobileUrl);
        }
        catch (Exception ex)
        {
            GalleryBrowserStatus.Text = $"내부 브라우저를 시작하지 못했습니다.\n{ex.Message}";
        }
    }

    private async Task EnsureGalleryBrowserInitializedAsync()
    {
        if (_galleryBrowserInitialized)
        {
            return;
        }

        var userDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Rabbit",
            "webview2");
        Directory.CreateDirectory(userDataDirectory);

        var environment = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: userDataDirectory);
        await GalleryBrowser.EnsureCoreWebView2Async(environment);

        GalleryBrowser.CoreWebView2.Settings.AreDevToolsEnabled = false;
        GalleryBrowser.CoreWebView2.Settings.IsStatusBarEnabled = false;
        GalleryBrowser.CoreWebView2.Settings.UserAgent = MobileBrowserUserAgent;
        GalleryBrowser.CoreWebView2.Profile.PreferredColorScheme = ThemeService.IsDark
            ? CoreWebView2PreferredColorScheme.Dark
            : CoreWebView2PreferredColorScheme.Light;
        GalleryBrowser.CoreWebView2.NewWindowRequested += GalleryBrowser_NewWindowRequested;
        _galleryBrowserInitialized = true;
    }

    private void GalleryBrowserBack_Click(object sender, RoutedEventArgs e)
    {
        GalleryBrowserHost.Visibility = Visibility.Collapsed;
        GalleryBrowser.CoreWebView2?.Navigate("about:blank");
        GalleryBrowserStatus.Visibility = Visibility.Visible;
        GalleryBrowserStatus.Text = "게시글을 불러오는 중…";
        GalleryBrowserAddress.Text = string.Empty;
        _browserPost = null;

        if (GalleryList.Visibility == Visibility.Visible)
        {
            GalleryList.Focus();
        }
        else
        {
            GalleryRowList.Focus();
        }
    }

    private void ReturnFromGalleryBrowserToAggregateList()
    {
        _browserPost = null;
        GalleryBrowserHost.Visibility = Visibility.Collapsed;
        GalleryBrowserStatus.Visibility = Visibility.Collapsed;
        GalleryBrowserAddress.Text = string.Empty;
        _lastGalleryBrowserUri = null;

        GalleryBrowser.CoreWebView2?.Stop();
        GalleryBrowser.CoreWebView2?.Navigate("about:blank");

        if (GalleryList.Visibility == Visibility.Visible)
        {
            GalleryList.Focus();
        }
        else
        {
            GalleryRowList.Focus();
        }
    }

    private void GalleryBrowserRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (_browserPost is null)
        {
            return;
        }

        GalleryBrowserStatus.Visibility = Visibility.Visible;
        GalleryBrowserStatus.Text = "새로고침 중…";
        GalleryBrowser.CoreWebView2?.Reload();
    }

    private void GalleryBrowserOpenExternal_Click(object sender, RoutedEventArgs e)
    {
        var address = GalleryBrowser.CoreWebView2?.Source ?? _lastGalleryBrowserUri;

        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            GalleryBrowserStatus.Visibility = Visibility.Visible;
            GalleryBrowserStatus.Text = "외부 브라우저로 열 주소가 없습니다.";
            return;
        }

        OpenUrl(uri.AbsoluteUri);
    }

    private void GalleryBrowser_NavigationStarting(
        object? sender,
        CoreWebView2NavigationStartingEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Uri))
        {
            return;
        }

        if (e.Uri.StartsWith("about:blank", StringComparison.OrdinalIgnoreCase) &&
            _browserPost is not null &&
            _lastGalleryBrowserUri is { Length: > 0 } previousUri &&
            !previousUri.StartsWith("about:blank", StringComparison.OrdinalIgnoreCase))
        {
            e.Cancel = true;
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(ReturnFromGalleryBrowserToAggregateList));
            return;
        }

        _lastGalleryBrowserUri = e.Uri;
        GalleryBrowserStatus.Visibility = Visibility.Visible;
        GalleryBrowserStatus.Text = "게시글을 불러오는 중…";
        GalleryBrowserAddress.Text = e.Uri;
    }

    private void GalleryBrowser_NavigationCompleted(
        object? sender,
        CoreWebView2NavigationCompletedEventArgs e)
    {
        GalleryBrowserStatus.Visibility = Visibility.Collapsed;

        if (!e.IsSuccess)
        {
            GalleryBrowserStatus.Visibility = Visibility.Visible;
            GalleryBrowserStatus.Text = "페이지를 불러오지 못했습니다. 새로고침해 주세요.";
        }
    }

    private void GalleryBrowser_NewWindowRequested(
        object? sender,
        CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        GalleryBrowser.CoreWebView2?.Navigate(e.Uri);
    }

    private bool MatchesAggregateGalleryFilter(object item)
    {
        if (item is not GalleryPost post)
        {
            return false;
        }

        var source = _gallerySources.FirstOrDefault(candidate =>
            string.Equals(candidate.Key, post.GalleryKey, StringComparison.OrdinalIgnoreCase));

        return post.RecommendCount >= (source?.MinimumRecommendations ?? 0);
    }

    private void ClearAggregateGallery()
    {
        _aggregateGeneration++;
        _aggregateImageGeneration++;
        _aggregateCancellation?.Cancel();
        _aggregateCancellation?.Dispose();
        _aggregateCancellation = null;
        _aggregateInitialized = false;
        _aggregateLoading = false;
        _aggregateImageLoading = false;
        _aggregateFeeds.Clear();
        _aggregateImageScheduled.Clear();
        _galleryPosts.Clear();
        _galleryView.Refresh();
        UpdateAggregateGalleryStatus();
    }

    private void UpdateAggregateGalleryStatus(string? message = null)
    {
        var visible = _galleryView.Cast<GalleryPost>().Count();
        var pendingImages = _galleryPosts.Count(post =>
            post.HasImageHint && post.ImageState == GalleryImageState.Pending);
        var more = _aggregateFeeds.Values.Any(state => state.HasMore);

        var breakdown = string.Join(
            " · ",
            _aggregateFeeds.Values.Select(state =>
                $"{state.Source.GalleryId} {state.LoadedPostCount:N0}"));

        GalleryStatusText.Text = message ??
            $"전체 {_gallerySources.Count}갤러리 · 표시 {visible:N0} / {_galleryPosts.Count:N0}" +
            (breakdown.Length > 0 ? $" [{breakdown}]" : string.Empty) +
            $" · 썸네일 대기 {pendingImages:N0}{(more ? " · 아래로 더 보기" : string.Empty)}";
        GalleryStatusText.ToolTip = string.Join(
            Environment.NewLine,
            _aggregateFeeds.Values.Select(state =>
                $"{state.Source.DisplayName}: {state.LoadedPostCount:N0}개 글, " +
                $"{state.PagesLoaded}페이지" +
                (state.PendingPosts.Count > 0
                    ? $", 다음 표시 대기 {state.PendingPosts.Count:N0}"
                    : string.Empty) +
                (state.LastError is null ? string.Empty : $" · 실패: {state.LastError}")));

        GalleryEmptyText.Visibility = visible == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        GalleryEmptyText.Cursor = more ? Cursors.Hand : Cursors.Arrow;
        GalleryEmptyText.Text =
            message?.Contains("불러오는", StringComparison.Ordinal) == true
                ? "등록된 갤러리의 글을 불러오는 중…"
                : more
                    ? "표시할 글이 없습니다.\n눌러서 모든 갤러리의 다음 글을 불러오세요."
                    : "표시할 글이 없습니다.\n추천 필터를 낮추거나 새로고침해 보세요.";
    }

    private sealed class GalleryFeedState(GallerySource source)
    {
        public GallerySource Source { get; } = source;

        public int NextPage { get; set; } = 1;

        public bool HasMore { get; set; } = true;

        public int PagesLoaded { get; set; }

        public int LoadedPostCount { get; set; }

        public string? LastError { get; set; }

        public int BatchLimit { get; set; }

        public Queue<GalleryPost> PendingPosts { get; } = new();
    }

    private sealed class GalleryPostComparer(GallerySortMode mode) : IComparer
    {
        public GallerySortMode Mode { get; set; } = mode;

        public int Compare(object? x, object? y)
        {
            if (x is not GalleryPost left || y is not GalleryPost right)
            {
                return 0;
            }

            var primary = Mode switch
            {
                GallerySortMode.RecommendationGrade =>
                    right.RecommendationRate.CompareTo(left.RecommendationRate),
                GallerySortMode.WeightedViews =>
                    right.ViewWeight.CompareTo(left.ViewWeight),
                _ => right.PublishedAt.CompareTo(left.PublishedAt),
            };

            if (primary != 0)
            {
                return primary;
            }

            var secondary = Mode switch
            {
                GallerySortMode.RecommendationGrade =>
                    right.RecommendCount.CompareTo(left.RecommendCount),
                GallerySortMode.WeightedViews =>
                    right.ViewCount.CompareTo(left.ViewCount),
                _ => right.ViewCount.CompareTo(left.ViewCount),
            };

            if (secondary != 0)
            {
                return secondary;
            }

            var time = right.PublishedAt.CompareTo(left.PublishedAt);
            if (time != 0)
            {
                return time;
            }

            return string.Compare(left.Title, right.Title, StringComparison.CurrentCulture);
        }
    }
}
