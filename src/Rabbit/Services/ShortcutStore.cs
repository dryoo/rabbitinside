using System.IO;
using System.Text.Json;
using Rabbit.Models;

namespace Rabbit.Services;

/// <summary>
/// 등록된 바로가기 목록을 JSON으로 저장하고 복원합니다.
/// 아이콘은 저장하지 않고 실행할 때마다 다시 읽습니다(대상이 업데이트되면 아이콘도 따라 바뀌어야 하므로).
/// </summary>
public static class ShortcutStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // 한글 이름이 \uXXXX로 저장되지 않도록 합니다.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string StorePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Rabbit",
        "shortcuts.json");

    public static bool Exists => File.Exists(StorePath);

    public static List<ShortcutItem> Load()
    {
        if (!Exists)
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(StorePath);
            return JsonSerializer.Deserialize<List<ShortcutItem>>(json, Options) ?? [];
        }
        catch (Exception)
        {
            // 파일이 깨졌거나 읽을 수 없으면 빈 목록으로 시작합니다.
            return [];
        }
    }

    public static void Save(IEnumerable<ShortcutItem> items)
    {
        var directory = Path.GetDirectoryName(StorePath)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(StorePath, JsonSerializer.Serialize(items.ToList(), Options));
    }
}
