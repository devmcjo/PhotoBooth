using System.Windows;
using MCPhoto.App.ViewModels;
using MCPhoto.App.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.Services;

/// <summary>
/// 진단·상태 모달 생성·표시. DiagnosticsViewModel을 DI로 해결하고 DiagnosticsWindow에 주입 후 ShowDialog.
/// CameraTestDialogService와 동일 스타일(IServiceProvider로 VM 해결 + Owner=MainWindow + 모달). (it11 §3.14.6)
/// </summary>
public sealed class DiagnosticsDialogService : IDiagnosticsDialogService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<DiagnosticsDialogService>? _logger;

    public DiagnosticsDialogService(IServiceProvider services,
        ILogger<DiagnosticsDialogService>? logger = null)
    {
        _services = services;
        _logger = logger;
    }

    public async Task ShowAsync()
    {
        var vm = _services.GetRequiredService<DiagnosticsViewModel>();
        var win = new DiagnosticsWindow
        {
            DataContext = vm,
            Owner = Application.Current?.MainWindow
        };

        // 진입 시 카메라 자동 검사(백그라운드 열거) + 웹 배포일 서버 조회. UI 스레드에서 await → 완료 후 창 표시.
        // 두 작업은 서로 독립이라 병렬로 돌린다 — 배포일 조회는 자체 타임아웃(5초)이 있어 카메라 열거보다
        // 오래 끌지 않는다. 둘 다 내부에서 실패를 흡수하므로 여기서 예외가 새지 않는다.
        await Task.WhenAll(
            vm.RefreshCamerasCommand.ExecuteAsync(null),
            vm.RefreshWebDeployDateCommand.ExecuteAsync(null));

        win.ShowDialog();          // 모달: 닫힐 때까지 블로킹
        _logger?.LogInformation("진단·상태 모달 종료");
    }
}
