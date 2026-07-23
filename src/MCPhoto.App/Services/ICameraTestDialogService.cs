namespace MCPhoto.App.Services;

/// <summary>
/// 카메라 테스트 모달을 여는 서비스. SettingsViewModel이 Window/Application을 직접 참조하지 않도록 추상화. (it9 §2.2 D2)
/// </summary>
public interface ICameraTestDialogService
{
    /// <summary>선택된 장치 인덱스로 테스트 모달(모달 다이얼로그)을 연다. 닫힐 때까지 대기 후 카메라 정지.</summary>
    Task ShowAsync(int deviceIndex);
}
