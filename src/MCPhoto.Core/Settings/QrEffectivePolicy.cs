namespace MCPhoto.Core.Settings;

/// <summary>
/// QR 전송의 런타임 effective 값 계산(순수 로직, 테스트 대상). raw ini 설정(AppSettings) + 로그인 역할 +
/// TempUser 한도상태를 조합해 "지금 이 세션에서 QR을 실제로 켤지"를 결정한다. (it13 §7.1b)
/// ⚠️ AppSettings(ini)를 절대 읽거나 변경하지 않는다 — 입력은 이미 로드된 값, 출력은 런타임 오버라이드일 뿐이다.
/// 게스트(미로그인)·TempUser 초과 시 effective=false, 그 외에는 raw 값 그대로(한도 해제 시 즉시 원복).
/// </summary>
public static class QrEffectivePolicy
{
    /// <summary>
    /// effective QR enabled. 규칙(우선순위 순, it13 §7.1b):
    ///   1) 미로그인(게스트) → false (기존 ResultViewModel.Next의 IsLoggedIn 조건 흡수).
    ///   2) TempUser이고 한도 초과(blocked) → false (신규).
    ///   3) 그 외(User/Manager/Admin, 정상 TempUser) → raw EnableQrDelivery 그대로.
    /// </summary>
    /// <param name="rawEnableQr">ini raw 값(AppSettings.EnableQrDelivery). 읽기만 — 변경 없음.</param>
    /// <param name="isLoggedIn">로그인 여부(게스트면 false).</param>
    /// <param name="isTempUserBlocked">역할이 TempUser이고 한도 초과인지(비TempUser는 항상 false — 호출측이 합성).</param>
    public static bool IsQrEnabled(bool rawEnableQr, bool isLoggedIn, bool isTempUserBlocked)
    {
        if (!isLoggedIn) return false;
        if (isTempUserBlocked) return false;   // role==TempUser && blocked를 호출측이 이미 판정해 넘긴다
        return rawEnableQr;
    }
}
