/**
 * 업로드 오케스트레이션 규칙 — Windows `Upload/UploadService.cs`의 순수 판정 부분 이식 (analysis/31 §5)
 *
 * 와이어 호출(prepare → 서명 PUT → commit)은 어댑터가 하고, 여기서는 **무엇을 올릴지**와
 * **진행률을 어떻게 합산할지**만 정한다.
 */

/** 업로드 단계. `Finalizing` = commit. */
export const UPLOAD_STAGES = ["Photo", "Timelapse", "Finalizing"] as const;
export type UploadStage = (typeof UPLOAD_STAGES)[number];

export interface UploadTargetInput {
  /** 설정 토글 — 사진을 보낼 의도가 있는가. */
  readonly sendPhoto: boolean;
  /** 설정 토글 — 타임랩스를 보낼 의도가 있는가. */
  readonly sendTimelapse: boolean;
  /** 합성 결과물이 실제로 존재하는가. */
  readonly hasFinalImage: boolean;
  /** 타임랩스 파일이 실제로 존재하는가(인코더 미지원·실패면 false). */
  readonly hasTimelapse: boolean;
}

export interface UploadTargets {
  readonly uploadPhoto: boolean;
  readonly uploadTimelapse: boolean;
  /**
   * 올릴 것이 1개 이상인가. **false면 업로드를 시도하지 않는다**(M7) —
   * "전송할 결과물이 없습니다."를 표시하고 끝낸다. 빈 commit을 만들지 않는다.
   */
  readonly canUpload: boolean;
}

/**
 * 전송 대상 확정 = **설정 토글 AND 파일 존재**.
 * 토글이 켜져 있어도 파일이 없으면 올리지 않는다(타임랩스 미지원 브라우저에서 정상 축소 — VF-6).
 */
export function resolveUploadTargets(input: UploadTargetInput): UploadTargets {
  const uploadPhoto = input.sendPhoto && input.hasFinalImage;
  const uploadTimelapse = input.sendTimelapse && input.hasTimelapse;
  return { uploadPhoto, uploadTimelapse, canUpload: uploadPhoto || uploadTimelapse };
}

/**
 * 활성 단계 순서. 항상 사진 → 타임랩스 → commit 순이다(Windows 동일).
 * `canUpload`가 false면 빈 배열이다.
 */
export function activeStages(targets: UploadTargets): UploadStage[] {
  if (!targets.canUpload) return [];
  const stages: UploadStage[] = [];
  if (targets.uploadPhoto) stages.push("Photo");
  if (targets.uploadTimelapse) stages.push("Timelapse");
  stages.push("Finalizing");
  return stages;
}

/**
 * 전체 진행률(0~1) 합산. 각 활성 단계에 **균등 가중**을 주고 현재 단계의 파일 진행률을 더한다.
 *
 * 파일 진행률은 XHR `upload.onprogress`에서 온다(WM5 — `fetch`는 업로드 진행률을 주지 않는다).
 * 알 수 없는 단계·비활성 단계가 들어오면 0을 반환한다(조용히 100%로 점프하지 않는다).
 */
export function overallProgress(
  targets: UploadTargets,
  stage: UploadStage,
  stageFraction: number,
): number {
  const stages = activeStages(targets);
  if (stages.length === 0) return 0;

  const index = stages.indexOf(stage);
  if (index < 0) return 0;

  const weight = 1 / stages.length;
  const clampedFraction = Math.min(1, Math.max(0, stageFraction));
  return Math.min(1, index * weight + clampedFraction * weight);
}
