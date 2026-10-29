using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace Rabbit.Services;

/// <summary>
/// 한 번 뽑은 아이콘을 PNG로 저장해 둡니다.
/// 바탕화면 바로가기를 지워도 아이콘이 남게 하려는 것입니다.
/// </summary>
public static class IconCache
{
    private static readonly string FolderPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Rabbit",
        "icons");

    public static void Store(string shortcutPath, BitmapSource icon)
    {
        try
        {
            Directory.CreateDirectory(FolderPath);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(icon));

            using var stream = File.Create(PathFor(shortcutPath));
            encoder.Save(stream);
        }
        catch (Exception)
        {
            // 캐시를 못 만들어도 앱 동작에는 지장이 없습니다.
        }
    }

    public static BitmapSource? Load(string shortcutPath)
    {
        var path = PathFor(shortcutPath);
        if (!File.Exists(path))
        {
            return null;
        }

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

    /// <summary>경로마다 파일 이름을 하나씩 갖도록 해시로 만듭니다.</summary>
    private static string PathFor(string shortcutPath)
    {
        var normalized = shortcutPath.ToLowerInvariant();
        var hash = SHA1.HashData(Encoding.Unicode.GetBytes(normalized));

        return Path.Combine(FolderPath, Convert.ToHexString(hash) + ".png");
    }
}
