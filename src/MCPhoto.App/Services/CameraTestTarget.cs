using MCPhoto.Core.Capture;
using MCPhoto.Core.Devices;

namespace MCPhoto.App.Services;

/// <summary>
/// 카메라 테스트 모달의 장치 목록 항목. (it23 §9.3, 요구 4)
/// <para>
/// 웹캠과 외부 카메라는 확인 목적이 다르다: 웹캠은 <b>타임랩스·프리뷰</b>가 제대로 나오는지,
/// 외부 카메라는 <b>카메라 세팅과 셔터 동작</b>이 되는지다. 그래서 같은 목록에 두면서도
/// 선택 시 열리는 화면이 다르다 — 그 분기를 문자열 비교가 아니라 이 타입으로 판정한다.
/// </para>
/// ToString=표시명: ComboBox가 닫힌 상태에서 DisplayMemberPath 없이도 사람이 읽을 수 있게(F12 관례).
/// </summary>
public sealed record CameraTestTarget
{
    private CameraTestTarget(bool isExternal, int deviceIndex, string displayName)
    {
        IsExternal = isExternal;
        DeviceIndex = deviceIndex;
        DisplayName = displayName;
    }

    /// <summary>외부 카메라(DSLR) 항목인지. false면 웹캠 항목이다.</summary>
    public bool IsExternal { get; }

    /// <summary>웹캠 장치 인덱스(외부 항목이면 -1). 웹캠 동작 기준은 항상 인덱스다(F12).</summary>
    public int DeviceIndex { get; }

    /// <summary>목록 표시명.</summary>
    public string DisplayName { get; }

    /// <summary>웹캠 항목 생성.</summary>
    public static CameraTestTarget Webcam(int deviceIndex, string? name = null)
        => new(false, deviceIndex, string.IsNullOrWhiteSpace(name) ? $"카메라 {deviceIndex}" : name!);

    /// <summary>웹캠 항목 생성(열거 결과에서).</summary>
    public static CameraTestTarget Webcam(CameraDevice device) => Webcam(device.Index, device.Name);

    /// <summary>
    /// 외부 카메라 항목 생성. 표시명에 "(외부 카메라)"를 붙여 웹캠 항목과 목록에서 구분된다 —
    /// "Nikon Webcam Utility"류가 깔린 PC에서는 같은 바디가 웹캠으로도 열거될 수 있어(설계 A9)
    /// 이름만으로는 어느 경로인지 알 수 없다.
    /// </summary>
    public static CameraTestTarget External(ExternalCameraModel model)
        => new(true, -1, $"{model.DisplayName} (외부 카메라)");

    public override string ToString() => DisplayName;
}
