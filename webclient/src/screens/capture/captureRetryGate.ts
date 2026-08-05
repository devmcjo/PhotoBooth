/**
 * 촬영 진입 재시도 게이트 — 03 §6.3 [다시 시도]
 *
 * `Capture` 진입 절차(03 §6.1)는 1단계 카메라 시작이 실패하면 **시퀀스를 시작하지 않고 그대로
 * 끝난다.** 그때 손님에게 남는 선택지가 [취소]뿐이었다 — 다른 앱이 카메라를 잠깐 물고 있었을
 * 뿐인데도(`inUse`) 세션을 통째로 버려야 했다. 03 §6.3의 실패 사유 표는 `noDevice`·`inUse`·
 * `unknown`에 [다시 시도]를 요구한다.
 *
 * 이 게이트가 하는 일은 **"같은 진입 절차를 다시 태우는 것"** 하나뿐이다.
 * 카메라를 여는 절차 자체(`camera.start()` 인자·Ready 대기·작업 공간·컷 루프)는 건드리지 않는다
 * — F7 non-goal("`Capture` 진입 시의 기존 카메라 시작 경로가 무수정")이 지키려는 것이 그 절차다.
 * 바뀌는 것은 **누가 몇 번 부르는가**뿐이다.
 *
 * ⚠️ **겹쳐 실행하면 안 된다.** 진입 절차는 컷 루프 완료까지 await한다. 진행 중에 다시 부르면
 *    시퀀스가 두 개 생겨 같은 컷을 두 번 찍고, 두 번째 `configureShell`이 첫 시퀀스의 취소 경로를
 *    덮어써 취소가 먹지 않는다. 그래서 재시도는 **진행 중이면 무시**한다.
 * ⚠️ 언마운트(`disposed`) 뒤의 재시도는 카메라를 되살릴 뿐이다 — LED가 켜진 채 남는다.
 */

export interface CaptureRetryGateDeps {
  /**
   * 진입 절차 1회분. 컷 루프가 끝나거나(정상·취소) 시작 전에 실패해야 resolve한다.
   * 게이트는 이 함수의 **내용을 모른다** — 그래서 진입 절차를 수정하지 않고 재사용할 수 있다.
   */
  readonly run: () => Promise<void>;
  /** 화면을 이미 벗어났는지. cleanup 이후의 호출을 전부 막는다. */
  readonly disposed: () => boolean;
  /**
   * 진입 절차가 예외로 끝났을 때의 보고 경로. `void run()`으로 흘려보내면 미처리 rejection이 된다
   * (운영 기기에서는 콘솔을 열 수 없어 그대로 사라진다 — 05 §7).
   */
  readonly onError: (err: unknown) => void;
}

export interface CaptureRetryGate {
  /** 최초 진입. 이미 진행 중이거나 폐기됐으면 무시한다. */
  start(): void;
  /** [다시 시도]. `start()`와 **같은 절차**를 탄다(부분 재개가 아니라 처음부터). */
  retry(): void;
  /** 진입 절차가 진행 중인지(테스트·진단용). */
  readonly running: boolean;
}

export function createCaptureRetryGate(deps: CaptureRetryGateDeps): CaptureRetryGate {
  let running = false;

  function invoke(): void {
    if (running || deps.disposed()) return;
    running = true;
    try {
      void deps
        .run()
        .catch((err: unknown) => {
          deps.onError(err);
        })
        // 예외 경로에서도 플래그를 반드시 푼다 — 잠긴 채 남으면 다시는 재시도할 수 없다.
        .finally(() => {
          running = false;
        });
    } catch (err) {
      // `run`이 동기적으로 던지는 경우(비동기 함수가 아닌 구현을 주입받은 경우)도 잠기지 않게 한다.
      running = false;
      deps.onError(err);
    }
  }

  return {
    start: invoke,
    retry: invoke,
    get running(): boolean {
      return running;
    },
  };
}
