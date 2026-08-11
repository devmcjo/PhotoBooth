using System.Windows;
using MCPhoto.App.ViewModels;
using MCPhoto.App.Views;
using MCPhoto.Core.Capture;
using MCPhoto.Core.Devices;
using MCPhoto.Core.Settings;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.Services;

/// <summary>
/// 카메라 테스트 모달 생성·표시·해제. 카메라(ICameraService)는 DI Singleton을 공유 — 촬영 경로와 동일 인스턴스.
/// 오픈: 창 표시 → Loaded에서 StopAsync→StartAsync(선택 인덱스). 닫힘: ShowDialog 종료 후 StopAsync(확실 해제). (it9 §2.2/§2.3)
/// <para>
/// it23: 외부 카메라(DSLR)도 목록에 오르므로 <see cref="IExternalCamera"/>를 함께 넘긴다. 닫을 때
/// VM의 StopAsync가 웹캠 정지 + 외부 카메라 해제를 모두 담당한다.
/// </para>
/// </summary>
public sealed class CameraTestDialogService : ICameraTestDialogService
{
    private readonly ICameraService _camera;
    private readonly ISettingsService _settings;
    private readonly IExternalCamera _external;
    private readonly ILogger<CameraTestDialogService>? _logger;

    public CameraTestDialogService(ICameraService camera, ISettingsService settings,
        IExternalCamera external, ILogger<CameraTestDialogService>? logger = null)
    {
        _camera = camera;
        _settings = settings;
        _external = external;
        _logger = logger;
    }

    /// <summary>웹캠 인덱스만 아는 호출자용 — 새 오버로드로 위임해 동작이 하나로 수렴한다.</summary>
    public Task ShowAsync(int deviceIndex) => ShowAsync(CameraTestTarget.Webcam(deviceIndex));

    public async Task ShowAsync(CameraTestTarget target)
    {
        var vm = new CameraTestViewModel(_camera, _settings, _external, target, _logger);
        var win = new CameraTestWindow
        {
            DataContext = vm,
            Owner = Application.Current?.MainWindow
        };
        vm.RequestClose += () => win.Close();

        // 창을 먼저 띄우고(로딩 오버레이 노출) Loaded에서 카메라 시작 — 설정 UI 프리즈 방지.
        win.Loaded += async (_, _) => await vm.StartAsync();

        win.ShowDialog();          // 모달: 닫힐 때까지 블로킹
        await vm.StopAsync();      // 닫힌 뒤 카메라 확실 해제(스레드 join) + 외부 카메라 연결 해제
        _logger?.LogInformation("카메라 테스트 모달 종료({Target})", target.DisplayName);
    }
}
