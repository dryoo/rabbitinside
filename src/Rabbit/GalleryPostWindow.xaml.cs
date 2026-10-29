using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rabbit.Models;
using Rabbit.Services;

namespace Rabbit;

/// <summary>DCInside 게시글을 앱 안에서 읽기 위한 자체 리더 창입니다.</summary>
public partial class GalleryPostWindow : Window
{
    private readonly GalleryPost _post;
    private readonly CancellationTokenSource _cancellation = new();

    public GalleryPostWindow(GalleryPost post)
    {
        InitializeComponent();

        _post = post;
        Title = $"{post.Title} · rabbit inside";
        PostTitleText.Text = post.Title;
        PostMetaText.Text =
            $"{post.GalleryName} · {post.GalleryKindLabel} · {post.Author} · {post.DateText}";
        PostScoreText.Text =
            $"조회 {post.ViewCount:N0} · 추천 {post.RecommendCount:N0} · " +
            $"상대 조회 {post.ViewWeight:0.#}% · 추천률 {post.RecommendationRate:0.#}%";

        Loaded += Window_Loaded;
        Closed += (_, _) => _cancellation.Cancel();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var detail = await DcGalleryClient.FetchPostDetailAsync(
                _post,
                _cancellation.Token);

            if (_cancellation.IsCancellationRequested)
            {
                return;
            }

            PostMetaText.Text =
                $"{_post.GalleryName} · {_post.GalleryKindLabel} · " +
                $"{detail.Author} · {detail.DateText}";

            foreach (var block in detail.Blocks)
            {
                if (!string.IsNullOrWhiteSpace(block.Text))
                {
                    AddTextBlock(block.Text);
                }
                else if (block.ImageUrl is not null)
                {
                    AddImage(block.ImageUrl);
                }
            }

            LoadingText.Visibility = Visibility.Collapsed;
            await LoadImagesAsync();
        }
        catch (OperationCanceledException)
        {
            // 창을 닫았습니다.
        }
        catch (Exception)
        {
            LoadingText.Visibility = Visibility.Collapsed;
            ErrorText.Text = "본문을 불러오지 못했습니다. 잠시 후 다시 열어 주세요.";
            ErrorText.Visibility = Visibility.Visible;

            if (_post.Image is null)
            {
                return;
            }

            AddTextBlock("이미 받아 둔 썸네일만 표시합니다.");
            AddBitmap(_post.Image);
        }
    }

    private void AddTextBlock(string text)
    {
        ContentPanel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 14,
            LineHeight = 24,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });
    }

    private void AddImage(string imageUrl)
    {
        var status = new TextBlock
        {
            Text = "이미지 불러오는 중…",
            FontSize = 11,
            Opacity = 0.5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var image = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxHeight = 540,
            Tag = imageUrl,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

        var host = new Grid
        {
            MinHeight = 120,
            Margin = new Thickness(0, 6, 0, 14),
            Background = (Brush)FindResource("TileHover"),
        };
        host.Children.Add(status);
        host.Children.Add(image);
        ContentPanel.Children.Add(host);
    }

    private async Task LoadImagesAsync()
    {
        for (var index = 0; index < ContentPanel.Children.Count; index++)
        {
            if (ContentPanel.Children[index] is not Grid host ||
                host.Children.OfType<Image>().FirstOrDefault() is not { } image ||
                image.Tag is not string imageUrl)
            {
                continue;
            }

            try
            {
                var bitmap = await DcGalleryClient.FetchImageAsync(
                    imageUrl,
                    _post.Url,
                    1200,
                    _cancellation.Token);

                if (_cancellation.IsCancellationRequested)
                {
                    return;
                }

                if (bitmap is null)
                {
                    if (host.Children[0] is TextBlock failed)
                    {
                        failed.Text = "이미지를 불러오지 못했습니다.";
                    }

                    continue;
                }

                image.Source = bitmap;
                if (host.Children[0] is TextBlock loaded)
                {
                    loaded.Visibility = Visibility.Collapsed;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                if (host.Children[0] is TextBlock failed)
                {
                    failed.Text = "이미지를 불러오지 못했습니다.";
                }
            }

            await Task.Delay(250, _cancellation.Token);
        }
    }

    private void AddBitmap(BitmapSource bitmap)
    {
        ContentPanel.Children.Add(new Image
        {
            Source = bitmap,
            Stretch = Stretch.Uniform,
            MaxHeight = 540,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 6, 0, 14),
        });
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
