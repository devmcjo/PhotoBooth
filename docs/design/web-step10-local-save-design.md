# Step 10 · 결과물 로컬 보관 (M6-W) 구현 설계

| 항목 | 값 |
|------|-----|
| 대상 | WBS **Step 10** — [11 §Step 10](../11-wbs.md) |
| 규격 | [05 §5](../05-storage-and-persistence.md) · [03 §8.1](../03-screens-spec.md) · [12 C1](../12-web-vs-windows-differences.md) · [analysis/41 §5](../../analysis/41-local-data-and-file-formats.md) |
| 관례 | [15 · 구현 관례](../15-implementation-conventions.md) — 계층·테스트 전략·함정 12건 |
| 작성 | js-architect (설계만. 구현은 js-developer, 검증은 js-code-reviewer) |
| 작성일 | 2026-07-31 |
| 전제 | Step 0~9 완료 · 웹 테스트 **645**(26파일) 녹색 · 브랜치 `feature/web-client-foundation` |

> **이 Step의 한 줄 요약**: 합성 결과물을 **업로드보다 먼저** OPFS `results/`에 남기고(필수),
> 데스크톱 Chromium이면 운영자가 지정한 실제 폴더에도 복사한다(부가).
> 업로드·QR은 **다음 Step**이며 여기서는 **분기 진입점만** 비워 둔다.

---

## 0. 검증된 사실 / 미검증 가정

### 0.1 검증된 사실 (코드·문서를 직접 읽어 확인)

| # | 사실 | 근거 |
|---|------|------|
| F1 | 합성 결과 Blob은 `useResultCompose().currentBlob()` — `blobRef.current`를 그대로 돌려준다. 필터 변경 시 갱신되고 화면 이탈까지 살아 있다 | `webclient/src/screens/result/useResultCompose.ts:86,123` |
| F2 | `ResultView.goNext()`는 현재 **`finish()` → `currentScreen() !== "Result"` 가드 → `isQrEffectivelyEnabled` → `go()`** 3단계뿐이다. 보관 코드가 들어갈 자리는 가드와 판정 사이다 | `webclient/src/ui/views/FlowViews.tsx:374-388` |
| F3 | 타임랩스 결과는 `sessionStore`에 없다. `getTimelapseService().current()`가 유일한 읽기 경로이고 `stop()`이 `result = null`로 폐기한다. `stop()`은 `stopEncoder` 훅에 걸려 `returnHome`에서 호출된다 | `timelapseService.ts:242,244-251` · `useCaptureRunner.ts:153` · `shellStore.ts:135` |
| F4 | `finish()`는 멱등이고 **어떤 실패도 `null`**이다(throw 없음 — VF-6) | `timelapseService.ts:206-240` · `timelapseEncoder.ts:72-80` |
| F5 | `OpfsClient`는 `write`/`remove`/`list`/`exists`/`capability`/`readFile` 6개다. **쓰기·삭제·열거는 Worker RPC**, **읽기(`readFile`)만 메인 스레드**다 | `opfsClient.ts:26-36,120-134` |
| F6 | `opfsWriter.worker.ts`가 앱의 **유일한 OPFS 쓰기 지점**이고, `sync-access-handle` → `writable-stream` → `none` 순으로 실제 쓰기를 해 보고 능력을 판정한다 | `opfsWriter.worker.ts:43-79` |
| F7 | `OPFS_DIRS = { sessions, results, frames }`. `purgeSessionLeftovers`는 `sessions/`만 지우고, `results/`·`frames/`를 건드리지 않는다는 것을 테스트가 고정한다 | `opfsProtocol.ts:58-62` · `opfs.test.ts:159-174` |
| F8 | Windows 규약: `SessionFolderName = "mcphoto_" + yyMMdd_HHmm`, 충돌 시 `-2`…`-999`, 소진 시 `Guid:N`(32 hex). `LocalSaveTests`가 `2026-07-20 14:45 → mcphoto_260720_1445`를 고정한다 | `src/MCPhoto.Core/LocalSave/LocalSaveService.cs:20-21,67-77` · `tests/MCPhoto.Tests/LocalSaveTests.cs:33-34` |
| F9 | Windows는 폴더명 기준 시각으로 **`session.SessionTime`(촬영 시작 시각)** 을 넘긴다 | `ResultViewModel.cs:144` · `CaptureViewModel.cs:94` |
| F10 | 웹 `sessionId`는 `yyyyMMdd_HHmmss_uuid`이고 **촬영 시작 직전**(`createWorkspace`)에 발급된다 → F9와 같은 시각 의미다 | `captureSessionController.ts:48-51` · `useCaptureRunner.ts:113-114` |
| F11 | 도메인 순수성 테스트는 `src/domain/**/*.ts`를 **glob으로 수집**한다 → 새 도메인 파일은 자동 포함된다 | `purity.test.ts:15-30` |
| F12 | `src/domain/index.ts`는 **평면 `export *` 배럴**이다 → 짧은 이름은 충돌한다(한정형 이름 필수) | `domain/index.ts` · [15 §6](../15-implementation-conventions.md) |
| F13 | vitest 환경은 **node**(jsdom은 파일 상단 주석으로 opt-in), `src/domain` 커버리지 임계 **95/95/95/90** | `vitest.config.ts` |
| F14 | 로그 마스킹은 키를 소문자화 + `-_ ` 제거 후 **정확 일치**로 판정한다. `state`·`code`·`token`·`pin`·`nonce`가 목록에 있고 `status`·`folderName`은 없다 | `logPolicy.ts:40-66` |
| F15 | `logStore`의 IndexedDB(`mcphoto` v1) 연결에 **`onversionchange` 핸들러가 없다** — 같은 DB의 버전 업그레이드는 영구 blocked 된다 | `logStore.ts:160-240` |
| F16 | `STRINGS.save.failed`("저장 위치에 쓸 수 없습니다.")·`STRINGS.storage.folderUnsupported`가 **이미 있다** → 새 문구가 필요 없다 | `ui/strings.ts:44-48,78-84` |
| F17 | `Settings`는 아직 `DummyScreen`이다. Step 6이 `DummyScreen`에 임시 [카메라 테스트] 버튼을 두고 "Step 13에서 설정 화면으로 옮긴다"고 남긴 선례가 있다 | `App.tsx:76-83,152-166` |
| F18 | React 밖에서 설정을 읽는 `currentSettings()`가 이미 있다 | `settingsStore.ts:119-121` |
| F19 | `opfsClient.write()`는 `Blob`을 받으면 매 호출마다 `await blob.arrayBuffer()`로 **새 버퍼**를 만들어 transfer한다 → 같은 Blob을 ①·②에 두 번 써도 detach 문제가 없다 | `opfsClient.ts:81-94` |

### 0.2 미검증 가정 (전부 검증 단계가 매핑돼 있다)

| # | 가정 | 검증 단계 |
|---|------|-----------|
| A1 | OPFS `usage` 재귀 walk(폴더 200 × 파일 2 = `getFile()` 400회)이 [다음] 체감을 해치지 않는다 | **S10-2** — 가짜 Worker로 400 엔트리 walk의 호출 횟수를 단언. 실기기 소요는 **V19-1**(사람)로 등재 |
| A2 | 사용자 지정 디렉터리 핸들에서 `keys()` 열거가 동작해 ② 계층의 폴더명 충돌을 검사할 수 있다 | **S10-4** — 열거가 실패하면 base 이름을 그대로 쓰는 폴백을 구현하고 그 경로를 테스트 |
| A3 | `showDirectoryPicker`가 있는 브라우저에는 `createWritable`도 반드시 있다 | **S10-4** — 두 기능을 **각각 런타임 감지**(함정 #2). 없으면 ② 전체를 `unsupported`로 축소 |
| A4 | 별 IndexedDB(`mcphoto-handles`)를 여는 것이 로그 DB(`mcphoto` v1) 연결과 충돌하지 않는다 | **S10-4** — DB 이름이 다르므로 구조적으로 충돌 불가. 테스트에서 두 DB 이름이 다름을 정적 단언 |
| A5 | 네트워크 차단 완주 시 OPFS `results/`에 파일이 실제로 생긴다(E8) | **브라우저 실행 필요 — 자동화 불가**. [14 §10](../14-handoff-and-user-actions.md)에 **V19** 묶음으로 등재(설계자·개발자 몫이 아니다) |

---

## 1. 전체 흐름 — 순서가 불변식이다 (M6-W)

```
ResultView [다음] 클릭
   │
   ├─ 1. getTimelapseService().finish()          ← 멱등. 실패·미지원은 null (VF-6)
   │
   ├─ 2. 홈 복귀 가드   stillOnResult()?  ─── no ──► 아무것도 하지 않고 종료
   │                                                (세션이 폐기됐고 타임랩스도 폐기됐다)
   ├─ 3. 로컬 보관   saveResultLocally()          ★★ M6-W 본체 ★★
   │        ① OPFS results/{folder}/final.{ext}  ← opfsWriter Worker 경계 (VF-14)
   │        ① OPFS results/{folder}/timelapse.mp4 (타임랩스가 있을 때만)
   │        ② 폴더 핸들이 granted면 그 폴더에도 복사 (Chromium 데스크톱만)
   │        ③ results/ 보존 정책 집행 (2GB / 200세션)
   │
   ├─ 4. 실패 표현   final 기록 실패 → 실패 토스트 + error 로그. **전이는 계속한다**
   │
   ├─ 5. ┌──────────────────────────────────────────────┐
   │     │  Step 11: 업로드 3단계가 **여기** 들어간다      │  ← 이번 Step은 주석만 남긴다
   │     └──────────────────────────────────────────────┘
   │
   └─ 6. effective QR 판정 → go("Qr") 또는 go("Done")
```

**왜 2번(홈 복귀 가드)이 보관보다 앞인가**: 홈 복귀는 손님 취소 또는 유휴 만료다.
그 시점에 세션 데이터·작업 공간·타임랩스 결과가 모두 폐기된 뒤라 보관해 봐야 반쪽이고,
취소된 촬영물을 **영구 보관**하는 것은 잔재 삭제 규격(analysis/41 §4)의 취지에 반한다.
M6-W("업로드 이전 보관")는 깨지지 않는다 — 이 경로에서는 업로드도 하지 않기 때문이다.

**왜 4번에서 전이를 막지 않는가**: 보관 실패로 화면이 멈추면 손님이 키오스크에 갇힌다.
[05 §5.2] "실패 처리 = 예외가 아니라 실패 반환 + 오류 표시. **촬영 흐름을 중단하지 않는다**".

---

## 2. 계층 배치 — 파일 목록과 책임

```
ui → screens → shell → domain ← adapters        (15 §2)
```

### 2.1 신규 파일 (7)

| 파일 | 계층 | 책임 | 근거 |
|------|------|------|------|
| `src/domain/results/resultNaming.ts` | 도메인 | 폴더명·파일명 조립, 충돌 접미 해석, 규약 이름 판정 | 순수 문자열 규칙이다. Windows `LocalSaveService`와 **같은 값**을 내야 하므로 브라우저 API가 섞이면 교차 검증이 불가능해진다 |
| `src/domain/results/resultSavePlan.ts` | 도메인 | "무엇을 어디에 쓸지" 판정 — `skip`/`save` 판별 유니온 + 대상 목록 | 저장 대상 선별은 순수 판정이다. 어댑터에 두면 "타임랩스 null" 분기가 브라우저 없이 테스트되지 않는다 |
| `src/domain/results/resultsRetention.ts` | 도메인 | 2GB/200세션 초과 시 삭제 대상 선별 | 산술·정렬만 있는 정책이다. 함정 #3(부동소수)을 피하려 **정수 바이트끼리** 비교한다 |
| `src/adapters/storage/resultSaver.ts` | 어댑터 | ①·②·③ 오케스트레이션. **절대 throw하지 않는다** | 규격이 요구하는 "순서"의 소유자. Blob·시각·난수 같은 부정 요소가 여기서 끝난다 |
| `src/adapters/storage/resultsStore.ts` | 어댑터 | `results/` 라이브러리 — 목록·용량·삭제·읽기 + 보존 정책 집행 | 보관본 접근을 한 곳에 모은다. Step 13 [보관된 결과물] 패널과 Step 16 진단이 이 인터페이스를 그대로 쓴다 |
| `src/adapters/storage/dirHandleRepo.ts` | 어댑터 | ② 계층 — `showDirectoryPicker`·IndexedDB 영속·권한·폴더 쓰기 | Chromium 전용 능력을 **한 파일에 격리**해 기능 감지 1개로 UI·저장·복원을 통째로 끄고 켤 수 있게 한다(05 §5.3) |
| `src/screens/result/resultNext.ts` | 화면(컴포저) | [다음] 처리 오케스트레이션. 의존을 전부 주입받아 **순서를 node에서 검증 가능**하게 한다 | 순서가 불변식인데 React 안에 있으면 테스트가 닿지 못한다(15 §3.1의 "순수 코어 + 얇은 래퍼") |

### 2.2 수정 파일 (6)

| 파일 | 변경 | 회귀 위험 |
|------|------|-----------|
| `src/adapters/storage/opfsProtocol.ts` | `usage` op 변형 + `OpfsUsage`/`OpfsUsageEntry` 타입 추가 | **낮음**. `OpfsRequestWithoutId`가 분배 조건부 타입이라 변형 추가는 자동 반영된다(`opfsProtocol.ts:22-26`) |
| `src/adapters/storage/opfsWriter.worker.ts` | `usage` 핸들러 추가(읽기 전용 walk) | **중간**. `handle(request)`의 `switch`에 `default: never` 소진 검사가 있어 케이스를 빠뜨리면 **컴파일 에러**로 잡힌다. 아래 §5.2의 "쓰기 잠금 금지" 주의 필수 |
| `src/adapters/storage/opfsClient.ts` | `usage(path)` 메서드 + `UNSUPPORTED_OPFS_CLIENT`에 항목 추가 | **중간**. `OpfsClient` 인터페이스 확장 → 기존 테스트가 `{...UNSUPPORTED_OPFS_CLIENT, …}` 스프레드로 목을 만들어서(`opfs.test.ts:142`) 기본 구현만 추가하면 깨지지 않는다 |
| `src/domain/index.ts` | `export * from "./results/…"` 3줄 | **낮음**. 이름 충돌만 주의(F12) — 아래 §3.4에 충돌 검사 결과 있음 |
| `src/ui/views/FlowViews.tsx` | `ResultView.goNext` 본문을 `runResultNext(...)` 호출로 교체 | **중간**. §6에 기존 코드 인용 + 교체 후 코드 명시 |
| `src/App.tsx` | `DummyScreen`에 **임시** [로컬 저장 폴더 선택] 버튼(Settings 화면 한정, 기능 감지 게이트) | **낮음**. Step 6의 [카메라 테스트] 버튼과 같은 선례(F17). Step 13에서 설정 화면으로 이관 |

### 2.3 테스트 파일 (신규 3 · 수정 1)

| 파일 | 성격 |
|------|------|
| `tests/unit/domain/results.test.ts` | 신규 — 도메인 3파일 전량 |
| `tests/unit/storage/resultSaver.test.ts` | 신규 — `resultSaver`·`resultsStore`·`dirHandleRepo` + **정적 불변식** |
| `tests/unit/screens/resultNext.test.ts` | 신규 — **호출 순서(M6-W)** 고정 |
| `tests/unit/storage/opfs.test.ts` | 수정 — `usage` op 케이스 추가 |

### 2.4 이번 Step에서 **만들지 않는 것** (WBS 이탈 — 11-wbs.md에 기록할 것)

| WBS 항목 | 처리 | 근거 |
|---------|------|------|
| `src/screens/settings/resultsPanel.tsx` ([보관된 결과물] 패널) | **Step 13으로 이월**. 어댑터(`resultsStore`)는 이번에 완성해 패널이 얹히기만 하면 되게 둔다 | 설정 화면 자체가 아직 `DummyScreen`이다(F17). 패널만 먼저 만들면 진입점이 없어 검증 불가 |
| [폴더 선택] 정식 UI·권한 재요청 배너 | **Step 13으로 이월**. 대신 `DummyScreen`에 임시 진입점을 둔다 | 진입점이 전혀 없으면 ② 계층을 **한 번도 실행할 수 없다** → WBS 완료 기준("폴더를 지정한 데스크톱에서는 그 폴더에도 같은 파일이 생긴다")을 확인할 방법이 사라진다 |
| 부트스트랩에서의 `queryPermission` (05 §5.3 "앱 시작 시") | **보관 시점 lazy 조회**로 대체 | 부트스트랩이 Chromium 전용 권한 조회에 매달릴 이유가 없고, 그 결과의 유일한 소비자(설정 배너)가 Step 13이다. `bootstrap.ts`·`bootstrap.test.ts`를 건드리지 않아 회귀 표면이 줄어든다 |
| `docs/spec-vectors/` 신규 벡터 파일 | **추가하지 않는다** | ①Windows의 충돌 접미 로직(`MakeUniqueFolder`)이 **private + 파일시스템 의존**이라 벡터로 절반밖에 못 고정한다 ②벡터 1개 추가는 `loadVector.ts`의 `EXPECTED_VECTOR_NAMES`와 `SpecVectorTests.cs`를 **함께** 고쳐야 해서 C#을 전혀 건드리지 않는 이 Step에 교차 변경을 끌어들인다 ③폴더명 형식은 이미 양쪽 테스트가 각자 고정한다. **대신** 웹 테스트에 Windows와 **같은 리터럴**(`mcphoto_260720_1445`)을 두고 `// ↔ tests/MCPhoto.Tests/LocalSaveTests.cs:33` 주석으로 짝을 명시한다 |
| E8 [기기에 저장] 다운로드 버튼 | **Step 11** | `Qr`·`Done` 화면 소유다 |
| 업로드·QR 일체 | **Step 11** | §7의 주석 자리만 남긴다 |

---

## 3. 도메인 설계

> 3파일 모두 `src/domain` 규칙을 지킨다: **도메인 내부 상대 경로만 import**, `Date.now`·`Math.random`·`crypto`·브라우저 API·`console` 0건(F11이 기계 검증).
> `Date` **값을 인자로 받는 것**은 합법이다 — `uploadContract.stampPrefix(localTime: Date)` 선례가 있다.

### 3.1 `src/domain/results/resultNaming.ts`

```ts
import type { OutputFormat } from "../settings/appSettings";

/** 결과물 세션 폴더 접두 — Windows `LocalSaveService.SessionFolderName`과 동일(analysis/41 §5). */
export const RESULT_FOLDER_PREFIX = "mcphoto_";

/** 타임랩스 파일명. 계약 고정값이라 포맷 분기가 없다. */
export const TIMELAPSE_FILE_NAME = "timelapse.mp4";

/** 충돌 접미 최대치. Windows `for (int i = 2; i < 1000; i++)`와 같다. */
export const MAX_RESULT_FOLDER_SUFFIX = 999;

/**
 * 세션 폴더명 `mcphoto_YYMMDD_HHMM` (예 `mcphoto_260720_1445`).
 * **로컬 시각 성분**으로 조립한다 — 운영자가 폴더를 시각으로 정렬·검색하기 때문이다.
 */
export function resultFolderName(localTime: Date): string;

/**
 * `sessionId`(`yyyyMMdd_HHmmss_uuid`)에서 같은 폴더명을 유도한다.
 * 형식이 어긋나면 `null`(호출자가 `resultFolderName(localTime)`으로 폴백).
 *
 * ⚠️ 이 경로가 **기본**이다: 폴더 시각이 업로드 `sessionId`와 같은 순간을 가리켜야
 *    운영자가 로컬 폴더와 서버 세션을 짝지을 수 있다. Windows도 `session.SessionTime`
 *    (촬영 시작 시각)을 쓴다.
 */
export function resultFolderNameFromSessionId(sessionId: string): string | null;

/**
 * 충돌 해석. 같은 이름이 있으면 `-2`, `-3` … `-999`, 그래도 없으면 `-{fallbackToken}`.
 * @param fallbackToken 어댑터가 주입하는 32자 hex(= `crypto.randomUUID().replace(/-/g,"")`).
 *                      도메인은 난수를 만들지 않는다(01 §8).
 */
export function resolveResultFolderName(
  base: string,
  existing: readonly string[],
  fallbackToken: string,
): string;

/** `final.jpg` 또는 `final.png`. */
export function finalFileName(format: OutputFormat): string;

/**
 * 우리 규약으로 만든 폴더명인가. **보존 정책의 삭제 후보를 이 판정으로 좁힌다** —
 * 사용자가 OPFS를 직접 만졌거나 다른 기능이 `results/` 아래에 둔 것을 지우지 않기 위해서다.
 * 패턴: `mcphoto_` + 6자리 + `_` + 4자리 + 선택적 `-{2..999}` 또는 `-{32 hex}`
 */
export function isResultFolderName(name: string): boolean;
```

**파일명 규칙 (analysis/41 §5 준수)**

| 대상 | 이름 | 조립 |
|------|------|------|
| 세션 폴더 | `mcphoto_260720_1445` | `RESULT_FOLDER_PREFIX` + `yy` + `MM` + `dd` + `_` + `HH` + `mm` (전부 0 패딩, 로컬 시각) |
| 충돌 | `mcphoto_260720_1445-2` | 위 + `-{n}` (n = 2..999) |
| 극단 충돌 | `mcphoto_260720_1445-{32 hex}` | Windows `Guid:N`과 같은 모양 |
| 사진 | `final.jpg` / `final.png` | `OutputFormat`이 `"Png"`면 `.png`, 아니면 `.jpg` |
| 타임랩스 | `timelapse.mp4` | 고정 |
| 전체 경로(①) | `results/mcphoto_260720_1445/final.jpg` | `OPFS_DIRS.results` + `/` + 폴더 + `/` + 파일 (조립은 어댑터) |

`sessionId → 폴더명` 문자열 변환 (구현 지시):
`yyyyMMdd_HHmmss_uuid`의 인덱스는 `0-3 yyyy · 4-5 MM · 6-7 dd · 8 '_' · 9-10 HH · 11-12 mm · 13-14 ss`다.
따라서 `RESULT_FOLDER_PREFIX + sessionId.slice(2, 8) + "_" + sessionId.slice(9, 13)`.
호출 전 `isValidSessionId`(`uploadContract`)로 검사한다 — 도메인 내부 상대 import라 합법이다.

### 3.2 `src/domain/results/resultSavePlan.ts`

```ts
import type { OutputFormat } from "../settings/appSettings";
import { finalImageContentType, TIMELAPSE_CONTENT_TYPE } from "../upload/uploadContract";
import { finalFileName, resolveResultFolderName, TIMELAPSE_FILE_NAME } from "./resultNaming";

export type ResultTargetKind = "final" | "timelapse";

export interface ResultSaveTarget {
  readonly kind: ResultTargetKind;
  readonly fileName: string;
  /** ② 계층 쓰기·향후 내보내기에서 쓴다. OPFS는 필요 없지만 계획을 완결시킨다. */
  readonly contentType: string;
}

/**
 * 저장 계획. **판별 유니온**이라 호출자가 `skip` 처리를 빠뜨릴 수 없다.
 */
export type ResultSavePlan =
  | { readonly kind: "skip"; readonly reason: "disabled" | "no-image" }
  | {
      readonly kind: "save";
      readonly folderName: string;
      /** 항상 `final`이 먼저, 있으면 `timelapse`가 뒤. 이 순서로 기록한다. */
      readonly targets: readonly ResultSaveTarget[];
    };

export interface ResultSavePlanInput {
  /** 설정 `SaveLocalCopy`. false면 `skip`(WBS trigger). */
  readonly saveLocalCopy: boolean;
  readonly hasFinalImage: boolean;
  /** 타임랩스가 **없는 것은 정상**이다(VF-6 · C3). */
  readonly hasTimelapse: boolean;
  readonly format: OutputFormat;
  /** 어댑터가 `resultFolderNameFromSessionId` 또는 `resultFolderName`으로 만든 값. */
  readonly baseFolderName: string;
  /** OPFS `results/`의 현재 폴더 목록. */
  readonly existingFolders: readonly string[];
  readonly fallbackToken: string;
}

export function planResultSave(input: ResultSavePlanInput): ResultSavePlan;
```

**설계 근거**
- `Blob`을 도메인에 넘기지 않고 "무엇을 쓸지"만 기술한다 — 어댑터가 `kind → Blob`을 매핑한다. 덕분에 "타임랩스 null이면 대상이 1개"라는 규칙이 브라우저 없이 검증된다.
- `saveLocalCopy` 게이트를 화면이 아니라 **이 함수**에 둔다. 화면에 `if (values.SaveLocalCopy)`를 흩뿌리면 진입점이 늘 때마다 게이트를 빠뜨린다.
- `bytes`를 입력으로 받지 않는다 — 보존 정책은 **기록 후 실측**으로 판정한다(§3.3). 예상 크기로 사전 축출하면 실제와 어긋나는 두 번째 회계가 생긴다.

### 3.3 `src/domain/results/resultsRetention.ts`

```ts
import { isResultFolderName } from "./resultNaming";

/** 05 §5.4 — 브라우저 할당량 때문에 웹에만 있는 정책(Windows는 무기한 보관). */
export const RESULTS_MAX_BYTES = 2 * 1024 * 1024 * 1024; // 2GB
export const RESULTS_MAX_SESSIONS = 200;

export interface ResultFolderUsage {
  readonly name: string;
  readonly bytes: number;
}

export interface ResultsRetentionLimits {
  readonly maxBytes: number;
  readonly maxSessions: number;
}

export interface ResultsRetentionDecision {
  /** 삭제할 폴더명(오래된 순). 비어 있으면 정리 불필요. */
  readonly remove: readonly string[];
  readonly keptCount: number;
  readonly keptBytes: number;
  /** 정리를 유발한 사유(정리 **전** 상태). 로그·진단 표시용. */
  readonly triggers: readonly ("count" | "bytes")[];
  /** 정리 후에도 한도를 넘는가(단일 세션이 2GB를 넘는 극단 상황) — 정직하게 보고한다. */
  readonly stillOverLimit: boolean;
}

export function planResultsRetention(
  folders: readonly ResultFolderUsage[],
  limits?: ResultsRetentionLimits,
): ResultsRetentionDecision;
```

**알고리즘 (구현 지시)**
1. 삭제 후보 = `folders.filter(f => isResultFolderName(f.name))`. 규약 밖 이름은 **회계에는 포함**하되 삭제하지 않는다(정직한 총량 + 남의 데이터 보호).
2. 후보를 **이름 오름차순**으로 정렬한다. `mcphoto_YYMMDD_HHMM`은 0 패딩이라 **문자열 사전순 = 시간순**이다. `localeCompare`를 쓰지 않는다(로케일·ICU 의존 금지) — `a.name < b.name ? -1 : a.name > b.name ? 1 : 0`.
3. 앞(가장 오래된 것)부터 `keptCount > maxSessions || keptBytes > maxBytes`인 동안 제거 목록에 담는다.
4. ⚠️ **가장 최신 후보는 절대 삭제하지 않는다**(루프 상한을 `sorted.length - 1`로 둔다). 방금 기록한 결과물을 지우면 M6-W가 무의미해진다. 단일 세션이 2GB를 넘어도 그 세션은 남고 `stillOverLimit: true`로 보고한다.
5. ⚠️ **정수 바이트끼리 비교**한다(함정 #3). `keptBytes / max < ratio` 같은 비율 비교 금지.

**정렬 주의(문서화할 것)**: 같은 분 안에서 `-10`이 `-2`보다 앞선다(사전순). 같은 분 안의 순서 오차라 보존 정책에 실질 영향이 없다.

### 3.4 `src/domain/index.ts` 배럴 추가 — 이름 충돌 검사

```ts
export * from "./results/resultNaming";
export * from "./results/resultSavePlan";
export * from "./results/resultsRetention";
```

기존 배럴과 대조한 결과 충돌 **없음**:

| 신규 이름 | 유사 기존 이름 | 충돌 |
|-----------|----------------|------|
| `finalFileName` | `finalImagePath`·`finalImageContentType`(uploadContract) | 없음 |
| `TIMELAPSE_FILE_NAME` | `TIMELAPSE_CONTENT_TYPE`·`TIMELAPSE_OUTPUT_FPS`·`TIMELAPSE_MIN_FRAMES`·`TIMELAPSE_SPOOL_*` | 없음 |
| `planResultSave` | `planTimelapse`·`planDecimation` | 없음 |
| `planResultsRetention` / `RESULTS_MAX_*` | — | 없음 |
| `resultFolderName`·`resolveResultFolderName`·`isResultFolderName`·`RESULT_FOLDER_PREFIX` | — | 없음 |
| `ResultSaveTarget`·`ResultSavePlan`·`ResultFolderUsage` | — | 없음 |

> 이름을 줄이지 마라(`plan`·`folderName` 등). 평면 배럴이라 짧은 이름은 다음 Step에서 충돌한다(15 §6).

---

## 4. OPFS 프로토콜 확장 — `usage`

### 4.1 왜 필요한가

`OpfsClient`에는 **크기를 알 방법이 없다**(`list`는 이름만). 2GB 한도를 지키려면 폴더별 바이트가 필요하다.
메인 스레드에서 `readFile()`를 400번 부르는 대안은 RPC 왕복이 아니라 핸들 열기 400회 + 폴더 열거 200회다.
Worker 안에서 한 번에 걷고 **왕복 1회**로 받는 편이 단순하고 빠르다. op는 `results/` 전용이 아니라
경로를 받는 **범용**이라 Step 14(프레임 캐시 용량)·Step 16(진단)이 그대로 재사용한다.

### 4.2 메시지 계약

`src/adapters/storage/opfsProtocol.ts`에 추가:

```ts
export type OpfsRequest =
  | …기존 5종…
  | { readonly id: number; readonly op: "usage"; readonly path: string };

/** `usage` 응답의 `value`. */
export interface OpfsUsageEntry {
  readonly name: string;
  readonly kind: "file" | "directory";
  /** 디렉터리는 하위 전체 합계. */
  readonly bytes: number;
  /** 디렉터리는 하위 파일 개수, 파일은 1. */
  readonly fileCount: number;
}

export interface OpfsUsage {
  readonly totalBytes: number;
  /** `path`의 **직속 자식**만. 디렉터리는 재귀 합계로 접힌다. */
  readonly entries: readonly OpfsUsageEntry[];
}

/** 재귀 walk 깊이 상한. `results/{folder}/{file}`은 2다. 방어적 상한. */
export const OPFS_USAGE_MAX_DEPTH = 8;
```

`OpfsRequestWithoutId`는 분배 조건부 타입이라 **수정 불필요**(F: `opfsProtocol.ts:22-26`).

### 4.3 Worker 핸들러 (`opfsWriter.worker.ts`)

```ts
async function usage(path: string): Promise<OpfsUsage>
```

| 규칙 | 이유 |
|------|------|
| **`createSyncAccessHandle().getSize()`를 쓰지 마라. `getFile().size`를 써라** | `SyncAccessHandle`은 파일당 **배타 잠금**이다(worker 파일 헤더 경고). 용량 조회가 잠금을 잡으면 같은 파일의 다음 쓰기가 `NoModificationAllowedError`로 실패한다 |
| 디렉터리 부재는 예외가 아니라 `{ totalBytes: 0, entries: [] }` | 첫 실행에 `results/`가 없다. 부재를 오류로 만들면 첫 세션이 실패 로그를 남긴다 |
| `OPFS_USAGE_MAX_DEPTH` 초과 하위는 걷지 않는다 | 방어. OPFS에 순환은 없지만 상한은 싸다 |
| Worker에서 `logger`를 부르지 않는다 | 함정 #12 — Worker 로그는 진단에 도달하지 않는다. 사유는 응답 `error`로 넘기고 메인이 기록한다 |
| `handle(request)`의 `switch`에 케이스를 추가한다 | `default: const never: never = request`가 있어 빠뜨리면 **컴파일 에러**다(안전망) |

### 4.4 클라이언트 (`opfsClient.ts`)

```ts
export interface OpfsClient {
  …기존 6개…
  /**
   * 경로의 용량. **실패·미지원은 빈 결과**(`{ totalBytes: 0, entries: [] }`)다 — 예외를 던지지 않는다.
   * ⚠️ 빈 결과는 "정리 불필요"로 해석된다. 즉 실패가 **삭제를 덜 하는** 방향으로 축소되므로
   *    데이터 손실이 없는 안전한 폴백이다.
   */
  usage(path: string): Promise<OpfsUsage>;
}
```

`UNSUPPORTED_OPFS_CLIENT`에 `usage: async () => ({ totalBytes: 0, entries: [] })`를 추가한다.
기존 테스트가 `{...UNSUPPORTED_OPFS_CLIENT, …}` 스프레드로 목을 만들기 때문에 이것만 추가하면 기존 26파일이 그대로 통과한다.

---

## 5. 어댑터 설계

### 5.1 `src/adapters/storage/resultsStore.ts`

```ts
import { OPFS_DIRS } from "./opfsProtocol";
import { getOpfsClient, type OpfsClient } from "./opfsClient";
import { logger } from "./logStore";
import {
  isResultFolderName,
  planResultsRetention,
  type ResultFolderUsage,
  type ResultsRetentionLimits,
} from "@domain/results/…";

export interface ResultsUsage {
  readonly totalBytes: number;
  readonly folders: readonly ResultFolderUsage[];
}

export interface ResultsStore {
  /** `results/` 직속 폴더명(오름차순 = 오래된 순). 실패는 `[]`. */
  listFolders(): Promise<string[]>;
  /** 폴더별 용량 + 총량. 실패는 `{ totalBytes: 0, folders: [] }`. */
  usage(): Promise<ResultsUsage>;
  /** 폴더 재귀 삭제. **규약 밖 이름은 거부하고 `false`**. */
  removeFolder(name: string): Promise<boolean>;
  /** 보관본 파일 읽기(Step 13 내보내기·미리보기). 메인 스레드 읽기라 Worker 불요. */
  readFile(folderName: string, fileName: string): Promise<File | null>;
  /** 보존 정책 집행. **삭제된 폴더 수**를 돌려준다(실패·불필요는 0). */
  enforceRetention(limits?: ResultsRetentionLimits): Promise<number>;
}

export function createResultsStore(client: OpfsClient): ResultsStore;
export function getResultsStore(): ResultsStore;            // getOpfsClient() 기반 싱글턴
export function setResultsStoreForTests(store: ResultsStore | null): void;
```

**설계 근거**
- `removeFolder`가 `isResultFolderName` 게이트를 통과해야 지운다. `opfsProtocol.splitOpfsPath`는 `..`를 막지만 `results` 형제 디렉터리 오지정까지 막지는 못한다 — 이름 규약이 두 번째 방어선이다.
- `enforceRetention`은 삭제 결과를 **정직하게 센다**(`purgeSessionLeftovers`가 실패를 개수에 세지 않는 것과 같은 방식 — `opfsClient.ts:185-192`).
- 로그: `logger.info("보관 결과물 정리", { removed, keptCount, keptBytes, triggers, stillOverLimit })`.
  키 이름에 `state`·`code`·`token`이 들어가지 않는지 확인했다(F14).

### 5.2 `src/adapters/storage/dirHandleRepo.ts` — ② 계층 (Chromium 데스크톱 전용)

```ts
export type DirPermissionStatus = "granted" | "prompt" | "denied" | "unsupported";

export interface DirFolderWriteResult {
  readonly ok: boolean;
  /** 실제로 만든 폴더명. ①과 다를 수 있다(충돌 해석이 위치마다 독립). */
  readonly folderName: string | null;
}

export interface DirHandleRepo {
  /** `showDirectoryPicker`·`createWritable` **런타임 감지**(함정 #2 — 타입을 믿지 않는다). */
  isSupported(): boolean;
  /** ⚠️ **사용자 제스처에서만** 호출한다. 취소·실패는 `null`. */
  pick(): Promise<FileSystemDirectoryHandle | null>;
  load(): Promise<FileSystemDirectoryHandle | null>;
  store(handle: FileSystemDirectoryHandle): Promise<boolean>;
  clear(): Promise<boolean>;
  /** 권한 **조회**만. 요청하지 않는다(제스처 불요). */
  query(handle: FileSystemDirectoryHandle): Promise<DirPermissionStatus>;
  /** 권한 **요청**. ⚠️ 사용자 버튼에서만(WBS trigger). */
  request(handle: FileSystemDirectoryHandle): Promise<DirPermissionStatus>;
  /** 폴더를 만들고 파일들을 쓴다. 실패는 `{ ok: false, folderName: null }`. */
  writeFolder(
    handle: FileSystemDirectoryHandle,
    baseFolderName: string,
    files: readonly { readonly name: string; readonly blob: Blob }[],
  ): Promise<DirFolderWriteResult>;
}

export function getDirHandleRepo(): DirHandleRepo;
export function setDirHandleRepoForTests(repo: DirHandleRepo | null): void;

export const DIR_HANDLE_DB_NAME = "mcphoto-handles";
export const DIR_HANDLE_STORE = "handles";
export const DIR_HANDLE_KEY = "localSaveDir";
```

#### ⚠️ 여기서 `createWritable()`을 메인 스레드에서 쓰는 것은 **합법이다** — 그리고 필수다

| 근거 | 내용 |
|------|------|
| VF-14의 적용 범위 | "메인 스레드에서 OPFS에 쓰면 iOS에서 전 저장이 실패한다"는 **OPFS 경로**의 규칙이다. ②의 대상은 OPFS가 아니라 사용자가 고른 디렉터리다 |
| 구조적으로 불가능 | `opfsWriter` Worker는 `navigator.storage.getDirectory()` 루트 기준 경로만 안다. 사용자 디렉터리 핸들에 **닿을 수 없다** |
| Safari에는 ② 자체가 없다 | `showDirectoryPicker`가 없어 `isSupported()`가 false다 → `createWritable` 부재 문제가 발생할 수 없다 |
| `createSyncAccessHandle`는 OPFS 전용 | 사용자 디렉터리 핸들에서는 쓸 수 없다 |

**그래도 `isSupported()`는 `showDirectoryPicker`와 `createWritable` 둘 다 런타임 감지한다**(A3 방어).
`createWritable` 감지는 `typeof FileSystemFileHandle?.prototype?.createWritable === "function"`으로 한다 —
타입 선언이 아니라 실제 프로토타입을 본다(함정 #2).

#### 별 IndexedDB를 쓰는 이유 (중요 · 회귀 방지)

로그 스토어는 DB `mcphoto` **v1**을 열고 앱 수명 내내 연결을 붙들고 있는데,
그 연결에 **`onversionchange` 핸들러가 없다**(F15). 여기서 같은 DB를 v2로 열면
업그레이드가 **영구 blocked** 되어 폴더 핸들 저장이 조용히 멈춘다.
→ 이번 Step은 **`mcphoto-handles` v1**이라는 별 DB를 쓴다. 부수 이점: 진단의 [로그 지우기]가 폴더 지정을 날리지 않는다.

> 📌 **다음 작업자에게**: `logStore.ts`가 `mcphoto`를 열 때 `db.onversionchange = () => db.close()`를
> 걸어 두지 않으면, "프레임 메타 스토어를 같은 DB에 버전 올려 추가"(`logStore.ts:169` 주석, Step 14 계획)가
> 같은 이유로 막힌다. **Step 14가 착수 전에 반드시 처리해야 한다.** 15 §6에 남길 것.

#### `writeFolder` 동작 (구현 지시)

1. `handle.keys()`로 기존 이름을 모은다. 열거가 던지면(A2) **빈 목록**으로 진행한다.
2. `resolveResultFolderName(baseFolderName, existing, fallbackToken)` — ①과 **같은 도메인 함수**를 쓴다.
   ⚠️ 결과가 ①과 다를 수 있다(위치마다 기존 이름이 다르므로). 그래서 outcome이 두 이름을 따로 보고한다.
   이름을 억지로 맞추려고 기존 폴더를 덮어쓰면 **사용자 파일이 사라진다** — 절대 하지 않는다.
3. `getDirectoryHandle(name, { create: true })` → 각 파일 `getFileHandle(name, { create: true })` → `createWritable()` → `write(blob)` → **`close()`**.
   ⚠️ `close()`를 빠뜨리면 데이터가 디스크에 도달하지 않는다. `try/finally`로 감싼다.
4. 어떤 실패도 예외를 전파하지 않고 `{ ok: false, folderName: null }`.

`query`/`request`도 런타임 감지한다 — `queryPermission`/`requestPermission`은 표준 DOM lib에 없거나
브라우저마다 있고 없다. 없으면 `"unsupported"`가 아니라 **`"granted"`로 낙관 처리하지 마라**:
없으면 `"prompt"`를 돌려 ②를 건너뛴다(조용한 실패보다 건너뛰는 편이 정직하다).

### 5.3 `src/adapters/storage/resultSaver.ts` — ①·②·③ 오케스트레이션

```ts
export type ResultSaveStatus = "saved" | "partial" | "failed" | "skipped";
export type FolderCopyStatus =
  | "unsupported"          // 브라우저에 ② 능력이 없다(Safari·Firefox·모바일)
  | "no-handle"            // 운영자가 폴더를 지정하지 않았다
  | "permission-required"  // 핸들은 있는데 권한이 granted가 아니다 → **자동 요청 금지**
  | "copied"
  | "failed";

export interface ResultSaveInput {
  /** `useResultCompose().currentBlob()`. null이면 `skip("no-image")`. */
  readonly finalBlob: Blob | null;
  readonly format: OutputFormat;
  /** `getTimelapseService().current()?.blob ?? null`. **null은 합법**(VF-6). */
  readonly timelapseBlob: Blob | null;
  /** 설정 `SaveLocalCopy`. */
  readonly saveLocalCopy: boolean;
  /** 폴더명 기준. 촬영 시작 시각을 담고 있다(F10). */
  readonly sessionId: string | null;
  /** `sessionId`가 없거나 형식이 깨졌을 때의 폴백 시각. 어댑터 경계에서 `new Date()`를 주입한다. */
  readonly localTime: Date;
  /** 충돌 999회 소진 시 접미. `crypto.randomUUID().replace(/-/g, "")`. */
  readonly fallbackToken: string;
}

export interface ResultSaveOutcome {
  readonly status: ResultSaveStatus;
  /** ① OPFS에 만든 폴더명. */
  readonly folderName: string | null;
  /** **M6-W 충족 여부**와 같다. */
  readonly finalSaved: boolean;
  readonly timelapseSaved: boolean;
  readonly hadTimelapse: boolean;
  readonly folderCopy: FolderCopyStatus;
  /** ② 폴더에 만든 이름(①과 다를 수 있다). */
  readonly folderCopyName: string | null;
  /** 보존 정책이 삭제한 폴더 수. */
  readonly evicted: number;
  readonly bytes: number;
  readonly elapsedMs: number;
}

export interface ResultSaverDeps {
  readonly opfs?: OpfsClient;
  readonly results?: ResultsStore;
  readonly dirHandles?: DirHandleRepo;
  readonly now?: () => number;
}

/** ⚠️ **절대 throw하지 않는다.** 모든 실패가 `status`로 표현된다(M4 성공 오인 금지). */
export async function saveResultLocally(
  input: ResultSaveInput,
  deps?: ResultSaverDeps,
): Promise<ResultSaveOutcome>;
```

**status 판정표**

| 조건 | status | 토스트(§7이 결정) |
|------|--------|-------------------|
| `SaveLocalCopy` off 또는 `finalBlob === null` | `skipped` | 없음 |
| `final` 기록 성공 + (타임랩스 없음 or 타임랩스도 성공) | `saved` | 없음 |
| `final` 성공 + 타임랩스 있는데 실패 | `partial` | **없음**(로그 warn만) |
| `final` 실패 | `failed` | `STRINGS.save.failed` |

> `partial`에 토스트를 띄우지 않는 근거: M6-W의 대상은 사진이고, 타임랩스 부재는 계약상 합법이며(VF-6),
> 손님이 그 메시지로 할 수 있는 조치가 없다. 운영자용 신호는 로그·진단(Step 16)에 남긴다.

**실행 순서 (구현 지시)**

```
1. base = resultFolderNameFromSessionId(sessionId) ?? resultFolderName(localTime)
2. existing = await results.listFolders()
3. plan = planResultSave({...})            → kind === "skip" 이면 즉시 skipped 반환 + logger.info
4. ① for (target of plan.targets)          ← final 먼저, timelapse 나중
      ok = await opfs.write(`${OPFS_DIRS.results}/${plan.folderName}/${target.fileName}`, blob)
   ⚠️ 반드시 OpfsClient를 통한다. 이 파일에 navigator.storage / createWritable /
      createSyncAccessHandle 문자열이 있으면 안 된다(§8.3 정적 검사가 막는다).
5. ② final 실패 여부와 **무관하게** 시도한다
      (①이 할당량으로 실패해도 ②는 성공할 수 있다 — 보관 기회를 버리지 않는다)
      isSupported() false            → "unsupported"
      load() === null                → "no-handle"
      query() !== "granted"          → "permission-required"   ★ request()를 부르지 마라
      writeFolder(...)               → "copied" | "failed"
6. ③ plan이 save였으면 evicted = await results.enforceRetention()
      ⚠️ 실패해도 status를 바꾸지 않는다(보존 정리는 보관의 성패와 무관하다)
7. logger.info/error("결과물 로컬 보관", { status, folderName, finalSaved, timelapseSaved,
      hadTimelapse, folderCopy, evicted, bytes, elapsedMs })
```

**보존 정책을 `await` 하는 이유**: fire-and-forget은 (a) 관측·테스트가 불가능하고 (b) `returnHome`의
`cleanupWorkspace`와 경합한다. 왕복 1회 + 삭제 몇 건이라 짧다. 실기기에서 300ms를 넘으면
그때 6번을 `void`로 바꾸고 `evicted`를 `-1`로 보고한다(V19-1 결과에 따른 후속).

**Blob 재사용 안전성**: `opfsClient.write()`는 `Blob`마다 `arrayBuffer()`로 새 버퍼를 만들어 transfer한다(F19).
같은 `finalBlob`을 ①과 ②에 연달아 써도 detach되지 않는다.

---

## 6. `ResultView.goNext` 배선 — 정확히 어디에 끼우는가

### 6.1 현재 코드 (`webclient/src/ui/views/FlowViews.tsx:374-388`, 원문)

```tsx
  async function goNext(): Promise<void> {
    if (finishing) return;
    setFinishing(true);
    try {
      // 03 §8.1 1단계 — 타임랩스 생성. **실패해도 계속한다**(`timelapseUrl=null`은 계약상 합법 — VF-6).
      await getTimelapseService().finish();
    } finally {
      setFinishing(false);
    }
    // 대기 중 홈 복귀·유휴 만료가 일어났을 수 있다 — 그때는 전이하지 않는다.
    if (currentScreen() !== "Result") return;
    // TempUser 한도는 Step 11에서 qr-usage 조회로 채운다(지금은 미차단).
    const qrOn = isQrEffectivelyEnabled(rawEnableQr, user !== null, false);
    shellStore.getState().go(qrOn ? "Qr" : "Done");
  }
```

**끼워 넣을 위치**: `if (currentScreen() !== "Result") return;` **다음 줄**,
`const qrOn = …` **이전**. 즉 홈 복귀 가드는 유지하고, 그 뒤·QR 판정 앞이 보관 자리다.
(Step 11의 업로드는 보관과 `const qrOn` 사이에 들어간다.)

또 하나 바뀌는 것: `setFinishing(false)`가 **보관까지 끝난 뒤**여야 한다.
현재는 `finish()`만 감싸고 있어서, 보관 중에 [다음] 버튼이 다시 눌릴 수 있다.
`try/finally`의 범위를 전체로 넓힌다.

### 6.2 교체 후 (`ResultView`)

```tsx
  async function goNext(): Promise<void> {
    if (finishing) return;
    setFinishing(true);
    try {
      // 순서 전체(타임랩스 → 로컬 보관 → 전이)를 resultNext가 소유한다.
      // 여기서 순서를 다시 조립하지 마라 — M6-W는 resultNext.test.ts가 고정한다.
      await runResultNext(defaultResultNextDeps({ finalBlob: result.currentBlob }));
    } finally {
      // ⚠️ 보관까지 끝난 뒤에 푼다. finish()만 감싸면 보관 중 이중 클릭이 들어온다.
      setFinishing(false);
    }
  }
```

`FlowViews.tsx`에서 **제거**되는 import: `getTimelapseService`, `isQrEffectivelyEnabled`, `currentScreen`
(모두 `resultNext.ts`로 이동). `rawEnableQr`·`user` 셀렉터도 `ResultView`에서 뺀다 — `resultNext`가
`currentSettings()`·`sessionStore`에서 직접 읽는다.
⚠️ `useSettingsStore`·`useSessionStore` 자체는 다른 뷰가 쓰므로 파일 상단 import를 통째로 지우지 마라.

### 6.3 `src/screens/result/resultNext.ts`

```ts
export interface ResultNextDeps {
  readonly finishTimelapse: () => Promise<TimelapseResult | null>;
  readonly currentTimelapse: () => TimelapseResult | null;
  readonly finalBlob: () => Blob | null;
  readonly save: (input: ResultSaveInput) => Promise<ResultSaveOutcome>;
  readonly settings: () => AppSettingsValues;
  readonly sessionId: () => string | null;
  readonly isLoggedIn: () => boolean;
  /** Step 11이 `qrUsageService`로 채운다. 지금은 항상 false. */
  readonly isTempUserBlocked: () => boolean;
  /** `currentScreen() === "Result"`. */
  readonly stillOnResult: () => boolean;
  readonly go: (to: AppState) => void;
  readonly toast: (kind: ToastKind, message: string) => void;
  readonly now: () => Date;
  readonly uuid: () => string;
}

export interface ResultNextOutcome {
  /** 홈 복귀·유휴 만료로 중단됐는가. true면 보관·전이 모두 하지 않았다. */
  readonly aborted: boolean;
  readonly save: ResultSaveOutcome | null;
  readonly destination: "Qr" | "Done" | null;
}

export async function runResultNext(deps: ResultNextDeps): Promise<ResultNextOutcome>;

/** 실제 배선. 인자로 덮어쓸 수 있게 열어 둔다(테스트·Step 11 확장). */
export function defaultResultNextDeps(
  overrides: Partial<ResultNextDeps> & { readonly finalBlob: () => Blob | null },
): ResultNextDeps;
```

본문 순서:

```
1. await deps.finishTimelapse()                     // 실패해도 계속 (VF-6)
2. if (!deps.stillOnResult()) return { aborted: true, save: null, destination: null }
3. const timelapse = deps.currentTimelapse()        // ⚠️ 반드시 여기서 소비한다
                                                    //    stop()이 폐기하므로 [다음] 밖에선 못 읽는다
4. const outcome = await deps.save({
     finalBlob: deps.finalBlob(),
     format: deps.settings().OutputFormat,
     timelapseBlob: timelapse?.blob ?? null,        // ★ 타임랩스 null 분기는 여기 한 곳
     saveLocalCopy: deps.settings().SaveLocalCopy,
     sessionId: deps.sessionId(),
     localTime: deps.now(),
     fallbackToken: deps.uuid().replace(/-/g, ""),
   })
5. if (outcome.status === "failed") deps.toast("error", STRINGS.save.failed)
   // partial·permission-required는 토스트 없음(§5.3 판정표)
6. ┌─────────────────────────────────────────────────────────────────┐
   │ Step 11: 업로드 3단계(prepare → 서명 PUT → commit)가 **여기** 들어간다. │
   │ 보관(4)보다 **뒤**, 전이(8)보다 **앞**이어야 한다 — M6-W.             │
   └─────────────────────────────────────────────────────────────────┘
7. if (!deps.stillOnResult()) return { aborted: true, save: outcome, destination: null }
8. const qrOn = isQrEffectivelyEnabled(settings.EnableQrDelivery, deps.isLoggedIn(), deps.isTempUserBlocked())
   const destination = qrOn ? "Qr" : "Done"
   deps.go(destination)
   return { aborted: false, save: outcome, destination }
```

`defaultResultNextDeps`의 배선:

| dep | 실제 값 |
|-----|---------|
| `finishTimelapse` | `() => getTimelapseService().finish()` |
| `currentTimelapse` | `() => getTimelapseService().current()` |
| `save` | `(input) => saveResultLocally(input)` |
| `settings` | `currentSettings` (`@shell/settingsStore`) |
| `sessionId` | `() => sessionStore.getState().sessionId` |
| `isLoggedIn` | `() => sessionStore.getState().currentUser !== null` |
| `isTempUserBlocked` | `() => false` — **Step 11이 `qrUsageService`로 교체**한다 |
| `stillOnResult` | `() => currentScreen() === "Result"` |
| `go` | `(to) => { shellStore.getState().go(to); }` |
| `toast` | `(kind, msg) => shellStore.getState().toast(kind, msg)` |
| `now` / `uuid` | `() => new Date()` / `() => crypto.randomUUID()` |

⚠️ 전부 **호출 시점에 싱글턴을 해석하는 클로저**로 쓴다. 모듈 로드 시 `getTimelapseService()`를
호출하면 node 테스트가 인코더 Worker를 붙잡는다.

### 6.4 타임랩스 `null` 분기 — 처리 지점 정리

| 지점 | 동작 |
|------|------|
| `finish()` 반환 | `null`이어도 예외가 아니다. 로그는 `timelapseEncoder`가 이미 남긴다 |
| `resultNext` 3~4 | `timelapse?.blob ?? null`로 그대로 넘긴다. **분기·경고 없음** |
| `planResultSave` | `hasTimelapse: false` → `targets`가 `final` 1개 |
| `resultSaver` | `hadTimelapse: false`, `timelapseSaved: false`. 이것은 **실패가 아니다** — status는 `saved` |
| UI | 아무 표시도 하지 않는다. 손님에게 "영상 없음"을 알릴 규격이 없다(C3의 안내는 `Guide` 화면 몫) |

### 6.5 저장 실패 UX·상태 표현

| 상황 | 사용자에게 | 로그 | 전이 |
|------|-----------|------|------|
| `SaveLocalCopy` off | 없음 | `info` | 정상 |
| OPFS 미지원(`capability === "none"`) | 실패 토스트 `STRINGS.save.failed` | `error` (부트스트랩이 이미 `warn` 1회) | **정상 진행** |
| final 쓰기 실패(할당량 등) | 실패 토스트 | `error` | **정상 진행** |
| 타임랩스만 실패 | 없음 | `warn` | 정상 |
| ② 폴더 권한 상실 | 없음(손님 화면에 운영자 메시지를 띄우지 않는다) | `warn` + Step 13 설정 배너 | 정상 |
| ② 미지원 브라우저 | 없음 | `info` 1회 | 정상 |

**어떤 경우에도 화면을 멈추지 않는다.** 키오스크에서 손님이 갇히는 것이 최악의 실패다.

---

## 7. 임시 진입점 — [로컬 저장 폴더 선택] (Step 13 이관 예정)

`src/App.tsx`의 `DummyScreen`에 추가한다. Step 6이 [카메라 테스트]로 만든 선례를 그대로 따른다(F17).

```tsx
{/* Step 10 ② 계층 실측용 진입점. Step 13에서 설정 화면의 [로컬 저장 폴더 선택]으로 옮긴다. */}
{screen === "Settings" && getDirHandleRepo().isSupported() && (
  <Button onClick={() => void pickLocalSaveFolder()}>로컬 저장 폴더 선택</Button>
)}
```

`pickLocalSaveFolder()` (App.tsx 모듈 스코프의 작은 헬퍼):
1. `const handle = await getDirHandleRepo().pick()` — 취소면 `null`, 조용히 종료
2. `await getDirHandleRepo().store(handle)` — 실패면 `toast("error", STRINGS.save.failed)`
3. `useSettingsStore.getState().save({ LocalSavePath: handle.name }, { isGuest: false })`
   ⚠️ `LocalSavePath`에는 **폴더 이름만** 들어간다(브라우저가 실 경로를 노출하지 않는다 — 05 §5.3 · D6).
   `LocalSavePath`는 `GUEST_LOCKED_KEYS`에 없다.
4. 성공 시 `toast("success", STRINGS.save.succeeded)`

**미지원 브라우저에서 버튼이 렌더되지 않는 것**이 WBS 완료 기준의 non-goal 항목이다 — `isSupported()` 게이트가 그것이다.

---

## 8. 테스트 전략

### 8.1 도메인 순수함수 — `tests/unit/domain/results.test.ts` (node)

| 대상 | 케이스 |
|------|--------|
| `resultFolderName` | **`new Date(2026, 6, 20, 14, 45, 0)` → `"mcphoto_260720_1445"`** — `// ↔ tests/MCPhoto.Tests/LocalSaveTests.cs:33`(Windows와 같은 리터럴) · 0 패딩(`2026-01-02 03:04` → `mcphoto_260102_0304`) · 연도 전환(`2030-12-31 23:59`) |
| `resultFolderNameFromSessionId` | 정상 · 형식 위반 → `null` · 빈 문자열 → `null` · **`newSessionId(date, uuid)` 결과를 넣으면 `resultFolderName(date)`와 같다**(두 경로의 정합 고정) |
| `resolveResultFolderName` | 충돌 없음 → base · base 있음 → `-2` · `-2`까지 있음 → `-3` · 2..999 전부 있음 → `-{fallbackToken}` · 빈 목록 |
| `finalFileName` | `"Jpg"` → `final.jpg` · `"Png"` → `final.png` |
| `isResultFolderName` | 정상 · `-2` 접미 · 32 hex 접미 · `frames`·`sessions`·`..`·빈 문자열 거부 · `mcphoto_1_1` 같은 자릿수 위반 거부 |
| `planResultSave` | `saveLocalCopy:false` → `skip("disabled")` · `hasFinalImage:false` → `skip("no-image")` · 타임랩스 없음 → `targets.length === 1` · 있음 → `2`이고 **`final`이 앞** · `Png` → `final.png`·`image/png` · 기존 폴더 충돌 시 폴더명에 `-2` |
| `planResultsRetention` | 한도 이하 → `remove: []`, `triggers: []` · 201개 → 가장 오래된 1개 · 2GB 초과 → 바이트 기준 축출 · 두 조건 동시 → `triggers` 2개 · **최신 1개는 절대 삭제 후보가 아니다**(3GB짜리 단일 폴더 → `remove: []`, `stillOverLimit: true`) · 규약 밖 이름은 삭제 후보에서 제외되지만 `keptBytes`에는 포함 · **문자열 정렬 = 시간 정렬** 검증(`sort()` 결과가 입력 시각 순서와 일치) |

**커버리지**: `vitest.config.ts`가 `src/domain`에 95/95/95/90을 강제한다(F13).
신규 도메인 3파일은 분기까지 채워야 한다 — `npx vitest run --coverage`로 확인한다.

### 8.2 Worker 통신 목 전략 — 2계층으로 나눈다

이 저장소는 이미 두 층을 다르게 목한다. 그 구조를 그대로 따른다.

| 층 | 대상 | 목 방식 | 왜 |
|----|------|---------|-----|
| **하위: Worker RPC 자체** | `opfsClient` ↔ `opfsWriter` 메시지 계약 | `opfs.test.ts`의 **`FakeWorker`**(요청을 기록하고 지정 응답을 `queueMicrotask`로 반환) | 이미 있고 id 짝짓기·타임아웃·transfer까지 고정한다. **`usage` 케이스만 여기에 추가**한다 |
| **상위: 보관 로직** | `resultSaver`·`resultsStore` | **`OpfsClient` 인터페이스에 목 객체 주입**(`{ ...UNSUPPORTED_OPFS_CLIENT, async write(...) {...} }`) | Worker 세부는 하위 층이 이미 검증했다. 여기서 또 Worker를 흉내 내면 같은 것을 두 번 테스트하면서 실패 지점만 흐려진다 (`purgeSessionLeftovers` 테스트가 쓰는 방식과 동일) |

`opfs.test.ts`에 추가할 `usage` 케이스:
- `client.usage("results")`가 `{ op: "usage", path: "results" }`를 보낸다
- Worker가 `ok:true, value:{totalBytes, entries}`면 그대로 돌려준다
- `ok:false`면 `{ totalBytes: 0, entries: [] }`로 축소된다(예외 없음)
- `UNSUPPORTED_OPFS_CLIENT.usage()`가 빈 결과다
- **엔트리 400개짜리 응답을 그대로 통과시킨다**(A1 규모 확인)

### 8.3 어댑터 — `tests/unit/storage/resultSaver.test.ts` (node)

**동작 테스트**
- 기록 경로가 `results/{folder}/final.jpg`·`results/{folder}/timelapse.mp4`이고 **final이 먼저** 호출된다(호출 로그로 순서 단언)
- 타임랩스 `null` → `write` 1회, `status: "saved"`, `hadTimelapse: false`
- `write`가 final에서 false → `status: "failed"`, `finalSaved: false`, **throw 없음**
- `write`가 타임랩스에서만 false → `status: "partial"`
- `saveLocalCopy: false` → `write` 0회, `status: "skipped"`, `listFolders`도 부르지 않는다
- `finalBlob: null` → `skipped`
- 기존 폴더에 같은 이름이 있으면 경로에 `-2`가 붙는다
- ② `isSupported() === false` → `folderCopy: "unsupported"`, `pick`·`load` 미호출
- ② `query()`가 `"prompt"` → `folderCopy: "permission-required"`이고 **`request()`가 호출되지 않는다**(WBS trigger)
- ② `writeFolder`가 ①과 다른 폴더명을 돌려주면 `folderCopyName`에 그 값이 담긴다
- ③ 200세션 초과 상황을 `usage` 목으로 만들면 `removeFolder`가 오래된 것부터 호출되고 `evicted`가 맞다
- ③ `enforceRetention`이 던져도 `status`가 바뀌지 않는다
- `resultsStore.removeFolder("frames")` → **false**이고 `client.remove`가 호출되지 않는다

**정적 불변식 테스트** (15 §3.4의 관례 — 문서에만 있으면 언젠가 깨진다)

```ts
// src/adapters/storage/ 소스를 읽어 검사한다.
```

| 불변식 | 검사 |
|--------|------|
| **`resultSaver.ts`·`resultsStore.ts`는 메인 스레드에서 OPFS를 만지지 않는다**(VF-14) | 두 파일 소스에 `navigator.storage`·`createWritable`·`createSyncAccessHandle`·`getDirectory` 문자열 **0건** |
| `resultSaver.ts`는 `OpfsClient`를 통해서만 쓴다 | 소스에 `getOpfsClient` 또는 `OpfsClient` import가 있고, `opfsWriter.worker` 직접 import는 0건 |
| **`dirHandleRepo.ts`는 OPFS를 건드리지 않는다** | 소스에 `navigator.storage`·`getDirectory`·`OPFS_DIRS` **0건**. ⚠️ 이 파일만 `createWritable`이 **허용**된다(§5.2) — 위 검사 대상에서 명시적으로 제외하고, 그 이유를 테스트 주석에 남긴다 |
| 로그 DB와 핸들 DB가 다르다 | `DIR_HANDLE_DB_NAME !== LOG_DB_NAME` |
| `console.*` 0건 | 신규 3파일 소스 검사 |
| `opfsWriter.worker.ts`의 기존 불변식 유지 | `usage` 추가 후에도 `timelapseService.test.ts`의 "encode.worker는 OPFS를 읽기만 한다" 검사에 영향이 없는지 확인(대상 파일이 달라 영향 없음 — 회귀 확인용) |

### 8.4 순서 불변식 — `tests/unit/screens/resultNext.test.ts` (node)

`runResultNext`에 **호출 로그를 기록하는 가짜 deps**를 넣고 순서를 단언한다.

| 케이스 | 단언 |
|--------|------|
| **정상 완주** | 호출 순서가 정확히 `["finishTimelapse", "save", "go"]` — **`save`가 `go`보다 앞**(M6-W). Step 11이 업로드를 끼워도 이 상대 순서가 유지돼야 한다 |
| 타임랩스 `null` | `save`에 넘어간 `timelapseBlob === null`, `destination`이 정상 결정된다 |
| `finish()` 중 홈 복귀 | `stillOnResult()` false → `save` **미호출**, `go` 미호출, `aborted: true` |
| 보관 중 홈 복귀 | `save`는 호출됐고 `go`는 미호출, `aborted: true` |
| `status: "failed"` | `toast("error", STRINGS.save.failed)` 1회 + **`go`는 여전히 호출된다**(흐름 중단 금지) |
| `status: "partial"` | `toast` **0회** |
| `SaveLocalCopy` off | `save`는 호출되지만(내부에서 skip 판정) `toast` 0회 · 또는 deps 설계에 맞춰 `status: "skipped"` 경로 단언 |
| QR 분기 | 로그인 + `EnableQrDelivery:true` → `"Qr"` / 게스트 → `"Done"`(VF-11) / `isTempUserBlocked:true` → `"Done"` |
| `finish()`가 throw | `runResultNext`가 throw하지 않고 보관·전이를 계속한다(이중 방어) |

### 8.5 기존 645건 스위트와의 통합 지점 · 회귀 위험

| 기존 파일 | 접촉 | 위험 | 완화 |
|-----------|------|------|------|
| `tests/unit/storage/opfs.test.ts` | **수정** — `usage` 케이스 추가 | 낮음. 기존 케이스는 그대로 | 추가만 하고 기존 단언을 고치지 않는다 |
| `tests/unit/domain/purity.test.ts` | **자동** — 신규 도메인 3파일이 glob에 잡힌다 | 중간. `Date.now()`·`crypto.` 등을 도메인에 쓰면 즉시 실패 | `Date`·난수는 **전부 인자**로 받는 설계(§3) |
| `tests/unit/domain/vectors.test.ts` | 없음 | — | 벡터 파일을 추가하지 않으므로 `EXPECTED_VECTOR_NAMES` 불변(§2.4) |
| `tests/golden/golden.test.ts` | 없음 | — | 합성 픽셀 무변경 |
| `tests/unit/shell/shell.test.ts` | 없음 | 낮음 | `shellStore`·훅 구조 무변경. `returnHome` 6단계 순서 건드리지 않는다 |
| `tests/unit/shell/bootstrap.test.ts` | 없음 | 낮음 | `bootstrap.ts`를 **의도적으로 건드리지 않는다**(§2.4) |
| `tests/unit/encode/timelapseService.test.ts` | 없음 | 낮음 | `TimelapseService` 인터페이스 무변경(`current()`를 소비만 한다) |
| `tests/unit/storage/settingsRepo.test.ts` | 없음 | 낮음 | `LocalSavePath`는 기존 키다. 모델 변경 없음 |
| `src/ui/strings.ts` | **무변경** | — | `STRINGS.save.failed`·`save.succeeded`를 재사용(F16). E22 문구 일치 테스트에 영향 없음 |
| `src/App.tsx` | 수정 | 낮음 | `DummyScreen`에 조건부 버튼 1개. 라우팅·전이 로직 무변경 |
| `src/ui/views/FlowViews.tsx` | 수정 | **중간** | `ResultView`만 바꾼다. 다른 뷰가 쓰는 import를 통째로 지우지 않도록 `tsc --noEmit`의 `noUnusedLocals`가 잡아 준다 |

**예상 테스트 증가**: 645 → **약 720~740**(도메인 ~45, 어댑터 ~25, 순서 ~12, opfs 추가 ~5).
정확한 수치는 구현 후 실측해 `11-wbs.md` Step 10 체크박스에 적는다(15 §5).

---

## 9. 구현 단계 (WBS 블루프린트 형식)

> 작업 디렉터리는 모든 단계에서 **`E:\Study\photobooth\webclient`** 다.
> 전 단계 공통 최종 게이트: `npx tsc --noEmit && npx vitest run` (기존 645건 + 신규 전부 녹색).

### Step 10-1: 도메인 3파일 + 배럴 + 도메인 테스트
- **Context Brief**: 웹 클라이언트가 촬영 결과물을 기기에 영구 보관한다(M6-W). 이 단계는 그 **순수 판정부**만 만든다 — 폴더·파일 이름 규칙(Windows `LocalSaveService`와 같은 값), 무엇을 저장할지 계획, 용량 초과 시 무엇을 지울지. 브라우저 API는 **하나도** 쓰지 않는다. `src/domain`은 아무것도 import하지 않고(도메인 내부 상대 경로만) `Date.now`·`Math.random`·`crypto`·`console`을 부르지 않으며, `tests/unit/domain/purity.test.ts`가 이를 glob으로 자동 검사한다.
- **대상 파일**: `src/domain/results/resultNaming.ts`(신규) · `src/domain/results/resultSavePlan.ts`(신규) · `src/domain/results/resultsRetention.ts`(신규) · `src/domain/index.ts`(3줄 추가) · `tests/unit/domain/results.test.ts`(신규)
- **선행 조건**: 없음
- **구현 내용**: 설계 §3.1~3.4의 시그니처·규칙 그대로. 폴더명 `mcphoto_YYMMDD_HHMM`, 충돌 `-2`…`-999` 후 32 hex 폴백, `final.{jpg|png}`·`timelapse.mp4`, 저장 계획은 `skip`/`save` 판별 유니온, 보존 정책은 이름 오름차순으로 오래된 것부터 축출하되 **최신 1개는 절대 삭제하지 않는다**. 테스트는 §8.1 표 전량.
- **검증 명령**: `npx vitest run tests/unit/domain/results.test.ts tests/unit/domain/purity.test.ts` · `npx vitest run --coverage` (src/domain 95/95/95/90) · `npx tsc --noEmit`
- **완료 기준**:
  - [관측] `resultFolderName(new Date(2026,6,20,14,45,0)) === "mcphoto_260720_1445"`(Windows `LocalSaveTests.cs:33`과 같은 리터럴)이고, 3GB짜리 폴더 1개만 있을 때 `planResultsRetention`의 `remove`가 **빈 배열**이며 `stillOverLimit: true`다.
  - [non-goal] `purity.test.ts`가 신규 3파일에 대해서도 통과한다(브라우저 API·시각·난수 0건). `docs/spec-vectors/`와 `EXPECTED_VECTOR_NAMES`는 **변경되지 않는다**. 기존 645건 전부 그대로 통과한다.
  - [trigger] 도메인 함수는 인자만으로 결과가 결정된다 — 같은 입력에 항상 같은 출력(시각·난수 주입).
- **롤백**: `src/domain/results/` 디렉터리와 `tests/unit/domain/results.test.ts` 삭제 + `domain/index.ts`의 3줄 제거.
- [ ] 완료

### Step 10-2: OPFS `usage` 프로토콜 확장
- **Context Brief**: 보관본 용량 한도(2GB)를 지키려면 OPFS 폴더별 바이트가 필요한데 현재 `OpfsClient`에는 크기를 얻을 방법이 없다(`list`는 이름만). OPFS **쓰기·열거는 전용 Worker(`opfsWriter.worker.ts`)를 반드시 지나야 하고**(Safari 17에 `createWritable`이 없고 `createSyncAccessHandle`은 Worker 전용 — 메인에서 쓰면 iOS 전 저장이 실패한다), 읽기만 메인에서 한다. 이 단계는 경로를 받아 직속 자식별 용량을 한 번에 돌려주는 **읽기 전용** op를 추가한다.
- **대상 파일**: `src/adapters/storage/opfsProtocol.ts` · `src/adapters/storage/opfsWriter.worker.ts` · `src/adapters/storage/opfsClient.ts` · `tests/unit/storage/opfs.test.ts`
- **선행 조건**: 없음(10-1과 병렬 가능)
- **구현 내용**: 설계 §4. `usage` 요청 변형 + `OpfsUsage`/`OpfsUsageEntry`/`OPFS_USAGE_MAX_DEPTH` 타입, Worker 재귀 walk(**`getFile().size`만 쓴다. `createSyncAccessHandle().getSize()`는 배타 잠금을 잡으므로 금지**), 디렉터리 부재는 빈 결과, 클라이언트 `usage()`와 `UNSUPPORTED_OPFS_CLIENT.usage()`. 테스트는 §8.2.
- **검증 명령**: `npx vitest run tests/unit/storage/opfs.test.ts` · `npx tsc --noEmit` · `npx vitest run`(전체 회귀)
- **완료 기준**:
  - [관측] `FakeWorker`가 `{op:"usage", path:"results"}`를 받고, 400 엔트리 응답이 그대로 통과하며, `ok:false` 응답이 `{totalBytes:0, entries:[]}`로 축소된다.
  - [non-goal] `opfsWriter.worker.ts`에 새 쓰기 경로가 생기지 않는다(`usage` 핸들러 안에 `createSyncAccessHandle`·`createWritable`·`removeEntry` 0건). 기존 `write`/`remove`/`list`/`exists`/`probe` 테스트가 전부 그대로 통과한다.
  - [trigger] `usage`는 호출될 때만 걷는다 — Worker 로드·`probe` 시점에 자동 실행되지 않는다.
- **롤백**: 세 파일의 `usage` 관련 추가분만 되돌린다(다른 op와 독립).
- [ ] 완료

### Step 10-3: `resultsStore` — `results/` 라이브러리 + 보존 정책 집행
- **Context Brief**: OPFS `results/`는 촬영 결과물의 영구 보관 위치다(`sessions/`와 달리 앱 시작 시 잔재 정리 대상이 **아니다**). 이 단계는 목록·용량·삭제·읽기를 한 어댑터로 모으고, Step 10-1의 순수 보존 정책을 실제 삭제로 옮긴다. Step 13의 [보관된 결과물] 패널과 Step 16 진단이 이 인터페이스를 그대로 쓴다. 어댑터는 **예외를 전파하지 않고** `false`/빈 값을 돌려준다.
- **대상 파일**: `src/adapters/storage/resultsStore.ts`(신규) · `tests/unit/storage/resultSaver.test.ts`(신규, 이 단계 분량만)
- **선행 조건**: Step 10-1(`planResultsRetention`·`isResultFolderName`), Step 10-2(`OpfsClient.usage`)
- **구현 내용**: 설계 §5.1. `listFolders`/`usage`/`removeFolder`/`readFile`/`enforceRetention` + `createResultsStore`·`getResultsStore`·`setResultsStoreForTests`. `removeFolder`는 `isResultFolderName` 게이트를 통과한 이름만 지운다. 로그 키는 `state`·`code`·`token`·`pin`·`nonce`를 쓰지 않는다(마스킹 함정 #1).
- **검증 명령**: `npx vitest run tests/unit/storage/resultSaver.test.ts` · `npx tsc --noEmit`
- **완료 기준**:
  - [관측] `usage` 목이 201개 폴더를 돌려주면 `enforceRetention()`이 가장 오래된 1개에 대해서만 `client.remove`를 부르고 `1`을 반환한다.
  - [non-goal] `removeFolder("frames")`·`removeFolder("sessions")`·`removeFolder("../x")`가 전부 `false`이고 `client.remove`가 **호출되지 않는다**. `sessions/`·`frames/` 경로 문자열이 이 파일에 등장하지 않는다.
  - [trigger] 삭제는 `enforceRetention()`·`removeFolder()` 호출 시에만 — 모듈 로드나 `usage()` 조회로는 아무것도 지워지지 않는다.
- **롤백**: `resultsStore.ts` 삭제 + 해당 테스트 블록 제거.
- [ ] 완료

### Step 10-4: `dirHandleRepo` — ② 실제 폴더 계층 (Chromium 데스크톱 전용)
- **Context Brief**: 데스크톱 Chromium에서는 운영자가 폴더를 1회 지정하면 결과물이 그 폴더에도 그대로 생겨 Windows 앱과 동등해진다(05 §5.3 · C1). `showDirectoryPicker`와 "폴더 핸들의 IndexedDB 영속"은 **둘 다 Chromium에만** 있으므로 기능 감지 하나로 이 계층 전체를 켜고 끈다. ⚠️ **이 파일에서만 메인 스레드 `createWritable()`이 허용된다** — 대상이 OPFS가 아니라 사용자 디렉터리이고, `opfsWriter` Worker는 그 핸들에 닿을 수조차 없기 때문이다. 반대로 이 파일은 **OPFS를 절대 건드리지 않는다**.
- **대상 파일**: `src/adapters/storage/dirHandleRepo.ts`(신규) · `tests/unit/storage/resultSaver.test.ts`(이 단계 분량 추가)
- **선행 조건**: Step 10-1(`resolveResultFolderName`)
- **구현 내용**: 설계 §5.2. `isSupported`(`showDirectoryPicker` **와** `FileSystemFileHandle.prototype.createWritable`을 각각 런타임 감지) · `pick`/`load`/`store`/`clear` · `query`/`request`(권한 API 부재 시 `"prompt"`) · `writeFolder`(기존 이름 열거 → `resolveResultFolderName` → `create:true` → `createWritable` → `write` → **`try/finally`로 `close()`**). IndexedDB는 **별 DB `mcphoto-handles` v1**을 쓴다(로그 DB `mcphoto` v1 연결에 `onversionchange`가 없어 같은 DB 버전 업그레이드는 영구 blocked 된다). 모든 실패는 `null`/`false`.
- **검증 명령**: `npx vitest run tests/unit/storage/resultSaver.test.ts` · `npx tsc --noEmit` · `npx vitest run`
- **완료 기준**:
  - [관측] `showDirectoryPicker`가 없는 환경에서 `isSupported()`가 `false`이고 `pick()`이 `null`을 돌려주며 **예외가 나지 않는다**. 핸들 열거가 던지는 목에서도 `writeFolder`가 base 이름으로 진행해 `ok:true`를 돌려준다.
  - [non-goal] 이 파일 소스에 `navigator.storage`·`getDirectory`·`OPFS_DIRS`가 **0건**이다(OPFS 미접촉). `DIR_HANDLE_DB_NAME !== LOG_DB_NAME`이다. 기존 로그 테스트(`logStore.test.ts`)가 그대로 통과한다.
  - [trigger] `pick()`·`request()`는 **사용자 제스처에서만** 호출된다 — 모듈 로드·`load()`·`query()`가 이들을 부르지 않는다(호출 로그로 단언).
- **롤백**: `dirHandleRepo.ts` 삭제 + 해당 테스트 블록 제거(② 없이 ①만 동작한다).
- [ ] 완료

### Step 10-5: `resultSaver` — ①·②·③ 오케스트레이션 + 정적 불변식
- **Context Brief**: M6-W의 본체다. 합성 결과 사진(과 있으면 타임랩스)을 **업로드 이전에** OPFS `results/mcphoto_YYMMDD_HHMM/`에 기록하고(필수), 폴더 핸들이 있으면 그 폴더에도 복사한 뒤(부가), 용량 정책을 집행한다. 실패는 예외가 아니라 **status**로 표현한다 — 저장 실패를 성공으로 오인시키면 안 되고(M4), 동시에 촬영 흐름을 멈춰서도 안 된다(키오스크에서 손님이 갇힌다).
- **대상 파일**: `src/adapters/storage/resultSaver.ts`(신규) · `tests/unit/storage/resultSaver.test.ts`(동작 + 정적 불변식 추가)
- **선행 조건**: Step 10-1, 10-3, 10-4
- **구현 내용**: 설계 §5.3의 타입·판정표·실행 순서 그대로. 최상단 `try/catch`로 **어떤 경우에도 throw하지 않는다**. ②는 ① 실패와 무관하게 시도하되 `query() !== "granted"`면 **`request()`를 부르지 않는다**. §8.3의 정적 불변식 테스트(메인 스레드 OPFS 접촉 0건, `dirHandleRepo.ts`만 `createWritable` 허용 — 이유를 테스트 주석에 남긴다)를 함께 작성한다.
- **검증 명령**: `npx vitest run tests/unit/storage/resultSaver.test.ts` · `npx tsc --noEmit` · `npx vitest run`
- **완료 기준**:
  - [관측] 목 `OpfsClient`에 대해 `write`가 `results/{folder}/final.jpg` → `results/{folder}/timelapse.mp4` **순서로** 호출되고, final만 실패시키면 `status: "failed"`·`finalSaved: false`이며 **함수가 throw하지 않는다**. 타임랩스를 `null`로 주면 `write` 1회 + `status: "saved"`다.
  - [non-goal] `resultSaver.ts`·`resultsStore.ts` 소스에 `navigator.storage`·`createWritable`·`createSyncAccessHandle`·`getDirectory` 문자열이 **0건**이다(VF-14). `query()`가 `"prompt"`일 때 `request()` 호출 **0회**다. `enforceRetention`이 던져도 `status`가 바뀌지 않는다.
  - [trigger] 보관은 `saveLocalCopy: true` **이고** `finalBlob !== null`일 때만 일어난다 — 둘 중 하나라도 아니면 `write` 0회, `status: "skipped"`.
- **롤백**: `resultSaver.ts` 삭제(호출자가 아직 없으므로 독립).
- [ ] 완료

### Step 10-6: `resultNext` 배선 — [다음] 순서 고정
- **Context Brief**: 결과 화면 [다음]의 처리 순서가 규격이다(03 §8.1): **타임랩스 마무리 → 로컬 보관 → (Step 11 업로드) → QR/Done 전이**. 타임랩스 결과는 `sessionStore`에 없고 `getTimelapseService().current()`로만 읽으며 홈 복귀(`stopEncoder`)에서 폐기되므로 **[다음] 처리 안에서** 소비해야 한다. 현재 `ResultView.goNext`는 `finish()` → 화면 가드 → QR 판정 → 전이 3단계뿐이다. 순서를 React 밖으로 빼내 node에서 검증 가능하게 만든다.
- **대상 파일**: `src/screens/result/resultNext.ts`(신규) · `src/ui/views/FlowViews.tsx`(`ResultView.goNext`만) · `tests/unit/screens/resultNext.test.ts`(신규)
- **선행 조건**: Step 10-5
- **구현 내용**: 설계 §6.3의 `ResultNextDeps`·`runResultNext`·`defaultResultNextDeps`. 홈 복귀 가드는 **보관 앞뒤 두 번** 검사한다. `FlowViews.tsx`는 §6.2대로 교체하고 `setFinishing(false)`가 **보관까지 끝난 뒤**에 오도록 `try/finally` 범위를 넓힌다. Step 11의 업로드 자리에 §6.3의 주석 블록을 남긴다. `defaultResultNextDeps`의 모든 dep은 **호출 시점에 싱글턴을 해석하는 클로저**여야 한다.
- **검증 명령**: `npx vitest run tests/unit/screens/resultNext.test.ts` · `npx tsc --noEmit` · `npx vitest run`
- **완료 기준**:
  - [관측] 가짜 deps의 호출 로그가 정확히 `["finishTimelapse", "save", "go"]` 순서다(M6-W). `status: "failed"`면 `toast("error", STRINGS.save.failed)`가 1회 뜨고 **`go`는 여전히 호출된다**.
  - [non-goal] `finish()` 직후 `stillOnResult()`가 false면 `save`·`go`가 **둘 다 호출되지 않는다**. `status: "partial"`에서는 `toast` **0회**다. 게스트는 `"Done"`으로 간다(VF-11 — 기존 동작 유지). `src/ui/strings.ts`가 변경되지 않는다.
  - [trigger] 보관·전이는 [다음] 버튼 클릭에서만 — `Result` 화면 진입만으로는 `finishTimelapse`도 `save`도 호출되지 않는다(기존 동작 유지).
- **롤백**: `FlowViews.tsx`의 `goNext`를 §6.1 원문으로 되돌리고 `resultNext.ts`·해당 테스트를 삭제한다(Step 10-5까지의 산출물은 호출자 없이 남는다).
- [ ] 완료

### Step 10-7: 임시 폴더 선택 진입점 + 문서 갱신
- **Context Brief**: ② 계층(실제 폴더 복사)은 운영자가 폴더를 1회 지정해야 동작하는데, 그 UI가 들어갈 설정 화면은 아직 `DummyScreen`이다(Step 13에서 구현). 진입점이 없으면 ②를 한 번도 실행할 수 없어 WBS 완료 기준을 확인할 방법이 사라진다. Step 6이 `DummyScreen`에 임시 [카메라 테스트] 버튼을 두고 "Step 13에서 설정 화면으로 옮긴다"고 남긴 선례를 그대로 따른다.
- **대상 파일**: `src/App.tsx` · `docs/web-client/11-wbs.md`(Step 10 체크박스) · `docs/web-client/15-implementation-conventions.md`(§6·§7) · `docs/web-client/14-handoff-and-user-actions.md`(실측 V19 등재)
- **선행 조건**: Step 10-4, 10-6
- **구현 내용**: 설계 §7의 조건부 버튼(`screen === "Settings" && getDirHandleRepo().isSupported()`)과 `pickLocalSaveFolder()` 헬퍼. 문서에는 (a) Step 10 산출물·검증 수치·**설계 이탈 5건**(§2.4), (b) 15 §6에 "Step 13이 이관할 임시 진입점"과 "**Step 14는 `logStore`에 `onversionchange`를 걸기 전에 `mcphoto` DB 버전을 올리면 안 된다**"(§5.2), (c) 14 §10에 실측 **V19**(네트워크 차단 완주 후 OPFS `results/` 확인 · 폴더 지정 후 실제 파일 생성 · `usage` walk 소요 — A1/A5) 등재.
- **검증 명령**: `npx tsc --noEmit && npx vitest run` (전체 녹색) · `npx vitest run --coverage` · `grep -n "Step 10" docs/web-client/11-wbs.md`
- **완료 기준**:
  - [관측] 전체 스위트가 녹색이고 테스트 수가 645에서 증가했다(실측치를 `11-wbs.md`에 기록). `11-wbs.md`의 Step 10 체크박스에 산출물·수치·이탈 5건이 적혀 있다.
  - [non-goal] `isSupported()`가 false인 환경(Safari·Firefox·모바일)에서 버튼이 **렌더되지 않는다**. `Settings` 외 다른 `DummyScreen`에는 버튼이 나타나지 않는다. 라우팅·전이 로직과 기존 [카메라 테스트] 버튼이 그대로다.
  - [trigger] 폴더 선택 대화상자는 **버튼 클릭에서만** 열린다 — 화면 진입·앱 시작으로는 `showDirectoryPicker`가 호출되지 않는다. `LocalSavePath` 설정은 폴더 선택이 **성공했을 때만** 기록된다(취소 시 불변).
- **롤백**: `App.tsx`의 버튼·헬퍼 제거 + 문서 되돌리기(코드 산출물에 영향 없음).
- [ ] 완료

---

## 10. 완결성 게이트 (js-developer 전달 전 자체 검사)

- [x] 검증된 사실(F1~F19) / 미검증 가정(A1~A5) 목록이 분리돼 있다
- [x] 모든 가정에 검증 단계가 매핑돼 있다(A1→10-2, A2·A3→10-4, A4→10-4, A5→사람·14 §10 V19)
- [x] 7개 단계 전부에 필수 7필드가 채워져 있다
- [x] 모든 완료 기준이 관측·non-goal·trigger 3문 형식이다(UI 단계 10-7 포함)
- [x] 검증 명령이 전부 자동 실행 가능한 CLI다

## 11. 설계 자체 점검 (js-architect 체크리스트)

- [x] **부수효과 해제 경로**: `createWritable` → `try/finally` `close()`(§5.2) · `SyncAccessHandle` 잠금은 `usage`에서 아예 잡지 않는다(§4.3) · 새 이벤트 리스너·타이머·`ObjectURL` **0개**(`useResultCompose`의 기존 revoke 로직 무변경)
- [x] **상태 소유권**: 보관 결과는 스토어에 넣지 않고 `ResultSaveOutcome`으로 흘려보낸다 — 홈 복귀 시 지울 상태가 늘지 않는다
- [x] **비동기 취소·오류**: 홈 복귀 가드를 보관 앞뒤 2회(§6.3), 모든 어댑터가 `false`/`null`/status로 축소, `resultSaver`는 최상단 `try/catch`
- [x] **TS strict 전제**: 판별 유니온(`ResultSavePlan`)으로 `skip` 누락을 컴파일 단계에서 막고, Worker `switch`의 `never` 소진 검사가 op 누락을 막는다
- [x] **보안**: DOM 삽입 없음(`innerHTML` 무관), 경로 조작은 `splitOpfsPath` + `isResultFolderName` 2중 방어, 로그 컨텍스트 키에 `state`·`code`·`token`·`pin`·`nonce` 미사용(F14 대조 완료)
- [x] **권한 거부/오류 UI**: §6.5 표 — 어떤 실패도 화면을 멈추지 않고, 운영자 사안(폴더 권한)은 손님 화면에 띄우지 않는다
- [x] **추가 질문 없이 구현 가능한 상세도**: 전 시그니처·판정표·호출 순서·정적 불변식 목록·기존 코드 인용 포함
