# web-fix (2026-08-06) · 모바일 카메라 획득 실패 — 폴백 배선 (P0)

| 항목 | 값 |
|------|-----|
| 문서 | "모바일 웹에서 카메라를 못 불러온다"의 **원인 진단 + P0 수정 내역** |
| 증상 | PC 웹은 정상. 모바일에서 카메라가 열리지 않거나, 열려도 화면이 검다 |
| 범위 | **P0(긴급 복구)만.** 사용자 승인 범위 — 카메라가 "열리게" 만드는 것에 집중 |
| 대상 기기 | **안드로이드 태블릿 Chrome · iPad Safari 17+**(사용자 확정) |
| 작성 | 2026-08-06 |
| 검증 | typecheck 통과 · vitest **2034**건 통과(+30 신규) · Playwright chromium **29**건 통과 · 프로덕션 빌드 성공 |

---

## 0. 진단 — 하나의 사슬이었다

### 0.1 근본 원인: 설계가 요구한 폴백이 코드에 없었다

[`04 §2.3.1`](../web-client/04-media-pipeline-web.md)과 [`10 §6.2`](../web-client/10-testing-and-acceptance.md)는
`OffscreenCanvas` 2D와 `transferControlToOffscreen` 부재 시의 폴백을 **명시적으로 요구**했다.
판정 함수까지 작성됐지만 **배선되지 않았다.**

| # | 사실 | 근거(수정 전) |
|---|------|---------------|
| **V-1** | `isWorkerPipelineSupported()`의 호출처가 **0건**이었다(dead code) | `frameProcessorClient.ts:27` 정의 · 전체 코드베이스 grep 0건 |
| **V-2** | `cameraService`가 무조건 Worker를 띄웠다 | `cameraService.ts:107` `deps.createProcessor ?? (() => spawnFrameProcessor())` |
| **V-3** | 프리뷰 비트맵 폴백 경로가 **프로토콜에 존재하지 않았다** | `frameProcessorProtocol.ts:23-37` 응답 3종에 비트맵 없음. 그런데 `CameraPreview.tsx:61` 주석은 *"실패하면 Worker가 비트맵을 보내는 경로로 동작한다"* 라고 **거짓 서술** |
| **V-4** | 이관 실패 시 `bindPreview`가 `false`만 돌려주고 끝 | `cameraService.ts:381` |
| **V-5** | `frameRate: { min: 15 }` — Windows에 대응물이 없는 웹 전용 **hard 제약** | `cameraService.ts:94` vs `OpenCvCameraService.cs:88-92`(요청만 하고 실패 시 기본값 폴백) |
| **V-6** | 유일한 폴백이 `{audio:false, video:true}` — 해상도·`facingMode`가 통째로 소실 | `cameraService.ts:222` |
| **V-7** | `<video>`가 `display:none` | `videoFrameSource.ts:35` |
| **V-8** | 가공기 생성이 `try` 밖 — `start()`가 예외를 던질 수 있었다("예외를 던지지 않는다" 계약 위반) | `cameraService.ts:314` |
| **V-9** | `webExtras.CameraFacing`이 **저장만 되고 적용되지 않았다** | 편집 UI `SettingsView.tsx:202-212` · 저장 `cameraDevicePanel.ts:78` · `camera.start()` 호출 2곳 모두 `facing` 미전달 → `cameraService.ts:97`이 항상 `"user"` |
| **V-10** | Ready 타임아웃 사유가 전부 `unknown` | `cameraService.ts:341` |
| **V-11** | **WC3 장치 폴백 체인이 프로덕션에서 한 번도 실행되지 않았다** | `matchDevice`(`deviceEnumerator.ts:37`)·`resolveSelectedDevice`·`storedDeviceRef`(`cameraDevicePanel.ts:44,56`)의 호출처가 **테스트뿐**. 화면들은 `values.CameraDevice`를 raw로 `start()`에 전달(`useCaptureRunner.ts:109`) → 함께 기록한 `CameraDeviceLabel`·`CameraDeviceGroupId`는 **쓰기만 되고 읽히지 않는 죽은 값** |

### 0.2 실패 사슬

```
OffscreenCanvas 없음 ─┐
new Worker 실패 ──────┼→ processed 응답 0건 → 8초 타임아웃 → Failed("unknown")
display:none로 rVFC 미발화 ─┘                                    └ 화면: "카메라를 사용할 수 없습니다"
                                                                    (권한 문제와 구분 불가)

transferControlToOffscreen 불가 → bindPreview false → 프리뷰 미연결
                                                       └ 상태는 Ready · 화면은 검은색 · 실패 보고도 없음

frameRate.min 강제 → OverconstrainedError → {video:true} → 640×480 후면 카메라
```

**PC에서 잘 되는 이유도 같다.** 데스크톱 Chrome은 모든 API가 있어 최적 경로만 타므로 폴백 부재가
드러나지 않는다.

### 0.3 배경 — 실기기 검증 미수행

[`16 실기기 검증 절차서`](../web-client/16-field-verification-runbook.md): 115항목 중 **103개 미수행**.
Android 태블릿(S6)·iPad(S7) 세션은 **통째로 미착수**. 모바일 실기기 검증을 통과한 적이 없다.

---

## 1. 수정 내역

### 1.1 제약 사다리 (V-5·V-6)

**신규**: `adapters/camera/cameraConstraints.ts`(순수)

넓은 것 → 좁은 것 5칸. `frameRate`는 `ideal`만. 저장된 장치가 없으면 1·2칸을 건너뛴다.

```
1 device+1080p → 2 device+720p → 3 facing+1080p → 4 facing → 5 any
```

- 다음 칸 진행 여부는 `shouldTryNextStep(err.name)` — **권한 거부에서 즉시 중단**(제약을 낮춰도
  결과가 같고 손님만 기다린다). `NotReadableError`는 계속 내려간다(해상도를 낮추면 열리는 기기가 있다).
- 사유는 **마지막 실패**로 확정한다(마지막 칸이 `{video:true}`라 그 실패가 가장 정확하다).
- 열린 칸을 `constraintStep()`으로 노출 → 진단 [적용된 제약] 행.

### 1.2 메인 스레드 가공기 (V-1·V-2)

**신규**: `adapters/camera/mainThreadProcessor.ts`

`FrameProcessor`를 Worker 없이 구현한다. 거울·중앙 크롭에 **같은 `centerCrop` 순수 함수**를 쓰므로
픽셀이 갈라지지 않는다(WM1 유지). 세 소비자 공유 구조도 동일. `HTMLCanvasElement.toBlob`으로 인코딩.

`spawnFrameProcessor()`가 판정 주체가 됐다:

```ts
if (!isWorkerPipelineSupported()) return createMainThreadProcessor();
try { return createFrameProcessorClient(new Worker(...)); }
catch { return createMainThreadProcessor(); }
```

`isWorkerPipelineSupported()`는 **`OffscreenCanvas`를 실제로 하나 만들어 `getContext("2d")`까지
확인**한다 — 생성자만 있고 2D가 `null`인 구현이 있다.

### 1.3 프리뷰 비트맵 폴백 (V-3·V-4)

`FrameProcessor.bindPreview`의 시그니처를 바꿨다:

```ts
- bindPreview(canvas: OffscreenCanvas): void   // cameraService가 이관을 직접 시도
+ bindPreview(canvas: HTMLCanvasElement): boolean  // 방식은 가공기가 정한다
```

Worker 클라이언트가 **이관 → 비트맵 → 실패** 순으로 내려간다. 프로토콜에 `previewChannel`(요청)과
`previewFrame`(응답)을 추가했고, Worker는 `processFrame`의 **맨 마지막**에 `transferToImageBitmap()`을
보낸다(캔버스를 비우므로 스틸·스풀보다 앞에 두면 컷이 빈 이미지가 된다).

`previewMode()` 4상태를 진단에 노출한다: `transferred` / `bitmap` / `direct` / `none`.
**`none`이 곧 "화면이 검다"** 이며 `bad` tone이다.

### 1.4 캔버스 세대 (신규 발견 버그)

`CameraPreview`가 카메라 재시작마다 `key`를 증가시켜 `<canvas>` DOM 노드를 갈아 끼운다.

이관된 캔버스는 **재이관도, 메인에서 `getContext("2d")`도 불가**하다. 세대 교체가 없으면
[다시 시도] 이후 새 가공기가 프리뷰를 붙일 수단이 전혀 없어 **화면이 영구히 검은색**이 된다.
(P0 복구 경로 자체를 무력화하는 버그였다.)

### 1.5 예외 포착과 사유 분리 (V-8·V-10)

- 가공기 생성을 `try`로 감싸고 실패를 `Failed("pipelineStalled")`로 흡수한다.
- Ready 타임아웃: 프레임 **0장**이면 `pipelineStalled`, 프레임은 오는데 조건 미달이면 `unknown`.
- `CameraFailureReason`에 `pipelineStalled`를 추가했다. `Record` 매핑 3곳이 컴파일로 강제되어
  문구·retryable·진단 라벨이 자동으로 함께 갱신된다(기존 설계 의도대로).

### 1.6 `<video>` 숨김 교정 (V-7)

`display:none` → **1×1 투명 고정 배치** + `aria-hidden="true"`. 렌더링 트리에 남겨야 WebKit에서
`requestVideoFrameCallback`이 돈다.

### 1.7 WC3 장치 폴백 배선 (V-11)

**신규**: `deviceEnumerator.resolveStartDeviceId()` + `hasStoredDevice()`

촬영과 카메라 테스트 모달이 **열거 → 해석 → `start()`** 순서를 타게 했다. deviceId는 브라우저·OS
재시작·권한 재부여로 바뀌고 **모바일에서는 사실상 매번 무효**이므로, 라벨·groupId 폴백이 없으면
"저장한 카메라가 사라졌다"가 반복된다 — 파일 헤더 주석이 경고하던 바로 그 시나리오가 방어되지
않고 있었다.

⚠️ **`matchDevice`의 결과를 그대로 쓰지 않는다.** `first`(저장 장치 소멸 시 첫 장치)와 `none`은
`null`로 접어 `facingMode` 경로로 보낸다. 모바일에서 첫 장치가 후면인 기기가 많아, `first`를
강요하면 전면 설정을 조용히 뒤집는다. 사다리 3·4칸이 `facing`을 쓰므로 의도가 보존된다.

> **범위 밖(보고만)**: 설정 화면의 선택 표시도 raw 비교다(`SettingsView.tsx:175`). 저장된
> deviceId가 무효면 어떤 버튼도 선택으로 보이지 않는다. 다만 운영자가 다시 고르면 새 deviceId가
> 저장되어 자동 복구되므로 실질 피해가 작다. `resolveSelectedDevice()`가 이미 있으니 배선만 남았다.

### 1.8 `CameraFacing` 배선 (V-9)

사다리에 `facing` 파라미터를 이미 만들었으므로 호출측 2곳(실촬영 · 카메라 테스트 모달 3개 호출부)을
연결했다. **설정 UI가 손님에게 거짓을 말하던 상태를 해소**한다.

> P1로 분류했던 항목이지만, 사다리 인프라가 생겨 배선이 수 줄로 끝나고, iPad에서 후면 카메라가
> 필요한 경우 이것이 없으면 카메라를 아예 쓸 수 없어 P0에 포함했다.

---

## 2. 정적 불변식 (신규)

| ID | 고정 대상 | 없었을 때 |
|----|-----------|-----------|
| **CAM-2** | `frameRate`에 `min`·`exact` 금지 | 저조도 안드로이드가 튕기고 폴백이 해상도·전후면을 버린다 |
| **CAM-3** | `<video>`에 `display:none` 금지 | WebKit에서 프레임 콜백이 멈춰 프레임 0 |
| **CAM-4** | 폴백 배선 유지(`isWorkerPipelineSupported` 호출 · `createMainThreadProcessor` 참조 · `new Worker`가 `try` 안 · `cameraService`가 `transferControlToOffscreen`을 직접 부르지 않음) | dead code 상태로 회귀 |
| **CAM-5** | `CameraFacing`이 `camera.start()`에 도달 + 설정 UI 존속 | 저장은 되고 적용은 안 되는 설정 |
| **CAM-6** | WC3 폴백이 프로덕션 경로에 배선(`resolveStartDeviceId` 호출 · raw 전달 금지 · 라벨/groupId 실제 읽힘) | deviceId가 바뀔 때마다 엉뚱한 카메라 |

기존 **CAM-1**(`getUserMedia` 소유자 2파일)은 그대로 유지된다.

---

## 3. P1 이후로 남긴 것 (사용자 범위 결정)

| # | 항목 | 왜 남겼나 |
|---|------|-----------|
| **R-1** | 권한 프롬프트 중 `visibilitychange` hidden 억제 | `visibility.ts:34-39`가 Capture에서 hidden이면 `returnHome()`. Guide에서 권한을 미리 받지 않고 직행하면 프롬프트 표시 순간 촬영이 취소될 수 있다(브라우저 의존) |
| **R-2** | orientation 변화 대응 | 세로↔가로 전환 시 획득 해상도·크롭 재판정 없음 |
| **R-3** | 인앱브라우저(카카오톡·인스타) 감지 + 외부 브라우저 안내 | `getUserMedia`가 아예 차단되는 경우가 많다. 사용자 확정 대상 기기가 태블릿이라 후순위 |
| **R-4** | 폰 폼팩터 레이아웃 | `10 §6.1`상 폰은 등급 B. 프리뷰가 세로 화면에서 넘칠 수 있다 |
| **R-5** | Wake Lock 복귀 재획득 | `visibility.ts:46`이 visible 복귀에서 재요청하므로 실질 영향 작음 |
| **R-6** | 합성 성능 예산 재측정 | 합성은 `ImageData` CPU 경로만 쓴다(WebGL2 미사용). 메인 스레드 가공기와 겹치면 태블릿에서 예산 초과 가능 |
| **R-7** | 설정 화면 카메라 선택 표시를 `resolveSelectedDevice()` 경유로 | §1.7 각주. 저장 deviceId 무효 시 선택 표시가 사라진다(자동 복구되므로 경미) |

### 3.1 카메라 밖에서 발견된 같은 유형의 미배선 (범위 밖 · 사용자 판단 필요)

전 화면 점검에서 **`CameraFacing`과 정확히 같은 클래스**(저장·편집은 되는데 런타임 소비처 없음)가
카메라 밖에도 있었다. 카메라 P0 범위가 아니므로 손대지 않았다.

| # | 항목 | 상태 | 심각도 |
|---|------|------|--------|
| **X-1** | `values.StorageBucket` | 설정 [고급]에 TextField로 **편집 가능**한데 소비처 0건. 진단 [버킷] 행조차 `env.storageBucket`을 읽는다(`DiagnosticsModal.tsx:89`) → 값을 바꿔 저장하면 "저장했습니다" 토스트만 뜨고 아무 데도 반영되지 않는다 | **중대** — 접속 구성처럼 보여 `CameraFacing`보다 오인 위험이 크다 |
| **X-2** | `values.BackendBaseUrl` · `values.GoogleClientId` | 소비처 0건(전부 `env`). `SETTINGS_HIDDEN_KEYS`에 **없어서** `isSettingEditable()`이 `true`인데 UI에도 없다 — 정책과 UI가 어긋난 유일한 축 | 중 (노출은 안 되므로) |
| **X-3** | 12 C3이 약속한 "타임랩스 미지원 시 **Guide 하단 안내**" | 진단 쪽만 구현. Guide에 문구 없고 `STRINGS`에 해당 문구 자체가 없다 | 중 |
| **X-4** | 12 E4/C1이 약속한 "[보관된 결과물] **내보내기**" | 패널에 버튼 없음(목록·용량·삭제만). 전용 API `ResultsStore.readFile()`은 호출처 0건. C1이 "OPFS에 갇힌 결과물을 꺼내는 유일한 수단"으로 지목한 경로라 공백이 크다 | 중 |
| **X-5** | 12 C8이 약속한 "진단에서 **최근 로그 항목 조회**" | 건수·기간만. `LogStore.recent()` 호출처 0건 | 경미 |
| **X-6** | `keyboardLock.unlockKeys()` 호출처 0건 | `fullscreenController.exit()`가 unlock을 부르지 않아 키오스크 종료 후 키보드 잠금이 남을 수 있다 | 경미 |

권고: **X-1은 둘 중 하나로 결정해야 한다** — ① `SETTINGS_HIDDEN_KEYS`에 넣어 미노출로 되돌리고
문서 12 D 표에 행 추가, 또는 ② 진단 [버킷]과 업로드가 설정값을 읽게 만들기. 지금 상태는
"편집 가능한 거짓 설정"이라 어느 쪽도 아니다.

---

## 4. 실기기 검증이 남아 있다

**이 수정은 코드로 닫을 수 있는 것만 닫았다.** 실제 기기에서 어느 경로를 타는지는
진단 모달의 신설 3행으로 확인해야 한다:

| 진단 행 | 정상(태블릿 기대값) | 이상 신호 |
|---------|---------------------|-----------|
| [가공 경로] | `Worker` | `메인 스레드(저성능)` → 성능 예산 재측정 필요(R-6) |
| [프리뷰 경로] | `캔버스 이관(zero-copy)` | `비트맵 전송(폴백)`은 동작하나 느림 · **`미연결`은 화면이 검다** |
| [적용된 제약] | `device+1080p` 또는 `facing+1080p` | `any` → 장치가 요청을 계속 거절하고 있다 |

[`16 §S6·S7`](../web-client/16-field-verification-runbook.md)의 Android 태블릿·iPad 세션을
수행해야 이 수정의 효과가 확인된다. 그 전까지 **"고쳤다"가 아니라 "고칠 수 있는 것을 고쳤다"** 가
정확한 상태 서술이다.
