using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace Rabbit.Services;

/// <summary>DCInside에서 받은 첫 사진을 화면용 PNG 썸네일로만 저장합니다.</summary>
public static class ThumbnailCache
{
    private static readonly string DirectoryPath =
        Environment.GetEnvironmentVariable("RABBIT_THUMBNAIL_DIR") is { Length: > 0 } customDirectory
            ? Path.GetFullPath(customDirectory)
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Rabbit",
                "thumbnails");

    public static BitmapSource? TryLoad(string galleryKey, string postNumber)
    {
        var path = PathFor(galleryKey, postNumber);

        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var stream = File.OpenRead(path);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.DecodePixelWidth = 480;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static bool Store(string galleryKey, string postNumber, BitmapSource? image)
    {
        if (image is null)
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var path = PathFor(galleryKey, postNumber);
            var temporary = path + ".tmp";

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));

            using (var stream = File.Create(temporary))
            {
                encoder.Save(stream);
            }

            File.Move(temporary, path, true);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string PathFor(string galleryKey, string postNumber)
    {
        var identity = $"{galleryKey}:{postNumber}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return Path.Combine(DirectoryPath, hash + ".png");
    }
}
