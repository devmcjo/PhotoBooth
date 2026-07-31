import { describe, expect, it } from "vitest";
import { buildStoreZip, crc32, parseZipEntries } from "@adapters/storage/zipStore";

/**
 * store(무압축) zip 코덱 — 05 §4.6 (설계 §9.1)
 *
 * ⚠️ 손상 항목은 **건너뛰고 계속**한다(`slotsFile.ts`와 동형).
 * ⚠️ 디렉터리·`..`·절대경로는 **버린다**(경로 조작 방어).
 */

const encoder = new TextEncoder();

function bytes(text: string): Uint8Array {
  return encoder.encode(text);
}

function text(data: Uint8Array): string {
  return new TextDecoder().decode(data);
}

describe("crc32 — 알려진 벡터", () => {
  it.each([
    ["", 0x00000000],
    ["a", 0xe8b7be43],
    ["abc", 0x352441c2],
    ["123456789", 0xcbf43926],
  ])("crc32(%j) = %s", (input, expected) => {
    expect(crc32(bytes(input))).toBe(expected >>> 0);
  });
});

describe("buildStoreZip → parseZipEntries 왕복", () => {
  it("바이트가 그대로 돌아온다", () => {
    const zip = buildStoreZip([
      { path: "여름 6컷.png", bytes: bytes("PNG-DATA") },
      { path: "여름 6컷.slots", bytes: bytes("#imagesize=100,200\n0,0,0,10,10\n") },
    ]);

    const entries = parseZipEntries(zip);
    expect(entries.map((entry) => entry.path)).toEqual(["여름 6컷.png", "여름 6컷.slots"]);
    expect(entries.every((entry) => entry.method === 0)).toBe(true);
    expect(text(entries[0]!.data)).toBe("PNG-DATA");
    expect(text(entries[1]!.data)).toBe("#imagesize=100,200\n0,0,0,10,10\n");
  });

  it("crc·크기가 기록된다", () => {
    const payload = bytes("hello");
    const entries = parseZipEntries(buildStoreZip([{ path: "a.png", bytes: payload }]));
    expect(entries[0]!.crc32).toBe(crc32(payload));
    expect(entries[0]!.uncompressedSize).toBe(payload.length);
  });

  it("빈 목록도 유효한 zip이다(항목 0개)", () => {
    expect(parseZipEntries(buildStoreZip([]))).toEqual([]);
  });

  it("한글·공백이 있는 이름이 깨지지 않는다(UTF-8 플래그)", () => {
    const entries = parseZipEntries(
      buildStoreZip([{ path: "새 프레임 사본 2.png", bytes: bytes("x") }]),
    );
    expect(entries[0]!.path).toBe("새 프레임 사본 2.png");
  });

  it("빈 파일도 왕복한다", () => {
    const entries = parseZipEntries(buildStoreZip([{ path: "empty.slots", bytes: bytes("") }]));
    expect(entries).toHaveLength(1);
    expect(entries[0]!.data.length).toBe(0);
  });
});

describe("parseZipEntries — 손상·위험 입력", () => {
  it("빈 바이트는 `[]`다(예외를 던지지 않는다)", () => {
    expect(parseZipEntries(new Uint8Array(0))).toEqual([]);
  });

  it("EOCD가 없으면 `[]`다", () => {
    expect(parseZipEntries(bytes("이건 zip이 아니다".repeat(10)))).toEqual([]);
  });

  it("EOCD를 훼손하면 `[]`다", () => {
    const zip = buildStoreZip([{ path: "a.png", bytes: bytes("x") }]);
    // 마지막 22바이트가 EOCD다 — 시그니처를 부순다.
    zip[zip.length - 22] = 0;
    expect(parseZipEntries(zip)).toEqual([]);
  });

  it("**디렉터리 엔트리를 버린다**", () => {
    const entries = parseZipEntries(
      buildStoreZip([
        { path: "folder/", bytes: bytes("") },
        { path: "folder/a.png", bytes: bytes("x") },
      ]),
    );
    expect(entries.map((entry) => entry.path)).toEqual(["folder/a.png"]);
  });

  it("**`..`·절대경로·백슬래시·드라이브 문자를 버린다**", () => {
    const entries = parseZipEntries(
      buildStoreZip([
        { path: "../escape.png", bytes: bytes("x") },
        { path: "a/../../escape.png", bytes: bytes("x") },
        { path: "/absolute.png", bytes: bytes("x") },
        { path: "C:\\windows\\evil.png", bytes: bytes("x") },
        { path: "back\\slash.png", bytes: bytes("x") },
        { path: "ok.png", bytes: bytes("x") },
      ]),
    );
    expect(entries.map((entry) => entry.path)).toEqual(["ok.png"]);
  });

  it("`..`가 이름의 일부인 것은 버리지 않는다(세그먼트 단위 판정)", () => {
    const entries = parseZipEntries(buildStoreZip([{ path: "a..b.png", bytes: bytes("x") }]));
    expect(entries.map((entry) => entry.path)).toEqual(["a..b.png"]);
  });

  it("중앙 디렉터리 오프셋이 파일 밖이면 `[]`다", () => {
    const zip = buildStoreZip([{ path: "a.png", bytes: bytes("x") }]);
    const view = new DataView(zip.buffer, zip.byteOffset, zip.byteLength);
    view.setUint32(zip.length - 22 + 16, 0xffffff, true);
    expect(parseZipEntries(zip)).toEqual([]);
  });

  it("바이트를 뒤에서 잘라도 예외 없이 축소된다", () => {
    const zip = buildStoreZip([{ path: "a.png", bytes: bytes("hello") }]);
    for (let cut = 1; cut < 40; cut++) {
      expect(() => parseZipEntries(zip.slice(0, zip.length - cut))).not.toThrow();
    }
  });
});
