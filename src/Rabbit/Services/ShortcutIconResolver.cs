using System.Windows.Media.Imaging;
using Rabbit.Models;
using System.IO;

namespace Rabbit.Services;

/// <summary>
/// 타일에 쓸 아이콘을 고릅니다.
/// 보통은 셸이 .lnk를 해석해 준 아이콘을 그대로 쓰고, 그게 너무 작으면
/// 설치 폴더의 실행 파일에서 다시 뽑아 봅니다.
/// </summary>
public static class ShortcutIconResolver
{
    /// <summary>이 정도 크기면 충분하다고 보는 기준입니다.</summary>
    private const int GoodEnoughSize = 128;

    public static BitmapSource? GetIcon(ShortcutItem item)
    {
        // 사용자가 직접 고른 아이콘이 있으면 그것을 씁니다.
        if (item.IconOverride is { Length: > 0 } overridePath && File.Exists(overridePath))
        {
            var custom = LoadIconFile(overridePath);

            if (custom is not null)
            {
                return custom;
            }
        }

        // 바로가기가 사라졌으면 저장해 둔 아이콘을 씁니다.
        if (!item.IsShellTarget && !File.Exists(item.Path))
        {
            if (IconCache.Load(item.Path) is { } cached)
            {
                return cached;
            }

            // 기억해 둔 아이콘 원본(스팀 캐시 아이콘 등)에서 다시 뽑습니다.
            if (item.FallbackIcon is { Length: > 0 } iconSource &&
                LoadIconFile(iconSource) is { } fromIcon)
            {
                return fromIcon;
            }

            // 저장해 둔 아이콘이 없으면 기억해 둔 대상에서 다시 뽑습니다.
            if (item.FallbackTarget is { Length: > 0 } fallbackPath &&
                ShellIconLoader.GetIcon(fallbackPath) is { } fromTarget)
            {
                return fromTarget;
            }
        }

        var shellIcon = ShellIconLoader.GetIcon(item.Path, item.IsShellTarget);

        if (item.IsShellTarget)
        {
            return shellIcon;
        }

        var explicitIcon = LoadExplicitIcon(item.Path);
        var resolved = ShellLinkResolver.Resolve(item.Path);

        // 대상 경로가 있으면 셸이 해석한 아이콘이 가장 정확합니다. 지정된 아이콘이 더 크면 그것을 씁니다.
        var best = resolved.TargetPath is not null ? Larger(explicitIcon, shellIcon) : explicitIcon;

        if (LargestSide(best) < GoodEnoughSize)
        {
            // 스토어 게임과 .url 바로가기는 셸이 일반 문서 아이콘을 돌려줄 때가 있습니다.
            // 패키지에 들어 있는 로고 이미지와 실행 파일 아이콘을 차례로 봅니다.
            if (InstallLocationResolver.FindLogoImage(item.Path) is { } logoPath)
            {
                best = Larger(best, ShellIconLoader.LoadImageFile(logoPath));
            }

            foreach (var executable in InstallLocationResolver.FindExecutables(item.Path))
            {
                best = Larger(best, ShellIconLoader.GetIcon(executable));
            }
        }

        return Larger(best, shellIcon);
    }

    /// <summary>
    /// 바로가기가 직접 지정한 아이콘 파일을 읽습니다.
    /// .ico는 셸이 가장 큰 프레임을 골라 주지만, .png는 셸이 파일 종류 아이콘을 주므로 직접 읽어야 합니다.
    /// </summary>
    private static BitmapSource? LoadExplicitIcon(string shortcutPath)
        => InstallLocationResolver.ResolveIconFile(shortcutPath) is { } iconFile
            ? LoadIconFile(iconFile)
            : null;

    private static BitmapSource? LoadIconFile(string path)
        => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".bmp" => ShellIconLoader.LoadImageFile(path),
            _ => ShellIconLoader.GetIcon(path),
        };

    private static BitmapSource? Larger(BitmapSource? first, BitmapSource? second) =>
        LargestSide(second) > LargestSide(first) ? second : first;

    private static int LargestSide(BitmapSource? source) =>
        source is null ? 0 : Math.Max(source.PixelWidth, source.PixelHeight);
}
