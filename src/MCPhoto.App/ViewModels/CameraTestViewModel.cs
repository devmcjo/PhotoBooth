using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPhoto.Core.Capture;
using MCPhoto.Core.Settings;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.ViewModels;

/// <summary>
/// 카메라 설정 테스트 모달 VM. 실제 촬영과 동일한 프리뷰·플래시·셔터를 재현하되 저장은 하지 않는다. (it9 §2.2 C1)
/// 카메라(ICameraService)는 DI Singleton 공유 — 오픈 시 StopAsync→StartAsync(선택 인덱스), 닫기 시 StopAsync.
/// </summary>
public sealed partial class CameraTestViewModel : ObservableObject
{
    /// <summary>첫 안정 프레임 대기 타임아웃(무한 로딩 방지, CaptureViewModel과 동일).</summary>
    private const int CameraReadyTimeoutMs = 8000;

    private readonly ICameraService _camera;
    private readonly ISettingsService _settings;
    private readonly int _deviceIndex;
    private readonly ILogger? _logger;
    private bool _shooting;

    /// <summary>View가 프리뷰 렌더(CameraFramePresenter)를 위해 구독.</summary>
    public ICameraService Camera => _camera;

    [ObservableProperty] private bool _flashActive;
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string _loadingMessage = "카메라 준비 중…";
    /// <summary>셔터 후 잠깐 노출되는 안내(저장되지 않음 재확인).</summary>
    [ObservableProperty] private string _shotNotice = string.Empty;

    /// <summary>View(Window)가 구독해 실제 창을 닫는다(VM은 Window 미참조).</summary>
    public event Action? RequestClose;

    public CameraTestViewModel(ICameraService camera, ISettingsService settings, int deviceIndex, ILogger? logger = null)
    {
        _camera = camera;
        _settings = settings;
        _deviceIndex = deviceIndex;
        _logger = logger;
    }

    /// <summary>모달 오픈 시 호출: 기존 점유 해제 후 선택 인덱스로 시작(StartAsync는 running이면 무시하므로 Stop 선행).</summary>
    public async Task StartAsync()
    {
        IsLoading = true;
        LoadingMessage = "카메라 준비 중…";
        try
        {
            await _camera.StopAsync();
            var s = _settings.Current;
            bool ok = await _camera.StartAsync(_deviceIndex, 3.0 / 4.0, s.MirrorMode);
            if (!ok)
            {
                LoadingMessage = "카메라를 열 수 없습니다.";
                _logger?.LogWarning("카메라 테스트: 장치 {Index} 열기 실패", _deviceIndex);
                return; // IsLoading=true 유지(오버레이에 실패 문구)
            }

            bool ready = await WaitForStablePreviewAsync(CameraReadyTimeoutMs);
            if (!ready)
            {
                LoadingMessage = "카메라 준비에 실패했습니다.";
                _logger?.LogWarning("카메라 테스트: 안정적 프리뷰 타임아웃(장치 {Index})", _deviceIndex);
                return;
            }
            IsLoading = false;
        }
        catch (Exception ex)
        {
            LoadingMessage = "카메라 준비 중 오류가 발생했습니다.";
            _logger?.LogError(ex, "카메라 테스트 시작 오류");
        }
    }

    /// <summary>테스트 셔터: 플래시 옵션 확인 후 재현, 스틸은 캡처하되 저장/합성 없이 폐기. (요구 §1)</summary>
    [RelayCommand]
    private async Task ShootTest()
    {
        if (_shooting || IsLoading) return;
        _shooting = true;
        try
        {
            if (_settings.Current.FlashMode)
            {
                FlashActive = true;
                await Task.Delay(120);
            }
            if (_settings.Current.ShutterSound) SoundEffects.PlayShutter(); // 실촬영과 동일 재현
            var still = await _camera.CaptureStillAsync(); // 결과 폐기(저장 안 함)
            _ = still;
            FlashActive = false;

            ShotNotice = "테스트 촬영 완료 · 저장되지 않았습니다";
            await Task.Delay(1500);
            ShotNotice = string.Empty;
        }
        catch (Exception ex)
        {
            FlashActive = false;
            _logger?.LogWarning(ex, "카메라 테스트 촬영 오류(무시)");
        }
        finally { _shooting = false; }
    }

    [RelayCommand]
    private void Close() => RequestClose?.Invoke();

    /// <summary>모달 닫힘 시 호출: 카메라 정지(스레드 join). 실 촬영 경로와 공유하는 단일 인스턴스 해제.</summary>
    public async Task StopAsync()
    {
        try { await _camera.StopAsync(); }
        catch (Exception ex) { _logger?.LogWarning(ex, "카메라 테스트 정지 오류(무시)"); }
    }

    /// <summary>안정적 프리뷰(연속 N프레임 + 최소 경과) 대기. CaptureViewModel과 동일 규칙(PreviewReadiness 재사용).</summary>
    private async Task<bool> WaitForStablePreviewAsync(int timeoutMs)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var readiness = new PreviewReadiness();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        void OnFrame(object? s, CameraFrame f)
        {
            if (readiness.OnFrame(sw.Elapsed.TotalMilliseconds, _camera.CurrentFps))
                tcs.TrySetResult(true);
        }
        _camera.FrameReady += OnFrame;
        try
        {
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
            return completed == tcs.Task;
        }
        finally { _camera.FrameReady -= OnFrame; }
    }
}
