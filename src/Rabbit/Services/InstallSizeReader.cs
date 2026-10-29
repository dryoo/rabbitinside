using System.IO;
using System.Text.Json;
using Rabbit.Models;

namespace Rabbit.Services;

/// <summary>
/// 설치 용량을 알아냅니다. 폴더를 매번 훑으면 오래 걸리므로
/// Steam과 Epic처럼 매니페스트가 있는 경우 그 값을 먼저 씁니다.
/// </summary>
public static class InstallSizeReader
{
    /// <summary>폴더를 훑을 때 안전 장치로 두는 파일 수 상한입니다.</summary>
    private const long MaxFilesToScan = 400_000;

    private static readonly HashSet<string> IntermediateFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "binaries", "x64", "x86", "win64", "win32",
        "engine", "shipping", "retail", "game", "games",
    };

    public static Task<long?> ComputeAsync(ShortcutItem item, CancellationToken token = default) =>
        Task.Run(() => Compute(item, token), token);

    private static long? Compute(ShortcutItem item, CancellationToken token)
    {
        if (item.IsShellTarget)
        {
            return null;
        }

        try
        {
            var size = TrySteamSize(item.Path) ?? TryEpicSize(item.Path);
            if (size is not null)
            {
                return size;
            }

            token.ThrowIfCancellationRequested();
            return TryDirectorySize(item.Path, token);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Steam 매니페스트에서 설치 용량을 읽습니다. 폴더를 훑지 않아 즉시 끝납니다.</summary>
    private static long? TrySteamSize(string shortcutPath) =>
        FindSteamAppId(shortcutPath) is { } appId ? SteamLibrary.ReadSizeOnDisk(appId) : null;

    /// <summary>Epic 매니페스트에서 설치 용량과 설치 위치를 읽습니다.</summary>
    private static long? TryEpicSize(string shortcutPath)
    {
        var directory = InstallLocationResolver.Resolve(shortcutPath);
        if (directory is null)
        {
            return null;
        }

        foreach (var manifest in EnumerateEpicManifests())
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(manifest));
                var root = document.RootElement;

                if (!root.TryGetProperty("InstallLocation", out var locationElement) ||
                    !root.TryGetProperty("InstallSize", out var sizeElement))
                {
                    continue;
                }

                var location = locationElement.GetString();
                if (!string.IsNullOrEmpty(location) &&
                    IsSameOrChild(directory, location) &&
                    sizeElement.TryGetInt64(out var size))
                {
                    return size;
                }
            }
            catch (Exception)
            {
                // 매니페스트 하나가 깨져도 나머지는 계속 봅니다.
            }
        }

        return null;
    }

    /// <summary>매니페스트가 없는 일반 설치는 대상 폴더를 훑어서 합산합니다.</summary>
    private static long? TryDirectorySize(string shortcutPath, CancellationToken token)
    {
        var directory = InstallLocationResolver.Resolve(shortcutPath);
        if (directory is null)
        {
            return null;
        }

        var total = ComputeDirectorySize(PromoteToInstallRoot(directory), token);
        return total > 0 ? total : null;
    }

    /// <summary>
    /// 게임 폴더\bin\x64 처럼 실행 파일이 깊숙이 있는 경우가 많아,
    /// 이름이 뻔한 중간 폴더는 건너뛰고 설치 루트까지 올라갑니다.
    /// </summary>
    private static string PromoteToInstallRoot(string directory)
    {
        var current = directory;

        while (Path.GetFileName(current) is { Length: > 0 } name &&
               IntermediateFolderNames.Contains(name))
        {
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent))
            {
                break;
            }

            current = parent;
        }

        return current;
    }

    private static long ComputeDirectorySize(string root, CancellationToken token)
    {
        long total = 0;
        long fileCount = 0;
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();

            string[] entries;
            try
            {
                entries = Directory.GetFileSystemEntries(pending.Pop());
            }
            catch (Exception)
            {
                // 접근이 막힌 폴더는 건너뜁니다.
                continue;
            }

            foreach (var entry in entries)
            {
                try
                {
                    var attributes = File.GetAttributes(entry);

                    // 정션과 심볼릭 링크를 따라가면 같은 폴더를 두 번 세거나 끝없이 돕니다.
                    if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        continue;
                    }

                    if (attributes.HasFlag(FileAttributes.Directory))
                    {
                        pending.Push(entry);
                        continue;
                    }

                    total += new FileInfo(entry).Length;

                    if (++fileCount > MaxFilesToScan)
                    {
                        return total;
                    }
                }
                catch (Exception)
                {
                    // 파일 하나를 못 읽어도 나머지는 계속 셉니다.
                }
            }
        }

        return total;
    }

    private static string? FindSteamAppId(string shortcutPath)
    {
        try
        {
            var extension = Path.GetExtension(shortcutPath);

            if (extension.Equals(".url", StringComparison.OrdinalIgnoreCase) && File.Exists(shortcutPath))
            {
                return SteamLibrary.ExtractAppId(File.ReadAllText(shortcutPath));
            }

            if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                var resolved = ShellLinkResolver.Resolve(shortcutPath);

                return SteamLibrary.ExtractAppId($"{resolved.TargetPath} {resolved.Arguments} {resolved.Url}");
            }
        }
        catch (Exception)
        {
            // 아래에서 null을 돌려줍니다.
        }

        return null;
    }

    private static IEnumerable<string> EnumerateEpicManifests()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic",
            "EpicGamesLauncher",
            "Data",
            "Manifests");

        return Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.item") : [];
    }

    private static bool IsSameOrChild(string candidate, string parent)
    {
        var a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        var b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));

        return a.Equals(b, StringComparison.OrdinalIgnoreCase) ||
               a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

}
