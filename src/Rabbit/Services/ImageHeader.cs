using System.IO;

namespace Rabbit.Services;

/// <summary>이미지를 실제로 디코딩하지 않고 헤더만 읽어 크기를 알아냅니다.</summary>
internal static class ImageHeader
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>PNG가 아니거나 읽지 못하면 null입니다.</summary>
    public static (int Width, int Height)? ReadPng(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var header = new byte[24];

            if (stream.Read(header, 0, header.Length) < header.Length ||
                !header.AsSpan(0, 8).SequenceEqual(PngSignature))
            {
                return null;
            }

            var width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
            var height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];

            return width > 0 && height > 0 ? (width, height) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
