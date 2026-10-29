using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rabbit.Services;
using Drawing = System.Drawing;

namespace Rabbit;

/// <summary>'?'를 누르면 나오는 정보 창입니다.</summary>
public partial class AboutDialog : Window
{
    public AboutDialog(int shortcutCount)
    {
        InitializeComponent();

        AppIcon.Source = LoadAppIcon();
        SummaryText.Text =
            $"버전 {GetVersion()} · 바로가기 {shortcutCount}개";

        ShortcutsText.Text = string.Join(
            Environment.NewLine,
            "아무 키나 누르면 검색창이 열립니다",
            "Enter  실행 · Delete  목록에서 제거 · Esc  검색 닫기",
            "타일 우클릭  고정·이름 바꾸기·아이콘 지정·대상 폴더 열기·용량 계산·경로 복사·제거",
            "Ctrl+Alt+R  창 앞으로 가져오기",
            "?  이 창 열기",
            "아래 줄 ⚙  설정 (무료 게임 국가 코드)",
            "창을 닫으면 앱이 끝납니다");

        PathsText.Text = string.Join(
            Environment.NewLine,
            $"바로가기 목록  {ShortcutStore.StorePath}",
            $"설정            {SettingsStore.StorePath}");
    }

    private static string GetVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>실행 파일에 박힌 앱 아이콘을 그대로 가져옵니다.</summary>
    private static ImageSource? LoadAppIcon()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable))
        {
            return null;
        }

        try
        {
            using var icon = Drawing.Icon.ExtractAssociatedIcon(executable);
            if (icon is null)
            {
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();

            return source;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
