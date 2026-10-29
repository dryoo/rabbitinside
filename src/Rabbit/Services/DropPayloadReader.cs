using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using Rabbit.Interop;

namespace Rabbit.Services;

/// <summary>드롭된 항목 하나입니다.</summary>
/// <param name="Target">파일 경로이거나, 가상 항목이면 셸 구문 분석 이름입니다.</param>
/// <param name="DisplayName">셸이 알려준 표시 이름입니다. 없으면 null입니다.</param>
/// <param name="IsShellTarget">파일이 아니라 셸 항목(스토어 앱 등)이면 true입니다.</param>
public sealed record DropTarget(string Target, string? DisplayName, bool IsShellTarget);

/// <summary>
/// 드롭된 데이터를 해석합니다.
/// 탐색기에서 온 파일 드롭은 경로 목록(CF_HDROP)으로 들어오고,
/// 시작 메뉴의 가상 항목은 셸 ID 목록(CFSTR_SHELLIDLIST)으로 들어옵니다.
/// </summary>
public static class DropPayloadReader
{
    public const string ShellIdListFormat = "Shell IDList Array";

    public static IReadOnlyList<DropTarget> ReadTargets(IDataObject data)
    {
        // 실제 파일이 들어 있으면 그쪽이 가장 정확합니다.
        var fromFiles = ReadFileDrop(data);
        return fromFiles.Count > 0 ? fromFiles : ReadShellIdList(data);
    }

    private static IReadOnlyList<DropTarget> ReadFileDrop(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop) ||
            data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return [];
        }

        // 시작 메뉴에서 온 드롭은 존재하지 않는 임시 경로를 섞어 보내기도 합니다.
        return paths
            .Where(File.Exists)
            .Select(path => new DropTarget(path, Path.GetFileNameWithoutExtension(path), false))
            .ToList();
    }

    private static IReadOnlyList<DropTarget> ReadShellIdList(IDataObject data)
    {
        if (!data.GetDataPresent(ShellIdListFormat) ||
            data.GetData(ShellIdListFormat) is not Stream stream)
        {
            return [];
        }

        using (stream)
        {
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return ParseShellIdList(buffer.ToArray());
        }
    }

    /// <summary>
    /// CIDA 구조를 읽습니다.
    ///   UINT cidl;           항목 수
    ///   UINT aoffset[cidl+1]; 0번은 부모 폴더, 1번부터가 각 항목의 PIDL 위치
    /// 오프셋은 구조 시작점 기준 바이트 거리입니다.
    /// </summary>
    public static IReadOnlyList<DropTarget> ParseShellIdList(byte[] cida)
    {
        var results = new List<DropTarget>();

        if (cida.Length < 8)
        {
            return results;
        }

        var count = BitConverter.ToUInt32(cida, 0);
        if (count == 0 || cida.Length < 4 + ((count + 1) * 4))
        {
            return results;
        }

        var handle = GCHandle.Alloc(cida, GCHandleType.Pinned);
        try
        {
            var start = handle.AddrOfPinnedObject();

            var parentOffset = BitConverter.ToUInt32(cida, 4);
            if (parentOffset >= cida.Length)
            {
                return results;
            }

            var parent = start + (int)parentOffset;

            for (var index = 1u; index <= count; index++)
            {
                var childOffset = BitConverter.ToUInt32(cida, 4 + ((int)index * 4));
                if (childOffset >= cida.Length)
                {
                    continue;
                }

                var combined = Shell32.ILCombine(parent, start + (int)childOffset);
                if (combined == IntPtr.Zero)
                {
                    continue;
                }

                try
                {
                    if (Resolve(combined) is { } target)
                    {
                        results.Add(target);
                    }
                }
                finally
                {
                    Shell32.ILFree(combined);
                }
            }
        }
        finally
        {
            handle.Free();
        }

        return results;
    }

    private static DropTarget? Resolve(IntPtr pidl)
    {
        var path = new StringBuilder(1024);
        if (Shell32.SHGetPathFromIDList(pidl, path) && path.Length > 0)
        {
            var value = path.ToString();
            return new DropTarget(value, Path.GetFileNameWithoutExtension(value), false);
        }

        var parseName = GetName(pidl, Shell32.SIGDN_DESKTOPABSOLUTEPARSING);
        if (string.IsNullOrWhiteSpace(parseName))
        {
            return null;
        }

        return new DropTarget(parseName, GetName(pidl, Shell32.SIGDN_NORMALDISPLAY), true);
    }

    private static string? GetName(IntPtr pidl, int format)
    {
        if (Shell32.SHGetNameFromIDList(pidl, format, out var pointer) != 0 || pointer == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(pointer);
        }
        finally
        {
            Shell32.CoTaskMemFree(pointer);
        }
    }
}
