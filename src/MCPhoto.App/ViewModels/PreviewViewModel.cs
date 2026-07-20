using CommunityToolkit.Mvvm.ComponentModel;
using MCPhoto.Core.Capture;
using MCPhoto.Core.Settings;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.ViewModels;

/// <summary>
/// 라이브 프리뷰 ViewModel. 카메라 서비스 시작/정지와 진단 fps 노출. (WBS Step 3)
/// 프레임 렌더(WriteableBitmap 커밋)는 성능상 View 코드비하인드가 담당한다.
/// 카메라(ICameraService)는 Singleton이므로 이 Transient VM이 소유·Dispose하지 않는다 —
/// 수명은 서비스 계층(StopAsync)과 DI 컨테이너(앱 종료)가 관리한다.
/// </summary>
public sealed partial class PreviewViewModel : ObservableObject
{
    private readonly ICameraService _camera;
    private readonly ISettingsService _settings;
    private readonly ILogger<PreviewViewModel>? _logger;

    [ObservableProperty]
    private double _fps;

    [ObservableProperty]
    private bool _cameraAvailable = true;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>프레임 소스(View가 구독해 재사용 WriteableBitmap에 커밋).</summary>
    public ICameraService Camera => _camera;

    public PreviewViewModel(ICameraService camera, ISettingsService settings, ILogger<PreviewViewModel>? logger = null)
    {
        _camera = camera;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>프리뷰 화면 진입 시 캡처 시작. targetAspect는 선택 프레임의 대표 슬롯 종횡비(기본 3:4).</summary>
    public async Task StartAsync(double targetAspect = 3.0 / 4.0)
    {
        var s = _settings.Current;
        bool ok = await _camera.StartAsync(s.CameraDevice, targetAspect, s.MirrorMode);
        CameraAvailable = ok;
        StatusMessage = ok ? string.Empty : "카메라를 찾을 수 없습니다. 연결을 확인해 주세요.";
        if (!ok)
            _logger?.LogWarning("프리뷰 시작 실패: 카메라 미연결/열기 실패");
    }

    /// <summary>화면 이탈 시 캡처 정지(리소스 해제, 완료 기준 trigger).</summary>
    public async Task StopAsync()
    {
        await _camera.StopAsync();
    }

    /// <summary>진단 fps 갱신(View 타이머에서 호출).</summary>
    public void RefreshFps() => Fps = _camera.CurrentFps;
}
