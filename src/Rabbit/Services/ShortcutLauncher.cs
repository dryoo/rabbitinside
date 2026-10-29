using System.Diagnostics;
using System.IO;
using Rabbit.Models;

namespace Rabbit.Services;

/// <summary>
/// 바로가기를 윈도우 셸에 넘겨 실행합니다. 대상 해석, 인수, 작업 디렉터리 처리를
/// 셸이 담당하므로 .lnk와 .url을 같은 방식으로 다룰 수 있습니다.
/// </summary>
public static class ShortcutLauncher
{
    public static void Launch(string target, bool isShellTarget = false)
    {
        if (isShellTarget)
        {
            // 스토어 앱 같은 가상 항목은 셸 구문 분석 이름을 탐색기에 넘겨 실행합니다.
            Process.Start(new ProcessStartInfo("explorer.exe", target)
            {
                UseShellExecute = true,
            });
            return;
        }

        Process.Start(new ProcessStartInfo(target)
        {
            UseShellExecute = true,
        });
    }

    /// <summary>
    /// 항목을 실행합니다. 바로가기가 살아 있으면 그대로 셸에 넘기고,
    /// 사라졌으면 기억해 둔 대상으로 대신 실행합니다.
    /// </summary>
    /// <returns>대체 대상으로 실행했으면 true입니다.</returns>
    public static bool Launch(ShortcutItem item)
    {
        if (item.IsShellTarget)
        {
            Launch(item.Path, isShellTarget: true);
            return false;
        }

        if (File.Exists(item.Path))
        {
            Launch(item.Path);
            return false;
        }

        if (item.FallbackUrl is { Length: > 0 } url)
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }

        if (item.FallbackTarget is { Length: > 0 } target)
        {
            var startInfo = new ProcessStartInfo(target) { UseShellExecute = true };

            if (!string.IsNullOrEmpty(item.FallbackArguments))
            {
                startInfo.Arguments = item.FallbackArguments;
            }

            if (!string.IsNullOrEmpty(item.FallbackWorkingDirectory) &&
                Directory.Exists(item.FallbackWorkingDirectory))
            {
                startInfo.WorkingDirectory = item.FallbackWorkingDirectory;
            }

            Process.Start(startInfo);
            return true;
        }

        // 대체 대상도 없으면 원래 경로로 시도해서 오류를 보여줍니다.
        Launch(item.Path);
        return false;
    }
}
