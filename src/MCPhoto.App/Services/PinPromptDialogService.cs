using System;
using System.Threading.Tasks;
using System.Windows;
using MCPhoto.App.Views;

namespace MCPhoto.App.Services;

/// <summary>설정·계정 관리 진입 PIN 게이트 모달(PinPromptWindow) 표시(it14 §5.4, it15 §6.2).</summary>
public sealed class PinPromptDialogService : IPinPromptDialogService
{
    public bool PromptVerify(Func<string, Task<bool>> verifyAsync)
    {
        var win = new PinPromptWindow(verifyAsync)
        {
            Owner = Application.Current?.MainWindow
        };
        return win.ShowDialog() == true;
    }

    public bool PromptSetup(Func<string, Task> setAsync)
    {
        var win = new PinPromptWindow(setAsync)
        {
            Owner = Application.Current?.MainWindow
        };
        return win.ShowDialog() == true;
    }
}
