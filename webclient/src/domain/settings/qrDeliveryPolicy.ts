/**
 * QR 전송 세분화 연동 규칙 — Windows `Settings/QrDeliveryPolicy.cs` 이식 (analysis/41 §2.4)
 * 단일 정규화 지점 — 하위 토글이 둘 다 off면 QR 전송 자체를 off로 만든다(M7).
 */

export interface QrToggles {
  readonly enableQrDelivery: boolean;
  readonly sendPhoto: boolean;
  readonly sendTimelapse: boolean;
}

/**
 * 정규화(로드·저장 시): QR on인데 사진·타임랩스 둘 다 off면 QR을 off로.
 * **하위 토글 값은 보존한다**(재활성 시 복원하기 위함).
 */
export function normalizeQrToggles(toggles: QrToggles): QrToggles {
  if (toggles.enableQrDelivery && !toggles.sendPhoto && !toggles.sendTimelapse) {
    return { ...toggles, enableQrDelivery: false };
  }
  return toggles;
}

/**
 * QR 전송 off→on **재활성** 시 하위 토글을 둘 다 on으로 강제한다(analysis/41 §2.4).
 * 이 규칙은 **화면 로직에만** 있다 — 설정 로드 중에는 억제해야 한다(로드가 사용자 값을 덮어쓰면 안 된다).
 */
export function onQrReEnabled(): Pick<QrToggles, "sendPhoto" | "sendTimelapse"> {
  return { sendPhoto: true, sendTimelapse: true };
}
