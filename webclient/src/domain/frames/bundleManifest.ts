/**
 * 번들 프레임 매니페스트 파서 — 05 §4.1 · 03 §4(목록 우선순위 ③)
 *
 * 브라우저는 정적 디렉터리를 **열거할 수 없다**(Windows `Directory.EnumerateFiles`의 대응물이 없다).
 * 그래서 `public/frames/index.json` 매니페스트를 규약으로 둔다 — 웹과 Windows의 유일한 구조적 차이다.
 *
 * ⚠️ 손상 항목은 **건너뛰고 계속**한다(`slotsFile.ts`와 동형). 매니페스트 한 줄이 잘못됐다고
 *    번들 전체가 사라지면 오프라인 부스의 마지막 폴백이 통째로 없어진다.
 */

export interface BundleFrameEntry {
  /** 표시 이름 겸 dedup 키. */
  readonly name: string;
  /** `public/frames/` 기준 이미지 파일명. */
  readonly image: string;
  /** `public/frames/` 기준 `.slots` 파일명. 없으면 2×2 자동 배치로 떨어진다. */
  readonly slots: string | null;
  readonly width: number;
  readonly height: number;
}

function isPositiveInt(value: unknown): value is number {
  return typeof value === "number" && Number.isInteger(value) && value > 0;
}

function parseEntry(raw: unknown): BundleFrameEntry | null {
  if (typeof raw !== "object" || raw === null) return null;
  const record = raw as Record<string, unknown>;

  const name = record.name;
  const image = record.image;
  if (typeof name !== "string" || name.trim().length === 0) return null;
  if (typeof image !== "string" || image.trim().length === 0) return null;
  // 매니페스트는 앱 자산이지만 경로 조작을 허용할 이유가 없다 — 파일명만 받는다.
  if (image.includes("/") || image.includes("\\") || image.includes("..")) return null;
  if (!isPositiveInt(record.width) || !isPositiveInt(record.height)) return null;

  const slots = record.slots;
  const slotsFile =
    typeof slots === "string" &&
    slots.trim().length > 0 &&
    !slots.includes("/") &&
    !slots.includes("\\") &&
    !slots.includes("..")
      ? slots
      : null;

  return { name, image, slots: slotsFile, width: record.width, height: record.height };
}

/** 매니페스트 파싱. 배열이 아니면 `[]`, 항목이 규약을 어기면 그 항목만 버린다. 예외를 던지지 않는다. */
export function parseBundleManifest(raw: unknown): BundleFrameEntry[] {
  if (!Array.isArray(raw)) return [];
  const entries: BundleFrameEntry[] = [];
  for (const item of raw) {
    const entry = parseEntry(item);
    if (entry !== null) entries.push(entry);
  }
  return entries;
}
