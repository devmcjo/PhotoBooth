using System.Windows;
using MCPhoto.App.ViewModels;
using MCPhoto.App.Views;
using MCPhoto.Core.Capture;
using MCPhoto.Core.Settings;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.Services;

/// <summary>
/// 카메라 테스트 모달 생성·표시·해제. 카메라(ICameraService)는 DI Singleton을 공유 — 촬영 경로와 동일 인스턴스.
/// 오픈: 창 표시 → Loaded에서 StopAsync→StartAsync(선택 인덱스). 닫힘: ShowDialog 종료 후 StopAsync(확실 해제). (it9 §2.2/§2.3)
/// </summary>
public sealed class CameraTestDialogService : ICameraTestDialogService
{
    private readonly ICameraService _camera;
    private readonly ISettingsService _settings;
    private readonly ILogger<CameraTestDialogService>? _logger;

    public CameraTestDialogService(ICameraService camera, ISettingsService settings,
        ILogger<CameraTestDialogService>? logger = null)
    {
        _camera = camera;
        _settings = settings;
        _logger = logger;
    }

    public async Task ShowAsync(int deviceIndex)
    {
        var vm = new CameraTestViewModel(_camera, _settings, deviceIndex, _logger);
        var win = new CameraTestWindow
        {
            DataContext = vm,
            Owner = Application.Current?.MainWindow
        };
        vm.RequestClose += () => win.Close();

        // 창을 먼저 띄우고(로딩 오버레이 노출) Loaded에서 카메라 시작 — 설정 UI 프리즈 방지.
        win.Loaded += async (_, _) => await vm.StartAsync();

        win.ShowDialog();          // 모달: 닫힐 때까지 블로킹
        await vm.StopAsync();      // 닫힌 뒤 카메라 확실 해제(스레드 join)
        _logger?.LogInformation("카메라 테스트 모달 종료(장치 {Index})", deviceIndex);
    }
}
