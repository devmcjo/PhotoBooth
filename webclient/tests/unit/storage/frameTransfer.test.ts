import { describe, expect, it } from "vitest";
import { LOCAL_FRAME_LIMIT } from "@domain/frames/frameStorePolicy";
import { serializeSlotsFile } from "@domain/frames/slotsFile";
import type { FrameTemplate, ImageSize, Slot } from "@domain/frames/types";
import {
  applyFrameImport,
  dedupeEntryNames,
  exportFrames,
  frameEntryBaseName,
  frameZipFileName,
  logExportFileName,
  exportLogs,
  previewFrameImport,
  type FrameImportDeps,
} from "@adapters/storage/exportImport";
import { buildStoreZip, parseZipEntries } from "@adapters/storage/zipStore";

/**
 * 프레임 · 로그 내보내기 / 가져오기 — 05 §2.5·§4.6·§7 (설계 §9)
 *
 * ⚠️ 실패한 프레임이 **성공으로 집계되지 않는다**(M4).
 * ⚠️ 가져오기는 **항상 개인 스코프**이고 `dbId`를 기록하지 않는다(05 §4.4).
 */

const decoder = new TextDecoder();
const encoder = new TextEncoder();

function frame(overrides: Partial<FrameTemplate> & { name: string }): FrameTemplate {
  return {
    id: `local:${overrides.name}`,
    userId: null,
    isDefault: true,
    imageUrl: "",
    imageSize: { width: 1200, height: 1800 },
    slots: [{ index: 0, x: 10, y: 20, width: 100, height: 200 }],
    createdAt: "2026-01-01T00:00:00.000Z",
    ...overrides,
  };
}

describe("파일명 규약", () => {
  it("로그·zip 파일명이 `{YYMMDD_HHMM}` 로컬 시각을 쓴다", () => {
    const local = new Date(2026, 7, 1, 9, 5);
    expect(logExportFileName(local)).toBe("mcphoto-log-260801_0905.log");
    expect(frameZipFileName(local)).toBe("mcphoto-frames-260801_0905.zip");
  });

  it("공용은 `{이름}`, 개인은 `{계정}_{이름}`이다(Windows `Frame\\` 규약)", () => {
    expect(frameEntryBaseName(frame({ name: "여름 6컷" }))).toBe("여름 6컷");
    expect(frameEntryBaseName(frame({ name: "내 프레임", userId: "devmcjo" }))).toBe(
      "devmcjo_내 프레임",
    );
  });

  it("중복 base는 `-2`, `-3`을 붙인다", () => {
    expect(dedupeEntryNames(["a", "a", "b", "a"])).toEqual(["a", "a-2", "b", "a-3"]);
  });
});

describe("exportLogs", () => {
  it("성공하면 `.log`를 쓴다", async () => {
    const written: { name: string; text: string }[] = [];
    const ok = await exportLogs({
      exportText: async () => "line1\nline2",
      write: (blob, fileName) => {
        written.push({ name: fileName, text: "" });
        expect(blob.type).toContain("text/plain");
        return true;
      },
      now: () => new Date(2026, 7, 1, 9, 5),
    });
    expect(ok).toBe(true);
    expect(written[0]!.name).toBe("mcphoto-log-260801_0905.log");
  });

  it("`exportText()`가 던져도 **`false`**다(모달이 닫히지 않는다)", async () => {
    let wrote = false;
    const ok = await exportLogs({
      exportText: async () => {
        throw new Error("IndexedDB 실패");
      },
      write: () => {
        wrote = true;
        return true;
      },
      now: () => new Date(),
    });
    expect(ok).toBe(false);
    expect(wrote).toBe(false);
  });

  it("쓰기 실패는 `false`다", async () => {
    const ok = await exportLogs({
      exportText: async () => "x",
      write: () => false,
      now: () => new Date(),
    });
    expect(ok).toBe(false);
  });
});

describe("exportFrames", () => {
  function exportDeps(overrides: Record<string, unknown> = {}) {
    const written: { blob: Blob; name: string }[] = [];
    return {
      written,
      deps: {
        userId: "devmcjo",
        listPublic: async () => [frame({ name: "공용A" })],
        listPersonal: async () => [frame({ name: "개인B", userId: "devmcjo" })],
        readImageBytes: async () => new Blob([encoder.encode("PNGBYTES")]),
        write: (blob: Blob, name: string) => {
          written.push({ blob, name });
          return true;
        },
        now: () => new Date(2026, 7, 1, 9, 5),
        ...overrides,
      },
    };
  }

  it("공용·개인 프레임의 png+slots를 zip에 담는다", async () => {
    const { deps, written } = exportDeps();
    const outcome = await exportFrames(deps as never);
    expect(outcome).toEqual({ ok: true, exported: 2, skipped: 0 });

    const zip = new Uint8Array(await written[0]!.blob.arrayBuffer());
    const entries = parseZipEntries(zip);
    expect(entries.map((entry) => entry.path).sort()).toEqual([
      "devmcjo_개인B.png",
      "devmcjo_개인B.slots",
      "공용A.png",
      "공용A.slots",
    ]);
  });

  it("`.slots` 본문이 `serializeSlotsFile`과 **동일**하다(`#dbid` 없음)", async () => {
    const { deps, written } = exportDeps();
    await exportFrames(deps as never);
    const entries = parseZipEntries(new Uint8Array(await written[0]!.blob.arrayBuffer()));
    const slots = entries.find((entry) => entry.path === "공용A.slots");
    const expected = serializeSlotsFile({
      imageSize: { width: 1200, height: 1800 },
      slots: [{ index: 0, x: 10, y: 20, width: 100, height: 200 }],
      dbId: null,
    });
    expect(decoder.decode(slots!.data)).toBe(expected);
    expect(decoder.decode(slots!.data)).not.toContain("#dbid");
  });

  it("이미지를 못 읽은 프레임은 **건너뛰고 개수를 보고**한다", async () => {
    const { deps } = exportDeps({
      readImageBytes: async (target: FrameTemplate) =>
        target.name === "공용A" ? new Blob([encoder.encode("x")]) : null,
    });
    const outcome = await exportFrames(deps as never);
    expect(outcome).toEqual({ ok: true, exported: 1, skipped: 1 });
  });

  it("게스트는 개인 프레임을 모으지 않는다", async () => {
    let personalCalls = 0;
    const { deps } = exportDeps({
      userId: null,
      listPersonal: async () => {
        personalCalls++;
        return [];
      },
    });
    const outcome = await exportFrames(deps as never);
    expect(personalCalls).toBe(0);
    expect(outcome.exported).toBe(1);
  });

  it("내보낼 프레임이 없으면 `ok:false`이고 파일을 만들지 않는다", async () => {
    const { deps, written } = exportDeps({
      listPublic: async () => [],
      listPersonal: async () => [],
    });
    expect(await exportFrames(deps as never)).toEqual({ ok: false, exported: 0, skipped: 0 });
    expect(written).toHaveLength(0);
  });

  it("전부 읽기 실패면 `ok:false`이고 skipped를 보고한다", async () => {
    const { deps } = exportDeps({ readImageBytes: async () => null });
    expect(await exportFrames(deps as never)).toEqual({ ok: false, exported: 0, skipped: 2 });
  });

  it("목록 조회가 던져도 크래시 없이 실패를 돌려준다", async () => {
    const { deps } = exportDeps({
      listPublic: async () => {
        throw new Error("IndexedDB 실패");
      },
    });
    expect(await exportFrames(deps as never)).toEqual({ ok: false, exported: 0, skipped: 0 });
  });
});

// ───────────────────────────── 가져오기 ─────────────────────────────

const PNG = encoder.encode("FAKE-PNG");

function slotsText(size: ImageSize, slots: readonly Slot[], dbId: string | null = null): string {
  return serializeSlotsFile({ imageSize: size, slots, dbId });
}

function zipFile(
  entries: readonly { path: string; text?: string; bytes?: Uint8Array }[],
): File {
  const zip = buildStoreZip(
    entries.map((entry) => ({
      path: entry.path,
      bytes: entry.bytes ?? encoder.encode(entry.text ?? ""),
    })),
  );
  return new File([zip], "frames.zip", { type: "application/zip" });
}

function importDeps(overrides: Partial<FrameImportDeps> = {}): FrameImportDeps {
  return {
    role: "advanced_user",
    userId: "devmcjo",
    existingNames: [],
    personalCount: 0,
    uniqueSuffix: () => "abcd1234",
    decodeImageSize: async () => ({ width: 1200, height: 1800 }),
    inflateRaw: async (bytes) => bytes,
    ...overrides,
  };
}

describe("previewFrameImport — 권한·상한", () => {
  it("로그인하지 않으면 거부한다", async () => {
    const result = await previewFrameImport(zipFile([]), importDeps({ userId: null }));
    expect(result).toEqual({ ok: false, reason: "not-logged-in" });
  });

  it("`canWriteFrames`가 false면 거부한다", async () => {
    for (const role of [null, "temp_user", "user"] as const) {
      const result = await previewFrameImport(zipFile([]), importDeps({ role }));
      expect(result).toEqual({ ok: false, reason: "no-write-permission" });
    }
  });

  it("이미 상한이면 거부한다", async () => {
    const result = await previewFrameImport(
      zipFile([]),
      importDeps({ personalCount: LOCAL_FRAME_LIMIT }),
    );
    expect(result).toEqual({ ok: false, reason: "limit-reached" });
  });

  it("항목이 없으면 `no-entries`다", async () => {
    const result = await previewFrameImport(zipFile([]), importDeps());
    expect(result.ok).toBe(false);
  });

  it("상한 직전이면 그 지점에서 멈추고 경고를 남긴다", async () => {
    const file = zipFile([
      { path: "A.png", bytes: PNG },
      { path: "A.slots", text: slotsText({ width: 1200, height: 1800 }, []) },
      { path: "B.png", bytes: PNG },
      { path: "B.slots", text: slotsText({ width: 1200, height: 1800 }, []) },
    ]);
    const result = await previewFrameImport(
      file,
      importDeps({ personalCount: LOCAL_FRAME_LIMIT - 1 }),
    );
    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.preview.candidates).toHaveLength(1);
    expect(result.preview.warnings).toContainEqual({ kind: "limit-reached" });
  });
});

describe("previewFrameImport — 이름·좌표", () => {
  const slots: readonly Slot[] = [{ index: 0, x: 100, y: 200, width: 300, height: 400 }];

  it("자기 계정 접두만 제거한다", async () => {
    const file = zipFile([
      { path: "devmcjo_내것.png", bytes: PNG },
      { path: "devmcjo_내것.slots", text: slotsText({ width: 1200, height: 1800 }, slots) },
      { path: "other_남의것.png", bytes: PNG },
      { path: "other_남의것.slots", text: slotsText({ width: 1200, height: 1800 }, slots) },
    ]);
    const result = await previewFrameImport(file, importDeps());
    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.preview.candidates.map((c) => c.name).sort()).toEqual([
      "other_남의것",
      "내것",
    ]);
  });

  it("이름이 겹치면 사본 이름을 만들고 `renamed`를 표시한다", async () => {
    const file = zipFile([
      { path: "여름.png", bytes: PNG },
      { path: "여름.slots", text: slotsText({ width: 1200, height: 1800 }, slots) },
    ]);
    const result = await previewFrameImport(file, importDeps({ existingNames: ["여름"] }));
    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.preview.candidates[0]!.renamed).toBe(true);
    expect(result.preview.candidates[0]!.name).toBe("여름 사본");
  });

  it("이름 검증 실패는 건너뛰고 경고에 담는다", async () => {
    const bad = "a".repeat(101);
    const file = zipFile([
      { path: `${bad}.png`, bytes: PNG },
      { path: `${bad}.slots`, text: slotsText({ width: 1200, height: 1800 }, slots) },
    ]);
    const result = await previewFrameImport(file, importDeps());
    expect(result.ok).toBe(false);
    if (result.ok) return;
    expect(result.reason).toBe("no-entries");
  });

  it("`.slots`가 없으면 건너뛰고 경고를 남긴다", async () => {
    const file = zipFile([
      { path: "슬롯없음.png", bytes: PNG },
      { path: "정상.png", bytes: PNG },
      { path: "정상.slots", text: slotsText({ width: 1200, height: 1800 }, slots) },
    ]);
    const result = await previewFrameImport(file, importDeps());
    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.preview.candidates.map((c) => c.name)).toEqual(["정상"]);
    expect(result.preview.warnings).toContainEqual({
      kind: "missing-slots",
      sourceName: "슬롯없음",
    });
  });

  it("`#imagesize`와 실제 PNG 크기가 다르면 **좌표를 환산한다**", async () => {
    const file = zipFile([
      { path: "확대.png", bytes: PNG },
      { path: "확대.slots", text: slotsText({ width: 600, height: 900 }, slots) },
    ]);
    // 실제 PNG는 1200×1800 → factor 2.
    const result = await previewFrameImport(file, importDeps());
    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.preview.candidates[0]!.slots[0]).toEqual({
      index: 0,
      x: 200,
      y: 400,
      width: 600,
      height: 800,
    });
    expect(result.preview.candidates[0]!.imageSize).toEqual({ width: 1200, height: 1800 });
  });

  it("크기가 같으면 좌표를 건드리지 않는다", async () => {
    const file = zipFile([
      { path: "그대로.png", bytes: PNG },
      { path: "그대로.slots", text: slotsText({ width: 1200, height: 1800 }, slots) },
    ]);
    const result = await previewFrameImport(file, importDeps());
    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.preview.candidates[0]!.slots[0]).toEqual(slots[0]);
  });

  it("**`#dbid`를 버린다**", async () => {
    const file = zipFile([
      { path: "서버것.png", bytes: PNG },
      {
        path: "서버것.slots",
        text: slotsText({ width: 1200, height: 1800 }, slots, "server-doc-1"),
      },
    ]);
    const result = await previewFrameImport(file, importDeps());
    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(JSON.stringify(result.preview.candidates[0])).not.toContain("server-doc-1");
  });

  it("PNG 디코딩 실패는 건너뛰고 경고를 남긴다", async () => {
    const file = zipFile([
      { path: "깨진.png", bytes: PNG },
      { path: "깨진.slots", text: slotsText({ width: 1200, height: 1800 }, slots) },
    ]);
    const result = await previewFrameImport(file, importDeps({ decodeImageSize: async () => null }));
    expect(result.ok).toBe(false);
  });

  it("하위 폴더가 있어도 마지막 세그먼트로 묶는다(탐색기 압축)", async () => {
    const file = zipFile([
      { path: "Frame/여름.png", bytes: PNG },
      { path: "Frame/여름.slots", text: slotsText({ width: 1200, height: 1800 }, slots) },
    ]);
    const result = await previewFrameImport(file, importDeps());
    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.preview.candidates[0]!.name).toBe("여름");
  });

  it("확장자 대문자도 인식한다", async () => {
    const file = zipFile([
      { path: "여름.PNG", bytes: PNG },
      { path: "여름.SLOTS", text: slotsText({ width: 1200, height: 1800 }, slots) },
    ]);
    const result = await previewFrameImport(file, importDeps());
    expect(result.ok).toBe(true);
  });
});

describe("applyFrameImport", () => {
  const preview = {
    candidates: [
      {
        name: "여름",
        sourceName: "여름",
        imageSize: { width: 1200, height: 1800 },
        slots: [{ index: 0, x: 1, y: 2, width: 3, height: 4 }],
        renamed: false,
        bytes: new Blob([PNG]),
      },
      {
        name: "겨울",
        sourceName: "겨울",
        imageSize: { width: 1200, height: 1800 },
        slots: [],
        renamed: true,
        bytes: new Blob([PNG]),
      },
    ],
    warnings: [],
  };

  it("**항상 개인 스코프**이고 `dbId`가 null이다", async () => {
    const saved: { scope: string; ownerId: string; dbId: null }[] = [];
    const outcome = await applyFrameImport(preview, {
      userId: "devmcjo",
      saveLocal: async (input) => {
        saved.push({ scope: input.scope, ownerId: input.ownerId, dbId: input.dbId });
        return null as never;
      },
    });
    expect(saved).toEqual([
      { scope: "user", ownerId: "devmcjo", dbId: null },
      { scope: "user", ownerId: "devmcjo", dbId: null },
    ]);
    expect(outcome.imported + outcome.failed).toBe(2);
  });

  it("저장 실패를 성공으로 집계하지 않는다", async () => {
    let call = 0;
    const outcome = await applyFrameImport(preview, {
      userId: "devmcjo",
      saveLocal: async () => {
        call++;
        return call === 1 ? (frame({ name: "여름" }) as never) : null;
      },
    });
    expect(outcome).toEqual({ imported: 1, failed: 1 });
  });

  it("저장이 던져도 크래시 없이 실패로 집계한다", async () => {
    const outcome = await applyFrameImport(preview, {
      userId: "devmcjo",
      saveLocal: async () => {
        throw new Error("OPFS 실패");
      },
    });
    expect(outcome).toEqual({ imported: 0, failed: 2 });
  });
});
