using System.Windows;

namespace Rabbit;

/// <summary>이름 바꾸기처럼 한 줄 입력이 필요할 때 쓰는 작은 대화 상자입니다.</summary>
public partial class TextInputDialog : Window
{
    public TextInputDialog(string title, string prompt, string initialValue)
    {
        InitializeComponent();

        Title = title;
        PromptText.Text = prompt;
        ValueBox.Text = initialValue;

        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    public string Value => ValueBox.Text.Trim();

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
