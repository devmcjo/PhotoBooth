import type { ImageSize, Slot } from "./types";

/**
 * `.slots` 텍스트 포맷 파서·직렬화 — Windows `Frames/LocalFrameStore.cs`(포맷 부분) 이식 (analysis/41 §3.3)
 *
 * 포맷은 **Windows와 상호 이동 가능**해야 한다(WD4 — 프레임 내보내기/가져오기):
 *   `#imagesize=W,H`  (메타)
 *   `#dbid=...`       (선택 — 공용 캐시의 서버 문서 id. 사본에는 기록하지 않는다)
 *   `index,x,y,w,h`   (슬롯. 5필드 고정)
 * 그 외 `#` 줄은 주석으로 무시하고, 손상된 줄은 **건너뛴다**(예외를 던지지 않는다).
 */

export interface SlotsFileContent {
  readonly imageSize: ImageSize;
  readonly slots: readonly Slot[];
  readonly dbId: string | null;
}

const IMAGE_SIZE_PREFIX = "#imagesize=";
const DB_ID_PREFIX = "#dbid=";

/**
 * C# `int.TryParse` 대응. 앞뒤 공백·부호를 허용하고 그 외(소수점·지수·16진수·범위 초과)는 거부한다.
 * `Number.parseInt`는 `"12abc"`를 12로 읽어버리므로 쓰지 않는다.
 */
function tryParseInt(raw: string): number | null {
  const text = raw.trim();
  if (!/^[+-]?\d+$/.test(text)) return null;
  const value = Number(text);
  if (!Number.isSafeInteger(value)) return null;
  // C# int 범위를 넘어가는 값은 TryParse가 실패한다 — 같은 지점에서 거부한다.
  if (value < -2147483648 || value > 2147483647) return null;
  return value;
}

/** `.slots` 텍스트를 파싱한다. 어떤 입력에도 예외를 던지지 않는다. */
export function parseSlotsFile(text: string): SlotsFileContent {
  let width = 0;
  let height = 0;
  let dbId: string | null = null;
  const slots: Slot[] = [];

  for (const rawLine of text.split(/\r?\n/)) {
    const line = rawLine.trim();
    if (line.length === 0) continue;

    const lower = line.toLowerCase();

    if (lower.startsWith(IMAGE_SIZE_PREFIX)) {
      const parts = line.slice(IMAGE_SIZE_PREFIX.length).split(",");
      if (parts.length === 2) {
        const w = tryParseInt(parts[0]!);
        const h = tryParseInt(parts[1]!);
        if (w !== null && h !== null) {
          width = w;
          height = h;
        }
      }
      continue;
    }

    if (lower.startsWith(DB_ID_PREFIX)) {
      dbId = line.slice(DB_ID_PREFIX.length).trim();
      continue;
    }

    if (line.startsWith("#")) continue; // 기타 주석

    const fields = line.split(",");
    if (fields.length !== 5) continue; // 손상 줄 — 건너뛴다

    const index = tryParseInt(fields[0]!);
    const x = tryParseInt(fields[1]!);
    const y = tryParseInt(fields[2]!);
    const w = tryParseInt(fields[3]!);
    const h = tryParseInt(fields[4]!);
    if (index === null || x === null || y === null || w === null || h === null) continue;

    slots.push({ index, x, y, width: w, height: h });
  }

  return { imageSize: { width, height }, slots, dbId };
}

/**
 * `.slots` 텍스트를 만든다(줄 끝은 `\n`, 파일은 UTF-8로 쓴다 — Windows와 동일).
 * `dbId`가 null·빈 값이면 `#dbid=` 줄을 쓰지 않는다(로컬 사본은 서버 문서와 연결이 끊긴다).
 */
export function serializeSlotsFile(content: SlotsFileContent): string {
  const lines: string[] = [`${IMAGE_SIZE_PREFIX}${content.imageSize.width},${content.imageSize.height}`];

  if (content.dbId !== null && content.dbId.length > 0) {
    lines.push(`${DB_ID_PREFIX}${content.dbId}`);
  }

  for (const s of content.slots) {
    lines.push(`${s.index},${s.x},${s.y},${s.width},${s.height}`);
  }

  return `${lines.join("\n")}\n`;
}
