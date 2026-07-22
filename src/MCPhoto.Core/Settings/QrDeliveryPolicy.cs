namespace MCPhoto.Core.Settings;

/// <summary>
/// QR 전송 세분화(사진/타임랩스) 연동 규칙(순수 로직, 테스트 대상). (it7 §3 F2)
/// 단일 정규화 지점 — 하위 토글이 둘 다 off면 QR 전송 자체를 off로.
/// </summary>
public static class QrDeliveryPolicy
{
    /// <summary>
    /// 정규화: QR on인데 사진·타임랩스 둘 다 off면 QR을 off로(전송할 게 없음).
    /// 그 외에는 입력 그대로. 하위 토글 값은 보존(QR off여도 재활성 시 복원).
    /// </summary>
    public static (bool enableQr, bool sendPhoto, bool sendTimelapse) Normalize(
        bool enableQr, bool sendPhoto, bool sendTimelapse)
    {
        if (enableQr && !sendPhoto && !sendTimelapse)
            return (false, sendPhoto, sendTimelapse);
        return (enableQr, sendPhoto, sendTimelapse);
    }
}
