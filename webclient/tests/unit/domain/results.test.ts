import { describe, expect, it } from "vitest";
import {
  finalFileName,
  isResultFolderName,
  MAX_RESULT_FOLDER_SUFFIX,
  RESULT_FOLDER_PREFIX,
  resolveResultFolderName,
  resultFolderName,
  resultFolderNameFromSessionId,
  TIMELAPSE_FILE_NAME,
} from "@domain/results/resultNaming";
import { planResultSave } from "@domain/results/resultSavePlan";
import {
  planResultsRetention,
  RESULTS_MAX_BYTES,
  RESULTS_MAX_SESSIONS,
  type ResultFolderUsage,
} from "@domain/results/resultsRetention";
import { newSessionId } from "@domain/upload/uploadContract";

/**
 * 결과물 보관 도메인 — 05 §5 · analysis/41 §5 (M6-W)
 *
 * 폴더명 규약은 **Windows와 같은 값**이어야 한다. 아래 리터럴은 C# 테스트와 짝이다.
 */

const TOKEN32 = "0123456789abcdef0123456789abcdef";

describe("resultFolderName — mcphoto_YYMMDD_HHMM", () => {
  it("Windows와 같은 리터럴을 만든다", () => {
    // ↔ tests/MCPhoto.Tests/LocalSaveTests.cs:33 (2026-07-20 14:45 → mcphoto_260720_1445)
    expect(resultFolderName(new Date(2026, 6, 20, 14, 45, 0))).toBe("mcphoto_260720_1445");
  });

  it("모든 성분을 0 패딩한다", () => {
    expect(resultFolderName(new Date(2026, 0, 2, 3, 4, 5))).toBe("mcphoto_260102_0304");
  });

  it("연·월·일 전환 경계에서도 어긋나지 않는다", () => {
    expect(resultFolderName(new Date(2030, 11, 31, 23, 59, 59))).toBe("mcphoto_301231_2359");
    expect(resultFolderName(new Date(2031, 0, 1, 0, 0, 0))).toBe("mcphoto_310101_0000");
  });

  it("접두는 상수와 같다", () => {
    expect(resultFolderName(new Date(2026, 6, 20, 14, 45, 0)).startsWith(RESULT_FOLDER_PREFIX)).toBe(
      true,
    );
  });
});

describe("resultFolderNameFromSessionId", () => {
  it("정상 sessionId에서 폴더명을 유도한다", () => {
    const sessionId = "20260720_144500_8f14e45f-ceea-467a-9f0c-1a2b3c4d5e6f";
    expect(resultFolderNameFromSessionId(sessionId)).toBe("mcphoto_260720_1445");
  });

  it("newSessionId(date, uuid)의 결과가 resultFolderName(date)와 같다(두 경로 정합)", () => {
    const date = new Date(2027, 2, 9, 8, 7, 6);
    const sessionId = newSessionId(date, "8f14e45f-ceea-467a-9f0c-1a2b3c4d5e6f");
    expect(resultFolderNameFromSessionId(sessionId)).toBe(resultFolderName(date));
  });

  it("형식이 어긋나면 null이다(호출자가 시각 폴백)", () => {
    expect(resultFolderNameFromSessionId("")).toBeNull();
    expect(resultFolderNameFromSessionId("20260720_144500")).toBeNull();
    expect(resultFolderNameFromSessionId("2026072_144500_8f14e45f-ceea-467a-9f0c-1a2b3c4d5e6f")).toBeNull();
    expect(resultFolderNameFromSessionId("../../etc")).toBeNull();
  });
});

describe("resolveResultFolderName — 충돌 해석", () => {
  const base = "mcphoto_260720_1445";

  it("충돌이 없으면 base 그대로다", () => {
    expect(resolveResultFolderName(base, [], TOKEN32)).toBe(base);
    expect(resolveResultFolderName(base, ["mcphoto_260720_1446"], TOKEN32)).toBe(base);
  });

  it("base가 있으면 -2, -2까지 있으면 -3", () => {
    expect(resolveResultFolderName(base, [base], TOKEN32)).toBe(`${base}-2`);
    expect(resolveResultFolderName(base, [base, `${base}-2`], TOKEN32)).toBe(`${base}-3`);
  });

  it("2..999가 전부 차 있으면 32자 hex 폴백이다(Windows Guid:N과 같은 모양)", () => {
    const existing = [base];
    for (let i = 2; i <= MAX_RESULT_FOLDER_SUFFIX; i++) existing.push(`${base}-${i}`);
    expect(resolveResultFolderName(base, existing, TOKEN32)).toBe(`${base}-${TOKEN32}`);
  });

  it("999까지는 접미를 쓴다(경계)", () => {
    const existing = [base];
    for (let i = 2; i < MAX_RESULT_FOLDER_SUFFIX; i++) existing.push(`${base}-${i}`);
    expect(resolveResultFolderName(base, existing, TOKEN32)).toBe(`${base}-999`);
  });
});

describe("finalFileName", () => {
  it("포맷에 따라 확장자가 바뀐다", () => {
    expect(finalFileName("Jpg")).toBe("final.jpg");
    expect(finalFileName("Png")).toBe("final.png");
  });

  it("타임랩스 파일명은 고정이다", () => {
    expect(TIMELAPSE_FILE_NAME).toBe("timelapse.mp4");
  });
});

describe("isResultFolderName — 삭제 후보 좁히기", () => {
  it("우리 규약 이름을 받아들인다", () => {
    expect(isResultFolderName("mcphoto_260720_1445")).toBe(true);
    expect(isResultFolderName("mcphoto_260720_1445-2")).toBe(true);
    expect(isResultFolderName("mcphoto_260720_1445-999")).toBe(true);
    expect(isResultFolderName(`mcphoto_260720_1445-${TOKEN32}`)).toBe(true);
  });

  it("규약 밖 이름은 거부한다(남의 데이터 보호)", () => {
    expect(isResultFolderName("frames")).toBe(false);
    expect(isResultFolderName("sessions")).toBe(false);
    expect(isResultFolderName("..")).toBe(false);
    expect(isResultFolderName("")).toBe(false);
    expect(isResultFolderName("mcphoto_1_1")).toBe(false);
    expect(isResultFolderName("mcphoto_260720_1445-1")).toBe(false); // 접미는 2부터다
    expect(isResultFolderName("mcphoto_260720_1445-1000")).toBe(false);
    expect(isResultFolderName("mcphoto_260720_1445-abc")).toBe(false);
    expect(isResultFolderName("mcphoto_260720_14455")).toBe(false);
    expect(isResultFolderName("xmcphoto_260720_1445")).toBe(false);
    expect(isResultFolderName("mcphoto_260720_1445/final.jpg")).toBe(false);
  });
});

describe("planResultSave", () => {
  const baseInput = {
    saveLocalCopy: true,
    hasFinalImage: true,
    hasTimelapse: true,
    format: "Jpg" as const,
    baseFolderName: "mcphoto_260720_1445",
    existingFolders: [] as readonly string[],
    fallbackToken: TOKEN32,
  };

  it("SaveLocalCopy가 꺼져 있으면 skip(disabled)", () => {
    expect(planResultSave({ ...baseInput, saveLocalCopy: false })).toEqual({
      kind: "skip",
      reason: "disabled",
    });
  });

  it("합성 이미지가 없으면 skip(no-image)", () => {
    expect(planResultSave({ ...baseInput, hasFinalImage: false })).toEqual({
      kind: "skip",
      reason: "no-image",
    });
  });

  it("타임랩스가 없으면 대상이 final 1개다(VF-6 — 정상 경로)", () => {
    const plan = planResultSave({ ...baseInput, hasTimelapse: false });
    expect(plan.kind).toBe("save");
    if (plan.kind !== "save") return;
    expect(plan.targets).toEqual([
      { kind: "final", fileName: "final.jpg", contentType: "image/jpeg" },
    ]);
  });

  it("타임랩스가 있으면 final이 **먼저**고 timelapse가 뒤다", () => {
    const plan = planResultSave(baseInput);
    expect(plan.kind).toBe("save");
    if (plan.kind !== "save") return;
    expect(plan.targets.map((t) => t.kind)).toEqual(["final", "timelapse"]);
    expect(plan.targets[1]).toEqual({
      kind: "timelapse",
      fileName: "timelapse.mp4",
      contentType: "video/mp4",
    });
  });

  it("Png 설정이면 final.png·image/png다", () => {
    const plan = planResultSave({ ...baseInput, format: "Png", hasTimelapse: false });
    expect(plan.kind).toBe("save");
    if (plan.kind !== "save") return;
    expect(plan.targets[0]).toEqual({
      kind: "final",
      fileName: "final.png",
      contentType: "image/png",
    });
  });

  it("같은 폴더가 이미 있으면 폴더명에 -2가 붙는다", () => {
    const plan = planResultSave({ ...baseInput, existingFolders: ["mcphoto_260720_1445"] });
    expect(plan.kind).toBe("save");
    if (plan.kind !== "save") return;
    expect(plan.folderName).toBe("mcphoto_260720_1445-2");
  });
});

describe("planResultsRetention — 05 §5.4", () => {
  function folder(name: string, bytes: number): ResultFolderUsage {
    return { name, bytes };
  }

  /** `mcphoto_2607{dd}_{HHMM}` 형태를 순번으로 만든다(0 패딩이라 사전순 = 시간순). */
  function series(count: number, bytes: number): ResultFolderUsage[] {
    const list: ResultFolderUsage[] = [];
    for (let i = 0; i < count; i++) {
      const hh = String(Math.floor(i / 60) % 24).padStart(2, "0");
      const mm = String(i % 60).padStart(2, "0");
      const dd = String(1 + Math.floor(i / 1440)).padStart(2, "0");
      list.push(folder(`mcphoto_2607${dd}_${hh}${mm}`, bytes));
    }
    return list;
  }

  it("한도 이하면 아무것도 지우지 않는다", () => {
    const decision = planResultsRetention(series(3, 1000));
    expect(decision.remove).toEqual([]);
    expect(decision.triggers).toEqual([]);
    expect(decision.keptCount).toBe(3);
    expect(decision.keptBytes).toBe(3000);
    expect(decision.stillOverLimit).toBe(false);
  });

  it("빈 목록도 안전하다", () => {
    expect(planResultsRetention([])).toEqual({
      remove: [],
      keptCount: 0,
      keptBytes: 0,
      triggers: [],
      stillOverLimit: false,
    });
  });

  it("201세션이면 가장 오래된 1개만 지운다", () => {
    const folders = series(RESULTS_MAX_SESSIONS + 1, 10);
    const decision = planResultsRetention(folders);
    expect(decision.remove).toEqual([folders[0]!.name]);
    expect(decision.keptCount).toBe(RESULTS_MAX_SESSIONS);
    expect(decision.triggers).toEqual(["count"]);
    expect(decision.stillOverLimit).toBe(false);
  });

  it("2GB를 넘으면 바이트 기준으로 오래된 것부터 축출한다", () => {
    const half = RESULTS_MAX_BYTES / 2;
    const folders = [
      folder("mcphoto_260720_1400", half),
      folder("mcphoto_260720_1500", half),
      folder("mcphoto_260720_1600", 1),
    ];
    const decision = planResultsRetention(folders);
    expect(decision.remove).toEqual(["mcphoto_260720_1400"]);
    expect(decision.triggers).toEqual(["bytes"]);
    expect(decision.keptBytes).toBe(half + 1);
    expect(decision.stillOverLimit).toBe(false);
  });

  it("두 조건이 동시에 걸리면 triggers가 2개다", () => {
    const folders = series(RESULTS_MAX_SESSIONS + 1, Math.ceil(RESULTS_MAX_BYTES / 100));
    const decision = planResultsRetention(folders);
    expect(decision.triggers).toEqual(["count", "bytes"]);
    expect(decision.remove.length).toBeGreaterThan(1);
  });

  it("최신 1개는 절대 삭제 후보가 아니다(3GB짜리 단일 폴더)", () => {
    const decision = planResultsRetention([folder("mcphoto_260720_1445", 3 * 1024 * 1024 * 1024)]);
    expect(decision.remove).toEqual([]);
    expect(decision.stillOverLimit).toBe(true);
    expect(decision.triggers).toEqual(["bytes"]);
  });

  it("규약 밖 이름은 삭제하지 않지만 회계에는 포함한다", () => {
    const folders = [
      folder("someone-elses-data", RESULTS_MAX_BYTES),
      folder("mcphoto_260720_1400", 100),
      folder("mcphoto_260720_1500", 100),
    ];
    const decision = planResultsRetention(folders);
    expect(decision.remove).toEqual(["mcphoto_260720_1400"]);
    expect(decision.keptBytes).toBe(RESULTS_MAX_BYTES + 100);
    expect(decision.remove).not.toContain("someone-elses-data");
  });

  it("한도를 인자로 낮출 수 있다(진단·테스트)", () => {
    const folders = series(4, 10);
    const decision = planResultsRetention(folders, { maxBytes: 1_000_000, maxSessions: 2 });
    expect(decision.remove).toEqual([folders[0]!.name, folders[1]!.name]);
    expect(decision.keptCount).toBe(2);
  });

  it("문자열 정렬 = 시간 정렬이다(0 패딩 규약)", () => {
    const times = [
      new Date(2026, 6, 20, 9, 5),
      new Date(2026, 6, 20, 10, 0),
      new Date(2026, 6, 21, 0, 0),
      new Date(2026, 11, 31, 23, 59),
    ];
    const names = times.map(resultFolderName);
    expect([...names].sort()).toEqual(names);
  });

  it("입력 순서가 뒤섞여도 오래된 것부터 지운다", () => {
    const folders = [
      folder("mcphoto_260720_1600", 10),
      folder("mcphoto_260720_1400", 10),
      folder("mcphoto_260720_1500", 10),
    ];
    const decision = planResultsRetention(folders, { maxBytes: 25, maxSessions: 200 });
    expect(decision.remove).toEqual(["mcphoto_260720_1400"]);
  });
});
