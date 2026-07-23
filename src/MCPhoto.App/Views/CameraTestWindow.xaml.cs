using System.Windows;
using MCPhoto.App.Imaging;
using MCPhoto.App.ViewModels;

namespace MCPhoto.App.Views;

/// <summary>
/// 카메라 설정 테스트 모달 창. 프리뷰 렌더는 CameraFramePresenter 재사용(CaptureView와 동일).
/// 창 닫힘 시 Presenter Detach(FrameReady 구독 해제) — 누수 방지. 카메라 정지는 dialog 서비스가 담당. (it9 §2.3)
/// </summary>
public partial class CameraTestWindow : Window
{
    private CameraFramePresenter? _presenter;

    public CameraTestWindow()
    {
        InitializeComponent();
        _presenter = new CameraFramePresenter(PreviewImage);
        DataContextChanged += OnDataContextChanged;
        Closed += (_, _) => _presenter?.Detach();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (DataContext is CameraTestViewModel vm)
            _presenter?.Attach(vm.Camera);
    }
}
