using System.Windows.Controls;
using System.Windows.Input;
using MCPhoto.App.ViewModels;

namespace MCPhoto.App.Views;

public partial class CutSelectView : UserControl
{
    public CutSelectView()
    {
        InitializeComponent();
    }

    // 항목 클릭 → 선택 토글(ListBox 기본 선택 대신 커스텀 토글 사용)
    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        if (DataContext is not CutSelectViewModel vm) return;

        var element = e.OriginalSource as System.Windows.FrameworkElement;
        var thumb = element?.DataContext as CutThumbnail;
        if (thumb is not null && vm.ToggleCutCommand.CanExecute(thumb))
            vm.ToggleCutCommand.Execute(thumb);
    }
}
