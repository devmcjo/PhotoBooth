using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using MCPhoto.App.Imaging;
using MCPhoto.App.Services;
using MCPhoto.App.ViewModels;

namespace MCPhoto.App.Views;

/// <summary>
/// 카메라 설정 테스트 모달 창. 프리뷰 렌더는 CameraFramePresenter 재사용(CaptureView와 동일).
/// 창 닫힘 시 Presenter Detach(FrameReady 구독 해제) — 누수 방지. 카메라 정지는 dialog 서비스가 담당. (it9 §2.3)
/// <para>
/// it23: (a) 장치 목록 선택 변경을 VM 커맨드로 중계하고(전환은 async라 SelectionChanged에서 직접 await할 수 없다),
/// (b) 셔터 테스트로 수신한 인코딩 바이트를 <see cref="BitmapImage"/>로 디코드해 보여 준다.
/// </para>
/// ⚠️ 바이트→이미지 변환을 코드비하인드에 두는 이유: 신규 컨버터 리소스 키를 만들지 않기 위해서다(설계 §12.3).
/// 이 창은 이미 프리뷰 렌더를 코드비하인드에서 하고 있어 같은 자리에 두는 것이 일관된다.
/// </summary>
public partial class CameraTestWindow : Window
{
    /// <summary>
    /// 셔터 테스트 프리뷰의 디코드 폭 상한(px). 모달 안 프리뷰 영역은 이보다 크지 않으므로 화면상 손실이 없고,
    /// 24MP 원본을 전량 펼치는 것을 막는다(폭만 지정 → 종횡비 유지). 근거는 <see cref="OnVmPropertyChanged"/> 주석.
    /// </summary>
    private const int ShotPreviewDecodeWidth = 1600;

    private CameraFramePresenter? _presenter;
    private CameraTestViewModel? _vm;

    public CameraTestWindow()
    {
        InitializeComponent();
        _presenter = new CameraFramePresenter(PreviewImage);
        DataContextChanged += OnDataContextChanged;
        Closed += OnClosed;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // 이전 VM 구독 해제(DataContext가 바뀌어도 누수 없게 — 대칭 유지).
        if (_vm is not null) _vm.PropertyChanged -= OnVmPropertyChanged;

        _vm = DataContext as CameraTestViewModel;
        if (_vm is null) return;

        _presenter?.Attach(_vm.Camera);
        _vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _presenter?.Detach();
        if (_vm is not null) _vm.PropertyChanged -= OnVmPropertyChanged;
    }

    /// <summary>목록 선택 변경 → VM 커맨드(장치 전환은 Stop→Start/Connect 순서가 있는 async 작업이다).</summary>
    private void OnTargetSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm is null) return;
        if (sender is not ComboBox combo || combo.SelectedItem is not CameraTestTarget target) return;
        if (_vm.SelectTargetCommand.CanExecute(target))
            _vm.SelectTargetCommand.Execute(target);
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CameraTestViewModel.ShotImageBytes)) return;

        var bytes = _vm?.ShotImageBytes;
        if (bytes is null || bytes.Length == 0)
        {
            ShotImage.Source = null;
            return;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            // ⚠️ OnLoad + 메모리 스트림: 파일 핸들을 잡지 않고 즉시 디코드해 3초 후 폐기가 실제로 일어나게 한다.
            image.CacheOption = BitmapCacheOption.OnLoad;
            // ⚠️ 디코드 상한: DSLR 원본은 24MP(6000×4000)급이고 WPF는 이를 BGRA로 펼치므로 상한이 없으면
            //    3초짜리 모달 프리뷰 하나에 ~96MB가 잡힌다(저사양 키오스크에서 체감 정지). 폭만 지정하면
            //    종횡비는 유지되므로 "원본 비율 그대로 보여 준다"는 이 화면의 규격(설계 it23 §9.3)은 지켜진다
            //    — 그 규격이 금지한 것은 거울반전·크롭 같은 **기하 변형**이지 해상도 축소가 아니다.
            //    본 촬영 경로의 상한(ExternalCapturePolicy.MaxIngestLongEdge)과 같은 취지다.
            image.DecodePixelWidth = ShotPreviewDecodeWidth;
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();
            image.Freeze();
            ShotImage.Source = image;
        }
        catch (Exception)
        {
            // 손상 수신은 화면을 비우는 것으로 끝낸다(모달이 죽으면 설정 화면째 얼어붙는다).
            ShotImage.Source = null;
        }
    }
}
