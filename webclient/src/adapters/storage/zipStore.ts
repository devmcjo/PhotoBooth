/**
 * store(무압축) zip 생성 · 파싱 — 05 §4.6 (순수 · **import 0**)
 *
 * 압축하지 않는 이유: PNG는 이미 압축돼 있어 deflate 이득이 거의 없고, 무압축 writer는
 * **의존성이 0**이다(`THIRD-PARTY.md`에 항목을 늘리지 않는다).
 *
 * ⚠️ **읽기는 deflate(method 8)도 받아야 한다.** 운영자가 Windows 탐색기로 다시 압축하면
 *    method 8이 된다. 여기서는 압축 바이트를 그대로 넘기고, 해제는 어댑터가
 *    `DecompressionStream("deflate-raw")`로 한다(브라우저 API라 순수 계층에 둘 수 없다).
 * ⚠️ 손상 항목은 **건너뛰고 계속**한다(`slotsFile.ts`와 동형) — 한 항목이 잘못됐다고 zip 전체를
 *    버리면 운영자가 무엇을 잃었는지 알 수 없다.
 * ⚠️ 디렉터리 엔트리·`..`·절대경로·백슬래시는 파싱 단계에서 **버린다**(경로 조작 방어).
 */

export interface ZipInputEntry {
  readonly path: string;
  readonly bytes: Uint8Array;
}

export interface ParsedZipEntry {
  readonly path: string;
  /** 0 = store(비압축), 8 = deflate. 그 외 method는 목록에서 제외된다. */
  readonly method: 0 | 8;
  /** method 8이면 **아직 압축된** 바이트다. */
  readonly data: Uint8Array;
  readonly crc32: number;
  readonly uncompressedSize: number;
}

const LOCAL_SIG = 0x04034b50;
const CENTRAL_SIG = 0x02014b50;
const EOCD_SIG = 0x06054b50;
const LOCAL_HEADER_SIZE = 30;
const CENTRAL_HEADER_SIZE = 46;
const EOCD_SIZE = 22;
/** UTF-8 파일명 플래그(general purpose bit 11). 한글 프레임 이름이 깨지지 않게 한다. */
const UTF8_FLAG = 0x0800;
/** 고정 DOS 날짜 1980-01-01 00:00. 빌드마다 바이트가 흔들리지 않게 시각을 넣지 않는다. */
const DOS_TIME = 0;
const DOS_DATE = 0x0021;

let crcTable: Uint32Array | null = null;

function table(): Uint32Array {
  if (crcTable !== null) return crcTable;
  const built = new Uint32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) {
      c = (c & 1) === 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    }
    built[n] = c >>> 0;
  }
  crcTable = built;
  return built;
}

/** CRC-32(IEEE 802.3). zip 규격이 요구하는 체크섬이다. */
export function crc32(bytes: Uint8Array): number {
  const lookup = table();
  let crc = 0xffffffff;
  for (let index = 0; index < bytes.length; index++) {
    crc = (crc >>> 8) ^ lookup[(crc ^ bytes[index]!) & 0xff]!;
  }
  return (crc ^ 0xffffffff) >>> 0;
}

function encodeName(path: string): Uint8Array {
  return new TextEncoder().encode(path);
}

function decodeName(bytes: Uint8Array): string {
  return new TextDecoder().decode(bytes);
}

/** store(무압축) zip 생성. 로컬 헤더 + 중앙 디렉터리 + EOCD. */
export function buildStoreZip(entries: readonly ZipInputEntry[]): Uint8Array {
  const encoded = entries.map((entry) => ({
    name: encodeName(entry.path),
    bytes: entry.bytes,
    crc: crc32(entry.bytes),
  }));

  const localSize = encoded.reduce(
    (sum, e) => sum + LOCAL_HEADER_SIZE + e.name.length + e.bytes.length,
    0,
  );
  const centralSize = encoded.reduce((sum, e) => sum + CENTRAL_HEADER_SIZE + e.name.length, 0);
  const buffer = new Uint8Array(localSize + centralSize + EOCD_SIZE);
  const view = new DataView(buffer.buffer);

  const offsets: number[] = [];
  let cursor = 0;

  for (const entry of encoded) {
    offsets.push(cursor);
    view.setUint32(cursor, LOCAL_SIG, true);
    view.setUint16(cursor + 4, 20, true); // version needed
    view.setUint16(cursor + 6, UTF8_FLAG, true);
    view.setUint16(cursor + 8, 0, true); // method = store
    view.setUint16(cursor + 10, DOS_TIME, true);
    view.setUint16(cursor + 12, DOS_DATE, true);
    view.setUint32(cursor + 14, entry.crc, true);
    view.setUint32(cursor + 18, entry.bytes.length, true);
    view.setUint32(cursor + 22, entry.bytes.length, true);
    view.setUint16(cursor + 26, entry.name.length, true);
    view.setUint16(cursor + 28, 0, true); // extra length
    buffer.set(entry.name, cursor + LOCAL_HEADER_SIZE);
    buffer.set(entry.bytes, cursor + LOCAL_HEADER_SIZE + entry.name.length);
    cursor += LOCAL_HEADER_SIZE + entry.name.length + entry.bytes.length;
  }

  const centralStart = cursor;
  for (let index = 0; index < encoded.length; index++) {
    const entry = encoded[index]!;
    view.setUint32(cursor, CENTRAL_SIG, true);
    view.setUint16(cursor + 4, 20, true); // version made by
    view.setUint16(cursor + 6, 20, true); // version needed
    view.setUint16(cursor + 8, UTF8_FLAG, true);
    view.setUint16(cursor + 10, 0, true); // method = store
    view.setUint16(cursor + 12, DOS_TIME, true);
    view.setUint16(cursor + 14, DOS_DATE, true);
    view.setUint32(cursor + 16, entry.crc, true);
    view.setUint32(cursor + 20, entry.bytes.length, true);
    view.setUint32(cursor + 24, entry.bytes.length, true);
    view.setUint16(cursor + 28, entry.name.length, true);
    view.setUint16(cursor + 30, 0, true); // extra
    view.setUint16(cursor + 32, 0, true); // comment
    view.setUint16(cursor + 34, 0, true); // disk number start
    view.setUint16(cursor + 36, 0, true); // internal attrs
    view.setUint32(cursor + 38, 0, true); // external attrs
    view.setUint32(cursor + 42, offsets[index]!, true);
    buffer.set(entry.name, cursor + CENTRAL_HEADER_SIZE);
    cursor += CENTRAL_HEADER_SIZE + entry.name.length;
  }

  view.setUint32(cursor, EOCD_SIG, true);
  view.setUint16(cursor + 4, 0, true); // disk number
  view.setUint16(cursor + 6, 0, true); // disk with central dir
  view.setUint16(cursor + 8, encoded.length, true);
  view.setUint16(cursor + 10, encoded.length, true);
  view.setUint32(cursor + 12, cursor - centralStart, true);
  view.setUint32(cursor + 16, centralStart, true);
  view.setUint16(cursor + 20, 0, true); // comment length

  return buffer;
}

/** EOCD 시그니처를 뒤에서 찾는다. 없으면 -1. */
function findEocd(view: DataView, length: number): number {
  const min = Math.max(0, length - EOCD_SIZE - 0xffff);
  for (let offset = length - EOCD_SIZE; offset >= min; offset--) {
    if (view.getUint32(offset, true) === EOCD_SIG) return offset;
  }
  return -1;
}

/**
 * 저장해도 되는 경로인가. 디렉터리·절대경로·`..`·백슬래시·빈 이름을 거부한다.
 * (zip 안의 경로는 신뢰할 수 없는 입력이다.)
 */
function isSafeEntryPath(path: string): boolean {
  if (path.length === 0) return false;
  if (path.endsWith("/")) return false;
  if (path.startsWith("/")) return false;
  if (path.includes("\\")) return false;
  // ⚠️ 공백은 **허용**한다 — 프레임 이름에 공백이 정상적으로 들어간다("새 프레임 사본").
  // 드라이브 문자(`C:`)는 거부한다.
  if (/^[A-Za-z]:/.test(path)) return false;
  return !path.split("/").includes("..");
}

/**
 * EOCD → 중앙 디렉터리 → 로컬 헤더 순으로 읽는다.
 * 어떤 입력에도 **예외를 던지지 않는다**. 손상·미지원 항목은 건너뛴다.
 */
export function parseZipEntries(bytes: Uint8Array): ParsedZipEntry[] {
  const results: ParsedZipEntry[] = [];
  if (bytes.length < EOCD_SIZE) return results;

  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const eocd = findEocd(view, bytes.length);
  if (eocd < 0) return results;

  const count = view.getUint16(eocd + 10, true);
  const centralOffset = view.getUint32(eocd + 16, true);
  if (centralOffset >= bytes.length) return results;

  let cursor = centralOffset;
  for (let index = 0; index < count; index++) {
    if (cursor + CENTRAL_HEADER_SIZE > bytes.length) break;
    if (view.getUint32(cursor, true) !== CENTRAL_SIG) break;

    const method = view.getUint16(cursor + 10, true);
    const crc = view.getUint32(cursor + 16, true);
    const compressedSize = view.getUint32(cursor + 20, true);
    const uncompressedSize = view.getUint32(cursor + 24, true);
    const nameLength = view.getUint16(cursor + 28, true);
    const extraLength = view.getUint16(cursor + 30, true);
    const commentLength = view.getUint16(cursor + 32, true);
    const localOffset = view.getUint32(cursor + 42, true);
    const nameStart = cursor + CENTRAL_HEADER_SIZE;
    const nameEnd = nameStart + nameLength;
    if (nameEnd > bytes.length) break;

    const path = decodeName(bytes.subarray(nameStart, nameEnd));
    cursor = nameEnd + extraLength + commentLength;

    if (method !== 0 && method !== 8) continue;
    if (!isSafeEntryPath(path)) continue;
    if (localOffset + LOCAL_HEADER_SIZE > bytes.length) continue;
    if (view.getUint32(localOffset, true) !== LOCAL_SIG) continue;

    // 로컬 헤더의 이름·extra 길이는 중앙 디렉터리와 다를 수 있다 — 데이터 시작은 여기서 읽는다.
    const localNameLength = view.getUint16(localOffset + 26, true);
    const localExtraLength = view.getUint16(localOffset + 28, true);
    const dataStart = localOffset + LOCAL_HEADER_SIZE + localNameLength + localExtraLength;
    const dataEnd = dataStart + compressedSize;
    if (dataEnd > bytes.length) continue;

    results.push({
      path,
      method,
      data: bytes.slice(dataStart, dataEnd),
      crc32: crc,
      uncompressedSize,
    });
  }

  return results;
}
