using System.Windows;
using MCPhoto.App.Views;

namespace MCPhoto.App.Services;

/// <summary>비밀번호 확인 모달(PasswordPromptWindow) 표시. (보완#1)</summary>
public sealed class PasswordPromptDialogService : IPasswordPromptDialogService
{
    public bool Prompt(string expectedPassword)
    {
        var win = new PasswordPromptWindow(expectedPassword ?? string.Empty)
        {
            Owner = Application.Current?.MainWindow
        };
        return win.ShowDialog() == true;
    }
}
