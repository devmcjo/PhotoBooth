namespace MCPhoto.App.Services;

/// <summary>
/// 카메라 테스트 모달을 여는 서비스. SettingsViewModel이 Window/Application을 직접 참조하지 않도록 추상화. (it9 §2.2 D2)
/// </summary>
public interface ICameraTestDialogService
{
    /// <summary>선택된 장치 인덱스로 테스트 모달(모달 다이얼로그)을 연다. 닫힐 때까지 대기 후 카메라 정지.</summary>
    Task ShowAsync(int deviceIndex);

    /// <summary>
    /// 초기 선택 항목을 지정해 테스트 모달을 연다. (it23 §9.3)
    /// <para>
    /// 기존 <see cref="ShowAsync(int)"/>는 <b>유지한다</b> — 웹캠 인덱스만 아는 호출자(및 기존 테스트)가
    /// 그대로 동작해야 하고, 새 오버로드로 위임하면 동작이 하나로 수렴한다.
    /// </para>
    /// 모달 안에서 장치를 바꿀 수 있으므로 이 인자는 "초기 선택"일 뿐 잠금이 아니다.
    /// </summary>
    Task ShowAsync(CameraTestTarget target);
}
