using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Rabbit.Services;

/// <summary>
/// Steam 설치 위치와 매니페스트를 다룹니다.
/// Steam 게임의 바로가기(.url)는 실행 대상이 steam:// 주소라서 설치 폴더를 아이콘 경로로 알 수 없습니다.
/// 대신 매니페스트(appmanifest_*.acf)에 설치 폴더와 용량이 적혀 있습니다.
/// </summary>
public static partial class SteamLibrary
{
    private const string PersonalKeyPath = @"Software\Valve\Steam";
    private const string MachineKeyPath = @"SOFTWARE\WOW6432Node\Valve\Steam";

    /// <summary>Steam 라이브러리 폴더들입니다(본체 + 추가 라이브러리).</summary>
    public static IEnumerable<string> GetRoots()
    {
        var root = ReadSteamRoot();
        if (root is null)
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (seen.Add(root))
        {
            yield return root;
        }

        var libraryFile = Path.Combine(root, "steamapps", "libraryfolders.vdf");
        var content = TryReadText(libraryFile);
        if (content is null)
        {
            yield break;
        }

        foreach (Match match in LibraryPathPattern().Matches(content))
        {
            var path = match.Groups[1].Value.Replace(@"\\", @"\");

            if (seen.Add(path))
            {
                yield return path;
            }
        }
    }

    /// <summary>글 안에 있는 steam://rungameid/&lt;번호&gt; 를 찾습니다.</summary>
    public static string? ExtractAppId(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var match = RunGamePattern().Match(text);

        return match.Success ? match.Groups[1].Value : null;
    }

    public static long? ReadSizeOnDisk(string appId)
    {
        var manifest = FindManifest(appId);
        var content = manifest is null ? null : TryReadText(manifest);
        if (content is null)
        {
            return null;
        }

        var match = SizeOnDiskPattern().Match(content);

        return match.Success && long.TryParse(match.Groups[1].Value, out var size) ? size : null;
    }

    /// <summary>게임이 실제로 설치된 폴더입니다(steamapps\common\&lt;설치 폴더&gt;).</summary>
    public static string? FindInstalledFolder(string appId)
    {
        foreach (var root in GetRoots())
        {
            var manifest = Path.Combine(root, "steamapps", $"appmanifest_{appId}.acf");
            var content = TryReadText(manifest);
            if (content is null)
            {
                continue;
            }

            var match = InstallDirectoryPattern().Match(content);
            if (!match.Success)
            {
                continue;
            }

            var folder = Path.Combine(root, "steamapps", "common", match.Groups[1].Value);

            if (Directory.Exists(folder))
            {
                return folder;
            }
        }

        return null;
    }

    private static string? FindManifest(string appId)
    {
        foreach (var root in GetRoots())
        {
            var manifest = Path.Combine(root, "steamapps", $"appmanifest_{appId}.acf");

            if (File.Exists(manifest))
            {
                return manifest;
            }
        }

        return null;
    }

    private static string? ReadSteamRoot()
    {
        var candidates = new (RegistryKey Hive, string Path)[]
        {
            (Registry.CurrentUser, PersonalKeyPath),
            (Registry.LocalMachine, MachineKeyPath),
        };

        foreach (var (hive, path) in candidates)
        {
            try
            {
                using var key = hive.OpenSubKey(path);
                var value = (key?.GetValue("SteamPath") ?? key?.GetValue("InstallPath")) as string;

                if (!string.IsNullOrEmpty(value) && Directory.Exists(value))
                {
                    // 레지스트리에는 c:/... 처럼 슬래시로 저장된 경우가 있습니다.
                    return value.Replace('/', '\\');
                }
            }
            catch (Exception)
            {
                // 다음 후보를 봅니다.
            }
        }

        return null;
    }

    private static string? TryReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    [GeneratedRegex("\"SizeOnDisk\"\\s*\"(\\d+)\"")]
    private static partial Regex SizeOnDiskPattern();

    [GeneratedRegex("\"installdir\"\\s*\"([^\"]+)\"")]
    private static partial Regex InstallDirectoryPattern();

    [GeneratedRegex("\"path\"\\s*\"([^\"]+)\"")]
    private static partial Regex LibraryPathPattern();

    [GeneratedRegex("steam://rungameid/(\\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex RunGamePattern();
}
