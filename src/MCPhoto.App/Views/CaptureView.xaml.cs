using System.Windows;
using System.Windows.Controls;
using MCPhoto.App.Imaging;
using MCPhoto.App.ViewModels;

namespace MCPhoto.App.Views;

/// <summary>촬영/카운트다운. CaptureViewModel이 카메라·시퀀스를 제어, View는 프리뷰 렌더. (WBS Step 9)</summary>
public partial class CaptureView : UserControl
{
    private CaptureViewModel? _vm;
    private CameraFramePresenter? _presenter;

    public CaptureView()
    {
        InitializeComponent();
        _presenter = new CameraFramePresenter(PreviewImage);
        DataContextChanged += OnDataContextChanged;
        Unloaded += (_, _) => _presenter?.Detach();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _vm = DataContext as CaptureViewModel;
        if (_vm is not null)
            _presenter?.Attach(_vm.Camera);
    }
}
