using System.IO;

namespace Rabbit.Services;

/// <summary>첫 실행 때 바탕화면에서 바로가기를 찾아옵니다.</summary>
public static class DesktopScanner
{
    private static readonly string[] Extensions = [".lnk", ".url"];

    /// <summary>
    /// 사용자 바탕화면과 공용 바탕화면을 함께 봅니다. 설치 프로그램이 만든 바로가기는
    /// 공용 바탕화면에 놓이는 경우가 많습니다.
    /// </summary>
    public static IEnumerable<string> FindShortcuts()
    {
        var folders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        };

        foreach (var folder in folders)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                continue;
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(folder);
            }
            catch (Exception)
            {
                // 접근이 막힌 환경은 조용히 건너뜁니다.
                continue;
            }

            foreach (var file in files)
            {
                if (Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }
        }
    }
}
