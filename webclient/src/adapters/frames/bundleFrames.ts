import { parseBundleManifest, type BundleFrameEntry } from "@domain/frames/bundleManifest";
import { autoArrange } from "@domain/frames/slotLayout";
import { parseSlotsFile } from "@domain/frames/slotsFile";
import { slotAspectToRatio } from "@domain/frames/slotAspect";
import type { FrameTemplate, Slot } from "@domain/frames/types";
import { logger } from "@adapters/storage/logStore";

/**
 * 번들 프레임 자산 — 카탈로그 우선순위 ③ (03 §4 · 05 §4.1)
 *
 * 브라우저는 정적 디렉터리를 열거할 수 없으므로 **매니페스트**(`public/frames/index.json`)를
 * 규약으로 둔다. Windows `LoadBundleFrames`(`Directory.EnumerateFiles`)와의 유일한 구조적 차이다.
 *
 * ⚠️ 실패는 전부 **빈 배열**이다(경고 로그만). 번들이 0개여도 ①②④로 목록이 비지 않는다.
 * ⚠️ 자산은 이 Step에서 커밋하지 않는다 — `index.json`에 `[]`만 두어 경로·포맷 규약을 고정하고
 *    실제 PNG는 운영 자산 준비 시 추가한다(VF-10).
 */

/** same-origin 정적 자산 경로. 앱은 사이트 루트에 배포된다. */
export const BUNDLE_FRAME_DIR = "/frames";
export const BUNDLE_MANIFEST_URL = `${BUNDLE_FRAME_DIR}/index.json`;
/** 정적 자산이라 오래 걸릴 이유가 없다. 대기 예산을 여기서 태우지 않는다. */
export const BUNDLE_FETCH_TIMEOUT_MS = 3_000;
/** 슬롯 정보가 없을 때 만드는 기본 배치(analysis/13 §5 ③). */
export const BUNDLE_DEFAULT_SLOT_COUNT = 4;

async function fetchText(url: string, timeoutMs: number): Promise<string | null> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  try {
    const response = await fetch(url, { signal: controller.signal });
    if (!response.ok) return null;
    return await response.text();
  } catch {
    return null;
  } finally {
    clearTimeout(timer);
  }
}

function parseJson(text: string): unknown {
  try {
    return JSON.parse(text) as unknown;
  } catch {
    return null;
  }
}

async function resolveSlots(entry: BundleFrameEntry): Promise<readonly Slot[]> {
  if (entry.slots !== null) {
    const text = await fetchText(`${BUNDLE_FRAME_DIR}/${entry.slots}`, BUNDLE_FETCH_TIMEOUT_MS);
    if (text !== null) {
      const parsed = parseSlotsFile(text);
      if (parsed.slots.length > 0) return parsed.slots;
    }
  }
  // `.slots`가 없거나 파싱 결과가 0개면 2×2 자동 생성(analysis/13 §5 ③).
  return autoArrange(
    BUNDLE_DEFAULT_SLOT_COUNT,
    entry.width,
    entry.height,
    slotAspectToRatio("Ratio3x4"),
  );
}

/** 번들 프레임 목록. 매니페스트 404·JSON 오류·타임아웃은 전부 `[]`다. */
export async function loadBundleFrames(): Promise<FrameTemplate[]> {
  const text = await fetchText(BUNDLE_MANIFEST_URL, BUNDLE_FETCH_TIMEOUT_MS);
  if (text === null) {
    logger.warn("번들 프레임 매니페스트를 읽을 수 없습니다 — 번들 0개로 진행");
    return [];
  }

  const entries = parseBundleManifest(parseJson(text));
  if (entries.length === 0) return [];

  const frames: FrameTemplate[] = [];
  for (const entry of entries) {
    frames.push({
      // `bundle:` 접두가 출처 판정의 근거다(05 §4.4 — 편집·삭제 불가).
      id: `bundle:${entry.name}`,
      userId: null,
      isDefault: true,
      name: entry.name,
      imageUrl: `${BUNDLE_FRAME_DIR}/${entry.image}`,
      imageSize: { width: entry.width, height: entry.height },
      slots: await resolveSlots(entry),
      // 번들 자산에는 생성 시각이 없다(서버 문서가 아니다). 어댑터가 시각을 지어내지 않는다.
      createdAt: "",
    });
  }
  logger.info("번들 프레임 로드", { count: frames.length });
  return frames;
}
