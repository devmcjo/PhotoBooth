import { canDeleteFrame, canEditFrame } from "@domain/frames/frameEditPolicy";
import { isFrameListInteractive, type FrameLoadPhase } from "@domain/frames/frameLoadPolicy";
import { LOCAL_ID_PREFIX } from "@domain/frames/frameStorePolicy";
import type { FrameTemplate } from "@domain/frames/types";
import { canWriteFrames, isPower, type UserRole } from "@domain/roles/userRole";
import { logger } from "@adapters/storage/logStore";
import { formatCount, STRINGS } from "@ui/strings";
import type { FrameLoadReason } from "./frameLoadRunner";

/**
 * `FrameSelect`의 권한·게이트·삭제 흐름 — 03 §4·§15.5 · analysis/13 §6.1·§6.6
 *
 * React 무관이라 순서·문구가 node에서 통째로 검증된다(15 §3.1). 화면은 여기 함수를 부르기만 한다.
 */

export interface FrameSelectPermissions {
  /** [프레임 만들기] 노출. `advanced_user` 이상. */
  readonly canCreateFrame: boolean;
  /** 카드 ✕ 노출의 **역할 축**. 출처 축은 `canDeleteFrame(frame, role)`이 본다. */
  readonly canDeleteFrames: boolean;
  /** manager·admin. "서버에서도 제거" 체크박스의 노출 조건이자 서버 삭제의 실행 조건. */
  readonly isPower: boolean;
}

/**
 * 권한 2축(03 §4). ⚠️ `canWriteFrames`와 `isPower`는 **별개 축**이다 —
 * `advanced_user`는 프레임을 만들고 지울 수 있지만 서버 공용 프레임은 건드리지 못한다.
 */
export function frameSelectPermissions(role: UserRole | null): FrameSelectPermissions {
  if (role === null) {
    return { canCreateFrame: false, canDeleteFrames: false, isPower: false };
  }
  return {
    canCreateFrame: canWriteFrames(role),
    canDeleteFrames: canWriteFrames(role),
    isPower: isPower(role),
  };
}

/**
 * 액션 가드(M10 ②). **각 액션 함수 첫 줄**에서 부른다.
 * 렌더 가드(scrim + `disabled`)만 두면 키보드 포커스·자동화·경쟁 상태로 우회된다.
 */
export function guardInteractive(phase: FrameLoadPhase): boolean {
  return isFrameListInteractive(phase);
}

/**
 * 삭제 확인 오버레이를 열 수 있는가.
 * ⚠️ `canDeleteFrame`은 **2인자**다 — `userId`를 넘기면 power가 fork 저장한 *공용* 로컬 프레임의
 *    삭제 능력이 회귀한다(그 프레임은 `userId=null`로 로드된다). 타인의 개인 프레임은
 *    `listPersonal(currentUserId)` 필터에서 이미 제외됐다.
 */
export function canOpenDelete(
  frame: FrameTemplate | null,
  role: UserRole | null,
  phase: FrameLoadPhase,
): boolean {
  if (!guardInteractive(phase)) return false;
  if (frame === null) return false;
  return canDeleteFrame(frame, role);
}

/** [선택 편집] 노출·실행 게이트. 편집은 소유자 축이 있으므로 **3인자**다. */
export function canEditSelected(
  frame: FrameTemplate | null,
  role: UserRole | null,
  userId: string | null,
  phase: FrameLoadPhase,
): boolean {
  if (!guardInteractive(phase)) return false;
  if (frame === null) return false;
  return canEditFrame(frame, role, userId);
}

// ─────────────────────────────── [다음] ───────────────────────────────

export interface FrameNextDeps {
  readonly phase: FrameLoadPhase;
  readonly selected: FrameTemplate | null;
  readonly configuredCutCount: number;
  /**
   * 프레임 확정 + 컷 수 해석. ★ 실제 구현은 `fixFrameAndResolveCutCount` **한 곳**이며
   * 이 화면이 유일한 해석 지점이다(VF-12 · WD19). 여기서 컷 수를 다시 계산하지 마라.
   */
  fixFrame(frame: FrameTemplate, configuredCutCount: number): void;
  go(): void;
}

/** [다음]. 국면·선택 가드를 통과했을 때만 컷 수를 해석하고 전이한다. */
export function resolveNext(deps: FrameNextDeps): boolean {
  if (!guardInteractive(deps.phase)) return false;
  if (deps.selected === null) return false;
  deps.fixFrame(deps.selected, deps.configuredCutCount);
  deps.go();
  return true;
}

// ─────────────────────────────── 삭제 ───────────────────────────────

export interface FrameDeleteDeps {
  /** 로컬 사본 삭제. 성공 판정은 **실제 부재 확인**이다(M4). */
  deleteLocal(frame: FrameTemplate): Promise<boolean>;
  /** `DELETE /frames/{id}`. `{deleted:false}`는 **성공이 아니다**. 예외는 그대로 던진다. */
  deleteServer(id: string): Promise<boolean>;
  /** 이름 매칭 재시도용 서버 공용 목록. */
  serverFrames(): Promise<readonly FrameTemplate[]>;
  /** 목록에서 제거 + 선택 이동 + **오버레이 닫기**(화면 상태). */
  applyRemoved(frame: FrameTemplate): void;
  /** 인라인 안내(`role="alert"`)에 남긴다 — 토스트가 아니다(4초 뒤 사라지면 안 되는 정보다). */
  setNotice(notice: string): void;
  reload(reason: FrameLoadReason): Promise<void>;
}

export interface FrameDeleteInput {
  readonly frame: FrameTemplate;
  /** 체크박스 값. ⚠️ **오버레이를 닫기 전에** 읽은 스냅샷이어야 한다. */
  readonly alsoServer: boolean;
  readonly isPower: boolean;
}

export interface FrameDeleteResult {
  readonly localOk: boolean;
  /** 실제로 서버 삭제를 시도했는가(비power의 `alsoServer=true`는 무시된다). */
  readonly serverAttempted: boolean;
  readonly notice: string;
}

function describe(err: unknown): string {
  return err instanceof Error ? err.message : String(err);
}

/** 서버 삭제 + 이름 매칭 재시도. 결과 문구를 돌려준다(성공 오인 금지). */
async function deleteFromServer(deps: FrameDeleteDeps, frame: FrameTemplate): Promise<string> {
  // `local:` 접두는 로컬 전용 프레임(서버 문서 없음). 그 외는 실 DB 문서 id를 담고 있다.
  const serverId = frame.id.startsWith(LOCAL_ID_PREFIX)
    ? frame.id.slice(LOCAL_ID_PREFIX.length)
    : frame.id;

  try {
    let deleted = await deps.deleteServer(serverId);

    // id로 못 찾으면(dbId 누락·불일치) 이름으로 서버 기본 프레임을 찾아 삭제한다.
    if (!deleted) {
      const list = await deps.serverFrames();
      const match = list.find((f) => f.name === frame.name && f.id.length > 0) ?? null;
      if (match !== null) {
        logger.info("서버 삭제 id 불일치 → 이름 매칭 재삭제", { name: frame.name, id: match.id });
        deleted = await deps.deleteServer(match.id);
      }
    }

    if (deleted) return STRINGS.frames.deleteServerOk;

    logger.warn("서버 프레임 삭제 실패: 문서 미발견", { name: frame.name, triedId: serverId });
    return formatCount(STRINGS.frames.deleteServerNotFound, frame.name);
  } catch (err) {
    logger.error("프레임 서버 삭제 실패", { id: serverId, reason: describe(err) });
    return formatCount(STRINGS.frames.deleteServerFailed, describe(err));
  }
}

/**
 * 삭제 실행 — **순서가 규격이다**(03 §15.5 · 05 §4.7).
 *
 * ```
 * ① alsoServer 확정(오버레이를 닫기 전에 지역 값으로)  ② 로컬 삭제  ③ 목록 제거 + 오버레이 닫기
 * ④ 서버 삭제  ⑤ 결과 문구(로컬 실패는 덧붙인다)  ⑥ 조용한 재스캔
 * ```
 *
 * ⚠️ ⑥이 `loadPublic`을 다시 부르므로 **로컬만 지운 DB 유래 공용 프레임은 재다운로드되어 카드가
 *    돌아온다.** Windows가 명시적으로 보존한 동작이며(K3), "서버에서도 제거"를 체크해야 영구
 *    삭제된다. 대기 UI 변경이 삭제 의미론을 조용히 바꾸지 않게 그대로 둔다.
 */
export async function runFrameDelete(
  deps: FrameDeleteDeps,
  input: FrameDeleteInput,
): Promise<FrameDeleteResult> {
  // ① 오버레이가 곧 닫히며 체크 상태가 리셋되므로 **먼저** 확정한다.
  const alsoServer = input.alsoServer && input.isPower;

  // ② 로컬 삭제는 항상 실행한다.
  const localOk = await deps.deleteLocal(input.frame);

  // ③ 목록·선택·오버레이 상태를 화면에 반영한다.
  deps.applyRemoved(input.frame);

  // ④
  let notice = alsoServer ? await deleteFromServer(deps, input.frame) : "";

  // ⑤ 성공 오인 금지: 로컬 실패는 서버 결과와 **함께** 보고한다.
  if (!localOk) {
    notice =
      notice.length === 0
        ? STRINGS.frames.deleteLocalFailed
        : notice + STRINGS.frames.deleteLocalFailedSuffix;
    logger.warn("로컬 프레임 삭제 실패", { id: input.frame.id, name: input.frame.name });
  }
  deps.setNotice(notice);

  // ⑥ 디스크 기준 재스캔(오버레이·진행 문구 없음).
  await deps.reload("refresh");

  return { localOk, serverAttempted: alsoServer, notice };
}
