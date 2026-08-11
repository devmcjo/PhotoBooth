namespace MCPhoto.Devices.Nikon;

/// <summary>
/// 사용자 노출 사유 문구(동결 — it23 §9.4 W10~W12).
/// <para>
/// 문구를 상수로 모아 두는 이유: 같은 사유가 설정 화면·테스트 모달·촬영 진입 토스트 3곳에 나타난다.
/// 문장을 각자 적으면 같은 원인이 화면마다 다르게 설명되고, 운영 문서와도 어긋난다.
/// </para>
/// capability 상태 사유(W13·W14)는 Core의 <c>ExternalCapturePolicy.DescribeClosed</c>가 담당한다 —
/// 그쪽은 장치 벤더와 무관한 판정이기 때문이다.
/// </summary>
public static class NikonCameraReasons
{
    /// <summary>W10 — SDK shim 자체가 미탑재(현 프로덕션 기본 상태).</summary>
    public const string SdkMissing = "SDK 모듈이 설치되지 않았습니다";

    /// <summary>W12 — 모듈은 있으나 카메라에 도달하지 못함(전원·USB·PTP 모드).</summary>
    public const string NotConnected = "카메라가 연결되지 않았습니다 (USB·전원 확인)";

    /// <summary>
    /// W11 — md3 모듈 파일 부재. 파일명을 문구에 넣는 이유: 운영자가 어디에 무엇을 넣어야 하는지가
    /// 곧 해결 방법이다(라이선스상 SDK를 동봉하지 못하면 수동 배치가 유일한 경로다 — 설계 §13).
    /// </summary>
    public static string ModuleFileMissing(string relativePath)
        => $"카메라 모듈 파일이 없습니다 ({relativePath})";
}
