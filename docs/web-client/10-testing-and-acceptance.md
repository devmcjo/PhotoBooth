# 10 · 테스트와 수락 기준

| 항목 | 값 |
|------|-----|
| 문서 | 무엇을 어떻게 검증하면 "Windows와 동일하게 동작한다"고 말할 수 있는가 |
| 근거 | `docs/design/multiplatform-client-architecture.md §3.1`(드리프트 방지 장치) · `docs/analysis/05 §11`(적합성 체크리스트) |
| Windows 테스트 | `tests/MCPhoto.Tests/` (721개) — **테스트 벡터 추출원** |
| 서버 테스트 | `web/functions/src/__tests__/` (Jest) |
| 갱신 규칙 | 규격 변경 시 **벡터 파일 → 각 클라이언트 테스트** 순으로 반영 |

---

## 1. 테스트 계층

| 계층 | 도구 | 대상 | 목표 |
|------|------|------|------|
| **1. 도메인 단위** | Vitest | `src/domain/**`(순수 함수) | **커버리지 95%+**. 여기가 "동일 동작"의 본체다 |
| **2. 공유 벡터** | Vitest + `docs/spec-vectors/*.json` | 도메인 함수 | **Windows와 같은 입력 → 같은 출력** |
| **3. 어댑터 단위** | Vitest + 목 | HTTP·저장소 | 헤더 부착·에러 매핑·저장 실패 반환 |
| **4. 화면 로직** | Vitest + React Testing Library | `src/screens/**` | 권한 가드·상태 전이·문구 |
| **5. 골든 이미지** | Vitest + `pixelmatch` | 합성·필터 | Windows 결과와 허용 오차 내 |
| **6. E2E** | Playwright(Chromium·WebKit) | 전 흐름 | 불변식 재현(특히 M1) |
| **7. 실기기 수동** | 체크리스트 | 카메라·인코딩·메모리 | 브라우저 매트릭스(§6) |

---

## 2. 도메인 단위 테스트 — 반드시 있어야 하는 케이스

Windows 테스트와 **1:1 대응**시킨다. 왼쪽이 웹 테스트 파일, 오른쪽이 대응 Windows 테스트다.

| 웹 테스트 | Windows 대응 | 필수 케이스 |
|-----------|--------------|-------------|
| `stateMachine.test.ts` | `AppStateTests.cs`, `AppShellOverlayReturnTests.cs` | 전이표 전수 · 오버레이 항상 허용 · `from == to` 거부 · 불법 전이 거부 · **`isOverlayScreen` 13값 전수 분류 + 오버레이 간 전환 시 복귀 지점 미저장(it19)** |
| `idleCountdown.test.ts` | `IdleCountdownTests.cs` | 120초 판정 · 10초 카운트다운 · 활동 무시 |
| `centerCrop.test.ts` | `CropCalculatorTests.cs` | 넓은/좁은 원본 · 정사각 · `targetAspect<=0` · **정수 나눗셈 경계** · **반올림 중간값(.5) 케이스**(은행가 반올림 — `roundHalfToEven` 검증) |
| `mathCompat.test.ts` | (C# `Math.Round` 의미론) | `roundHalfToEven`: `66.5→66`·`67.5→68`·`-1.5→-2`·`-0.5→0` — JS `Math.round`와 갈라지는 값 전수 |
| `previewReadiness.test.ts` | `PreviewReadinessTests.cs` | 3조건 각각 미달 · 전이 1회만 · 타임아웃 |
| `captureSession.test.ts` | `CaptureSessionTests.cs` | `begin()`이 `cutCountPolicy.resolve`로 실효 컷 수를 산출하고 **`isAutoCutCount`를 함께 기록** · 토글·번호 재계산 · 슬롯 초과 거부 · 전체 재촬영 카운터 · **재촬영이 `cutCount`를 재해석하지 않음**(it17) |
| `slotLayout.test.ts` | `SlotLayoutTests.cs` | `autoArrange` 1~6개 · 세로 스트립(aspect<0.6) · `scaleSlots` 원본 기준 · 클램프 · 겹침 판정 |
| `editorTransform.test.ts` | `EditorTransformTests.cs` | 레터박스 원점 · 왕복 변환 · `scale<=0` 방어 |
| `frameOrigin.test.ts` | `FrameOriginTests.cs` | `local:`·`bundle:`·`fallback`·빈 id·서버 id 분류 |
| `frameEditPolicy.test.ts` | `FrameEditPolicyTests.cs` | 1차 게이트(쓰기 권한) · 출처별 편집/삭제 · **소유자 미검사 규칙** |
| `frameNaming.test.ts` | `FrameNamingTests.cs` | 사본 이름 1~99 · base 되돌림 · 난수 폴백 · 빈 이름 |
| `slotsFile.test.ts` | `LocalFrameStoreTests.cs` | 메타 대소문자 무시 · 손상 줄 무시 · `#dbid` 유무 · `#imagesize` 부재 |
| `appSettings.test.ts` | `SettingsTests.cs` | 전 키 기본값 · 최근접 보정 · **자동 sentinel `0` 보정 제외(왕복 보존) + `-1`은 6으로 보정** · `RetentionHours` clamp · **두 URL 정규화 방향 반대** |
| `cutCountPolicy.test.ts` | `CutCountPolicyTests.cs` | `isAuto`(0만 자동, -1 아님) · `resolve` 자동/고정 각 케이스(슬롯 0~6, 7 산출 포함) · 슬롯 미확정(≤0) 폴백 |
| `qrDeliveryPolicy.test.ts` | `QrDeliveryPolicyTests.cs` | 정규화 · 재활성 · 하위 값 보존 |
| `qrEffectivePolicy.test.ts` | `QrEffectivePolicyTests.cs` | 진리표: **미로그인 → false(raw 무관)** · TempUser 한도 초과 → false · 정상 TempUser·`user` 이상 → raw 그대로 · **raw=true·초과 시 effective=false지만 입력 raw는 불변**(오버라이드 확인) |
| `tests/unit/domain/settingsAndRoles.test.ts`·`accounts.test.ts` | `RoleManagementTests.cs` | `isPower` · `canWriteFrames` · `canManage`(**동급 허용**) · `canResetPin`(**동급 차단**) · 알 수 없는 역할 → `user` |
| `roleChangePolicy.test.ts` | `RoleManagementTests.cs` | `assignableRoles` 전수 매트릭스 · admin 지정 불가 · 순서 오름차순 |
| `uploadContract.test.ts` | `UploadContractTests.cs` | 세션 ID 정규식 · 경로 조립 · 토큰 URL 인코딩(`%2F`) · 다운로드 페이지 URL · 만료 계산 |
| `uploadOrchestration.test.ts` | `UploadServiceTests.cs` | 최소 1개 불변식 · 3단계 순서 · 진행률 합산(**순서 비의존**) |
| `timelapseSpeed.test.ts` | `FfmpegArgsTests.cs` | `computeSpeedFactor` 표(12/15/30/60/120초) |
| `frameCatalogPolicy.test.ts` | `FrameCatalogServiceTests.cs` | 우선순위 4단 · **이름 dedup** · 서버 미도달 폴백 |
| **`frameLoadPolicy.test.ts`**(it20) | `FrameLoadPolicyTests.cs`(13건) | `classify` 진리표(0개→Failed / 중단→Degraded / 그 외 Ready) · `finalize`의 **quiet 갈래에서 `Loading`을 반환하지 않는다**(오버레이 고착 불가) · `nextDeadline`(무진행 30초 vs 잔여 총량 중 **먼저 오는 쪽**, 0 이하 → 즉시 취소) · 국면별 안내 문구 |
| **`frameCatalogProgress.test.ts`**(it20) | `FrameCatalogProgressTests.cs`(5건) | 단계별 문구 · `total>0`일 때만 `(n/m)` 부착 · 보고 전 시작 문구 |
| **`frameNaming.test.ts` 증분** | `FrameNamingTests.cs`(+39줄) | **`isFileNameSafe`**: 빈 값·공백만 → false, 금지문자 → false, **길이는 보지 않는다**(100자 초과도 true — `validateFrameName`과 축이 다르다) |

---

## 3. 공유 테스트 벡터 (드리프트 방지 — 가장 중요한 장치)

### 3.1 원리

```
docs/spec-vectors/*.json      ← 플랫폼 중립 (입력 → 기대 출력)
        ├── Windows 테스트가 읽는다  (tests/MCPhoto.Tests)
        └── 웹 테스트가 읽는다      (webclient/tests/vectors)
```

같은 파일을 양쪽이 읽으므로 **한쪽만 바뀌면 즉시 실패**한다. 손으로 대조하지 않는다.

### 3.2 만들 벡터 파일

| 파일 | 내용 | 추출원 |
|------|------|--------|
| `center-crop.json` | `{srcW, srcH, targetAspect} → {x,y,w,h}` 30+ 케이스. **반올림 중간값(.5) 케이스 필수**(은행가 반올림 검증) | `CropCalculatorTests.cs` |
| `auto-arrange.json` | `{slotCount, frameW, frameH, targetAspect} → Slot[]` | `SlotLayoutTests.cs` |
| `scale-slots.json` | `{baseSlots, factor, frameW, frameH} → Slot[]`. **중간값(.5) 케이스 필수**(`cx - newW/2`가 .5로 떨어지는 입력 포함) | 동상 |
| `clamp-slot.json` | 편집기용·합성용 **두 식 각각** | 동상 + `CompositionTests.cs` |
| `overlap.json` | 겹침·경계 접촉 케이스 | `SlotLayoutTests.cs` |
| `editor-transform.json` | `{canvasW,canvasH,frameW,frameH} → {scale,originX,originY}` + 왕복 | `EditorTransformTests.cs` |
| `role-matrix.json` | `{actor, current} → assignableRoles[]` 전수 + `canManage`·`canResetPin` | `RoleManagementTests.cs` |
| `copy-name.json` | `{원본이름, 기존이름목록} → 사본이름` | `FrameNamingTests.cs` |
| `session-id.json` | `{now, uuid} → sessionId` + 경로·URL 조립 | `UploadContractTests.cs` |
| `timelapse-speed.json` | `sessionSeconds → factor` | `FfmpegArgsTests.cs` |
| `settings-clamp.json` | `{입력 설정} → {보정된 설정}` (자동 sentinel 0 보존 케이스 포함) | `SettingsTests.cs` |
| `cut-count.json` | `{configured, slotCount} → {resolved, isAuto}` | `CutCountPolicyTests.cs` |
| `qr-normalize.json` | QR 토글 정규화·재활성 | `QrDeliveryPolicyTests.cs` |
| `slots-file.json` | `.slots` 텍스트 → 파싱 결과(손상 줄 포함) | `LocalFrameStoreTests.cs` |
| **`frame-load-policy.json`**(it20 — **미작성**) | `{frameCount, waitInterrupted} → phase` + `{current, frameCount, waitInterrupted, quiet} → phase` + `{elapsedMs} → nextDeadlineMs` | `FrameLoadPolicyTests.cs` |

> 14파일 271케이스가 작성돼 있다. **`frame-load-policy.json`은 Step 14 착수 시 추가한다** — Windows 쪽 `FrameLoadPolicyTests.cs`가 이미 13건으로 판정을 고정하고 있으므로 §3.3 절차대로 그쪽에서 덤프해 추출한다.

### 3.3 추출 절차 (1회)

1. Windows 테스트에서 입력·기대값을 JSON으로 덤프하는 임시 테스트를 추가해 `docs/spec-vectors/`에 쓴다.
2. 그 테스트를 **벡터를 읽어 검증하는 형태로 바꾼다**(덤프 코드는 제거).
3. 웹 테스트가 같은 파일을 읽어 검증한다.
4. 이후 규격 변경은 **벡터 파일을 먼저 고친다** → 양쪽 테스트가 동시에 실패 → 양쪽을 고친다.

---

## 4. 골든 이미지 (합성·필터 픽셀 검증)

### 4.1 구성

| 항목 | 내용 |
|------|------|
| 고정 입력 | 컷 4장(체커보드·그라데이션·피부톤 패치·고주파 패턴) + 프레임 PNG(1200×1600, 슬롯 4개) |
| 입력 위치 | `docs/spec-vectors/golden/input/` (양쪽이 공유) |
| 기준 출력 | Windows 앱으로 생성한 `expected-{filter}.png` 4장(원본·흑백·밝게·뷰티) |
| 비교 | `pixelmatch`로 평균 절대 오차(MAE)·최대 차이 계산 |

### 4.2 허용 오차

| 필터 | MAE 허용 | 최대 차이 허용 | 근거 |
|------|:--------:|:--------------:|------|
| 원본 | **≤ 1.0/255** | ≤ 4/255 | 리사이즈 보간 차이만 |
| 흑백 | ≤ 1.5/255 | ≤ 5/255 | BT.601 직접 계산이므로 반올림 차이만 |
| 밝게 | ≤ 1.5/255 | ≤ 5/255 | 동상 |
| **뷰티** | **≤ 3.0/255** | ≤ 12/255 | bilateral 근사([04 §6.2](./04-media-pipeline-web.md)) |
| 슬롯 위치 | **0px 오차** | — | 정수 연산이 §04 §9 표대로면 정확히 일치해야 한다 |

> **슬롯 위치가 1px이라도 다르면 실패로 처리한다.** 픽셀 색은 근사가 허용되지만 **기하는 계약**이다.

### 4.3 JPEG 인코딩 차이

Windows(OpenCV libjpeg)와 브라우저 JPEG 인코더는 **바이트가 다르다**. 비교는 **디코드된 픽셀**로 한다(파일 해시 비교 금지).

---

## 5. E2E 시나리오 (Playwright)

**구현 완료(Step 17).** 하네스·판정 근거는 [`design/web-step17-e2e-and-acceptance.md`](../design/web-step17-e2e-and-acceptance.md).

| 항목 | 실제 |
|------|------|
| 위치 | `webclient/tests/e2e/**` · 설정 `webclient/playwright.config.ts` |
| 실행 | `npm run e2e:install`(1회) → `npm run e2e` |
| 대상 | **dev 서버(5173)**. 배포본(SW·CSP·실서버)은 실측이 소유한다([16](./16-field-verification-runbook.md)) |
| 목 백엔드 | **같은 오리진** `/__mock-api/`. 교차 오리진이면 CORS preflight(OPTIONS)를 `page.route`가 가로채지 못한다 |
| 카메라 | `--use-fake-device-for-media-stream`의 **기본 합성 패턴**. y4m 파일은 커밋하지 않는다(결정적 픽셀은 골든 이미지가 이미 고정한다 — §4) |
| 로그인 | `src/`에 백도어를 만들지 않는다. authorize 이동을 하네스가 가로채 `state`를 읽고 `/oauth2callback`을 재현해 **실제 `oauthCallbackRunner`** 를 태운다 |

**판정 3종**: **자동**(spec이 전부 검증) · **부분**(핵심은 자동, 나머지는 실측 V) · **불가**(실측 V가 소유).

| # | 시나리오 | 검증 포인트 | 불변식 | 자동화 | spec / 남는 것 |
|---|----------|-------------|:------:|:------:|----------------|
| E1 | 게스트 촬영 완주 | 홈 → 프레임 → 가이드 → 촬영(6컷) → 컷선택 → 결과 → **`Done`(QR 건너뜀)**. 업로드 요청 **0건** | — | **자동** | `guest-flow` / 실카메라 품질 = V1·V14 |
| E1b | 로그인 촬영 완주 | 같은 흐름에서 `Result` → **`Qr`** → 업로드 성공 → QR canvas 렌더 → [완료] → `Done` | — | **자동** | `upload-qr` / 폰 스캔 = V21-5 |
| E2 | 업로드 3단계 | 호출 순서 `prepare → PUT → commit` · PUT에 **`requiredHeaders` 전량** · `authorization`·`x-mcphoto-client` **부재** · commit의 `downloadPageUrl`이 **P1 도메인** | M14 | **부분** | `upload-qr` / ⚠️ **`OPTIONS 204`는 관측 불가** — 목 `putUrl`이 같은 오리진이라 preflight가 발생하지 않는다. 실왕복은 **V20-1·V20-2** |
| E3 | ~~로그아웃 후 익명 업로드에 토큰 부재~~ **재정의** | ⚠️ **웹에는 이 상황이 존재하지 않는다.** ① 게스트는 `Qr`에 도달할 수 없고(VF-11 · E23) ② `auth:"required"` 호출은 토큰이 없으면 **요청 자체가 나가지 않는다**. `qrEffectivePolicy`를 목으로 `true` 고정하는 것은 **모듈 치환 또는 소스 백도어**를 요구해 "테스트한 앱 ≠ 배포할 앱"이 된다 → 아래 3개로 분해했다 | **M1** | **재정의** | 다음 세션이 다시 목을 만들려 시도하지 않도록 이 판정을 남긴다 |
| E3-1 | 로그인 업로드에 Bearer가 **있다** | prepare 헤더 `Authorization: Bearer <A>` | **M1** | **자동** | `auth-session` |
| E3-2 | 로그아웃 후 같은 흐름 | `uploads/*` 요청이 **0건**(익명 업로드 사건 자체가 발생하지 않는다) | **M1** | **자동** | `auth-session` |
| E3b | 재로그인 후 토큰 교체 | A로 로그인 → 로그아웃 → B로 로그인 → prepare의 Bearer가 **B**다(A의 잔존이 아니다) | **M1** | **자동** | `auth-session` |
| E4 | JWT 미저장 | 로그인 상태에서 `localStorage`·`sessionStorage`·쿠키·**전 IndexedDB 레코드**에 토큰 문자열 0건 | **M2** | **자동** | `auth-session`(WebKit에서도 실행) / 배포본 관측 = V21-4 |
| E5 | 유휴 타임아웃 | `page.clock`으로 120초 → 경고 모달 → 10초 → 홈. **계정 라벨 유지**(로그아웃 아님) | M3 | **자동** | `idle-and-recovery` / 실기기 체감 = V11 |
| E6 | 저장 실패 표시 | 저장소 쓰기 실패 → 실패 토스트 + 전이 계속 | M4 | **불가** | ⚠️ **자동화 레버가 없다.** CDP `Storage.overrideQuotaForOrigin(origin, 0)`을 실제로 걸면 `navigator.storage.estimate().quota`는 0이 되지만 **OPFS 쓰기(2 MiB)는 그대로 성공**한다(Chromium 131 실측). `navigator.storage.getDirectory`를 지우는 방법은 컷조차 못 읽어 `finalBlob === null` → `skipped`(토스트 없음)가 되어 **다른 경로를 보게 된다**. 판정은 `resultSaver` 단위 테스트가 고정하고, **할당량 소진 실관측은 V19-6**이 소유한다 |
| E7 | 업로드 실패 시 QR 미노출 | prepare 500 → QR 없음 + 사유 문구 + [완료] 활성 · **commit·PUT 0건**(M8) | M5 | **자동** | `upload-qr` / 오프라인 실동작 = V20-6 |
| E8 | **로컬 보관이 업로드보다 먼저** | `uploads/prepare` **라우트 핸들러 안에서** OPFS를 열어 `results/{폴더}/final.jpg`가 **이미 있음**을 확인 | **M6-W** | **자동** | `offline-storage` / 실기기 지연 = V19-2 |
| E9 | 최소 1개 불변식 | 사진·타임랩스 둘 다 off → `normalizeQrToggles`가 QR을 끈다 → `Done` + `uploads/*` 0건 | M7 | **부분** | `upload-qr` / ⚠️ `Qr`의 "전송할 결과물이 없습니다."는 **설정만으로 도달할 수 없다**(두 토글이 꺼지면 QR 자체가 꺼진다). 그 분기는 `tests/unit/screens/uploadRunner.test.ts`가 요청 0건까지 고정한다 |
| E10 | 프레임 고정 | `Capture`의 버튼이 [취소]·[바로 촬영] **둘뿐**이고 프레임 카드가 0개 | M11 | **자동** | `guest-flow` |
| E11 | 컷 선택 개수 | 3/4에서 [다음] 비활성 · 4/4에서 활성 · 5번째 클릭 무효 | M12 | **자동** | `guest-flow` |
| E12 | 세션 ID 형식 | prepare 본문 `sessionId`를 **도메인 `isValidSessionId`로** 검사(정규식 재작성 금지) + commit이 같은 ID를 쓴다 | M13 | **자동** | `upload-qr` |
| E13 | 프레임 이름 `_` | ⚠️ **축이 셋이다**: 서버 등록 = **하드 거부** / 로컬 저장 = **비차단 경고** / 저장 전 선검증 = `_` 무관. 따라서 관측 지점은 **서버 등록 체크 on**이다 — 체크 on + `a_b` → 거부 문구 + `POST /frames` **0건**, 체크 off → **저장 성공 + 경고만** | M15 | **부분** | `frame-authoring` / 실서버 등록 = V24-4 |
| E14 | 전역 예외 복구 | `setTimeout`에서 throw → 홈 + `error.temporary` 토스트 + **로그인 유지** | M16 | **자동** | `idle-and-recovery` |
| E15 | 권한 게이트 | `user` 역할 → `FrameSelect`에 [프레임 만들기]·[선택 편집] 부재 | M10 | **부분** | `roles-and-pin` / "액션 직접 호출 거부"는 **내부 함수 호출**이라 E2E 범위 밖 — `frameSelectActions` 단위 테스트가 고정 |
| E16 | PIN 게이트 | verify 401 × 5 → 모달 닫힘 + `localStorage["mcphoto.pinLock.v1"]` 존재 → **리로드 후 재로그인** → 잠금 문구·키패드 미노출·서버 요청 0건 | M9·WD16 | **자동** | `roles-and-pin` / 실계정 = V22-5 |
| E17 | PIN 401 오해 방지 | 1회 오입력 → `(1/5)` + **계정 라벨 유지**(로그아웃 아님) | — | **자동** | `roles-and-pin` / 실계정 = V22-3 |
| E18 | 역할 매트릭스 | manager: 다른 manager 행에 [삭제] 있고 [PIN] 없음 · 하위 대역 콤보에 admin·manager 없음 · admin 행·자기 행 액션 없음 · **좁은 뷰포트 카드에서도 동일** | — | **자동** | `roles-and-pin`(+ 목록 실패 변형) / 실계정 = V25-5 |
| E19 | 탭 hidden 취소 | `document.hidden` 덮어쓰기 + `visibilitychange` → 홈 + OPFS `sessions/` **비어 있음** | **WM4** | **부분** | `guest-flow` / ⚠️ 진짜 탭 전환(프레임 스로틀링)은 만들 수 없다 → **V16** |
| E20 | 오프라인 촬영 | 백엔드 미도달 → 프레임 폴백 + **안내 문구 없음**(`Ready` 유지) + 촬영·보관 성공 | — | **부분** | `offline-storage` / ⚠️ `context.setOffline(true)`는 dev에 SW가 없어 **앱 문서 자체를 못 받는다** → 앱 셸 오프라인은 **V25-1**(배포본) |
| E21 | 새로고침 | 촬영 중 `reload()` → 홈 + `sessions/` 비어 있음 · `results/` 무영향 | — | **자동** | `guest-flow` |
| E22 | 문구 카탈로그 | 6화면(Home·Login·FrameSelect·Settings·Account·UserMgmt + 진단)의 문구가 `@ui/strings` 값과 **문자열 일치** | — | **부분** | `strings-catalog` / 카탈로그 ↔ `analysis/13 §14` **문서 대조는 사람 검토** |
| E23 | **게스트 QR 게이트** | `Result` [다음] → `Qr`을 건너뛰고 `Done` · `uploads/*` 0건 · **저장된 `EnableQrDelivery` 불변** | — | **자동** | `guest-flow` |
| E24 | **TempUser 한도 초과 게이트** | `qr-usage` blocked → `Done` + 설정 불변 → 해제 후 **재로그인** → `Qr` 진입 | — | **자동** | `upload-qr` / 실계정 한도 = V22-7 계열 |

**요약: 자동 17 · 부분 7 · 불가 1(E6) · 재정의 1(E3 → E3-1/E3-2/E3b).**

### 5.1 E2E가 하지 않는 것 (중복 금지)

[`15 §3.4`](./15-implementation-conventions.md)의 40+ 정적 불변식은 **테스트가 소스를 읽어** 고정한다. E2E로 다시 확인하지 않는다.

| 이미 고정된 것 | 고정 수단 | E2E에서 하지 않는다 |
|----------------|-----------|---------------------|
| WM1 CSS 반전 금지 · `<video>` 미렌더 | 정적 grep | 픽셀 좌우 비교 |
| M2 / M2-a / M2-b 저장소 경계 | 소스 grep | **E4는 예외** — "실제로 브라우저 저장소에 없는가"는 grep이 증명할 수 없다 |
| AUTH-1~5 · PIN-1~5 · SET-1~5 · FR-1~15 · ACC-1~4 · SW-1~3 · DIAG-1 | 소스 grep | 소스 문자열 검사 재현 |
| 도메인 값(크롭·슬롯·역할·세션 ID 형식·컷 수·QR 정규화) | `docs/spec-vectors` 271케이스 | 값 재검증 — **E12는 도메인 함수를 호출해** 판정만 재사용한다 |
| 합성·필터 픽셀 | 골든 이미지 | 픽셀 비교 |
| 순서 불변식(M6-W·M7·M8·업로드 3단계) | `resultNext.test.ts`·`uploadRunner.test.ts` | **재현은 한다** — 단위는 목 위의 순서고, E2E는 **실브라우저에서 실제로 그 순서로 일어나는가**다 |

한 문장 규칙: **"소스를 읽으면 알 수 있는 것"은 vitest, "브라우저에서 돌려 봐야 아는 것"은 Playwright, "사람 눈·하드웨어가 필요한 것"은 V.**

### 5.2 dev 서버에서만 나타나는 관측 잡음 (오해 방지)

| 관측 | 원인 | 운영 빌드 |
|------|------|-----------|
| `uploads/prepare`가 **2건** | `<StrictMode>`가 개발 빌드에서 effect를 2회 실행한다. `useUploadRun`이 첫 실행을 cleanup에서 abort하므로 **PUT·commit은 1건씩**이다(설계된 동작) | 1건 |
| `logger.error`가 콘솔에 보임 | `createLogStore({ mirrorToConsole })`가 개발 빌드에서만 켜진다 | 미러링 없음 |
| `/favicon.ico` 404 | 저장소에 favicon 파일이 없고 `index.html`에 `<link rel="icon">`도 없다(브라우저 내부 요청이라 `page.route`로 가로챌 수 없다) | 배포본도 동일 — 기능 영향 없음 |
| `A VideoFrame was garbage collected…` | `camera.stop()`이 가공 Worker를 `terminate()`할 때 그 Worker가 소유하던 프레임이 `close()` 없이 사라진다. Worker와 함께 자원도 회수되므로 누수는 아니다 | 동일(권고 문구) |

---

## 6. 브라우저·기기 매트릭스

### 6.1 지원 목표

| 플랫폼 | 브라우저 | 최소 버전 | 지원 등급 | 타임랩스 |
|--------|----------|-----------|-----------|----------|
| Windows 10/11 | Chrome / Edge | 111+ | **A(주력)** | ○ |
| macOS | Chrome / Edge | 111+ | **A** | ○ |
| macOS | Safari | 17+ | B | ○(WebCodecs) |
| Android 12+ (태블릿) | Chrome | 111+ | **A(주력)** | ○ |
| iPadOS 17+ | Safari | 17+ | **A(주력)** | ○(WebCodecs) |
| iOS 17+ (폰) | Safari | 17+ | B(폼팩터 비권장) | ○ |
| Windows/macOS | Firefox | 133+ | C(검증만) | △ — `VideoEncoder`는 133+이지만 **H.264 인코딩 가용성이 플랫폼 의존**이고 `MediaRecorder`는 mp4를 지원하지 않는다 → **경로 C(미제공)로 떨어질 수 있다**([04 §7.3b](./04-media-pipeline-web.md)) |
| 그 외·구버전 | — | — | **미지원** | ✕ |

> **Safari 최소 버전을 17로 잡은 근거**: WebCodecs `VideoEncoder`(타임랩스)와 `OffscreenCanvas` 2D는 **16.4**에서 되지만, **`OffscreenCanvas.getContext("webgl2")`(Worker 뷰티 필터)가 17부터** 동작한다([04 §2.3.1](./04-media-pipeline-web.md)). 16.4~16.6에서도 CPU 폴백으로 동작할 수는 있으나 성능 예산을 보장하지 않으므로 지원 목표에서 제외한다.
> **모든 판정은 런타임 기능 감지로 한다** — 위 표의 버전은 기기 선정·기대치 설정용이며 UA·버전 문자열로 분기하지 않는다(§6.2).

| 등급 | 의미 |
|------|------|
| **A** | 전 기능 + 실기기 회귀 검증 대상 |
| B | 전 기능 동작해야 하나 성능·메모리 여유가 적다 |
| C | 치명 결함만 대응. 타임랩스·폴더 저장 미지원 가능 |

### 6.2 기능별 지원 판정 (앱이 런타임에 판정해 진단에 표시)

| 기능 | 판정 방법 | 미지원 시 |
|------|-----------|-----------|
| 카메라 | `navigator.mediaDevices?.getUserMedia` | 앱 사용 불가 → 진입 시 안내 |
| OPFS | `navigator.storage?.getDirectory` **+ Worker에서 `createSyncAccessHandle`(또는 `createWritable`) 실제 성공 여부**([05 §3.1](./05-storage-and-persistence.md)) | **결과물 보관·세션 작업이 불가** → 촬영 시작 전 경고(업로드만 가능) |
| 타임랩스 | `await VideoEncoder.isConfigSupported(실사용 config)` → `MediaRecorder.isTypeSupported("video/mp4;codecs=avc1")` | 타임랩스 미제공(정상 축소) |
| Worker 가공 | `typeof OffscreenCanvas !== "undefined"` + Worker에서 `getContext("2d")` 성공 | 메인 스레드 가공(저성능 모드 표시) |
| Worker WebGL2(뷰티) | Worker에서 `getContext("webgl2")` 성공 | 메인 WebGL2 → `ImageData` CPU 폴백 |
| 비트맵 축소 | `createImageBitmap(…,{resizeWidth})` 결과의 `width`가 요청값과 일치하는지 | 캔버스 `drawImage` 단계 축소 |
| 폴더 저장 | `window.showDirectoryPicker` | 버튼 미노출 + 안내 |
| Wake Lock | `navigator.wakeLock` | OS 설정 안내 |
| 저장소 영속 | `navigator.storage?.persist` | 진단에 "미지원" 표시 + PWA 설치 권장 |

### 6.2b Playwright WebKit 프로젝트의 실행 범위 (Step 17 실측)

`playwright.config.ts`에는 `chromium`·`webkit` 두 프로젝트가 있고 **둘 다 실제로 돈다**. 다만
WebKit에서 **제외하는 태그가 2개**이며, 제외는 조용히 지우지 않고 여기에 사유를 남긴다.

| 제외 태그 | 사유(실측) | 그래서 누가 검증하나 |
|-----------|-----------|----------------------|
| `@camera` | `--use-fake-device-for-media-stream`은 **Chromium 전용 스위치**다. Playwright WebKit에는 동등한 가짜 카메라 주입 수단이 없다(`permissions: ["camera"]`도 Chromium 전용) | 촬영 흐름은 chromium + 실기기 V1·V2·V14~V17 |
| `@opfs-write` | Playwright **WebKit 18.2(Windows)** 빌드에는 `navigator.storage.getDirectory`와 `OffscreenCanvas`가 **아예 없다**(런타임 probe로 확인). `getOpfsClient()`가 `UNSUPPORTED_OPFS_CLIENT`로 떨어져 모든 쓰기가 `false`다 | 저장 경로는 chromium + **V7·V23-5·V24-2·V25-4** |

> ⚠️ **Playwright WebKit ≠ Safari.** 엔진 계열은 같지만 빌드·플랫폼이 다르다. 위 OPFS 부재는
> **이 빌드의 한계이지 Safari의 동작이 아니다** — 실제 Safari 17+에는 OPFS가 있다.
> 따라서 **WebKit 프로젝트가 전부 통과해도 Safari가 검증된 것이 아니다.** Safari 고유 항목
> (`createWritable` 부재 · `playsinline` · 저장소 회수)은 iPad 실측(S7)이 계속 소유한다.

**WebKit에서 실제로 도는 것(15건)**: `auth-session`(E4) · `roles-and-pin`(E15·E16·E17·E18 + 목록 실패 변형) ·
`strings-catalog`(E22 5건) · `idle-and-recovery`(E5 2건·E14) · `frame-authoring`(E13 서버 등록 축).

### 6.3 실기기 수동 체크리스트 (기기당)

- [ ] 카메라 권한 허용 후 프리뷰가 24fps 이상으로 부드럽다
- [ ] 거울모드 on/off가 **저장 결과에도** 반영된다(WM1)
- [ ] 10컷 세션에서 탭이 죽지 않는다(메모리)
- [ ] 합성이 1.2초 이내
- [ ] 타임랩스가 생성되고 재생되며 길이가 10~15초
- [ ] 업로드 후 폰으로 QR 스캔 → 다운로드 성공
- [ ] 전체화면·화면 꺼짐 없음
- [ ] 세로·가로 회전에서 레이아웃이 깨지지 않는다
- [ ] 진단 모달의 표시값이 실제와 일치

---

## 7. 성능 검증

[04 §8](./04-media-pipeline-web.md)의 예산을 실기기에서 측정해 기록한다.

> ⚠️ **E2E는 성능을 측정하지 않는다.** headless + SwiftShader에서 나온 수치는 실기기 예산과
> 무관하고, 그것을 표에 적으면 "측정했다"는 잘못된 안심을 만든다. **아래 표는 Step 17 시점에
> 비어 있다** — 채우는 것은 실측 세션 **S9**([16 §3](./16-field-verification-runbook.md))이다.

| 측정 | 방법 |
|------|------|
| 프리뷰 fps·지연 | 앱 내부 계측값을 진단 모달에 표시(개발 빌드는 오버레이) |
| 합성 시간 | `performance.now()` 구간 측정 → 로그 |
| 타임랩스 인코딩 | 동상 |
| 메모리 | Chrome DevTools Memory / iOS는 Safari Web Inspector |
| 결과 기록 | 아래 표에 누적 |

| 기기 | 브라우저·버전 | 프리뷰 fps | 합성(ms) | 타임랩스(s) | 메모리 peak | 측정일 |
|------|---------------|-----------:|---------:|------------:|------------:|--------|
| _(Windows PC)_ | | | | | | |
| _(Android 태블릿)_ | | | | | | |
| _(iPad)_ | | | | | | |

---

## 8. 수락 체크리스트 (출시 전 — `analysis/05 §11` 웹 확장)

**확인 수단 표기는 세 가지뿐이다.** 빈칸을 남기지 않는다 — 수단을 못 적는 항목은 검증 계획이 없다는 뜻이므로
그 자리에서 V 항목을 신설하거나 항목을 삭제한다.

| 표기 | 뜻 | 언제 닫히나 |
|------|-----|-------------|
| **자동** `{spec} {E번호}` | Playwright가 확인한다 | `npm run e2e` 통과 시 |
| **정적** `{불변식}` | vitest가 소스를 읽어 고정한다 | `npm test` 통과 시 |
| **사람** `{V번호}` | 실기기·실계정·눈이 필요하다 | [16 실기기 절차서](./16-field-verification-runbook.md) 수행 시 |

> ⚠️ **`자동`·`정적`이 붙은 항목도 체크는 사람이 한다.** 표기는 "무엇이 그것을 증명하는가"이고,
> 체크는 "그 증명을 실제로 돌려서 봤는가"다.

### 공통 (7)

| ✔ | 항목 | 확인 수단 |
|:-:|------|-----------|
| [ ] | 배포 게이트 키를 저장소에 커밋하지 않았다(빌드 주입) | **정적** 저장소 검토 + `.gitignore`의 `.env*` · **사람** V25-8(진단·로그에도 값 없음) |
| [ ] | JWT를 어떤 저장소에도 쓰지 않는다(M2) | **자동** `auth-session` E4 · **정적** M2/M2-a/M2-b · **사람** V21-4 |
| [ ] | 로그인 업로드에는 Bearer가 붙고, 로그아웃 후에는 업로드 요청 자체가 없다(M1) | **자동** `auth-session` E3-1·E3-2·E3b · **정적** AUTH-1 |
| [ ] | 모든 API 실패가 사용자에게 보이거나 로그에 남는다(조용한 실패 0 — M4) | **자동** `upload-qr` E7 · `roles-and-pin` E18 변형 · **사람** V20-6·V22-6·V23-8·V24-8 |
| [ ] | 401/403/404/409/501·네트워크 실패가 각각 다른 안내로 구분된다 | **정적** 오류 매핑 단위 테스트 · **사람** V21-7~V21-10 · V22-6 |
| [ ] | 시크릿·토큰·인가 코드·PKCE verifier·PIN이 로그에 없다 | **정적** AUTH-3·PIN-1·DIAG-1 · **사람** V25-8 |
| [ ] | 미처리 예외가 앱을 죽이지 않고 홈 복귀 + 로그로 처리된다(M16) | **자동** `idle-and-recovery` E14 |

### P2 촬영 (12)

| ✔ | 항목 | 확인 수단 |
|:-:|------|-----------|
| [ ] | 프리뷰·스틸·타임랩스가 동일 가공(거울→중앙 크롭)을 거친다(WM1) | **정적** WM1 grep + 골든 이미지 · **사람** V2 |
| [ ] | 카메라 Ready 게이트를 통과한 뒤에만 시퀀스가 시작된다 | **자동** `guest-flow` E1(Ready 전 [바로 촬영] 비활성) · **사람** V1·V4 |
| [ ] | **자동 컷 수(it17)**: `CutCount=0`이 저장 왕복에 보존되고 슬롯 5 프레임에서 7컷이 촬영된다 | **정적** `cutCountPolicy` 벡터 · **사람** V22-8 |
| [ ] | 세션 ID 형식이 정규식을 만족한다(M13) | **자동** `upload-qr` E12(도메인 함수로 판정) |
| [ ] | 서명 PUT에 `requiredHeaders` 전부를 부착한다(M14) | **자동** `upload-qr` E2 · **사람** V20-2 |
| [ ] | **로컬 보관이 업로드 시도 이전에 끝난다**(M6-W) | **자동** `offline-storage` E8 · **사람** V19-1·V19-2 |
| [ ] | 업로드 성공 후에만 QR을 노출한다(M5) | **자동** `upload-qr` E1b·E7 |
| [ ] | **게스트는 `Qr`에 도달하지 않고 `Done`으로 끝나며** `EnableQrDelivery`가 변하지 않는다(E23) | **자동** `guest-flow` E1·E23 |
| [ ] | TempUser 한도 초과 시 QR에 진입하지 않고, 서버 거부는 사유별 문구로 안내한다 | **자동** `upload-qr` E24 · **사람** V22-7 계열 |
| [ ] | 유휴 타임아웃이 로그아웃하지 않는다(M3) | **자동** `idle-and-recovery` E5 · **사람** V11 |
| [ ] | 탭 hidden에서 촬영이 안전하게 취소된다(WM4) | **자동** `guest-flow` E19(에뮬레이션) · **사람** V16(진짜 탭 전환) |
| [ ] | 타임랩스가 mp4/H.264/무음/10~15초이거나 미지원 시 `null`로 정상 축소된다 | **사람** V18-1·V18-2·V18-6 |

### P3 저작 (6)

| ✔ | 항목 | 확인 수단 |
|:-:|------|-----------|
| [ ] | 프레임 이름의 `_`를 **서버 등록에서 하드 거부**하고, 로컬 저장에서는 경고만 낸다(M15) | **자동** `frame-authoring` E13 · **사람** V24-4 |
| [ ] | 슬롯 저장 검증(1~6개·경계 내·겹침 없음) | **정적** `slotValidation` 벡터 |
| [ ] | 편집 진입·버튼 노출·저장 3곳에 권한 가드가 있다(M10) | **자동** `roles-and-pin` E15(렌더 축) · **정적** FR-*(액션 축) |
| [ ] | 카탈로그 유래 프레임 편집이 **사본으로 분기**되고 원본을 건드리지 않는다 | **정적** `frameEditPolicy` 단위 · **사람** V23-8 |
| [ ] | `PUT /frames/{id}`를 호출하지 않는다 | **정적** FR-9(함수 자체가 없다) |
| [ ] | **편집기에서 본 슬롯 위치 = 합성 결과 위치**(0px) | **사람** V24-3 — 골든 이미지는 **합성만** 본다(편집기 화면과의 대조는 사람 몫) |

### P4 운영 (5)

| ✔ | 항목 | 확인 수단 |
|:-:|------|-----------|
| [ ] | 역할 변경 옵션이 서버 `canSetRole`과 1:1이다 | **자동** `roles-and-pin` E18(콤보 옵션) · **정적** `role-matrix.json` 벡터 |
| [ ] | 자기 계정 삭제·자기 대상 PIN 재설정을 UI에서 막고 서버 거부도 우아 처리한다 | **자동** `roles-and-pin` E18(자기 행 액션 0) · **사람** V25-5(서버 거부) |
| [ ] | PIN 재설정 대상이 **엄격히 낮은 위계**만 노출된다(삭제와 게이트가 다르다) | **자동** `roles-and-pin` E18 · **사람** V25-5 |
| [ ] | PIN 게이트가 fail-closed이며 네트워크 오류를 실패 횟수로 세지 않는다 | **자동** `roles-and-pin` E16·E17 · **정적** PIN-2 · **사람** V22-6 |
| [ ] | 사용자 목록 조회 실패가 **빈 목록이 아니라 오류**로 표시된다 | **자동** `roles-and-pin` E18 변형 |

### 웹 전용 (11)

| ✔ | 항목 | 확인 수단 |
|:-:|------|-----------|
| [ ] | 서버 프레임 이미지가 CORS-clean하게 로드되고 canvas 오염이 없다(WM2) | **사람** V23-3 |
| [ ] | 모든 타이머가 실경과 기반이다(WM3) | **정적** WM3 grep · **사람** V18-5 |
| [ ] | 업로드 진행률이 XHR로 측정된다(WM5) | **자동** `upload-qr` E2(PUT이 XHR로 나간다) · **사람** V20-3(0→100 증가 관측) |
| [ ] | 앱 시작 시 세션 잔재가 정리되고 `results/`·`frames/`·로그는 보존된다 | **자동** `guest-flow` E21 · **사람** V8 |
| [ ] | `results/` 용량 정책이 동작하고 삭제가 로그에 남는다 | **사람** V22-10 |
| [ ] | 저장에 실패하면 실패 토스트가 뜨고 전이는 계속된다(M4) | **정적** `resultSaver` 단위 · **사람** V19-6(E6은 자동화 불가 — §5) |
| [ ] | 진단 모달이 카메라·인코더·서버·저장소 상태를 정직하게 표시한다 | **자동** `strings-catalog`(문구 축) · **사람** V25-8 |
| [ ] | `downloadPageUrl`이 **P1 사이트 도메인**을 가리킨다 | **자동** `upload-qr` E2 · **사람** V20-4/V21-5 |
| [ ] | CSP 위반이 콘솔에 없다 | **사람** V13·V21-2·V25-6 — ⚠️ **로컬 dev에는 CSP가 없다.** E2E의 콘솔 오류 수집은 CSP 검증이 아니라 **회귀 그물**이다 |
| [ ] | 오프라인에서 게스트 촬영·로컬 저장이 동작한다 | **자동** `offline-storage` E20(백엔드 미도달) · **사람** V23-2 · **V25-1**(앱 셸 오프라인 = SW) |
| [ ] | [12 차이 보고서](./12-web-vs-windows-differences.md)에 **등재되지 않은 동작 차이가 없다** | **사람** 문서 검토(Step 17에서 1회 수행 — 12 상단 갱신일) |
