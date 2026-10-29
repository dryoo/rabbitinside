using System.Windows;
using Microsoft.Win32;

namespace Rabbit.Services;

/// <summary>
/// Windows 설정(앱 모드)에 맞춰 라이트/다크 색을 바꿉니다.
/// 테마 사전 전체를 교체하는 방식이라 실행 중에 바꿔도 즉시 반영됩니다.
/// </summary>
public static class ThemeService
{
    private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string LightSource = "Themes/Light.xaml";
    private const string DarkSource = "Themes/Dark.xaml";

    public static bool IsDark { get; private set; }

    /// <summary>Windows 설정에서 "앱 모드" 값을 읽습니다. 못 읽으면 라이트로 봅니다.</summary>
    public static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);

            // AppsUseLightTheme: 1 = 라이트, 0 = 다크
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void ApplySystemTheme() => Apply(IsSystemDark());

    public static void Apply(bool dark)
    {
        var application = Application.Current;
        if (application is null)
        {
            return;
        }

        var dictionaries = application.Resources.MergedDictionaries;
        var source = dark ? DarkSource : LightSource;
        var replacement = CreateDictionary(source);

        for (var index = 0; index < dictionaries.Count; index++)
        {
            if (IsThemeDictionary(dictionaries[index]))
            {
                dictionaries[index] = replacement;
                IsDark = dark;
                return;
            }
        }

        dictionaries.Insert(0, replacement);
        IsDark = dark;
    }

    private static ResourceDictionary CreateDictionary(string source) => new()
    {
        Source = CreateUri(source),
    };

    /// <summary>
    /// 상대 경로로 두면 진입 어셈블리 기준으로 찾기 때문에, 라이브러리로 참조하는
    /// 경우 리소스를 못 찾습니다. 어셈블리를 명시한 pack URI를 씁니다.
    /// </summary>
    private static Uri CreateUri(string source)
    {
        var assemblyName = typeof(ThemeService).Assembly.GetName().Name;

        return new Uri($"/{assemblyName};component/{source}", UriKind.Relative);
    }

    private static bool IsThemeDictionary(ResourceDictionary dictionary)
    {
        var source = dictionary.Source?.OriginalString;

        return source is not null &&
               (source.EndsWith("Light.xaml", StringComparison.OrdinalIgnoreCase) ||
                source.EndsWith("Dark.xaml", StringComparison.OrdinalIgnoreCase));
    }
}
