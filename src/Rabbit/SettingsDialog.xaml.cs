using System.Windows;

namespace Rabbit;

/// <summary>
/// 설정 창입니다. 지금은 무료 게임을 어느 지역 기준으로 볼지만 정합니다.
/// 값을 비워 두면 Windows 지역 설정을 따릅니다.
/// </summary>
public partial class SettingsDialog : Window
{
    public SettingsDialog(string? countryCode, string windowsCountry)
    {
        InitializeComponent();

        CountryBox.Text = countryCode ?? string.Empty;
        CountryHint.Text =
            $"Epic 스토어는 지역마다 무료 목록이 다릅니다. 비워 두면 Windows 지역 설정({windowsCountry})을 따릅니다. " +
            "예: KR(한국), US(미국), JP(일본), GB(영국)";

        Loaded += (_, _) =>
        {
            CountryBox.Focus();
            CountryBox.SelectAll();
        };
    }

    /// <summary>확인을 누른 뒤의 국가 코드입니다. 빈 값이면 Windows 지역 설정을 따릅니다.</summary>
    public string CountryCode { get; private set; } = string.Empty;

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        CountryBox.Clear();
        ErrorText.Visibility = Visibility.Collapsed;
        CountryBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var text = CountryBox.Text.Trim().ToUpperInvariant();

        if (text.Length is not (0 or 2) || !text.All(char.IsAsciiLetter))
        {
            ErrorText.Text = "두 글자 국가 코드로 적어 주세요. 예: KR, US";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        CountryCode = text;
        DialogResult = true;
    }
}
