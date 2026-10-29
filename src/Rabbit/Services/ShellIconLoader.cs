using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rabbit.Interop;

namespace Rabbit.Services;

/// <summary>
/// 바로가기의 아이콘을 셸에서 256px로 받아옵니다. .lnk는 셸이 대상을 해석하므로
/// 게임이나 앱의 원래 아이콘이 그대로 나옵니다.
/// </summary>
public static class ShellIconLoader
{
    /// <summary>고DPI에서도 흐려지지 않도록 큰 원본을 받아 화면에서 줄여 씁니다.</summary>
    private static readonly int[] PreferredImageLists = [Shell32.SHIL_JUMBO, Shell32.SHIL_EXTRALARGE];

    private static readonly ConcurrentDictionary<string, BitmapSource?> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>아이콘을 못 찾으면 null을 돌려줍니다.</summary>
    public static BitmapSource? GetIcon(string target, bool isShellTarget = false) =>
        Cache.GetOrAdd(
            isShellTarget ? "::shell::" + target : target,
            _ => Prepare(isShellTarget ? LoadFromShellTarget(target) : Load(target)));

    private static BitmapSource? Prepare(BitmapSource? source) =>
        source is null ? null : TrimTransparentBorder(source);

    /// <summary>PNG 같은 이미지 파일을 그대로 읽습니다. 셸 아이콘이 아니라 그림 자체를 쓸 때 필요합니다.</summary>
    public static BitmapSource? LoadImageFile(string path) =>
        Cache.GetOrAdd("::file::" + path, _ => Prepare(LoadBitmapFile(path)));

    private static BitmapSource? LoadBitmapFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();

            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 셸이 돌려주는 아이콘 중에는 큰 캔버스 한쪽에 그림이 작게 박히고 나머지가 투명한 경우가 있습니다.
    /// 그대로 쓰면 타일에서 아이콘이 작게 보이므로 투명 여백을 잘라냅니다.
    /// </summary>
    private static BitmapSource TrimTransparentBorder(BitmapSource source)
    {
        var width = source.PixelWidth;
        var height = source.PixelHeight;

        if (width < 2 || height < 2)
        {
            return source;
        }

        var readable = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        var stride = width * 4;
        var pixels = new byte[stride * height];
        readable.CopyPixels(pixels, stride, 0);

        int minX = width, minY = height, maxX = -1, maxY = -1;
        for (var y = 0; y < height; y++)
        {
            var row = y * stride;
            for (var x = 0; x < width; x++)
            {
                if (pixels[row + (x * 4) + 3] <= 8)
                {
                    continue;
                }

                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        // 완전히 투명하거나, 여백이 전혀 없으면 그대로 씁니다.
        if (maxX < 0 || (minX == 0 && minY == 0 && maxX == width - 1 && maxY == height - 1))
        {
            return source;
        }

        var cropped = new CroppedBitmap(
            source,
            new Int32Rect(minX, minY, maxX - minX + 1, maxY - minY + 1));
        cropped.Freeze();

        return cropped;
    }

    private static BitmapSource? Load(string path)
    {
        var info = new Shell32.SHFILEINFO();
        var result = Shell32.SHGetFileInfo(
            path,
            0,
            ref info,
            (uint)Marshal.SizeOf<Shell32.SHFILEINFO>(),
            Shell32.SHGFI_SYSICONINDEX);

        return result == IntPtr.Zero ? null : LoadFromImageList(info.iIcon);
    }

    /// <summary>스토어 앱처럼 파일 경로가 없는 항목은 구문 분석 이름을 PIDL로 바꿔 아이콘을 얻습니다.</summary>
    private static BitmapSource? LoadFromShellTarget(string parseName)
    {
        if (Shell32.SHParseDisplayName(parseName, IntPtr.Zero, out var pidl, 0, out _) != 0 ||
            pidl == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var info = new Shell32.SHFILEINFO();
            var result = Shell32.SHGetFileInfo(
                pidl,
                0,
                ref info,
                (uint)Marshal.SizeOf<Shell32.SHFILEINFO>(),
                Shell32.SHGFI_SYSICONINDEX | Shell32.SHGFI_PIDL);

            return result == IntPtr.Zero ? null : LoadFromImageList(info.iIcon);
        }
        finally
        {
            Shell32.CoTaskMemFree(pidl);
        }
    }

    private static BitmapSource? LoadFromImageList(int iconIndex)
    {
        foreach (var imageListIndex in PreferredImageLists)
        {
            var iid = typeof(Shell32.IImageList).GUID;
            if (Shell32.SHGetImageList(imageListIndex, ref iid, out var imageList) != 0 ||
                imageList is null)
            {
                continue;
            }

            try
            {
                if (imageList.GetIcon(iconIndex, Shell32.ILD_TRANSPARENT, out var hIcon) != 0 ||
                    hIcon == IntPtr.Zero)
                {
                    continue;
                }

                try
                {
                    var source = Imaging.CreateBitmapSourceFromHIcon(
                        hIcon,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());

                    // 다른 스레드에서도 안전하게 쓰도록 고정합니다.
                    source.Freeze();
                    return source;
                }
                finally
                {
                    Shell32.DestroyIcon(hIcon);
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(imageList);
            }
        }

        return null;
    }
}
