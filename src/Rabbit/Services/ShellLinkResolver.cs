using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Rabbit.Interop;

namespace Rabbit.Services;

/// <summary>바로가기가 가리키는 대상 정보입니다.</summary>
public sealed record ResolvedShortcut(
    string? TargetPath = null,
    string? WorkingDirectory = null,
    string? Arguments = null,
    string? Url = null,
    string? IconLocation = null)
{
    public bool HasTarget => !string.IsNullOrEmpty(TargetPath) || !string.IsNullOrEmpty(Url);

    /// <summary>대상 폴더를 열거나 설치 위치를 추정할 때 쓰는 경로입니다.</summary>
    public string? TargetDirectory
    {
        get
        {
            if (!string.IsNullOrEmpty(WorkingDirectory) && Directory.Exists(WorkingDirectory))
            {
                return WorkingDirectory;
            }

            if (!string.IsNullOrEmpty(TargetPath))
            {
                var directory = Path.GetDirectoryName(TargetPath);
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                {
                    return directory;
                }
            }

            return null;
        }
    }
}

/// <summary>.lnk는 셸 링크 COM으로, .url은 텍스트로 해석합니다.</summary>
public static class ShellLinkResolver
{
    private const int BufferSize = 1024;

    public static ResolvedShortcut Resolve(string shortcutPath)
    {
        var extension = Path.GetExtension(shortcutPath);

        try
        {
            if (extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
            {
                return ResolveUrlFile(shortcutPath);
            }

            if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                return ResolveShellLink(shortcutPath);
            }
        }
        catch (Exception)
        {
            // 해석에 실패해도 실행은 셸에 맡기면 되므로 빈 결과를 돌려줍니다.
        }

        return new ResolvedShortcut();
    }

    private static ResolvedShortcut ResolveShellLink(string shortcutPath)
    {
        var link = (ShellLink.IShellLinkW)new ShellLink.CoShellLink();

        try
        {
            ((ShellLink.IPersistFile)link).Load(shortcutPath, 0);

            var target = new StringBuilder(BufferSize);
            link.GetPath(target, target.Capacity, IntPtr.Zero, ShellLink.SLR_NO_UI);

            var working = new StringBuilder(BufferSize);
            link.GetWorkingDirectory(working, working.Capacity);

            var arguments = new StringBuilder(BufferSize);
            link.GetArguments(arguments, arguments.Capacity);

            var icon = new StringBuilder(BufferSize);
            link.GetIconLocation(icon, icon.Capacity, out _);

            return new ResolvedShortcut(
                TargetPath: ToLongPath(NullIfEmpty(target.ToString())),
                WorkingDirectory: ToLongPath(NullIfEmpty(working.ToString())),
                Arguments: NullIfEmpty(arguments.ToString()),
                IconLocation: ToLongPath(NullIfEmpty(icon.ToString())));
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    /// <summary>
    /// 인터넷 바로가기(.url)는 ini 형식이라 줄을 직접 읽습니다.
    /// URL=은 실행 대상이고, IconFile=은 아이콘 파일입니다.
    /// Steam은 게임 아이콘을 여기에 적어 두는데, 셸은 이 값을 무시하고 일반 문서 아이콘을 돌려줍니다.
    /// </summary>
    private static ResolvedShortcut ResolveUrlFile(string path)
    {
        string? url = null;
        string? iconFile = null;

        foreach (var line in File.ReadAllLines(path))
        {
            if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
            {
                url = line[4..].Trim();
            }
            else if (line.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase))
            {
                // IconFile=C:\...\game.ico,0 처럼 뒤에 아이콘 번호가 붙습니다.
                var value = line[9..].Trim();
                var comma = value.LastIndexOf(',');

                iconFile = NullIfEmpty(comma > 1 ? value[..comma] : value);
            }
        }

        return new ResolvedShortcut(Url: NullIfEmpty(url ?? string.Empty), IconLocation: iconFile);
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// 셸 링크가 대상 경로를 8.3 단축 형식(C:\PROGRA~1\...)으로 돌려줄 때가 있어
    /// 화면에 보여주거나 폴더를 열 때 쓰기 좋도록 긴 경로로 되돌립니다.
    /// </summary>
    private static string? ToLongPath(string? path)
    {
        if (path is null)
        {
            return null;
        }

        var buffer = new StringBuilder(BufferSize);
        var length = Kernel32.GetLongPathName(path, buffer, (uint)buffer.Capacity);

        return length == 0 || length >= buffer.Capacity ? path : buffer.ToString();
    }
}
