using System;
using System.Windows;
using System.Windows.Input;

namespace MCPhoto.App.Views;

/// <summary>
/// 설정 진입 전 비밀번호 확인 모달. 입력이 기대 비밀번호와 일치하면 DialogResult=true. (보완#1 — 진입 전 게이트)
/// </summary>
public partial class PasswordPromptWindow : Window
{
    private readonly string _expected;

    public PasswordPromptWindow(string expected)
    {
        InitializeComponent();
        _expected = expected;
        Loaded += (_, _) => Pw.Focus();
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (string.Equals(Pw.Password, _expected, StringComparison.Ordinal))
        {
            DialogResult = true; // 창 닫힘
        }
        else
        {
            ErrorText.Text = "비밀번호가 일치하지 않습니다.";
            ErrorText.Visibility = Visibility.Visible;
            Pw.SelectAll();
            Pw.Focus();
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnConfirm(sender, e);
    }
}
