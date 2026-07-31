import type { OutputFormat } from "../settings/appSettings";
import { finalImageContentType, TIMELAPSE_CONTENT_TYPE } from "../upload/uploadContract";
import { finalFileName, resolveResultFolderName, TIMELAPSE_FILE_NAME } from "./resultNaming";

/**
 * 결과물 로컬 보관 계획(순수) — 05 §5.2 · M6-W
 *
 * "무엇을 어디에 쓸지"만 기술한다. `Blob`은 받지 않는다 — 어댑터가 `kind → Blob`을 매핑한다.
 * 덕분에 "타임랩스가 없으면 대상이 1개"라는 규칙이 브라우저 없이 검증된다(VF-6).
 */

export type ResultTargetKind = "final" | "timelapse";

export interface ResultSaveTarget {
  readonly kind: ResultTargetKind;
  readonly fileName: string;
  /** ② 계층 쓰기·향후 내보내기에서 쓴다. OPFS는 필요 없지만 계획을 완결시킨다. */
  readonly contentType: string;
}

/** 저장 계획. **판별 유니온**이라 호출자가 `skip` 처리를 빠뜨릴 수 없다. */
export type ResultSavePlan =
  | { readonly kind: "skip"; readonly reason: "disabled" | "no-image" }
  | {
      readonly kind: "save";
      readonly folderName: string;
      /** 항상 `final`이 먼저, 있으면 `timelapse`가 뒤. 이 순서로 기록한다. */
      readonly targets: readonly ResultSaveTarget[];
    };

export interface ResultSavePlanInput {
  /** 설정 `SaveLocalCopy`. false면 `skip`. */
  readonly saveLocalCopy: boolean;
  readonly hasFinalImage: boolean;
  /** 타임랩스가 **없는 것은 정상**이다(VF-6 · C3). */
  readonly hasTimelapse: boolean;
  readonly format: OutputFormat;
  /** 어댑터가 `resultFolderNameFromSessionId` 또는 `resultFolderName`으로 만든 값. */
  readonly baseFolderName: string;
  /** 보관 위치의 현재 폴더 목록. */
  readonly existingFolders: readonly string[];
  readonly fallbackToken: string;
}

/**
 * ⚠️ `saveLocalCopy` 게이트가 **여기** 있다. 화면마다 `if (values.SaveLocalCopy)`를 흩뿌리면
 *    진입점이 늘 때마다 게이트를 빠뜨린다.
 */
export function planResultSave(input: ResultSavePlanInput): ResultSavePlan {
  if (!input.saveLocalCopy) return { kind: "skip", reason: "disabled" };
  if (!input.hasFinalImage) return { kind: "skip", reason: "no-image" };

  const folderName = resolveResultFolderName(
    input.baseFolderName,
    input.existingFolders,
    input.fallbackToken,
  );

  const targets: ResultSaveTarget[] = [
    {
      kind: "final",
      fileName: finalFileName(input.format),
      contentType: finalImageContentType(input.format),
    },
  ];
  if (input.hasTimelapse) {
    targets.push({
      kind: "timelapse",
      fileName: TIMELAPSE_FILE_NAME,
      contentType: TIMELAPSE_CONTENT_TYPE,
    });
  }

  return { kind: "save", folderName, targets };
}
