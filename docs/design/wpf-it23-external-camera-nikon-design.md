# it23 설계 — Nikon D5300 외부 카메라(DSLR) 연동

> 작성: wpf-architect · 2026-08-10
> 파이프라인: wpf-architect → wpf-developer → wpf-code-reviewer
> 상태: 설계 초안 rev2 (SDK 실물·실물 카메라 **없이** 작성 — §2의 미검증 항목 표가 이 문서의 전제)
> rev2 (2026-08-10): Nikon SDK 라이선스 **제3자 사본** 실측 반영 — §13 전면 개정(배포 경계·출하 차단 판정), §2 A12·A14, §3.3 법적 절차 병기, §15 C3, Step S-B

## §0 개요

### 0.1 요구사항 원문 (사용자 확정, 축약 금지)

1. **역할 분리(하이브리드)**: 웹캠 = 프리뷰 + 타임랩스(현행 유지). 외부 카메라 = 스틸 촬영 + 카메라 세팅 확인. 두 카메라가 **각각 독립 동작**한다.
2. 외부 카메라 설정을 켜면 UI에 **"타임랩스 기능은 웹캠으로만 동작됩니다"** 표기. 웹캠은 **있는 경우에만** 동작(없으면 타임랩스 불가를 우아하게 처리).
3. **플래시는 현재 화면 플래시만 동작.** 단 SDK로 물리 플래시 제어가 가능하다고 **판별되면 둘 다 켜지는** 구조여야 한다 → capability 프로브 훅을 포함하되, 지금은 화면 플래시 단독 경로가 유일한 활성 경로다.
4. **카메라 테스트 모달의 장치 목록에 외부 카메라도 나와야 한다**(외부 카메라 설정이 켜진 경우에만). 웹캠 항목 = 타임랩스용 확인, 외부 카메라 항목 = **카메라 세팅 확인 + 셔터 동작 테스트**.
5. **셔터 세부 설정**(셔터 속도·조리개·ISO 등)을 외부 카메라 설정이 켜지면 조정 가능하게 — **슬라이더 + 직접 입력(edit)** 병행 UI.
6. 설정에 **연동 가능 모델 목록** 추가. md3는 모델별 파일이므로 **"모델 → md3 모듈 파일명" 레지스트리**로 설계해 모델 추가가 표 한 줄로 끝나게 한다. 현재 실제 활성 항목은 D5300 하나.
7. 권한: **User 역할 이상**에서 사용 가능. `UserRole.cs` 판정 규약(서수 부등식 금지, 명시 열거) 준수.
8. 사용자 지시: **막히면 멈추지 말고 진행.** 미검증 구간은 "런타임 비활성 + 사유 노출"로 처리한다.

### 0.2 이 설계의 최우선 제약 — 장비·SDK 부재 선행 개발

실물 D5300도, Nikon SDK 실물 파일도 현재 없다. 나중에 SDK와 장비가 도착했을 때 **고칠 파일이 정확히 1개(SDK shim)로 국소화**되도록 경계를 세우는 것이 이 설계의 핵이다. Core·App·설정·UI는 SDK 시그니처를 **모른 채** 완성된다.

이를 위해 문서 전체에서 다음 표기 규약을 쓴다:

| 표기 | 의미 |
|---|---|
| (실측) | 이 리포 코드에서 직접 확인 — `파일:줄` 근거 병기 |
| (공개 근거) | 공개 웹 자료(오픈소스 래퍼 위키 등)에서 확인 — 링크 병기. 단 **SDK 실물로 재확인 전에는 계약에 박지 않는다** |
| ⚠️ 미검증 | SDK 실물·실물 카메라 확인 필요. §2 표에 검증 방법 매핑 |

**추측을 사실로 승격시키지 않는다.** MAID API의 함수·상수 이름은 어떤 것도 확정 사실로 쓰지 않았다 — SDK 시그니처가 새어 들어갈 수 있는 파일은 오직 `NikonSdkShim.cs`(§3.4) 하나다.

### 0.3 판정 요약

| 쟁점 | 판정 | 왜 |
|---|---|---|
| SDK 경계 | Core 계약(POCO) + 어댑터 오케스트레이션 + **SDK shim 1파일** 3층 분리 (§3) | shim만이 SDK 타입을 만진다. SDK 도착 시 수정 지점이 1파일로 수렴 |
| `IExternalCamera` 확장 | 기존 4멤버 **유지 + 멤버 추가**(시그니처 파괴 없음) (§3.2) | 프로덕션 소비자가 DI 등록뿐(실측)이라 확장 비용이 최소. Null 구현·테스트만 추가 멤버 구현 |
| WYSIWYG | DSLR 스틸을 **수신 즉시 웹캠과 동일 규칙**(거울반전→대표 슬롯 종횡비 중앙크롭)으로 정규화해 `CapturedStill`로 변환 → 하류(컷선택·필터·합성) 무변경 (§5) | 필터·합성이 `CapturedStill`만 알기 때문에, 입구에서 규칙을 통일하면 WYSIWYG 불변식이 자동 계승된다 |
| 프리뷰≠결과물 잔여 불일치 | 기하(종횡비·거울)는 **제거**, 광학(화각·색감)은 **제거 불가 → 고지** (§5.4) | 웹캠 프리뷰와 DSLR 결과물은 다른 렌즈·센서다. 숨기면 WYSIWYG 사기가 된다 |
| 촬영 시퀀스 | 컷당 **순차 대기**(카운트다운→플래시→DSLR 셔터→수신 대기 상태 표시→다음 컷). 파이프라이닝 비목표 (§6) | 수신 지연(수 초 추정)을 겹치기 시작하면 재촬영·세션 영상·컷 순서 3곳이 동시에 복잡해진다 |
| 수신 실패 시 강등 | 1회 재시도 → 실패 시 **해당 컷 웹캠 대체**(웹캠 있으면) + 세션 배너, 웹캠도 없으면 세션 중단 (§6.4, §11) | 키오스크 UX 우선 — 게스트 세션을 죽이지 않는다(리포 일관 원칙) |
| 설정 저장 | 노출값은 **인덱스가 아닌 표시 문자열**로 ini 저장(예: `1/125`) (§7, §10) | 인덱스는 카메라 모드·SDK 버전에 따라 표류한다. 문자열 재매칭이 실기 없이도 정의 가능 |
| 권한 게이트 | 편집 게이트 = **명시 열거** `CanConfigureExternalCamera`(User·AdvancedUser·Manager·Admin). 런타임 동작은 ini값 기준(게스트 세션에도 적용) (§8) | 기존 설정 게이트 관례(편집만 제한, 기능은 ini로 동작)와 동일. TempUser는 로그인해도 편집 불가 |
| 미검증 기능(LiveView·동영상·물리 플래시) | capability 3상(Unknown/Unsupported/Supported) + **기본 Unsupported·화면에 사유 노출** (§4) | 요구 8: 막히면 비활성+사유. 프로브가 Supported를 돌려줄 때만 경로가 열린다 |
| SDK 배포 (rev2) | **리포 커밋 금지 + 인스톨러 미동봉이 기본 아키텍처.** 런타임 탐색(`{exe}\NikonSdk\`) → 부재 시 강등이 곧 "배포 시 옵션 차단" 메커니즘 — 별도 킬스위치 없음 (§13.2) | 라이선스 제3자 사본에 재배포 금지·단일 컴퓨터 조항 — ffmpeg(GPLv3=동봉+고지)와 정반대 방향 (§13.3) |

---

## §1 검증된 사실 (verified facts)

### 1.1 리포 실측 (전부 코드 직접 확인)

| # | 사실 | 근거 |
|---|---|---|
| F1 | `IExternalCamera`는 4멤버 스캐폴드(`IsAvailable`/`ConnectAsync`/`CaptureAsync`/`DisconnectAsync`). 관례: 미지원·실패 시 예외 대신 false/null | `src/MCPhoto.Core/Devices/IExternalCamera.cs:10-27` |
| F2 | 프로덕션에서 `IExternalCamera`를 참조하는 곳은 **DI 등록 1곳뿐**(소비자 0) — 인터페이스 확장의 파급이 Null 구현·테스트로 한정된다 | grep 결과: `ServiceRegistration.cs:67`, 인터페이스, `NullExternalCamera.cs` 3파일 |
| F3 | 웹캠 파이프라인: 전용 캡처 스레드에서 프레임당 1회 **거울반전(`Cv2.Flip` FlipMode.Y) → 대표 슬롯 종횡비 중앙크롭(`CropCalculator.CenterCrop`)** 후 프리뷰·녹화·스틸 3소비자로 같은 버퍼 분기. 스틸=프리뷰 프레임 그 자체(WYSIWYG의 실체) | `src/MCPhoto.Capture/OpenCvCameraService.cs:134-190` |
| F4 | `CapturedStill`은 BGR24 연속 픽셀(`Width/Height/Pixels`) POCO. 필터는 촬영 시가 아니라 **합성 시** 전체 컷 일괄 적용(`ICompositionService.ComposeAsync(frame, cuts, filter, …)`) | `src/MCPhoto.Core/Capture/ICameraService.cs:48-53`, `src/MCPhoto.Core/Capture/ICompositionService.cs:18-31` |
| F5 | 촬영 시퀀스: Ready 게이트 → 컷 루프[카운트다운 → (FlashMode면 하양 오버레이 120ms) → 셔터음 → `CaptureStillAsync` → `AddCut` → 300ms 간격] → 녹화 종료 → 컷선택 | `src/MCPhoto.App/ViewModels/CaptureViewModel.cs:128-179` |
| F6 | 대표 슬롯 종횡비 = `frame.Slots[0].AspectRatio`(폴백 3:4). 세션 녹화(타임랩스 원본)는 웹캠 가공 프레임을 ffmpeg stdin으로 파이프 | `CaptureViewModel.cs:66`, `OpenCvCameraService.cs:164-173` |
| F7 | `FlashMode`는 **화면 플래시**다("촬영 직전 하양 화면") — 물리 플래시 아님 | `src/MCPhoto.Core/Settings/AppSettings.cs:60-61` |
| F8 | `ExternalCameraEnabled`는 placeholder(ini 저장/복원만, 실기능 미배선). 설정 UI 토글은 `IsEnabled="False"` + "추후 지원 예정", 섹션 전체가 게스트에게 Collapsed | `AppSettings.cs:118-125`, `src/MCPhoto.App/Views/SettingsView.xaml:368-404`, `SettingsViewModel.cs:226-228,335-341` |
| F9 | 설정 게스트 편집 게이트는 VM 3지점 패턴(Load 강제 off / Save 미기록 / XAML IsEnabled). 게이트는 편집만 제한 — 런타임 기능은 ini값으로 게스트 세션에도 동작 | `SettingsViewModel.cs:308-341` + agent-memory `settings-guest-edit-gate` |
| F10 | 역할 판정은 서수 부등식 금지·명시 열거(`IsPower`/`CanWriteFrames` 패턴). TempUser도 로그인 가능한 역할이다 | `src/MCPhoto.Core/Models/UserRole.cs:54-64` |
| F11 | 카메라 테스트 모달: 설정의 웹캠 ComboBox에서 고른 인덱스를 `ICameraTestDialogService.ShowAsync(int deviceIndex)`로 전달. VM은 Stop→Start(인덱스)로 Singleton 카메라를 재점유, 닫기 시 Stop | `src/MCPhoto.App/Services/ICameraTestDialogService.cs:9`, `CameraTestViewModel.cs:44-75,110-115` |
| F12 | 장치 열거는 OpenCV 인덱스 프로빙(0~7 open/close) + WMI FriendlyName best-effort 매핑. 동작 기준은 항상 인덱스 | `OpenCvCameraService.cs:308-334`, `CameraNameProbe.cs` |
| F13 | `ICameraService`는 DI **Singleton** — UVC 웹캠 단일 점유 제약 때문. `StartAsync`는 running이면 파라미터 무시 → 전환은 반드시 Stop→Start | `ServiceRegistration.cs:63` + agent-memory `camera-singleton-constraint` |
| F14 | ini 인프라: `[MCPhoto]` 단일 섹션, `IniFile` 다중 섹션 지원, 손상 키 기본값 폴백, `Clamp()`가 범위 강제, `Clone()`은 전 필드 수동 복사 | `IniSettingsService.cs:134-211`, `AppSettings.cs:160-272` |
| F15 | 배포 번들(키 등)은 csproj가 아니라 **publish.ps1 레벨**에서 포함하는 것이 이 리포의 확정 관례 | agent-memory `it10-server-key-distribution` |
| F16 | .cs 파일은 UTF-8 **no BOM**(한글 주석 포함) — 신규 파일도 no BOM | agent-memory `source-file-encoding` |

### 1.2 팀리드 실측 (재조사 불필요 — 이 문서의 외부 전제)

| # | 사실 |
|---|---|
| T1 | Nikon 공식 SDK는 D5300을 지원 대상에 포함. D5300은 MAID 방식의 **`Type0011.md3` 모듈**을 쓰며 이 모듈은 **D5300 전용**(다른 바디와 공유 안 함) |
| T2 | 커뮤니티 래퍼(sourceforge nikoncswrapper) 지원 매트릭스에서 D5300 상태는 **`B` = 동작 보고는 있으나 미검증**. 기능별 지원 표는 존재하지 않음 |
| T3 | SDK 취득은 4단계 신청 절차(카테고리→SDK 선택→라이선스 동의→다운로드). **현재 SDK 실물은 리포에 없다** |
| T4 | md3는 Nikon 독점 바이너리(확장자만 다른 DLL) — 상용 배포 시 재배포 라이선스 조건 확인 필요 (§13) |
| T5 | 공개 문서로 확인된 SDK 기능 서술은 "셔터 속도·조리개·ISO 조정 및 셔터 릴리즈"까지. **LiveView 스트림·동영상 녹화의 D5300 지원 여부는 공개 근거로 확인되지 않음** |
| T6 | (rev2) Nikon SDK License Agreement **제3자 호스팅 사본**([canfieldsci.com PDF](https://www.canfieldsci.com/common/docs/eulas/Nikon-SDK_License.pdf), pdftotext 추출)에서 확인한 조항: §1(a) 단일 컴퓨터 한정 — 시스템·다중 CPU·네트워크 사용은 Nikon **supplementary license** 선행 필요, §1(b) 백업 1부 외 복제 금지·저작권 고지 재현 의무, §2 타인 배포 금지("may not make or distribute copies of the SOFTWARE to others")·양도/개작/**DISTRIBUTE**/**CREATE DERIVATIVE WORKS** 금지·리버스 엔지니어링/역컴파일/디스어셈블 금지(trade secrets), §4 구매·다운로드 국가 외 반출 금지, §6 일본법 준거·도쿄 지방법원 관할. ⚠️ **신뢰 등급**: Nikon 공식 게시가 아닌 제3자(의료영상 업체) 사본이며 본문에 "disk"·"authorized Nikon Dealer"·"refund" 등 일반 제품 EULA 서식 정황이 있다 — **SDK 신청 절차(T3)에서 실제 제시되는 약관과 동일하다는 보장 없음. 원문 대조 전 확정 사실로 승격 금지**(§2 A14) |

### 1.3 공개 웹 근거 (커뮤니티 래퍼 API 표면 — SDK 실물 재확인 전 참고용)

[nikoncswrapper Getting Started 위키](https://sourceforge.net/p/nikoncswrapper/wiki/Getting%20Started/)에서 코드 샘플로 확인:

- `new NikonManager("Type0003.md3")` — **md3 파일명이 생성자 문자열 인자**(모델별 교체)
- `manager.DeviceAdded/DeviceRemoved` 이벤트, 핸들러 `(NikonManager sender, NikonDevice device)`
- `device.Capture()`(편의 함수), `device.ImageReady` 이벤트 → `(NikonDevice sender, NikonImage image)`
- `device.GetCapabilityInfo()` → `NkMAIDCapInfo[]`(장치별 capability 열거 — §4 프로브의 근거), `device.GetInteger(eNkMAIDCapability.kNkMAIDCapability_BatteryLevel)`
- 종료 전 `manager.Shutdown()` **필수**(내부 스레드 정리 — 미호출 시 드라이버 불안정 경고)

[래퍼 토론 스레드](https://sourceforge.net/p/nikoncswrapper/discussion/general/thread/1c0724a0/)에서: 셔터 속도 = `GetEnum(kNkMAIDCapability_ShutterSpeed)` → `NikonEnum`(이산 목록 + `Index`) → `SetEnum`, 조리개 = `kNkMAIDCapability_Aperture`, ISO = `kNkMAIDCapability_Sensitivity`. **즉 노출 3요소는 SDK가 이산 열거로 준다** — §10 값 도메인 설계의 근거.

⚠️ 위 이름들은 커뮤니티 래퍼 문서 기준이며 **D5300 + 현행 SDK 버전에서의 실효성은 미검증**이다. 계약(Core 타입)에는 이 이름이 등장하지 않는다.

---

## §2 ⚠️ 미검증 항목 전수표 (open assumptions) — 검증 방법 매핑

이 표가 이 문서의 핵이다. 아래 항목은 **전부 설계상 "기본 비활성·폴백 동작"으로 처리**되어 있어, 어느 것이 거짓으로 판명돼도 앱은 크래시 없이 강등된다(요구 8). 검증 시점은 §15(SDK 도착 후 체크리스트)와 §16 WBS의 SDK 필요 단계.

| # | 미검증 가정 | 거짓이면 생기는 일 | 설계상 처리 | 검증 방법 |
|---|---|---|---|---|
| A1 | MAID 함수·상수 이름(§1.3의 `Capture`/`GetEnum`/`kNkMAIDCapability_*` 등)이 실제 SDK에서 그 이름·시그니처로 존재 | shim 구현이 컴파일 불가/오동작 | 이름은 **shim 1파일에만** 등장(§3.4). Core 계약은 무명(無名) POCO | SDK 헤더·샘플 코드 대조 (§15-C1) |
| A2 | D5300이 PC 제어 스틸 캡처(셔터 릴리즈→이미지 PC 전송)를 지원 | 외부 카메라 기능 전체 무용 | `CaptureAsync` null 폴백 → 웹캠 대체(§6.4). 기능 자체가 런타임 강등 | 실기 셔터 테스트 (§15-C5) |
| A3 | D5300 노출 3요소(셔터·조리개·ISO)의 이산 열거 조회·설정 가능 | 설정 화면의 노출 조정 무용 | 도메인 미확보 시 슬라이더 disable + 자유 입력 폴백(§10.3) | 실기 `GetEnum` 대조 (§15-C6) |
| A4 | 물리 플래시(내장 팝업) 제어 capability 존재 여부 | 없음 — 애초에 기본 Unsupported | 프로브가 Supported일 때만 이중 발광(§4.3). 현재 유일 활성 경로는 화면 플래시 | 실기 프로브 (§15-C7) |
| A5 | LiveView 스트림의 D5300 지원 여부 (T5: 공개 근거 없음) | 없음 — 설계상 **비목표**(§17.2). 프리뷰는 웹캠 전담 | capability에 자리만 두고 UI 미배선 | 실기 프로브 (§15-C7) |
| A6 | 동영상 녹화 시작/정지의 D5300 지원 여부 (T5: 공개 근거 없음) | 없음 — 타임랩스는 웹캠 전담(요구 1·2) | 상동 | 상동 |
| A7 | 이미지 수신 소요 시간 규모(셔터→`ImageReady` 완료). 수 초로 **추정** | 타임아웃 상수(§6.3 기본 10s)가 과소/과대 | 타임아웃을 상수 1곳(`ExternalCapturePolicy`)에 격리, 실측 후 조정 | 실기 10컷 연속 측정 (§15-C5) |
| A8 | 웹캠(DirectShow/UVC)과 DSLR(USB PTP/MAID)의 **동시 open 무충돌**. 서로 다른 OS 스택이므로 충돌하지 않을 것으로 추정 | 촬영 화면에서 둘 중 하나 열기 실패 | 세션 시작 시 DSLR 연결 실패 → 웹캠 단독 강등(§11 E4). 역방향(웹캠 실패)도 기존 Failed 경로 재사용 | 실기 동시 구동 (§15-C8) |
| A9 | "Nikon Webcam Utility" 등이 설치된 PC에서 DSLR이 UVC 웹캠으로도 열거될 수 있음(이중 점유 위험) | 웹캠 목록에 D5300이 중복 등장, 동시 open 충돌 | 운영 수칙으로 문서화(§11 비고). 코드 대응 비목표 | 실기 확인 (§15-C8) |
| A10 | md3 외에 wrapper·SDK 네이티브 DLL 의존 목록(런타임 파일 세트) | publish 번들 누락 → 시작 시 모듈 부재 강등 | 런타임 파일 프로브가 "무엇이 없는지" 사유 문자열로 노출(§4.2) | SDK 배포물 압축 해제 목록 (§15-C2) |
| A11 | nikoncswrapper의 라이선스가 상용 재배포 허용인지. (rev2) 추가 우려: 래퍼 자체가 SDK 헤더 기반 파생물이라면 T6 §2의 파생물 금지 조항과 충돌 가능 | wrapper 동봉 불가 → shim을 P/Invoke 직구현으로 대체 | wrapper는 shim 내부 구현 선택지일 뿐 — 계약 무영향(§3.4) | 라이선스 파일 확인 + T6 원문 대조 결과와 교차 판정 (§13, §15-C3) |
| A12 | ~~md3·SDK DLL의 재배포 라이선스 조건~~ (rev2 갱신) 제3자 사본(T6)에서 **재배포 금지·단일 컴퓨터 조항 확인** — 미검증으로 남은 것은 "그 사본이 실제 SDK 약관과 동일한가"(A14)뿐. 설계는 이미 **금지가 참**이라는 보수적 전제로 전환(§13.2 배포 경계) | (사본이 실제보다 관대하다고 판명되면) 동봉 배포로 완화 가능 — 구조 변경 없이 publish.ps1 1곳 | 미동봉 기본 + 파일 부재 강등(§11 E1·E2) | Nikon 원문 약관 대조 (§13.4, §15-C3) |
| A13 | 메모리카드 없는 바디에서의 캡처 동작(SDRAM 캡처 가부) | 카드 없으면 셔터 실패 | 캡처 실패 = E6 경로(재시도→웹캠 대체) — 별도 분기 불요 | 실기 카드 제거 테스트 (§15-C9) |
| A14 | (rev2) T6 제3자 사본이 SDK 신청 시 실제 동의하는 약관과 **동일 문서인지**(사본에 일반 제품 EULA 서식 정황 있음) | 실제 약관이 더 관대 → §13.2 완화 여지 / 더 엄격(예: 개발 PC 수 제한) → 개발 환경 절차 추가 | 보수적 전제(금지 참) 채택 — 어느 쪽으로 판명돼도 코드 구조 무변경 | **USER-ACTION**: SDK 신청 3단계(라이선스 동의 화면)의 원문 확보·대조 (§13.4, §15-C3) |

**가정 매핑 완결성**: A1~A14 전부 §15 체크리스트 항목(C1~C9)에 매핑됨. SDK 없이 완료되는 WBS 단계(§16 Step 1~9)는 위 가정 중 어느 것에도 의존하지 않는다 — 가정이 전부 거짓이어도 Step 1~9의 산출물은 "외부 카메라 = 항상 강등(웹캠 단독)"인 채로 정상 동작한다.

---

## §3 경계 설계 — SDK 수정 지점을 1파일로 국소화

### 3.1 3층 구조

```
[MCPhoto.Core]                        SDK 무지(無知). POCO 계약만.
  Devices/IExternalCamera.cs          ← 확장(멤버 추가, §3.2)
  Devices/ExternalCameraTypes.cs      ← 신규: capability·노출 도메인 POCO
  Devices/ExternalCameraModels.cs     ← 신규: 모델→md3 레지스트리 (§3.3)
  Devices/ExternalCapturePolicy.cs    ← 신규: 타임아웃·재시도·강등 순수 정책
  Devices/NullExternalCamera.cs       ← 추가 멤버 no-op 구현
        ▲ 계약(컴파일 의존)
[MCPhoto.Devices.Nikon] (신규 프로젝트)
  NikonExternalCamera.cs              오케스트레이션: 상태머신·타임아웃·이벤트 수명.
                                      SDK 타입 미등장 — INikonSdkShim만 호출
  INikonSdkShim.cs                    무명(無名) 계약: "연결/캡처/열거조회/설정"의 추상 동사만
  MissingNikonSdkShim.cs              SDK 부재 구현: 항상 "모듈 없음(사유)" 반환
  NikonSdkShim.cs                     ★ SDK 도착 시 작성/수정하는 유일한 파일 ★
                                      (지금은 파일 자체를 만들지 않는다 — §16 Step 8 참고)
  SdkRuntimeProbe.cs                  {exe}\NikonSdk\ 파일 존재 검사(md3·DLL 목록)
        ▲ DI 등록
[MCPhoto.App]
  ServiceRegistration.cs              IExternalCamera → NikonExternalCamera(shim) 교체
```

**왜 신규 프로젝트인가**: `MCPhoto.Capture`는 OpenCvSharp(웹캠) 전담이다. SDK 관리 DLL 참조가 생기는 순간 그 어셈블리 전체가 SDK 유무에 인질로 잡힌다. 별도 프로젝트면 (a) SDK 참조 추가가 이 프로젝트의 csproj 1곳에 갇히고, (b) 배포 번들(md3·네이티브 DLL)의 publish.ps1 규칙(F15 관례)이 이 프로젝트 산출물 기준으로 기술되며, (c) 라이선스 이슈(§13) 발생 시 프로젝트째 제외해도 App은 `NullExternalCamera`로 돌아간다.

**왜 shim을 인터페이스로 또 쪼개는가**: `NikonExternalCamera`(오케스트레이션)는 타임아웃·재시도·이벤트 수명·강등 등 **지금 검증 가능한 로직**을 전부 담는다. 이것을 SDK 호출과 한 파일에 두면 SDK 도착 시 "고칠 파일 1개"가 "다시 읽고 이해할 파일 1개(수백 줄)"가 된다. shim을 분리하면 오케스트레이션은 지금 FakeShim으로 단위 테스트까지 끝내고(§14), SDK 도착 시 사람이 채울 것은 **얇은 번역 계층뿐**이다.

### 3.2 `IExternalCamera` 확장 — 기존 시그니처 불파괴

기존 4멤버(F1)는 그대로 두고 멤버를 추가한다. F2(소비자 0)이므로 파급은 `NullExternalCamera`와 테스트 fake뿐이다.

```csharp
public interface IExternalCamera
{
    // ── 기존 4멤버 유지(시그니처 불변) ──
    bool IsAvailable { get; }
    Task<bool> ConnectAsync(CancellationToken ct = default);
    Task<byte[]?> CaptureAsync(CancellationToken ct = default);   // 인코딩 이미지(JPG) 반환 계약 유지
    Task DisconnectAsync();

    // ── it23 추가 ──
    /// <summary>연결된 모델 표시명(미연결 null). 예: "Nikon D5300".</summary>
    string? ModelName { get; }

    /// <summary>사용 불가 사유(사용자 노출용 한국어 짧은 문구). 사용 가능하면 null. 예: "SDK 모듈 없음(Type0011.md3)".</summary>
    string? UnavailableReason { get; }

    /// <summary>capability 프로브 결과. 미연결이면 null. 실패 항목은 Unknown(§4).</summary>
    Task<ExternalCameraCapabilities?> GetCapabilitiesAsync(CancellationToken ct = default);

    /// <summary>노출 3요소의 이산 도메인+현재값. 미연결/미지원 null(예외 금지).</summary>
    Task<ExposureDomain?> GetExposureDomainAsync(CancellationToken ct = default);

    /// <summary>노출값 적용(도메인 내 표시 문자열로 지정). 미지원/불일치 false.</summary>
    Task<bool> SetExposureAsync(ExposureParameter parameter, string value, CancellationToken ct = default);

    /// <summary>물리 플래시 강제 발광 설정 시도. capability가 Supported일 때만 true 가능(§4.3). 현재 구현은 전부 false.</summary>
    Task<bool> TrySetPhysicalFlashAsync(bool enabled, CancellationToken ct = default);

    /// <summary>연결 상태 변화(USB 뽑힘 등). ⚠️ 임의 스레드에서 발생 — 구독자가 Dispatcher 마샬링(§12.1).</summary>
    event EventHandler<ExternalCameraConnectionChange>? ConnectionChanged;
}
```

신규 POCO(`ExternalCameraTypes.cs`, 전부 Core — UI·SDK 타입 무의존):

```csharp
/// <summary>capability 3상. Unknown은 "프로브 실패/미실시" — 게이트에서는 Unsupported와 동일하게 닫되, UI 사유 문구가 다르다(§4.1).</summary>
public enum CapabilityState { Unknown, Unsupported, Supported }

/// <summary>프로브 결과 묶음. 항목별 3상 — 부분 실패 허용.</summary>
public sealed record ExternalCameraCapabilities(
    CapabilityState StillCapture,
    CapabilityState ExposureControl,
    CapabilityState PhysicalFlash,
    CapabilityState LiveView,       // 자리만 확보(비목표, A5)
    CapabilityState VideoRecord,    // 자리만 확보(비목표, A6)
    int? BatteryLevelPercent);      // 조회 실패 null

public enum ExposureParameter { ShutterSpeed, Aperture, Iso }

/// <summary>한 파라미터의 이산 도메인: 카메라가 준 표시 문자열 목록(순서 보존) + 현재값 인덱스(-1=미확인).</summary>
public sealed record ExposureDomainEntry(IReadOnlyList<string> Values, int CurrentIndex);

/// <summary>노출 3요소 도메인. 파라미터별 미지원이면 해당 엔트리 null.</summary>
public sealed record ExposureDomain(
    ExposureDomainEntry? ShutterSpeed,
    ExposureDomainEntry? Aperture,
    ExposureDomainEntry? Iso);

public sealed record ExternalCameraConnectionChange(bool IsConnected, string? Reason);
```

**설계 근거**: (a) 값은 전부 `string`이다 — 셔터 `"1/125"`, 조리개 `"f/5.6"` 같은 표시 문자열을 SDK가 주는 그대로 운반한다(§1.3 공개 근거: `NikonEnum`은 이산 목록). 숫자 파싱·단위 해석을 Core에 넣으면 SDK 표기 관례라는 미검증 가정(A3)에 Core가 오염된다. (b) `LiveView`/`VideoRecord`는 요구 3의 "판별되면 열리는 구조" 원칙을 플래시 외 항목에도 대칭 적용한 자리 확보다 — UI 배선은 비목표(§17.2).

### 3.3 모델 레지스트리 — 모델 추가 = 표 한 줄

`ExternalCameraModels.cs` (Core, 정적 표):

```csharp
/// <summary>연동 가능 모델 1행. Md3FileName은 {exe}\NikonSdk\ 기준 상대 파일명(T1: md3는 모델 전용).</summary>
public sealed record ExternalCameraModel(string Id, string DisplayName, string Md3FileName);

public static class ExternalCameraModels
{
    /// <summary>지원 모델 표. 추가는 여기 한 줄(+publish 번들에 md3 한 개). Id는 ini 저장값 — 변경 금지.</summary>
    public static readonly IReadOnlyList<ExternalCameraModel> All = new[]
    {
        new ExternalCameraModel("NikonD5300", "Nikon D5300", "Type0011.md3"),
    };

    public static ExternalCameraModel Default => All[0];

    /// <summary>Id 조회(대소문자 무시). 미지 Id는 null — 호출측이 Default로 보정(Clamp, §7.2).</summary>
    public static ExternalCameraModel? Find(string? id) => ...;
}
```

- `Id`는 ini `ExternalCameraModel` 키의 저장값(§7)이자 레지스트리 키다. `Md3FileName`이 아니라 `Id`를 저장하는 이유: md3 파일명은 Nikon 사정(SDK 버전)으로 바뀔 수 있는 **바이너리 세부**고, Id는 우리 관례다.
- md3 파일명에 프레임 이름 `_` 금지 규약(it10)은 무관 — 이 파일은 Firebase에 올라가지 않는 로컬 바이너리다.
- (rev2) **모델 추가 = 코드 한 줄 + 법적 절차 1건.** 모델별 md3는 각각 별도 SDK 다운로드·별도 라이선스 동의 대상이다(T3 신청 절차가 SDK 단위). 레지스트리에 행을 늘리는 것은 코드상 자유지만, 해당 md3의 취득·사용 조건(§13)은 모델마다 독립적으로 성립해야 한다 — 표에 행을 추가하는 PR에는 대응 SDK의 약관 확인 기록을 요구한다.

### 3.4 shim 계약 — SDK 이름이 살 수 있는 유일한 집

`INikonSdkShim`(MCPhoto.Devices.Nikon)은 MAID의 **동사만** 추상화한다. 여기에도 SDK 이름은 없다:

```csharp
/// <summary>MAID SDK 원시 호출 경계. 구현체(NikonSdkShim)만 SDK 타입을 참조한다.
/// 모든 메서드는 예외 대신 실패 결과 반환(크래시 금지 관례 계승). 호출 스레드 보장 없음(§12.1).</summary>
internal interface INikonSdkShim : IAsyncDisposable
{
    /// <summary>모듈 로드+장치 대기. md3 절대경로를 받는다. 실패 시 (false, 사유).</summary>
    Task<(bool ok, string? reason)> OpenAsync(string md3Path, CancellationToken ct);

    /// <summary>셔터 릴리즈→이미지 수신 완료까지. 타임아웃은 호출측(오케스트레이션) 소관. 실패 null.</summary>
    Task<byte[]?> CaptureImageAsync(CancellationToken ct);

    Task<ExternalCameraCapabilities?> ProbeCapabilitiesAsync(CancellationToken ct);
    Task<ExposureDomain?> ReadExposureDomainAsync(CancellationToken ct);
    Task<bool> WriteExposureAsync(ExposureParameter parameter, string value, CancellationToken ct);
    Task<bool> WritePhysicalFlashAsync(bool enabled, CancellationToken ct);

    /// <summary>장치 탈락 통지(USB 뽑힘). 임의 스레드.</summary>
    event Action<string?>? DeviceLost;
}
```

- `MissingNikonSdkShim`: `OpenAsync` → `(false, "SDK 모듈이 설치되지 않았습니다")`, 나머지 null/false. **이번 이터레이션의 프로덕션 기본 구현.**
- `NikonSdkShim`: **지금은 파일을 만들지 않는다.** 빈 껍데기를 미리 두면 "미구현인데 존재하는" 파일이 생겨 §15 체크리스트의 "파일 생성부터 시작"이라는 명확한 신호가 사라진다. SDK 도착 시 §15-C4에 따라 신설.
- `SdkRuntimeProbe`: `{exe}\NikonSdk\{Md3FileName}` 존재 검사 + (A10 확정 후) 동반 DLL 목록 검사. 부재 시 사유 문자열 생성(`"NikonSdk\Type0011.md3 없음"`). `NikonExternalCamera.ConnectAsync`의 첫 관문 — **shim을 아예 호출하지 않고** 강등한다.

`NikonExternalCamera`(오케스트레이션)가 담는 것 — 전부 지금 Fake shim으로 테스트 가능:

| 책임 | 내용 |
|---|---|
| 연결 상태머신 | Disconnected → Connecting → Connected → Lost. 재진입 안전(`ConnectAsync` 중복 호출 시 진행 중 Task 공유 — it20 단일 비행 교훈) |
| 파일 프로브 선행 | md3 부재면 shim 미호출 강등, `UnavailableReason` 확정 |
| 캡처 타임아웃 | `ExternalCapturePolicy.CaptureTimeout`(기본 10s, A7) 초과 시 null 반환 + 로그. shim의 CancellationToken으로 전파 |
| DeviceLost 중계 | shim 이벤트 → `ConnectionChanged` 재발행 + `IsAvailable=false` 확정 |
| Shutdown 보장 | `DisconnectAsync`/`DisposeAsync`에서 shim DisposeAsync(§1.3: `Shutdown()` 미호출 시 드라이버 불안정 — App 종료 훅 §12.2) |

### 3.5 DI 배선 (`ServiceRegistration.cs:67` 교체)

```csharp
// it23: 외부 카메라 = Nikon 어댑터(오케스트레이션) + SDK shim.
// shim은 현재 MissingNikonSdkShim(항상 모듈 부재) — SDK 도착 시 NikonSdkShim으로 교체(설계 it23 §15-C4).
// 사용 여부 게이트(ExternalCameraEnabled)는 등록이 아니라 호출측(§6.1) — 설정은 런타임 변경 가능하기 때문.
services.AddSingleton<INikonSdkShim, MissingNikonSdkShim>();
services.AddSingleton<IExternalCamera>(sp => new NikonExternalCamera(
    sp.GetRequiredService<INikonSdkShim>(),
    sp.GetRequiredService<ISettingsService>(),   // 모델 Id → 레지스트리 → md3 경로
    sp.GetService<ILogger<NikonExternalCamera>>()));
```

- **Singleton인 이유**: 물리 장치 1대 + `manager.Shutdown()` 수명(§1.3)이 앱 수명과 일치해야 한다. 웹캠 `ICameraService` Singleton(F13)과 동형.
- `NullExternalCamera`는 등록에서 빠지지만 **삭제하지 않는다** — 테스트의 무해 기본값(§14)이고, MCPhoto.Devices.Nikon 프로젝트를 제외해야 하는 사태(A11/A12)의 즉시 복귀처다.
- `ExternalCameraEnabled`를 등록 시점에 읽지 않는 이유: 설정 화면에서 토글 후 **앱 재시작 없이** 다음 세션부터 반영되어야 한다(기존 설정 항목 전부의 관례). 게이트는 소비 지점 3곳(촬영 진입 §6.1, 테스트 모달 §9.3, 설정 노출 §9.2)에서 `settings.Current` 기준.

### 3.6 기존 시그니처 파급 정리

| 대상 | 변경 | 파급 |
|---|---|---|
| `IExternalCamera` | 멤버 추가(기존 4멤버 불변) | 구현 2개(Null·Nikon) + 테스트 fake. 프로덕션 소비자 0(F2) → 호출부 수정 없음 |
| `NullExternalCamera` | 추가 멤버 no-op 구현(`ModelName=null`, `UnavailableReason="외부 카메라 미구성"`, Get*=null, Set*=false, 이벤트 미발행) | 기존 4멤버 동작 불변 — 기존 테스트 그대로 통과해야 함(회귀 기준) |
| `ICameraTestDialogService.ShowAsync(int)` | **오버로드 추가** `ShowAsync(CameraTestTarget target)` — 기존 int 버전은 웹캠 타깃으로 위임 | 호출자 1곳(`SettingsViewModel.OpenCameraTest`)은 새 오버로드로 이행(§9.3). 기존 시그니처는 유지(테스트 호환) |
| `ICameraService` | **무변경** | 웹캠 파이프라인(프리뷰·타임랩스)은 이 설계에서 손대지 않는다(요구 1) |
| `CapturedStill` | **무변경** — DSLR 스틸도 이 타입으로 정규화(§5) | 하류(컷선택·필터·합성·재촬영) 무변경의 근거 |

---

## §4 capability 프로브 모델과 기능별 강등 표

### 4.1 프로브 시점·수명

- 프로브는 `ConnectAsync` 성공 직후 1회(`GetCapabilitiesAsync`가 캐시 반환, 재연결 시 무효화). 매 촬영마다 조회하지 않는다 — SDK 왕복은 비용·실패 가능성 모두 미지수(A1)다.
- 프로브 **자체가 실패**하면(예외·타임아웃) 전 항목 `Unknown`. Unknown은 게이트 판정상 Unsupported와 동일하게 **닫힘**이되, UI 사유가 다르다: Unsupported = "이 카메라가 지원하지 않는 기능입니다", Unknown = "기능 지원 여부를 확인하지 못했습니다".
- 판정은 순수 함수 `ExternalCapturePolicy`에 둔다(§14 테스트 대상): `bool IsOpen(CapabilityState s) => s == CapabilityState.Supported;`

### 4.2 기능별 강등 표 (요구 3 — 플래시·LiveView·동영상 각각)

| 기능 | Supported일 때 | Unsupported/Unknown일 때 | 프로브 실패·미연결일 때 |
|---|---|---|---|
| 스틸 캡처 | DSLR 셔터 경로 활성(§6) | 세션 시작 시 웹캠 단독 강등 + 토스트(§11 E4) | 상동 |
| 물리 플래시 | `FlashMode=on`이면 **화면 플래시 + 물리 플래시 둘 다**(§4.3) | 화면 플래시 단독(현행 유지) — **현재 유일 활성 경로** | 상동 |
| 노출 제어 | 설정·테스트 모달에서 도메인 슬라이더 활성(§10) | 슬라이더 disable + 자유 입력 폴백 + 사유 캡션 | 상동 |
| LiveView | (비목표 — UI 미배선, capability 값만 진단 노출) | — | — |
| 동영상 녹화 | (비목표 — 타임랩스는 웹캠 전담, 요구 1·2) | — | — |
| 배터리 | 테스트 모달 정보 패널에 % 표시 | "확인 불가" 표시 | 상동 |

### 4.3 플래시 이중 발광 훅 (요구 3)

촬영 시퀀스의 플래시 블록(F5의 `CaptureViewModel.cs:147-152` 패턴)을 다음으로 확장한다:

```
if (settings.FlashMode)
{
    FlashActive = true;                       // 화면 플래시 — 항상(유일 활성 경로)
    if (물리플래시게이트) await _external.TrySetPhysicalFlashAsync(true);   // Supported일 때만 시도
    await Task.Delay(120);
}
// 물리플래시게이트 = ExternalCameraEnabled && caps?.PhysicalFlash == Supported
```

- `TrySetPhysicalFlashAsync`는 "발광 모드 설정"이지 발광 트리거가 아니다 — 실제 발광은 셔터에 종속된다는 것이 통상적 동작이나 **이 역시 ⚠️ 미검증(A4)**. 그래서 호출 위치를 셔터 직전 1곳에 두고, 실패(false)해도 시퀀스는 계속된다(화면 플래시는 이미 켜져 있음).
- 현재 프로덕션에서 이 게이트는 **항상 닫혀 있다**(MissingShim → caps null). 훅이 존재하되 죽은 코드가 아닌 이유: 테스트 모달의 셔터 테스트(§9.3)가 같은 경로를 타므로 Fake로 열림 상태를 검증한다(§14 T-F6).

---

## §5 WYSIWYG — 이 문서에서 가장 중요한 절

### 5.1 현행 불변식이 성립하는 구조 (F3·F4)

현행 WYSIWYG는 "프리뷰에 보인 그 프레임이 곧 스틸"이라는 **물리적 동일성**으로 성립한다: 거울반전·중앙크롭을 캡처 스레드에서 프레임당 1회 수행한 버퍼가 프리뷰·녹화·스틸로 갈라진다. 필터는 촬영 후 합성 시 전체 컷 일괄(F4)이므로 컷 간 일관성도 자동이다.

DSLR 도입은 이 물리적 동일성을 **원리적으로 깬다** — 스틸이 다른 광학계에서 온다. 따라서 목표를 재정의한다:

> **it23의 WYSIWYG 계약**: "프리뷰와 결과물이 같은 픽셀"은 포기한다(불가능). 대신 **"프리뷰에 적용된 모든 소프트웨어 규칙(거울·종횡비 크롭·필터)이 결과물에도 동일하게 적용된다"**를 보장하고, 소프트웨어로 제거 불가능한 광학 차이(화각·원근·색감)는 사용자에게 **명시 고지**한다(§5.4).

### 5.2 수신 정규화 파이프라인 — 입구에서 통일하고 하류는 무변경

DSLR JPEG 수신 직후, 웹캠 캡처 스레드가 하는 일과 **같은 규칙·같은 순서**로 정규화해 `CapturedStill`을 만든다:

```
DSLR JPEG bytes (CaptureAsync 반환)
  → ① 디코드 (OpenCV Imdecode — MCPhoto.Capture에 배치, Core는 기하 정책만)
  → ② 거울반전: settings.MirrorMode면 Cv2.Flip(FlipMode.Y)   ← 웹캠 F3과 동일 조건·동일 연산
  → ③ 중앙크롭: CropCalculator.CenterCrop(w, h, 대표슬롯종횡비) ← 웹캠과 같은 함수 재사용(실측 F3)
  → ④ 축소 상한: 긴 변 > MaxIngestLongEdge(2400px)면 uniform 축소
  → ⑤ BGR24 연속 버퍼 추출 → CapturedStill { Width, Height, Pixels }
```

- **②·③이 웹캠과 같은 코드 경로**(`Cv2.Flip`/`CropCalculator.CenterCrop`)라는 것이 이 설계의 요점이다. 규칙을 복제하지 않고 재사용하므로, 웹캠과 DSLR의 기하 처리가 어긋날 수가 없다.
- ④가 필요한 이유: D5300은 24MP(6000×4000 급)로 BGR24 원시 버퍼가 **컷당 ~72MB**다. 10컷이면 720MB — `CapturedStill`을 세션 내내 들고 있는 현행 구조(F5 `AddCut`)에서 감당 불가. 2400px 상한이면 컷당 ~11.5MB, 10컷 115MB로 현행 웹캠(1080p 크롭 ~4.5MB/컷)의 수십 배가 아닌 수 배 수준이 된다. 합성 출력(프레임 캔버스)은 이보다 작으므로 화질 손실 없음. 상수는 `ExternalCapturePolicy.MaxIngestLongEdge` 1곳.
- 구현 배치: 기하 결정(크롭 사각형·축소 배율 계산)은 **Core 순수 함수** `ExternalStillNormalizePlan.Compute(srcW, srcH, slotAspect, mirror)`로 분리(→ headless 테스트, §14 T-N*), OpenCV 연산은 `MCPhoto.Capture/ExternalStillDecoder.cs`(신규)가 plan을 집행. `MCPhoto.Devices.Nikon`은 이미지 처리를 모른다 — bytes만 반환.

### 5.3 필터·컷선택·합성·재촬영이 무변경인 이유

| 하류 | 왜 무변경인가 |
|---|---|
| 컷선택 썸네일 | `CapturedStill` 픽셀을 그대로 렌더 — 소스 무관 |
| 필터 | 합성 시 `Filters.Apply(Mat, FilterKind)` 전체 컷 일괄(F4) — DSLR 컷도 같은 Mat 경로 |
| 합성 | `ComposeAsync(frame, cuts, filter, …)`는 컷이 이미 슬롯 종횡비라는 전제(②·③이 보장) |
| 재촬영(it11 #13) | 재촬영 = 같은 캡처 경로 재호출 — 소스 선택 로직(§6.1)이 컷 단위로 동일 적용 |
| 세션 영상/타임랩스 | 웹캠 녹화 그대로(요구 1) — DSLR과 무관 |

### 5.4 제거 불가능한 불일치와 고지 (숨기면 WYSIWYG 사기)

| 불일치 | 소프트웨어 제거 | 처리 |
|---|---|---|
| 종횡비(D5300 3:2 vs 웹캠 원본) | **가능** — ③이 동일 슬롯 종횡비로 크롭 | 제거됨. 단 3:2→세로형 슬롯 크롭은 좌우를 많이 버린다 — 화각 차이를 키우는 요인으로 아래 고지에 포함 |
| 거울모드 | **가능** — ②가 동일 적용 | 제거됨. 문자·로고가 뒤집히는 것까지 프리뷰와 동일(현행 웹캠 정책 계승) |
| 필터 | **가능** — 합성 일괄(F4) | 제거됨 |
| 화각·원근(다른 렌즈·설치 위치) | **불가능** | 고지 ①② |
| 색감·노출(다른 센서·픽처컨트롤) | **불가능** | 고지 ①②. 노출은 §10 설정으로 운영자가 보정 |
| 셔터 순간차(프리뷰 마지막 프레임 ≠ 셔터 순간) | **불가능**(웹캠도 엄밀히는 동일했음) | 고지 없음 — 현행에도 있던 미세 차이 |

고지 지점(동결 문구는 §9.4 표):
- **① 설정 화면**: 외부 카메라 섹션 활성 시 캡션 — 구도·화각 불일치와 타임랩스 웹캠 전담을 함께 고지.
- **② 촬영 화면**: 외부 카메라로 진행되는 세션에서 프리뷰 상단에 소형 배지 "외부 카메라 촬영 중 — 프리뷰는 참고용입니다" 상시 표시. 촬영 순간마다 안내를 띄우지 않는 이유: 카운트다운 UX를 방해하고, 불일치는 세션 내내 참인 성질이라 상시 배지가 정직하다.
- 게스트(손님)에게도 ②는 보인다 — 결과물이 프리뷰와 다르게 나오는 것을 겪는 당사자는 손님이다.

---

## §6 촬영 세션 시퀀스 — 두 카메라 동시 점유와 수신 지연

### 6.1 세션 시작 시 소스 확정 (컷마다 흔들지 않는다)

`CaptureViewModel.OnEnterAsync`에서 세션의 스틸 소스를 **1회 확정**하고 세션 내내 유지한다:

```
ExternalCameraEnabled(ini) == false
  → 현행 그대로(웹캠 스틸). 이 설계의 코드가 전혀 개입하지 않는 경로 — 회귀 0 원칙.
ExternalCameraEnabled == true
  → _external.ConnectAsync() (타임아웃 ExternalCapturePolicy.ConnectTimeout=5s)
      성공 + caps.StillCapture==Supported → 소스 = External
      실패/미지원                         → 소스 = Webcam 강등 + 토스트(§11 E4) — 세션은 계속
  → 웹캠 StartAsync는 소스와 무관하게 시도(프리뷰·타임랩스 담당, 요구 1)
      웹캠 실패 + 소스 External → 프리뷰 없는 촬영 모드(§6.5) — 세션은 계속
      웹캠 실패 + 소스 Webcam   → 현행 Failed 경로(촬영 불가) 그대로
```

컷마다 소스를 재판정하지 않는 이유: 컷 중간에 소스가 바뀌면 (a) 컷 간 화질·화각이 뒤섞이는 것이 **기본 동작**이 되고, (b) 재촬영의 "같은 조건 재촬영" 의미가 무너진다. 소스 전환은 **실패 강등**(§6.4)이라는 예외 경로에서만 일어나고, 일어나면 반드시 사용자에게 배너로 알린다.

### 6.2 두 카메라 동시 점유 판정

- 웹캠 = DirectShow/UVC(F3), DSLR = USB PTP/MAID — **다른 OS 스택이라 동시 open 무충돌로 추정(⚠️ A8)**. 설계는 이 가정이 깨져도 안전하다: DSLR 연결 실패는 E4 강등, 웹캠 열기 실패는 기존 Failed/프리뷰 없는 모드로 각각 독립 처리된다. 즉 A8이 거짓이어도 새 크래시 경로는 없다.
- `ICameraService`(웹캠)는 Singleton 재시작 불가 제약(F13) 그대로 — 이 설계는 웹캠 서비스를 건드리지 않는다.
- `IExternalCamera`도 Singleton(§3.5)이므로 촬영 화면과 테스트 모달이 같은 인스턴스를 공유한다. 테스트 모달은 설정 화면에서만 열리고 촬영 중에는 설정 진입이 불가(오버레이 네비게이션 구조 — agent-memory `mcphoto-settings-ini-infra`)하므로 **동시 사용 경합은 구조적으로 없다**. 방어로 `NikonExternalCamera`는 캡처 진행 중 재진입 `CaptureAsync`를 즉시 null 반환(단일 비행).

### 6.3 컷 루프 타이밍 — 수신 지연이 카운트다운을 깨는 문제

현행 컷 루프(F5)는 `CaptureStillAsync`가 **다음 프레임 1장**(수십 ms)이라는 전제로 컷 간 300ms만 쉰다. DSLR은 셔터→JPEG 수신까지 **수 초(⚠️ A7)** 걸리므로 그대로 두면 두 가지가 깨진다: (a) 다음 컷 카운트다운이 이미지 없는 채로 시작되고, (b) 취소·이탈 시 수신 중 버퍼가 유실된다.

**판정: 순차 대기 + 대기 상태 가시화.** 컷 N의 이미지가 정규화 완료될 때까지 컷 N+1 카운트다운을 시작하지 않는다.

```
for cut in 1..TotalCuts:
    카운트다운(CountdownSec)                  ← 현행 동일([바로촬영] 스킵 포함)
    화면 플래시 (+물리 플래시 게이트, §4.3)    ← 현행 위치 동일
    셔터음                                    ← 현행 동일
    소스 External:
        IsReceiving = true  → 프리뷰 위 "사진 전송 중…" 오버레이(카운트다운 숫자 대신)
        bytes = await _external.CaptureAsync(ct)   [타임아웃 10s = CaptureTimeout]
        실패(null/타임아웃) → §6.4 강등 절차
        성공 → 정규화(§5.2, Task.Run — UI 스레드 금지 §12.1) → AddCut
        IsReceiving = false
    소스 Webcam: still = await _camera.CaptureStillAsync(ct)   ← 현행 동일
    컷 간 300ms                                ← 현행 동일
```

- 파이프라이닝(수신 대기 중 다음 카운트다운 시작)을 **명시적 비목표**(§17.2)로 한다. 세션 총 시간은 컷당 최대 +10s(타임아웃 상한) 늘어날 수 있으나, 통상 +1~3s(추정)이고, 겹치기 도입 시 재촬영 컷 순서·세션 영상 동기·취소 전파 3곳이 동시에 복잡해진다. 실측(§15-C5) 후 필요하면 별도 이터레이션.
- 유휴 감시: 촬영 화면은 유휴 제외 대상(it4 B5에서 편집기와 동일 계열 처리) — 수신 대기가 유휴로 오인되지 않는지 Step 7에서 확인 항목으로 남긴다.

### 6.4 컷 실패 강등 절차 (키오스크 UX — 세션을 죽이지 않는다)

```
CaptureAsync 실패(null/타임아웃/수신 중 연결 끊김)
  → 1회 재시도 (재연결 포함: ConnectAsync → CaptureAsync)
  → 재시도 실패:
      웹캠 살아 있음 → 이 컷부터 세션 끝까지 소스 = Webcam으로 강등(컷 단위 복귀 없음)
                        + 상단 배너 "외부 카메라 연결이 끊겨 웹캠으로 촬영합니다" (세션 잔여 기간 유지)
      웹캠 없음      → 세션 중단, ReturnHome("외부 카메라 오류") + 토스트(§11 E7)
```

- "이 컷부터 끝까지" 강등(컷별 재판정 없음)인 이유: 실패한 장치를 컷마다 재시도하면 매 컷 10s 타임아웃을 손님이 반복 대기한다. 배너를 세션 내내 유지하는 이유: 앞 컷과 뒤 컷의 화질이 달라진 사실의 고지다.
- 이미 확보한 컷은 유지한다 — 혼합 소스 세션이 되지만 ②·③ 정규화(§5.2) 덕에 기하는 동일하고, 화질 차이는 배너로 고지된다. 전량 폐기·재시작보다 손님 피해가 작다.

### 6.5 웹캠 부재 모드 (요구 2 — "웹캠은 있는 경우에만")

소스 External + 웹캠 열기 실패(또는 장치 0대):

| 요소 | 동작 |
|---|---|
| 프리뷰 | 검정 배경 + 중앙 안내 "프리뷰 없음 — 외부 카메라로 촬영됩니다" (CameraLoadState에 신규 국면 추가 없이, `PreviewAbsent` bool로 오버레이만) |
| 카운트다운·플래시·셔터 | 정상 동작(화면 플래시는 프리뷰와 무관한 전체 오버레이라 그대로 유효) |
| 세션 녹화(타임랩스) | 시작하지 않음 → `SendTimelapse` 산출물 없음. QR 흐름은 타임랩스 없는 세션을 이미 처리(it7 F3: null=옵션 꺼짐 추론) — 신규 분기 불요 |
| 컷선택·합성·QR | 정상(컷은 DSLR에서 공급) |

- Ready 게이트(F5): 소스 External이면 웹캠 안정 프리뷰 대기를 **건너뛰고** DSLR ConnectAsync 성공을 Ready 조건으로 삼는다(웹캠이 있으면 병행 시작하되 웹캠 실패가 세션을 막지 않음).

---

## §7 설정 스키마 변경 (ini `[MCPhoto]` 섹션)

### 7.1 키 신설·변경 목록

| 키 | 타입 | 기본값 | 의미 | Clamp 규칙 |
|---|---|---|---|---|
| `ExternalCameraEnabled` | bool | `false` | (기존 키 — placeholder에서 **실배선으로 승격**) | 없음(bool) |
| `ExternalCameraModel` | string | `NikonD5300` | 레지스트리 Id(§3.3) | Trim 후 `ExternalCameraModels.Find` 실패 시 `Default.Id`로 보정 |
| `ExternalShutterSpeed` | string | `""` | 셔터 속도 표시 문자열(예: `1/125`). **빈 값 = 미지정(카메라 현재값 유지)** | Trim만 — 도메인 검증은 적용 시(§10.2). ini에 도메인이 없으므로 Clamp가 판정 불가 |
| `ExternalAperture` | string | `""` | 조리개(예: `f/5.6`). 빈 값 = 미지정 | Trim만 |
| `ExternalIso` | string | `""` | ISO(예: `400`). 빈 값 = 미지정 | Trim만 |

- **인덱스가 아닌 문자열 저장**인 이유(§0.3 판정): `NikonEnum`의 인덱스는 노출 모드·렌즈·SDK 버전에 따라 목록이 달라지면 표류한다. 문자열은 "카메라가 그 값을 지금 지원하면 적용, 아니면 건너뜀"이라는 안전한 재매칭 의미론을 갖고, SDK 없이도 저장·복원·테스트가 완결된다.
- 노출값을 ini에 저장하는 이유: 부스 운영 조명은 고정적이다 — 운영자가 맞춘 노출이 재시작 후에도 유지되어야 한다. 적용 시점은 `ConnectAsync` 성공 직후(§10.2).
- `PhotoPrinterEnabled`는 **이번 범위 밖** — placeholder 그대로 둔다(UI 문구만 §9.2에서 분리).

### 7.2 코드 갱신 지점 (전부 실측 파일:줄)

| 파일 | 갱신 |
|---|---|
| `AppSettings.cs` | 필드 4개 신설(§7.1) + placeholder 주석(118-120줄) 갱신. `Clamp()`(163줄~)에 모델 Id 보정·Trim 추가. `Clone()`(236줄~)에 4필드 복사 추가 — **Clone 누락은 편집 취소 시 값 유실**이 되므로 §14 T-S3이 회귀 잠금 |
| `IniSettingsService.cs` | `ReadInto`(136줄~)·`WriteFrom`(174줄~)에 4키 추가(`nameof` 관례 유지). 노출 3키는 `GetString`/`Set` |
| `SettingsViewModel.cs` | Load(207줄~)/Save(302줄~)에 4필드 왕복 + 권한 게이트(§8) |

- 기존 ini에 키가 없으면 기본값 — `IniFile` 손상·누락 폴백(F14) 그대로. 마이그레이션 불요.
- `ExternalCameraEnabled=true`가 **기존 설치 ini에 이미 기록돼 있을 수 있다**(placeholder 시절 저장 왕복). 종전엔 Disable 토글이라 사용자가 켤 수 없었으므로 실제로는 항상 false겠지만, 만약 true라도 shim이 Missing이라 E1 강등 토스트가 뜰 뿐 오동작은 없다 — 마이그레이션 코드를 넣지 않는 근거.

---

## §8 권한 게이트 — User 역할 이상 (요구 7)

### 8.1 판정 함수 (명시 열거 — 서수·랭크 부등식 금지)

`UserRoleExtensions`에 추가:

```csharp
/// <summary>
/// 외부 장치(DSLR) 설정 편집 권한(it23 §8). User 이상 — TempUser 제외.
/// ⚠️ HierarchyRank 부등식으로 쓰지 않는다 — 역할 추가 시 편집 권한이 조용히 따라 움직이는 것을
///    막기 위해 명시 열거를 유지한다(IsPower·CanWriteFrames와 같은 이유, UserRole.cs 규약).
/// </summary>
public static bool CanConfigureExternalCamera(this UserRole role)
    => role is UserRole.User or UserRole.AdvancedUser or UserRole.Manager or UserRole.Admin;
```

### 8.2 "사용 가능"의 해석 — 편집 게이트이지 동작 게이트가 아니다

요구 7 "User 역할 이상에서 사용 가능"을 기존 설정 게이트 관례(F9: 게이트는 편집만 제한, 기능은 ini값으로 동작)와 정합시킨다:

- **편집**(토글·모델·노출값 변경): `CanConfigureExternalCamera(로그인 역할)` — TempUser·게스트 불가.
- **동작**(촬영 세션에서 DSLR 사용): ini의 `ExternalCameraEnabled` 기준 — 관리자가 켜 두면 게스트(손님) 세션도 DSLR로 촬영된다. 부스의 손님이 장비 구성을 바꿀 수는 없지만 장비로 찍히는 것은 당연하다는 키오스크 모델 그대로다.
- 이 해석이 틀렸다면(= TempUser 세션에서 DSLR 동작 자체를 막아야 한다면) 소스 확정(§6.1)에 역할 조건 1줄 추가로 끝난다 — 구조 변화 없음. **구현 전 사용자 확인 권장(USER-DECISION)**.

### 8.3 3지점 게이트 확장 (F9 패턴 그대로 — 새 메커니즘 금지)

`SettingsViewModel`에 `CanEditExternalCamera` 파생 속성(= `IsLoggedIn && 로그인역할.CanConfigureExternalCamera()`, 설정 진입 중 불변이라 INPC 불요)을 두고:

1. `LoadSettings`: 편집 불가면 강제 off 하지 **않는다** — 기존 게이트(QR 등)의 "게스트 강제 off"는 게스트에게 섹션이 아예 안 보이는 항목이었지만, 외부 카메라 섹션은 TempUser에게 **보이되 읽기 전용**(§9.2)이므로 ini 원값을 그대로 표시한다.
2. `SaveSettings`: `if (CanEditExternalCamera) { s.ExternalCameraEnabled = …; s.ExternalCameraModel = …; s.ExternalShutterSpeed = …; … }` — 편집 불가 세션은 미기록 → ini 원값 보존(클로버 금지).
3. XAML: 섹션 내 편집 컨트롤 전부 `IsEnabled="{Binding CanEditExternalCamera}"` + `Toggle.Gated` 계열 툴팁(F9 이력의 hover 관례).

게스트: 현행대로 섹션 자체 Collapsed(`IsLoggedIn` Visibility, F8) 유지.

---

## §9 UI 명세

### 9.1 설정 화면 — 외부 장치 섹션 개편 (`SettingsView.xaml:368-404` 대체)

```
외부 장치                                   ← 그룹 제목("(추후 지원)" 삭제)
  [캡션] 타임랩스 기능은 웹캠으로만 동작됩니다.     ← ExternalCameraEnabled=on일 때만 표시(요구 2)
  외부 카메라 사용            [토글]              ← IsEnabled=CanEditExternalCamera (F8의 IsEnabled="False" 해제)
  ── 이하 ExternalCameraEnabled=on일 때만 표시(BoolToVis) ──
  모델                        [ComboBox]          ← ExternalCameraModels.All, SelectedValuePath=Id
                                                    (it7 B9 교훈: SelectedIndex 금지, 값 기반 SelectedValue)
  [캡션] 프리뷰는 웹캠 영상입니다. 실제 사진은 외부 카메라로 촬영되어
         구도·화각이 프리뷰와 다를 수 있습니다.      ← §5.4 고지 ①
  셔터 속도    [슬라이더────────] [TextBox]        ← §10 (도메인 확보 시 슬라이더, 항상 TextBox)
  조리개       [슬라이더────────] [TextBox]
  ISO          [슬라이더────────] [TextBox]
  [캡션] 노출 목록은 카메라 연결 시 확인됩니다.     ← 도메인 미확보 상태에서만(§10.3)
  프린터 사용                  [토글 · Disable]    ← 현행 "추후 지원 예정" 유지(범위 밖)
```

- 노출 3행의 편집 활성도 `CanEditExternalCamera` 게이트(§8.3-3) 적용.
- 설정 진입 시 DSLR **자동 연결하지 않는다**. 도메인 확보는 사용자 명시 액션(테스트 모달의 연결, §9.3) 또는 촬영 세션의 연결 부산물로만 일어난다 — 설정 화면 진입이 USB 장치를 건드리는 부수효과를 만들지 않기 위해서다(설정은 열람 빈도가 높다). 확보된 도메인은 `NikonExternalCamera`가 세션 캐시로 보유(§4.1)하고 설정 VM이 조회한다.

### 9.2 TempUser 읽기 전용 노출

TempUser는 로그인 상태이므로 섹션이 보인다(F8 Visibility=IsLoggedIn). 단 §8.3에 따라 전 컨트롤 Disable + 게이트 툴팁. 값은 ini 원값 표시.

### 9.3 카메라 테스트 모달 개편 (요구 4)

현행(F11): 설정에서 웹캠 인덱스를 골라 `ShowAsync(int)`로 열고, 모달에는 장치 목록이 없다. 개편: **모달 상단에 장치 ComboBox 신설** — 요구 4의 "모달의 장치 목록"을 문자 그대로 충족하고, 웹캠↔외부 카메라를 모달 안에서 오가며 비교 확인할 수 있게 한다.

| 요소 | 명세 |
|---|---|
| 장치 목록 | `웹캠 항목들(EnumerateDevices, F12)` + `ExternalCameraEnabled=on이면 "Nikon D5300 (외부 카메라)" 1항목 추가`. 항목 타입은 신규 `CameraTestTarget`(Webcam(int index) / External) — sealed 계층 또는 enum+payload. ToString=표시명(F12의 ComboBox 폴백 관례) |
| 진입 | `ShowAsync(CameraTestTarget)` 신설(§3.6). 설정의 [카메라 테스트] 버튼은 현행 웹캠 선택 인덱스로 진입(초기 선택) |
| 웹캠 항목 선택 | 현행 동작 그대로(Stop→Start(인덱스), 프리뷰·플래시·셔터 재현·저장 없음). 목적 라벨 "타임랩스·프리뷰 확인" |
| 외부 항목 선택 | 웹캠 프리뷰 정지(StopAsync — Singleton 반납, F13) → `ConnectAsync` → 정보 패널: 모델명·배터리·capability 요약(§4.2의 사유 문구 포함)·노출 3요소 현재값/도메인 → [셔터 테스트] 버튼 |
| 셔터 테스트 | `CaptureAsync` → 수신 JPEG를 **정규화 없이 원본 비율로** 화면에 3초 표시 후 폐기(저장 없음 — 현행 모달 원칙 유지). "정규화 없이"인 이유: 이 화면의 목적은 카메라 자체 확인이지 합성 미리보기가 아니다. 플래시 옵션이 켜져 있으면 §4.3 이중 발광 경로 재현 |
| 노출 조정 | 테스트 모달에서도 §10 슬라이더+입력 노출(연결돼 있으므로 도메인 활성) — "카메라 세팅 확인" 요구의 실체. 변경값은 즉시 카메라 적용 + `CanEditExternalCamera`면 저장 대상 VM 값에도 반영 |
| 전환·닫기 | 외부→웹캠 전환 시 `DisconnectAsync` 하지 않고 유지(재연결 비용 회피), 모달 닫기 시 `DisconnectAsync` + 웹캠 StopAsync(현행) |

### 9.4 동결 문구 표

| ID | 위치 | 문구 |
|---|---|---|
| W1 | 설정 · 외부 장치 캡션 | `타임랩스 기능은 웹캠으로만 동작됩니다.` |
| W2 | 설정 · 화각 고지 | `프리뷰는 웹캠 영상입니다. 실제 사진은 외부 카메라로 촬영되어 구도·화각이 프리뷰와 다를 수 있습니다.` |
| W3 | 설정 · 도메인 미확보 | `노출 목록은 카메라 연결 시 확인됩니다.` |
| W4 | 촬영 · 상시 배지 | `외부 카메라 촬영 중 — 프리뷰는 참고용입니다` |
| W5 | 촬영 · 수신 대기 | `사진 전송 중…` |
| W6 | 촬영 · 강등 배너 | `외부 카메라 연결이 끊겨 웹캠으로 촬영합니다` |
| W7 | 촬영 · 시작 강등 토스트 | `외부 카메라를 사용할 수 없어 웹캠으로 촬영합니다 ({사유})` |
| W8 | 촬영 · 프리뷰 부재 | `프리뷰 없음 — 외부 카메라로 촬영됩니다` |
| W9 | 테스트 모달 · 외부 정보 패널 | `외부 카메라 — 카메라 세팅 확인 · 셔터 동작 테스트` |
| W10 | 공통 · 사유(모듈 없음) | `SDK 모듈이 설치되지 않았습니다` |
| W11 | 공통 · 사유(md3 없음) | `카메라 모듈 파일이 없습니다 (NikonSdk\{파일명})` |
| W12 | 공통 · 사유(미연결) | `카메라가 연결되지 않았습니다 (USB·전원 확인)` |
| W13 | 공통 · 사유(미지원) | `이 카메라가 지원하지 않는 기능입니다` |
| W14 | 공통 · 사유(확인 불가) | `기능 지원 여부를 확인하지 못했습니다` |

---

## §10 노출 값 도메인 — 슬라이더 ↔ 이산 목록 매핑

### 10.1 문제

셔터 속도·조리개는 연속량이 아니라 카메라가 허용하는 **이산 목록**(`1/200`, `f/5.6`, …)이고 SDK가 열거로 준다(§1.3). WPF `Slider`는 연속 double이다.

### 10.2 판정 — 인덱스 슬라이더 + 문자열 정합

- 슬라이더: `Minimum=0, Maximum=domain.Values.Count-1, IsSnapToTickEnabled=True, TickFrequency=1` — **값 = 도메인 인덱스**. 현재 눈금의 표시 문자열을 슬라이더 옆 라벨로 표시.
- TextBox(직접 입력): 입력 문자열을 Trim 후 도메인과 **대소문자·공백 무시 정확 일치**로 매칭. 일치 → 슬라이더 동기 + 적용. 불일치 → 적용하지 않고 입력란 하단 힌트 "카메라가 지원하지 않는 값" (근사 매칭은 하지 않는다 — `1/100`을 `1/125`로 바꿔 적용하는 것은 운영자 몰래 노출을 바꾸는 것).
- 저장(ini)은 항상 **문자열**(§7.1). 적용 시점: `ConnectAsync` 성공 직후 저장값 3종을 `SetExposureAsync`로 재적용 — 도메인에 없으면 건너뛰고 로그(카메라 현재값 유지). 컷마다 재적용하지 않는다.
- VM 표면: `ExposureParameterViewModel`(파라미터당 1개 — Domain, SelectedIndex, Text, IsDomainAvailable) 3인스턴스. 슬라이더/TextBox 동기화 루프 방지: `_normalizing` 플래그(SettingsViewModel 기존 관례).

### 10.3 도메인 미확보 폴백 (SDK 미연결 — 현재의 상시 상태)

| 상태 | 슬라이더 | TextBox | 캡션 |
|---|---|---|---|
| 도메인 확보(연결 후) | 활성 | 활성(도메인 검증) | 없음 |
| 미확보(미연결·미지원·SDK 없음) | **Disable** | 활성(**자유 입력** — 검증 불가, 저장만) | W3 |

자유 입력을 허용하는 이유: 운영자가 장비 없이 미리 값을 준비해 두는 워크플로(이 프로젝트의 선행 개발 상황 그 자체)를 막을 이유가 없고, 적용 시점 검증(§10.2)이 안전망이다.

---

## §11 실패·부재 경로 전수표

강등 원칙: **어느 행도 크래시·무한 대기로 가지 않는다.** 사용자 문구는 §9.4 동결표 ID 참조.

| ID | 상황 | 감지 지점 | 사용자 문구 | 강등 동작 |
|---|---|---|---|---|
| E1 | SDK(래퍼/shim) 미탑재 — 현 프로덕션 기본 | `MissingNikonSdkShim.OpenAsync` | W10 (설정·테스트 모달 사유 표시), 촬영 진입 시 W7 | 웹캠 단독. 토글은 켤 수 있으나 효과는 강등 — 요구 8의 "런타임 비활성 + 사유 노출" |
| E2 | md3 파일 없음(`{exe}\NikonSdk\` 부재·오배치) | `SdkRuntimeProbe`(shim 호출 전) | W11, 촬영 진입 시 W7 | 웹캠 단독 |
| E3 | md3 로드 실패(버전 불일치·손상) | `NikonSdkShim.OpenAsync` 실패 사유 | W10 계열(사유 원문 로그) | 웹캠 단독 |
| E4 | 카메라 미연결·전원 꺼짐(연결 시도 실패/타임아웃 5s) | `ConnectAsync` | W12, 촬영 진입 시 W7 | 웹캠 단독. 테스트 모달에서는 정보 패널에 W12 + [다시 연결] 버튼 |
| E5 | USB 뽑힘(유휴 중) | shim `DeviceLost` → `ConnectionChanged` | 설정·테스트 모달 열려 있으면 상태 갱신(W12) | `IsAvailable=false`. 다음 세션 시작 시 재연결 시도 |
| E6 | 촬영 중 연결 끊김·수신 타임아웃(10s)·캡처 null | 컷 루프(§6.4) | W5→(재시도)→W6 배너 또는 W7 | 1회 재시도 → 웹캠 강등(있으면) / 세션 중단(없으면 E7) |
| E7 | 강등할 웹캠도 없음 | §6.4 말단 | 토스트 "촬영을 계속할 수 없습니다 — 카메라를 확인해 주세요" | `ReturnHome` — 확보 컷 폐기(세션 리셋 관례). 완성 불가 세션을 컷선택으로 보내지 않는다 |
| E8 | 메모리카드 없음 | 캡처 실패로 표면화(⚠️ A13 — 별도 감지 불가 전제) | E6과 동일 | E6과 동일 — 카드 유무를 사전 판별할 수 있다고 판명되면(§15-C9) 사유 문구만 분화 |
| E9 | 노출 적용 실패(도메인 불일치·쓰기 거부) | `SetExposureAsync=false` | 설정·모달: 입력 힌트 "카메라가 지원하지 않는 값" / 연결 직후 일괄 적용은 무음 스킵+로그 | 카메라 현재값 유지 — 촬영은 계속 |
| E10 | capability 프로브 실패 | `GetCapabilitiesAsync` 예외/타임아웃 | W14(항목별) | 전 항목 Unknown → 게이트 닫힘(§4.1) |
| E11 | 수신 JPEG 디코드 실패(손상) | `ExternalStillDecoder` | E6과 동일 취급 | 캡처 실패로 편입 — 재시도 1회 대상 |

비고(⚠️ A9): "Nikon Webcam Utility"류가 설치된 PC에서는 D5300이 **웹캠 목록에도** 나타날 수 있다. 이 경우 웹캠으로서의 D5300과 PTP로서의 D5300을 동시에 열면 충돌 가능성이 있다 — 운영 수칙("부스 PC에 Webcam Utility를 설치하지 않는다")으로 처리하고 코드 감지는 비목표.

---

## §12 스레딩·수명·리소스·인코딩

### 12.1 스레드 경계

| 경계 | 규칙 |
|---|---|
| shim 이벤트(`DeviceLost`)·래퍼 콜백 | **임의 스레드 전제**(⚠️ SDK 스레딩 모델 미검증 — A1의 일부). `NikonExternalCamera`는 마샬링하지 않고 그대로 재발행, 문서화된 계약(§3.2)로 못박음 |
| VM 구독(`ConnectionChanged`) | UI 반영은 `Dispatcher.InvokeAsync` 경유(설정·테스트 모달 VM). CaptureViewModel은 이벤트 구독 대신 컷 루프의 await 결과로 감지(폴링 아님 — 실패가 결과로 도착) |
| 정규화(§5.2 디코드·크롭) | `Task.Run` — 24MP 디코드+크롭은 수백 ms 급으로 UI 스레드 금지 |
| 컷 루프 | 현행 async 루프 구조(F5) 유지 — UI 스레드에서 await, 무거운 일은 전부 await 너머 |

### 12.2 수명·구독 해제 (누수 0 원칙)

| 구독/자원 | 해제 경로 |
|---|---|
| `NikonExternalCamera` → shim `DeviceLost` | 어댑터 생성 시 1회 구독, `DisposeAsync`에서 해제. 어댑터는 Singleton — 앱 수명과 동일하므로 실질 누수 없음 |
| VM → `ConnectionChanged` | 설정 VM·테스트 모달 VM은 Transient(F: RegisterScreens) — 화면 이탈(`OnLeaveAsync`)·모달 닫힘에서 반드시 해제. `CameraTestViewModel`의 `FrameReady += / -=` try/finally 관례(실측 129-135줄)와 동형 |
| SDK Shutdown | App 종료: `App.OnExit`(또는 기존 종료 훅)에서 `IExternalCamera.DisconnectAsync` await — §1.3의 "Shutdown 미호출 시 드라이버 불안정" 대응. MissingShim에선 no-op |
| 수신 대기 CTS | 컷 루프 `_sessionCts` 연동(현행 취소 전파 구조 재사용) — 화면 이탈 시 수신 대기도 취소 |

### 12.3 리소스 키·XAML

- 신규 브러시·스타일 키를 만들지 않는다 — 기존 팔레트(`Brush.Scrim`, `Text.Caption`, `Toggle`, `Button.Secondary`)와 it20의 `Spinner.Ring`(수신 대기 W5에 재사용)로 충분. 신규 키 0 = 충돌 위험 0.
- 병합 딕셔너리 교차 `StaticResource` 금지(memory: `wpf-merged-dict-staticresource`) — 신규 키가 없으므로 자동 준수.
- ComboBox는 `SelectedValue`/`SelectedValuePath` 값 기반(it7 B9 — SelectedIndex clobber 재발 금지).

### 12.4 파일 인코딩

- 수정·신규 .cs 전부 **UTF-8 no BOM**(F16). XAML은 기존 파일 인코딩 유지. 완료 확인: `head -c 3 <file> | od -An -tx1` ≠ `ef bb bf`.

---

## §13 법적 선결 조건 — **출하 차단 가능 항목** (rev2 전면 개정)

이 프로젝트는 상용화 단계(Inno Setup 단일 exe 배포 검토 중)다. 아래는 부수 주의사항이 아니라 **출하를 막을 수 있는 항목**이며, 설계는 rev2부터 "금지 조항이 참"이라는 보수적 전제로 전환했다.

### 13.1 라이선스 조항 실측 (T6 — 제3자 사본 기준, 원문 대조 필요)

근거: Nikon SDK License Agreement의 [제3자 호스팅 사본](https://www.canfieldsci.com/common/docs/eulas/Nikon-SDK_License.pdf). **신뢰 등급 주의** — Nikon 공식 게시가 아니고 일반 제품 EULA 서식 정황이 있어(§2 A14) 확정 사실이 아니다. 그러나 "재배포 금지가 참일 가능성"만으로도 배포 아키텍처는 보수적으로 잡아야 한다 — 나중에 관대한 쪽으로 완화하는 것은 publish.ps1 1곳 수정이지만, 반대 방향은 출하 후 리콜이다.

| 조항(사본) | 내용 | MC포토에의 함의 |
|---|---|---|
| §1(a) | 소프트웨어를 **단일 컴퓨터에서만** 사용. 시스템·다중 CPU·네트워크 사용은 Nikon **supplementary license** 선행 필요 | **키오스크 다중 PC 설치가 곧 이 조항의 사정권.** 부스 N대 운영 시 PC마다 SDK 사본이 놓인다 — supplementary license 문의가 상용 전 필수(§13.4 P2) |
| §1(b) | 백업 목적 사본 1부 외 복제 금지, 사본마다 Nikon 저작권 고지 재현 의무 | 사내 개발 머신 간 md3 복사도 조항 문면상 제약 대상 — 개발 절차(§13.2 D3) |
| §2 | 타인에 사본 **배포 금지**, 네트워크 전송 금지. 양도·개작·번역·대여·재판매·**DISTRIBUTE**·**CREATE DERIVATIVE WORKS** 금지. 리버스 엔지니어링·역컴파일·디스어셈블 금지(trade secrets) | **인스톨러에 md3 동봉 = 문면상 위반.** 래퍼가 SDK 파생물로 해석될 여지(A11). 개발 방식 제약(§13.2 D5) |
| §4 | 구매·다운로드한 국가 외 반출·재수출 금지 | 해외 부스 전개 시 국가별 SDK 재취득 필요 가능성 — 현 단계 비목표, 기록만 |
| §6 | 일본법 준거, 도쿄 지방법원 전속 관할 | 분쟁 비용 구조 — 기록만 |

### 13.2 배포 경계 (이 설계의 확정 사항 — A14 판정과 무관하게 유효)

| ID | 경계 | 규칙 |
|---|---|---|
| D1 | **리포** | SDK 바이너리·헤더·샘플·문서를 **커밋하지 않는다.** `.gitignore`에 `**/NikonSdk/` 추가(Step 5 대상 파일에 포함). 개발자는 SDK를 각자 신청·취득해 `{exe 출력폴더}\NikonSdk\`(bin 하위 — 이미 ignore)나 리포 루트 `NikonSdk\`(신설 ignore)에 배치한다 |
| D2 | **인스톨러/publish** | **미동봉이 기본 아키텍처다**(임시 조치가 아님). publish.ps1에 SDK 파일 규칙을 만들지 않는다. 앱은 런타임에 `{exe}\NikonSdk\{Md3FileName}`을 **탐색**(§3.4 `SdkRuntimeProbe`)하고, 부재 시 "SDK 미설치" 강등(E1·E2). 운영자가 Nikon에서 직접 SDK를 받아 배치하는 것이 정규 설치 절차다 |
| D3 | **개발 절차** | 개발 머신 간 md3 파일 공유(메신저·공유폴더) 금지 — 각자 신청 취득(§1(b) 사본 제한). CI에는 SDK가 없으므로 빌드·테스트가 SDK에 의존하지 않는 구조(§3 경계)가 그대로 CI 요건이 된다 |
| D4 | **킬스위치 통합** | 사용자 지시 "실제 배포 시 옵션을 강제로 막으면 된다"의 구현 = **D2의 파일 부재 강등 그 자체.** 별도 feature flag·빌드 상수를 신설하지 않는다 — 배포물에 SDK가 없으면 외부 카메라 옵션은 켜도 W10/W11 사유와 함께 동작하지 않고, 배치하면 열린다. 차단 메커니즘이 라이선스 준수 메커니즘과 동일물이므로 이중 관리가 없다 |
| D5 | **배선 근거** | shim(`NikonSdkShim.cs`) 구현의 근거는 **SDK 동봉 공식 문서(API 사양서·샘플)만** 허용한다. md3에서 export 심볼을 덤프하거나 디스어셈블해 시그니처를 역추적하는 방식은 §2 리버스 엔지니어링 금지 조항 위반 소지 — **금지.** 공개 커뮤니티 자료(§1.3)는 설계 참고까지만, 구현 확정 근거는 항상 동봉 문서(§15-C1) |

### 13.3 ffmpeg 배포와 왜 정반대인가 (`docs/design/wpf-ffmpeg-licensing-and-distribution-design.md` 대비)

| | ffmpeg.exe | Nikon md3·SDK DLL |
|---|---|---|
| 라이선스 성격 | GPLv3 — 재배포를 **명시적으로 허용**, 조건(전문 동봉·고지·소스 접근 제공)을 요구 | 독점 EULA — 사본 기준 재배포 자체를 **금지** |
| 위반의 형태 | 동봉하되 **조건 미이행**이 위반 | **동봉 그 자체**가 위반(사본 기준) |
| 그래서 택한 방향 | 동봉 + `licenses/` 고지 + 소스 접근 제공(it22 인프라, `ServiceRegistration.cs:52`) | **미동봉** + 런타임 탐색 + 부재 강등 |
| 배포물 부재 시 | 타임랩스 생성 실패(기능 강등, 로그) | 외부 카메라 강등(E1·E2, 사유 노출) |

같은 "외부 바이너리 의존"이지만 라이선스가 요구를 정반대로 걸기 때문에 배포 전략도 정반대다 — ffmpeg 관례를 md3에 복사하면 안 되고, 그 역도 안 된다.

### 13.4 선결 항목 목록 (상태 추적)

| ID | 항목 | 상태 |
|---|---|---|
| P1 | **약관 원문 대조(A14)**: SDK 신청 3단계 라이선스 동의 화면의 실제 원문 확보 → T6 사본과 대조 | ⚠️ **USER-ACTION** — SDK 신청 시 동의 화면 전문 보존(스크린샷/사본) 요망 |
| P2 | **supplementary license 문의**: 키오스크 다중 PC 상용 운영이 §1(a) 사정권인지, 필요 시 취득 절차·비용 | ⚠️ USER-ACTION(Nikon 접촉) — 상용 출하 게이트 |
| P3 | **래퍼 라이선스·파생물 판정(A11)**: nikoncswrapper 라이선스 확인 + SDK 파생물 해석 여지 검토. 불허/불명 시 shim을 SDK 문서 기반 자체 구현으로 | ⚠️ 미확인 — §15-C3 |
| P4 | 오픈소스 고지: 래퍼 채택 시 `licenses/` 관례에 따라 고지 파일 추가 | 조건부(P3 결과에 종속) |

**배포 게이트**: P1·P2 해소 전에는 SDK 파일이 포함된 어떤 배포물도 만들지 않는다(D2가 기본이므로 추가 작업 없이 자동 준수). 개발·사내 검증은 각자 취득 + 수동 배치(D1·D3)로 진행 — 개발 일정을 막지 않는다(요구 8).

---

## §14 테스트 전략 — 실물 없이 검증하는 것 전부 (`tests/MCPhoto.Tests`, headless)

핵심 도구: `FakeExternalCamera : IExternalCamera`(모든 응답 시나리오 주입 가능) + `FakeNikonSdkShim : INikonSdkShim`(어댑터 오케스트레이션 검증용 — 지연·실패·이벤트 스크립트 주입).

### 14.1 Core 순수 정책 (SDK·UI 무관 — 전부 지금 완결)

| ID | 테스트 | 검증 |
|---|---|---|
| T-R1 | `ExternalCameraModels.Find` — 정확 Id·대소문자 무시·미지 Id null·`Default`=D5300/Type0011.md3 | §3.3 레지스트리 |
| T-P1 | `ExternalCapturePolicy.IsOpen` — Supported만 true, Unknown/Unsupported false | §4.1 |
| T-N1 | `ExternalStillNormalizePlan.Compute` — 6000×4000 + 슬롯 3:4 → 크롭 사각형이 `CropCalculator.CenterCrop`와 동치 | §5.2 ③ 재사용 증명 |
| T-N2 | 상동 — 긴 변 2400 초과 시 uniform 축소 배율, 이하면 원본 유지 | §5.2 ④ |
| T-N3 | 상동 — mirror on/off가 plan에 반영 | §5.2 ② |
| T-S1 | `AppSettings.Clamp` — 미지 `ExternalCameraModel` → Default 보정, 노출 3키 Trim, 빈 값 보존 | §7.1 |
| T-S2 | `IniSettingsService` 라운드트립 — 신설 4키 저장→로드 동일 | §7.2 |
| T-S3 | `AppSettings.Clone` — 신설 4필드 복사(누락 = 편집 취소 유실 회귀 잠금) | §7.2 |
| T-U1 | `CanConfigureExternalCamera` — TempUser=false, User/AdvancedUser/Manager/Admin=true. **명시 열거 5역할 전수** | §8.1 |

### 14.2 어댑터 오케스트레이션 (FakeShim 스크립트)

| ID | 테스트 | 검증 |
|---|---|---|
| T-A1 | md3 파일 부재 → `ConnectAsync=false`, `UnavailableReason`=W11 계열, **shim 미호출** | §3.4 프로브 선행 |
| T-A2 | `OpenAsync` 실패 사유 전파 → `IsAvailable=false` | E3/E4 |
| T-A3 | `CaptureImageAsync` 지연 > CaptureTimeout → null 반환(예외 없음) + 토큰 취소 전파 | §6.3, A7 격리 |
| T-A4 | `DeviceLost` 발화 → `ConnectionChanged(IsConnected=false)` 재발행 + `IsAvailable=false` | E5 |
| T-A5 | `ConnectAsync` 동시 2호출 → shim `OpenAsync` 1회(단일 비행) | §3.4 상태머신 |
| T-A6 | 연결 성공 직후 저장 노출값 3종 재적용 — 도메인 불일치 항목은 스킵(shim Write 미호출) | §10.2 |
| T-A7 | `DisposeAsync` → shim `DisposeAsync` 호출(Shutdown 보장) | §12.2 |
| T-A8 | 캡처 진행 중 재진입 `CaptureAsync` → 즉시 null | §6.2 단일 비행 |

### 14.3 촬영 세션 (FakeExternalCamera + 기존 FakeCameraService)

| ID | 테스트 | 검증 |
|---|---|---|
| T-C1 | Enabled=off → 외부 카메라 **미접촉**(Connect 호출 0) + 현행 웹캠 경로 그대로 | §6.1 회귀 0 |
| T-C2 | Enabled=on + 연결 성공 → 전 컷 External 소스, `AddCut` 수 = TotalCuts | §6.1/§6.3 |
| T-C3 | Enabled=on + 연결 실패 → 웹캠 강등 + W7 통지 상태 | E4 |
| T-C4 | 컷3에서 캡처 실패×2(재시도 포함) → 컷3부터 웹캠, 배너 상태 on, 컷1·2 유지 | §6.4 |
| T-C5 | 캡처 실패 + 웹캠 부재 → 세션 중단(`ReturnHome`) | E7 |
| T-C6 | 웹캠 부재 + External 정상 → `PreviewAbsent=true`, 녹화 미시작, 세션 완주 | §6.5 |
| T-C7 | 수신 대기 중 화면 이탈(`OnLeaveAsync`) → 취소 전파, 후속 AddCut 없음 | §12.2 CTS |
| T-F6 | FlashMode=on + PhysicalFlash=Supported(Fake) → `TrySetPhysicalFlashAsync(true)` 1회 호출 / Unsupported → 0회 | §4.3 게이트 |

### 14.4 설정·테스트 모달 VM

| ID | 테스트 | 검증 |
|---|---|---|
| T-V1 | TempUser 로그인 Load→Save — 외부 4필드 ini 원값 보존(미기록) | §8.3-2 |
| T-V2 | User 로그인 — 토글·모델·노출값 저장 반영 | §8.2 |
| T-V3 | 게스트 — 섹션 Visibility 게이트 현행 유지(기존 테스트 회귀) | F8 |
| T-V4 | 도메인 미확보 — 슬라이더 disable·TextBox 자유 입력 저장 | §10.3 |
| T-V5 | 도메인 확보 — TextBox 불일치 입력 시 미적용+힌트 상태, 일치 시 슬라이더 인덱스 동기 | §10.2 |
| T-V6 | 테스트 모달 장치 목록 — Enabled=off면 웹캠만, on이면 +외부 1항목 | §9.3 |
| T-V7 | 모달 외부 항목 선택 → 웹캠 StopAsync 후 ConnectAsync 순서 | §9.3 (F13 Singleton) |
| T-X1 | headless XAML 로드(기존 `XamlResourceTests` 관례) — 개편 설정 섹션·모달 바인딩 경로 유효 | §9.1/§9.3 |

### 14.5 실물 없이 검증 **불가능**한 것 (정직 목록)

실기 셔터·수신 시간·capability 실값·물리 플래시·동시 점유(A2·A4·A7·A8) — §15로 이월. 여기 나열한 자동 테스트가 전부 통과해도 "D5300에서 동작한다"는 명제는 **증명되지 않는다**. 증명되는 것은 "SDK가 계약대로 응답하면 앱이 설계대로 동작하고, 응답하지 않으면 설계대로 강등된다"까지다.

---

## §15 SDK 실물 도착 후 체크리스트 (파일별·순서대로)

> 전제: §16 Step 1~9 완료 상태. 이 목록만 따라가면 된다.

- **C1. API 대조**: SDK **동봉 공식 문서·샘플**에서 §1.3의 이름들(모듈 로드 진입점, Capture 계열, 열거 조회, capability 조회) 실재 확인. 어긋나면 — 고칠 곳은 `NikonSdkShim.cs`(신설 예정)뿐임을 재확인. Core·App 파일은 열 필요 없음. ⚠️ (rev2) 확인 수단은 동봉 문서로 한정 — md3 export 덤프·디스어셈블 금지(§13.2 D5)
- **C2. 런타임 파일 세트 확정(A10)**: SDK 배포물에서 md3 외 필수 DLL 목록 작성 → `SdkRuntimeProbe`의 검사 목록 상수 갱신 (`MCPhoto.Devices.Nikon/SdkRuntimeProbe.cs` 1곳)
- **C3. 라이선스 판정(§13.4 P1~P3)**: ① SDK 신청 동의 화면 원문 보존 → T6 사본과 대조(A14 종결) ② 다중 PC 키오스크의 supplementary license 필요 여부 Nikon 문의(P2) ③ 래퍼 라이선스·파생물 판정(P3). **판정 전 기본은 미동봉(D2) — 판정이 관대해도 바뀌는 것은 publish.ps1 1곳**
- **C4. shim 구현**: `MCPhoto.Devices.Nikon/NikonSdkShim.cs` **신설**(현재 부재가 정상) + csproj에 SDK/래퍼 참조 추가 + DI 등록 1줄 교체(`MissingNikonSdkShim` → `NikonSdkShim`, `ServiceRegistration.cs`). FakeShim 계약 테스트(T-A1~A8)의 시나리오가 곧 구현 명세다
- **C5. 실기 캡처 검증(A2·A7)**: 테스트 모달 셔터 테스트 → 수신 성공 확인. 10컷 연속 세션에서 셔터→수신 시간 실측 → `ExternalCapturePolicy.CaptureTimeout`(10s)·`ConnectTimeout`(5s) 조정
- **C6. 노출 도메인 검증(A3)**: 테스트 모달에서 셔터·조리개·ISO 도메인 표시 확인, M/A/S/P 모드별 차이 기록(모드에 따라 일부 파라미터가 잠길 수 있음 — 잠기면 E9 경로 확인)
- **C7. capability 실값 기록(A4·A5·A6)**: PhysicalFlash·LiveView·VideoRecord의 실제 프로브 결과 기록. PhysicalFlash=Supported면 §4.3 이중 발광 실기 확인
- **C8. 동시 점유 검증(A8·A9)**: 웹캠+DSLR 동시 세션 완주. Webcam Utility 설치 PC에서 장치 목록 오염 여부 확인 → 운영 수칙 문서 갱신
- **C9. 실패 경로 실기 재현**: 표 §11의 E4(전원 off)·E5(USB 뽑기)·E6(촬영 중 뽑기)·E8(카드 제거) 각 1회 — 문구·강등 동작이 표와 일치하는지. E8에서 카드 부재를 사전 판별 가능하면 사유 문구 분화(선택)
- **C10. WYSIWYG 실기 판정**: 같은 세션에서 웹캠 컷과 DSLR 컷의 기하 일치(거울·크롭) 육안 확인 + 화각 차이 정도를 보고 W2/W4 문구 강도 재평가

---

## §16 구현 WBS (템플릿: docs/templates/WBS_BLUEPRINT.md)

> 검증된 사실 = §1 (F1~F16·T1~T6), 미검증 가정 = §2 (A1~A14 → §15 C1~C9 매핑).
> **Step 1~9는 SDK 없이 완료 가능**하며 §2의 어느 가정에도 의존하지 않는다. Step S-A~S-C는 SDK 필요.
> 공통 검증 명령: `build-verify` 스킬(없으면 `dotnet build MCPhoto.sln` + `dotnet test tests/MCPhoto.Tests`).

### Step 1: Core 계약 — 타입·인터페이스 확장·레지스트리·정책
- **Context Brief**: DSLR 연동의 SDK 무지(無知) 계약층. `IExternalCamera`(현 4멤버 스캐폴드, 소비자는 DI 등록뿐)에 §3.2 멤버를 추가하고, capability/노출 POCO·모델 레지스트리·타임아웃 정책 상수를 신설한다.
- **대상 파일**: `src/MCPhoto.Core/Devices/IExternalCamera.cs`(확장), `ExternalCameraTypes.cs`(신규), `ExternalCameraModels.cs`(신규), `ExternalCapturePolicy.cs`(신규), `NullExternalCamera.cs`(추가 멤버 no-op), `tests/MCPhoto.Tests/…`(T-R1, T-P1)
- **선행 조건**: 없음
- **구현 내용**: §3.2 인터페이스 전문 + §3.3 레지스트리(D5300 1행) + `ExternalCapturePolicy`(ConnectTimeout=5s, CaptureTimeout=10s, MaxIngestLongEdge=2400, `IsOpen`)
- **검증 명령**: build-verify (빌드 + 전체 테스트)
- **완료 기준**: [관측] T-R1·T-P1 통과, 기존 전체 테스트 무손상 / [non-goal] 기존 4멤버 시그니처 diff 없음, `System.Windows`·OpenCV 참조 없음 / [trigger] 없음(순수 타입 추가)
- **롤백**: 커밋 revert(후속 단계와 독립)

### Step 2: 설정 스키마 — AppSettings·INI 왕복
- **Context Brief**: §7.1의 ini 4키(Enabled 승격 + 모델 + 노출 3종 문자열). 노출값은 인덱스가 아닌 표시 문자열, 빈 값=미지정.
- **대상 파일**: `src/MCPhoto.Core/Settings/AppSettings.cs`(필드·Clamp·Clone), `IniSettingsService.cs`(ReadInto/WriteFrom), 테스트(T-S1~S3)
- **선행 조건**: Step 1(`ExternalCameraModels.Find` — Clamp 보정에 사용)
- **구현 내용**: §7.1 표 그대로. placeholder 주석(AppSettings.cs:118-120) "실기능 미배선" 문구 삭제
- **검증 명령**: build-verify
- **완료 기준**: [관측] T-S1~S3 통과 + 기존 설정 라운드트립 테스트 무손상 / [non-goal] 기존 키 직렬화 형식 불변(ini diff는 신설 키 추가뿐) / [trigger] 없음
- **롤백**: 커밋 revert

### Step 3: 권한 판정 — `CanConfigureExternalCamera`
- **Context Brief**: User 이상(TempUser 제외) 편집 게이트. `UserRole.cs`의 명시 열거 규약(서수·랭크 부등식 금지) 준수.
- **대상 파일**: `src/MCPhoto.Core/Models/UserRole.cs`, 테스트(T-U1)
- **선행 조건**: 없음(Step 1·2와 병렬 가능)
- **구현 내용**: §8.1 코드 전문
- **검증 명령**: build-verify
- **완료 기준**: [관측] T-U1 5역할 전수 통과 / [non-goal] 기존 판정 함수(IsPower 등) diff 없음 / [trigger] 없음
- **롤백**: 커밋 revert

### Step 4: 수신 정규화 — 기하 plan(Core) + 디코더(Capture)
- **Context Brief**: WYSIWYG의 핵(§5.2). DSLR JPEG를 웹캠과 같은 규칙(거울→`CropCalculator.CenterCrop`→축소 상한)으로 `CapturedStill` 변환. 기하 결정은 Core 순수 함수, OpenCV 집행은 Capture.
- **대상 파일**: `src/MCPhoto.Core/Capture/ExternalStillNormalizePlan.cs`(신규), `src/MCPhoto.Capture/ExternalStillDecoder.cs`(신규), 테스트(T-N1~N3 + 디코더는 합성 JPEG 바이트로 왕복 검증)
- **선행 조건**: Step 1(`ExternalCapturePolicy.MaxIngestLongEdge`)
- **구현 내용**: §5.2 파이프라인 ①~⑤. plan은 crop rect·scale만 산출, decoder가 `Imdecode`→Flip→crop→resize→BGR24 추출(`OpenCvCameraService.ExtractBgr24`와 동일 규칙)
- **검증 명령**: build-verify
- **완료 기준**: [관측] T-N1~N3 통과 — 특히 T-N1의 `CropCalculator` 동치 / [non-goal] `OpenCvCameraService`·`ICameraService` diff 없음 / [trigger] 없음
- **롤백**: 커밋 revert

### Step 5: Nikon 어댑터 프로젝트 — 오케스트레이션 + Missing shim
- **Context Brief**: 신규 어셈블리 `MCPhoto.Devices.Nikon`(§3.1). SDK 타입 없이 상태머신·타임아웃·이벤트 수명을 완성하고, 프로덕션 기본 shim은 "모듈 없음" 고정. **`NikonSdkShim.cs`는 만들지 않는다**(§3.4 — SDK 도착 신호 보존).
- **대상 파일**: `src/MCPhoto.Devices.Nikon/MCPhoto.Devices.Nikon.csproj`(신규, Core만 참조), `NikonExternalCamera.cs`, `INikonSdkShim.cs`, `MissingNikonSdkShim.cs`, `SdkRuntimeProbe.cs`, `MCPhoto.sln`, `.gitignore`(`**/NikonSdk/` 추가 — §13.2 D1), 테스트(T-A1~A8, FakeNikonSdkShim)
- **선행 조건**: Step 1, Step 2(설정에서 모델 Id 읽기), Step 4(캡처 결과 정규화 호출은 App층이지만 어댑터 반환 계약 bytes 확인)
- **구현 내용**: §3.4 표의 5책임 + `SdkRuntimeProbe`(md3 존재 검사, 목록은 레지스트리 기준)
- **검증 명령**: build-verify
- **완료 기준**: [관측] T-A1~A8 통과 / [non-goal] 이 프로젝트가 OpenCvSharp·WPF·SDK를 참조하지 않음(csproj 검사) / [trigger] `ConnectAsync` 호출 없이는 어떤 파일 I/O도 발생하지 않음
- **롤백**: 솔루션에서 프로젝트 제거(다른 단계 무영향 — DI 교체는 Step 6)

### Step 6: DI 배선 교체 + App 종료 훅
- **Context Brief**: `ServiceRegistration.cs:67`의 `NullExternalCamera` 등록을 §3.5로 교체하고, App 종료 시 `DisconnectAsync`(SDK Shutdown 보장, §12.2)를 건다. Missing shim이므로 실행 동작은 현행과 동일해야 한다.
- **대상 파일**: `src/MCPhoto.App/ServiceRegistration.cs`, `src/MCPhoto.App/App.xaml.cs`(종료 훅)
- **선행 조건**: Step 5
- **구현 내용**: §3.5 코드 + 종료 훅. `NullExternalCamera`는 삭제하지 않음
- **검증 명령**: build-verify + 앱 기동 스모크(홈 표시·종료 무예외)
- **완료 기준**: [관측] 기동·종료 정상, DI 해석 예외 없음 / [non-goal] 촬영·설정 화면 동작 변화 없음(강등이 조용히 성립 — 외부 카메라 off 기본값이므로 접촉 자체가 없음) / [trigger] 어댑터 생성은 첫 `IExternalCamera` 해석 시점뿐
- **롤백**: 등록 2줄 원복

### Step 7: 촬영 세션 — 소스 확정·컷 루프·강등·프리뷰 부재
- **Context Brief**: `CaptureViewModel`(F5 시퀀스)에 §6 전체를 배선한다: 세션 시작 소스 확정, 수신 대기 상태(W5), 실패 강등(W6/W7), 웹캠 부재 모드(W8), WYSIWYG 배지(W4), 플래시 이중 발광 훅(§4.3).
- **대상 파일**: `src/MCPhoto.App/ViewModels/CaptureViewModel.cs`, `src/MCPhoto.App/Views/CaptureView.xaml`, 테스트(T-C1~C7, T-F6)
- **선행 조건**: Step 1·4·6
- **구현 내용**: §6.1/§6.3/§6.4/§6.5 + §4.3. 정규화는 `Task.Run`(§12.1). 수신 대기 오버레이는 `Spinner.Ring` 재사용(§12.3)
- **검증 명령**: build-verify
- **완료 기준**: [관측] T-C1~C7·T-F6 통과 / [non-goal] **Enabled=off에서 외부 카메라 코드 무접촉(T-C1)** — 기존 웹캠 세션 회귀 0. 유휴 감시가 수신 대기를 중단시키지 않음 / [trigger] External 경로 진입은 ini `ExternalCameraEnabled=true`일 때만
- **롤백**: 커밋 revert(설정 UI와 독립)

### Step 8: 설정 화면 — 섹션 개편·노출 UI·게이트
- **Context Brief**: §9.1 레이아웃 + §8.3 3지점 게이트 + §10 슬라이더/입력 병행. 섹션은 "(추후 지원)" 딱지를 떼고 실배선된다.
- **대상 파일**: `src/MCPhoto.App/Views/SettingsView.xaml`(368-404 대체), `src/MCPhoto.App/ViewModels/SettingsViewModel.cs`, 테스트(T-V1~V5, T-X1)
- **선행 조건**: Step 2·3·6
- **구현 내용**: §9.1 명세 + `ExposureParameterViewModel` 3인스턴스 + W1~W3 문구(§9.4 동결)
- **검증 명령**: build-verify (headless XAML 테스트 포함)
- **완료 기준**: [관측] T-V1~V5·T-X1 통과 / [non-goal] TempUser Save가 ini 원값 보존(T-V1)·게스트 섹션 Collapsed 유지·프린터 행 Disable 유지 / [trigger] 하위 UI(모델·노출·W1) 노출은 토글 on일 때만, 설정 진입이 DSLR 연결을 유발하지 않음(§9.1)
- **롤백**: 커밋 revert

### Step 9: 카메라 테스트 모달 — 장치 목록·외부 테스트 모드 + 문서 동기화
- **Context Brief**: §9.3 개편(모달 내 장치 ComboBox, 외부 항목=정보 패널+셔터 테스트) + `ShowAsync(CameraTestTarget)` 오버로드. 마지막 단계로 docs/analysis 갱신.
- **대상 파일**: `src/MCPhoto.App/Services/ICameraTestDialogService.cs`(+구현), `ViewModels/CameraTestViewModel.cs`, `Views/CameraTestWindow.xaml`, `ViewModels/SettingsViewModel.cs`(OpenCameraTest 1곳), `docs/analysis/…`(장치·설정 절), 테스트(T-V6·V7, T-X1 확장)
- **선행 조건**: Step 6·8
- **구현 내용**: §9.3 표 전부 + W9~W14 문구
- **완료 기준**: [관측] T-V6·V7 통과, 모달에서 웹캠↔외부 전환 시 Stop→Connect 순서 준수 / [non-goal] Enabled=off일 때 모달 목록·동작이 현행과 동일(외부 항목 부재), 셔터 테스트가 파일을 저장하지 않음 / [trigger] 외부 항목은 목록 선택 시에만 연결 시도
- **검증 명령**: build-verify
- **롤백**: 커밋 revert

### Step S-A (SDK 필요): shim 실구현
- **Context Brief**: §15 C1~C4. `NikonSdkShim.cs` 신설 + SDK/래퍼 참조 + DI 1줄 교체. T-A 계약 테스트 시나리오가 구현 명세. 배선 근거는 SDK 동봉 문서만(§13.2 D5 — 리버스 엔지니어링 금지).
- **대상 파일**: `src/MCPhoto.Devices.Nikon/NikonSdkShim.cs`(신설), csproj, `ServiceRegistration.cs`(1줄), `SdkRuntimeProbe.cs`(DLL 목록)
- **선행 조건**: Step 1~9 + SDK 실물 + §13.4 P3(래퍼 채택 여부 판정)
- **검증 명령**: build-verify + §15 C5~C7 실기
- **완료 기준**: [관측] 테스트 모달 셔터 테스트 성공(실기) / [non-goal] Core·App 파일 diff 없음(경계 증명), 리포에 SDK 바이너리 미커밋(`git status`에 NikonSdk 부재 — D1) / [trigger] 실기 검증 전 배포 금지
- **롤백**: DI 1줄을 MissingShim으로 원복

### Step S-B (SDK 필요): 운영자 배치 절차 + 실기 인수 (rev2 — 번들 전제 삭제)
- **Context Brief**: 기본 아키텍처는 **미동봉**(§13.2 D2)이다. 이 단계의 산출물은 publish 번들이 아니라 **운영자 설치 안내 문서**(SDK 신청 절차 T3 → `{설치폴더}\NikonSdk\` 배치 → 테스트 모달로 확인)와 §15 C8~C10 실기 인수다. publish.ps1에 SDK 규칙을 추가하는 것은 §13.4 P1·P2가 "동봉 허용"으로 판정된 경우에만 별도 승인으로 진행.
- **대상 파일**: 운영 문서(`docs/…` 설치 안내), (조건부) `publish.ps1`
- **선행 조건**: Step S-A
- **검증 명령**: 클린 PC 수동 배치 시나리오 + §15 C8~C10
- **완료 기준**: [관측] 클린 PC에서 설치→SDK 수동 배치→연결→10컷 세션 완주 / [non-goal] SDK 미배치 PC에서 E2 강등 정상, publish 산출물에 SDK 파일 부재(D2 준수) / [trigger] 번들 전환은 P1·P2 해소 + 사용자 승인 시에만
- **롤백**: 문서 revert (코드 무접촉)

---

## §17 리스크와 명시적 비목표

### 17.1 리스크

| 리스크 | 완화 |
|---|---|
| A7(수신 시간)이 10s를 상회 → 컷 실패 오판 | 타임아웃 상수 1곳 격리(Step 1) + C5 실측 후 조정. 오판해도 강등 경로라 세션은 산다 |
| MAID가 STA/특정 스레드 요구(스레딩 모델 미검증) | shim 계약이 "호출 스레드 보장 없음"이므로, 요구가 판명되면 **shim 내부에** 전용 스레드 펌프를 넣는다 — 계약·오케스트레이션 무변경 |
| 혼합 소스 세션(§6.4)의 화질 이질감 클레임 | W6 배너 상시 고지 + 재촬영(있으면)으로 재확보 가능 |
| 24MP 정규화 비용(디코드+크롭 수백 ms)이 컷 간격 지연 체감 | 수신 대기 오버레이(W5)가 시간을 흡수. C5 실측 후 필요 시 ④ 상한 하향 |
| (rev2) 실제 약관이 제3자 사본보다 엄격(예: 단일 컴퓨터 조항이 상용 키오스크 자체를 막음) → supplementary license 협상 실패 시 기능 출하 불가 | 미동봉 기본(D2)이라 코드·배포물은 무변경으로 대응 — 최악의 경우에도 외부 카메라는 "SDK 미설치 강등" 상태의 잠재 기능으로 남고 나머지 앱 출하에는 영향 없음(§13.4 P1·P2가 게이트) |

### 17.2 명시적 비목표 (이번 이터레이션에서 하지 않는 것)

| 항목 | 왜 비목표인가 |
|---|---|
| DSLR LiveView 프리뷰 | D5300 지원 자체가 공개 근거 없음(T5·A5). 프리뷰는 웹캠 전담이 사용자 확정 요구(요구 1) |
| DSLR 동영상/타임랩스 | 상동(A6) — 타임랩스는 웹캠 전담(요구 1·2) |
| 컷 파이프라이닝(수신 중 다음 카운트다운) | §6.3 판정 — 재촬영·세션영상·취소 3중 복잡화. 실측 후 별도 이터레이션 |
| 웹캠 목록에서 D5300(UVC) 자동 배제 | A9 — 운영 수칙으로 처리 |
| 프린터(`PhotoPrinterEnabled`) 배선 | 범위 밖 — placeholder 유지 |
| 노출값 근사 매칭(입력 `1/100`→`1/125`) | §10.2 — 몰래 값을 바꾸는 동작은 금지 |
| ini에 도메인 목록 캐시 | 낡은 목록이 실기와 어긋나면 더 해롭다 — 세션 캐시만(§9.1) |

---

## §18 완결성 게이트 (developer 전달 전 자체 검사)

- [x] 검증된 사실(§1) / 미검증 가정(§2) 분리 — 가정 14건 전부 §15 체크 항목에 매핑
- [x] 모든 Step에 7필드(Context Brief/대상 파일/선행 조건/구현 내용/검증 명령/완료 기준/롤백) 기재
- [x] 완료 기준 전부 관측 기반 3문 형식(UI 단계 Step 7·8·9는 non-goal·trigger 포함)
- [x] 검증 명령 자동 실행 가능(build-verify / dotnet build·test)
- [x] 모든 View 변경에 대응 VM·연결 방식 명시(§9), 바인딩 대상 VM 멤버 누락 없음(§9.1·§9.3·§10.2)
- [x] 이벤트 구독마다 해제 경로 명시(§12.2)
- [x] UI/백그라운드 경계·동기화 전략 명시(§12.1)
- [x] 리소스 키 신설 0 — 충돌 없음(§12.3)
- [x] 전역 예외·강등 경로 전수표(§11) — 크래시·무한 대기 경로 없음
- [x] ViewModel은 UI 타입 무의존 — Core 신규 타입 전부 POCO(§3.2)
- [x] 파일 인코딩 규칙 명시(§12.4)

**미해결 확인 사항**:
- **USER-DECISION (§8.2)**: "User 역할 이상 사용 가능"을 편집 게이트로 해석했다(런타임 동작은 ini 기준, 게스트 세션에도 적용). TempUser 세션에서 DSLR **동작 자체**를 막아야 한다면 §6.1에 역할 조건 1줄 추가로 대응 가능 — Step 7 착수 전 확인 요망.
- **USER-ACTION (§13.4 P1)**: SDK 신청 시 라이선스 동의 화면 원문을 보존(스크린샷/사본)해 T6 제3자 사본과 대조 — A14 종결 조건.
- **USER-ACTION (§13.4 P2)**: 키오스크 다중 PC 상용 운영의 supplementary license 필요 여부 Nikon 문의 — **상용 출하 게이트**(개발은 막지 않음).
