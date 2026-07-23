namespace MCPhoto.App.Services;

/// <summary>
/// 진단·상태 모달을 여는 서비스. SettingsViewModel이 Window/Application을 직접 참조하지 않도록 추상화
/// (ICameraTestDialogService와 동일 관례). (it11 §3.14.6)
/// </summary>
public interface IDiagnosticsDialogService
{
    /// <summary>진단 모달(모달 다이얼로그)을 연다. 진입 시 카메라 1회 검사 후 닫힐 때까지 대기.</summary>
    Task ShowAsync();
}
