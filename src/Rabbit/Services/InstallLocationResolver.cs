using System.Collections.Concurrent;
using System.IO;

namespace Rabbit.Services;

/// <summary>
/// 바로가기가 가리키는 설치 폴더를 찾습니다.
/// 스토어(MSIX) 게임은 대상 경로가 비어 있어서, 바로가기에 적힌 아이콘 위치에서 역추적합니다.
/// 예: C:\Games\Starfield\Content\Config\Images\TaskBarIcon.ico → C:\Games\Starfield
/// </summary>
public static class InstallLocationResolver
{
    /// <summary>설치 루트가 아니라 자산 폴더인 이름들입니다.</summary>
    private static readonly HashSet<string> AssetFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "content", "config", "images", "assets", "resources", "icons", "logo", "textures",
    };

    private static readonly ConcurrentDictionary<string, string?> FolderCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, IReadOnlyList<string>> ExecutableCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, string?> LogoCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, string?> IconFileCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>게임 로고를 찾을 때 훑는 폴더 수와 깊이입니다. 큰 게임 폴더에서 오래 걸리지 않도록 제한합니다.</summary>
    private const int MaxDirectoriesToScan = 300;
    private const int MaxLogoSearchDepth = 3;

    /// <summary>옮겨진 게임을 다른 드라이브에서 찾을 때의 탐색 한계입니다.</summary>
    private const int MaxDrivesScanDirectories = 2000;
    private const int MaxMoveSearchDepth = 3;

    /// <summary>설치 폴더를 못 찾으면 null입니다.</summary>
    public static string? Resolve(string shortcutPath) =>
        FolderCache.GetOrAdd(shortcutPath, static path => Find(path));

    /// <summary>아이콘을 다시 뽑아 볼 실행 파일 후보들입니다(큰 것부터).</summary>
    public static IReadOnlyList<string> FindExecutables(string shortcutPath)
    {
        var root = Resolve(shortcutPath);

        return root is null ? [] : ExecutableCache.GetOrAdd(root, static directory => FindExecutablesIn(directory));
    }

    /// <summary>
    /// 설치 폴더 안의 게임 로고 이미지입니다.
    /// MSIX 게임은 실행 파일 아이콘이 게임 로고가 아니라 안내용 이미지인 경우가 있어,
    /// 패키지에 들어 있는 Logo*.png를 먼저 씁니다.
    /// </summary>
    public static string? FindLogoImage(string shortcutPath) =>
        LogoCache.GetOrAdd(shortcutPath, static path => FindLogo(path));

    /// <summary>
    /// 바로가기가 지정한 아이콘 파일의 실제 경로입니다.
    /// 게임을 다른 드라이브로 옮기면 예전 경로가 그대로 남아 있어서, 같은 상대 경로를 다른 드라이브에서도 찾아봅니다.
    /// </summary>
    public static string? ResolveIconFile(string shortcutPath) =>
        IconFileCache.GetOrAdd(shortcutPath, static path => FindIconFile(path));

    private static string? FindIconFile(string shortcutPath)
    {
        var resolved = ShellLinkResolver.Resolve(shortcutPath);

        return resolved.IconLocation is { Length: > 0 } iconLocation ? FindExisting(iconLocation) : null;
    }

    /// <summary>지정한 경로가 없으면 같은 상대 경로를 다른 고정 드라이브에서 찾아봅니다.</summary>
    private static string? FindExisting(string path)
    {
        if (File.Exists(path))
        {
            return path;
        }

        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root) || path.Length <= root.Length)
        {
            return null;
        }

        var relative = path[root.Length..];
        var drives = DriveInfo.GetDrives()
            .Where(drive => drive.IsReady &&
                            drive.DriveType is DriveType.Fixed or DriveType.Removable &&
                            !string.Equals(drive.Name, root, StringComparison.OrdinalIgnoreCase))
            .Select(drive => drive.Name)
            .ToList();

        // 1) 같은 상대 경로를 그대로 옮긴 경우 (D:\Games\X → F:\Games\X)
        foreach (var drive in drives)
        {
            var candidate = Path.Combine(drive, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        // 2) 중간 경로가 달라진 경우: 자산 폴더 바로 위(설치 폴더) 이름을 기준으로 찾습니다.
        //    예: D:\Games\Hell is Us\Content\... → 다른 드라이브 어딘가의 "Hell is Us\Content\..."
        var parts = relative.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var assetIndex = Array.FindIndex(parts, part => AssetFolderNames.Contains(part));

        if (assetIndex < 1)
        {
            return null;
        }

        var installFolderName = parts[assetIndex - 1];
        var tail = parts[(assetIndex - 1)..];

        foreach (var drive in drives)
        {
            if (FindUnderDrive(drive, installFolderName, tail) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// 드라이브를 몇 단계만 훑어서 이름이 같은 설치 폴더를 찾고, 그 아래 같은 하위 경로가 있는지 확인합니다.
    /// 이름만 보고 정하지 않고 파일이 실제로 있는지까지 보므로 엉뚱한 폴더를 고르지 않습니다.
    /// </summary>
    private static string? FindUnderDrive(string driveRoot, string folderName, string[] tail)
    {
        var pending = new Queue<(string Directory, int Depth)>();
        pending.Enqueue((driveRoot, 0));
        var visited = 0;

        while (pending.Count > 0 && visited < MaxDrivesScanDirectories)
        {
            var (directory, depth) = pending.Dequeue();
            visited++;

            string[] children;
            try
            {
                children = Directory.GetDirectories(directory);
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var child in children)
            {
                if (string.Equals(Path.GetFileName(child), folderName, StringComparison.OrdinalIgnoreCase))
                {
                    var candidate = Path.Combine([child, .. tail.Skip(1)]);

                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }

                if (depth < MaxMoveSearchDepth)
                {
                    pending.Enqueue((child, depth + 1));
                }
            }
        }

        return null;
    }

    private static string? FindLogo(string shortcutPath)
    {
        var root = Resolve(shortcutPath);
        if (root is null)
        {
            return null;
        }

        string? best = null;
        var bestArea = 0;
        var visited = 0;
        var pending = new Queue<(string Directory, int Depth)>();
        pending.Enqueue((root, 0));

        while (pending.Count > 0 && visited < MaxDirectoriesToScan)
        {
            var (directory, depth) = pending.Dequeue();
            visited++;

            string[] files;
            try
            {
                files = Directory.GetFiles(directory, "*.png");
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var file in files)
            {
                var name = Path.GetFileNameWithoutExtension(file);

                if (!name.Contains("logo", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("splash", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var size = ImageHeader.ReadPng(file);
                var area = size is null ? 0 : size.Value.Width * size.Value.Height;

                if (area > bestArea)
                {
                    bestArea = area;
                    best = file;
                }
            }

            if (depth >= MaxLogoSearchDepth)
            {
                continue;
            }

            string[] children;
            try
            {
                children = Directory.GetDirectories(directory);
            }
            catch (Exception)
            {
                continue;
            }

            // Content·Assets 같은 폴더를 먼저 봅니다.
            foreach (var child in children.OrderByDescending(c => AssetFolderNames.Contains(Path.GetFileName(c))))
            {
                pending.Enqueue((child, depth + 1));
            }
        }

        return best;
    }

    private static string? Find(string shortcutPath)
    {
        var resolved = ShellLinkResolver.Resolve(shortcutPath);

        // 일반 앱은 대상 폴더가 곧 설치 위치입니다.
        if (resolved.TargetDirectory is { } targetDirectory)
        {
            return targetDirectory;
        }

        // Steam 게임은 매니페스트에 설치 폴더가 적혀 있습니다.
        // .url의 아이콘은 Steam 캐시 폴더에 있어서 설치 위치의 단서가 되지 못합니다.
        var steamText = $"{resolved.Url} {resolved.Arguments} {resolved.TargetPath}";
        if (SteamLibrary.ExtractAppId(steamText) is { } appId &&
            SteamLibrary.FindInstalledFolder(appId) is { } steamFolder)
        {
            return steamFolder;
        }

        // 스토어(MSIX) 게임은 아이콘 위치에서 역추적합니다.
        if (shortcutPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) &&
            FindIconFile(shortcutPath) is { } iconFile)
        {
            var directory = Path.GetDirectoryName(iconFile);

            if (!string.IsNullOrEmpty(directory))
            {
                return PromoteToInstallRoot(directory);
            }
        }

        return null;
    }

    /// <summary>Content·Images 같은 자산 폴더는 건너뛰고 설치 루트까지 올라갑니다.</summary>
    private static string PromoteToInstallRoot(string directory)
    {
        var current = directory;

        while (Path.GetFileName(current) is { Length: > 0 } name && AssetFolderNames.Contains(name))
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

    private static IReadOnlyList<string> FindExecutablesIn(string root)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        var found = new List<string>();

        try
        {
            found.AddRange(Directory.EnumerateFiles(root, "*.exe", SearchOption.TopDirectoryOnly));

            // 실행 파일이 한 단계 아래(예: Binaries\Win64)에 있는 경우도 흔합니다.
            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                try
                {
                    found.AddRange(Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly));
                }
                catch (Exception)
                {
                    // 접근이 막힌 폴더는 건너뜁니다.
                }
            }
        }
        catch (Exception)
        {
            return [];
        }

        return found
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.Length)
            .Take(4)
            .Select(file => file.FullName)
            .ToList();
    }
}
