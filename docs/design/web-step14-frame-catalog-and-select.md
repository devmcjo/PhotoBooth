# 웹 클라이언트 Step 14 — 프레임 저장소 + 프레임 선택 화면 (it20 대기 국면 포함)

| 항목 | 값 |
|------|-----|
| 대상 | `webclient/`(TypeScript + React + Vite) · 브랜치 `feature/web-client-foundation` |
| 범위 | WBS [Step 14](../web-client/11-wbs.md#step-14-프레임-저장소--프레임-선택-화면) — 프레임 저장소(IndexedDB 메타 + OPFS PNG) · 카탈로그 로더(단일 비행 + 진행 replay) · `FrameSelect` 본편 · **it20 로딩 4국면** · 삭제 |
| 규격 진실원 | [`analysis/13 §4.2·§5·§6.1`](../analysis/13-client-behavior-spec.md) · [`analysis/41 §3`](../analysis/41-local-data-and-file-formats.md) · [`analysis/31 §4.10·§4.14`](../analysis/31-backend-api-reference.md) |
| 웹 규격 | [`03 §4·§4.1·§15.5`](../web-client/03-screens-spec.md) · [`05 §4`](../web-client/05-storage-and-persistence.md) · [`06 §6·§6.1`](../web-client/06-backend-integration-web.md) · [`02 §6.2`](../web-client/02-app-shell-and-navigation.md) · [`04 §5.2`](../web-client/04-media-pipeline-web.md) |
| Windows 참조 | `src/MCPhoto.App/Services/FrameCatalogService.cs` · `src/MCPhoto.App/ViewModels/FrameSelectViewModel.cs` · [it20 설계](./wpf-it20-frame-download-waiting-design.md) |
| 작성 | 2026-08-01 · js-architect |
| 상태 | 설계 확정 — js-developer 전달 가능 |

> **이 Step의 절반은 대기 국면(it20)이다.** 나머지 절반은 "프레임이 어디서 오고 어디에 저장되며 어떻게 지워지는가"다.
> 판정 계층(`frameLoadPolicy`·`frameCatalogProgress`·`frameCatalogPolicy`)은 **Step 8.5에서 이미 이식됐다** — 이 설계는 그 위에 어댑터·화면만 얹는다.

---

## 0. 검증된 사실 / 미검증 가정

### 0.1 검증된 사실 (2026-08-01, 코드·문서를 직접 읽어 확인)

- **F-1** 판정 도메인이 존재한다: `webclient/src/domain/frames/frameLoadPolicy.ts`(`FRAME_LOAD_PHASES`·`DEFAULT_FRAME_LOAD_PHASE`·`NO_PROGRESS_TIMEOUT_MS`·`MAX_TOTAL_WAIT_MS`·`nextFrameLoadDeadlineMs`·`classifyFrameLoad`·`finalizeFrameLoad`·`frameLoadNotice`), `frameCatalogProgress.ts`(`FRAME_CATALOG_PHASES`·`CATALOG_START_LABEL`·`catalogProgressLabel`). `docs/spec-vectors/frame-load-policy.json` 52케이스가 Windows와 교차 고정한다.
- **F-2** `frameCatalogPolicy.ts`에 `serverFramesToCache`·`dedupeByName`·`hasUsableImage`·`buildCatalog`·`hasUnderscoreCacheConflict`가 있다. `frameOrigin.ts`·`frameEditPolicy.ts`(`canDeleteFrame(frame, role)` — **2인자**)·`frameNaming.ts`·`slotsFile.ts`(`parseSlotsFile`)·`slotLayout.ts`(`autoArrange`)·`fallbackFrameSpec.ts`도 이식돼 있다.
- **F-3** `domain/index.ts`는 **평면 `export *` 배럴**이다(`src/domain/index.ts:21-32`). 짧은 일반명은 재수출 충돌을 만든다 — Step 8.5가 `classifyFrameLoad` 같은 한정형 이름을 쓴 이유.
- **F-4** `OpfsClient`에 `write`/`remove`/`list`/`exists`/`usage`/`capability`/`readFile`이 있다(`adapters/storage/opfsClient.ts`). **쓰기·삭제·열거는 Worker RPC**, `readFile`만 메인 스레드 직접 읽기다. 실패는 전부 `false`/`[]`/`null`이며 예외를 던지지 않는다. `OPFS_DIRS.frames === "frames"`.
- **F-5** `purgeSessionLeftovers`는 `sessions/`만 지운다 — `frames/` 캐시는 잔재 정리 대상이 아니다(`opfsClient.ts:207-214`).
- **F-6** 로그 스토어가 IndexedDB **`mcphoto` v1**(store `logs`)을 **앱 수명 내내 열어 두고** 있고 그 연결에 `onversionchange` 핸들러가 **없다**(`logStore.ts:160-174`). 같은 DB를 v2로 열면 업그레이드가 영구 blocked 된다. `dirHandleRepo`는 이 이유로 별 DB(`mcphoto-handles` v1)를 쓰고 트랜잭션마다 연결을 닫는다(`dirHandleRepo.ts:52-133`).
- **F-7** `logStore.ts:169`에 **낡은 주석**이 있다: *"프레임 메타 스토어는 Step 14(frameStore)가 같은 DB에 버전을 올려 추가한다."* F-6과 정면으로 충돌한다.
- **F-8** `frameRepository.deleteFrame(id): Promise<void>` 는 응답 본문을 **버린다**(`adapters/http/frameRepository.ts:134-141`). 서버는 `{ "deleted": true|false }`를 주고 **`deleted:false`는 성공이 아니다**(`analysis/31 §4.14`). 현재 구현으로는 "문서 미발견"을 구분할 수 없다.
- **F-9** `compositor.loadFrameImage`가 프레임 이미지를 **항상** `fetch(url, { mode: "cors", cache: "force-cache" })`로 읽는다(`adapters/compose/compositor.ts:52-59`). 즉 `FrameTemplate.imageUrl`이 무엇이든 이 경로를 지난다.
- **F-10** `frameRepository.getUserFrames(userId)`는 `auth: "required"`다. 401은 `backendClient`의 401 분기 → `handleSessionExpired` → **세션 해제 + 토스트**로 이어진다(15 §6 Step 12절).
- **F-11** 번들 프레임 자산이 **존재하지 않는다**: `webclient/public/`에는 `branding.json`·`icons/`·`manifest.webmanifest`뿐이다(VF-10과 정합).
- **F-12** 현행 `FrameSelectView`는 `ui/views/FlowViews.tsx:68-155`에 있고 fallback 1개로 동작한다. **컷 수 해석은 `fixFrameAndResolveCutCount(selected, configuredCutCount)` 한 줄**이며 [다음] 핸들러가 유일한 호출부다(VF-12).
- **F-13** `canTransition("FrameSelect", "FrameEditor") === true`이고 `isSessionActive("FrameSelect") === true`(유휴 감시 대상)다(`domain/navigation/stateMachine.ts:16,46`).
- **F-14** `shellStore.ModalId`에 `"confirmDelete"`·`"framePicker"`가 이미 선언돼 있고 `App.tsx`의 `ModalStack`은 미구현 스텁을 렌더한다 — **Step 15의 자리**다.
- **F-15** 유휴 상한 불변식(총 60초 < `IDLE_TIMEOUT_MS` 120초)은 `tests/unit/shell/shell.test.ts`가 이미 4건으로 고정한다.
- **F-16** `vitest` 환경은 `node`다(`vitest.config.ts`). jsdom·Testing Library가 없어 **React 훅·컴포넌트는 테스트에서 호출할 수 없다** → 판정·순서는 전부 React 밖 모듈에 둬야 검증된다(15 §3.1).
- **F-17** `visibility.ts`가 visible 복귀 시 유휴 감시를 재판정한다(Step 4 산출). 즉 탭 백그라운드 후 복귀의 최종 안전망은 유휴 경고다.

### 0.2 미검증 가정 (전부 검증 단계가 매핑돼 있다)

| # | 가정 | 검증 |
|---|------|------|
| **A-1** | `fetch(objectUrl)`(blob: 스킴)가 `cache`·`mode` 옵션 없이 정상 동작하고, `createImageBitmap`으로 디코드된다 | **Step 14-3**에서 `compositor`의 원격/로컬 분기를 넣어 **구조적으로 제거**한다(blob:에 `mode:"cors"`를 주지 않는다) + 실측 V23-4 |
| **A-2** | `URL.createObjectURL(File)`(OPFS 파일 핸들 유래)가 바이트를 메모리에 올리지 않고 지연 로드한다 | 실측 V23-6(프레임 20개 목록에서 메모리 증가 관측). 틀려도 동작은 정상이고 메모리만 늘어난다 |
| **A-3** | `createImageBitmap(blob, {resizeWidth,…})`의 resize 옵션이 대상 브라우저에서 실효한다 | **프로브로 런타임 판정**(04 §5.2 규격) — 미실효 시 캔버스 축소 폴백. Step 14-3 단위 테스트 + 실측 V23-5 |
| **A-4** | 서버 프레임 이미지(`firebasestorage.googleapis.com`)가 **200 응답에서도** CORS-clean하게 읽힌다 | OA-2와 동일 건. 실측 V23-3(온라인에서 서버 프레임으로 합성 성공) |
| **A-5** | IndexedDB `mcphoto-frames` v1 신설이 기존 `mcphoto`(로그)·`mcphoto-handles`(폴더)와 충돌하지 않는다 | Step 14-2 정적 검사(FR-3: 세 DB 이름이 서로 다름) + 실측 V23-7(DevTools에서 DB 3개 확인) |

---

## 1. 이 Step이 푸는 문제 4개

1. **첫 방문의 빈 화면.** 로컬 캐시가 비어 있으면 서버 다운로드를 기다려야 하는데, 지금은 기다리는 동안 "빈 목록 + 활성 [다음]"이 보인다. 웹은 이 상황이 Windows보다 훨씬 잦다(신규 기기·시크릿 창·저장소 비우기마다 첫 방문).
2. **줄 세우기.** 부트스트랩 prefetch와 화면 진입이 같은 작업을 두 번 하거나(중복 다운로드) 앞 작업이 끝날 때까지 줄을 서면(진행 문구 정체) 대기 예산이 통째로 낭비된다 — Windows it20 §6.3이 실제로 밟은 실패다.
3. **canvas 오염(WM2).** 서버 프레임을 `crossOrigin` 없이 그리면 `convertToBlob`이 `SecurityError`를 던져 **합성이 전면 실패**한다. 손님은 6컷을 다 찍은 뒤에야 그것을 안다.
4. **프레임의 생애.** 어디에 저장하고(IndexedDB 메타 + OPFS PNG), 어떻게 이름 dedup하며, 어떻게 지우고(로컬 항상 → power는 서버까지), 지운 뒤 목록을 **조용히** 갱신하는가.

---

## 2. 계층 배치 (한눈에)

```
                       ┌──────────────────────────────────────────┐
  ui/views/            │ FrameSelectView.tsx                      │  React (테스트 불가 — F-16)
                       │  ├ FrameGrid / FrameCard / FrameThumb    │
                       │  ├ FrameLoadingOverlay   (Loading)       │  ← 화면 로컬 오버레이 3종
                       │  ├ FrameFailedCard       (Failed)        │     (모달 스택 미사용)
                       │  └ FrameDeleteOverlay    (삭제 확인)      │
                       └───────────────┬──────────────────────────┘
                                       │ useFrameSelect()  ← 얇은 훅(상태 보관 + 배선만)
  screens/frameSelect/ ┌───────────────┴──────────────────────────┐
                       │ frameLoadRunner.ts   국면 수명·상한·finally│  React 무관 → node 테스트
                       │ frameSelectActions.ts 권한·[다음]·삭제 흐름 │
                       │ frameLoadDeadline.ts  실경과 상한 타이머   │
                       └───────────────┬──────────────────────────┘
  adapters/frames/     ┌───────────────┴──────────────────────────┐
                       │ frameCatalog.ts      단일 비행 + 진행 replay│  브라우저 격리·예외 미전파
                       │ frameDownloader.ts   CORS-clean 다운로드   │
                       │ frameImageCache.ts   object URL 소유·해제  │
                       │ frameThumbnails.ts   resize 프로브 + 폴백  │
                       │ bundleFrames.ts      /frames/index.json    │
                       │ fallbackFrame.ts     (기존 — 최종 폴백)     │
                       └───────────────┬──────────────────────────┘
  adapters/storage/    ┌───────────────┴──────────────────────────┐
                       │ frameStore.ts   IndexedDB 메타 + OPFS PNG  │  쓰기는 전부 opfsWriter Worker
                       │   └ FrameMetaStore 인터페이스(메모리/IDB)   │     (VF-14)
                       └───────────────┬──────────────────────────┘
  domain/frames/       ┌───────────────┴──────────────────────────┐
                       │ frameLoadPolicy(+isFrameListInteractive)   │  순수 · 벡터 교차 고정
                       │ frameCatalogProgress · frameCatalogPolicy  │
                       │ frameEditPolicy · frameOrigin · slotsFile  │
                       │ frameStorePolicy.ts (신규) · bundleManifest │
                       └───────────────────────────────────────────┘
```

**규칙**: 판정은 도메인, 브라우저 API는 어댑터, 순서·수명은 screens, 렌더만 ui. `console.*` 금지·`logger.*`만. 어댑터는 예외를 전파하지 않는다.

---

## 3. 로딩 4국면 상태 머신

### 3.1 국면 ↔ 화면 매핑 (03 §4.1 그대로)

| 국면 | 대기 오버레이 | 실패 카드 | 인라인 안내 | 목록 | [다음]·[만들기]·[선택 편집]·삭제 ✕ |
|------|:---:|:---:|:---:|:---:|:---:|
| `Loading` | **표시**(스피너 + 진행 문구 + [기다리지 않고 시작]) | – | – | scrim 아래 | **차단** |
| `Ready` | – | – | – | 표시 | 권한대로 |
| `Degraded` | – | – | **표시** + [다시 시도] | 표시 | 권한대로 |
| `Failed` | – | **표시** + [다시 시도]/[메인으로] | (카드 문구로 대체) | scrim 아래(비어 있음) | **차단** |

### 3.2 차단은 2중이다 (scrim + 상태 가드)

```ts
// domain/frames/frameLoadPolicy.ts 에 추가하는 유일한 함수
export function isFrameListInteractive(phase: FrameLoadPhase): boolean {
  return phase === "Ready" || phase === "Degraded";
}
```

- **① 렌더 가드**: `Loading`·`Failed`에서 오버레이/카드가 `position:absolute; inset:0`의 scrim으로 그리드를 덮는다. 카드·버튼은 `pointer-events`가 scrim에 막히고, [다음]·[만들기]·[선택 편집]은 `disabled`가 된다.
- **② 액션 가드**: 각 액션 **함수 첫 줄**에서 `if (!isFrameListInteractive(phase)) return;`. Windows `FrameSelectViewModel`의 `if (!IsInteractive) return;` 4곳과 1:1이다.

> ①만 두면 키보드 포커스·자동화·경쟁 상태로 우회된다. ②만 두면 손님이 눌러도 아무 일이 없어 고장으로 보인다. **둘 다** 둔다(M10과 같은 정신).

### 3.3 왜 `isFrameListInteractive`를 도메인에 두는가

Windows는 이것을 ViewModel 속성으로 뒀다(`IsInteractive`). 웹에서 화면 파일에 두면 **F-16 때문에 영원히 테스트되지 않는다**. 이 함수는 국면만 받는 순수 술어이고, `frameLoadPolicy.ts`에는 이미 Windows에 없는 웹 전용 항목(`DEFAULT_FRAME_LOAD_PHASE`·ms 파생 상수)이 들어 있다 — 같은 자리에 둔다.

⚠️ **기존 export·판정은 한 줄도 바꾸지 않는다.** `docs/spec-vectors/frame-load-policy.json`의 기대값은 불변이다(추가 함수는 벡터 대상이 아니다).

### 3.4 `Loading` 고착 불가능성 (구조적 보장)

`finalizeFrameLoad`는 **어떤 입력에서도 `Loading`을 반환하지 않는다**(Step 8.5가 32조합 전수로 고정). 화면의 로딩 루틴은 그 함수를 `finally`에서 **무조건** 부른다. 따라서 `Loading`으로 남을 수 있는 경로는 "이 로딩이 이미 stale"(화면 이탈·재시도 연타) 하나뿐이고, 그때는 화면이 이미 바뀌었거나 새 로딩이 국면을 다시 소유한다.

| 경로 | 결과 |
|------|------|
| 정상 완료 | `finally` → `Ready` |
| 상한 초과 / [기다리지 않고 시작] → 로컬 폴백 성공 | `finally` → `Degraded` |
| 상한 초과 → **로컬 폴백까지 실패** | `safeLocalOnly`가 빈 목록으로 축퇴 → `finally` → `Failed` |
| 목록 반영 도중 예외 | `completed=false` → `finally` → `Degraded` 또는 `Failed` |
| 화면 이탈 취소(stale) | `finally`가 아무것도 쓰지 않는다(폐기된 화면) |
| 상한 타이머 자체가 예외 | `try/catch`로 삼키고 `finally`가 확정 |

---

## 4. 단일 비행 + 진행 replay (`adapters/frames/frameCatalog.ts`)

### 4.1 계약

```ts
export interface FrameCatalogLoadOptions {
  /** 이 호출자만 취소한다. 공유 작업은 계속 진행해 캐시를 완성한다. */
  readonly signal?: AbortSignal;
  /** 진행 보고. 합류 즉시 최근 보고가 **동기 1회** replay된다. */
  readonly onProgress?: (progress: FrameCatalogProgress) => void;
}

export interface UnavailableFrame {
  readonly id: string;
  readonly name: string;
  /** 원격 URL — 썸네일만 `<img>`로 보여준다(06 §6). */
  readonly imageUrl: string;
}

export interface FrameCatalogResult {
  /** **선택 가능한** 프레임만(=`hasUsableImage` 통과). */
  readonly frames: readonly FrameTemplate[];
  /** 서버 목록에는 있으나 이미지를 가져오지 못한 것(카드는 보이되 선택 불가). */
  readonly unavailable: readonly UnavailableFrame[];
  readonly source: CatalogSource;
}

export interface FrameCatalog {
  /** 공용 프레임. 동시 호출은 **한 작업을 공유**한다. */
  loadPublic(options?: FrameCatalogLoadOptions): Promise<FrameCatalogResult>;
  /** 네트워크를 쓰지 않는 로컬 해석(캐시 → 번들 → fallback). **단일 비행에 합류하지 않는다.** */
  loadLocalOnly(): Promise<FrameCatalogResult>;
  /** 개인 로컬 프레임. **서버를 조회하지 않는다**(아래 4.5). */
  loadPersonal(userId: string): Promise<readonly FrameTemplate[]>;
}
```

### 4.2 단일 비행 구현 골격 (JS 고유 함정 2개 포함)

```ts
let inFlight: Promise<FrameCatalogResult> | null = null;
const observers = new Set<(p: FrameCatalogProgress) => void>();
let lastProgress: FrameCatalogProgress = { phase: "ResolvingLocal" };

function loadPublic(options: FrameCatalogLoadOptions = {}): Promise<FrameCatalogResult> {
  const { signal, onProgress } = options;

  // ⚠️ 새 패스를 시작하는 호출자에게 이전 패스의 마지막 국면(Completed = "정리하는 중…")을
  //    replay하면 안 된다 — 홈 왕복 후 재진입마다 첫 문구가 거짓이 된다(Windows :70-74).
  if (inFlight === null) lastProgress = { phase: "ResolvingLocal" };
  const snapshot = lastProgress;

  if (onProgress !== undefined) observers.add(onProgress);

  if (inFlight === null) {
    const task = runSharedPass();                 // ⚠️ 절대 reject하지 않는다
    inFlight = task;
    // ⚠️ 함정 A: `finally`를 task 내부에 두면, loadCore가 첫 await 이전에 동기 throw할 때
    //    `inFlight = task` 대입보다 정리가 **먼저** 일어나 해결된 promise가 영구히 남는다.
    //    바깥에서 붙이고 **동일성으로 가드**한다.
    void task.finally(() => { if (inFlight === task) inFlight = null; });
  }
  const shared = inFlight;

  onProgress?.(snapshot);                          // 문구 공백 구간 제거(합류 즉시 표시)
  return awaitShared(shared, onProgress, signal);
}

async function awaitShared(shared, onProgress, signal): Promise<FrameCatalogResult> {
  try {
    if (signal === undefined) return await shared;
    if (signal.aborted) throw new FrameLoadCancelledError();
    return await raceAbort(shared, signal);        // 공유 작업은 계속 진행한다
  } finally {
    // ⚠️ 구독 제거 경로는 이 finally **한 곳**이다(취소·예외·정상 완료 모두 통과) → 누적되지 않는다.
    if (onProgress !== undefined) observers.delete(onProgress);
  }
}
```

- **`raceAbort`**: `Promise.race([shared, abortPromise])`. `abortPromise`는 `signal.addEventListener("abort", …, { once: true })`로 만들고 **어느 쪽이 이기든 리스너를 제거**한다(함정 B — race의 패자는 영원히 pending이므로 리스너가 남는다).
- **`runSharedPass`는 절대 reject하지 않는다.** 내부에서 전부 catch해 빈 결과로 축퇴한다 → race의 패자가 unhandled rejection을 만들지 않는다.
- 취소 예외는 전용 `FrameLoadCancelledError`다. 상위(`frameLoadRunner`)가 **취소와 그 밖의 실패를 구분하지 않는다** — 둘 다 `waitInterrupted=true`로 같은 갈래다. 구분은 로그 문구에만 쓴다.

### 4.3 공유 작업 본체 (`loadCore`)

```
report(ResolvingLocal)
  local = frameStore.listPublic()                       ← IndexedDB 메타 + OPFS 파일 확인
try {
  report(QueryingServer)
  server = frameRepository.getDefaultFrames()           ← 게이트 키만(게스트 조회 가능)
  pending = serverFramesToCache(localNames, server)     ← 도메인(이름 dedup)
  for i in pending:
      report(DownloadingImage, i+1, pending.length)
      cached = cacheOne(pending[i])                     ← CORS-clean 다운로드 → OPFS + IDB
      cached ? local += cached : unavailable += pending[i]
} catch (err) {
  // ⚠️ 서버 조회·다운로드 실패는 **여기서 삼킨다** → waitInterrupted=false → Ready(E20)
  logger.warn("기본 프레임 서버 조회 실패 — 로컬/번들/fallback로 폴백(오프라인 모드)")
}
report(Completed)
return resolveLocal(local, unavailable)
```

**오프라인이 `Degraded`가 아닌 이유**가 이 catch다. `classifyFrameLoad`의 `waitInterrupted`가 "즉시 실패(오프라인)"와 "잘라낸 대기(상한 초과)"를 가르는 유일한 축이며, 즉시 실패는 어댑터 안에서 소멸해 화면에 도달하지 않는다. **이 catch를 지우거나 rethrow로 바꾸면 오프라인 부스가 매 진입마다 안내를 띄운다(E20 회귀).**

`resolveLocal(local, unavailable)`:

```ts
buildCatalog({                       // 도메인 — 4단 우선순위 + 이름 dedup
  localCache: local,                 // ① OPFS 캐시(서버 캐시 + power 저장분)
  server: [],                        //   ②는 이미 ①에 병합됐다
  bundle: await bundleFrames(),      // ③ 번들 자산(매니페스트)
  fallback: await fallbackTemplate(),// ④ 코드 생성
})
→ frames.filter(hasUsableImage)      // 이미지 없는 프레임은 목록에 올리지 않는다(Step 8.5)
```

### 4.4 `loadLocalOnly`가 단일 비행에 합류하지 않는 이유

방금 상한을 넘긴 그 작업을 다시 기다리면 상한이 무의미해진다(Windows it20 §7.2와 동일 판정). `loadLocalOnly`는 `frameStore.listPublic()` + 번들 + fallback만 쓰고 **백엔드를 호출하지 않는다**. 번들 매니페스트 fetch는 same-origin 정적 자산이므로 허용하되 **3초 타임아웃**을 건다.

### 4.5 개인 프레임은 로컬만 읽는다 (`loadPersonal`)

`frameRepository.getUserFrames`를 **부르지 않는다**. 이유 2가지:

1. 정책상 개인 커스텀 프레임은 서버에 올라가지 않는다(`analysis/41 §3`, `analysis/31 §4.11` 주석 — 보통 빈 배열).
2. **F-10**: 그 호출은 `auth:"required"`라 토큰 만료 시 401 → `handleSessionExpired` → **프레임 목록을 여는 것만으로 로그아웃 토스트**가 뜬다. 얻는 것이 빈 배열인데 잃는 것이 세션이다.

`loadPersonal = frameStore.listPersonal(userId)`(IndexedDB `by_owner` 인덱스).

### 4.6 진행 보고 → 문구

문구 조립은 **도메인**이 한다: `catalogProgressLabel({phase, index, total})`. 화면은 문자열을 만들지 않는다. 보고 전 초기 문구는 `CATALOG_START_LABEL`.

| 단계 | 보고 시점 | 문구(도메인이 만든다) |
|------|-----------|------|
| `ResolvingLocal` | 캐시 스캔 시작 | 설치된 프레임을 확인하는 중… |
| `QueryingServer` | `GET /frames/default` 발신 | 서버에서 기본 프레임 목록을 확인하는 중… |
| `DownloadingImage` | **각 이미지마다** | 기본 프레임 내려받는 중… (n/m) |
| `Completed` | 마지막 | 프레임 목록을 정리하는 중… |

**분모 `m`은 `pending.length`다**(로컬 캐시 히트를 뺀 수). 캐시가 다 차 있으면 `pending`이 비어 `DownloadingImage` 보고 자체가 없다 — 정직한 카운터.

### 4.7 부트스트랩 prefetch

`main.tsx`의 `startApp` **끝**에서 fire-and-forget으로 `getFrameCatalog().loadPublic()`를 1회 호출한다(진행 구독 없음, 결과 폐기, 실패 무시).

- 첫 렌더 **뒤**에 시작하므로 `01 §4.2`의 부트스트랩 1~11단계 순서를 바꾸지 않는다(`bootstrap()` 안에 넣지 않는다).
- 손님이 [촬영하기]를 누를 때쯤이면 이미 다운로드 중이고, 화면 진입은 **줄 서지 않고 합류**해 `(n/m)`을 즉시 본다 — 단일 비행이 실제로 값을 내는 유일한 경로다.
- ⚠️ `void`로 삼키되 `.catch`를 붙여 unhandled rejection을 만들지 않는다(`loadPublic`은 reject하지 않지만 방어).

---

## 5. 상한 타이머 — 실경과 기준 (`screens/frameSelect/frameLoadDeadline.ts`)

```ts
export interface LoadDeadline {
  /** 진행이 관측됐다 — 무진행 창을 재무장한다. 총 상한이 이미 소진됐으면 즉시 취소. */
  arm(): void;
  dispose(): void;
}

export function createLoadDeadline(deps: {
  now(): number;                                  // performance.now 주입(테스트 결정성)
  abort(): void;
  setTimer(fn: () => void, ms: number): unknown;
  clearTimer(handle: unknown): void;
}): LoadDeadline
```

동작:

1. 생성 시 `startedAt = now()`.
2. `arm()`: 기존 타이머 해제 → `due = nextFrameLoadDeadlineMs(now() - startedAt)`(도메인) → `due <= 0`이면 **즉시 `abort()`**, 아니면 `due` 후 발화 예약.
3. 발화 시: **경과를 다시 재서** 판정하고 `abort()`.
4. `dispose()`: 타이머 해제(멱등).

**`setTimeout` 누적을 세지 않는다.** 매 `arm()`이 `now() - startedAt`이라는 실경과로 다음 만기를 계산하므로, 탭 스로틀로 타이머가 늘어져도 총 상한이 부풀지 않는다(늘어짐은 대기를 **연장**할 뿐이며, 그 바깥 안전망은 실경과 기반 유휴 감시다 — F-17).

`visibilitychange` 구독은 **두지 않는다**. 근거: ① 스로틀은 대기를 늘릴 뿐 짧게 만들지 않는다 ② 복귀 시 유휴 감시가 이미 실경과로 재판정한다 ③ 화면 로컬 리스너를 늘리면 해제 누락 위험만 커진다.

---

## 6. 화면 로딩 루틴 (`screens/frameSelect/frameLoadRunner.ts`)

Windows `FrameSelectViewModel.ReloadFramesAsync`의 웹 대응이다. **React를 import하지 않는다** → node에서 통째로 검증된다(`runResultNext`·`runUpload` 선례).

```ts
export type FrameLoadReason = "enter" | "retry" | "refresh";

export interface FrameSelectPatch {          // 부분 갱신(적용은 호출자 몫)
  readonly phase?: FrameLoadPhase;
  readonly loadingMessage?: string;
  readonly notice?: string;
  readonly frames?: readonly FrameTemplate[];
  readonly unavailable?: readonly UnavailableFrame[];
  readonly selectedId?: string | null;
}

export interface FrameLoadDeps {
  loadPublic(o: FrameCatalogLoadOptions): Promise<FrameCatalogResult>;
  loadLocalOnly(): Promise<FrameCatalogResult>;
  loadPersonal(userId: string): Promise<readonly FrameTemplate[]>;
  currentUserId(): string | null;
  /** 이 로딩 시작 시점의 국면 — `finalizeFrameLoad(current, …)`의 첫 인자. */
  initialPhase(): FrameLoadPhase;
  /** 이 로딩이 아직 최신인가(화면 이탈·재시도 연타 판정). */
  isStale(): boolean;
  apply(patch: FrameSelectPatch): void;
  createDeadline(abort: () => void): LoadDeadline;
}

export async function runFrameLoad(deps: FrameLoadDeps, reason: FrameLoadReason): Promise<void>;
```

### 6.1 본체 (순서가 규격)

```ts
const quiet = reason === "refresh";
let phase = deps.initialPhase();              // 지역 사본 — apply가 비동기여도 finally가 옳게 읽는다
let frames: readonly FrameTemplate[] = [];
let interrupted = false;
let completed = false;

if (!quiet) {
  phase = "Loading";
  deps.apply({ phase, loadingMessage: CATALOG_START_LABEL, notice: "" });
}

const controller = new AbortController();
const deadline = deps.createDeadline(() => controller.abort());
try {
  deadline.arm();                              // quiet에서도 상한은 동일하게 건다(무한 대기 금지는 계기 무관)

  const onProgress = quiet ? undefined : (p: FrameCatalogProgress) => {
    if (deps.isStale()) return;                // 늦은 보고가 새 로딩 문구를 덮지 않게
    deps.apply({ loadingMessage: catalogProgressLabel(p) });
    deadline.arm();                            // 진행 관측 → 무진행 타이머 재무장
  };

  let result: FrameCatalogResult;
  try {
    result = await deps.loadPublic({ signal: controller.signal, onProgress });
  } catch (err) {
    if (deps.isStale()) return;                // 화면 이탈 취소 → finally도 아무것도 하지 않는다
    interrupted = true;
    logger.warn("기본 프레임 대기 중단 — 로컬 전용 폴백", {
      reason: err instanceof FrameLoadCancelledError ? "cancelled" : describe(err),
      noProgressSec: NO_PROGRESS_TIMEOUT_SECONDS, totalSec: MAX_TOTAL_WAIT_SECONDS,
    });
    result = await safeLocalOnly(deps);        // 여기까지 실패하면 빈 결과 → Failed가 실제로 도달 가능
  }
  if (deps.isStale()) return;

  const merged = [...result.frames];
  const userId = deps.currentUserId();
  if (userId !== null) {
    // 개인 프레임 실패가 공용 목록을 무너뜨리지 않게 개별 방어(Windows :207-218과 동형)
    try { merged.push(...await deps.loadPersonal(userId)); }
    catch (err) { logger.warn("개인 프레임 로드 실패(공용 목록은 유지)", { reason: describe(err) }); }
  }
  if (deps.isStale()) return;

  // 목록을 **미리 비우지 않는다.** 마지막에 한 번 교체한다 —
  // 선행 비우기는 quiet 재스캔에서 "빈 목록 + 조작 열림"을 노출하고 Enter에서도 깜빡인다.
  frames = merged.filter(hasUsableImage);
  deps.apply({ frames, unavailable: result.unavailable, selectedId: frames[0]?.id ?? null });
  completed = true;
} finally {
  deadline.dispose();
  if (!deps.isStale()) {
    const next = finalizeFrameLoad(phase, frames.length, interrupted || !completed, quiet);
    deps.apply({ phase: next, notice: frameLoadNotice(next) });
  }
}
```

⚠️ **`frames`의 초기값은 빈 배열이다.** `quiet` 경로에서 로딩이 중단되면 `finalizeFrameLoad(current, 0, …)`가 `Failed`를 내는데, 이는 "재스캔이 실패했으니 목록도 없다"는 뜻이 아니라 **화면에 이미 떠 있는 목록과 어긋난다**. 그래서 `refresh`에서는 `deps` 대신 호출자가 **현재 목록 길이를 초기값으로 주입**한다:

```ts
// FrameLoadDeps에 추가
/** 로딩 시작 시점의 목록 길이. quiet 재스캔이 중단됐을 때 `finalize`의 근거가 된다. */
initialFrameCount(): number;
```
→ `let frameCount = deps.initialFrameCount();` 로 시작하고, 목록 교체 성공 시에만 `frameCount = frames.length`로 갱신한다. `finalizeFrameLoad(phase, frameCount, …)`.

### 6.2 [기다리지 않고 시작] · [다시 시도] · 이탈

| 조작 | 동작 |
|------|------|
| **[기다리지 않고 시작]** | `if (phase !== "Loading") return;` 후 **현재 로딩의 `controller.abort()`만** 부른다. 새 로딩을 시작하지 않는다. 공유 작업은 계속 진행해 캐시를 완성하므로 잠시 뒤 [다시 시도]가 성공할 가능성이 높다 |
| **[다시 시도]** | `runFrameLoad(deps, "retry")` — 상한을 새로 부여하고 오버레이를 다시 연다 |
| **화면 이탈(언마운트)** | ① 세대 카운터 증가(→ `isStale()`가 즉시 true) ② `controller.abort()`. **순서가 중요하다** — 반대로 하면 취소 예외가 stale이 아닌 상태에서 잡혀 국면을 덮어쓴다 |
| **삭제 직후** | `runFrameLoad(deps, "refresh")` — 오버레이·진행 문구 없음(`quiet`) |

`<StrictMode>` 이중 effect는 이 구조에서 **무해하다**: 1회차 cleanup이 자기 자신만 stale로 만들고 공유 작업은 살아 있으므로, 2회차는 같은 작업에 합류해 replay를 받는다(중복 다운로드 0). ⚠️ 취소가 공유 작업까지 죽이도록 바꾸면 개발 모드에서 매번 취소→재시작이 일어난다 — **호출자별 취소를 유지하라.**

---

## 7. 프레임 저장소 (`adapters/storage/frameStore.ts`)

### 7.1 IndexedDB는 **별 DB**를 쓴다 — `mcphoto-frames` v1

`05 §4.2`는 DB `mcphoto` v1의 store `frames`를 명시하지만, **현실이 다르다**: `mcphoto` v1은 로그 스토어가 이미 점유했고(F-6) 그 연결에 `onversionchange`가 없다. 여기서 v2로 올리면 업그레이드가 **영구 blocked** 되어 프레임 로딩이 응답하지 않는다(그리고 상한 타이머가 30초 뒤 `Degraded`를 띄운다 — 원인을 알 수 없는 만성 결함이 된다).

**결정**: `DIR_HANDLE_DB_NAME`(Step 10) 선례를 그대로 따른다.

```ts
export const FRAME_DB_NAME = "mcphoto-frames";
export const FRAME_DB_VERSION = 1;
export const FRAME_STORE_NAME = "frames";
```

- 연결은 **트랜잭션 1회마다 열고 닫는다**(`dirHandleRepo.withHandleStore` 패턴). 연결을 붙들지 않으므로 다른 탭의 업그레이드를 막지 않는다.
- `request.onsuccess`에서 `db.onversionchange = () => db.close()`를 건다.
- `logStore.ts:169`의 낡은 주석(F-7)을 **이 결정으로 정정**한다. `logStore`의 동작은 건드리지 않는다 — `mcphoto`를 업그레이드할 계획이 없어졌으므로 `onversionchange` 추가가 불필요하고, 추가하면 다른 탭 업그레이드 시 로그가 조용히 죽는 새 실패 모드가 생긴다.
- `05 §4.2` 문서를 함께 정정한다(Step 14-7).

### 7.2 스키마 (05 §4.2 — DB 이름만 정정)

```jsonc
// DB "mcphoto-frames" v1 / store "frames" (keyPath: "key")
{
  "key": "user:devmcjo:내프레임",       // scope:owner:name — 유일 키
  "scope": "user",                      // "public" | "user"
  "ownerId": "devmcjo",                 // scope=user일 때만
  "name": "내프레임",                    // 원문 그대로(정규화 금지)
  "id": "local:user:devmcjo:내프레임",  // 출처 판정 근거(05 §4.4)
  "dbId": null,                         // 서버 문서 id(공용 캐시·power 등록분만)
  "imageFile": "frames/9f1c….png",      // OPFS 상대 경로
  "imageSize": { "width": 1200, "height": 1600 },
  "slots": [ { "index":0, "x":80, "y":140, "width":480, "height":640 } ],
  "createdAt": "2026-07-30T05:11:00.000Z",
  "updatedAt": "2026-07-30T05:11:00.000Z"
}
// 인덱스: by_scope(scope) · by_owner(ownerId) · by_name(name)
```

공용 프레임의 key는 `public:{name}`이다(스코프 안에서 이름 유일 — 이름 dedup의 저장소 표현).

### 7.3 메타 계층을 인터페이스로 분리한다 (node 테스트 가능성)

node에 IndexedDB가 없다(F-16). `LogSink`가 푼 방식을 그대로 쓴다.

```ts
export interface FrameMetaStore {
  all(): Promise<FrameRecord[]>;
  put(record: FrameRecord): Promise<boolean>;
  delete(key: string): Promise<boolean>;
}
export function createIndexedDbFrameMeta(): FrameMetaStore;   // 얇다(브라우저 전용)
export function createMemoryFrameMeta(seed?): FrameMetaStore; // 테스트·미지원 폴백
```

`createFrameStore({ meta, opfs, newToken, now })`가 실제 로직을 갖고, 그 로직 전부가 node에서 검증된다. IndexedDB가 없는 환경에서는 메모리 메타로 축소해 **앱이 죽지 않는다**(프레임은 세션 동안만 유지된다 — 축소 동작).

### 7.4 API

```ts
export interface FrameStore {
  /** 공용 캐시(번들 제외). 이미지 파일이 실제로 없는 레코드는 **건너뛴다**(반쪽 프레임 미노출). */
  listPublic(): Promise<FrameTemplate[]>;
  listPersonal(userId: string): Promise<FrameTemplate[]>;
  /** 서버 프레임을 캐시한다. OPFS 쓰기 → 메타 기록 순서. 실패는 `null`. */
  cacheServerFrame(frame: FrameTemplate, bytes: Blob): Promise<FrameTemplate | null>;
  /** Step 15가 쓰는 저장 경로(지금은 구현만 두고 호출자 없음). */
  saveLocal(input: SaveFrameInput): Promise<FrameTemplate | null>;
  /** 05 §4.7. 성공 판정은 **실제 부재 확인**이다. */
  deleteLocal(frame: FrameTemplate): Promise<boolean>;
  /** 개인 프레임 개수(10개 상한 판정 — Step 15가 쓴다). */
  countPersonal(userId: string): Promise<number>;
  /** `frames/` OPFS 사용량(Step 16 진단). `OpfsClient.usage`를 그대로 쓴다. */
  usageBytes(): Promise<number>;
}
```

**쓰기 순서가 규격이다**: `opfs.write(imageFile, bytes)`가 성공한 **뒤에** 메타를 기록한다. 반대로 하면 이미지 없는 레코드가 목록에 올라간다. 이는 Windows가 "png 먼저, `.slots` 나중"으로 얻은 성질과 같다.

**삭제 절차**(05 §4.7):

```
1. exists(imageFile) 확인
     없음 → 메타 레코드를 지우고 **true**를 돌려준다(아래 이탈 ④)
2. 메타 레코드 삭제
3. opfs.remove(imageFile)
4. 성공 판정 = exists(imageFile) === false  ← 예외를 삼키고 성공으로 보고하지 않는다(M4)
```

**모든 OPFS 쓰기·삭제는 `OpfsClient`(= `opfsWriter` Worker)를 지난다**(VF-14). `frameStore.ts`에 `navigator.storage`·`createWritable`·`createSyncAccessHandle`·`getDirectory(` 가 0건이어야 하고, 정적 검사 **FR-1**이 이를 고정한다.

---

## 8. 프레임 이미지 — 로드·캐시·수명 (WM2)

### 8.1 다운로드 (`adapters/frames/frameDownloader.ts`)

```ts
/** CORS-clean 다운로드. 실패·비200·빈 본문은 전부 `null`(예외 미전파). */
export async function downloadFrameImage(url: string): Promise<Blob | null> {
  // ⚠️ `mode: "cors"`를 생략하면 헤더가 있어도 canvas가 오염된다 — 절대 빼지 마라(WM2).
  const res = await fetch(url, { mode: "cors", credentials: "omit", cache: "force-cache" });
  …
}
```

- 게이트 키·Bearer를 **붙이지 않는다**(Storage 다운로드 토큰 URL은 인증 불요 — `analysis/31 §4.10`).
- 이미지 없는 문서가 존재할 수 있다(`analysis/31 §4.10` 마지막 항목) → 404를 **정상 경로**로 처리해 `null`.
- 15초 타임아웃(`AbortController`) — 한 장이 영원히 매달려 무진행 30초를 태우지 않게.

### 8.2 `imageUrl`에 무엇을 담는가 (핵심 판단)

| 출처 | `imageUrl` |
|------|-----------|
| OPFS 캐시(서버 캐시 · power 저장분 · 개인 프레임) | **object URL**(`URL.createObjectURL(File)`) — `frameImageCache`가 소유 |
| 번들 자산 | `/frames/{name}.png` (same-origin 정적 경로) |
| fallback | 기존 `ensureFallbackImageUrl()`의 object URL |
| **다운로드 실패한 서버 프레임** | 원격 https URL — `unavailable` 목록에만 들어가고 **선택 불가** |

`URL.createObjectURL(File)`은 OPFS 파일 핸들이 준 **디스크 백업 File**을 가리키므로 바이트를 메모리에 올리지 않는다(A-2). 그래서 20장을 동시에 들고 있어도 비용이 낮다.

**수명 소유자는 `frameImageCache`다**(모듈 스코프 `Map<opfsPath, string>`):

```ts
export function frameImageUrl(path: string, file: File): string;  // 캐시 히트면 재사용
export function revokeFrameImage(path: string): void;             // 삭제 시
export function revokeAllFrameImages(): void;                     // 테스트·리셋
```

- **화면 이탈에서 revoke하지 않는다.** 선택된 프레임의 URL은 `sessionStore.session.frame`을 타고 `Result`의 합성까지 살아 있어야 한다 — 여기서 해제하면 합성이 실패한다. (`fallbackFrame.ts`가 이미 같은 판단을 하고 있다.)
- 해제 시점은 **프레임 삭제**뿐이다. 항목 수가 프레임 개수(≤ 수십)로 유계이므로 누수가 아니다.

### 8.3 합성 경로 보정 (`adapters/compose/compositor.ts` — 최소 변경)

현행은 URL 종류를 가리지 않고 `{ mode: "cors", cache: "force-cache" }`를 준다(F-9). `blob:` 스킴에 이 옵션을 주는 것은 의미가 없고 브라우저별 동작이 불확실하다(A-1).

```ts
// 원격만 CORS 규약을 적용한다. blob:·상대 경로는 same-origin이라 옵션이 무의미하다.
const remote = /^https?:/i.test(url);
const response = await fetch(url, remote ? { mode: "cors", cache: "force-cache" } : {});
```

⚠️ **https 분기에서 `mode:"cors"`를 없애면 WM2가 깨진다.** 정적 검사 **FR-6**이 `compositor.ts` 소스에 `mode: "cors"` 문자열이 남아 있음을 고정하고, 단위 테스트가 https URL에 대해 `mode:"cors"`가 전달되는지 가짜 fetch로 확인한다.

### 8.4 썸네일 (`adapters/frames/frameThumbnails.ts` · 04 §5.2)

```ts
export const FRAME_THUMB_WIDTH = 240;
/** 실패·미지원은 `null`(카드가 이름만 보여준다). 예외를 던지지 않는다. */
export async function createFrameThumbnail(blob: Blob, targetWidth?: number): Promise<ImageBitmap | null>;
export function resetThumbnailProbeForTests(): void;
```

- 1순위: `createImageBitmap(blob, { resizeWidth, resizeHeight, resizeQuality: "high" })`.
- ⚠️ **resize 옵션은 미지원 시 예외가 아니라 조용히 무시된다.** 결과 `bitmap.width === resizeWidth`인지 **1회 확인해 판정을 모듈에 캐시**한다(04 §5.2 규격). 미실효면 그 비트맵을 닫고 폴백으로 간다.
- 폴백: 전체 디코드 → `OffscreenCanvas` 축소(`imageSmoothingQuality="high"`) → `transferToImageBitmap()`. **중간 비트맵은 반드시 `close()`.**
- 카드 컴포넌트(`FrameThumb`)가 effect에서 만들고 **cleanup에서 `close()`** 한다(`CutThumbnail` 선례). 전역 썸네일 캐시는 두지 않는다 — 언마운트로 확실히 회수되는 편이 안전하다.

### 8.5 번들 프레임 (`adapters/frames/bundleFrames.ts`)

브라우저는 정적 디렉터리를 열거할 수 없다(Windows `Directory.EnumerateFiles`의 대응물이 없다). **매니페스트**를 규약으로 둔다.

```jsonc
// webclient/public/frames/index.json
[
  { "name": "베이직 4컷", "image": "basic4.png", "slots": "basic4.slots",
    "width": 1200, "height": 1600 }
]
```

- 파싱은 도메인 순수 함수 `parseBundleManifest(raw: unknown): BundleFrameEntry[]` — 형식 위반 항목은 **건너뛰고 계속**(예외 금지, `slotsFile` 관례와 동형).
- `slots`가 없거나 파싱 결과가 0개면 `autoArrange(4, width, height, 3/4)`로 **2×2 자동 생성**(analysis/13 §5 ③).
- 매니페스트 404·JSON 오류 → **번들 0개**(경고 로그만). 3초 타임아웃.
- id = `bundle:{name}`, `isDefault: true`, `userId: null`.
- **이번 Step에서 자산을 커밋하지 않는다**(F-11 · VF-10). `public/frames/index.json`에 빈 배열 `[]`만 두어 경로 규약을 고정하고, 실제 PNG는 운영 자산 준비 시 추가한다.

---

## 9. 삭제 흐름 (03 §15.5 · 05 §4.7)

### 9.1 확인 UI는 **화면 로컬 오버레이**다 (공용 모달 미사용)

`03 §790`이 오버레이 소유를 못박는다: *"`FrameSelect` = 프레임 준비 대기 · 프레임 준비 실패 · 삭제 확인"*. 그리고 Step 15가 `src/screens/modals/confirmDelete/*`를 소유한다(WBS Step 15 대상 파일).

**결정**: 삭제 확인은 `FrameSelectView` 내부의 오버레이 컴포넌트로 만들고 **`shellStore.pushModal("confirmDelete")`를 부르지 않는다.** Step 13이 [보관된 결과물] 전체 삭제를 인라인 2단 확인으로 처리한 것과 같은 판단이다. 정적 검사 **FR-5**가 `FrameSelectView.tsx`에 `pushModal(`·`"confirmDelete"`가 0건임을 고정한다(선점 방지).

같은 화면의 오버레이 3종은 **상호배타**다: 삭제 오버레이는 `isFrameListInteractive(phase)`일 때만 열리고, `refresh` 로딩을 시작하기 **전에** 닫힌다.

### 9.2 순서 (Windows `ConfirmDelete`와 1:1)

```
0. 게이트: isFrameListInteractive(phase) && canDeleteFrame(frame, role) && isDeletableOrigin(frame)
1. alsoServer = (checkbox && isPower)      ← ⚠️ 오버레이를 닫기 **전에** 지역 값으로 확정
2. localOk = await frameStore.deleteLocal(frame)   (+ revokeFrameImage)
3. 목록에서 제거 + 선택 이동 + 오버레이 닫기
4. alsoServer → 서버 삭제
     ① DELETE /frames/{serverId}          serverId = id에서 `local:` 접두 제거
     ② 응답 {deleted:false} → 이름 매칭 재시도(GET /frames/default → name 일치 → DELETE)
5. 결과 문구 확정(4종). localOk === false면 실패를 **덧붙인다**(성공 오인 금지)
6. runFrameLoad(reason="refresh")          ← 조용한 갱신
```

| 결과 | 문구(`STRINGS.frames`) |
|------|------|
| 로컬 삭제 실패 | 로컬 프레임 파일을 삭제하지 못했습니다(사용 중일 수 있음). |
| 서버 삭제 성공 | 서버에서도 삭제되었습니다. |
| 서버 문서 미발견 | 로컬은 삭제했지만 서버에서 '{n}' 문서를 찾지 못했습니다. |
| 서버 삭제 예외 | 서버 삭제 실패: {n} |

문구는 **인라인 안내 영역**(`role="alert"`)에 남긴다(토스트가 아니다 — Windows `DeleteNotice`와 동형, 4초 뒤 사라지면 안 되는 정보다).

### 9.3 삭제 판정에 `userId`를 넘기지 않는다

`canDeleteFrame(frame, role)`은 **2인자**다(F-2). power가 fork 저장한 *공용* 로컬 프레임은 `userId=null`로 로드되므로 소유자 판정을 넣으면 삭제 능력이 회귀한다. 타인의 개인 프레임은 `listPersonal(currentUserId)` 필터에서 이미 제외된다. 정적 검사 **FR-2**가 `canDeleteFrame(` 호출의 인자 수를 고정한다.

### 9.4 `frameRepository.deleteFrame` 계약 수정 (F-8)

```ts
/** 서버 응답 `{deleted:boolean}`을 그대로 돌려준다. **`false`는 성공이 아니다**(analysis/31 §4.14). */
deleteFrame(id: string): Promise<boolean>;
```

- 응답 파싱: `typeof raw?.deleted === "boolean" ? raw.deleted : false`(형태가 어긋나면 성공으로 오인하지 않는다).
- 예외(401/403/404/네트워크)는 **그대로 던진다** — HTTP 서비스의 기존 관례이고, 호출부(`frameSelectActions`)가 잡아 "서버 삭제 실패: {사유}"로 표현한다.

### 9.5 삭제 후 서버 재조회는 종전대로 유지한다

`refresh`도 `loadPublic`을 부르므로, 로컬만 지운 DB 유래 공용 프레임은 **재다운로드되어 카드가 돌아온다**. Windows가 명시적으로 보존한 동작이며(it20 §6.5 말미), 대기 UI 변경이 삭제 의미론을 조용히 바꾸지 않게 그대로 둔다. "서버에서도 제거"를 체크해야 영구 삭제된다.

---

## 10. 컴포넌트 트리 · props · 이벤트

```
<FrameSelectView>                       ← ui/views/FrameSelectView.tsx (신규 파일)
  state = useFrameSelect()              ← screens/frameSelect/useFrameSelect.ts (얇은 훅)
  │
  ├─ <h1>프레임 선택</h1>
  ├─ <div class=stage>                                   position: relative
  │   ├─ <FrameGrid
  │   │     frames  unavailable  selectedId  interactive
  │   │     canDelete: (f) => boolean
  │   │     onSelect(id)  onRequestDelete(frame) />
  │   │     └─ <FrameCard frame selected disabled canDelete onSelect onDelete>
  │   │            └─ <FrameThumb src={frame.imageUrl} />      canvas + ImageBitmap(cleanup close)
  │   │            └─ ✕ 버튼 (canDelete && interactive 일 때만 **렌더**)
  │   ├─ {phase==="Loading"  && <FrameLoadingOverlay message onSkip />}   scrim
  │   └─ {phase==="Failed"   && <FrameFailedCard notice onRetry onHome />} scrim
  │
  ├─ {phase==="Degraded" && <p role="alert">{notice}</p> + [다시 시도]}
  ├─ {deleteNotice && <p role="alert">{deleteNotice}</p>}
  ├─ <p>선택: {name} (슬롯 {n}개 · {aspect})</p>
  ├─ <div class=actions>
  │     [취소] [프레임 만들기]? [선택 편집]? [다음]
  └─ {deleteTarget && <FrameDeleteOverlay frame isPower alsoServer onToggle onConfirm onCancel />}
```

### props/이벤트 계약

| 컴포넌트 | props | 이벤트 |
|---|---|---|
| `FrameGrid` | `frames`, `unavailable`, `selectedId`, `interactive`, `canDelete(frame)` | `onSelect(id)`, `onRequestDelete(frame)` |
| `FrameCard` | `frame`, `selected`, `disabled`(=`!interactive`), `showDelete` | `onSelect`, `onDelete` |
| `UnavailableCard` | `entry` | 없음 — `aria-disabled`, 캡션 *"이 프레임을 불러올 수 없습니다."* |
| `FrameThumb` | `src`(URL), `alt=""` | 없음 |
| `FrameLoadingOverlay` | `message`(도메인 문구) | `onSkip` — [기다리지 않고 시작] |
| `FrameFailedCard` | `notice`(도메인 문구) | `onRetry`, `onHome` |
| `FrameDeleteOverlay` | `frame`, `isPower`, `alsoServer`, `busy` | `onToggleServer`, `onConfirm`, `onCancel` |

`useFrameSelect()`가 돌려주는 표면:

```ts
interface FrameSelectView {
  phase: FrameLoadPhase; loadingMessage: string; notice: string;
  frames: readonly FrameTemplate[]; unavailable: readonly UnavailableFrame[];
  selectedId: string | null; selected: FrameTemplate | null;
  interactive: boolean;                       // isFrameListInteractive(phase)
  canCreateFrame: boolean; canDeleteFrames: boolean; isPower: boolean;
  canEditSelected: boolean;
  deleteTarget: FrameTemplate | null; deleteAlsoServer: boolean; deleteNotice: string;
  select(id): void; retry(): void; skipWait(): void;
  requestDelete(frame): void; toggleDeleteServer(v): void; confirmDelete(): void; cancelDelete(): void;
  createFrame(): void; editSelected(): void; goNext(): void; cancel(): void;
}
```

⚠️ 훅에는 **판정을 넣지 않는다**(F-16). `useState` 보관 + 세대 카운터 + 위 모듈 호출만 한다.

### 접근성·반응형

- 그리드는 기존 `screens.module.css`의 `.frameGrid`(auto-fill)를 재사용해 임의 개수를 수용한다.
- 카드 최소 48×48, 선택 상태는 `aria-pressed`. ✕는 별 버튼(카드 안의 버튼 중첩 금지 — 카드를 `<div role="button">`이 아니라 **`<button>` + 형제 ✕ 버튼**으로 배치한다).
- 오버레이: `role="status"`(Loading) / `role="alert"`(Failed·Degraded), `aria-live` 문구는 진행 문구 하나만.
- 삭제 오버레이는 `role="dialog" aria-modal="true"` + 진입 시 [취소]에 포커스, `Esc` = 취소. **유휴 경고(셸 모달)는 언제나 이 위에 그려진다**(02 §6.2).

---

## 11. 파일별 역할과 시그니처

### 11.1 도메인 (순수 · node 테스트)

| 파일 | 내용 |
|------|------|
| `domain/frames/frameLoadPolicy.ts` **(수정)** | `isFrameListInteractive(phase)` **추가만**. 기존 export·판정 불변 |
| `domain/frames/frameStorePolicy.ts` **(신규)** | `frameStoreKey(scope, ownerId, name)` · `frameIdFor(scope, ownerId, name, dbId)` · `frameImagePath(token)` → `frames/{token}.png` · `isFrameRecord(v): v is FrameRecord`(경계 검증) · `recordToTemplate(record, imageUrl)` · `templateToRecord(...)` · `LOCAL_FRAME_LIMIT = 10` · `exceedsLocalFrameLimit(count)` |
| `domain/frames/bundleManifest.ts` **(신규)** | `parseBundleManifest(raw: unknown): BundleFrameEntry[]` — 손상 항목 건너뜀, 예외 없음 |

> 이름은 전부 **한정형**이다(F-3 배럴 충돌 방지). `domain/index.ts`에 두 파일을 추가 export한다.

### 11.2 어댑터 (브라우저 격리 · 예외 미전파)

| 파일 | 내용 |
|------|------|
| `adapters/storage/frameStore.ts` **(신규)** | §7.4 API + `FrameMetaStore` 인터페이스 + `createIndexedDbFrameMeta()`/`createMemoryFrameMeta()` + `getFrameStore()`/`setFrameStoreForTests()` |
| `adapters/frames/frameDownloader.ts` **(신규)** | `downloadFrameImage(url): Promise<Blob|null>` — CORS-clean · 15초 타임아웃 |
| `adapters/frames/frameImageCache.ts` **(신규)** | object URL 소유·재사용·해제 |
| `adapters/frames/frameThumbnails.ts` **(신규)** | resize 프로브 + 폴백 |
| `adapters/frames/bundleFrames.ts` **(신규)** | 매니페스트 fetch(3초) → `FrameTemplate[]` |
| `adapters/frames/frameCatalog.ts` **(신규)** | 단일 비행 + 진행 replay + 4단 조립. `getFrameCatalog()`/`setFrameCatalogForTests()`/`createFrameCatalog(deps)` |
| `adapters/frames/fallbackFrame.ts` **(기존, 무변경)** | 최종 폴백 |
| `adapters/http/frameRepository.ts` **(수정)** | `deleteFrame(id): Promise<boolean>` |
| `adapters/compose/compositor.ts` **(수정)** | 원격/로컬 fetch 분기(§8.3) |

### 11.3 화면 로직 (React 무관 · node 테스트)

| 파일 | 내용 |
|------|------|
| `screens/frameSelect/frameLoadDeadline.ts` | `createLoadDeadline(deps)` |
| `screens/frameSelect/frameLoadRunner.ts` | `runFrameLoad(deps, reason)` · `FrameSelectPatch` |
| `screens/frameSelect/frameSelectActions.ts` | `frameSelectPermissions(role)` · `canOpenDelete(...)` · `runFrameDelete(deps, input)` · `resolveNext(...)` 게이트 |
| `screens/frameSelect/useFrameSelect.ts` | 얇은 훅(상태·세대 카운터·배선) |

### 11.4 UI · 배선

| 파일 | 내용 |
|------|------|
| `ui/views/FrameSelectView.tsx` **(신규)** | §10 트리 |
| `ui/views/frameSelect.module.css` **(신규)** | scrim·카드 ✕·오버레이 |
| `ui/views/FlowViews.tsx` **(수정)** | 최소 `FrameSelectView` **삭제**(관련 import 정리) |
| `ui/strings.ts` **(수정)** | `frames.*`에 삭제 4문구 + 오버레이 라벨 추가 |
| `src/App.tsx` **(수정)** | `FrameSelectView`를 새 파일에서 import |
| `src/main.tsx` **(수정)** | prefetch 1줄(§4.7) |
| `webclient/public/frames/index.json` **(신규)** | `[]` |
| `adapters/storage/logStore.ts` **(주석만)** | F-7 낡은 주석 정정 |

---

## 12. 데이터 흐름 시나리오

### 12.1 첫 방문(캐시 0) · Slow 3G

```
main.tsx  prefetch → loadPublic()  … ResolvingLocal → QueryingServer → Downloading(1/3)
[촬영하기] → FrameSelect 진입
  useFrameSelect effect → runFrameLoad("enter")
    phase=Loading, "기본 프레임을 준비하고 있어요…"      ← 첫 페인트에 빈 목록이 없다
    loadPublic({signal,onProgress}) → 합류 → replay "기본 프레임 내려받는 중… (1/3)"
    보고마다 deadline.arm()                              ← 무진행 30초 재무장
  … (3/3) → Completed → frames 3개 → apply
  finally: finalizeFrameLoad("Loading", 3, false, false) = Ready
```

### 12.2 서버 무응답(진행 멎음)

```
Downloading(1/3)에서 30초 무진행 → deadline 발화 → controller.abort()
loadPublic이 FrameLoadCancelledError → interrupted=true
safeLocalOnly() → 캐시 1개(방금 받은 것) 또는 번들/fallback
finally: finalizeFrameLoad("Loading", 1, true, false) = Degraded
  인라인 안내 "서버 프레임을 모두 가져오지 못해 지금 준비된 프레임으로 진행합니다." + [다시 시도]
공유 작업은 계속 진행 → 잠시 뒤 [다시 시도]가 성공
```

### 12.3 오프라인 부스 (조용한 폴백 — E20)

```
loadCore: QueryingServer → fetch 즉시 실패 → **catch가 삼킨다**(warn 로그만)
Completed → 캐시 2개 반환 → interrupted=false
finally: finalizeFrameLoad("Loading", 2, false, false) = **Ready**  ← Degraded 아님
```

### 12.4 삭제 → 조용한 갱신

```
✕ → (interactive && canDeleteFrame) → 삭제 오버레이
[확인] → alsoServer 확정 → deleteLocal → 목록 제거·오버레이 닫기 → (서버 삭제) → 문구
runFrameLoad("refresh"):  quiet=true → phase 손대지 않음(오버레이 없음, 진행 문구 없음)
finally: finalizeFrameLoad("Ready", n, …, quiet=true)
   n>0 → "Ready" 유지 / n===0 → "Failed" / 종전이 Failed였고 n>0 → "Ready" 회복
```

### 12.5 [다음] (컷 수 해석 — VF-12 불변)

```
goNext():
  if (!interactive) return;            ← 국면 가드
  if (selected === null) return;
  fixFrameAndResolveCutCount(selected, settings.CutCount);   ← 유일한 해석 지점(기존 함수 그대로)
  shellStore.go("Guide");
```

`Guide`는 세션의 `cutCount`·`isAutoCutCount`를 읽기만 한다(재해석 금지 — it17).

---

## 13. Windows 구현과의 대응 관계

| Windows | 웹 | 차이 |
|---------|-----|------|
| `FrameCatalogService`(싱글턴 DI) | `adapters/frames/frameCatalog.ts`(모듈 싱글턴) | 동일 구조. `lock` → 단일 스레드라 불요, 대신 §4.2의 **함정 A/B**가 JS 고유 |
| `_inFlight` / `_observers` / `_lastProgress` | 동명 모듈 변수 | 새 패스에서 스냅샷을 시작 국면으로 되돌리는 처리까지 동일 |
| `IProgress<T>` + `Progress<T>`(UI 마샬링) | 콜백 `(p) => void` | 웹은 단일 스레드라 마샬링 불요. **stale 보고 차단**은 동일하게 필요 |
| `CancellationTokenSource` + `WaitAsync(ct)` | `AbortController` + `raceAbort` | 호출자별 취소·공유 작업 존속이라는 성질이 같다 |
| `CancelAfter` + `Stopwatch` | `createLoadDeadline`(주입 시계) | 웹은 시계를 주입해 node에서 상한을 직접 검증한다 |
| `ILocalFrameStore`(파일 시스템, `{계정}_` 접두) | `frameStore`(IndexedDB 메타 + OPFS, `scope`/`ownerId` 필드) | 05 §4.3이 허용한 대체. `.slots` 텍스트 포맷은 **내보내기/가져오기에서만** 유지(Step 16) |
| `EnsureFallbackFrame` + 파일 lock | `ensureFallbackImageUrl()`(모듈 캐시) | 단일 스레드라 쓰기 경합이 없다 |
| `LoadBundleFrames`(`Directory.EnumerateFiles`) | `bundleFrames`(매니페스트) | **웹은 디렉터리 열거가 불가** — 유일한 구조적 차이 |
| `FrameSelectViewModel.Phase`/`IsInteractive` | `useFrameSelect().phase` + `isFrameListInteractive`(도메인) | 판정을 도메인으로 내려 node 테스트 가능 |
| `ReloadFramesAsync(reason)` | `runFrameLoad(deps, reason)` | `finally`가 `finalizeFrameLoad`를 무조건 부르는 구조가 동일 |
| `DeleteFromServerAsync`(bool) | `frameSelectActions.runFrameDelete` + `deleteFrame(): Promise<boolean>` | **F-8을 고쳐야 대응이 성립한다** |
| `N8`(공유 인스턴스 별칭) | 동일 성질 | `FrameTemplate`이 **`readonly` 필드**라 웹은 제자리 변형이 타입으로 차단된다 — Windows보다 안전 |

---

## 14. 설계 이탈 (지시문·규격과 다른 6가지)

### 이탈 ① IndexedDB를 `mcphoto`가 아니라 **`mcphoto-frames`** 에 만든다
`05 §4.2`는 DB `mcphoto`를 적었지만 F-6/F-7이 그것을 불가능하게 만든다. Step 10이 같은 이유로 별 DB를 썼다. **문서(`05 §4.2`)와 `logStore.ts:169` 주석을 정정한다.**

### 이탈 ② 삭제 확인을 공용 `confirmDelete` 모달이 아니라 **화면 로컬 오버레이**로 만든다
`03 §790`의 오버레이 소유 목록이 근거이고, Step 15의 모달 파일을 선점하지 않기 위함이다(Step 13의 인라인 2단 확인과 같은 판단). Step 15가 편집기에서도 같은 UI가 필요하다고 판단하면 그때 승격하면 된다 — 반대 방향(모달을 먼저 만들고 Step 15가 재작성)보다 되돌리기 쉽다.

### 이탈 ③ CORS 실패 프레임을 "선택 가능하되 경고"가 아니라 **선택 불가 카드**로 만든다
`06 §6`은 *"선택 시 안내"* 라고 쓴다. 그러나 Step 8.5가 `hasUsableImage`를 만든 이유가 **합성 실패를 촬영 뒤로 미루지 않는 것**이다. 선택을 허용하면 손님이 6컷을 다 찍고 `Result`에서 실패한다. 카드는 그대로 보이고(진단 가능성 유지) 캡션으로 사유를 알리되 **선택은 막는다**. `06 §6`을 이 판정으로 정정한다.

### 이탈 ④ OPFS 이미지가 이미 없으면 삭제를 **성공**으로 본다
`05 §4.7` 1단계는 *"없으면 실패(false)"* 다. 그러나 같은 절의 4단계가 성공 판정을 *"이미지 파일이 실제로 사라졌는가"* 로 정의한다 — 이미 없으면 그 목표는 달성돼 있다. 실패로 보고하면 **카드가 영원히 지워지지 않는다**. 메타 레코드는 함께 지워 고아를 남기지 않고, 경고 로그를 남긴다.

### 이탈 ⑤ 개인 프레임에 `GET /frames?userId=` 를 쓰지 않는다
§4.5 참조. 얻는 것이 빈 배열인데 401이면 세션이 날아간다.

### 이탈 ⑥ `public/frames/`에 자산을 커밋하지 않고 **빈 매니페스트만** 둔다
VF-10·F-11. 경로·포맷 규약만 고정하고 실제 PNG는 운영 자산 준비 시 추가한다. 번들 0개여도 ①②④로 목록이 비지 않는다.

---

## 15. 테스트 계획 (js-developer가 작성할 것)

vitest·node. 예상 **+78건**(현행 1297 → 약 1375).

### 15.1 도메인 — `tests/unit/domain/frames.test.ts`(증분) + `tests/unit/frames/frameStorePolicy.test.ts`(신규)

| # | 케이스 |
|---|--------|
| D1 | `isFrameListInteractive`: `Ready`·`Degraded` = true / `Loading`·`Failed` = false (4국면 전수) |
| D2 | `frameStoreKey`: `public:{name}` / `user:{owner}:{name}` · 이름의 `:`가 키를 깨지 않는다(마지막 세그먼트 규칙 명시) |
| D3 | `frameIdFor`: `dbId` 있으면 서버 id 그대로 / 없으면 `local:{key}` |
| D4 | `frameImagePath(token)` = `frames/{token}.png` · 토큰에 `/`·`..`가 들어오면 **거부**(null) |
| D5 | `isFrameRecord`: 필수 필드 누락·타입 불일치·slots 비배열을 전부 거부 |
| D6 | `recordToTemplate` 왕복이 `slots`·`imageSize`를 보존 |
| D7 | `exceedsLocalFrameLimit(10) === true`, `(9) === false` |
| D8 | `parseBundleManifest`: 정상 · 항목 일부 손상 → 나머지 유지 · 배열 아님 → `[]` · 예외 0 |

### 15.2 저장소 — `tests/unit/frames/frameStore.test.ts` (메모리 메타 + 가짜 `OpfsClient`)

| # | 케이스 |
|---|--------|
| S1 | `cacheServerFrame`: **OPFS 쓰기 성공 후에만** 메타가 기록된다(쓰기 실패 → 메타 0건, 반환 `null`) |
| S2 | `listPublic`: 이미지 파일이 없는 레코드를 **건너뛴다**(반쪽 프레임 미노출) |
| S3 | `listPersonal(userId)`: 타인 소유·공용은 제외 |
| S4 | `deleteLocal`: 메타·파일 모두 사라지고 `true` |
| S5 | `deleteLocal`: `remove`가 실패해 파일이 남으면 **`false`**(성공 오인 금지 — M4) |
| S6 | `deleteLocal`: 파일이 애초에 없으면 메타를 지우고 `true`(이탈 ④) |
| S7 | 손상된 메타 레코드가 섞여 있어도 목록이 나머지를 돌려준다 |
| S8 | `usageBytes`가 `OpfsClient.usage("frames")`를 쓰고 실패 시 0 |
| S9 | **정적**: 소스에 `navigator.storage`·`createWritable`·`createSyncAccessHandle`·`getDirectory(` 0건 (FR-1) |
| S10 | **정적**: `FRAME_DB_NAME !== LOG_DB_NAME && FRAME_DB_NAME !== DIR_HANDLE_DB_NAME` (FR-3) |

### 15.3 카탈로그 — `tests/unit/frames/frameCatalog.test.ts`

| # | 케이스 |
|---|--------|
| C1 | **단일 비행**: 동시 2회 호출에서 `getDefaultFrames`·이미지 다운로드가 **각각 1회** |
| C2 | **replay**: 늦게 합류한 구독자가 최근 보고를 **동기 1회** 즉시 받는다 |
| C3 | **새 패스 스냅샷 리셋**: 앞 패스가 끝난 뒤의 첫 호출은 `Completed`가 아니라 `ResolvingLocal`을 replay |
| C4 | **호출자별 취소**: A가 abort해도 B는 정상 결과를 받고 캐시 쓰기가 완료된다 |
| C5 | 취소 후 재호출이 **새 패스**를 시작한다(`inFlight` 해제 — 함정 A 회귀) |
| C6 | abort 리스너가 누적되지 않는다(정상 완료 시 `removeEventListener` 호출 — 함정 B) |
| C7 | **서버 조회 실패를 삼킨다**: `loadPublic`이 reject하지 않고 캐시 결과를 돌려준다(E20) |
| C8 | 이름 dedup: 로컬에 같은 이름이 있으면 **다운로드하지 않는다**(`(n/m)`의 분모에서도 빠진다) |
| C9 | 4단 폴백: 캐시 0 + 서버 0 + 번들 1 → 번들 / 전부 0 → fallback 1개 |
| C10 | 다운로드 실패 프레임이 `unavailable`에 들어가고 `frames`에는 없다(이탈 ③) |
| C11 | `hasUsableImage` 필터: 빈 URL 프레임이 `frames`에 없다 |
| C12 | `_` 포함 공용 프레임에 경고 로그(동작은 유지) |
| C13 | `loadLocalOnly`가 **백엔드를 호출하지 않는다**(가짜 repo 호출 0회) |
| C14 | `loadPersonal`이 `frameRepository.getUserFrames`를 **호출하지 않는다**(이탈 ⑤) |
| C15 | `runSharedPass`가 어떤 내부 예외에서도 reject하지 않는다 |

### 15.4 로딩 루틴 — `tests/unit/frames/frameLoadRunner.test.ts`

| # | 케이스 |
|---|--------|
| R1 | `enter`: 첫 patch가 `phase:"Loading"` + `CATALOG_START_LABEL` (빈 목록 + 활성 [다음]이 없다) |
| R2 | 정상 완료 → `Ready` + `notice` 빈 문자열 + `selectedId` = 첫 항목 |
| R3 | 취소 → `Degraded` + 규격 문구 |
| R4 | `loadLocalOnly`까지 실패 → **`Failed`** |
| R5 | 목록 반영 중 예외(`apply` throw) → `finally`가 `Degraded`/`Failed`로 닫는다 (`Loading` 잔존 0) |
| R6 | stale(이탈) → `finally`가 **아무 patch도 내지 않는다** |
| R7 | `refresh`(quiet): `phase:"Loading"` patch가 **없다**, 진행 문구 patch도 없다 |
| R8 | `refresh` 중단 + 목록 유지 → 종전 국면 유지(`initialFrameCount` 근거) |
| R9 | `refresh` 결과 0개 → `Failed` / 종전 `Failed` + 결과 ≥1 → `Ready` 회복 |
| R10 | 무진행 30초: 보고가 없으면 30초에 abort |
| R11 | 진행 재무장: 25초마다 보고가 오면 **총 60초까지** 살아 있다 |
| R12 | 총 상한: 진행이 계속 와도 60초에서 abort(`nextFrameLoadDeadlineMs`가 0을 준다) |
| R13 | 늦은 진행 보고(stale)가 문구를 덮지 않는다 |
| R14 | 개인 프레임 로드 실패가 공용 목록을 지우지 않는다 |
| R15 | 상한 판정이 **실경과**다: 주입 시계를 점프시키면 tick 수와 무관하게 abort |

### 15.5 액션·삭제 — `tests/unit/frames/frameSelectActions.test.ts`

| # | 케이스 |
|---|--------|
| A1 | 권한 2축: `advanced_user` → create/delete true·isPower false / `manager` → 셋 다 true / `user`·`temp_user`·게스트 → 전부 false |
| A2 | 국면 가드: `Loading`·`Failed`에서 `goNext`·`createFrame`·`editSelected`·`requestDelete`가 **전부 no-op** |
| A3 | `goNext`가 `fixFrameAndResolveCutCount`를 **정확히 1회** 부른다(선택 없으면 0회) |
| A4 | 삭제 4문구 각각 |
| A5 | `alsoServer`가 **오버레이 닫기 전에** 확정된다(닫힌 뒤 리셋돼도 서버 삭제가 일어난다) |
| A6 | `{deleted:false}` → 이름 매칭 재시도 → 그래도 없으면 "문서를 찾지 못했습니다" |
| A7 | 서버 삭제 예외 → "서버 삭제 실패: {사유}" + 로컬 삭제 결과는 별개로 보고 |
| A8 | 로컬 실패 + 서버 성공 → 두 사실이 **함께** 보고된다 |
| A9 | 삭제 후 `runFrameLoad`가 `"refresh"`로 호출된다 |
| A10 | 비power가 `alsoServer=true`를 넣어도 서버 삭제가 **일어나지 않는다** |
| A11 | **정적 FR-2**: `src/` 전체에서 `canDeleteFrame(` 호출이 2인자다 |

### 15.6 이미지·썸네일 — `tests/unit/frames/frameImages.test.ts`

| # | 케이스 |
|---|--------|
| I1 | `downloadFrameImage`: 비200·네트워크 예외·빈 본문 → `null`, 예외 0 |
| I2 | `mode:"cors"`·`credentials:"omit"`가 실제로 전달된다(가짜 fetch 인자 검사 — WM2) |
| I3 | 15초 타임아웃에서 abort |
| I4 | `frameImageCache`: 같은 경로에 URL 1개만 만든다 · `revoke` 후 재생성 |
| I5 | 썸네일 프로브: resize가 실효하면 그 경로, `width` 불일치면 **폴백**으로 전환하고 판정을 캐시(2회째 프로브 없음) |
| I6 | 썸네일 실패 → `null`(예외 0), 중간 비트맵 `close()` 호출됨 |
| I7 | **정적 FR-6**: `compositor.ts`에 `mode: "cors"` 문자열이 남아 있다 |
| I8 | `compositor.loadFrameImage`가 `https:`에는 cors 옵션을, `blob:`에는 옵션 없이 fetch한다 |

### 15.7 HTTP — `tests/unit/http/frameRepository.test.ts`(신규)

| # | 케이스 |
|---|--------|
| H1 | `deleteFrame`이 `{deleted:true}` → `true`, `{deleted:false}` → `false` |
| H2 | 응답 형태가 어긋나면 `false`(성공 오인 금지) |
| H3 | 401/403이 타입 있는 예외로 전파된다 |
| H4 | `PUT /frames/{id}` 함수가 **여전히 없다**(소스 정적 검사) |

### 15.8 정적 불변식 — `tests/unit/frames/frameInvariants.test.ts`

| 코드 | 내용 |
|------|------|
| **FR-1** | `frameStore.ts`·`frameCatalog.ts`·`frameImageCache.ts`에 OPFS 직접 접근 0건(VF-14) |
| **FR-2** | `canDeleteFrame(` 호출 인자 2개(소유자 미전달) |
| **FR-3** | 프레임 DB 이름 ≠ 로그 DB ≠ 폴더 핸들 DB |
| **FR-5** | `FrameSelectView.tsx`에 `pushModal(`·`"confirmDelete"` 0건(Step 15 선점 금지) |
| **FR-6** | `compositor.ts`에 `mode: "cors"` 존재 |
| **FR-7** | `frameLoadPolicy.ts`의 **기존 export 이름 8종이 그대로 있다**(Step 8.5 산출물 보호) |

> ⚠️ **유휴 상한 불변식(60초 < 120초)은 만들지 마라.** `shell.test.ts`가 이미 4건으로 고정한다(F-15).
> ⚠️ **`docs/spec-vectors/frame-load-policy.json`을 수정하지 마라.** 기대값 변경은 Windows와의 교차 고정을 무력화한다.

### 15.9 E2E (Step 17로 이월 — 시나리오만 확정)

- 저장소 비우기 → Slow 3G 진입 → 오버레이 + `(n/m)` 관측 → `Ready`
- 오프라인 진입 → 안내 없이 목록 표시(`Degraded` 아님)
- 두 번째 진입에서 이미지 요청 0건(Network 패널)

---

## 16. 구현 단계 (WBS 블루프린트)

> 각 단계는 **self-contained**다. 컨텍스트가 없는 에이전트가 그 단계만 읽고 실행할 수 있다.
> 공통 사전 조건: `cd webclient && npm ci` 가 끝나 있고 `npx tsc --noEmit && npx vitest run`이 녹색(1297건).

### Step 14-1: 도메인 보강 (순수 3파일)

- **Context Brief**: it20 판정(`frameLoadPolicy`·`frameCatalogProgress`)과 카탈로그 판정(`frameCatalogPolicy`)은 Step 8.5에서 **이미 이식됐다 — 다시 만들지 마라.** 이 단계는 저장소·번들·국면 게이트에 필요한 **순수 함수만** 추가한다. 도메인은 아무것도 import하지 않고(내부 상대 경로만) `Date.now`·`Math.random`·브라우저 API·`console`을 쓰지 않는다 — `tests/unit/domain/purity.test.ts`가 파일 단위로 검사한다. 이름은 **한정형**으로 짓는다(`domain/index.ts`가 평면 `export *` 배럴이라 일반명은 충돌한다).
- **대상 파일**: `webclient/src/domain/frames/frameLoadPolicy.ts`(수정), `webclient/src/domain/frames/frameStorePolicy.ts`(신규), `webclient/src/domain/frames/bundleManifest.ts`(신규), `webclient/src/domain/index.ts`, `webclient/tests/unit/domain/frames.test.ts`, `webclient/tests/unit/frames/frameStorePolicy.test.ts`(신규)
- **선행 조건**: 없음
- **구현 내용**:
  - `frameLoadPolicy.ts`에 `isFrameListInteractive(phase): boolean`(`Ready`|`Degraded`)를 **추가만** 한다. 기존 export·판정·주석은 한 글자도 바꾸지 않는다.
  - `frameStorePolicy.ts`: §11.1 목록. `frameImagePath(token)`은 토큰에 `/`·`\`·`..`·빈 문자열이 오면 `null`을 돌려준다(경로 조작 1차 방어). `isFrameRecord`는 unknown 경계 검증이며 예외를 던지지 않는다.
  - `bundleManifest.ts`: `parseBundleManifest(raw: unknown)` — 배열이 아니면 `[]`, 항목별로 `name`(문자열·비어있지 않음)·`image`(문자열)·`width`/`height`(양의 정수)를 검사하고 위반 항목은 **건너뛴다**.
  - `domain/index.ts`에 두 파일 export 추가.
- **검증 명령**: `cd webclient && npx tsc --noEmit && npx vitest run tests/unit/domain`
- **완료 기준**:
  - [관측] `frames.test.ts`에 D1이, `frameStorePolicy.test.ts`에 D2~D8이 추가되어 전부 통과한다. `purity.test.ts`가 신규 2파일을 자동으로 포함하고 통과한다(파일 수 증가가 출력에 보인다).
  - [non-goal] `frame-load-policy.json`·`vectors.test.ts`·`SpecVectorTests.cs`는 **변경되지 않는다**. `classifyFrameLoad`·`finalizeFrameLoad`·`nextFrameLoadDeadlineMs`·`frameLoadNotice`의 동작이 불변이다(기존 테스트 무수정 통과가 증거).
  - [trigger] 새 함수는 **호출자가 아직 없다**(다음 단계가 붙인다). 배럴 export 추가만으로 기존 import가 깨지지 않는다.
- **롤백**: 신규 2파일 삭제 + `frameLoadPolicy.ts`·`domain/index.ts` revert.
- [ ] 완료

### Step 14-2: 프레임 저장소 + `deleteFrame` 계약 수정

- **Context Brief**: 프레임 메타는 IndexedDB, 이미지 PNG는 OPFS에 둔다(05 §4). **모든 OPFS 쓰기·삭제는 `OpfsClient`(전용 Worker RPC)를 지나야 한다** — 메인 스레드에서 직접 쓰면 iOS/iPadOS Safari에서 전 저장 경로가 실패한다(VF-14). IndexedDB는 **`mcphoto`를 쓰면 안 된다**: 로그 스토어가 그 DB 연결을 앱 수명 내내 붙들고 있고 `onversionchange` 핸들러가 없어 버전 업그레이드가 영구 blocked 된다(`logStore.ts:160-174`). Step 10의 `dirHandleRepo`(`mcphoto-handles` v1, 트랜잭션마다 열고 닫기)가 올바른 형태의 선례다. node에 IndexedDB가 없으므로 메타 계층을 인터페이스로 분리해 메모리 구현으로 테스트한다(`LogSink`/`createMemoryLogSink` 선례).
- **대상 파일**: `webclient/src/adapters/storage/frameStore.ts`(신규), `webclient/src/adapters/http/frameRepository.ts`(수정), `webclient/src/adapters/storage/logStore.ts`(주석만), `webclient/tests/unit/frames/frameStore.test.ts`(신규), `webclient/tests/unit/http/frameRepository.test.ts`(신규)
- **선행 조건**: Step 14-1(`frameStorePolicy`)
- **구현 내용**:
  - `FRAME_DB_NAME = "mcphoto-frames"` / `FRAME_DB_VERSION = 1` / store `frames`(keyPath `key`) + 인덱스 `by_scope`·`by_owner`·`by_name`. 연결은 트랜잭션 1회마다 열고 `finally`에서 `close()`, `onsuccess`에서 `db.onversionchange = () => db.close()`.
  - `FrameMetaStore` 인터페이스 + `createIndexedDbFrameMeta()` + `createMemoryFrameMeta()`. IndexedDB 부재 시 메모리로 축소(경고 로그).
  - `createFrameStore({ meta, opfs, newToken, now })` + §7.4의 7개 메서드. **`cacheServerFrame`은 OPFS 쓰기 성공 후에만 메타를 기록**한다. `deleteLocal`은 §7.4 4단계(실제 부재 확인). 예외를 전파하지 않는다.
  - `getFrameStore()`/`setFrameStoreForTests()` 싱글턴(호출 시점 해석).
  - `frameRepository.deleteFrame(id)` 반환을 `Promise<boolean>`으로 바꾸고 `{deleted}`를 파싱한다. 호출자가 아직 없으므로 파급이 없다.
  - `logStore.ts:169`의 낡은 주석을 "프레임 메타는 별 DB(`mcphoto-frames`)를 쓴다 — 이 연결에 `onversionchange`가 없어 같은 DB 업그레이드가 blocked 되기 때문"으로 정정한다. **동작은 바꾸지 않는다.**
- **검증 명령**: `cd webclient && npx tsc --noEmit && npx vitest run tests/unit/frames tests/unit/http tests/unit/storage`
- **완료 기준**:
  - [관측] S1~S10·H1~H4가 통과한다. 특히 **S5**(파일이 남으면 `false`)와 **S9**(소스에 OPFS 직접 접근 0건)가 통과한다.
  - [non-goal] `logStore`의 **런타임 동작이 바뀌지 않는다**(`logStore.test.ts` 무수정 통과). `mcphoto` DB 버전은 **1 그대로**다. `sessions/`·`results/` 관련 코드는 손대지 않는다.
  - [trigger] 프레임 DB는 `frameStore`의 첫 호출에서만 열린다 — 앱 부팅만으로 생성되지 않는다.
- **롤백**: `frameStore.ts`·테스트 2파일 삭제 + `frameRepository.ts`·`logStore.ts` revert.
- [ ] 완료

### Step 14-3: 이미지 계층(다운로드·URL 캐시·썸네일) + 합성 경로 보정

- **Context Brief**: 서버 프레임 이미지는 다른 오리진(`firebasestorage.googleapis.com`)에 있다. **CORS-clean하게 받지 않으면 canvas가 오염되어 `convertToBlob`이 `SecurityError`를 던지고 합성이 전면 실패한다(WM2).** 받은 Blob은 OPFS에 캐시해 이후 same-origin으로 쓴다. 목록 썸네일은 원본(1200×1600)을 그대로 여러 장 들고 있으면 모바일 메모리를 태우므로 `createImageBitmap`의 resize로 줄인다 — 단 **resize 옵션은 미지원 시 예외가 아니라 조용히 무시되므로** 결과 `width`를 확인해 폴백을 결정하고 그 판정을 캐시한다(04 §5.2).
- **대상 파일**: `webclient/src/adapters/frames/frameDownloader.ts`(신규), `webclient/src/adapters/frames/frameImageCache.ts`(신규), `webclient/src/adapters/frames/frameThumbnails.ts`(신규), `webclient/src/adapters/compose/compositor.ts`(수정), `webclient/tests/unit/frames/frameImages.test.ts`(신규)
- **선행 조건**: Step 14-2(`frameStore`가 캐시 대상)
- **구현 내용**:
  - `downloadFrameImage(url)`: `fetch(url, { mode:"cors", credentials:"omit", cache:"force-cache", signal })` + 15초 `AbortController`. 비200·빈 본문·예외 → `null` + `logger.warn`. **게이트 키·Bearer를 붙이지 않는다.**
  - `frameImageCache`: `Map<opfsPath, string>`. `frameImageUrl(path, file)`·`revokeFrameImage(path)`·`revokeAllFrameImages()`. **화면 이탈에서 해제하지 않는다**(선택 프레임이 `Result` 합성까지 살아야 한다).
  - `frameThumbnails`: §8.4. 프로브 판정을 모듈 변수에 캐시하고 `resetThumbnailProbeForTests()`를 노출.
  - `compositor.loadFrameImage`: `/^https?:/i` 인 경우에만 `{ mode:"cors", cache:"force-cache" }`를 준다. 그 외(blob:·상대 경로)는 옵션 없이 `fetch(url)`.
- **검증 명령**: `cd webclient && npx tsc --noEmit && npx vitest run tests/unit/frames tests/golden`
- **완료 기준**:
  - [관측] I1~I8이 통과한다. 특히 **I2**(가짜 fetch가 `mode:"cors"`를 받는다)와 **I5**(프로브 1회 후 폴백 고정)가 통과한다.
  - [non-goal] **골든 이미지 테스트가 무수정으로 통과한다**(합성 픽셀 불변). `composeCore`·`pixelFilters`는 건드리지 않는다.
  - [trigger] 썸네일 프로브는 **첫 호출 1회**만 일어난다(2회째 호출에서 `createImageBitmap` 옵션 인자가 폴백 형태다).
- **롤백**: 신규 3파일·테스트 삭제 + `compositor.ts` revert(원상: 항상 cors).
- [ ] 완료

### Step 14-4: 카탈로그 로더 — 단일 비행 + 진행 replay

- **Context Brief**: 이 단계가 it20의 심장이다. 부트스트랩 prefetch와 화면 진입이 **하나의 작업을 공유**하고(중복 다운로드 0), 늦게 합류한 구독자는 최근 진행 보고를 **즉시 replay** 받으며, 취소는 **호출자별**이라 공유 작업은 계속 진행해 캐시를 완성한다. Windows `src/MCPhoto.App/Services/FrameCatalogService.cs`(특히 `:61-131`)가 참조 구현이다. **서버 조회 실패는 이 어댑터 안에서 삼켜야 한다** — 밖으로 던지면 오프라인 부스가 매 진입마다 "가져오지 못했습니다" 안내를 띄운다(E20 회귀). 우선순위·dedup 판정은 `domain/frames/frameCatalogPolicy.ts`에 이미 있다(`serverFramesToCache`·`buildCatalog`·`hasUsableImage`·`hasUnderscoreCacheConflict`) — **다시 만들지 마라.**
- **대상 파일**: `webclient/src/adapters/frames/frameCatalog.ts`(신규), `webclient/src/adapters/frames/bundleFrames.ts`(신규), `webclient/public/frames/index.json`(신규 — 내용 `[]`), `webclient/tests/unit/frames/frameCatalog.test.ts`(신규)
- **선행 조건**: Step 14-1·14-2·14-3
- **구현 내용**:
  - §4.1 계약 + §4.2 골격. **함정 A**(`finally`를 task 바깥에서 동일성 가드와 함께 붙인다)와 **함정 B**(abort 리스너를 어느 쪽이 이기든 제거)를 주석으로 명시한다.
  - `runSharedPass`는 **절대 reject하지 않는다**(전부 catch → 빈 결과 축퇴).
  - `loadCore`: §4.3. 서버 조회·다운로드 실패를 삼키는 catch에 "지우거나 rethrow로 바꾸면 E20 회귀" 경고 주석.
  - `loadLocalOnly`: 단일 비행에 **합류하지 않는다**. 백엔드 호출 0.
  - `loadPersonal(userId)`: `frameStore.listPersonal`만. **`frameRepository.getUserFrames`를 부르지 않는다**(401 → 세션 해제 위험).
  - `bundleFrames`: `/frames/index.json` fetch(3초 타임아웃) → `parseBundleManifest` → 항목별 `.slots` fetch(선택) → `parseSlotsFile`, 슬롯 0개면 `autoArrange(4, w, h, 3/4)`. 실패는 `[]`.
  - `getFrameCatalog()`/`createFrameCatalog(deps)`/`setFrameCatalogForTests()`.
- **검증 명령**: `cd webclient && npx tsc --noEmit && npx vitest run tests/unit/frames`
- **완료 기준**:
  - [관측] C1~C15가 통과한다. 특히 **C1**(동시 2호출 = 다운로드 1회)·**C3**(새 패스는 `ResolvingLocal`을 replay)·**C4**(A 취소에도 B 성공 + 캐시 완성)·**C7**(서버 실패를 삼켜 reject하지 않음)이 통과한다.
  - [non-goal] `frameCatalogPolicy.ts`·`frameLoadPolicy.ts`는 **변경되지 않는다**. `frameRepository.getUserFrames` 호출이 0건이다(C14가 고정).
  - [trigger] 서버 조회는 `loadPublic` 호출에만 일어난다. `loadLocalOnly`는 백엔드를 **한 번도** 부르지 않는다(C13).
- **롤백**: 신규 파일 3개·테스트 삭제.
- [ ] 완료

### Step 14-5: 화면 로직 — 국면 수명·상한·삭제 (React 무관)

- **Context Brief**: `FrameSelect`의 로딩은 **`finally`가 국면을 무조건 확정**하는 구조여야 오버레이 고착이 원리적으로 불가능하다 — `finalizeFrameLoad`는 어떤 입력에서도 `Loading`을 반환하지 않는다(Step 8.5가 32조합 전수로 고정). 대기 상한은 **무진행 30초 + 총 60초 2단**이고 진행 보고마다 재무장하며, 판정은 `nextFrameLoadDeadlineMs(실경과)`가 한다. 이 저장소는 jsdom이 없어 React 훅을 테스트할 수 없으므로 **순서·판정은 전부 이 단계의 모듈에 둔다**(`runResultNext`·`runUpload` 선례). Windows 참조는 `src/MCPhoto.App/ViewModels/FrameSelectViewModel.cs:142-241`(로딩)과 `:298-390`(삭제)이다.
- **대상 파일**: `webclient/src/screens/frameSelect/frameLoadDeadline.ts`(신규), `webclient/src/screens/frameSelect/frameLoadRunner.ts`(신규), `webclient/src/screens/frameSelect/frameSelectActions.ts`(신규), `webclient/src/ui/strings.ts`(수정), `webclient/tests/unit/frames/frameLoadRunner.test.ts`(신규), `webclient/tests/unit/frames/frameSelectActions.test.ts`(신규)
- **선행 조건**: Step 14-4
- **구현 내용**:
  - `createLoadDeadline(deps)`: §5. 시계·타이머를 **주입**받는다(테스트 결정성).
  - `runFrameLoad(deps, reason)`: §6.1 본체를 그대로. `initialPhase()`·`initialFrameCount()`·`isStale()`·`apply()`를 deps로 받는다. `FrameLoadCancelledError`와 그 밖의 실패를 **같은 갈래**(`interrupted=true`)로 다루되 로그 문구만 구분한다.
  - `frameSelectActions.ts`: `frameSelectPermissions(role)`(`canWriteFrames` 2축 + `isPower`), `canOpenDelete(frame, role, phase)`, `runFrameDelete(deps, input)`(§9.2 6단계 + 4문구), `guardInteractive(phase)`.
  - `STRINGS.frames`에 추가: `deleteLocalFailed`·`deleteServerOk`·`deleteServerNotFound`(`{n}`=이름)·`deleteServerFailed`(`{n}`=사유)·`deleteConfirmTitle`·`deleteAlsoServer`·`unavailableImage`·`skipWait`(*"기다리지 않고 시작"*)·`goHome`(*"메인으로"*). 문구는 03 §15.5·§4.1과 **문자열 일치**여야 한다.
- **검증 명령**: `cd webclient && npx tsc --noEmit && npx vitest run tests/unit/frames`
- **완료 기준**:
  - [관측] R1~R15·A1~A11이 통과한다. 특히 **R4**(로컬 폴백까지 실패 → `Failed`)·**R5**(`apply` 예외에서도 `Loading` 잔존 0)·**R7**(quiet에 `Loading` patch 없음)·**R11/R12**(재무장과 총 상한)·**A5**(`alsoServer` 사전 확정)가 통과한다.
  - [non-goal] React를 **import하지 않는다**(정적 검사 또는 import 목록 확인). `shell.test.ts`의 유휴 불변식 4건은 **손대지 않는다**. 새 유휴 상한 테스트를 만들지 않는다.
  - [trigger] 상한 취소는 무진행 30초 또는 총 60초에만 일어난다. `refresh`는 오버레이를 **켜지 않는다**.
- **롤백**: 신규 3파일·테스트 2파일 삭제 + `strings.ts` revert.
- [ ] 완료

### Step 14-6: `FrameSelect` 본편 UI + 배선

- **Context Brief**: Step 7이 만든 최소 `FrameSelect`(`ui/views/FlowViews.tsx:68-155`, fallback 1개)를 본편으로 교체한다. **컷 수 해석은 [다음]의 `fixFrameAndResolveCutCount(selected, configuredCutCount)` 한 줄이며 이 화면이 유일한 해석 지점이다(VF-12) — 그 성질을 유지하라.** 화면 로컬 오버레이 3종(대기·실패·삭제 확인)은 **셸 모달 스택을 쓰지 않는다**: `confirmDelete` 모달은 Step 15의 것이고(WBS Step 15 대상 파일), `03 §790`이 삭제 확인을 `FrameSelect`의 화면 로컬 오버레이로 규정한다. 유휴 경고(셸 모달)는 언제나 이 오버레이들 위에 그려진다. jsdom이 없으므로 **이 단계의 코드에는 판정을 넣지 않는다** — 전부 Step 14-5 모듈을 호출한다.
- **대상 파일**: `webclient/src/ui/views/FrameSelectView.tsx`(신규), `webclient/src/ui/views/frameSelect.module.css`(신규), `webclient/src/screens/frameSelect/useFrameSelect.ts`(신규), `webclient/src/ui/views/FlowViews.tsx`(수정 — 최소판 제거), `webclient/src/App.tsx`(수정 — import 경로)
- **선행 조건**: Step 14-5
- **구현 내용**:
  - `useFrameSelect()`: `useState`로 §10 표면을 보관하고, **세대 카운터(`runIdRef`)** 로 `isStale()`을 구현한다. 진입 effect의 cleanup은 **① 세대 증가 → ② `controller.abort()`** 순서다(반대로 하면 취소가 stale이 아닌 상태에서 잡혀 국면을 덮는다). `apply(patch)`는 `setState((prev) => ({...prev, ...patch}))`.
  - `FrameSelectView.tsx`: §10 트리. 초기 `phase`는 `DEFAULT_FRAME_LOAD_PHASE`(= `Loading`)로 시작해 **첫 페인트에 "빈 목록 + 활성 [다음]"이 나타나지 않게** 한다.
  - 카드 ✕는 `canDeleteFrames && canDeleteFrame(frame, role) && interactive`일 때만 **렌더**한다(렌더 가드). 액션 함수 첫 줄에서 다시 확인한다(액션 가드).
  - `FrameThumb`: effect에서 `fetch(url).blob()` → `createFrameThumbnail` → canvas 그리기, **cleanup에서 `ImageBitmap.close()`**.
  - `unavailable` 카드는 `<img src={원격URL}>` + `aria-disabled` + 캡션(`STRINGS.frames.unavailableImage`). 선택 불가.
  - [프레임 만들기]/[선택 편집]은 권한대로 렌더하고 `shellStore.go("FrameEditor")`만 한다(편집 대상 인계 채널은 Step 15가 만든다 — 코드에 `TODO(Step 15)` 주석).
  - `FlowViews.tsx`에서 `FrameSelectView`와 그 전용 import(`ensureFallbackImageUrl`·`createFallbackFrame`·`hasUsableImage`·`classifyFrameLoad`·`frameLoadNotice`)를 제거한다. `App.tsx`의 import를 새 경로로 바꾼다.
- **검증 명령**: `cd webclient && npx tsc --noEmit && npx vitest run && npm run build`
- **완료 기준**:
  - [관측] 타입 검사·전체 테스트·프로덕션 빌드가 통과하고, `App.tsx`가 새 `FrameSelectView`를 렌더한다(`FlowViews.tsx`에 `FrameSelectView` 정의가 **0건**).
  - [non-goal] `Home`·`Guide`·`Capture`·`CutSelect`·`Result` 화면 코드가 **변경되지 않는다**. `fixFrameAndResolveCutCount` 호출부가 **여전히 1곳**이다(grep으로 확인). `screens/modals/confirmDelete/`·`framePicker/` 디렉터리를 **만들지 않는다**.
  - [trigger] 프레임 로딩은 화면 진입(마운트)과 [다시 시도]에만 시작된다. 삭제 후 재스캔은 `quiet`이라 오버레이가 뜨지 않는다. [기다리지 않고 시작]은 **새 로딩을 시작하지 않는다**.
- **롤백**: 신규 3파일 삭제 + `FlowViews.tsx`·`App.tsx` revert(최소 `FrameSelect`로 복귀).
- [ ] 완료

### Step 14-7: 정적 불변식 · prefetch 배선 · 문서 동기화 · 전량 검증

- **Context Brief**: 이 저장소는 문서에만 있는 규칙을 **테스트가 소스를 읽어** 고정하는 관례를 쓴다(15 §3.4). 이 단계에서 Step 14가 도입한 6개 불변식을 그 방식으로 박고, 부트스트랩 prefetch를 붙이며, 설계와 어긋난 기존 문서 3곳을 정정한다.
- **대상 파일**: `webclient/tests/unit/frames/frameInvariants.test.ts`(신규), `webclient/src/main.tsx`(수정), `docs/web-client/05-storage-and-persistence.md`(§4.2 DB 이름), `docs/web-client/06-backend-integration-web.md`(§6 CORS 실패 처리), `docs/web-client/11-wbs.md`(Step 14 체크박스·산출물), `docs/web-client/15-implementation-conventions.md`(§3.4 불변식 표 + §6 Step 14 절 + §7 상태 요약), `docs/design/README.md`(§3.1 등재), `docs/web-client/14-handoff-and-user-actions.md`(V23 실측)
- **선행 조건**: Step 14-6
- **구현 내용**:
  - `frameInvariants.test.ts`: **FR-1·FR-2·FR-3·FR-5·FR-6·FR-7**(§15.8). 주석 제거 후 grep하는 기존 `authInvariants.test.ts`·`settingsInvariants.test.ts` 방식을 그대로 쓴다.
  - `main.tsx`의 `startApp` 말미에 prefetch 1줄: `void getFrameCatalog().loadPublic().catch(() => undefined);` + "첫 페인트 뒤 fire-and-forget · 결과 폐기 · 실패 무시 · 단일 비행이 화면 진입과 작업을 공유한다" 주석.
  - 문서 정정: `05 §4.2` DB 이름(이탈 ①) · `06 §6` CORS 실패 시 선택 차단(이탈 ③) · `11-wbs` Step 14 완료 기록(산출물·검증 수치·설계 이탈 6건) · `15 §3.4`에 FR-1·2·3·5·6·7 등재 + §6에 "Step 14 완료 — 뒤 Step이 알아야 할 것" 절 신설(Step 15가 `confirmDelete` 모달·프레임 피커·편집기를 얹을 지점, `frameStore.saveLocal`·`LOCAL_FRAME_LIMIT`가 준비돼 있다는 사실) + §7 상태 요약 갱신 · `docs/design/README.md` §3.1에 이 문서 등재 · `14 §10.9`에 V23 실측 7건 등재.
- **검증 명령**:
  - `cd webclient && npx tsc --noEmit && npx vitest run && npm run coverage && npm run build`
  - `cd webclient && npx vitest run tests/unit/domain/vectors.test.ts` (벡터 무변경 확인)
  - `cd E:/Study/photobooth && dotnet test tests/MCPhoto.Tests --filter SpecVectorTests` (교차 고정 무회귀)
- **완료 기준**:
  - [관측] 웹 테스트가 전부 통과하고(약 1375건) `src/domain` 커버리지 임계(lines 95 / branches 90)를 넘으며 `npm run build`가 성공한다. Windows `SpecVectorTests`가 무수정 통과한다. `docs/design/README.md` §3.1에 이 문서가 보인다.
  - [non-goal] `docs/spec-vectors/*.json`·`tests/MCPhoto.Tests/*`·`web/functions/*`가 **변경되지 않는다**(`git status`로 확인). 새 유휴 상한 테스트가 생기지 않았다(FR 목록에 없음).
  - [trigger] prefetch는 **React 마운트 뒤 1회**만 일어난다(`bootstrap()` 안이 아니다). 실패해도 앱이 죽지 않는다.
- **롤백**: `frameInvariants.test.ts` 삭제 + `main.tsx`·문서 revert.
- [ ] 완료

### 완결성 게이트 (developer 전달 전 자체 검사)

- [x] 검증된 사실(F-1~F-17) / 미검증 가정(A-1~A-5)이 분리돼 있다
- [x] 모든 가정에 검증 단계가 매핑돼 있다(A-1→14-3, A-2→V23-6, A-3→14-3+V23-5, A-4→V23-3, A-5→14-2+V23-7)
- [x] 7개 단계 전부에 필수 7필드가 채워져 있다
- [x] 모든 완료 기준이 관측 기반 3문 형식이다(UI 단계 14-6은 non-goal·trigger 포함)
- [x] 검증 명령이 전부 자동 실행 가능하다(`tsc`·`vitest`·`npm run build`·`npm run coverage`·`dotnet test`)

---

## 17. 남는 사용자 액션 — 실측 V23 (브라우저 필요, 자동화 불가)

| # | 시나리오 | 기대 관측 |
|---|----------|-----------|
| V23-1 | DevTools에서 OPFS·IndexedDB를 비우고 Slow 3G로 `FrameSelect` 진입 | 진입 즉시 대기 오버레이 + `(n/m)` 카운터. **"빈 목록 + 활성 [다음]"이 한 프레임도 나타나지 않는다** |
| V23-2 | 오프라인 전환 후 진입 | 안내 없이 조용히 목록 표시(`Ready`). "가져오지 못해…" 문구가 **없다** |
| V23-3 | 온라인에서 서버 프레임을 골라 촬영·합성 | 합성 성공(canvas 오염 없음 — WM2 · OA-2 종결) |
| V23-4 | 두 번째 진입 | Network 패널에 프레임 이미지 요청 **0건**(이름 dedup) · blob: URL 합성 성공(A-1) |
| V23-5 | Safari(iOS 17)에서 목록 진입 | 썸네일이 보이고 콘솔 오류 0 · 폴백 경로여도 카드가 정상(A-3) |
| V23-6 | 프레임 20개 상태에서 목록 왕복 10회 | 메모리 증가가 누적되지 않는다(썸네일 `close()`·object URL 재사용 — A-2) |
| V23-7 | DevTools → Application → IndexedDB | `mcphoto`·`mcphoto-handles`·`mcphoto-frames` **3개**가 각각 존재하고 로그가 계속 쌓인다(A-5) |
| V23-8 | power 계정으로 서버 프레임 삭제("서버에서도 제거" 체크) | 결과 문구가 4종 중 정확한 하나이고, 목록이 **오버레이 없이** 갱신된다 |

`docs/web-client/14-handoff-and-user-actions.md §10.9`에 등재한다.

---

## 18. 리스크와 명시적 비목표

### 18.1 리스크

| # | 리스크 | 완화 |
|---|--------|------|
| K1 | prefetch가 부팅마다 `GET /frames/default` + 이미지를 받는다 | 두 번째 부팅부터는 이름 dedup으로 이미지 요청이 0이다. 목록 조회 1건은 감수한다(Windows도 동일) |
| K2 | object URL을 앱 수명 동안 들고 있다 | 항목 수가 프레임 개수로 유계이고 OPFS File은 디스크 백업이다(A-2). 삭제 시 revoke |
| K3 | 삭제 후 서버 재조회로 카드가 되돌아온다 | **의도된 종전 동작 보존**(§9.5). 사용자에게는 "서버에서도 제거" 체크가 답이다 |
| K4 | 번들 매니페스트가 Windows `Frame/` 폴더 규약과 다르다 | 내보내기/가져오기(`.slots` 텍스트)는 규약을 그대로 유지하므로 상호 이동은 성립한다(Step 16) |
| K5 | `unavailable` 카드가 매 진입 재시도로 네트워크를 태운다 | 항목 수가 적고 15초 타임아웃이 있다. 무진행 30초 상한이 최종 방어 |

### 18.2 명시적 비목표

- 프레임 **편집기**·프레임 피커 모달·서버 등록 확인 모달 — Step 15.
- 프레임 zip **내보내기/가져오기** — Step 16(`frameStore.saveLocal`만 준비해 둔다).
- `PUT /frames/{id}` 호출 — 정책상 영구 비목표.
- 프레임 **10개 상한 강제** — 판정(`exceedsLocalFrameLimit`)과 개수 조회만 준비하고 게이트는 저장 경로를 만드는 Step 15가 건다.
- 유휴 상한 불변식 테스트 — 이미 있다(F-15).
- `docs/spec-vectors/frame-load-policy.json` 변경 — 금지.

---

## 19. 요약

- **판정은 이미 있다.** 이 Step은 `frameLoadPolicy`·`frameCatalogProgress`·`frameCatalogPolicy` 위에 어댑터 6개 · 화면 로직 4개 · UI 1개를 얹는 작업이고, 도메인에 새로 넣는 것은 순수 함수 3파일뿐이다.
- **오버레이 고착은 구조적으로 불가능하다**: `finalizeFrameLoad`가 `Loading`을 반환할 수 없고, 화면의 `finally`가 그 함수를 무조건 부른다.
- **단일 비행 + 진행 replay**가 "줄 서기"를 없앤다. 취소는 호출자별이라 [기다리지 않고 시작]이 캐시 완성을 방해하지 않고, `<StrictMode>` 이중 effect도 중복 다운로드를 만들지 않는다.
- **오프라인은 `Ready`다.** 서버 조회 실패를 어댑터가 삼키는 catch 한 곳이 그 성질을 지킨다.
- **WM2는 두 곳에서 지킨다**: 다운로드(`mode:"cors"`)와 캐시(OPFS same-origin). 합성 경로의 fetch 옵션도 원격/로컬로 갈라 blob: 스킴의 불확실성을 제거한다.
- **발견한 기존 결함 2건**을 함께 고친다: `frameRepository.deleteFrame`이 `{deleted}`를 버리는 것(F-8), `logStore`의 낡은 주석이 다음 작업자를 blocked DB로 안내하는 것(F-7).
