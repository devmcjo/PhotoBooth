import { logger } from "@adapters/storage/logStore";

/**
 * 프레임 이미지 object URL 소유자 — 설계 §8.2 (05 §4 · WM2)
 *
 * OPFS에 캐시된 프레임 PNG는 `URL.createObjectURL(File)`로 화면에 준다. 이 URL의 **수명 소유자는
 * 이 모듈 하나**이고, 키는 OPFS 상대 경로다. 같은 경로에 URL을 두 번 만들지 않으므로 목록을
 * 왕복해도 URL이 늘지 않는다.
 *
 * ⚠️ **화면 이탈에서 해제하지 않는다.** 선택된 프레임의 URL은 `sessionStore.session.frame`을 타고
 *    `Result`의 합성까지 살아 있어야 한다 — 여기서 revoke하면 합성이 `SecurityError`가 아니라
 *    "불러올 수 없음"으로 죽는다(`fallbackFrame.ts`가 이미 같은 판단을 하고 있다).
 * ⚠️ 해제 시점은 **프레임 삭제**뿐이다. 항목 수가 프레임 개수(≤ 수십)로 유계라 누수가 아니다(K2).
 * ⚠️ 어댑터 규약: 예외를 전파하지 않는다. URL을 만들 수 없으면 **빈 문자열**이고,
 *    `hasUsableImage`가 그 프레임을 목록에서 걸러낸다.
 */

const urls = new Map<string, string>();

/** 경로에 대응하는 object URL. 이미 있으면 **재사용**한다(중복 생성 금지). */
export function frameImageUrl(path: string, source: Blob): string {
  const cached = urls.get(path);
  if (cached !== undefined) return cached;

  try {
    const url = URL.createObjectURL(source);
    urls.set(path, url);
    return url;
  } catch (err) {
    logger.warn("프레임 이미지 URL 생성 실패", {
      path,
      reason: err instanceof Error ? err.message : String(err),
    });
    return "";
  }
}

/** 프레임 삭제 시에만 부른다. 없는 경로는 무시한다(멱등). */
export function revokeFrameImage(path: string): void {
  const url = urls.get(path);
  if (url === undefined) return;
  urls.delete(path);
  try {
    URL.revokeObjectURL(url);
  } catch {
    // 이미 해제됐거나 지원하지 않는 환경 — 맵에서 지운 것으로 충분하다.
  }
}

/** 테스트·전체 리셋용. 운영 경로에서는 부르지 않는다(선택 프레임의 URL이 합성까지 살아야 한다). */
export function revokeAllFrameImages(): void {
  for (const path of [...urls.keys()]) revokeFrameImage(path);
}

/** 진단·테스트용 보유 개수. */
export function frameImageUrlCount(): number {
  return urls.size;
}
