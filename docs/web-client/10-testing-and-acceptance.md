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
| `stateMachine.test.ts` | `AppStateTests.cs` | 전이표 전수 · 오버레이 항상 허용 · `from == to` 거부 · 불법 전이 거부 |
| `idleCountdown.test.ts` | `IdleCountdownTests.cs` | 120초 판정 · 10초 카운트다운 · 활동 무시 |
| `centerCrop.test.ts` | `CropCalculatorTests.cs` | 넓은/좁은 원본 · 정사각 · `targetAspect<=0` · **정수 나눗셈 경계** |
| `previewReadiness.test.ts` | `PreviewReadinessTests.cs` | 3조건 각각 미달 · 전이 1회만 · 타임아웃 |
| `captureSession.test.ts` | `CaptureSessionTests.cs` | `max(설정컷, 슬롯수)` · 토글·번호 재계산 · 슬롯 초과 거부 · 전체 재촬영 카운터 |
| `slotLayout.test.ts` | `SlotLayoutTests.cs` | `autoArrange` 1~6개 · 세로 스트립(aspect<0.6) · `scaleSlots` 원본 기준 · 클램프 · 겹침 판정 |
| `editorTransform.test.ts` | `EditorTransformTests.cs` | 레터박스 원점 · 왕복 변환 · `scale<=0` 방어 |
| `frameOrigin.test.ts` | `FrameOriginTests.cs` | `local:`·`bundle:`·`fallback`·빈 id·서버 id 분류 |
| `frameEditPolicy.test.ts` | `FrameEditPolicyTests.cs` | 1차 게이트(쓰기 권한) · 출처별 편집/삭제 · **소유자 미검사 규칙** |
| `frameNaming.test.ts` | `FrameNamingTests.cs` | 사본 이름 1~99 · base 되돌림 · 난수 폴백 · 빈 이름 |
| `slotsFile.test.ts` | `LocalFrameStoreTests.cs` | 메타 대소문자 무시 · 손상 줄 무시 · `#dbid` 유무 · `#imagesize` 부재 |
| `appSettings.test.ts` | `SettingsTests.cs` | 전 키 기본값 · 최근접 보정 · `RetentionHours` clamp · **두 URL 정규화 방향 반대** |
| `qrDeliveryPolicy.test.ts` | `QrDeliveryPolicyTests.cs`, `QrEffectivePolicyTests.cs` | 정규화 · 재활성 · 하위 값 보존 |
| `rolePolicy.test.ts` | `RoleManagementTests.cs` | `isPower` · `canWriteFrames` · `canManage`(**동급 허용**) · `canResetPin`(**동급 차단**) · 알 수 없는 역할 → `user` |
| `roleChangePolicy.test.ts` | `RoleManagementTests.cs` | `assignableRoles` 전수 매트릭스 · admin 지정 불가 · 순서 오름차순 |
| `uploadContract.test.ts` | `UploadContractTests.cs` | 세션 ID 정규식 · 경로 조립 · 토큰 URL 인코딩(`%2F`) · 다운로드 페이지 URL · 만료 계산 |
| `uploadOrchestration.test.ts` | `UploadServiceTests.cs` | 최소 1개 불변식 · 3단계 순서 · 진행률 합산(**순서 비의존**) |
| `timelapseSpeed.test.ts` | `FfmpegArgsTests.cs` | `computeSpeedFactor` 표(12/15/30/60/120초) |
| `frameCatalogPolicy.test.ts` | `FrameCatalogServiceTests.cs` | 우선순위 4단 · **이름 dedup** · 서버 미도달 폴백 |

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
| `center-crop.json` | `{srcW, srcH, targetAspect} → {x,y,w,h}` 30+ 케이스 | `CropCalculatorTests.cs` |
| `auto-arrange.json` | `{slotCount, frameW, frameH, targetAspect} → Slot[]` | `SlotLayoutTests.cs` |
| `scale-slots.json` | `{baseSlots, factor, frameW, frameH} → Slot[]` | 동상 |
| `clamp-slot.json` | 편집기용·합성용 **두 식 각각** | 동상 + `CompositionTests.cs` |
| `overlap.json` | 겹침·경계 접촉 케이스 | `SlotLayoutTests.cs` |
| `editor-transform.json` | `{canvasW,canvasH,frameW,frameH} → {scale,originX,originY}` + 왕복 | `EditorTransformTests.cs` |
| `role-matrix.json` | `{actor, current} → assignableRoles[]` 전수 + `canManage`·`canResetPin` | `RoleManagementTests.cs` |
| `copy-name.json` | `{원본이름, 기존이름목록} → 사본이름` | `FrameNamingTests.cs` |
| `session-id.json` | `{now, uuid} → sessionId` + 경로·URL 조립 | `UploadContractTests.cs` |
| `timelapse-speed.json` | `sessionSeconds → factor` | `FfmpegArgsTests.cs` |
| `settings-clamp.json` | `{입력 설정} → {보정된 설정}` | `SettingsTests.cs` |
| `qr-normalize.json` | QR 토글 정규화·재활성 | `QrDeliveryPolicyTests.cs` |
| `slots-file.json` | `.slots` 텍스트 → 파싱 결과(손상 줄 포함) | `LocalFrameStoreTests.cs` |

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

## 5. E2E 시나리오 (Playwright — 반드시 자동화할 것)

카메라는 `--use-fake-device-for-media-stream --use-file-for-fake-video-capture=<y4m>`로 대체한다(Chromium). WebKit은 수동 검증으로 보완.

| # | 시나리오 | 검증 포인트 | 불변식 |
|---|----------|-------------|:------:|
| E1 | 게스트 촬영 완주 | 홈 → 프레임 → 가이드 → 촬영(6컷) → 컷선택 → 결과 → QR → 완료 → 홈 | — |
| E2 | 업로드 3단계 | prepare/PUT/commit 요청 순서·헤더·본문. **`requiredHeaders` 전부 부착** | M14 |
| E3 | **로그아웃 후 게스트 업로드** | 로그인 → 로그아웃 → 촬영 → prepare 요청에 **`Authorization` 헤더 없음** | **M1** |
| E4 | JWT 미저장 | 로그인 후 `localStorage`·`sessionStorage`·`indexedDB`·쿠키에 토큰 문자열 부재 | **M2** |
| E5 | 유휴 타임아웃 | 촬영 중 무동작 → 경고 → 카운트다운 → 홈. **로그인 유지** | M3 |
| E6 | 저장 실패 표시 | 저장소 쓰기 실패를 목으로 유발 → **실패 토스트 표시** | M4 |
| E7 | 업로드 실패 시 QR 미노출 | prepare를 500으로 목 → QR 없음 + 사유 문구 + [완료] 가능 | M5 |
| E8 | **로컬 보관이 업로드보다 먼저** | 네트워크를 끊고 촬영 완주 → OPFS에 결과 폴더 존재 | **M6-W** |
| E9 | 최소 1개 불변식 | 사진·타임랩스 둘 다 off → 업로드 시도 안 함 | M7 |
| E10 | 프레임 고정 | 촬영 시작 후 프레임 변경 UI 없음 | M11 |
| E11 | 컷 선택 개수 | 슬롯 수 미달·초과에서 [다음] 비활성 | M12 |
| E12 | 세션 ID 형식 | prepare 본문의 `sessionId`가 정규식 통과 | M13 |
| E13 | 프레임 이름 `_` | `_` 포함 이름 저장 거부 | M15 |
| E14 | 전역 예외 복구 | 강제 예외 주입 → 홈 복귀 + 로그인 유지 | M16 |
| E15 | 권한 게이트 | `user` 역할로 프레임 만들기 버튼 부재 + 액션 직접 호출 시 거부 | M10 |
| E16 | PIN 게이트 | 5회 실패 → 모달 닫힘 + **5분 잠금 유지(재시작 후에도)** | M9·WD16 |
| E17 | PIN 401 오해 방지 | PIN 1회 오입력이 **로그아웃을 유발하지 않음** | — |
| E18 | 역할 매트릭스 | manager 로그인 시 다른 manager 행에 [PIN] 없음·[삭제] 있음 | — |
| E19 | 탭 hidden 취소 | 촬영 중 `visibilitychange(hidden)` → 홈 복귀, 부분 컷 없음 | **WM4** |
| E20 | 오프라인 촬영 | 네트워크 차단 → 프레임 목록 폴백 + 촬영·로컬 저장 성공 + QR 실패 우아 처리 | — |
| E21 | 새로고침 | 촬영 중 새로고침 → 홈에서 시작 + 세션 잔재 정리됨 | — |
| E22 | 문구 카탈로그 | 주요 문구가 `analysis/13 §14`와 문자열 일치 | — |

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
| Windows/macOS | Firefox | 130+ | C(검증만) | △(WebCodecs 130+) |
| 그 외·구버전 | — | — | **미지원** | ✕ |

| 등급 | 의미 |
|------|------|
| **A** | 전 기능 + 실기기 회귀 검증 대상 |
| B | 전 기능 동작해야 하나 성능·메모리 여유가 적다 |
| C | 치명 결함만 대응. 타임랩스·폴더 저장 미지원 가능 |

### 6.2 기능별 지원 판정 (앱이 런타임에 판정해 진단에 표시)

| 기능 | 판정 방법 | 미지원 시 |
|------|-----------|-----------|
| 카메라 | `navigator.mediaDevices?.getUserMedia` | 앱 사용 불가 → 진입 시 안내 |
| OPFS | `navigator.storage?.getDirectory` | **결과물 보관·세션 작업이 불가** → 촬영 시작 전 경고(업로드만 가능) |
| 타임랩스 | `VideoEncoder.isConfigSupported` → `MediaRecorder.isTypeSupported` | 타임랩스 미제공(정상 축소) |
| 폴더 저장 | `window.showDirectoryPicker` | 버튼 미노출 + 안내 |
| Wake Lock | `navigator.wakeLock` | OS 설정 안내 |
| 저장소 영속 | `navigator.storage?.persist` | 진단에 "미지원" 표시 + PWA 설치 권장 |

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

| 측정 | 방법 |
|------|------|
| 프리뷰 fps·지연 | 앱 내부 계측값을 진단 모달에 표시(개발 빌드는 오버레이) |
| 합성 시간 | `performance.now()` 구간 측정 → 로그 |
| 타임랩스 인코딩 | 동상 |
| 메모리 | Chrome DevTools Memory / iOS는 Safari Web Inspector |
| 결과 기록 | `webclient/docs/perf-{기기}.md` 또는 이 문서에 표로 누적 |

---

## 8. 수락 체크리스트 (출시 전 — `analysis/05 §11` 웹 확장)

### 공통
- [ ] 배포 게이트 키를 저장소에 커밋하지 않았다(빌드 주입)
- [ ] JWT를 어떤 저장소에도 쓰지 않는다(M2 — E4)
- [ ] 로그아웃 시 JWT가 폐기되고 직후 익명 업로드에 Bearer가 붙지 않는다(M1 — E3)
- [ ] 모든 API 실패가 사용자에게 보이거나 로그에 남는다(조용한 실패 0 — M4)
- [ ] 401/403/404/409/501·네트워크 실패가 각각 다른 안내로 구분된다
- [ ] 시크릿·토큰·인가 코드·PKCE verifier·PIN이 로그에 없다
- [ ] 미처리 예외가 앱을 죽이지 않고 홈 복귀 + 로그로 처리된다(M16)

### P2 촬영
- [ ] 프리뷰·스틸·타임랩스가 동일 가공(거울→중앙 크롭)을 거친다(WM1)
- [ ] 카메라 Ready 게이트를 통과한 뒤에만 시퀀스가 시작된다
- [ ] 세션 ID 형식이 정규식을 만족한다(M13)
- [ ] 서명 PUT에 `requiredHeaders` 전부를 부착한다(M14)
- [ ] **로컬 보관이 업로드 시도 이전에 끝난다**(M6-W — E8)
- [ ] 업로드 성공 후에만 QR을 노출한다(M5)
- [ ] TempUser 한도 초과를 사유별 문구로 안내한다
- [ ] 유휴 타임아웃이 로그아웃하지 않는다(M3)
- [ ] 탭 hidden에서 촬영이 안전하게 취소된다(WM4)
- [ ] 타임랩스가 mp4/H.264/무음/10~15초이거나, 미지원 시 `null`로 정상 축소된다

### P3 저작
- [ ] 프레임 이름에 `_`를 허용하지 않는다(M15)
- [ ] 슬롯 저장 검증(1~6개·경계 내·겹침 없음)
- [ ] 편집 진입·버튼 노출·저장 3곳에 권한 가드가 있다(M10)
- [ ] 카탈로그 유래 프레임 편집이 **사본으로 분기**되고 원본을 건드리지 않는다
- [ ] `PUT /frames/{id}`를 호출하지 않는다
- [ ] **편집기에서 본 슬롯 위치 = 합성 결과 위치**(골든 이미지 0px)

### P4 운영
- [ ] 역할 변경 옵션이 서버 `canSetRole`과 1:1이다
- [ ] 자기 계정 삭제·자기 대상 PIN 재설정을 UI에서 막고 서버 거부도 우아 처리한다
- [ ] PIN 재설정 대상이 **엄격히 낮은 위계**만 노출된다(삭제와 게이트가 다르다)
- [ ] PIN 게이트가 fail-closed이며 네트워크 오류를 실패 횟수로 세지 않는다
- [ ] 사용자 목록 조회 실패가 **빈 목록이 아니라 오류**로 표시된다

### 웹 전용
- [ ] 서버 프레임 이미지가 CORS-clean하게 로드되고 canvas 오염이 없다(WM2)
- [ ] 모든 타이머가 실경과 기반이다(WM3)
- [ ] 업로드 진행률이 XHR로 측정된다(WM5)
- [ ] 앱 시작 시 세션 잔재가 정리되고 `results/`·`frames/`·로그는 보존된다
- [ ] `results/` 용량 정책이 동작하고 삭제가 로그에 남는다
- [ ] 진단 모달이 카메라·인코더·서버·저장소 상태를 정직하게 표시한다
- [ ] `downloadPageUrl`이 **P1 사이트 도메인**을 가리킨다
- [ ] CSP 위반이 콘솔에 없다
- [ ] 오프라인에서 게스트 촬영·로컬 저장이 동작한다
- [ ] [12 차이 보고서](./12-web-vs-windows-differences.md)에 **등재되지 않은 동작 차이가 없다**
