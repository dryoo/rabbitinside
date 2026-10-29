using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rabbit.Models;

namespace Rabbit.Services;

/// <summary>정렬 기준과 타일 크기 같은 앱 설정을 저장합니다.</summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string StorePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Rabbit",
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(StorePath))
            {
                return new AppSettings();
            }

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(StorePath), Options)
                   ?? new AppSettings();
        }
        catch (Exception)
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(StorePath)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(StorePath, JsonSerializer.Serialize(settings, Options));
    }
}
