# Step 11 · 업로드 3단계 + QR + Done (★마일스톤 A) 구현 설계

| 항목 | 값 |
|------|-----|
| 대상 | WBS **Step 11** — [11 §Step 11](../web-client/11-wbs.md) |
| 규격 | [06 §4·§5](../web-client/06-backend-integration-web.md) · [03 §9·§10·§16](../web-client/03-screens-spec.md) · [07 §7](../web-client/07-auth-and-permissions-web.md) · [analysis/31 §5·§7](../analysis/31-backend-api-reference.md) · [analysis/13 §4.8·§4.9·§14](../analysis/13-client-behavior-spec.md) |
| 관례 | [15 · 구현 관례](../web-client/15-implementation-conventions.md) — 계층·테스트 전략·함정 12건·§3.4 정적 불변식 |
| 작성 | js-architect (설계만. 구현은 js-developer, 검증은 js-code-reviewer) |
| 작성일 | 2026-07-31 |
| 전제 | Step 0~10 완료 · 웹 테스트 **758**(29파일) 녹색 · 사용자 액션 A1~A5 완료(버킷 CORS 구성 완료 — `web/OPS-cors.md`) · 브랜치 `feature/web-client-foundation` |

> **이 Step의 한 줄 요약**: `Result` [다음] 이후 `Qr` 화면에서 **prepare → 서명 PUT(XHR 진행률) → commit**을 수행하고,
> **성공했을 때만** ECC **Q** QR을 그린다. 실패해도 흐름은 막지 않는다(결과물은 이미 로컬에 있다 — M6-W).
> `Done`은 6초 실경과 후 자동 홈이다.

---

## 0. 검증된 사실 / 미검증 가정

### 0.1 검증된 사실 (코드·문서를 직접 읽어 확인)

| # | 사실 | 근거 |
|---|------|------|
| F1 | `uploadGateway.prepare/commit`이 이미 있고 `auth: "optional"`이다. `parsePrepare`가 **`requiredHeaders` 객체를 통째로 보존**한다(키 선별 없음 — M14) | `src/adapters/http/uploadGateway.ts:88-92,99-121` |
| F2 | `backendClient.request`는 `signal`(외부 취소)·100초 타임아웃을 받고, 게이트 키·Bearer를 **자기 안에서만** 조립한다. `credentials:"omit"` | `backendClient.ts:83-109` |
| F3 | 오류 타입 매핑이 `errors.ts`에 완성돼 있다: `TempUserLimitError.reason`은 `"time"|"count"`, `isConflict`(409)·`NetworkError.timedOut`가 존재 | `errors.ts:50-63,116-154` |
| F4 | `qrUsageService`가 이미 있고 **fail-open**이다(`QR_USAGE_FAIL_OPEN`). `isTempUserBlocked(usage)`는 **`role === "temp_user"` 일 때만** true를 낼 수 있다 | `qrUsageService.ts:27-35,65-67` |
| F5 | **Windows는 업로드를 `QrPopupViewModel.OnEnterAsync`에서 한다.** `ResultViewModel.Next`는 타임랩스 → 로컬 저장 → `NavigateAsync(Qr|Done)`뿐이고 업로드 코드가 **없다** | `src/MCPhoto.App/ViewModels/ResultViewModel.cs:117-165` · `QrPopupViewModel.cs:50-134` |
| F6 | Windows는 **업로드 시점에 세션 ID를 새로 만든다**(`UploadContract.NewSessionId(DateTime.Now)`), 즉 [재시도]는 자동으로 새 ID다 | `src/MCPhoto.Core/Upload/UploadService.cs:41` |
| F7 | Windows QR은 `ECCLevel.Q` + `pixelsPerModule` **표시 파라미터**(팝업은 12를 넘긴다 — 20은 기본값일 뿐 계약이 아니다) | `QrService.cs:13` · `QrPopupViewModel.cs:102` |
| F8 | Windows 셸은 **계정 변경 시 1회 fire-and-forget**으로 `IQrUsageService`를 조회해 캐시하고, `IsTempUserQrBlocked`는 **동기 파생 프로퍼티**다. 조회 중 계정이 바뀌면 응답을 폐기한다 | `AppShellViewModel.cs:75-82,146-186` |
| F9 | 웹 `resultNext.ts`의 `isTempUserBlocked`는 **동기 `() => boolean`** 이고 현재 상수 `false`다. deps 주입 구조라 `defaultResultNextDeps`만 바꾸면 된다 | `resultNext.ts:42,120` |
| F10 | `resultNext.test.ts`가 `["finishTimelapse","save","go"]`를 고정한다. 이 배열은 **harness가 `calls.push`한 dep만** 담는다 → `calls`에 push하지 않는 dep을 추가해도 단언은 유지된다 | `tests/unit/screens/resultNext.test.ts:58-97` |
| F11 | 합성 Blob은 `useResultCompose`의 **React ref**에 있다(`blobRef.current`). `ResultView`가 언마운트되면 접근 경로가 사라진다 → **`Qr` 화면에서는 읽을 수 없다** | `useResultCompose.ts:43,86,123` |
| F12 | 타임랩스 결과는 **싱글턴 서비스**가 들고 있고 `stop()`(홈 복귀)까지 살아 있다 → `Qr` 화면에서 `getTimelapseService().current()`로 **읽을 수 있다** | `timelapseService.ts:242,244-251` · `shellStore.ts:135` |
| F13 | 전이 규칙: `Result → Qr|Done`, `Qr → Done`, `Done → Home`. 상단바는 `Capture`·`Qr`에서 숨는다. 유휴 감시 대상에 `Qr`은 **포함**, `Done`은 **제외** | `stateMachine.ts:18-20,44-53,65-67` |
| F14 | `env.hostingBaseUrl`은 **트레일링 슬래시 제거** 정규화가 끝난 값이고 기본값이 **P1 도메인**(`https://mcphoto-955fb.web.app`)이다. `settings.HostingBaseUrl`도 `clampSettings`가 같은 방향으로 정규화한다 | `env.ts:41-48,86-88` · `appSettings.ts:97,193` |
| F15 | 도메인에 `downloadPageUrl`·`newSessionId`·`isValidSessionId`·`stampPrefix`·`finalImageContentType`·`TIMELAPSE_CONTENT_TYPE`이 **이미 있다** | `domain/upload/uploadContract.ts` |
| F16 | 도메인에 `resolveUploadTargets`·`activeStages`·`overallProgress`·`UPLOAD_STAGES`가 **이미 있다**(진행률은 활성 단계 **균등 가중**) | `domain/upload/uploadOrchestration.ts` |
| F17 | 로그 마스킹은 키를 소문자화 + `-_ ` 제거 후 **정확 일치**다. `code`·`token`·`state`·`nonce`·`pin`이 목록에 있고 `errorCode`·`status`·`bytes`는 없다 | `logPolicy.ts` · 15 §4 함정 #1 |
| F18 | `STRINGS.upload`에 `nothingToSend`·`inProgress`·`retentionNotice`·`saveToDevice`가 이미 있다. ⚠️ `retentionNotice`가 **"{n}시간 후 자동 삭제됩니다."** 로 카탈로그(**"업로드된 사진·영상은 {N}시간 후 자동 삭제됩니다."**)보다 짧다 | `ui/strings.ts:70-76` ↔ `analysis/13 §14` |
| F19 | 버킷 CORS가 **구성 완료**다: 허용 오리진 3(kiosk 2 + `http://localhost:5173`), 허용 헤더 `Content-Type`·`x-goog-meta-firebaseStorageDownloadTokens`·`x-goog-resumable`, 메서드 PUT | `web/OPS-cors.md` 머리표 |
| F20 | P1 다운로드 페이지의 파일명 규칙이 확정돼 있다: `MCPhoto_{yyyyMMdd}_{HHmmss}.{jpg|png}` / `…_timelapse.mp4`, 형식 위반 시 `MCPhoto.jpg` / `MCPhoto_timelapse.mp4` | `docs/design/web-it17-download-share-design.md §6` 표 |
| F21 | vitest 환경은 **node**다(jsdom은 파일 상단 주석 opt-in). 커버리지 임계는 `src/domain`에만 걸린다(95/95/95/90) | `vitest.config.ts` |
| F22 | `main.tsx`는 `<StrictMode>`로 마운트한다 → **개발 빌드에서 effect가 2회 실행된다** | `main.tsx:26-28` |
| F23 | `src/domain/index.ts`는 평면 `export *` 배럴이다 → 새 도메인 심볼은 **한정형 이름**이어야 한다 | `domain/index.ts` |
| F24 | `qrcode-generator@2.0.4`는 **MIT · 런타임 의존 0 · 자체 `.d.ts` 동봉 · ESM(`dist/qrcode.mjs`)+CJS 양쪽 제공**이고 API가 `qrcode(typeNumber, 'Q')` → `make()` → `getModuleCount()`/`isDark(r,c)`다 | `npm view qrcode-generator@2.0.4` 실행 결과(2026-07-31) · 패키지 README |

### 0.2 미검증 가정 (전부 검증 단계가 매핑돼 있다)

| # | 가정 | 검증 |
|---|------|------|
| A1 | **OA-1**: 버킷 CORS 구성 상태에서 브라우저 서명 PUT이 실제로 성공한다(`OPTIONS 204 → PUT 200`) | **브라우저 실행 필요 — 자동화 불가.** [14 §10](../web-client/14-handoff-and-user-actions.md)에 **V20**으로 등재(§9의 S11-8) |
| A2 | `xhr.upload.onprogress`가 대상 브라우저에서 `lengthComputable: true`로 온다 | **S11-2** — 가짜 XHR로 계약을 고정. 실기기 관측은 **V20-3** |
| A3 | `qrcode-generator@2.0.4`의 `qrcode(0, "Q")` 자동 타입 선택이 ~90자 URL을 수용한다(type 6~7) | **S11-4** — 실제 길이의 `downloadPageUrl`로 `getModuleCount() > 0` 단언. 실패 시 typeNumber를 0→명시값으로 올린다 |
| A4 | 스캐너가 이 모듈 픽셀 크기(정수 배율 + quiet zone 4)를 읽는다 | **폰 스캔 필요 — 자동화 불가.** **V20-4** |
| A5 | StrictMode 이중 마운트에서 첫 실행이 **commit 이전에** 중단된다(409 미발생) | **S11-6** — `signal.aborted`를 commit 직전에 재확인하는 경로를 테스트로 고정. 잔여 위험은 **개발 빌드 한정**(§6.4) |

---

## 1. 전체 흐름 — 누가 무엇을 언제

```
Result [다음]  (runResultNext — Step 10이 소유, 순서 불변식)
   1. 타임랩스 마무리
   2. 홈 복귀 가드
   3. 로컬 보관  ★ M6-W
   4. 실패 토스트
   5. ← Step 11이 채우는 자리: 없음(§1.1). 합성 Blob 인계는 useResultCompose가 이미 해 둔다
   6. 홈 복귀 가드 재검사
   7. isQrEffectivelyEnabled(설정, 로그인, ★isTempUserBlocked ← qrUsageStore)
        false → Done      (게스트·TempUser 초과·설정 off — 업로드 요청 0건, VF-11)
        true  → Qr
                 │
Qr 진입(03 §9.1) │
   1. 전송 대상 확정  resolveUploadTargets(설정 토글 AND 파일 존재)
        canUpload=false → "전송할 결과물이 없습니다."  ← 요청 0건 (M7)
   2. 업로드 3단계
        ① POST /uploads/prepare   (파일당 1회)
        ② PUT {putUrl}            (XHR · requiredHeaders 전량 순회 · 인증 헤더 0건 · 진행률)
        ③ POST /uploads/commit    (prepare의 downloadUrl 그대로 · downloadPageUrl = P1 도메인)
        실패 → ②에서 멈추고 commit 호출 안 함
   3. 성공 → QR(ECC Q) + "업로드된 사진·영상은 {N}시간 후 자동 삭제됩니다."
      실패 → QR 숨김 + 사유별 문구 + [완료]/[재시도](새 세션 ID로 전 과정)
   [기기에 저장] — 업로드 성패와 무관하게 항상 가능(WD3 ③)
   [완료] → Done
Done: 6초 실경과 후 자동 홈. 로그아웃하지 않는다.
```

### 1.1 ⚠️ 설계 이탈 ① — 업로드 실행 주체는 `Qr` 화면이다 (지시문과 다름)

오케스트레이터 지시문과 `resultNext.ts`의 예약 주석은 **업로드 3단계를 `runResultNext` 안**(보관 뒤·전이 앞)에 두라고 한다.
설계 조사 결과 **그 자리는 옳지 않다**고 판단했다. 근거 4가지:

| # | 근거 | 출처 |
|---|------|------|
| 1 | **Windows가 그렇게 하지 않는다.** `ResultViewModel.Next`에는 업로드 코드가 없고 `QrPopupViewModel.OnEnterAsync`가 3단계를 수행한다 | F5 |
| 2 | **웹 규격이 명시적으로 `Qr` 화면 절차로 쓰여 있다.** "진입 절차 1. 전송 대상 확정 → 2. 업로드 3단계(진행률) → 3. 성공/실패 분기" | [03 §9.1](../web-client/03-screens-spec.md) · [analysis/13 §4.8](../analysis/13-client-behavior-spec.md) |
| 3 | **[재시도]가 `Qr` 화면 액션이다.** 재시도는 전 과정 재실행이므로 업로드 함수는 어차피 `Qr`에서 호출 가능해야 한다 → `runResultNext`에도 두면 **같은 부수효과의 진입점이 2개**가 된다(15 §4 함정 #6과 동종) | 06 §4.4 · 03 §9.1 |
| 4 | **전이 목적지가 업로드 성패와 무관하다.** effective QR이 on이면 실패해도 `Qr`로 가서 사유를 보여야 한다(WBS 완료 기준: "업로드 실패 시 QR이 뜨지 않고 [완료]로 진행 가능") → 미리 올릴 이유가 없고, 올리면 **진행률 UI가 없는 `Result` 화면에서 수 초간 정지**한다(06 §4.5의 단계 라벨 규격이 무의미해진다) | WBS Step 11 · 06 §4.5 |

**M6-W는 그대로 성립한다.** `runResultNext`가 `save`를 `go`보다 먼저 하고, 업로드는 `go` 이후 화면에서 일어나므로
"보관 → 업로드" 순서는 **구조적으로 보장**된다(오히려 더 강하다). `resultNext.test.ts`의 `["finishTimelapse","save","go"]`도 변하지 않는다.

**되돌리기 비용은 낮게 설계했다**: 업로드는 `runUpload(deps)` **단일 함수**(React 무관, node 테스트 가능)이므로,
팀이 지시문대로를 고수하면 `runResultNext` 안에서 같은 함수를 부르고 `Qr`은 결과만 표시하도록 바꾸면 된다(호출 지점 이동 = 약 10줄).
**이 판단은 오케스트레이터 확인 대상이다**(§12).

### 1.2 합성 Blob을 `Qr`까지 옮기는 방법

`Qr` 화면은 `useResultCompose`의 ref에 닿을 수 없다(F11). 반면 타임랩스는 싱글턴이라 그대로 읽힌다(F12).
따라서 **합성 결과만** 세션 컨텍스트로 올린다.

```ts
// sessionStore
export interface FinalImageArtifact {
  readonly blob: Blob;
  /** 합성 시점의 출력 포맷. prepare의 ext·contentType과 **반드시 같아야** 한다. */
  readonly format: OutputFormat;
}
```

- `useResultCompose`가 합성 성공마다 `sessionStore.getState().setFinalImage({ blob, format: values.OutputFormat })`.
- `discardCaptureData()`가 `finalImage: null`로 지운다(홈 복귀·유휴 만료·전역 예외 복구가 전부 이 경로).
- **`format`을 같이 든다**: `Result → Settings → Result` 왕복으로 `OutputFormat`이 바뀌어도, 이미 만들어진 바이트와
  `Content-Type` 선언이 어긋나지 않는다(어긋나면 GCS 오브젝트의 content type이 실제 바이트와 달라진다).
- `Blob`은 `ImageBitmap`과 달리 **명시 해제가 없다**(GC). 새 해제 경로가 생기지 않는다.
- `resultNext`의 `finalBlob` dep은 **그대로 둔다**(`ResultView`가 `result.currentBlob`을 넘기는 현행 유지) — 회귀 표면을 늘리지 않는다.

---

## 2. 계층 배치 — 파일 목록과 책임

### 2.1 신규 파일 (10)

| 파일 | 책임 |
|------|------|
| `src/domain/upload/qrRenderPlan.ts` | QR 렌더 기하 **순수 계산**: quiet zone 4모듈 상수, 정수 모듈 픽셀·캔버스 크기 산출 |
| `src/domain/upload/exportFileName.ts` | [기기에 저장] 파일명 — **P1 페이지와 같은 규칙**(F20) |
| `src/adapters/qr/qrService.ts` | `qrcode-generator` 래퍼. **ECC Q 고정**(VF-13) · 모듈 행렬 생성 · canvas 렌더. 실패는 `null`/`false` |
| `src/adapters/platform/fileExport.ts` | `<a download>` 내보내기. 기능 감지 + **`revokeObjectURL` 필수** |
| `src/shell/qrUsageStore.ts` | TempUser 한도 캐시 + `installQrUsageLifecycle()`(계정 변경 1회 fire-and-forget · stale 응답 폐기 · fail-open) |
| `src/screens/qr/uploadRunner.ts` | 업로드 3단계 오케스트레이션. **React 무관 · node 테스트 가능**(`runResultNext`와 같은 형태) |
| `src/screens/qr/useUploadRun.ts` | React 배선 — `AbortController` 수명, [재시도] 키, phase 상태 |
| `src/screens/done/doneAutoHome.ts` | 6초 **실경과** 자동 홈(탭 hidden 복귀 시 즉시 재판정). 주입형 · node 테스트 가능 |
| `src/ui/views/QrView.tsx` | `Qr` 화면 표현 — 진행률 · QR canvas · 사유별 문구 · [기기에 저장]/[재시도]/[완료] |
| `src/ui/views/DoneView.tsx` | `Done` 화면 표현 |

### 2.2 수정 파일 (9)

| 파일 | 변경 | 위험 |
|------|------|------|
| `src/adapters/http/uploadGateway.ts` | `put()` 추가(XHR). `UploadGateway` 인터페이스 확장 | 중 — 기존 `prepare/commit` 본문 무변경 |
| `src/shell/sessionStore.ts` | `finalImage` 필드 + `setFinalImage` + `discardCaptureData`에서 해제 | 낮 |
| `src/screens/result/useResultCompose.ts` | 합성 성공 시 `setFinalImage` 1줄 | 낮 |
| `src/screens/result/resultNext.ts` | `defaultResultNextDeps.isTempUserBlocked`를 `qrUsageStore`로 교체 + 예약 주석 블록을 **§1.1 사유 주석**으로 교체 | 낮 — 시그니처 무변경 |
| `src/main.tsx` | `installQrUsageLifecycle()` 설치(`installTokenLifecycle` 옆) | 낮 |
| `src/App.tsx` | `ScreenRouter`에 `Qr`·`Done` 케이스 추가 | 낮 |
| `src/ui/strings.ts` | `upload` 블록 보강 + `retentionNotice` 카탈로그 문구로 **정정**(F18) | 낮 |
| `src/ui/views/screens.module.css` | QR·진행률·상태 문구 스타일 | 낮 |
| `webclient/package.json` · `webclient/THIRD-PARTY.md` | `qrcode-generator` **정확 핀 2.0.4** 추가 + 라이선스 기록 | 낮 |

### 2.3 테스트 파일 (신규 5 · 수정 3)

| 파일 | 내용 |
|------|------|
| `tests/unit/http/uploadGateway.test.ts`(신규) | `put()` 동작 + **M14 순회 부착** + **정적 불변식**(인증 헤더 0건) |
| `tests/unit/screens/uploadRunner.test.ts`(신규) | 3단계 순서·M7·M8·실패 분기·재시도 세션 ID·취소 |
| `tests/unit/qr/qrService.test.ts`(신규) | ECC Q 정적 검사 + 행렬 생성 + 가짜 canvas 렌더 |
| `tests/unit/shell/qrUsage.test.ts`(신규) | 계정 변경 1회 조회 · 비TempUser 미조회 · stale 폐기 · fail-open |
| `tests/unit/screens/doneAutoHome.test.ts`(신규) | 실경과 6초 · hidden 복귀 즉시 판정 · 정리 |
| `tests/unit/domain/uploadAndFilters.test.ts`(수정) | `planQrRender`·`exportFileName` 케이스 추가 |
| `tests/unit/screens/resultNext.test.ts`(수정) | 기존 단언 **유지** + `isTempUserBlocked` 실배선 케이스 추가 |
| `tests/unit/storage/platform.test.ts`(수정) | `fileExport` 케이스 추가(미지원·revoke) |

### 2.4 이번 Step에서 **만들지 않는 것** (WBS에 이탈로 기록할 것)

| 항목 | 이유 |
|------|------|
| `prepare`의 `bucket`으로 **설정 `StorageBucket` 갱신** | 웹은 URL을 재조립하지 않고 `downloadUrl`을 그대로 넘긴다(06 §4.3) → 갱신 이득이 없다. 게다가 `StorageBucket`은 `GUEST_LOCKED_KEYS`라 손님 세션에서 쓰면 권한 축이 흐려진다. 값은 **로그로만** 남긴다 |
| Playwright E2E(`e2e/upload-qr.spec.ts`·`e2e/guest-flow.spec.ts`) | 저장소에 Playwright 설치·설정이 **아직 없다**(`package.json` devDeps에 없음). E2E 도입은 **Step 17**의 범위다. Step 11은 node 단위 테스트 + [14 §10] 실측(V20)으로 대체 |
| 로그인 UI를 통한 종단(폰 스캔) 검증 | 로그인이 **Step 12**다(WBS Step 11 Context Brief가 명시). 이 Step은 `runUpload`·`runResultNext`를 목으로 검증 |
| `Qr` 화면의 재시도 횟수 제한 | 규격에 없다. 무제한 [재시도](각 시도는 새 세션 ID) |

---

## 3. 도메인 추가 (2파일) — 순수·주입형

> `src/domain`은 아무것도 import하지 않고 `Date.now`·`Math.random`·브라우저 API·`console`을 부르지 않는다.
> `purity.test.ts`가 glob으로 자동 포함한다. **배럴이 평면이라 한정형 이름**을 쓴다(F23).

### 3.1 `src/domain/upload/qrRenderPlan.ts`

```ts
/** QR 여백(quiet zone) — **4모듈이 규격**이다(03 §9). 줄이면 스캐너가 인식하지 못한다. */
export const QR_QUIET_ZONE_MODULES = 4;

export interface QrRenderPlan {
  /** 모듈 1개의 픽셀 크기(정수 ≥ 1). */
  readonly modulePx: number;
  /** 캔버스 한 변 픽셀 = modulePx * (moduleCount + quiet*2). */
  readonly canvasPx: number;
  /** 좌·상 여백 픽셀 = modulePx * quiet. */
  readonly quietPx: number;
}

/**
 * 표시 크기에서 **정수 배율** 모듈 픽셀을 정한다.
 * 정수인 이유: 소수 배율은 모듈 경계가 반픽셀에 걸려 스캐너 인식률이 떨어진다.
 * `targetPx`가 너무 작아도 **최소 1px**을 보장한다(0이면 빈 캔버스가 된다).
 */
export function planQrRender(
  moduleCount: number,
  targetPx: number,
  quietModules: number = QR_QUIET_ZONE_MODULES,
): QrRenderPlan;
```

규칙(그대로 구현):

```
quiet   = 유한·0 이상이면 floor(quietModules), 아니면 QR_QUIET_ZONE_MODULES
modules = 유한·양수면 floor(moduleCount), 아니면 0
total   = modules + quiet * 2
if (modules <= 0) return { modulePx: 1, canvasPx: 1, quietPx: 0 }   // 그릴 것이 없다
target   = 유한·양수면 targetPx, 아니면 0
modulePx = max(1, floor(target / total))
return { modulePx, canvasPx: modulePx * total, quietPx: modulePx * quiet }
```

판정표(테스트 그대로):

| moduleCount | targetPx | quiet | modulePx | canvasPx | quietPx |
|---|---|---|---|---|---|
| 41 (type 6) | 640 | 4 | `floor(640/49)=13` | 637 | 52 |
| 41 | 40 | 4 | `max(1, floor(40/49)=0)=1` | 49 | 4 |
| 41 | 640 | 0 | `floor(640/41)=15` | 615 | 0 |
| 0 (방어) | 640 | 4 | 1 | 1 | 0 |
| 41 | 0 (방어) | 4 | 1 | 49 | 4 |

- 어떤 입력에도 **던지지 않는다**(도메인은 방어적). `modules <= 0`은 `createQrMatrix`가 `null`을 준 경우라 화면이 이미 걸러낸다.

### 3.2 `src/domain/upload/exportFileName.ts`

```ts
/** [기기에 저장] 파일명 접두 — P1 다운로드 페이지와 **같은 값**을 쓴다(web-it17 §6). */
export const EXPORT_FILE_PREFIX = "MCPhoto";

/**
 * `MCPhoto_{yyyyMMdd}_{HHmmss}.{jpg|png}` / `MCPhoto_{yyyyMMdd}_{HHmmss}_timelapse.mp4`
 * 세션 ID 형식이 아니면(방어) `MCPhoto.jpg` / `MCPhoto_timelapse.mp4`.
 * ⚠️ **UUID 부분을 파일명에 넣지 않는다**(web-it17 §6의 보안 판정과 동일).
 */
export function exportFileName(
  sessionId: string | null,
  kind: "final" | "timelapse",
  format: OutputFormat,
): string;
```

| sessionId | kind | format | 결과 |
|---|---|---|---|
| `20260730_143022_a1b2…` | final | Jpg | `MCPhoto_20260730_143022.jpg` |
| 동상 | final | Png | `MCPhoto_20260730_143022.png` |
| 동상 | timelapse | — | `MCPhoto_20260730_143022_timelapse.mp4` |
| `null` / 형식 위반 | final | Jpg | `MCPhoto.jpg` |
| `null` / 형식 위반 | timelapse | — | `MCPhoto_timelapse.mp4` |

- 스탬프는 `isValidSessionId`를 통과했을 때만 `sessionId.slice(0, 15)`로 뽑는다(정규식 캡처와 동치이면서 재구현이 없다).

### 3.3 배럴 추가 · 이름 충돌 검사

`src/domain/index.ts`에 2줄:
```ts
export * from "./upload/qrRenderPlan";
export * from "./upload/exportFileName";
```
신규 심볼 `QR_QUIET_ZONE_MODULES`·`QrRenderPlan`·`planQrRender`·`EXPORT_FILE_PREFIX`·`exportFileName` —
기존 배럴 export와 **충돌 없음**(구현 시 `npx tsc --noEmit`이 중복 export를 즉시 잡는다).

---

## 4. 어댑터 설계

### 4.1 `src/adapters/http/uploadGateway.ts` — ② 서명 PUT (XHR)

`fetch`를 쓰지 않는 이유는 **업로드 진행률을 주지 않기 때문**이다(WM5 · 06 §4.2).

```ts
/** `xhr.upload.onprogress` 1회분. */
export interface SignedPutProgress {
  readonly loaded: number;
  readonly total: number;
}

export interface SignedPutRequest {
  /** V4 서명 URL. **절대 로그에 남기지 않는다.** */
  readonly url: string;
  readonly body: Blob;
  /**
   * prepare가 준 `requiredHeaders` **그대로**. 이 객체를 **순회해 전부** 부착한다(M14).
   * 키를 골라 담거나 이름 대소문자를 바꾸면 서명 불일치(403) 또는 다운로드 토큰 미설정이 된다.
   */
  readonly headers: Readonly<Record<string, string>>;
  readonly onProgress?: (progress: SignedPutProgress) => void;
  readonly signal?: AbortSignal;
  readonly timeoutMs?: number;
}

export type SignedPutFailure = "http" | "network" | "timeout" | "aborted";

export type SignedPutOutcome =
  | { readonly ok: true; readonly status: number; readonly bytes: number; readonly elapsedMs: number }
  | { readonly ok: false; readonly failure: SignedPutFailure; readonly status: number | null; readonly elapsedMs: number };

export const SIGNED_PUT_TIMEOUT_MS = 100_000;   // 06 §4.2 · backendClient와 같은 값
```

`UploadGateway` 인터페이스에 추가:
```ts
export interface UploadGateway {
  prepare(request: PrepareRequest): Promise<PrepareResponse>;
  commit(request: CommitRequest): Promise<CommitResponse>;
  /** ⚠️ **던지지 않는다**(15 §2). 실패는 `SignedPutOutcome`으로 표현한다. */
  put(request: SignedPutRequest): Promise<SignedPutOutcome>;
}

export interface UploadGatewayDeps {
  /** 테스트 주입. 기본 `() => new XMLHttpRequest()`. */
  readonly createXhr?: () => XMLHttpRequest;
  readonly now?: () => number;
}
export function createUploadGateway(
  client: BackendClient = getBackendClient(),
  deps: UploadGatewayDeps = {},
): UploadGateway;
```

구현 규칙(그대로 지킬 것):

| # | 규칙 | 이유 |
|---|------|------|
| 1 | `xhr.open("PUT", url, true)` **직후** `for (const [k, v] of Object.entries(request.headers)) xhr.setRequestHeader(k, v)` | M14. `open` 전 호출은 `InvalidStateError` |
| 2 | `Authorization`·`X-MCPhoto-Client`를 **붙이지 않는다** | 서명 URL 자체가 권한. 붙이면 서명 검증 실패 또는 preflight 실패(31 §5.2) |
| 3 | `xhr.upload.onprogress = (e) => { if (e.lengthComputable) onProgress({loaded: e.loaded, total: e.total}); }` | `xhr.onprogress`(다운로드)가 **아니다** |
| 4 | `xhr.timeout = timeoutMs ?? SIGNED_PUT_TIMEOUT_MS` | 06 §4.2 |
| 5 | `xhr.onload` → `status >= 200 && status < 300 ? ok : {failure:"http", status}` | 4xx/5xx는 http 실패 |
| 6 | `xhr.onerror` → `{failure:"network", status:null}` + 로그에 **"네트워크 또는 CORS 차단 가능 — 업로드 구성(CORS) 확인 필요"** | 브라우저는 CORS 차단을 구분해 주지 않는다(03 §9.3 · 08 §5) |
| 7 | `xhr.ontimeout` → `"timeout"`, `xhr.onabort` → `"aborted"` | |
| 8 | `settled` 플래그로 **정확히 1회만** resolve | 여러 핸들러가 겹칠 수 있다 |
| 9 | `signal.aborted`면 **send 없이** 즉시 `"aborted"` 반환 | 취소 후 낭비 전송 금지 |
| 10 | `signal.addEventListener("abort", onAbort)` → settle 시 **반드시 `removeEventListener`** | 누수 방지(§11) |
| 11 | 로그 컨텍스트: `{ kind, bytes, status, elapsedMs, headerNames }`만. **`url`·헤더 값·응답 본문 금지** | 41 §8 · 마스킹 함정 #1. `headerNames`는 이름 문자열 배열이라 비밀이 아니고 **M14 진단의 유일한 단서**다 |
| 12 | `xhr.responseType`을 건드리지 않고 응답 본문을 **읽지 않는다** | 남길 이유가 없다 |

**정적 불변식(테스트가 소스를 읽어 고정 — 15 §3.4 관례)**

| 불변식 | 검사 |
|---|---|
| 서명 PUT에 인증 헤더가 붙지 않는다 | `uploadGateway.ts` 소스에 `Authorization` · `X-MCPhoto-Client` · `GATE_KEY_HEADER` · `getToken` **0건** |
| 서명 URL이 로그에 가지 않는다 | `uploadGateway.ts` 소스의 `logger.` 호출 인자에 `url` 식별자 0건(정규식: `logger\.[a-z]+\([^)]*\burl\b`) |
| `fetch`로 PUT하지 않는다 | `uploadGateway.ts` 소스에 `fetch(` 0건(WM5) |

### 4.2 `src/adapters/qr/qrService.ts` — QR 생성 (ECC **Q**)

```ts
/** ⚠️ **Windows `QrService.cs`의 `ECCLevel.Q`와 같아야 한다**(VF-13). 상수를 바꾸면 정적 테스트가 실패한다. */
export const QR_ECC_LEVEL = "Q" as const;

export interface QrMatrix {
  readonly moduleCount: number;
  isDark(row: number, col: number): boolean;
}

/** 실패는 **예외가 아니라 `null`**이다(용량 초과 등). 화면은 "QR을 만들 수 없습니다"로 축소한다. */
export function createQrMatrix(text: string): QrMatrix | null;

/**
 * 흰 배경 고정 + 검정 모듈 + quiet zone 4모듈로 canvas에 그린다.
 * ⚠️ **다크모드에서도 반전하지 않는다**(스캐너 호환 — 03 §9).
 * 성공 여부를 boolean으로 돌려준다(2D 컨텍스트 부재 등).
 */
export function drawQrToCanvas(
  canvas: HTMLCanvasElement,
  matrix: QrMatrix,
  targetPx: number,
): boolean;
```

- 구현: `import qrcode from "qrcode-generator";` → `const qr = qrcode(0, QR_ECC_LEVEL); qr.addData(text); qr.make();`
  (`typeNumber: 0` = 자동. 실패는 라이브러리가 던지므로 `try/catch` → `null`.)
- 렌더: `planQrRender(matrix.moduleCount, targetPx)` → `canvas.width = canvas.height = plan.canvasPx`
  → `ctx.fillStyle = "#ffffff"; ctx.fillRect(0,0,canvasPx,canvasPx)` → 어두운 모듈만 `#000000`으로 `fillRect`.
- **`createImgTag`·`createSvgTag`를 쓰지 않는다** — HTML 문자열을 만들며 `innerHTML` 유혹을 만든다(보안 규칙).
- CSS: `image-rendering: pixelated` + `max-width: 100%`로 표시 확대(정수 모듈 유지).

**정적 불변식**: `qrService.ts` 소스에 `"L"`·`"M"`·`"H"` ECC 리터럴이 등장하지 않고 `QR_ECC_LEVEL === "Q"`다. `innerHTML` 0건.

### 4.3 `src/adapters/platform/fileExport.ts` — [기기에 저장] (WD3 ③)

```ts
/** `<a download>` 지원 여부. **타입을 믿지 말고 런타임 감지**한다(15 §4 함정 #2). */
export function canExportFile(): boolean;

/** 실패는 `false`. 예외를 던지지 않는다. */
export function exportBlob(blob: Blob, fileName: string): boolean;
```

- `canExportFile()`: `typeof document !== "undefined" && "download" in document.createElement("a")`.
- `exportBlob`: `URL.createObjectURL(blob)` → `<a href download={fileName}>` → `a.click()` →
  **`setTimeout(() => URL.revokeObjectURL(url), 0)`** (즉시 revoke하면 일부 브라우저가 다운로드를 취소한다).
  `a`는 DOM에 붙이지 않거나 붙였으면 즉시 `remove()`.
- **파일이 2개면 버튼도 2개**다(03 §9.3) — 다중 자동 다운로드는 브라우저가 차단한다. 한 클릭 = 한 파일.

---

## 5. 셸 설계 — `src/shell/qrUsageStore.ts` (`isTempUserBlocked` 실배선)

Windows `AppShellViewModel`(F8)과 **같은 형태**다: 계정 변경 1회 fire-and-forget 조회 → 캐시 → **동기 파생값**.

```ts
export interface QrUsageSnapshot {
  readonly usage: QrUsage | null;   // 미조회·비TempUser는 null
  readonly loading: boolean;
}

/** TempUser이고 한도 초과인가. **미조회·조회 실패·비TempUser·게스트는 false**(fail-open — M9). */
export function isTempUserQrBlocked(): boolean;

/** 초과 사유(설정·진단 표시용, Step 13·16이 소비). 해당 없으면 `"ok"`. */
export function tempUserQrReason(): QrUsageReason;

/** 현재 스냅샷(진단용). */
export function qrUsageSnapshot(): QrUsageSnapshot;

/** `main.tsx`가 1회 설치. `installTokenLifecycle`과 같은 자리다. 해제 함수를 돌려준다. */
export function installQrUsageLifecycle(deps?: QrUsageLifecycleDeps): () => void;

export interface QrUsageLifecycleDeps {
  readonly service?: QrUsageService;
  /** 테스트 주입(기본 `sessionStore`). */
  readonly subscribe?: (listener: (user: SessionUser | null) => void) => () => void;
}
```

동작:

| 이벤트 | 처리 |
|--------|------|
| 계정 변경(로그인·로그아웃·교체) | 캐시를 **먼저 비운다** → `role === "temp_user"`일 때만 `service.fetch()` 호출 |
| 비TempUser·게스트 | **요청하지 않는다.** 캐시 null 유지 → `isTempUserQrBlocked() === false` |
| 응답 도착 | 조회를 시작한 사용자와 **현재 사용자가 다르면 폐기**(경합 방어 — F8과 동일) |
| 조회 실패 | `qrUsageService`가 이미 fail-open으로 `QR_USAGE_FAIL_OPEN`을 준다 → `blocked:false`. 추가 처리 없음 |
| 업로드 성공 후 | **갱신하지 않는다.** 세션당 1카운트는 서버가 집계하고, 다음 촬영은 어차피 stale 가능 → 서버가 prepare에서 최종 판정한다(과금 안전은 서버 담보 — 07 §7) |

`resultNext.ts` 변경은 **한 줄**이다:

```ts
// defaultResultNextDeps
isTempUserBlocked: () => isTempUserQrBlocked(),   // 종전: () => false
```
그리고 예약 주석 블록을 §1.1의 사유를 담은 주석으로 교체한다:

```ts
  // ┌─────────────────────────────────────────────────────────────────────────┐
  // │ 업로드 3단계는 **여기서 하지 않는다** — `Qr` 화면이 소유한다(03 §9.1).      │
  // │  ① Windows도 `QrPopupViewModel.OnEnterAsync`가 수행한다                   │
  // │  ② [재시도]가 Qr 화면 액션이라 어차피 그쪽에서 호출 가능해야 한다           │
  // │  ③ 진행률 3단계 라벨이 Qr 화면 규격이다(06 §4.5)                          │
  // │ M6-W(보관 → 업로드)는 save가 go보다 앞이므로 **구조적으로** 성립한다.       │
  // │ 합성 Blob은 `useResultCompose`가 `sessionStore.finalImage`로 인계해 둔다.  │
  // └─────────────────────────────────────────────────────────────────────────┘
```

**⚠️ `isTempUserBlocked`의 동기 시그니처를 바꾸지 않는다.** 비동기로 바꾸면 [다음]이 네트워크를 기다리게 되고,
서버 미도달 환경에서 손님이 최대 100초 멈춘다. Windows도 캐시 기반 동기 판정이다.

---

## 6. 화면 설계

### 6.1 `src/screens/qr/uploadRunner.ts` — React 무관 오케스트레이션

```ts
export type UploadFailureReason =
  | "temp-user-time" | "temp-user-count"   // 403 TEMP_USER_*
  | "network"                              // 응답 없음(타임아웃·CORS 포함)
  | "conflict"                             // 409 — 같은 세션 재commit(이중 실행 의심)
  | "server";                              // 그 밖의 서버 오류·PUT 4xx/5xx·예기치 못한 예외

export type UploadPhase =
  | { readonly kind: "idle" }
  | { readonly kind: "nothing" }                                     // 전송 대상 0 — 요청 0건(M7)
  | { readonly kind: "uploading"; readonly stage: UploadStage; readonly progress: number | null }
  | { readonly kind: "succeeded"; readonly downloadPageUrl: string; readonly retentionHours: number }
  | { readonly kind: "failed"; readonly reason: UploadFailureReason };

export interface UploadRunOutcome {
  readonly phase: UploadPhase;
  /** 취소(화면 이탈·재시도로 교체)로 끝났는가. true면 phase를 화면에 반영하지 않는다. */
  readonly aborted: boolean;
}

export interface UploadRunDeps {
  readonly gateway: UploadGateway;
  readonly finalImage: () => FinalImageArtifact | null;
  readonly timelapse: () => Blob | null;
  readonly settings: () => AppSettingsValues;
  /** 촬영 세션 ID(`sessionStore.sessionId`). */
  readonly captureSessionId: () => string | null;
  /** 0 = 최초, 1↑ = [재시도] 횟수. 세션 ID 결정에 쓰인다. */
  readonly attempt: number;
  readonly onPhase: (phase: UploadPhase) => void;
  readonly now: () => Date;
  readonly uuid: () => string;
  readonly signal?: AbortSignal;
}

export async function runUpload(deps: UploadRunDeps): Promise<UploadRunOutcome>;

/** 최초 시도는 촬영 세션 ID를 **재사용**하고, [재시도]는 **새로 만든다**(06 §4.4). */
export function resolveUploadSessionId(
  captureSessionId: string | null,
  attempt: number,
  now: Date,
  uuid: string,
): string;

/** 03 §9.2 문구표. 로컬 저장 **토글** 기준이다(실제 보관 성패가 아니다 — Windows와 동일). */
export function uploadFailureMessage(reason: UploadFailureReason, saveLocalCopy: boolean): string;
```

**실행 순서(그대로 구현)**

```
0. signal.aborted → {aborted:true}
1. targets = resolveUploadTargets({
       sendPhoto: s.SendPhoto, sendTimelapse: s.SendTimelapse,
       hasFinalImage: finalImage() !== null, hasTimelapse: timelapse() !== null })
   1a. 옵션 on인데 파일이 없으면 **경고 로그**(설정 문제와 생성 실패를 로그에서 가른다 — Windows와 동종)
   1b. !targets.canUpload → onPhase({kind:"nothing"}) → return   ← 요청 0건 (M7)
2. sessionId = resolveUploadSessionId(captureSessionId(), attempt, now(), uuid())
   로그: { attempt, sameAsCaptureSession: boolean }   ← 세션 ID 원문은 비밀이 아니지만 남기지 않는다(§7.3)
3. onPhase({kind:"uploading", stage: activeStages(targets)[0], progress: null})   ← 초기 불확정(06 §4.5)
4. for stage of ["Photo","Timelapse"] 중 활성인 것:
   4a. abort 확인
   4b. prepare({ sessionId, files: [ 파일 1개 ] })            ← **파일당 1회**(06 §4.1)
         final     : { kind:"final",     ext: format==="Png"?"png":"jpg", contentType: finalImageContentType(format) }
         timelapse : { kind:"timelapse", ext:"mp4", contentType: TIMELAPSE_CONTENT_TYPE }
   4c. 응답에서 uploads.find(u => u.kind === 해당 kind). 없으면 → failed("server")
   4d. put({ url: putUrl, body: blob, headers: requiredHeaders, signal,
             onProgress: p => onPhase({kind:"uploading", stage,
                 progress: overallProgress(targets, stage, p.total>0 ? p.loaded/p.total : 0)}) })
   4e. !ok → **commit을 호출하지 않고** failed(매핑) → return
   4f. downloadUrl 보관
5. abort 확인 → onPhase({kind:"uploading", stage:"Finalizing",
                          progress: overallProgress(targets,"Finalizing",0)})
6. commit({ sessionId,
            finalImageUrl:  targets.uploadPhoto     ? photoDownloadUrl : null,   ← M8: 꺼짐은 null
            timelapseUrl:   targets.uploadTimelapse ? tlDownloadUrl    : null,
            retentionHours: s.RetentionHours,
            downloadPageUrl: downloadPageUrl(s.HostingBaseUrl, sessionId) })     ← P1 도메인
7. onPhase({kind:"succeeded",
            downloadPageUrl: res.downloadPageUrl || 로컬 조립값,
            retentionHours: s.RetentionHours})
```

**불변식 대응표**

| 불변식 | 이 설계에서 지켜지는 지점 |
|---|---|
| **M7** 최소 1개 | 1b에서 `canUpload=false`면 **prepare 자체를 하지 않는다**. 6에서 `finalImageUrl`·`timelapseUrl`이 둘 다 null이 되는 경로가 **구조적으로 없다**(활성 단계가 최소 1개이고 그 단계가 성공해야 6에 도달) |
| **M8** 꺼짐 vs 실패 혼동 금지 | 4e에서 **어느 파일이든 실패하면 commit을 하지 않는다.** "타임랩스는 실패했지만 사진만 commit"을 **하지 않는다** — 그러면 P1이 `timelapseUrl:null`을 "옵션 꺼짐"으로 표시해 실패를 은폐한다 |
| **M14** | `requiredHeaders`를 그대로 `put`에 넘기고 어댑터가 `Object.entries` 순회 |
| **M5** QR은 성공 후에만 | 화면이 `phase.kind === "succeeded"`에서만 canvas를 렌더 |
| 06 §4.4 재시도 | `attempt`가 세션 ID를 새로 만든다. 결과물은 **재합성하지 않고** 같은 Blob을 다시 올린다 |
| 06 §4.2 실패 시 commit 금지 | 4e |

**오류 → `UploadFailureReason` 매핑(유일 지점)**

| 입력 | reason |
|------|--------|
| `TempUserLimitError` `reason==="time"` | `temp-user-time` |
| `TempUserLimitError` `reason==="count"` | `temp-user-count` |
| `NetworkError` (prepare/commit) · `put` `failure==="network"|"timeout"` | `network` |
| `isConflict(err)` (409) | `conflict` — **로그 메시지를 구분**한다(`업로드 commit 충돌(이중 실행 의심)`) |
| `put` `failure==="http"` · 그 밖의 `BackendError` · 알 수 없는 예외 | `server` |
| `put` `failure==="aborted"` · `signal.aborted` | phase를 바꾸지 않고 `{aborted:true}` |

`uploadFailureMessage`:

| reason | 문구(STRINGS) |
|---|---|
| `temp-user-time` | `upload.tempUserTimeExceeded` = "무료 사용 시간이 지났습니다. 관리자에게 문의해주세요." |
| `temp-user-count` | `upload.tempUserCountExceeded` = "무료 사용 횟수가 소진되었습니다. 관리자에게 문의해주세요." |
| 그 외 · `saveLocalCopy === true` | `upload.failedSaved` = "전송 실패 — 사진은 기기에 저장되었습니다." |
| 그 외 · `saveLocalCopy === false` | `upload.failedNotSaved` = "전송에 실패했습니다. 로컬 저장을 켜면 기기에 보관됩니다." |

### 6.2 `src/screens/qr/useUploadRun.ts` — React 배선

```ts
export interface UploadRun {
  readonly phase: UploadPhase;
  readonly attempt: number;
  /** [재시도] — 새 세션 ID로 전 과정 재실행. 진행 중이면 이전 실행을 중단하고 다시 시작한다. */
  retry(): void;
  /** [완료]로 떠나기 전 명시 중단. */
  cancel(): void;
}
export function useUploadRun(): UploadRun;
```

```ts
const [runKey, setRunKey] = useState(0);
const [phase, setPhase] = useState<UploadPhase>({ kind: "idle" });

useEffect(() => {
  const controller = new AbortController();
  void runUpload({
    ...defaultUploadRunDeps(),
    attempt: runKey,
    signal: controller.signal,
    onPhase: (p) => { if (!controller.signal.aborted) setPhase(p); },
  });
  // 화면 이탈·재시도 → 진행 중 요청을 끊는다(낭비 전송·유령 commit 방지).
  return () => controller.abort();
}, [runKey]);
```

| 항목 | 규칙 |
|------|------|
| **StrictMode 이중 실행**(F22) | effect가 **매 실행마다 자기 controller를 만든다** → 첫 실행은 cleanup에서 중단되고 두 번째가 정상 진행한다. `runningRef` 같은 전역 잠금을 쓰면 두 번째가 **영구히 스킵**되므로 쓰지 않는다 |
| 잔여 위험(A5) | 개발 빌드에서 첫 실행의 prepare가 서버에 한 번 더 갈 수 있다(**부수효과 없음** — 카운트는 commit에서만 증가). commit 직전 abort 재확인으로 409 창을 최소화한다 |
| [재시도] | `setRunKey(k => k + 1)` → cleanup이 이전 실행 중단 → 새 `attempt`로 재실행. **진행률·상태가 0에서 재시작**된다 |
| [완료] | `cancel()` 후 `shellStore.go("Done")` |
| 유휴 만료·전역 예외 | `returnHome` → 화면 언마운트 → cleanup abort. 별도 셸 훅을 **추가하지 않는다**(표면 최소화) |

### 6.3 `src/ui/views/QrView.tsx`

```
┌──────────────────────────────────────────────┐   ← 상단바 없음(F13)
│                                              │
│   [phase별 본문]                              │
│                                              │
│   [기기에 저장(사진)] [기기에 저장(영상)]       │  ← 파일이 있을 때만, 각각 1버튼(03 §9.3)
│                        [재시도]   [완료]      │
└──────────────────────────────────────────────┘
```

| phase | 본문 |
|-------|------|
| `idle`·`uploading` | `<Spinner label={단계 라벨} />` + `<progress>`(`progress === null`이면 **불확정**: `value` 미지정) · `aria-live="polite"` |
| `nothing` | `STRINGS.upload.nothingToSend` · [재시도] **숨김**(올릴 것이 없다) |
| `succeeded` | `<QrCanvas text={downloadPageUrl} />` + `formatCount(STRINGS.upload.retentionNotice, retentionHours)` · [재시도] 숨김 |
| `failed` | `uploadFailureMessage(reason, settings.SaveLocalCopy)` · **QR 미렌더**(M5) · [재시도] 노출 |

단계 라벨(STRINGS 신규): `Photo`="사진 업로드 중" · `Timelapse`="영상 업로드 중" · `Finalizing`="마무리 중".
⚠️ **콜백 순서를 가정한 단언을 테스트에 쓰지 않는다**(06 §4.5 — Windows에서 flakiness 원인이었다).

> **관측된 규격 차이(수용)**: 06 §4.5는 진행률을 "파일 크기 가중 합산"이라 쓰지만 Step 2가 이식한
> `overallProgress`는 **활성 단계 균등 가중**이고(사진+영상+마무리 = 각 1/3), Windows `ComputeOverall`은
> 또 달라 사진 0~0.5 / 영상 0.5~1 / 마무리 1.0이다. 셋 다 **표시값**일 뿐 계약이 아니며
> `docs/spec-vectors/`에 이 함수의 벡터가 **없다**(교차 고정 대상이 아니다). 이번 Step은 **이미 이식돼
> 테스트로 고정된 `overallProgress`를 그대로 쓴다** — 도메인을 바꾸면 Step 2의 테스트를 함께 고쳐야 하고
> 얻는 것이 표시 곡선뿐이다. 차이는 `11-wbs.md` Step 11 이탈 항목에 기록한다.

`QrCanvas`(같은 파일 내 소컴포넌트):
```tsx
const ref = useRef<HTMLCanvasElement>(null);
useEffect(() => {
  const canvas = ref.current;
  if (canvas === null) return;
  const matrix = createQrMatrix(text);
  if (matrix === null || !drawQrToCanvas(canvas, matrix, QR_TARGET_PX)) setFailed(true);
}, [text]);
```
- 실패 시 문구: "QR을 만들 수 없습니다. 아래 [기기에 저장]으로 받아 주세요." (신규 `STRINGS.upload.qrRenderFailed`)
- `alt` 대체: `<canvas role="img" aria-label="다운로드 페이지 QR 코드" />`.
- **`downloadPageUrl` 원문을 화면에 텍스트로 노출하지 않는다**(스캔용이고, 손님이 옆 사람 링크를 손으로 읽어 갈 수 있다).

### 6.4 `src/screens/done/doneAutoHome.ts` + `DoneView.tsx`

```ts
export const DONE_AUTO_HOME_MS = 6_000;

export interface DoneAutoHomeDeps {
  readonly now?: () => number;                 // 기본 performance.now
  readonly setTimer?: (fn: () => void, ms: number) => unknown;
  readonly clearTimer?: (handle: unknown) => void;
  readonly onExpire?: () => void;              // 기본 shellStore.returnHome("완료 화면 자동 복귀")
  readonly target?: Pick<EventTarget, "addEventListener" | "removeEventListener">; // 기본 document
  readonly isHidden?: () => boolean;           // 기본 document.visibilityState === "hidden"
}

/** 시작하고 **정리 함수**를 돌려준다. 언마운트에서 반드시 호출한다. */
export function startDoneAutoHome(deps?: DoneAutoHomeDeps): () => void;
```

| 규칙 | 내용 |
|------|------|
| **실경과 기반** | 진입 시 `deadline = now() + 6000`. 타이머 만료 시 `now() >= deadline`이면 복귀, 아니면 남은 만큼 **재무장**(탭 스로틀 방어 — WM3와 동종) |
| 탭 hidden 복귀 | `visibilitychange`에서 visible이면 **즉시 재판정**(이미 지났으면 바로 홈) |
| 정리 | 타이머 clear + `removeEventListener` — 정리 함수 1개로 둘 다(§11) |
| 로그아웃 | **하지 않는다**(03 §10 · M3) |
| 유휴 감시 | `Done`은 감시 대상이 아니다(F13) — 중복 홈 복귀가 없다 |

`DoneView`: 문구 1줄(브랜딩 `appName` + "이용해 주셔서 감사합니다.") + [처음으로] 버튼(즉시 복귀). `useEffect`에서 `startDoneAutoHome()`.

---

## 7. 문구 · 로그 · 마스킹

### 7.1 `src/ui/strings.ts` 변경

```ts
upload: {
  nothingToSend: "전송할 결과물이 없습니다.",
  inProgress: "업로드 중...",                                   // ← 카탈로그 표기(말줄임표 3점) 확인 후 통일
  stagePhoto: "사진 업로드 중",
  stageTimelapse: "영상 업로드 중",
  stageFinalizing: "마무리 중",
  /** ⚠️ 정정: 카탈로그 문구 전체를 쓴다(analysis/13 §14). */
  retentionNotice: "업로드된 사진·영상은 {n}시간 후 자동 삭제됩니다.",
  tempUserTimeExceeded: "무료 사용 시간이 지났습니다. 관리자에게 문의해주세요.",
  tempUserCountExceeded: "무료 사용 횟수가 소진되었습니다. 관리자에게 문의해주세요.",
  failedSaved: "전송 실패 — 사진은 기기에 저장되었습니다.",
  failedNotSaved: "전송에 실패했습니다. 로컬 저장을 켜면 기기에 보관됩니다.",
  qrRenderFailed: "QR을 만들 수 없습니다. 아래 [기기에 저장]으로 받아 주세요.",
  saveToDevice: "기기에 저장",
  saveToDevicePhoto: "사진 저장",
  saveToDeviceVideo: "영상 저장",
},
```
- 기존 `inProgress`("전송 중입니다…")는 카탈로그의 "업로드 중..."과 다르다 → **카탈로그로 맞춘다**.
  현재 어떤 코드도 이 키를 쓰지 않으므로 회귀가 없다(구현 시 `grep`으로 확인할 것).
- `formatCount`가 `{n}`을 치환한다(기존 헬퍼 재사용).

### 7.2 로그 항목 (진단·Step 16이 소비)

| 시점 | 메시지 | 컨텍스트 |
|------|--------|----------|
| 전송 대상 확정 | `업로드 대상 확정` | `{ uploadPhoto, uploadTimelapse, attempt }` |
| 옵션 on·파일 없음 | `사진 전송 옵션 on 이지만 결과 이미지가 없어 전송에서 제외` / `타임랩스 …` | `{}` |
| 대상 0 | `전송할 결과물이 없어 업로드를 시작하지 않음` | `{ sendPhoto, sendTimelapse }` |
| prepare | `업로드 prepare` | `{ kind, attempt, bucket }` |
| PUT 성공 | `서명 PUT 완료` | `{ kind, bytes, status, elapsedMs, headerNames }` |
| PUT 실패 | `서명 PUT 실패` | `{ kind, failure, status, elapsedMs, hint }` — `failure==="network"`면 `hint: "네트워크 또는 CORS 차단 가능 — 업로드 구성(CORS) 확인 필요"` |
| commit | `업로드 commit 완료` | `{ hasFinal, hasTimelapse, retentionHours, elapsedMs }` |
| 409 | `업로드 commit 충돌(이중 실행 의심)` | `{ attempt }` |
| 실패 총괄 | `업로드 실패` | `{ reason, attempt, elapsedMs }` |
| QR 렌더 | `QR 렌더` / `QR 렌더 실패` | `{ moduleCount, modulePx, canvasPx }` / `{ reason }` |

### 7.3 금지 (마스킹 함정 #1 — 15 §4)

| 금지 | 이유 |
|------|------|
| `putUrl`·`downloadUrl`·`downloadPageUrl` **원문** | 서명·다운로드 토큰이 들어 있다. URL 자체가 capability다 |
| `requiredHeaders`의 **값** | `x-goog-meta-firebaseStorageDownloadTokens` = 다운로드 토큰 |
| `sessionId` 원문 | 다운로드 페이지의 `?s=` 토큰과 같다 → 로그 유출이 곧 링크 유출이다. 필요한 진단은 `attempt`·`sameAsCaptureSession`으로 충분하다 |
| 컨텍스트 **키** `code`·`token`·`state`·`nonce`·`pin` | 마스킹 대상이라 값이 `[masked]`가 된다 → 서버 오류 코드는 `errorCode`(기존 관례) |

---

## 8. 테스트 전략

> vitest 환경은 **node**다(F21). 브라우저 객체는 전부 **주입 또는 최소 가짜**로 대체한다.

### 8.1 `tests/unit/domain/uploadAndFilters.test.ts` (수정)

- `planQrRender`: §3.1 판정표 4행 + `targetPx=0`·`moduleCount=-1` 방어.
- `exportFileName`: §3.2 표 5행 + `sessionId`에 UUID가 **포함되지 않음**을 단언(`expect(name).not.toContain("-")`).

### 8.2 `tests/unit/http/uploadGateway.test.ts` (신규)

가짜 XHR:
```ts
class FakeXhr {
  headers: [string, string][] = [];
  method = ""; url = ""; timeout = 0; status = 0; sent: Blob | null = null;
  upload = { onprogress: null as ((e: {lengthComputable:boolean;loaded:number;total:number}) => void) | null };
  onload: (() => void) | null = null; onerror: (() => void) | null = null;
  ontimeout: (() => void) | null = null; onabort: (() => void) | null = null;
  open(m: string, u: string) { this.method = m; this.url = u; }
  setRequestHeader(n: string, v: string) { this.headers.push([n, v]); }
  send(b: Blob) { this.sent = b; }
  abort() { this.onabort?.(); }
}
// createUploadGateway(client, { createXhr: () => fake as unknown as XMLHttpRequest })
```

| 케이스 | 단언 |
|--------|------|
| **M14 순회 부착** | `requiredHeaders`가 3개면 `setRequestHeader` **3회**, 이름·값·**순서**가 응답 객체의 `Object.entries`와 같다. `x-goog-meta-firebaseStorageDownloadTokens`가 포함된다 |
| 인증 헤더 미부착 | `fake.headers`에 `Authorization`·`X-MCPhoto-Client`가 **없다** |
| 진행률 | `upload.onprogress({lengthComputable:true,loaded:50,total:100})` → `onProgress({loaded:50,total:100})` 1회. `lengthComputable:false`면 **호출 0회** |
| 2xx | `status=204` → `{ok:true, status:204, bytes: blob.size}` |
| 4xx | `status=403` → `{ok:false, failure:"http", status:403}` — **던지지 않는다** |
| 네트워크 | `onerror()` → `{ok:false, failure:"network", status:null}` |
| 타임아웃 | `ontimeout()` → `failure:"timeout"`, `fake.timeout === 100_000` |
| 사전 취소 | 이미 aborted인 signal → `send` **호출 0회**, `failure:"aborted"` |
| 중도 취소 | `controller.abort()` → `xhr.abort()` 호출 + `failure:"aborted"`, **리스너가 제거된다**(같은 signal에 두 번 abort해도 resolve 1회) |
| 단일 settle | `onload` 후 `onerror`를 불러도 결과가 바뀌지 않는다 |
| **정적 불변식 3건** | §4.1 표 |

### 8.3 `tests/unit/screens/uploadRunner.test.ts` (신규)

가짜 `UploadGateway`(호출 로그 배열 방식 — `resultNext.test.ts` harness 선례).

| 케이스 | 단언 |
|--------|------|
| 정상(사진+영상) | 호출 순서 `["prepare:final","put:final","prepare:timelapse","put:timelapse","commit"]`. commit 본문의 `finalImageUrl`·`timelapseUrl`이 **prepare가 준 `downloadUrl` 그대로** |
| 사진만 | `prepare` 1회, commit `timelapseUrl: null`, `finalImageUrl` non-null |
| 영상만(SendPhoto off) | commit `finalImageUrl: null` |
| **M7** 둘 다 없음 | `phase.kind === "nothing"`, gateway 호출 **0회** |
| **M7** 토글 on·파일 없음 | 동상 + 경고 로그 |
| **M8** PUT 실패 | `commit` 호출 **0회**, `phase.kind === "failed"` |
| 진행률 | `overallProgress`와 같은 값이 `onPhase`로 온다. 값이 `[0,1]`을 벗어나지 않는다. **단계 라벨 순서를 단언하지 않는다**(06 §4.5) |
| `downloadPageUrl` | commit 본문이 `{HostingBaseUrl}/?s={sessionId}`이고 **kiosk 도메인이 아니다**(설정에 kiosk URL을 넣어도 그 값이 그대로 나가는지 = 조립 함수만 검증) |
| 세션 ID | `attempt=0` → `captureSessionId`와 **같다**. `attempt=1` → 다르고 `isValidSessionId` 통과. `captureSessionId`가 `null`·형식 위반 → 새로 만든다 |
| TempUser 403 | prepare가 `TempUserLimitError("time")` → `reason:"temp-user-time"`, `put`·`commit` 호출 0회 |
| 409 | commit이 409 → `reason:"conflict"` |
| 네트워크 | prepare가 `NetworkError` → `reason:"network"` |
| 취소 | 진행 중 `controller.abort()` → `{aborted:true}`, 이후 `onPhase` 호출 없음, commit 0회 |
| 문구 | `uploadFailureMessage` 4분기가 STRINGS 리터럴과 일치 |

### 8.4 `tests/unit/qr/qrService.test.ts` (신규)

| 케이스 | 단언 |
|--------|------|
| ECC | `QR_ECC_LEVEL === "Q"` + **소스 정적 검사**(`"L"`/`"M"`/`"H"` 리터럴 0건, `innerHTML` 0건) |
| 행렬 | 실제 길이의 `downloadPageUrl`(약 90자)로 `moduleCount > 0`, `isDark(0,0) === true`(좌상단 finder) — **A3 검증** |
| 렌더 | 가짜 canvas(`{width,height,getContext:()=>fakeCtx}`)로 `fillRect` 호출 수 = 배경 1 + 어두운 모듈 수, 첫 호출의 `fillStyle`이 `#ffffff` |
| quiet zone | `canvasPx === modulePx * (moduleCount + 8)` |
| 2D 컨텍스트 부재 | `getContext` → `null`이면 `drawQrToCanvas`가 **`false`**(예외 없음) |

### 8.5 `tests/unit/shell/qrUsage.test.ts` (신규)

| 케이스 | 단언 |
|--------|------|
| TempUser 로그인 | `service.fetch` **1회**, 이후 `isTempUserQrBlocked()`가 응답을 반영 |
| 비TempUser 로그인 | `fetch` **0회**, `isTempUserQrBlocked() === false` |
| 로그아웃 | 캐시가 비고 `false`. `fetch` 추가 호출 없음 |
| stale 폐기 | 조회 중 계정 교체 → 늦게 온 응답이 **반영되지 않는다** |
| fail-open | `fetch`가 `QR_USAGE_FAIL_OPEN`을 주면 `false` |
| 미조회 | 설치 직후(응답 전) `false` |
| 해제 | `installQrUsageLifecycle()`의 반환 함수 호출 후 계정을 바꿔도 `fetch` 호출 0회 |

### 8.6 `tests/unit/screens/doneAutoHome.test.ts` (신규)

| 케이스 | 단언 |
|--------|------|
| 6초 만료 | 주입 시계를 6000 진행 → `onExpire` 1회 |
| 조기 발화 방어 | 타이머가 3000에 발화해도(스로틀) `now`가 3000이면 `onExpire` **0회** + 재무장 |
| hidden 복귀 | hidden 중 시계가 10000 → visible 이벤트 → **즉시** `onExpire` |
| 정리 | 정리 함수 호출 후 시계를 아무리 돌려도 `onExpire` 0회, `removeEventListener` 호출됨 |

### 8.7 `tests/unit/screens/resultNext.test.ts` (수정 — 기존 단언 보존)

- **기존 12케이스 전부 그대로 통과해야 한다**(특히 `expect(h.calls).toEqual(["finishTimelapse","save","go"])`).
- 추가: `defaultResultNextDeps()`의 `isTempUserBlocked`가 `qrUsageStore`를 읽는지(스토어를 목으로 blocked 상태로 만들고 `Done`으로 가는지).
- 추가: 소스 정적 검사 — `resultNext.ts`에 `uploads/prepare`·`uploads/commit`·`runUpload` 문자열이 **0건**(§1.1 결정을 코드로 고정).

### 8.8 회귀 위험표

| 대상 | 변경 | 위험 | 완화 |
|------|------|------|------|
| `tests/unit/http/backendClient.test.ts` | 없음 | 낮 | `backendClient` 무변경 |
| `tests/unit/screens/resultNext.test.ts` | 케이스 추가 | **중** | 기존 단언 문자열을 건드리지 않는다. `calls`에 push하는 dep을 늘리지 않는다(F10) |
| `tests/unit/storage/resultSaver.test.ts` | 없음 | 낮 | `resultSaver` 무변경 |
| `tests/unit/shell/shell.test.ts` | 없음 | 낮 | `shellStore`·`ShellHooks` 무변경(§6.2에서 셸 훅을 늘리지 않기로 했다) |
| `tests/unit/domain/purity.test.ts` | 자동 포함 | 낮 | 신규 도메인 2파일이 브라우저 API·시각·난수를 쓰지 않는다 |
| `tests/unit/domain/vectors.test.ts` · `docs/spec-vectors/` | **무변경** | — | 이번 Step은 C# 교차 변경을 만들지 않는다 → `dotnet test` 불필요 |
| 커버리지 임계 | 도메인 2파일 추가 | 낮 | 두 파일 모두 표 기반 테스트로 100% 근처 |

**예상 테스트 증가**: 758 → **약 820~850**(도메인 ~12, uploadGateway ~14, uploadRunner ~18, qrService ~6, qrUsage ~7, doneAutoHome ~4, resultNext ~3, fileExport ~3).
정확한 수치는 구현 후 실측해 `11-wbs.md` Step 11 체크박스에 적는다(15 §5).

---

## 9. 구현 단계 (WBS 블루프린트 형식)

> 작업 디렉터리는 모든 단계에서 **`E:\Study\photobooth\webclient`** 다.
> 전 단계 공통 최종 게이트: `npx tsc --noEmit && npx vitest run` (기존 758건 + 신규 전부 녹색).
> **`git commit`·`git push`를 하지 않는다.**

### Step 11-1: 도메인 2파일 + 배럴 + 도메인 테스트
- **Context Brief**: 이번 Step의 순수 판정부만 만든다 — QR 렌더 기하(여백 4모듈·정수 모듈 픽셀)와 [기기에 저장] 파일명(P1 다운로드 페이지와 **같은 규칙**). `src/domain`은 아무것도 import하지 않고(도메인 내부 상대 경로만) `Date.now`·`Math.random`·브라우저 API·`console`을 부르지 않는다. `tests/unit/domain/purity.test.ts`가 glob으로 자동 검사한다. 배럴이 평면 `export *`라 **한정형 이름**을 쓴다.
- **대상 파일**: `src/domain/upload/qrRenderPlan.ts`(신규) · `src/domain/upload/exportFileName.ts`(신규) · `src/domain/index.ts`(2줄 추가) · `tests/unit/domain/uploadAndFilters.test.ts`(케이스 추가)
- **선행 조건**: 없음
- **구현 내용**: 설계 §3.1~3.3의 시그니처·판정표 그대로. `planQrRender`는 정수 배율 + 최소 1px 보장, `exportFileName`은 `isValidSessionId` 통과 시에만 스탬프 15자를 쓰고 **UUID를 파일명에 넣지 않는다**.
- **검증 명령**: `npx vitest run tests/unit/domain` · `npx vitest run --coverage` (src/domain 95/95/95/90) · `npx tsc --noEmit`
- **완료 기준**:
  - [관측] `planQrRender(41, 640)`가 `{modulePx:13, canvasPx:637, quietPx:52}`이고, `exportFileName("20260730_143022_a1b2c3d4-…","timelapse","Jpg") === "MCPhoto_20260730_143022_timelapse.mp4"`다.
  - [non-goal] `purity.test.ts`가 신규 2파일에서도 통과한다. `docs/spec-vectors/`와 `EXPECTED_VECTOR_NAMES`가 **변경되지 않는다**. 기존 758건이 그대로 통과한다.
  - [trigger] 두 함수는 인자만으로 결과가 결정된다 — 같은 입력에 항상 같은 출력.
- **롤백**: 신규 2파일 + 배럴 2줄 + 추가 테스트 블록 제거.
- [ ] 완료

### Step 11-2: `uploadGateway.put` — XHR 서명 PUT
- **Context Brief**: 업로드 3단계 중 ②다. **`fetch`를 쓰면 업로드 진행률을 얻을 수 없어**(WM5) XHR로 구현한다. 인증 헤더를 붙이면 서명 검증이 깨지거나 preflight가 막힌다 — **서명 URL 자체가 권한**이다. prepare가 준 `requiredHeaders`는 **객체를 순회해 전부** 붙인다(M14). 하나라도 빠지면 서명 불일치 403이거나 다운로드 토큰이 설정되지 않아 파일 GET이 불가능해진다. 어댑터는 예외를 전파하지 않는다(15 §2) — 실패를 판별 유니온으로 돌려준다. 서명 URL·헤더 값은 **로그에 남기지 않는다**.
- **대상 파일**: `src/adapters/http/uploadGateway.ts`(수정) · `tests/unit/http/uploadGateway.test.ts`(신규)
- **선행 조건**: 없음(11-1과 병렬 가능)
- **구현 내용**: 설계 §4.1의 타입·규칙 12항 그대로. `createUploadGateway(client, { createXhr, now })`로 XHR 주입점을 연다. `signal` abort 리스너는 settle 시 반드시 제거하고 resolve는 `settled` 플래그로 1회만. 테스트는 §8.2 전량 + **정적 불변식 3건**.
- **검증 명령**: `npx vitest run tests/unit/http` · `npx tsc --noEmit` · `npx vitest run`(전체 회귀)
- **완료 기준**:
  - [관측] `requiredHeaders`가 `{Content-Type, x-goog-meta-firebaseStorageDownloadTokens}`일 때 `setRequestHeader`가 **정확히 2회** 같은 이름·값으로 호출되고, `status=403`에서 함수가 **던지지 않고** `{ok:false, failure:"http", status:403}`을 돌려준다.
  - [non-goal] `uploadGateway.ts` 소스에 `Authorization`·`X-MCPhoto-Client`·`GATE_KEY_HEADER`·`getToken`·`fetch(`가 **0건**이다. `logger` 호출 인자에 `url`이 없다. 기존 `prepare`/`commit` 동작·`backendClient.test.ts`가 그대로 통과한다.
  - [trigger] `send`는 `signal.aborted === false`일 때만 호출된다 — 사전 취소면 요청이 나가지 않는다.
- **롤백**: `uploadGateway.ts`의 `put` 관련 추가분과 신규 테스트 파일만 제거(prepare/commit과 독립).
- [ ] 완료

### Step 11-3: `qrUsageStore` — `isTempUserBlocked` 실배선
- **Context Brief**: 지금 `resultNext.ts`의 `isTempUserBlocked`는 상수 `false`다. 서버 조회(`GET /accounts/me/qr-usage`)는 **계정 변경 시 1회 fire-and-forget**이고 판정은 **동기 캐시**여야 한다(Windows `AppShellViewModel`과 동형 — 비동기로 바꾸면 [다음]이 최대 100초 멈춘다). `role`을 먼저 봐야 한다: 비TempUser는 서버가 `remaining*: 0`을 주는데 그것은 "소진"이 아니라 **"무제한"**이다. 조회 실패는 **fail-open**(허용) — 과금 안전은 서버가 prepare/commit에서 담보한다.
- **대상 파일**: `src/shell/qrUsageStore.ts`(신규) · `src/shell/sessionStore.ts`는 **건드리지 않는다** · `src/screens/result/resultNext.ts`(1줄 + 주석 블록 교체) · `src/main.tsx`(설치 1줄) · `tests/unit/shell/qrUsage.test.ts`(신규) · `tests/unit/screens/resultNext.test.ts`(케이스 추가)
- **선행 조건**: 없음
- **구현 내용**: 설계 §5의 인터페이스·동작표 그대로. `installQrUsageLifecycle()`은 `sessionStore.subscribe(s => s.currentUser, …)`로 구독하고 해제 함수를 돌려준다(`subscribeWithSelector` 미들웨어가 이미 있다). 조회 시작 시점의 사용자와 응답 시점 사용자가 다르면 **폐기**. `resultNext.ts`는 `isTempUserBlocked: () => isTempUserQrBlocked()`로 바꾸고 예약 주석 블록을 §5의 사유 주석으로 교체한다.
- **검증 명령**: `npx vitest run tests/unit/shell tests/unit/screens/resultNext.test.ts` · `npx tsc --noEmit` · `npx vitest run`
- **완료 기준**:
  - [관측] `temp_user`로 로그인하면 `service.fetch`가 **1회** 호출되고 응답의 `blocked:true`가 `isTempUserQrBlocked() === true`로 반영되어 `runResultNext`가 `"Done"`으로 간다.
  - [non-goal] `role: "user"`·`"admin"`·게스트에서 `fetch` 호출 **0회**이고 판정이 항상 `false`다. 조회 실패·미조회에서도 `false`다(fail-open). `resultNext.test.ts`의 기존 12케이스가 **문자열 수정 없이** 통과하고 `calls`가 여전히 `["finishTimelapse","save","go"]`다.
  - [trigger] 조회는 **계정이 temp_user로 바뀔 때만** 일어난다 — 화면 전이·[다음] 클릭으로는 요청이 나가지 않는다.
- **롤백**: `qrUsageStore.ts` 삭제 + `resultNext.ts`의 dep를 `() => false`로 되돌림 + `main.tsx` 1줄 제거.
- [ ] 완료

### Step 11-4: QR 어댑터 + 의존성 추가
- **Context Brief**: QR 오류정정 레벨은 **Q**여야 한다(Windows `QrService.cs`의 `ECCLevel.Q`와 일치 — VF-13). 라이브러리는 **CDN 런타임 로드를 하지 않고**(01 §7) 번들에 포함하며 **정확 버전 핀**으로 넣고 `THIRD-PARTY.md`에 먼저 적는다(15 §6 Step 9 선례). 라이선스는 **MIT/Apache-2.0 계열만** 허용된다. 렌더는 라이브러리의 `createImgTag`/`createSvgTag`(HTML 문자열)를 쓰지 않고 **canvas에 직접 그린다** — `innerHTML` 경로를 만들지 않기 위해서다. 배경은 흰색 고정이고 다크모드에서도 반전하지 않는다(스캐너 호환).
- **대상 파일**: `webclient/package.json` · `webclient/THIRD-PARTY.md` · `src/adapters/qr/qrService.ts`(신규) · `tests/unit/qr/qrService.test.ts`(신규)
- **선행 조건**: Step 11-1(`planQrRender`)
- **구현 내용**: `npm i qrcode-generator@2.0.4 --save-exact`(**캐럿 없는 정확 핀** — MIT · 런타임 의존 0 · 자체 `.d.ts` 동봉). `THIRD-PARTY.md` 표에 한 행 추가(패키지·버전·MIT·용도 "QR 코드 모듈 행렬 생성(ECC Q)"·비고 "런타임 의존 0, 표면 4개(`qrcode`·`addData`·`make`·`isDark`)라 교체 비용이 한 파일에 국한"). 설계 §4.2의 시그니처·규칙 그대로.
- **검증 명령**: `npx vitest run tests/unit/qr` · `npx tsc --noEmit` · `npx vite build` · `node -e "const p=require('./package.json');if(p.dependencies['qrcode-generator']!=='2.0.4')process.exit(1)"`
- **완료 기준**:
  - [관측] 실제 길이의 `https://mcphoto-955fb.web.app/?s=20260730_143022_…`(약 90자)로 `createQrMatrix`가 `moduleCount > 0`인 행렬을 주고, `drawQrToCanvas`가 가짜 canvas에 배경 1회 + 어두운 모듈 수만큼 `fillRect`를 호출한다. `npx vite build`가 성공한다.
  - [non-goal] `package.json`의 버전에 캐럿·틸드가 **없다**. `qrService.ts` 소스에 `"L"`·`"M"`·`"H"` ECC 리터럴과 `innerHTML`이 **0건**이다. `THIRD-PARTY.md`에 항목이 있다. 2D 컨텍스트가 없으면 `false`를 돌려주고 **예외를 던지지 않는다**.
  - [trigger] QR 생성은 `createQrMatrix(text)` 호출에만 일어난다 — 모듈 로드 시 아무것도 계산하지 않는다.
- **롤백**: `npm uninstall qrcode-generator` + `qrService.ts`·테스트·`THIRD-PARTY.md` 행 제거.
- [ ] 완료

### Step 11-5: 합성 Blob 인계 + `fileExport`
- **Context Brief**: 합성 결과 Blob은 `useResultCompose`의 **React ref**에 있어 `Result`가 언마운트되면 접근 경로가 사라진다. `Qr` 화면이 그것을 올려야 하므로 **세션 컨텍스트로 인계**한다. 타임랩스는 싱글턴 서비스가 들고 있어 인계가 필요 없다. 함께 [기기에 저장](WD3 3계층 중 ③)의 브라우저 다운로드 어댑터를 만든다 — `URL.createObjectURL`을 만들면 **반드시 revoke**해야 하고, 즉시 revoke하면 일부 브라우저가 다운로드를 취소하므로 다음 태스크로 미룬다.
- **대상 파일**: `src/shell/sessionStore.ts`(필드+setter+해제) · `src/screens/result/useResultCompose.ts`(1줄) · `src/adapters/platform/fileExport.ts`(신규) · `tests/unit/storage/platform.test.ts`(케이스 추가)
- **선행 조건**: Step 11-1(`exportFileName`)
- **구현 내용**: 설계 §1.2·§4.3. `FinalImageArtifact = { blob, format }`를 `sessionStore`에 두고 `discardCaptureData()`에서 `null`로 만든다(`ImageBitmap`과 달리 `close()`가 없다). `useResultCompose`는 합성 성공 직후 `setFinalImage({ blob: result.blob, format: values.OutputFormat })`. `exportBlob`은 기능 감지 → objectURL → `a.click()` → `setTimeout(revoke, 0)`, 실패는 `false`.
- **검증 명령**: `npx vitest run tests/unit/storage tests/unit/shell` · `npx tsc --noEmit` · `npx vitest run`
- **완료 기준**:
  - [관측] `sessionStore.setFinalImage(a)` 후 `discardCaptureData()`를 부르면 `finalImage`가 `null`이다. `exportBlob`이 `download` 미지원 환경에서 `false`를 돌려주고 **예외가 나지 않으며**, 지원 환경에서는 `createObjectURL` 1회 뒤 **`revokeObjectURL`이 1회** 호출된다.
  - [non-goal] `ImageBitmap` 해제 로직(`releaseThumbnails`)이 변경되지 않는다. `useResultCompose`의 `blob:` URL revoke 로직·`currentBlob()` 반환이 그대로다. `resultNext.ts`의 `finalBlob` dep 경로가 그대로다(회귀 표면 최소화).
  - [trigger] 인계는 **합성이 성공했을 때만** 일어난다 — 합성 실패·취소에서는 `setFinalImage`가 호출되지 않는다.
- **롤백**: 세 파일의 추가분 제거(`Qr` 화면이 아직 없으므로 소비자 없음).
- [ ] 완료

### Step 11-6: `uploadRunner` — 3단계 오케스트레이션
- **Context Brief**: 이 Step의 심장이다. **전송 대상 확정(설정 토글 AND 파일 존재) → prepare(파일당 1회) → 서명 PUT → commit**. 둘 다 없으면 **요청을 아예 보내지 않는다**(M7). PUT이 실패하면 **commit을 호출하지 않는다** — 사진만 성공한 채 commit하면 P1이 `timelapseUrl:null`을 "옵션 꺼짐"으로 표시해 실패를 은폐한다(M8). commit에는 prepare가 준 `downloadUrl`을 **그대로** 넘기고(서버가 버킷·경로 소속을 검증한다), `downloadPageUrl`은 **P1 사이트 도메인**이어야 한다 — kiosk 도메인으로 바꾸면 QR이 앱을 열어 버린다. [재시도]는 **새 세션 ID로 전 과정 재실행**이다(같은 ID 재commit은 409). React를 import하지 않아 node에서 통째로 검증된다(`runResultNext`와 같은 형태).
- **대상 파일**: `src/screens/qr/uploadRunner.ts`(신규) · `src/ui/strings.ts`(upload 블록) · `tests/unit/screens/uploadRunner.test.ts`(신규)
- **선행 조건**: Step 11-2(`put`), Step 11-5(`finalImage`)
- **구현 내용**: 설계 §6.1의 타입·실행 순서·오류 매핑표·문구표 그대로. `signal.aborted`를 **매 단계 사이와 commit 직전**에 확인한다. `STRINGS.upload`를 §7.1대로 보강하고 `retentionNotice`를 카탈로그 문구로 **정정**한다(기존 사용처 0건을 `grep`으로 확인할 것).
- **검증 명령**: `npx vitest run tests/unit/screens/uploadRunner.test.ts` · `npx tsc --noEmit` · `npx vitest run`
- **완료 기준**:
  - [관측] 사진+영상 시나리오에서 호출 순서가 `prepare(final) → put(final) → prepare(timelapse) → put(timelapse) → commit`이고, commit 본문의 두 URL이 prepare가 준 `downloadUrl`과 **문자 단위로 같다**. `downloadPageUrl`이 `{HostingBaseUrl}/?s={sessionId}`다.
  - [non-goal] 전송 대상이 0이면 gateway 호출이 **0회**이고 `phase.kind === "nothing"`이다(M7). PUT 실패 시 `commit` 호출이 **0회**다(M8). `attempt=0`이면 세션 ID가 촬영 세션 ID와 **같고**, `attempt=1`이면 다르며 `isValidSessionId`를 통과한다. 취소 시 `onPhase`가 더 호출되지 않고 commit이 0회다.
  - [trigger] 업로드는 `runUpload` 호출에만 일어난다 — 모듈 로드·`resolveUploadTargets` 계산으로는 아무 요청도 나가지 않는다.
- **롤백**: `uploadRunner.ts`·테스트 삭제 + `strings.ts` 되돌리기(소비자 없음).
- [ ] 완료

### Step 11-7: `Qr`·`Done` 화면 + 라우팅
- **Context Brief**: `Qr`은 상단바가 숨는 몰입 화면이고 유휴 감시 대상이다. **QR은 업로드 성공 후에만** 렌더한다(M5) — 실패 시에는 사유별 문구와 [재시도]만 보인다. 실패해도 [완료]로 진행할 수 있어야 한다(결과물은 이미 로컬에 있다). `Done`은 **6초 실경과** 후 자동 홈이며 **로그아웃하지 않는다**. 개발 빌드는 `<StrictMode>`라 effect가 2회 실행되므로 업로드 실행은 **effect마다 자기 `AbortController`를 만드는** 형태여야 한다(전역 실행 잠금을 쓰면 두 번째 실행이 영구 스킵된다).
- **대상 파일**: `src/screens/qr/useUploadRun.ts`(신규) · `src/screens/done/doneAutoHome.ts`(신규) · `src/ui/views/QrView.tsx`(신규) · `src/ui/views/DoneView.tsx`(신규) · `src/ui/views/screens.module.css`(수정) · `src/App.tsx`(`ScreenRouter` 2케이스) · `tests/unit/screens/doneAutoHome.test.ts`(신규)
- **선행 조건**: Step 11-3, 11-4, 11-6
- **구현 내용**: 설계 §6.2~6.4. `useUploadRun`은 `useEffect([runKey])` 안에서 controller를 만들고 cleanup에서 abort한다. `QrView`는 phase별 4분기 + [기기에 저장] 버튼(파일 수만큼, `canExportFile()`이 true일 때만) + [재시도](failed일 때만) + [완료]. `doneAutoHome`은 실경과 재무장 + `visibilitychange` 즉시 재판정 + 정리 함수 1개.
- **검증 명령**: `npx vitest run tests/unit/screens` · `npx tsc --noEmit` · `npx vite build` · `npx vitest run`(전체)
- **완료 기준**:
  - [관측] `doneAutoHome`이 주입 시계 6000ms에서 `onExpire`를 **정확히 1회** 부르고, 타이머가 일찍 발화하면 재무장하며, hidden 중 시간이 지났으면 visible 즉시 복귀한다. `npx vite build`가 성공한다.
  - [non-goal] `QrView`가 `phase.kind !== "succeeded"`에서 **QR canvas를 렌더하지 않는다**(M5). `nothing`·`failed`에서도 [완료] 버튼이 항상 있다. `Done`에서 `sessionStore.logout()`이 호출되지 않는다. 제품 코드에 **QR 게이트 우회 경로가 없다**(`grep -rn "forceQr\|skipQrGate\|bypass" src/` 0건). `App.tsx`의 기존 라우팅·모달·더미 화면이 그대로다.
  - [trigger] 업로드는 `Qr` 화면 진입과 [재시도] 클릭에만 시작된다 — `Result` 화면에서는 요청이 나가지 않는다. 자동 홈 복귀는 `Done` 진입에만 무장된다.
- **롤백**: 신규 4파일 삭제 + `App.tsx`의 2케이스 제거(더미 화면으로 복귀) + CSS 되돌리기.
- [ ] 완료

### Step 11-8: 문서 갱신 + 실측 등재
- **Context Brief**: Step을 끝내면 WBS 체크박스에 산출물·검증 수치·**설계 이탈**·남은 실측을 적는 것이 이 저장소의 관례다(15 §5) — 그게 다음 세션의 진입점이다. 15 §6은 "다음 Step이 알아야 할 것"만 남기는 문서이므로 Step 11 절을 **완료 형태로 다시 쓰고**, §7의 수치·완료 목록도 **함께** 갱신해 다른 Step의 서술이 stale이 되지 않게 한다. 브라우저·폰이 필요한 검증은 추정 통과 처리하지 않고 14 §10에 등재한다.
- **대상 파일**: `docs/web-client/11-wbs.md`(Step 11 체크박스) · `docs/web-client/15-implementation-conventions.md`(§1 표·§6 Step 11 절·§7 요약) · `docs/web-client/14-handoff-and-user-actions.md`(§10에 **V20** 신설, §10.5에서 해소된 항목 정리) · `docs/design/README.md` §3.1(이 설계 문서 등재 — 이미 등재돼 있으면 확인만)
- **선행 조건**: Step 11-1 ~ 11-7
- **구현 내용**: (a) WBS Step 11에 산출물·**실측 테스트 수치**·설계 이탈(§12 목록)·미검증 항목. (b) 15 §6의 "Step 11 업로드·QR" 절을 **완료 기록**으로 교체하고 뒤 Step이 알아야 할 것만 남긴다(업로드는 `Qr` 화면 소유 · `sessionStore.finalImage` 인계 · `qrUsageStore`가 `isTempUserBlocked` 공급 · `qrcode-generator` 도입). (c) 15 §1 재개 절차의 **758 → 실측치**, §7 표의 완료·테스트 수치를 함께 고친다. (d) 14 §10에 **V20**(브라우저·폰 실측) 표를 만들고 §10.5의 "업로드 OPTIONS/PUT"·"폰 QR 스캔" 행을 갱신한다 — 폰 스캔은 **Step 12 로그인 이후**라는 조건을 남긴다.
- **검증 명령**: `npx tsc --noEmit && npx vitest run` (전체 녹색·수치 기록) · `npx vitest run --coverage` · `npx vite build` · `grep -n "Step 11" docs/web-client/11-wbs.md docs/web-client/15-implementation-conventions.md` · `grep -n "V20" docs/web-client/14-handoff-and-user-actions.md`
- **완료 기준**:
  - [관측] 세 명령이 모두 성공하고 테스트 수가 758에서 증가했으며 그 **실측치**가 `11-wbs.md`와 `15 §1·§7`에 **같은 값**으로 적혀 있다. 14 §10에 V20 항목이 있다.
  - [non-goal] 다른 Step(9·10·12~16)의 서술이 stale이 되지 않는다 — 특히 15 §1 표의 "다음 Step"과 §7의 완료 목록이 Step 11 완료를 반영한다. `docs/spec-vectors/`가 변경되지 않았으므로 `dotnet test`는 **수행 대상이 아니다**(그 사실도 기록한다).
  - [trigger] 문서 갱신은 코드 동작을 바꾸지 않는다 — 이 단계에서 `src/` 파일을 수정하지 않는다.
- **롤백**: 문서 되돌리기(코드 산출물에 영향 없음).
- [ ] 완료

---

## 10. 완결성 게이트 (js-developer 전달 전 자체 검사)

- [x] 검증된 사실(F1~F24) / 미검증 가정(A1~A5)이 분리돼 있다
- [x] 모든 가정에 검증 단계가 매핑돼 있다(A1→V20·S11-8, A2→S11-2·V20-3, A3→S11-4, A4→V20-4, A5→S11-6·§6.2)
- [x] 8개 단계 전부에 필수 7필드(Context Brief·대상 파일·선행 조건·구현 내용·검증 명령·완료 기준·롤백)가 있다
- [x] 모든 완료 기준이 관측·non-goal·trigger 3문 형식이다(UI 단계 11-7 포함)
- [x] 검증 명령이 전부 자동 실행 가능한 CLI다
- [x] 각 Step이 self-contained다(그 절만 읽고 실행 가능 — 배경·불변식·함정을 Context Brief에 담았다)

## 11. 설계 자체 점검 (js-architect 체크리스트)

- [x] **부수효과 해제 경로**: XHR `signal` 리스너 → settle 시 `removeEventListener`(§4.1 규칙 10) · `URL.createObjectURL` → `setTimeout(revoke, 0)`(§4.3) · `doneAutoHome` 타이머 + `visibilitychange` → 정리 함수 1개(§6.4) · `installQrUsageLifecycle` → 해제 함수 반환(§5) · `useUploadRun` effect → cleanup abort(§6.2) · 신규 `ImageBitmap`·`MediaStream`·`setInterval` **0개**
- [x] **상태 소유권**: 업로드 상태는 `Qr` 화면 로컬(`phase`)이고 전역 스토어에 넣지 않는다 → 홈 복귀 시 지울 상태가 늘지 않는다. 인계된 `finalImage`만 `sessionStore`에 있고 `discardCaptureData` 한 경로가 지운다
- [x] **비동기 취소·오류**: `signal`이 prepare(backendClient)·PUT·commit 전 구간을 관통하고, 모든 실패가 `UploadPhase`/`SignedPutOutcome` 판별 유니온으로 축소된다. 자동 재시도 없음(사용자 액션만)
- [x] **TS strict 전제**: `UploadPhase`·`SignedPutOutcome`·`ResultSavePlan`(기존)이 판별 유니온이라 분기 누락이 컴파일에서 잡힌다. `UploadStage`는 기존 도메인 리터럴 유니온
- [x] **보안**: `innerHTML` 0건(QR은 canvas 렌더) · 서명 URL·다운로드 토큰·세션 ID를 **로그에 남기지 않는다**(§7.3) · 서명 PUT에 자격 증명 미부착(정적 검사) · QR 문자열을 화면에 텍스트로 노출하지 않는다 · 파일명에 UUID 미포함(§3.2)
- [x] **권한 거부/오류 UI**: 03 §9.2 문구표 4분기를 `uploadFailureMessage`가 전담하고, 어떤 실패도 [완료]를 막지 않는다. 게스트·TempUser 초과는 `Qr`에 **도달하지 않는다**(VF-11)
- [x] **접근성**: 진행률 `aria-live="polite"` · QR canvas `role="img" aria-label` · 버튼 48px(기존 `Button` 토큰) · QR 배경 흰색 고정(다크모드 반전 금지)
- [x] **추가 질문 없이 구현 가능한 상세도**: 전 시그니처·판정표·호출 순서·정적 불변식·테스트 케이스표·기존 코드 인용 포함

---

## 12. 설계 이탈 · 확인 필요 (오케스트레이터 판단 요청)

| # | 항목 | 설계 판단 | 되돌리기 비용 |
|---|------|-----------|---------------|
| **①** | **업로드 실행 위치**: 지시문·`resultNext.ts` 예약 주석은 `runResultNext` 안, 설계는 **`Qr` 화면** | §1.1의 근거 4가지(Windows F5 · 03 §9.1 · [재시도] 중복 진입점 · 진행률 UI). M6-W는 구조적으로 유지되고 `resultNext.test.ts`도 무변경 | **낮음** — `runUpload`가 단일 함수라 호출 지점 이동 약 10줄 |
| ② | Playwright E2E 2종(`upload-qr`·`guest-flow`)을 만들지 않음 | 저장소에 Playwright 설치·설정이 없다. E2E 도입은 Step 17 범위 | 중간(도구 도입 필요) |
| ③ | `prepare`의 `bucket`으로 설정 `StorageBucket`을 갱신하지 않음 | 웹은 URL을 재조립하지 않는다 + `GUEST_LOCKED_KEYS` 축 오염 회피. 값은 로그로만 | 낮음 |
| ④ | 서명 PUT이 예외 대신 **판별 유니온**을 돌려줌(06 §4.2 예시 코드는 `reject`) | 15 §2 저장소 관례("어댑터는 예외를 전파하지 않는다")를 따랐다. 06 §4.2 코드는 예시이며 계약은 "XHR로 진행률"이다 | 낮음 |
| ⑤ | `STRINGS.upload.retentionNotice`·`inProgress`를 `analysis/13 §14` 카탈로그 문구로 **정정** | 01 §8이 "카탈로그와 1:1"을 규정한다. 현재 사용처 0건이라 회귀 없음 | 낮음 |
| ⑥ | `Qr` 화면 이탈 시 업로드를 **중단**(commit 전이면 세션 문서가 생기지 않음) | 손님이 떠난 뒤 TempUser 카운트를 소모하지 않는 편이 옳다. 이미 올라간 바이트는 TTL로 정리된다 | 낮음 |
| ⑦ | 진행률 가중치가 06 §4.5("파일 크기 가중")·Windows `ComputeOverall`(0.5/0.5)과 다름 — 이식된 `overallProgress`의 **균등 가중**을 그대로 씀 | 표시값이고 계약이 아니다. 벡터 교차 고정 대상도 아니다. 도메인을 바꾸면 Step 2 테스트를 함께 고쳐야 한다(§6.3 주) | 낮음 |

### 확인이 필요한 모호점

1. **①이 최우선 확인 대상이다.** 지시문을 그대로 따르라면 §1.1의 4가지 문제(특히 [재시도] 이중 진입점)를 어떻게 처리할지 지시가 필요하다.
2. **QR 라이브러리 확정**: `qrcode-generator@2.0.4`를 추천한다(MIT · 런타임 의존 0 · 자체 타입 · 표면 4개). 대안 `qrcode@1.5.4`는 MIT이지만 `pngjs`·`yargs`·`dijkstrajs`를 끌고 온다(브라우저 번들에 불필요). **정확 핀 + `THIRD-PARTY.md` 기록**은 어느 쪽이든 동일하게 적용한다. 사내 라이선스 승인 절차가 있다면 도입 전 확인이 필요하다.
3. **A1(서명 PUT 실동작)은 이 Step에서 자동 검증이 불가능하다.** `web/OPS-cors.md`가 preflight 프로브 절차를 갖고 있지만 200 응답 실측은 브라우저가 필요하다 → **V20으로 등재하고 "미검증(사용자 액션)"으로 기재**한다.
