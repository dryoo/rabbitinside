using System.Text;
using System.Windows;
using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.IO;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Data;
using System.Collections.ObjectModel;
using Rabbit.Models;
using System.Globalization;
using Rabbit.Services;
using Rabbit.Interop;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Controls.Primitives;
namespace Rabbit;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window, INotifyPropertyChanged
{
    private static readonly string[] AcceptedExtensions = [".lnk", ".url"];

    private const int HotkeyId = 0x5262;

    /// <summary>무료 게임을 더 받아 볼 수 있는 스토어 주소입니다.</summary>
    private const string FreeGamesPageUrl = "https://store.epicgames.com/free-games";

    /// <summary>Windows 설정이 바뀌면 오는 메시지입니다.</summary>
    private const int WmSettingChange = 0x001A;

    /// <summary>설치 용량을 채워 넣는 주기입니다. 사용 중에 부담을 주지 않도록 넉넉히 잡습니다.</summary>
    private static readonly TimeSpan BackgroundScanInterval = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan IdleScanInterval = TimeSpan.FromSeconds(20);

    /// <summary>한 번 확인한 용량을 다시 확인하기까지의 기간입니다.</summary>
    private static readonly TimeSpan SizeRefreshInterval = TimeSpan.FromDays(7);

    /// <summary>배포가 끝나는 시각에는 새 게임이 걸리므로, 그때 확인하되 조금 여유를 둡니다.</summary>
    private static readonly TimeSpan FreeGameEndBuffer = TimeSpan.FromMinutes(2);

    /// <summary>기한을 아직 모를 때(조회 실패 등) 한 번 더 해 보기까지의 시간입니다.</summary>
    private static readonly TimeSpan FreeGameRetryDelay = TimeSpan.FromHours(1);

    /// <summary>타이머가 받아 주는 간격에는 한계(약 24일)가 있어서, 아무리 멀어도 여기까지만 잡습니다.</summary>
    private static readonly TimeSpan FreeGameMaxDelay = TimeSpan.FromDays(20);

    private readonly ObservableCollection<ShortcutItem> _items = [];

    private readonly AppSettings _settings = SettingsStore.Load();
    private readonly ListCollectionView _view;

    private readonly ObservableCollection<GallerySource> _gallerySources = [];
    private readonly ObservableCollection<GalleryPost> _galleryPosts = [];
    private readonly ObservableCollection<GalleryKindOption> _galleryKinds =
    [
        new(DcGalleryKind.MinorGallery, "마이너갤러리"),
        new(DcGalleryKind.MiniGallery, "미니갤러리"),
        new(DcGalleryKind.Gallery, "일반갤러리"),
    ];

    private ListCollectionView _galleryView = null!;
    private CancellationTokenSource? _galleryCancellation;
    private GallerySource? _activeGallerySource;
    private int _galleryNextPage = 1;
    private bool _galleryHasMore = true;
    private bool _galleryLoading;
    private bool _galleryInitialized;
    private bool _galleryToolbarReady;
    private bool _syncingRecommendationFilter;
    private int _galleryMinimumRecommendations;
    private int _galleryGeneration;
    private int _galleryImageGeneration;
    private bool _galleryImageLoading;
    private readonly HashSet<string> _galleryImageScheduled = new(StringComparer.Ordinal);

    private double _tileIconSize;
    private double _tileItemWidth = 80;
    private double _tileItemHeight = 80;
    private Thickness _tileAreaMargin;
    private bool _initializing = true;
    private bool _autoTileUpdateQueued;

    private IntPtr _hwnd;
    private bool _hotkeyRegistered;
    private bool _hotkeyInitialized;

    private CancellationTokenSource? _sizeScanCancellation;
    private bool _sizeScanHurry;

    private DateTimeOffset _missingCheckedAt;

    private readonly DispatcherTimer _messageTimer = new() { Interval = TimeSpan.FromSeconds(6) };

    private readonly DispatcherTimer _freeGameTimer = new();

    private IReadOnlyList<FreeGameOffer> _freeGames = [];

    /// <summary>기한을 모를 때 한 번 더 확인해 봤는지 여부입니다. 두 번까지 해 보고 멈춥니다.</summary>
    private bool _freeGameRetried;

    public MainWindow()
    {
        InitializeComponent();

        _tileIconSize = 64;

        _view = (ListCollectionView)CollectionViewSource.GetDefaultView(_items);
        _view.CustomSort = new ShortcutComparer(_settings.SortMode);
        _view.Filter = MatchesSearch;

        ShortcutList.ItemsSource = _view;

        foreach (var source in (_settings.GallerySources ?? [])
                     .Where(source => !string.IsNullOrWhiteSpace(source.GalleryId))
                     .DistinctBy(source => source.Key))
        {
            _gallerySources.Add(source);
        }

        if (_gallerySources.Count == 0)
        {
            _gallerySources.Add(GallerySource.CreateDune());
        }

        _galleryView = (ListCollectionView)CollectionViewSource.GetDefaultView(_galleryPosts);
        _galleryView.CustomSort = new GalleryPostComparer(_settings.GallerySortMode);

        GalleryList.ItemsSource = _galleryView;
        GalleryRowList.ItemsSource = _galleryView;
        GallerySourceCombo.ItemsSource = _gallerySources;
        GalleryKindCombo.ItemsSource = _galleryKinds;
        GalleryKindCombo.SelectedIndex = 0;

        // 저장된 갤러리가 없거나 지운 갤러리라면 첫 항목으로 돌아갑니다.
        var selectedGallery =
            _gallerySources.FirstOrDefault(source => source.Key == _settings.SelectedGalleryKey)
            ?? _gallerySources[0];

        GallerySourceCombo.SelectedItem = selectedGallery;
        _galleryToolbarReady = true;
        SelectGallerySort(_settings.GallerySortMode);
        SelectGalleryView(_settings.GalleryViewMode);

        SelectSortButton(_settings.SortMode);

        Loaded += Window_Loaded;
        Closed += Window_Closed;
        Activated += Window_Activated;
        Closing += Window_Closing;
        PreviewTextInput += Window_PreviewTextInput;
        SizeChanged += Window_SizeChanged;
        StateChanged += Window_StateChanged;

        _messageTimer.Tick += (_, _) =>
        {
            _messageTimer.Stop();
            MessageText.Visibility = Visibility.Collapsed;
        };

        // 무료 게임 줄은 다음에 볼 시각을 그때그때 정해서 한 번만 울립니다.
        _freeGameTimer.Tick += (_, _) =>
        {
            _freeGameTimer.Stop();
            _ = LoadFreeGamesAsync();
        };

        _initializing = false;

        RestoreWindowBounds();
        UpdateStatus();
    }

    private void MainTabs_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.GetPosition(MainTabs).Y > 40 ||
            FindVisualAncestor<TabItem>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximized();
        }
        else if (WindowState == WindowState.Normal && e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // 창 드래그가 이미 끝난 경우입니다.
            }
        }

        e.Handled = true;
    }

    private void TitleBarMinimize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void TitleBarMaximize_Click(object sender, RoutedEventArgs e) =>
        ToggleMaximized();

    private void TitleBarClose_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximized()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        var maximized = WindowState == WindowState.Maximized;
        TitleBarMaximizeButton.Content = maximized ? "❐" : "□";
        TitleBarMaximizeButton.ToolTip = maximized ? "이전 크기로 복원" : "최대화";
        QueueAutoTileSizeUpdate();
    }

    private static T? FindVisualAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
            {
                return match;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    /// <summary>1단계: 바로가기를 끌어다 놓으면 목록에 쌓이고, 더블클릭하거나 Enter로 실행합니다.</summary>
    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (MainTabs.SelectedIndex != 0)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = GetDroppedTargets(e).Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (MainTabs.SelectedIndex != 0)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var added = 0;
        var skipped = 0;
        var newItems = new List<ShortcutItem>();

        foreach (var target in GetDroppedTargets(e))
        {
            var item = AddTarget(target);
            if (item is null)
            {
                skipped++;
                continue;
            }

            newItems.Add(item);
            added++;
        }

        UpdateStatus(added, skipped);

        if (added > 0)
        {
            // 추가하는 순간 바로가기 정보를 뽑아 둡니다. 나중에 바로가기를 지워도 실행할 수 있게 하기 위해서입니다.
            foreach (var item in newItems)
            {
                UpdateItemState(item);
            }

            Save();
            _ = LoadIconsAsync(newItems);
        }

        // 원본을 옮기지 않고 복사해서 받았다는 것을 셸에 알려 줍니다(원본이 지워지지 않게).
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void ShortcutList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => LaunchSelected();

    private void ShortcutList_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                LaunchSelected();
                e.Handled = true;
                break;

            case Key.Delete:
                RemoveSelected();
                e.Handled = true;
                break;
        }
    }

    private void RemoveSelected()
    {
        if (ShortcutList.SelectedItem is ShortcutItem item)
        {
            RemoveItem(item, ShortcutList.SelectedIndex);
        }
    }

    /// <summary>
    /// 목록에서 지우고 바로 다음 항목을 선택합니다. Delete 키를 연달아 눌러
    /// 여러 개를 지울 수 있도록 하기 위해서입니다.
    /// </summary>
    private void RemoveItem(ShortcutItem item, int index)
    {
        _items.Remove(item);
        RefreshView();

        if (_view.Count > 0)
        {
            ShortcutList.SelectedIndex = Math.Clamp(index, 0, _view.Count - 1);
            ShortcutList.ScrollIntoView(ShortcutList.SelectedItem);
            ShortcutList.Focus();
        }

        Save();
    }

    private void LaunchSelected()
    {
        if (ShortcutList.SelectedItem is ShortcutItem item)
        {
            LaunchItem(item);
        }
    }

    private void LaunchItem(ShortcutItem item)
    {
        try
        {
            var usedFallback = ShortcutLauncher.Launch(item);

            item.LastLaunchedAt = DateTimeOffset.Now;
            item.LaunchCount++;
            UpdateStatus();
            Save();

            if (usedFallback)
            {
                ShowMessage($"{item.DisplayName} · 바로가기 대신 실행 파일로 실행했습니다");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"실행하지 못했습니다.\n\n{item.Path}\n\n{ex.Message}",
                "rabbit inside",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>아래 줄에는 개수만 숫자로 보여주고, 자세한 설명은 툴팁에 담습니다.</summary>
    private void UpdateStatus(int added = 0, int skipped = 0)
    {
        var total = _items.Count;
        var visible = total == 0 ? 0 : _view.Cast<ShortcutItem>().Count();

        StatusText.Text = visible == total ? total.ToString() : $"{visible} / {total}";

        var detail = new List<string> { $"등록된 바로가기 {total}개" };

        if (visible != total)
        {
            detail.Add($"지금 {visible}개 표시");
        }

        if (added > 0)
        {
            detail.Add($"방금 {added}개 추가");
        }

        if (skipped > 0)
        {
            detail.Add($"이미 등록된 {skipped}개는 건너뜀");
        }

        if (_hotkeyInitialized && !_hotkeyRegistered)
        {
            detail.Add("전역 단축키 Ctrl+Alt+R을 등록하지 못했습니다(다른 앱이 사용 중)");
        }

        StatusText.ToolTip = string.Join(" · ", detail);
        EmptyHint.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;

        QueueAutoTileSizeUpdate();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // 저장된 목록이 아예 없으면 첫 실행으로 보고 바탕화면을 한 번 훑습니다.
        var firstRun = !ShortcutStore.Exists;

        foreach (var item in ShortcutStore.Load())
        {
            _items.Add(item);
        }

        var discovered = 0;
        if (firstRun)
        {
            foreach (var path in DesktopScanner.FindShortcuts())
            {
                if (AddShortcut(path) is not null)
                {
                    discovered++;
                }
            }
        }

        UpdateStatus(added: discovered);

        if (discovered > 0)
        {
            Save();
        }

        await LoadIconsAsync(_items.ToList());

        RefreshMissingFlags();

        // 설치 용량은 오래 걸리므로 창이 준비된 뒤 백그라운드에서 채웁니다.
        _sizeScanCancellation = new CancellationTokenSource();
        _ = RunSizeScanLoopAsync(_sizeScanCancellation.Token);

        // 저장해 둔 무료 게임을 그대로 씁니다. 기한을 아니까 창을 켤 때는 스토어에 묻지 않습니다.
        StartFreeGames();
    }

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_galleryToolbarReady || MainTabs.SelectedIndex != 1)
        {
            return;
        }

        if (!_galleryInitialized)
        {
            _ = ReloadGalleryAsync();
        }
    }

    private void GallerySourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_galleryToolbarReady)
        {
            return;
        }

        if (GallerySourceCombo.SelectedItem is not GallerySource source)
        {
            SyncRecommendationFilter(null);
            _settings.SelectedGalleryKey = null;
            SaveSettings();
            ClearGallery();
            return;
        }

        SyncRecommendationFilter(source);
        _settings.SelectedGalleryKey = source.Key;
        SaveSettings();

        // 탭이 아직 보이지 않을 때는 선택만 기억하고, 실제 요청은 탭을 열었을 때 시작합니다.
        if (MainTabs.SelectedIndex == 1)
        {
            _ = ReloadGalleryAsync();
        }
        else
        {
            _galleryInitialized = false;
        }
    }

    private void GalleryIdBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        AddSelectedGallery();
        e.Handled = true;
    }

    private void AddGalleryButton_Click(object sender, RoutedEventArgs e) => AddSelectedGallery();

    private void AddSelectedGallery()
    {
        var fallbackKind = GalleryKindCombo.SelectedItem is GalleryKindOption option
            ? option.Kind
            : DcGalleryKind.MinorGallery;

        var parsed = GallerySource.Parse(GalleryIdBox.Text, fallbackKind);
        if (parsed is null)
        {
            GalleryStatusText.Text = "갤러리 ID나 주소를 확인해 주세요. 예: duneawakening";
            return;
        }

        var existing = _gallerySources.FirstOrDefault(source =>
            string.Equals(source.Key, parsed.Key, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            _gallerySources.Add(parsed);
            existing = parsed;
        }

        GalleryIdBox.Clear();
        GallerySourceCombo.SelectedItem = existing;
        SaveGallerySources();
    }

    private void RemoveGalleryButton_Click(object sender, RoutedEventArgs e)
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
        SyncRecommendationFilter(GallerySourceCombo.SelectedItem as GallerySource);
        SaveGallerySources();

        if (GallerySourceCombo.SelectedItem is not GallerySource next)
        {
            ClearGallery();
            return;
        }

        _settings.SelectedGalleryKey = next.Key;
        SaveSettings();
        if (MainTabs.SelectedIndex == 1)
        {
            _ = ReloadGalleryAsync();
        }
        else
        {
            _galleryInitialized = false;
        }
    }

    private void SaveGallerySources()
    {
        _settings.GallerySources = [.. _gallerySources];
        if (GallerySourceCombo.SelectedItem is GallerySource selected)
        {
            _settings.SelectedGalleryKey = selected.Key;
        }

        SaveSettings();
    }

    /// <summary>선택한 갤러리마다 마지막으로 쓰던 추천 필터를 화면에 복원합니다.</summary>
    private void SyncRecommendationFilter(GallerySource? source)
    {
        _galleryMinimumRecommendations = Math.Max(0, source?.MinimumRecommendations ?? 0);

        _syncingRecommendationFilter = true;
        RecommendFilterBox.Text = _galleryMinimumRecommendations.ToString();
        _syncingRecommendationFilter = false;

        _galleryView.Refresh();
    }

    private void RefreshGalleryButton_Click(object sender, RoutedEventArgs e)
    {
        if (GallerySourceCombo.SelectedItem is not GallerySource)
        {
            GalleryStatusText.Text = "추가한 갤러리가 없습니다. 갤러리 ID를 입력해 주세요.";
            return;
        }

        _ = ReloadGalleryAsync();
    }

    private async Task ReloadGalleryAsync()
    {
        if (GallerySourceCombo.SelectedItem is not GallerySource source)
        {
            ClearGallery();
            return;
        }

        _galleryCancellation?.Cancel();
        _galleryCancellation?.Dispose();
        _aggregateCancellation?.Cancel();
        _aggregateCancellation?.Dispose();
        _galleryCancellation = new CancellationTokenSource();

        var generation = ++_galleryGeneration;
        _galleryImageGeneration++;
        _galleryImageLoading = false;
        _galleryImageScheduled.Clear();
        _activeGallerySource = source;
        _galleryInitialized = true;
        _galleryNextPage = 1;
        _galleryHasMore = true;
        _galleryLoading = false;
        _galleryPosts.Clear();
        _galleryView.Refresh();
        UpdateGalleryStatus("불러오는 중…");

        await LoadGalleryPageAsync(
            source,
            _galleryNextPage,
            generation,
            _galleryCancellation.Token);
    }

    private async Task LoadGalleryPageAsync(
        GallerySource source,
        int page,
        int generation,
        CancellationToken token)
    {
        if (_galleryLoading || !_galleryHasMore || generation != _galleryGeneration)
        {
            return;
        }

        _galleryLoading = true;
        UpdateGalleryStatus(page == 1 ? "글 목록을 불러오는 중…" : "다음 글을 불러오는 중…");

        try
        {
            var posts = await DcGalleryClient.FetchPostsAsync(source, page, token);
            if (token.IsCancellationRequested || generation != _galleryGeneration)
            {
                return;
            }

            var existingNumbers = _galleryPosts
                .Select(post => post.Number)
                .ToHashSet(StringComparer.Ordinal);

            var added = posts
                .Where(post => existingNumbers.Add(post.Number))
                .ToList();

            foreach (var post in added)
            {
                _galleryPosts.Add(post);
            }

            _galleryHasMore = posts.Count > 0;
            _galleryNextPage = page + 1;
            _galleryView.Refresh();
            UpdateGalleryStatus();

            if (added.Count > 0 && !token.IsCancellationRequested)
            {
                var imageGeneration = _galleryImageGeneration;
                _ = Dispatcher.BeginInvoke(
                    DispatcherPriority.Loaded,
                    new Action(() => StartGalleryImageLoader(token, imageGeneration)));
            }
        }
        catch (OperationCanceledException)
        {
            // 다른 갤러리를 선택했거나 창을 닫았습니다.
        }
        catch (Exception)
        {
            if (generation == _galleryGeneration)
            {
                _galleryHasMore = false;
                UpdateGalleryStatus("불러오지 못했습니다. 잠시 후 새로고침해 주세요.");
            }
        }
        finally
        {
            if (generation == _galleryGeneration)
            {
                _galleryLoading = false;
            }
        }
    }

    private void StartGalleryImageLoader(CancellationToken token, int imageGeneration)
    {
        if (_galleryImageLoading ||
            imageGeneration != _galleryImageGeneration ||
            token.IsCancellationRequested)
        {
            return;
        }

        _galleryImageLoading = true;
        _ = LoadGalleryImagesAsync(token, imageGeneration);
    }

    /// <summary>
    /// 목록에서 사진 글로 표시된 글만 상세 페이지를 열어 첫 사진을 가져옵니다.
    /// 접속 간격은 약 1분으로 무작위화해 짧은 시간에 요청이 몰리지 않게 합니다.
    /// </summary>
    private async Task LoadGalleryImagesAsync(CancellationToken token, int imageGeneration)
    {
        try
        {
            while (!token.IsCancellationRequested &&
                   imageGeneration == _galleryImageGeneration)
            {
                var next = _galleryPosts.FirstOrDefault(post =>
                    post.HasImageHint &&
                    !_galleryImageScheduled.Contains(post.Number));

                if (next is null)
                {
                    break;
                }

                _galleryImageScheduled.Add(next.Number);
                next.ImageState = GalleryImageState.Loading;

                try
                {
                    var result = await DcGalleryClient.FetchFirstImageAsync(next, token);
                    if (token.IsCancellationRequested ||
                        imageGeneration != _galleryImageGeneration)
                    {
                        return;
                    }

                    next.ThumbnailUrl = result.ImageUrl;
                    next.Image = result.Image;
                    next.ImageState = result.Image is null
                        ? GalleryImageState.Missing
                        : GalleryImageState.Ready;
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

                // 55~95초 사이로 간격을 흔들어 일정한 자동 요청 패턴을 피합니다.
                await Task.Delay(Random.Shared.Next(55, 96) * 1000, token);
            }
        }
        catch (OperationCanceledException)
        {
            // 화면이 바뀌었거나 창을 닫았습니다.
        }
        finally
        {
            if (imageGeneration == _galleryImageGeneration)
            {
                _galleryImageLoading = false;

                if (!token.IsCancellationRequested &&
                    _galleryPosts.Any(post =>
                        post.HasImageHint &&
                        !_galleryImageScheduled.Contains(post.Number)))
                {
                    StartGalleryImageLoader(token, imageGeneration);
                }
            }
        }
    }

    private async void GalleryScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_activeGallerySource is not { } source ||
            _galleryCancellation is null ||
            _galleryLoading ||
            !_galleryHasMore ||
            e.ExtentHeight <= 0 ||
            e.VerticalOffset + e.ViewportHeight < e.ExtentHeight - 320)
        {
            return;
        }

        await LoadGalleryPageAsync(
            source,
            _galleryNextPage,
            _galleryGeneration,
            _galleryCancellation.Token);
    }

    private async void GalleryEmptyText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_activeGallerySource is not { } source ||
            _galleryCancellation is null ||
            _galleryLoading ||
            !_galleryHasMore)
        {
            return;
        }

        await LoadGalleryPageAsync(
            source,
            _galleryNextPage,
            _galleryGeneration,
            _galleryCancellation.Token);
    }

    private void RecommendFilterBox_TextChanged(object sender, TextChangedEventArgs e)
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
        UpdateGalleryStatus();

        if (_galleryCancellation is { } token)
        {
            var imageGeneration = _galleryImageGeneration;
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() => StartGalleryImageLoader(token.Token, imageGeneration)));
        }
    }

    private bool MatchesGalleryFilter(object item) =>
        item is GalleryPost post &&
        post.RecommendCount >= _galleryMinimumRecommendations;

    private void GalleryList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(
                GalleryList,
                e.OriginalSource as DependencyObject) is ListBoxItem
            {
                DataContext: GalleryPost post,
            })
        {
            GalleryList.SelectedItem = post;
            OpenGalleryPostInApp(post);
        }
    }

    private void GalleryList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        OpenSelectedGalleryPost();
        e.Handled = true;
    }

    private void OpenSelectedGalleryPost()
    {
        if (GalleryList.SelectedItem is GalleryPost post)
        {
            OpenGalleryPostInApp(post);
        }
    }

    private void ClearGallery()
    {
        _galleryGeneration++;
        _galleryImageGeneration++;
        _galleryCancellation?.Cancel();
        _galleryCancellation?.Dispose();
        _galleryCancellation = null;
        _galleryImageLoading = false;
        _galleryImageScheduled.Clear();
        _activeGallerySource = null;
        _galleryInitialized = false;
        _galleryLoading = false;
        _galleryHasMore = false;
        _galleryPosts.Clear();
        _galleryView.Refresh();
        UpdateGalleryStatus();
    }

    private void UpdateGalleryStatus(string? message = null)
    {
        var visible = _galleryView.Cast<GalleryPost>().Count();
        var galleryName = _activeGallerySource?.Name ?? "갤러리";
        var more = _galleryHasMore ? " · 아래로 내려 더 보기" : string.Empty;

        GalleryStatusText.Text = message ??
            $"{galleryName} · 표시 {visible} / {_galleryPosts.Count}{more}";

        GalleryEmptyText.Visibility = visible == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        GalleryEmptyText.Cursor = _galleryHasMore
            ? Cursors.Hand
            : Cursors.Arrow;
        GalleryEmptyText.Text = message?.Contains("불러오는", StringComparison.Ordinal) == true
            ? "글 목록을 불러오는 중…"
            : _galleryHasMore
                ? "표시할 글이 없습니다.\n눌러서 다음 글을 더 불러오세요."
                : "표시할 글이 없습니다.\n추천 필터를 낮추거나 새로고침해 보세요.";
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _sizeScanCancellation?.Cancel();
        _freeGameTimer.Stop();
        _galleryCancellation?.Cancel();
        _galleryCancellation?.Dispose();

        if (_hotkeyRegistered)
        {
            User32.UnregisterHotKey(_hwnd, HotkeyId);
            _hotkeyRegistered = false;
        }

        Save();
    }

    /// <summary>
    /// 창을 켤 때 무료 게임 줄을 준비합니다.
    /// 저장해 둔 기한이 아직 남아 있으면 그대로 보여 주고 그때까지만 기다립니다.
    /// 기한이 없거나 이미 지났을 때만 스토어에 물어봅니다.
    /// </summary>
    private void StartFreeGames()
    {
        _freeGames = _settings.FreeGames;
        ShowFreeGames();

        if (NextFreeGameDelay() is { } delay)
        {
            _freeGameRetried = false;
            ScheduleFreeGameCheck(delay);
            return;
        }

        _ = LoadFreeGamesAsync();
    }

    /// <summary>
    /// 지금 무료로 받을 수 있는 Epic 게임을 확인해 아래 줄에 채웁니다.
    /// 다음 확인 시각은 끝나는 기한으로 정하므로, 기한을 아는 동안에는 다시 묻지 않습니다.
    /// </summary>
    private async Task LoadFreeGamesAsync()
    {
        IReadOnlyList<FreeGameOffer> offers;

        try
        {
            var locale = CultureInfo.CurrentUICulture.Name;

            offers = await EpicFreeGames.FetchAsync(
                locale.Length > 0 ? locale : "en-US",
                EffectiveCountry());
        }
        catch (Exception)
        {
            // 무료 게임 줄은 덤입니다. 못 채워도 앱은 그대로 쓰고, 한 번만 더 해 봅니다.
            ScheduleFreeGameRetry();
            return;
        }

        _freeGames = offers;
        ShowFreeGames();

        // 기한을 저장해 두면 다음에 창을 켤 때 스토어에 묻지 않아도 됩니다.
        _settings.FreeGames = [.. offers];
        SaveSettings();

        if (NextFreeGameDelay() is { } delay)
        {
            _freeGameRetried = false;
            ScheduleFreeGameCheck(delay);
        }
        else
        {
            // 기한을 모르면 한 번만 더 해 보고 그만둡니다.
            ScheduleFreeGameRetry();
        }
    }

    /// <summary>무료 게임을 물어볼 국가 코드입니다. 설정에 적어 두었으면 그 값을, 아니면 Windows 지역 설정을 씁니다.</summary>
    private string EffectiveCountry() =>
        string.IsNullOrWhiteSpace(_settings.FreeGameCountry)
            ? WindowsCountry()
            : _settings.FreeGameCountry.Trim().ToUpperInvariant();

    /// <summary>Windows 지역 설정에서 뽑은 두 글자 국가 코드입니다. 못 얻으면 US로 봅니다.</summary>
    private static string WindowsCountry()
    {
        try
        {
            var country = RegionInfo.CurrentRegion.TwoLetterISORegionName;

            return country.Length == 2 ? country : "US";
        }
        catch (Exception)
        {
            return "US";
        }
    }

    /// <summary>
    /// 지금 무료인 게임이 끝나는 시각 직후에 볼 시간을 돌려줍니다.
    /// 기한이 아직 없거나 이미 지났으면 null입니다.
    /// </summary>
    private TimeSpan? NextFreeGameDelay()
    {
        if (_freeGames.Count == 0)
        {
            return null;
        }

        // 지금 떠 있는 게임이 끝나는 순간이 곧 다음 게임이 걸리는 때입니다.
        var remaining = _freeGames.Min(offer => offer.EndsAt) + FreeGameEndBuffer - DateTimeOffset.UtcNow;

        if (remaining <= TimeSpan.Zero)
        {
            return null;
        }

        return remaining < FreeGameMaxDelay ? remaining : FreeGameMaxDelay;
    }

    /// <summary>기한을 아직 모를 때 한 번만 더 확인해 봅니다. 이미 해 봤으면 더 예약하지 않습니다.</summary>
    private void ScheduleFreeGameRetry()
    {
        if (_freeGameRetried)
        {
            _freeGameTimer.Stop();
            return;
        }

        _freeGameRetried = true;
        ScheduleFreeGameCheck(FreeGameRetryDelay);
    }

    /// <summary>다음 확인 시각을 예약합니다. 타이머는 한 번 울리고, 조회가 끝나면 다시 예약됩니다.</summary>
    private void ScheduleFreeGameCheck(TimeSpan delay)
    {
        _freeGameTimer.Stop();
        _freeGameTimer.Interval = delay;
        _freeGameTimer.Start();
    }

    /// <summary>무료 게임 줄을 채우고, 보여 줄 게임이 없으면 숨깁니다.</summary>
    private void ShowFreeGames()
    {
        var alreadyDismissed = string.Equals(
            FreeGamesKey(_freeGames),
            _settings.DismissedFreeGameKey,
            StringComparison.Ordinal);

        var show = _freeGames.Count > 0 && !alreadyDismissed;

        FreeGameLinks.Children.Clear();

        if (show)
        {
            foreach (var offer in _freeGames)
            {
                FreeGameLinks.Children.Add(CreateFreeGameLink(offer));
            }

            // 여러 개면 가장 먼저 끝나는 시각을 기준으로 알려 줍니다.
            FreeGameDeadline.Text =
                $"{_freeGames.Min(offer => offer.EndsAt).ToLocalTime():M월 d일 HH:mm}까지";
        }

        FreeGameBanner.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

        // 줄이 나타나거나 사라지면 타일이 놓일 자리도 달라집니다. 배치가 끝난 뒤에 다시 계산합니다.
        QueueAutoTileSizeUpdate();
    }

    /// <summary>무료 게임 하나를 누르면 스토어 페이지가 열리는 링크 단추로 만듭니다.</summary>
    private Button CreateFreeGameLink(FreeGameOffer offer)
    {
        var button = new Button
        {
            Style = (Style)FindResource("BannerLinkButton"),
            Content = offer.Title,
            ToolTip = offer.Url,
            Tag = offer.Url,
        };

        button.Click += FreeGameLink_Click;

        return button;
    }

    private void FreeGameLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url })
        {
            OpenUrl(url);
        }
    }

    private void OpenFreeGamesPage_Click(object sender, RoutedEventArgs e) => OpenUrl(FreeGamesPageUrl);

    /// <summary>지금 떠 있는 무료 게임 묶음을 닫습니다. 다음 무료 게임이 나오면 줄이 다시 뜹니다.</summary>
    private void FreeGameDismiss_Click(object sender, RoutedEventArgs e)
    {
        if (_freeGames.Count == 0)
        {
            return;
        }

        _settings.DismissedFreeGameKey = FreeGamesKey(_freeGames);
        SaveSettings();

        ShowFreeGames();
    }

    /// <summary>기본 브라우저로 주소를 엽니다. 열지 못해도 앱은 계속 씁니다.</summary>
    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowMessage($"주소를 열지 못했습니다: {ex.Message}");
        }
    }

    /// <summary>지금 목록에 있는 무료 게임 묶음을 나타내는 식별자입니다.</summary>
    private static string FreeGamesKey(IReadOnlyList<FreeGameOffer> offers) =>
        string.Join("|", offers.Select(offer => offer.Key));

    private ShortcutItem? AddShortcut(string path) => AddTarget(new DropTarget(path, null, false));

    /// <summary>같은 항목이 이미 등록되어 있으면 null을 돌려줍니다.</summary>
    private ShortcutItem? AddTarget(DropTarget target)
    {
        if (_items.Any(item => string.Equals(item.Path, target.Target, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var item = ShortcutItem.FromTarget(target.Target, target.DisplayName, target.IsShellTarget);
        _items.Add(item);
        return item;
    }

    /// <summary>
    /// 아이콘은 셸을 거치는 작업이라 시간이 걸릴 수 있어 백그라운드에서 읽고,
    /// UI 스레드에서 각 항목에 반영합니다.
    /// </summary>
    private async Task LoadIconsAsync(IReadOnlyList<ShortcutItem> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        Dictionary<string, BitmapSource> icons;
        try
        {
            icons = await Task.Run(() =>
            {
                var map = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in items)
                {
                    var icon = ShortcutIconResolver.GetIcon(item);
                    if (icon is not null)
                    {
                        map[item.Path] = icon;
                    }
                }

                return map;
            });
        }
        catch (Exception)
        {
            // 아이콘을 못 읽어도 목록 자체는 그대로 씁니다.
            return;
        }

        foreach (var item in items)
        {
            if (icons.TryGetValue(item.Path, out var icon))
            {
                item.Icon = icon;

                // 바로가기를 지웠을 때를 대비해 아이콘도 저장해 둡니다.
                if (File.Exists(item.Path))
                {
                    IconCache.Store(item.Path, icon);
                }
            }
        }
    }

    private void Save()
    {
        try
        {
            ShortcutStore.Save(_items);
        }
        catch (Exception ex)
        {
            MessageText.Text = $"저장하지 못했습니다: {ex.Message}";
            MessageText.Visibility = Visibility.Visible;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _hwnd = new WindowInteropHelper(this).Handle;
        ApplyTitleBarTheme();

        if (HwndSource.FromHwnd(_hwnd) is { } source)
        {
            source.AddHook(WndProc);
        }

        _hotkeyRegistered = User32.RegisterHotKey(
            _hwnd,
            HotkeyId,
            User32.MOD_CONTROL | User32.MOD_ALT,
            User32.VK_R);
        _hotkeyInitialized = true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == User32.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            BringToFront();
            handled = true;
        }

        // Windows에서 다크/라이트 모드를 바꾸면 이 메시지가 옵니다.
        if (msg == WmSettingChange && IsImmersiveColorSet(lParam))
        {
            ApplyTheme();
        }

        return IntPtr.Zero;
    }

    private static bool IsImmersiveColorSet(IntPtr lParam)
    {
        var name = lParam == IntPtr.Zero ? null : Marshal.PtrToStringUni(lParam);

        return string.Equals(name, "ImmersiveColorSet", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Windows 설정에 맞춰 색과 제목 표시줄을 다시 적용합니다.</summary>
    private void ApplyTheme()
    {
        ThemeService.ApplySystemTheme();
        ApplyTitleBarTheme();
    }

    private void ApplyTitleBarTheme()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        var dark = ThemeService.IsDark ? 1 : 0;

        Dwmapi.DwmSetWindowAttribute(_hwnd, Dwmapi.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        Dwmapi.DwmSetWindowAttribute(_hwnd, Dwmapi.DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref dark, sizeof(int));
    }

    /// <summary>
    /// 창을 앞으로 가져옵니다. 전역 단축키를 누르거나, 실행 중일 때 다시 실행하면 불립니다.
    /// </summary>
    public void BringToFront()
    {
        Show();

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();

        // 다른 창에 가려져 있을 때 확실히 앞으로 올립니다.
        Topmost = true;
        Topmost = false;

        if (MainTabs.SelectedIndex == 1)
        {
            GalleryList.Focus();
        }
        else
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        }
    }

    /// <summary>창을 닫으면 앱을 끝냅니다. 남겨 둘 것은 닫기 전에 저장합니다.</summary>
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        SaveWindowBounds();
    }

    private void Window_Activated(object? sender, EventArgs e)
    {
        // 다른 작업을 하다 돌아오면 사라진 항목이 없는지 다시 봅니다(1분에 한 번).
        RefreshMissingFlags();

        // 최근 실행·실행 빈도 정렬은 다른 앱을 쓰다 돌아왔을 때 순서가 바뀌어야 자연스럽습니다.
        if (_settings.SortMode is SortMode.RecentLaunch or SortMode.LaunchCount)
        {
            RefreshView();
        }
    }

    /// <summary>아래 줄 왼쪽에 잠깐 안내 문구를 보여줍니다.</summary>
    private void ShowMessage(string text, double seconds = 6)
    {
        MessageText.Text = text;
        MessageText.Visibility = Visibility.Visible;

        _messageTimer.Stop();
        _messageTimer.Interval = TimeSpan.FromSeconds(seconds);
        _messageTimer.Start();
    }

    /// <summary>저장해 둔 위치와 크기로 창을 엽니다. 화면 밖이면 무시합니다.</summary>
    private void RestoreWindowBounds()
    {
        if (_settings is not
            {
                WindowLeft: { } left,
                WindowTop: { } top,
                WindowWidth: { } width,
                WindowHeight: { } height,
            })
        {
            return;
        }

        // 모니터 구성을 바꿔서 창이 화면 밖으로 나갔으면 무시하고 기본 위치로 엽니다.
        var screenLeft = SystemParameters.VirtualScreenLeft;
        var screenTop = SystemParameters.VirtualScreenTop;
        var screenRight = screenLeft + SystemParameters.VirtualScreenWidth;
        var screenBottom = screenTop + SystemParameters.VirtualScreenHeight;

        var visible = left < screenRight - 80 &&
                      left + width > screenLeft + 80 &&
                      top < screenBottom - 40 &&
                      top + height > screenTop + 40;

        if (!visible)
        {
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = left;
        Top = top;
        Width = width;
        Height = height;

        if (_settings.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void SaveWindowBounds()
    {
        _settings.WindowMaximized = WindowState == WindowState.Maximized;

        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;

        _settings.WindowLeft = bounds.Left;
        _settings.WindowTop = bounds.Top;
        _settings.WindowWidth = bounds.Width;
        _settings.WindowHeight = bounds.Height;

        SaveSettings();
    }

    /// <summary>
    /// 항목 상태를 갱신합니다. 실행할 수 없는 항목을 표시하고,
    /// 바로가기가 사라져도 실행할 수 있도록 대상 정보를 기억해 둡니다.
    /// 자주 하면 느리므로 1분에 한 번만 봅니다.
    /// </summary>
    private void RefreshMissingFlags()
    {
        if (DateTimeOffset.Now - _missingCheckedAt < TimeSpan.FromMinutes(1))
        {
            return;
        }

        _missingCheckedAt = DateTimeOffset.Now;
        var changed = false;

        foreach (var item in _items)
        {
            changed |= UpdateItemState(item);
        }

        if (changed)
        {
            Save();
        }
    }

    /// <summary>항목 상태를 갱신하고, 대체 실행 정보가 바뀌었으면 true를 돌려줍니다.</summary>
    private static bool UpdateItemState(ShortcutItem item)
    {
        // 스토어 앱처럼 셸 항목으로 등록된 것은 파일이 없으므로 확인할 대상이 없습니다.
        if (item.IsShellTarget)
        {
            item.IsMissing = false;
            item.IsShortcutDetached = false;
            return false;
        }

        try
        {
            var extension = Path.GetExtension(item.Path);
            var isShortcutFile = extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
                                 extension.Equals(".url", StringComparison.OrdinalIgnoreCase);

            var shortcutExists = File.Exists(item.Path);
            var resolved = shortcutExists && isShortcutFile ? ShellLinkResolver.Resolve(item.Path) : null;

            var resolvedTarget = resolved?.TargetPath;
            var targetAlive = resolvedTarget is not null &&
                              (File.Exists(resolvedTarget) || Directory.Exists(resolvedTarget));
            var url = resolved?.Url;

            // 대상도 주소도 없는 바로가기(스토어 앱)는 셸이 따로 실행하므로 정상으로 봅니다.
            var storeStyle = shortcutExists && resolved is not null && resolvedTarget is null && string.IsNullOrEmpty(url);
            var shortcutUsable = shortcutExists && (targetAlive || !string.IsNullOrEmpty(url) || storeStyle);

            var changed = false;

            // 바로가기가 살아 있을 때만 기억해 둘 대상을 갱신합니다.
            // 사라진 뒤에는 이전에 기억해 둔 값을 그대로 써야 실행할 수 있습니다.
            if (shortcutExists)
            {
                string? fallbackTarget;
                string? fallbackArguments = null;
                string? fallbackWorkingDirectory = null;

                if (!isShortcutFile)
                {
                    // 실행 파일을 직접 등록한 항목은 그 파일 자체가 대상입니다.
                    fallbackTarget = item.Path;
                }
                else if (targetAlive)
                {
                    fallbackTarget = resolvedTarget;
                    fallbackArguments = resolved?.Arguments;
                    fallbackWorkingDirectory = resolved?.WorkingDirectory;
                }
                else if (!string.IsNullOrEmpty(url))
                {
                    // steam:// 같은 주소는 그대로 다시 실행할 수 있습니다.
                    fallbackTarget = null;
                }
                else
                {
                    // 대상이 사라졌으면 설치 폴더에서 대신 실행할 파일을 찾아 둡니다.
                    fallbackTarget = InstallLocationResolver.FindExecutables(item.Path).FirstOrDefault();
                }

                // 아이콘을 다시 뽑을 때 쓸 원본도 기억해 둡니다.
                var fallbackIcon = InstallLocationResolver.ResolveIconFile(item.Path)
                                   ?? (fallbackTarget is { Length: > 0 } ? fallbackTarget : null);

                changed |= Assign(fallbackTarget, item.FallbackTarget, value => item.FallbackTarget = value);
                changed |= Assign(fallbackArguments, item.FallbackArguments, value => item.FallbackArguments = value);
                changed |= Assign(
                    fallbackWorkingDirectory,
                    item.FallbackWorkingDirectory,
                    value => item.FallbackWorkingDirectory = value);
                changed |= Assign(fallbackIcon, item.FallbackIcon, value => item.FallbackIcon = value);
                changed |= Assign(url, item.FallbackUrl, value => item.FallbackUrl = value);
            }

            var hasFallback = item.FallbackTarget is not null || !string.IsNullOrEmpty(item.FallbackUrl);

            item.IsShortcutDetached = !shortcutUsable && hasFallback;
            item.IsMissing = !shortcutUsable && !hasFallback;

            return changed;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool Assign(string? next, string? current, Action<string?> assign)
    {
        if (string.Equals(next, current, StringComparison.Ordinal))
        {
            return false;
        }

        assign(next);
        return true;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initializing)
        {
            RefreshView();
        }
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                LaunchFirstVisible();
                e.Handled = true;
                break;

            case Key.Escape:
                HideSearchBox();
                e.Handled = true;
                break;
        }
    }

    /// <summary>평소에는 검색창을 숨겨 두고, 키보드를 누르면 그 글자로 검색을 시작합니다.</summary>
    private void Window_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (MainTabs.SelectedIndex != 0 ||
            Keyboard.FocusedElement is TextBoxBase or ComboBox or Button ||
            SearchBox.IsVisible ||
            string.IsNullOrEmpty(e.Text) ||
            char.IsControl(e.Text[0]))
        {
            return;
        }

        // '?'는 정보 창입니다. 검색어로 쓰는 경우가 거의 없습니다.
        if (e.Text == "?")
        {
            ShowAbout();
            e.Handled = true;
            return;
        }

        ShowSearchBox(e.Text);
        e.Handled = true;
    }

    private void ShowAbout()
    {
        new AboutDialog(_items.Count) { Owner = this }.ShowDialog();
    }

    /// <summary>
    /// 설정 창을 열고, 국가 코드가 바뀌었으면 바로 반영합니다.
    /// 바꾼 지역으로 다시 확인해야 결과가 눈에 보이므로 그 자리에서 한 번 더 조회합니다.
    /// </summary>
    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(_settings.FreeGameCountry, WindowsCountry()) { Owner = this };

        if (dialog.ShowDialog() != true ||
            string.Equals(_settings.FreeGameCountry, dialog.CountryCode, StringComparison.Ordinal))
        {
            return;
        }

        _settings.FreeGameCountry = dialog.CountryCode;
        SaveSettings();

        ShowMessage(dialog.CountryCode.Length == 0
            ? "국가 코드를 비웠습니다 · Windows 지역 설정을 따릅니다"
            : $"국가 코드를 {dialog.CountryCode}로 바꿨습니다 · 무료 게임을 다시 확인합니다");

        // 기한을 새로 받아야 하므로 '한 번 더 해 봤음' 표시도 지우고 다시 시작합니다.
        _freeGameRetried = false;
        _freeGames = [];
        ShowFreeGames();
        _ = LoadFreeGamesAsync();
    }

    private void ShowSearchBox(string initialText)
    {
        SearchBox.Visibility = Visibility.Visible;
        SearchHint.Visibility = Visibility.Collapsed;
        SearchBox.Text = initialText;
        SearchBox.CaretIndex = SearchBox.Text.Length;
        SearchBox.Focus();

        RefreshView();
    }

    private void HideSearchBox()
    {
        SearchBox.Clear();
        SearchBox.Visibility = Visibility.Collapsed;
        SearchHint.Visibility = Visibility.Visible;

        RefreshView();
    }

    /// <summary>정렬 버튼을 누르면 그 기준으로 다시 정렬하고 기억합니다.</summary>
    private void SortMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag } || !Enum.TryParse<SortMode>(tag, out var mode))
        {
            return;
        }

        _settings.SortMode = mode;
        ((ShortcutComparer)_view.CustomSort).Mode = mode;

        RefreshView();
        SaveSettings();

        if (mode == SortMode.Size)
        {
            // 아직 용량을 모르는 항목이 있으면 기다리지 않고 바로 계산합니다.
            _sizeScanHurry = true;
        }
    }

    private void SelectSortButton(SortMode mode)
    {
        var button = mode switch
        {
            SortMode.RecentLaunch => SortRecentButton,
            SortMode.LaunchCount => SortCountButton,
            SortMode.Size => SortSizeButton,
            _ => SortNameButton,
        };

        button.IsChecked = true;
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e) =>
        QueueAutoTileSizeUpdate();

    /// <summary>
    /// Windows 화면 정렬은 크기 변경 이벤트가 새 레이아웃보다 먼저 옵니다.
    /// 레이아웃이 끝난 다음 실제 뷰포트를 읽도록 한 번만 예약합니다.
    /// </summary>
    private void QueueAutoTileSizeUpdate()
    {
        if (_autoTileUpdateQueued)
        {
            return;
        }

        _autoTileUpdateQueued = true;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _autoTileUpdateQueued = false;

            if (MainTabs.SelectedIndex != 0)
            {
                return;
            }

            ShortcutList.UpdateLayout();
            UpdateAutoTileSize();
        }));
    }

    /// <summary>창 크기와 항목 수에 맞춰 아이콘 크기를 다시 계산합니다.</summary>
    private void UpdateAutoTileSize()
    {
        // 목록 안쪽(스크롤 영역)의 실제 크기를 씁니다. 테두리 때문에 몇 px 차이가 나면
        // 열이 하나 접혀서 오른쪽에 빈 공간이 생기기 때문입니다.
        var viewer = FindScrollViewer(ShortcutList);
        var width = viewer is { ViewportWidth: > 1 } ? viewer.ViewportWidth : ShortcutList.ActualWidth;
        var height = viewer is { ViewportHeight: > 1 } ? viewer.ViewportHeight : ShortcutList.ActualHeight;

        var layout = AutoTileSizer.Compute(
            width,
            height,
            _items.Count,
            VisualTreeHelper.GetDpi(this).DpiScaleX);

        TileIconSize = layout.IconSize;
        TileItemWidth = layout.ItemWidth;
        TileItemHeight = layout.ItemHeight;
        TileAreaMargin = new Thickness(0, layout.TopPadding, 0, 0);
    }

    /// <summary>목록 안쪽의 ScrollViewer를 찾습니다. 타일이 실제로 놓일 수 있는 크기를 알기 위해서입니다.</summary>
    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer viewer)
        {
            return viewer;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var index = 0; index < count; index++)
        {
            if (FindScrollViewer(VisualTreeHelper.GetChild(root, index)) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private void LaunchFirstVisible()
    {
        if (_view.Cast<ShortcutItem>().FirstOrDefault() is { } first)
        {
            ShortcutList.SelectedItem = first;
            LaunchItem(first);
        }
    }

    private void RefreshView()
    {
        _view.Refresh();
        UpdateStatus();
    }

    private void SaveSettings()
    {
        try
        {
            SettingsStore.Save(_settings);
        }
        catch (Exception)
        {
            // 설정을 저장하지 못해도 앱 동작은 계속됩니다.
        }
    }

    /// <summary>검색어가 이름이나 경로에 들어 있는 항목만 남깁니다.</summary>
    private bool MatchesSearch(object item)
    {
        if (item is not ShortcutItem shortcut)
        {
            return false;
        }

        var query = SearchBox.Text.Trim();
        if (query.Length == 0)
        {
            return true;
        }

        if (shortcut.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            shortcut.Path.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 자음만 입력했으면 초성으로도 찾습니다("ㅎㄹ" → "헬로").
        return KoreanInitials.IsConsonantOnly(query) &&
               KoreanInitials.From(shortcut.DisplayName).Contains(query, StringComparison.Ordinal);
    }

    private void MenuLaunch_Click(object sender, RoutedEventArgs e)
    {
        if (GetMenuItem(sender) is { } item)
        {
            LaunchItem(item);
        }
    }

    private void MenuPin_Click(object sender, RoutedEventArgs e)
    {
        if (GetMenuItem(sender) is null)
        {
            return;
        }

        // IsChecked가 IsPinned에 양방향으로 물려 있어 값은 이미 바뀌었습니다. 순서만 다시 잡습니다.
        RefreshView();
        Save();
    }

    /// <summary>아이콘을 자동으로 못 찾거나 다른 그림을 쓰고 싶을 때 직접 지정합니다.</summary>
    private void MenuSetIcon_Click(object sender, RoutedEventArgs e)
    {
        if (GetMenuItem(sender) is not { } item)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "아이콘으로 쓸 파일 선택",
            Filter = "아이콘·이미지·실행 파일|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.exe;*.dll|모든 파일|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        item.IconOverride = dialog.FileName;
        RefreshIcon(item);
    }

    private void MenuResetIcon_Click(object sender, RoutedEventArgs e)
    {
        if (GetMenuItem(sender) is not { } item)
        {
            return;
        }

        item.IconOverride = null;
        RefreshIcon(item);
    }

    private void RefreshIcon(ShortcutItem item)
    {
        // 같은 참조가 다시 들어오면 화면이 갱신되지 않으므로 한 번 비웁니다.
        item.Icon = null;
        item.Icon = ShortcutIconResolver.GetIcon(item);

        Save();
        UpdateStatus();
    }

    private void MenuRename_Click(object sender, RoutedEventArgs e)
    {
        if (GetMenuItem(sender) is not { } item)
        {
            return;
        }

        var dialog = new TextInputDialog("이름 바꾸기", "타일에 표시할 이름을 입력하세요.", item.DisplayName)
        {
            Owner = this,
        };

        if (dialog.ShowDialog() == true && dialog.Value.Length > 0)
        {
            item.DisplayName = dialog.Value;
            RefreshView();
            Save();
        }
    }

    private void MenuOpenTargetFolder_Click(object sender, RoutedEventArgs e)
    {
        if (GetMenuItem(sender) is not { } item)
        {
            return;
        }

        var directory = InstallLocationResolver.Resolve(item.Path);
        if (directory is null)
        {
            MessageBox.Show(
                this,
                "대상 폴더를 찾지 못했습니다.",
                "rabbit inside",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"")
        {
            UseShellExecute = true,
        });
    }

    private async void MenuComputeSize_Click(object sender, RoutedEventArgs e)
    {
        if (GetMenuItem(sender) is not { } item)
        {
            return;
        }

        // 직접 요청한 항목은 기억해 둔 값을 무시하고 다시 계산합니다.
        item.SizeAttemptedAt = null;
        MessageText.Text = $"{item.DisplayName} 설치 용량 계산 중";
        MessageText.Visibility = Visibility.Visible;

        await ComputeSizeAsync(item, CancellationToken.None);

        MessageText.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// 설치 용량은 폴더를 훑어야 하는 항목이 있어 오래 걸립니다.
    /// 창이 뜬 뒤 한 항목씩 천천히 채워 넣어 사용을 방해하지 않습니다.
    /// </summary>
    private async Task RunSizeScanLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var next = FindItemNeedingSize();

                if (next is null)
                {
                    if (_sizeScanHurry)
                    {
                        _sizeScanHurry = false;
                        MessageText.Visibility = Visibility.Collapsed;
                    }

                    await Task.Delay(IdleScanInterval, token);
                    continue;
                }

                await ComputeSizeAsync(next, token);

                if (!_sizeScanHurry)
                {
                    // 컴퓨터를 붐비게 하지 않도록 항목 사이에 쉬어 갑니다.
                    await Task.Delay(BackgroundScanInterval, token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 창을 닫는 중입니다.
        }
    }

    private ShortcutItem? FindItemNeedingSize()
    {
        var staleBefore = DateTimeOffset.Now - SizeRefreshInterval;

        return _items.FirstOrDefault(item =>
            !item.IsShellTarget &&
            (item.SizeAttemptedAt is null || item.SizeAttemptedAt < staleBefore));
    }

    private async Task ComputeSizeAsync(ShortcutItem item, CancellationToken token)
    {
        if (_sizeScanHurry)
        {
            MessageText.Text = $"{item.DisplayName} 용량 계산 중";
            MessageText.Visibility = Visibility.Visible;
        }

        var size = await InstallSizeReader.ComputeAsync(item, token);

        if (size is not null)
        {
            item.SizeBytes = size;
            item.SizeComputedAt = DateTimeOffset.Now;
        }

        item.SizeAttemptedAt = DateTimeOffset.Now;
        Save();

        if (_settings.SortMode == SortMode.Size)
        {
            RefreshView();
        }
    }

    private void MenuCopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (GetMenuItem(sender) is { } item)
        {
            Clipboard.SetText(item.Path);
        }
    }

    private void MenuRemove_Click(object sender, RoutedEventArgs e)
    {
        if (GetMenuItem(sender) is not { } item)
        {
            return;
        }

        RemoveItem(item, _view.IndexOf(item));
    }

    private static ShortcutItem? GetMenuItem(object sender) =>
        (sender as MenuItem)?.DataContext as ShortcutItem;

    /// <summary>타일 아이콘 한 변의 길이입니다. XAML에서 바인딩합니다.</summary>
    public double TileIconSize
    {
        get => _tileIconSize;
        private set
        {
            if (SetField(ref _tileIconSize, value))
            {
                RaisePropertyChanged(nameof(TileWidth));
            }
        }
    }

    /// <summary>이름을 표시하지 않으므로 타일 폭은 아이콘에 여백만 더합니다.</summary>
    public double TileWidth => _tileIconSize + 16;

    /// <summary>타일 한 칸의 폭입니다. 오른쪽에 빈 공간이 남지 않도록 계산합니다.</summary>
    public double TileItemWidth
    {
        get => _tileItemWidth;
        private set => SetField(ref _tileItemWidth, value);
    }

    /// <summary>타일 한 칸의 높이입니다. 남는 세로 공간을 줄 간격으로 나눠 넣습니다.</summary>
    public double TileItemHeight
    {
        get => _tileItemHeight;
        private set => SetField(ref _tileItemHeight, value);
    }

    /// <summary>아래쪽에 몰리는 빈 공간을 위아래로 나누기 위한 여백입니다.</summary>
    public Thickness TileAreaMargin
    {
        get => _tileAreaMargin;
        private set => SetField(ref _tileAreaMargin, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        RaisePropertyChanged(propertyName);
        return true;
    }

    private void RaisePropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>
    /// 고정한 항목을 항상 위에 두고, 그 안에서 선택한 기준으로 정렬합니다.
    /// 점수가 같으면 탐색기와 같은 이름 순서로 갈라 순서가 흔들리지 않게 합니다.
    /// </summary>
    private sealed class ShortcutComparer(SortMode mode) : IComparer
    {
        public SortMode Mode { get; set; } = mode;

        public int Compare(object? x, object? y)
        {
            if (x is not ShortcutItem a || y is not ShortcutItem b)
            {
                return 0;
            }

            if (a.IsPinned != b.IsPinned)
            {
                return a.IsPinned ? -1 : 1;
            }

            var primary = Mode switch
            {
                SortMode.RecentLaunch => CompareDescending(a.LastLaunchedAt, b.LastLaunchedAt),
                SortMode.LaunchCount => b.LaunchCount.CompareTo(a.LaunchCount),
                SortMode.Size => CompareDescending(a.SizeBytes, b.SizeBytes),
                _ => 0,
            };

            return primary != 0
                ? primary
                : LogicalStringComparer.Instance.Compare(a.DisplayName, b.DisplayName);
        }

        private static int CompareDescending<T>(T? a, T? b)
            where T : struct, IComparable<T>
        {
            if (a is null && b is null)
            {
                return 0;
            }

            if (a is null)
            {
                return 1;
            }

            if (b is null)
            {
                return -1;
            }

            return b.Value.CompareTo(a.Value);
        }
    }

    /// <summary>드롭된 데이터에서 등록할 수 있는 항목만 골라냅니다.</summary>
    private static List<DropTarget> GetDroppedTargets(DragEventArgs e)
    {
        return DropPayloadReader.ReadTargets(e.Data)
            .Where(target => target.IsShellTarget
                             || AcceptedExtensions.Contains(
                                 System.IO.Path.GetExtension(target.Target),
                                 StringComparer.OrdinalIgnoreCase))
            .ToList();
    }
}
