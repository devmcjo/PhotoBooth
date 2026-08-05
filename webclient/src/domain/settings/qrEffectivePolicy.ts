/**
 * QR 전송의 런타임 effective 값 — Windows `Settings/QrEffectivePolicy.cs` 이식
 * (design/wpf-it13-temp-user-role-design.md §7.1b)
 *
 * ⚠️ **저장된 설정을 읽거나 변경하지 않는다.** 입력은 이미 로드된 raw 값이고 출력은 런타임 오버라이드다.
 *    이 판정 때문에 `EnableQrDelivery`를 write하면 게스트 촬영 한 번에 운영자 설정이 꺼진다(E23 위반).
 *
 * ⚠️ **게스트에게는 QR이 제공되지 않는다**(VF-11). 미로그인이면 `Result → Done`으로 끝나며
 *    업로드 요청이 0건이다 — Windows와 동일하다.
 */
export function isQrEffectivelyEnabled(
  rawEnableQr: boolean,
  isLoggedIn: boolean,
  isTempUserBlocked: boolean,
): boolean {
  if (!isLoggedIn) return false; // 1) 게스트
  if (isTempUserBlocked) return false; // 2) TempUser 한도 초과(호출측이 역할+한도를 합성해 넘긴다)
  return rawEnableQr; // 3) 그 외 — raw 값 그대로(한도 해제 시 즉시 원복)
}
