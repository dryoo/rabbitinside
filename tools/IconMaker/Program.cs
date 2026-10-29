using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IconMaker;

/// <summary>
/// 토끼 이모지(🐇)를 그려서 앱 아이콘(app.ico)을 만듭니다.
/// 색 글리프로 그려지는지 먼저 확인하고, 단색이면 배경을 깔아 아이콘처럼 보이게 합니다.
/// </summary>
internal static class Program
{
    private const string Glyph = "\U0001F407";   // 🐇 RABBIT
    private const int Canvas = 256;

    private static readonly int[] IconSizes = [16, 24, 32, 48, 64, 128, 256];

    /// <summary>인자를 주지 않으면 저장소 뿌리에서 실행한 것으로 보고 이 위치에 씁니다.</summary>
    private const string DefaultOutput = @"src\Rabbit\app.ico";

    [STAThread]
    private static void Main(string[] args)
    {
        // 출력 위치는 인자로 받습니다. 특정 PC의 경로를 코드에 박아 두지 않습니다.
        var output = Path.GetFullPath(args.Length > 0 ? args[0] : DefaultOutput);

        if (args.Length == 0 && !File.Exists(output))
        {
            Console.WriteLine("출력할 .ico 경로를 인자로 주세요.");
            Console.WriteLine(@"예: dotnet run --project tools/IconMaker/IconMaker.csproj -- src\Rabbit\app.ico");
            return;
        }

        var plain = RenderGlyph(Canvas, background: null, glyphBrush: Brushes.Black);
        var stats = Analyze(plain);

        Console.WriteLine($"글리프 = {Glyph} (U+{(int)char.ConvertToUtf32(Glyph, 0):X4})");
        Console.WriteLine($"  그려진 픽셀 = {stats.DrawnPixels}");
        Console.WriteLine($"  색 종류(대략) = {stats.DistinctColors}");
        Console.WriteLine($"  색 글리프 여부 = {stats.IsColored} (채널 최대 차이 {stats.MaxChannelSpread})");

        // 색 글리프면 그대로, 단색이면 둥근 배경 위에 흰 토끼로 그립니다.
        var frames = IconSizes
            .Select(size => stats.IsColored
                ? RenderGlyph(size, background: null, glyphBrush: Brushes.Black)
                : RenderGlyph(
                    size,
                    background: new SolidColorBrush(Color.FromRgb(0xF0, 0xA7, 0x3B)),
                    glyphBrush: Brushes.White))
            .ToList();

        File.WriteAllBytes(output, BuildIcon(frames));

        Console.WriteLine($"  저장 = {output} ({new FileInfo(output).Length / 1024} KB)");

        var check = Analyze(RenderGlyph(32, background: null, glyphBrush: Brushes.Black));
        Console.WriteLine($"  32px 확인: 그려진 픽셀 {check.DrawnPixels}");
    }

    /// <summary>지정한 크기로 글리프를 그립니다.</summary>
    private static BitmapSource RenderGlyph(int size, Brush? background, Brush glyphBrush)
    {
        var visual = new DrawingVisual();

        using (var context = visual.RenderOpen())
        {
            if (background is not null)
            {
                var radius = size * 0.22;
                context.DrawRoundedRectangle(
                    background,
                    null,
                    new Rect(0, 0, size, size),
                    radius,
                    radius);
            }

            // 글리프가 캔버스의 약 72%를 차지하도록 글꼴 크기를 잡습니다.
            var text = CreateText(size * 0.72, glyphBrush);
            var x = (size - text.Width) / 2;
            var y = (size - text.Height) / 2;

            context.DrawText(text, new Point(x, y));
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();

        return bitmap;
    }

    private static FormattedText CreateText(double fontSize, Brush brush) =>
        new(
            Glyph,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Emoji"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            fontSize,
            brush,
            96);

    private static (int DrawnPixels, int DistinctColors, bool IsColored, int MaxChannelSpread) Analyze(
        BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        converted.CopyPixels(pixels, stride, 0);

        var drawn = 0;
        var colors = new HashSet<int>();
        var maxSpread = 0;

        for (var index = 0; index < pixels.Length; index += 4)
        {
            if (pixels[index + 3] <= 16)
            {
                continue;
            }

            drawn++;

            int blue = pixels[index], green = pixels[index + 1], red = pixels[index + 2];
            colors.Add((red << 16) | (green << 8) | blue);

            var spread = Math.Max(red, Math.Max(green, blue)) - Math.Min(red, Math.Min(green, blue));
            maxSpread = Math.Max(maxSpread, spread);
        }

        return (drawn, colors.Count, maxSpread > 24, maxSpread);
    }

    /// <summary>PNG 프레임들로 .ico 파일을 만듭니다(Windows Vista 이상에서 지원).</summary>
    private static byte[] BuildIcon(IReadOnlyList<BitmapSource> frames)
    {
        var pngs = frames.Select(EncodePng).ToList();

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write((short)0);
        writer.Write((short)1);                    // 1 = 아이콘
        writer.Write((short)frames.Count);

        var offset = 6 + (frames.Count * 16);

        for (var index = 0; index < frames.Count; index++)
        {
            var size = frames[index].PixelWidth;

            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);                 // 팔레트 없음
            writer.Write((byte)0);
            writer.Write((short)1);                // 색 평면
            writer.Write((short)32);               // 비트 수
            writer.Write(pngs[index].Length);
            writer.Write(offset);

            offset += pngs[index].Length;
        }

        foreach (var png in pngs)
        {
            writer.Write(png);
        }

        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));

        using var stream = new MemoryStream();
        encoder.Save(stream);

        return stream.ToArray();
    }
}
