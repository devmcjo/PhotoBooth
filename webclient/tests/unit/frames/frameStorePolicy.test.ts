import { describe, expect, it } from "vitest";
import {
  exceedsLocalFrameLimit,
  FRAME_IMAGE_DIR,
  frameIdFor,
  frameImagePath,
  frameStoreKey,
  isFrameRecord,
  LOCAL_FRAME_LIMIT,
  recordToTemplate,
  templateToRecord,
  type FrameRecord,
} from "@domain/frames/frameStorePolicy";
import { parseBundleManifest } from "@domain/frames/bundleManifest";
import { classifyFrameOrigin } from "@domain/frames/frameOrigin";
import type { FrameTemplate } from "@domain/frames/types";
import { OPFS_DIRS } from "@adapters/storage/opfsProtocol";

/**
 * 프레임 저장소 규약(순수) — 설계 §11.1 D2~D8
 *
 * 저장 키·id 접두·경로 방어·경계 검증은 **깨져도 조용한** 종류다: 키가 겹치면 프레임이 서로를
 * 덮어쓰고, id 접두가 어긋나면 삭제 권한이 통째로 바뀐다(`frameOrigin`이 id로 출처를 판정한다).
 */

const RECORD: FrameRecord = {
  key: "user:devmcjo:내프레임",
  scope: "user",
  ownerId: "devmcjo",
  name: "내프레임",
  id: "local:user:devmcjo:내프레임",
  dbId: null,
  imageFile: "frames/9f1c.png",
  imageSize: { width: 1200, height: 1600 },
  slots: [{ index: 0, x: 80, y: 140, width: 480, height: 640 }],
  createdAt: "2026-07-30T05:11:00.000Z",
  updatedAt: "2026-07-30T05:11:00.000Z",
};

describe("D2: frameStoreKey — 스코프별 유일 키", () => {
  it("공용은 public:{name}, 개인은 user:{owner}:{name}이다", () => {
    expect(frameStoreKey("public", null, "베이직 4컷")).toBe("public:베이직 4컷");
    expect(frameStoreKey("user", "devmcjo", "내프레임")).toBe("user:devmcjo:내프레임");
  });

  it("같은 이름이라도 스코프·소유자가 다르면 키가 다르다", () => {
    const keys = new Set([
      frameStoreKey("public", null, "A"),
      frameStoreKey("user", "u1", "A"),
      frameStoreKey("user", "u2", "A"),
    ]);
    expect(keys.size).toBe(3);
  });

  it("이름의 `:`가 키를 깨지 않는다 — 이름은 항상 마지막 세그먼트(나머지 전부)다", () => {
    const key = frameStoreKey("user", "devmcjo", "a:b:c");
    expect(key).toBe("user:devmcjo:a:b:c");
    // 앞 2개 세그먼트만 고정 의미이고 나머지가 이름이다 → 되읽어도 이름이 보존된다.
    const [scope, owner, ...rest] = key.split(":");
    expect(scope).toBe("user");
    expect(owner).toBe("devmcjo");
    expect(rest.join(":")).toBe("a:b:c");
    // 그럼에도 진실원은 `name` 필드다(키에서 이름을 되읽지 않는다).
    expect(frameStoreKey("public", null, "x:y")).toBe("public:x:y");
  });
});

describe("D3: frameIdFor — dbId 유무가 출처를 가른다(05 §4.4)", () => {
  it("dbId가 있으면 서버 문서 id를 그대로 쓴다", () => {
    expect(frameIdFor("public", null, "베이직", "srv-1")).toBe("srv-1");
  });

  it("dbId가 없으면 local:{key}다", () => {
    expect(frameIdFor("user", "devmcjo", "내프레임", null)).toBe("local:user:devmcjo:내프레임");
    // 빈 문자열도 "없음"으로 다룬다(서버 응답 결손 방어).
    expect(frameIdFor("public", null, "A", "")).toBe("local:public:A");
  });

  it("id 접두가 frameOrigin 판정과 정합한다", () => {
    const asFrame = (id: string): FrameTemplate => ({ ...recordToTemplate(RECORD, "u"), id });
    expect(classifyFrameOrigin(asFrame(frameIdFor("user", "u", "A", null)))).toBe("UserLocal");
    expect(classifyFrameOrigin(asFrame(frameIdFor("public", null, "A", "srv-1")))).toBe("DbDefault");
  });
});

describe("D4: frameImagePath — 경로 조작 1차 방어", () => {
  it("정상 토큰은 frames/{token}.png다", () => {
    expect(frameImagePath("9f1c")).toBe("frames/9f1c.png");
  });

  it("디렉터리 상수가 OPFS 규약과 같다(도메인이 값을 복제하고 있다)", () => {
    expect(FRAME_IMAGE_DIR).toBe(OPFS_DIRS.frames);
  });

  it("구분자·상대 참조·빈 토큰을 거부한다", () => {
    for (const bad of ["", "   ", "a/b", "a\\b", "..", "../x", ".", "x..y"]) {
      expect(frameImagePath(bad), bad).toBeNull();
    }
  });
});

describe("D5: isFrameRecord — 경계 검증(예외 0)", () => {
  it("정상 레코드를 통과시킨다", () => {
    expect(isFrameRecord(RECORD)).toBe(true);
    expect(isFrameRecord({ ...RECORD, scope: "public", ownerId: null })).toBe(true);
  });

  it("필수 필드 누락·타입 불일치·slots 비배열을 거부한다", () => {
    const cases: unknown[] = [
      null,
      undefined,
      "record",
      42,
      { ...RECORD, key: "" },
      { ...RECORD, scope: "team" },
      { ...RECORD, name: "" },
      { ...RECORD, id: 7 },
      { ...RECORD, dbId: 7 },
      { ...RECORD, imageFile: "" },
      { ...RECORD, imageSize: { width: "1200", height: 1600 } },
      { ...RECORD, imageSize: null },
      { ...RECORD, slots: "0,1,2" },
      { ...RECORD, slots: [{ index: 0, x: 1, y: 2, width: 3 }] },
      { ...RECORD, createdAt: 0 },
      { ...RECORD, updatedAt: null },
      { ...RECORD, ownerId: 12 },
    ];
    for (const value of cases) {
      expect(isFrameRecord(value), JSON.stringify(value)).toBe(false);
    }
  });
});

describe("D6: recordToTemplate / templateToRecord 왕복", () => {
  it("slots·imageSize를 보존한다", () => {
    const template = recordToTemplate(RECORD, "blob:x");
    expect(template.slots).toEqual(RECORD.slots);
    expect(template.imageSize).toEqual(RECORD.imageSize);
    expect(template.userId).toBe("devmcjo");
    expect(template.isDefault).toBe(false);

    const back = templateToRecord(template, {
      scope: "user",
      ownerId: "devmcjo",
      dbId: null,
      imageFile: RECORD.imageFile,
      updatedAt: RECORD.updatedAt,
    });
    expect(back).toEqual(RECORD);
  });

  it("공용 레코드는 userId=null·isDefault=true로 노출된다", () => {
    const publicRecord: FrameRecord = {
      ...RECORD,
      key: "public:베이직",
      scope: "public",
      ownerId: null,
      name: "베이직",
      id: "srv-1",
      dbId: "srv-1",
    };
    const template = recordToTemplate(publicRecord, "blob:y");
    expect(template.userId).toBeNull();
    expect(template.isDefault).toBe(true);
    expect(template.id).toBe("srv-1");
  });

  it("createdAt이 비어 있으면 updatedAt으로 메운다(빈 날짜 레코드 방지)", () => {
    const template: FrameTemplate = { ...recordToTemplate(RECORD, "u"), createdAt: "" };
    const record = templateToRecord(template, {
      scope: "user",
      ownerId: "devmcjo",
      dbId: null,
      imageFile: "frames/a.png",
      updatedAt: "2026-08-01T00:00:00.000Z",
    });
    expect(record.createdAt).toBe("2026-08-01T00:00:00.000Z");
  });
});

describe("D7: exceedsLocalFrameLimit — 계정당 10개(05 §4.8)", () => {
  it("10개면 초과, 9개면 아니다", () => {
    expect(LOCAL_FRAME_LIMIT).toBe(10);
    expect(exceedsLocalFrameLimit(10)).toBe(true);
    expect(exceedsLocalFrameLimit(9)).toBe(false);
    expect(exceedsLocalFrameLimit(11)).toBe(true);
    expect(exceedsLocalFrameLimit(0)).toBe(false);
  });
});

describe("D8: parseBundleManifest — 손상 항목을 건너뛴다", () => {
  it("정상 매니페스트를 파싱한다", () => {
    expect(
      parseBundleManifest([
        { name: "베이직 4컷", image: "basic4.png", slots: "basic4.slots", width: 1200, height: 1600 },
      ]),
    ).toEqual([
      { name: "베이직 4컷", image: "basic4.png", slots: "basic4.slots", width: 1200, height: 1600 },
    ]);
  });

  it("일부 항목이 손상돼도 나머지를 유지한다", () => {
    const parsed = parseBundleManifest([
      { name: "", image: "a.png", width: 10, height: 10 },
      { name: "정상", image: "b.png", width: 10, height: 10 },
      { name: "크기없음", image: "c.png" },
      { name: "음수", image: "d.png", width: -1, height: 10 },
      { name: "소수", image: "e.png", width: 10.5, height: 10 },
      { name: "경로조작", image: "../secret.png", width: 10, height: 10 },
      null,
      "문자열",
    ]);
    expect(parsed.map((e) => e.name)).toEqual(["정상"]);
    // slots가 없으면 null이고, 화면·어댑터가 2×2 자동 배치로 떨어진다.
    expect(parsed[0]?.slots).toBeNull();
  });

  it("배열이 아니면 빈 배열이고 어떤 입력에도 예외를 던지지 않는다", () => {
    for (const raw of [null, undefined, 0, "[]", { frames: [] }]) {
      expect(() => parseBundleManifest(raw)).not.toThrow();
      expect(parseBundleManifest(raw)).toEqual([]);
    }
  });

  it("slots 파일명의 경로 조작은 null로 축소한다(항목 자체는 유지)", () => {
    const parsed = parseBundleManifest([
      { name: "A", image: "a.png", slots: "../x.slots", width: 10, height: 10 },
    ]);
    expect(parsed).toHaveLength(1);
    expect(parsed[0]?.slots).toBeNull();
  });
});
