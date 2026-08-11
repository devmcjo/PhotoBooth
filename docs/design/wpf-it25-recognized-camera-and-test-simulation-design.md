# it25 설계 — 프린터 "추후 제공" 환원 · 인식된 카메라 목록 · TestMode 외부 카메라 시뮬레이션

> 작성: wpf-architect · 2026-08-11
> 파이프라인: wpf-architect → wpf-developer → wpf-code-reviewer
> 상태: 설계 초안 (실물 DSLR·Nikon SDK **없이** 작성 — it23·it24와 동일 조건. 이번 요구의 핵심이 바로 "장비 없이 UI를 확인하는 수단"이다)
> 선행 문서: `wpf-it24-external-device-discovery-design.md`(상태 전수표 S0~S7·R1~R5·프린터 판정), `wpf-it23-external-camera-nikon-design.md`(rev2 — 3층 경계·레지스트리·강등), `wpf-it23-session-testmode-license-design.md` B부(`[Test]` 규격·참조 동일성 봉인·TM1~TM5)

## §0 개요

### 0.1 요구사항 원문 (사용자 피드백, 축약 금지)

> "외부 장치 연결 시 프린터는 추후 제공으로 남겨놔줘. 아직 지원되는 항목이 하나도 없으니까.
> 그리고 외부 카메라 같은 설정들도 TestMode 에서는 있는 것 처럼 확인 되도록 키를 하나 만들어줘. ExternelCamera=0(TRUE/FALSE 기본값 0) ExtermelCameraType=-1(없음, 0-니콘D5300, 1-... enum 형태로 제공)
> 연결된 외부 카메라가 만약 없다면 해당 버튼을 켜고 콤보박스도 -선택안함- 이 기본값으로 노출되고, 선택하려고 콤보박스를 열어도 선택안함만 존재하면 될 것 같아. 그리고 메시지 문구를 연결이 인식된 카메라만 노출된다고 표시해주면 될듯. 그리고, 별도 notify 창을 만들어서 해당 버튼을 누르면 지원하는 카메라 목록을 제조사, 제품명 별로 정리해주면 좋을 것 같아."

**팀리드 확정 사항**: 요구 원문의 키 이름 `Externel`/`Extermel`은 `External`의 오타다(두 곳 표기가 서로 다른 것이 그 증거). ini 키는 배포되면 되돌리기 어려운 계약이므로 **`ExternalCamera` · `ExternalCameraType`**로 바로잡아 설계한다.

요구 분해:

1. **A. 프린터 환원**: it24가 연 프린터 열거·선택·저장 표면을 "추후 제공" placeholder로 되돌린다.
2. **B. `[Test]` 신규 키**: `ExternalCamera`(bool, 기본 0) · `ExternalCameraType`(enum int, 기본 -1) — 장비 없이 외부 카메라 UI를 "있는 것처럼" 확인.
3. **C. 콤보 개념 전환**: 모델 콤보를 "지원 모델 목록"에서 **"연결이 인식된 카메라 목록"**으로 — 인식 0이면 `- 선택안함 -`만, 문구로 그 의미를 고지.
4. **D. 지원 카메라 목록 창**: 제조사·제품명별 정리된 별도 표면(버튼 진입).

### 0.2 이 설계의 최우선 제약 — 시뮬레이션이 진짜처럼 동작하면 안 된다

가장 위험한 실패 모드는 기능 미달이 아니라 **과잉**이다: TestMode의 가짜 카메라가 촬영 경로에 들어가 가짜 사진을 만들어내는 것.

- 가짜 스틸이 `CapturedStill`로 하류(컷선택·필터·합성·QR 업로드)에 흘러가면 그 산출물은 **거짓**이고, QR로 외부에 유출될 수도 있다.
- 실기(SDK+D5300) 연동 시점에 "TestMode에서는 되던 촬영이 안 된다"는 오인 보고를 만든다 — 시뮬레이션과 실경로의 경계가 흐리면 회귀 판단 자체가 불가능해진다.
- it23·it24가 세운 원칙("거짓말 금지" R1~R5, 상태 전수표)은 화면이 **관측하지 않은 것을 말하지 않게** 하는 장치다. 시뮬레이션이 관측을 위조하면서 그 표식이 없으면 원칙 전체가 무효가 된다.

**판정: TestMode 외부 카메라 시뮬레이션은 `설정 화면의 검색·표시 표면만` 대체한다. 촬영·카메라 테스트 모달·업로드는 실경로 그대로이며, 그 경로들은 SDK 부재 사유(W7·W10)와 함께 정직하게 실패한다.** 경계는 판정 단일 지점 + 불변식(TS1~TS4, §3.2)으로 봉인하고, 봉인 자체를 테스트로 잠근다(§12). 근거와 구조는 §5.5.

> **기각된 대안 — 전체 시뮬레이션**(DI에서 `IExternalCamera`를 가짜 구현으로 교체하고 `CaptureAsync`가 합성 JPEG를 반환): "연결만 성공하고 캡처는 실패하는 반쪽 시뮬은 강등 UX만 반복 재생하고 강등 경로를 정상인 양 학습시킨다"는 논거가 제시됐으나 **기각**한다. 결정적 이유는 두 가지다. ① it23 B부 §B8.5는 **테스트 ini를 켠 채 실계정으로 로그인해 일하는 것을 허용**한다 — 그 상태에서 합성 JPEG가 합성·QR·업로드 실경로를 주행하면 **가짜 사진이 실제 서버에 올라가고 실제 QR 정원을 소비**한다. 배너 표기는 산출물 자체를 구분해 주지 못한다. ② 반쪽 시뮬의 부작용은 과장이다 — 촬영 진입 시 강등은 **세션당 1회 토스트**이며(§5.5), 컷마다 반복되지 않는다. 노출 슬라이더를 장비 없이 주행하려는 목적은 it23 §10.3이 이미 허용한 **자유 입력 폴백**(도메인 미확보 시 문자열 직접 입력 → 실연결 시 검증)이 담당한다.

### 0.3 판정 요약

| 쟁점 | 판정 | 왜 |
|---|---|---|
| A. 프린터 표면 | 하위 패널(고지·콤보·다시 검색·상태 문구) **제거**, 토글 `IsEnabled="False"` + "추후 지원 예정" 캡션으로 환원 (§4) | 사용자 지시 원문("추후 제공으로 남겨놔줘"). it24 판정 (b)는 본 문서가 **대체**한다(it24 §7 → it25 §4) |
| A-2. 열거자 코드 | `IPrinterEnumerator`/`SystemPrinterEnumerator`는 **삭제하지 않고 의도된 스캐폴드로 보존**(DI 등록 포함, 프로덕션 소비자 0) (§4.2) | `IPhotoPrinter`/`NullPhotoPrinter`·구 `IExternalCamera`와 동일한 리포 관례. it24 Step 5가 해소한 실측(U3·U4·U7)을 버리면 인쇄 이터레이션에서 재검증 비용을 다시 치른다 |
| A-3. ini 2키 | `PhotoPrinterEnabled`·`PhotoPrinterName` **유지(라운드트립 보존, VM 미기록)** (§4.3) | 키 삭제는 기존 ini 값을 첫 저장에서 지운다 — 클로버 금지 원칙. inert한 키는 무해하다 |
| B. `[Test]` 2키 | `ExternalCamera`(bool, 0) · `ExternalCameraType`(int enum, -1). 기존 `[Test]` 규약(폴백+Warning+배너) 그대로 승계 (§5) | 사용자 요구. B부(it23) 키 전수표의 확장이며 새 메커니즘이 아니다 |
| B-2. int↔모델 매핑 | 레지스트리 배열 인덱스 금지 — **레지스트리 행에 명시 `TestTypeCode` 필드**를 박는다 (§5.2) | 인덱스는 행 순서 변경에 조용히 깨진다(it7 B9 `SelectedIndex` 사고와 동형). 코드가 행 안에 있으면 "모델 추가 = 표 한 줄" 규약이 매핑까지 포괄한다 |
| B-3. 봉인 | 시뮬레이션 분기는 **SettingsViewModel 검색 시퀀스의 관측 채취 지점 1곳** + `IsTestUser`(참조 동일성) 게이트. `IExternalCamera`·shim·촬영·모달에 주입 금지 (§5.5) | TM3(참조 동일성 봉인)과 같은 급의 규약. DI 데코레이션은 촬영 경로까지 오염시킨다 |
| C. "인식됨"의 정의 | **SDK 연결 확인(S6)만** 인식으로 콤보에 올린다. WMI 후보(S3·S5)는 검색 결과 라인(W20)으로만 (§6.1) | WMI 매칭은 이름 우연에 기대는 best-effort고, 콤보는 저장으로 이어지는 조작 표면이다 — 제어 불가 항목을 올리면 "선택했는데 안 된다"를 사용자가 만들 수 있다 |
| C-2. 저장값 보존 | 콤보 선택을 ini `ExternalCameraModel`과 **분리**(별도 VM 속성). `- 선택안함 -`은 저장을 건드리지 않는다 (§6.3) | it24 P5와 같은 원칙 — 인식 목록이 비었다고 운영자가 맞춰 둔 값을 지우면 안 된다. WPF 콤보의 SelectedValue null 되쓰기 함정 회피 |
| D. 창 형태 | 별도 `Window` 금지 — **SettingsView 안 오버레이**(라이선스 고지와 동형) (§7.1) | headless 테스트에서 `Window` 인스턴스화 불가(B-T9 함정) — XAML 회귀를 잡을 수 있는 유일한 형태 |
| D-2. 레지스트리 스키마 | `ExternalCameraModel`을 (Id, **Manufacturer, ModelName**, Md3FileName, **TestTypeCode**)로 확장. `DisplayName`은 파생 속성으로 유지 (§7.2) | 제조사·제품명별 정리 요구 + B-2 매핑을 한 번의 스키마 변경으로. DisplayName 소비자(콤보·키워드 유도) 무영향 |

---

## §1 검증된 사실 (verified facts — 전부 코드 직접 확인, 2026-08-11)

| # | 사실 | 근거 |
|---|---|---|
| F1 | it24는 **구현 완료 상태**다: `[장치 검색]`(S0~S7)·프린터 열거(P1~P5)·게스트 가시성·`IsExternalEditDenied` 전부 실배선 + 테스트 존재 | `SettingsViewModel.cs:588-881`, `SettingsView.xaml:508-733`, `tests/MCPhoto.Tests/SettingsViewModelDiscoveryTests.cs` |
| F2 | 프린터 표면의 실체: VM 멤버 9종(`PhotoPrinterName`·`IsEnumeratingPrinters`·`PrinterStateText`·`HasPrinterStateText`·`HasPrinters`·`PrinterOptions`·`PrinterEnumerationTask`·`RefreshPrintersCommand`·`OnPhotoPrinterEnabledChanged` 훅) + XAML 하위 패널(`SettingsView.xaml:692-732`) + `OnEnterAsync`의 조건 열거(`:260`) + Save 2키 기록(`:506-507`) | 각 파일 실측 |
| F3 | `IPrinterEnumerator`(Core)·`SystemPrinterEnumerator`(App)는 예외를 던지지 않는 계약으로 완성돼 있고 DI Singleton 등록(`ServiceRegistration.cs:98`). it24 Step 5의 실측(U3 자동 참조 성립·U4 스풀러 중지 강등·U7 기본 프린터 null 가드)이 코드·테스트에 반영됨 | `IPrinterEnumerator.cs`, `SystemPrinterEnumerator.cs`, `SettingsViewModelDiscoveryTests.cs:695`(Real_Printer_Enumeration_Never_Throws) |
| F4 | `AppSettings`에 `PhotoPrinterEnabled`·`PhotoPrinterName` 필드 + Clamp(Trim) + Clone 복사 + ini 라운드트립이 있다 | `AppSettings.cs:162,172,240,341,343` |
| F5 | `[Test]` 인프라: `TestModeOptions.FromIni`(순수, Warnings 반환)·`TestModeService`(1회 캐시)·`ITestModeService.IsTestUser`(참조 동일성). 기존 키 7종(TestMode·Id·Email·Role·Pin·QrBlocked·QrBlockReason), 폴백 규약 = 기본값 + Warning | `TestModeOptions.cs`, `TestModeService.cs`, `ITestModeService.cs` |
| F6 | TM3 규약이 계약 주석에 명문화돼 있다: "모든 테스트 모드 분기는 `IsTestUser`(참조 동일성)를 통과해야 한다. `IsEnabled`는 배너 표시와 DI 등록에만" — DI 등록의 유일한 `IsEnabled` 분기는 `TestModeQrUsageService` 데코레이터 | `ITestModeService.cs:13-16`, `ServiceRegistration.cs:127-134` |
| F7 | 모델 레지스트리는 D5300 1행 `(Id="NikonD5300", DisplayName="Nikon D5300", Md3FileName="Type0011.md3")`. `Find`(미지 null)·`Resolve`(미지→Default) 분리. `Default = All[0]` — **행 순서가 기본 모델을 정한다**(순서 민감성이 이미 주석으로 경고됨) | `ExternalCameraModels.cs:37-43,63` |
| F8 | 촬영 경로의 외부 카메라 게이트는 ini `ExternalCameraEnabled` 1곳(`CaptureViewModel.OnEnterAsync:138`) → `ResolveExternalSourceAsync`가 실 `ConnectAsync`+capability로 소스 확정, 실패 시 W7 토스트 + 웹캠 강등. **off면 외부 카메라를 한 번도 접촉하지 않는다** | `CaptureViewModel.cs:28,134-139,209-230` |
| F9 | 현 프로덕션 shim은 `MissingNikonSdkShim`(`IsOperational=false`) — `CheckReadiness`가 W10으로 강등하므로 [장치 검색]의 실경로 도달점은 항상 S2다. `NikonSdkShim.cs` 파일 부재가 정상(SDK 도착 신호) | `MissingNikonSdkShim.cs:29`, `NikonExternalCamera.cs:102-113` |
| F10 | 검색 시퀀스의 관측은 이미 주입 가능하다: `probePortableDevices`(Func, ctor 선택 인자)·`IExternalCamera`(fake). 문구 조립은 `ApplyDiscoveryResult` 1곳 | `SettingsViewModel.cs:210,223-224,690-740` |
| F11 | 모델 콤보는 `ExternalCameraModelOptions`(레지스트리 전체) + `SelectedValue=ExternalCameraModel`(값 기반) — **지원 목록이 그대로 콤보**다. W24 캡션("연결된 장치 목록이 아닙니다")이 그 의미를 고지 | `SettingsViewModel.cs:144`, `SettingsView.xaml:558-572` |
| F12 | USB 관측 키워드는 `DisplayName.Split(' ')`로 유도 — DisplayName 형태("제조사 모델명")에 의존한다 | `SettingsViewModel.cs:683-685` |
| F13 | 라이선스 고지가 "별도 Window 금지 + 같은 화면 오버레이" 선례이고, 진단·라이선스·설정의 닫기/복귀 버튼은 **하단 중앙 정렬 액션 바 + `Margin="8,0"`** 관례. 버튼 라벨에 화살표·글리프 금지(라이선스 화면에서 같은 이유로 수정한 이력) | `SettingsView.xaml`(라이선스 구역), agent-memory |
| F14 | headless 테스트는 `Window`를 인스턴스화할 수 없다(B-T9 함정) — `XamlResourceTests`가 UserControl 수준 XAML 로드로 회귀를 잡는다 | `wpf-it23-session-testmode-license-design.md` B4.3, `XamlResourceTests.cs` |
| F15 | `IniFile.GetInt(section, key, fallback)` 존재 — `[Test]` int 키 파싱에 파서 변경 불요 | `IniFile.cs:100` |
| F16 | .cs는 UTF-8 no BOM(한글 주석 포함), XAML·문서는 기존 인코딩 유지 | agent-memory `source-file-encoding` |

---

## §2 ⚠️ 미검증 가정 (open assumptions)

이번 이터레이션은 **장비·SDK 없이 전 단계 완료 가능**하도록 설계했다(그것이 요구의 목적이다). it24의 U1·U2(D5300의 WMI 관찰 여부·이름)·U6(SDK 정상 상태의 실거동)은 **그대로 미해소 승계**되며, 본 설계는 어느 것에도 의존하지 않는다.

| # | 미검증 가정 | 거짓이면 생기는 일 | 설계상 처리 | 검증 방법 |
|---|---|---|---|---|
| V1 | 실 S6(SDK+실기 연결 확인)에서 인식 콤보가 실측 기대대로 채워진다 | 콤보가 안 채워짐(표시 결함) — 저장값·촬영은 무영향 | S6 배선은 fake로 전수 검증(T-C 계열). 실기 확인은 it24 Step 9(실물 필요 단계)에 편입 | SDK+D5300 도착 후 실기 [장치 검색] |
| V2 | `ExternalCameraModel` record 스키마 확장(위치 인자 5개)의 소비자가 레지스트리 초기화·테스트뿐이다 | 빌드 에러로 즉시 표면화 — 조용한 오동작 없음 | 위치 인자 생성 지점 전수 grep을 Step 2 완료 기준에 포함 | Step 2 `dotnet build` |

**가정 매핑 완결성**: V1은 실기 단계로 격리, V2는 Step 2 빌드가 판정. WBS 어느 단계도 U1·U2·U6·V1에 의존하지 않는다.

---

## §3 원칙 — it23·it24 원칙의 승계와 시뮬레이션 불변식

### 3.1 승계 확인 (이번 변경이 R1~R5를 깨는지 전수 점검)

| 원칙(it24 §3) | 이번 변경에서의 지위 |
|---|---|
| R1 "없습니다"는 판정 능력이 있을 때만 | **유지.** S0~S7 판정(`ExternalDiscoveryJudge`)·문구는 무변경. 시뮬레이션은 관측 입력을 대체할 뿐 판정 규칙을 우회하지 않는다(§5.5 — 같은 Judge를 통과). 인식 콤보의 빈 상태 문구(W33)는 "표시 범위"를 말하지 "장치 부재"를 단정하지 않는다 |
| R2 부재 단정은 완화형 | **유지.** W19 무변경. 시뮬레이션 Type=-1은 S4(완화형)로 표현된다(§5.4) |
| R3 USB 관측은 양성 신호 전용 | **유지.** 시뮬레이션은 WMI 프로브를 아예 호출하지 않는다(관측 위조가 아니라 관측 생략 + 대체 입력, W38로 명시) |
| R4 프린터 "없습니다" ≠ "확인 불가" | **해당 표면 폐기**(A부). 계약(`PrinterEnumerationResult`)에는 구분이 타입으로 남아 스캐폴드째 보존된다 — 인쇄 이터레이션이 재사용 |
| R5 지원 모델과 연결된 장치를 한 목록에 섞지 않는다 | **강화.** 콤보가 "인식된 카메라" 단일 개념이 되고, 지원 목록은 별도 오버레이(D부)로 완전히 분리된다. 단 W24 문구 자체는 콤보 의미 변경으로 거짓이 되므로 폐기·대체(§8.3) |

### 3.2 신규 불변식 TS1~TS4 — 시뮬레이션 봉인 (TM 계열과 같은 급)

| # | 불변식 | 위반 예시 |
|---|---|---|
| **TS1** | 시뮬레이션 분기는 **`SettingsViewModel`의 검색 시퀀스(관측 채취 지점) 단 한 곳**에만 존재한다. `IExternalCamera`·`INikonSdkShim`·`CaptureViewModel`·`CameraTestViewModel`·DI 등록 어디에도 시뮬레이션 구현을 주입·데코레이션하지 않는다 | `IExternalCamera`를 감싸는 `TestModeExternalCamera` 데코레이터 — `ConnectAsync`는 촬영도 쓰는 멤버라 촬영 경로가 오염된다 |
| **TS2** | 시뮬레이션 적용 조건은 `ITestModeService.IsTestUser(현재 세션 사용자)`를 **반드시 통과**한다(TM3 준수 — `IsEnabled` 단독 분기 금지) | `if (_testMode?.IsEnabled == true)` 분기 — 테스트 ini가 켜진 채 **실계정으로 로그인한 운영자**가 가짜 "연결 확인됨"을 보고 실장비 진단을 그르친다 |
| **TS3** | 시뮬레이션은 화면 표시(검색 헤드라인·상세 라인·인식 콤보)만 바꾸고, **ini `[MCPhoto]` 어느 키에도 자동 기록되지 않는다**. QA가 인식 콤보에서 명시 선택 후 [저장]하는 것은 일반 편집 규칙(레지스트리 Id 기록)이며 예외가 아니다 | 시뮬레이션 활성 시 `ExternalCameraModel`을 매핑 모델로 자동 세팅 |
| **TS4** | 시뮬레이션이 만든 검색 결과에는 **시뮬레이션 명시 라인(W38)이 항상 포함**된다 | S6 헤드라인만 표시 — 스크린샷 단위에서 실관측과 구분 불가 |

봉인 구조가 TM3(B부 §B8.3)과 동형인 이유: "ini가 켜져 있다"(`IsEnabled`)와 "이 세션이 그 가짜 계정이다"(`IsTestUser`)는 다른 명제고, 시뮬레이션은 후자에만 걸어야 실계정 병행 사용(B8.5에서 허용한 워크플로)이 안전하다. 촬영 경로(F8)는 `[Test]` 키를 아예 읽지 않으므로 — 코드 그래프상 도달 경로가 없으므로 — TS1이 지켜지는 한 "시뮬레이션이 촬영으로 샌다"는 상태는 표현 자체가 불가능하다.

---

## §4 A. 프린터 — "추후 제공"으로 환원

> **대체 선언**: it24 §7의 판정 (b)(열거+선택+저장)는 **본 절이 대체**한다. it24 문서는 폐기 관례(이력 보존, 소급 편집 금지)에 따라 수정하지 않는다 — 당시 판정은 당시 요구("찾아보고 나열")에 대해 유효했고, 이번 사용자 지시("아직 지원되는 항목이 하나도 없으니까 추후 제공으로")가 그 요구를 명시적으로 철회했다.

### 4.1 설정 표면 — it23 형태로 환원

| 요소 | it24(현행) | it25(환원) |
|---|---|---|
| 프린터 토글 | `IsEnabled="{Binding CanEditExternalCamera}"` + 게이트 노티 2종 | **`IsEnabled="False"`** + 캡션 W32 `추후 지원 예정`. 게이트 노티(GuestGateNote·권한 없음)는 **제거** — 아무도 편집할 수 없는 컨트롤에 "로그인하면 되는가"를 암시하는 노티는 거짓 안내다 |
| 토글 표시값 | ini 원값 | ini 원값 **유지**(Load만, 표시 정직 — 강제 off 표시 금지 원칙 그대로) |
| 하위 패널(W25 고지·프린터 콤보·[다시 검색]·상태 문구) | 토글 on일 때 노출 | **전부 제거** |
| `OnEnterAsync`의 조건 열거(`if (PhotoPrinterEnabled) RefreshPrintersAsync()`) | 있음 | **제거** — 설정 진입이 스풀러를 접촉할 이유가 사라진다 |
| Save 기록(`s.PhotoPrinterEnabled`·`s.PhotoPrinterName`) | `CanEditExternalCamera` 게이트로 기록 | **2줄 모두 제거(미기록)** — VM이 안 건드리면 Clone 원값이 그대로 재기록되어 보존이 자동 성립 |
| VM 멤버 9종(F2) | 있음 | `PhotoPrinterEnabled`(토글 표시용)만 남기고 **8종 + `PrinterOptionItem` record + 프린터 문구 상수 6종(W26~W31 대응) 제거**. ctor의 `IPrinterEnumerator? printers` 인자도 제거(소비자 0) |

### 4.2 열거자 코드의 운명 — 삭제가 아니라 의도된 스캐폴드

| 선택지 | 판정 | 왜 |
|---|---|---|
| 삭제 | 기각 | it24 Step 5가 장비 없이 해소한 실측 3건(U3: `System.Printing` 자동 참조 성립, U4: 스풀러 중지 시 P4 강등, U7: 기본 프린터 null 가드)이 코드·테스트에 굳어 있다. 지우면 인쇄 이터레이션(it24 §7.1 (c) — `IPhotoPrinter.PrintAsync` 배선)에서 같은 검증을 다시 치른다 |
| **스캐폴드 보존** | **채택** | 이 리포의 확립된 관례: `IPhotoPrinter`/`NullPhotoPrinter`(item3)와 it23 이전의 `IExternalCamera`/`NullExternalCamera`가 전부 "프로덕션 소비자 0인 스캐폴드 + DI 등록"으로 존재했고, 실배선 시점에 그대로 소비됐다 |

**"죽은 코드가 아니라 의도된 스캐폴드"임을 표시하는 방법** (셋 다 필수):

1. `IPrinterEnumerator.cs`·`SystemPrinterEnumerator.cs` 클래스 주석 선두에 명기: `"it25 A부: 프린터 표면 환원으로 프로덕션 소비자 0 — 의도된 스캐폴드다(IPhotoPrinter와 동일 지위). 인쇄 기능 이터레이션이 재배선한다. 삭제 금지."`
2. `ServiceRegistration.cs`의 등록 1줄은 **유지**하되 주석을 "소비자 0 스캐폴드(it25)" 취지로 갱신 — 상태 없는 Singleton이라 등록 자체는 무해하고(호출 없으면 스풀러 무접촉), 등록을 지우면 재배선 시 U3류 배선 실수가 재발할 표면이 늘어난다.
3. 계약 수준 테스트는 존치: `Real_Printer_Enumeration_Never_Throws`·열거자 단위 테스트는 스캐폴드의 "예외를 던지지 않는다" 계약을 계속 잠근다(§12.4).

재개 조건은 §4.4에 명문화한다.

### 4.3 ini 2키의 운명 — 유지(라운드트립 보존)

| 키 | 판정 | 왜 |
|---|---|---|
| `PhotoPrinterEnabled` | **유지.** 의미 서술을 "추후 인쇄 기능용 예약 플래그(현재 런타임 효과 없음, UI 편집 불가)"로 되돌린다 | it23 이전부터 있던 키. 삭제하면 직렬화 diff가 생길 뿐 이득이 없다 |
| `PhotoPrinterName` | **유지(잔존 키로 강등).** `AppSettings`·`IniSettingsService`·Clamp·Clone 전부 무변경 — 값이 있으면 보존, UI에서 편집·표시하지 않는다 | it24 배포본(1.1.16 계열)이 이미 이 키를 쓸 수 있다. `WriteFrom`에서 키를 빼면 기존 ini의 값이 **첫 저장에서 소멸**한다(외래 섹션 보존 B4와 같은 계열의 함정 — `[MCPhoto]` 소유 키는 매핑에서 빠지는 순간 지워진다). 클로버 금지 |

파급 없음 확인: `AppSettings`/`IniSettingsService`/`Clamp`/`Clone`은 **한 줄도 바꾸지 않는다.** 변경은 VM(Load에서 `PhotoPrinterName` 제거·Save 2줄 제거)과 XAML뿐이다. 기존 라운드트립 테스트(T-S4 계열)는 그대로 유효하다.

### 4.4 재개 조건 (스캐폴드가 존재하는 이유의 명문화)

인쇄 기능은 **별도 이터레이션에서 재개**하며, 그때 it24 §7의 열거·선택·저장 판정(P1~P5 상태표·W25~W31 문구)을 되살리는 것이 출발점이다. §4.2의 스캐폴드(계약·구현·DI 등록·계약 테스트)와 §4.3의 잔존 ini 키는 전부 **그 재개 비용을 최소화하기 위해 남긴 것**이다 — 이 코드·키를 죽은 코드로 오해해 지우면 재개 시점에 it24 Step 5의 실측(U3·U4·U7)부터 다시 치른다. 재개의 실질 조건은 "지원(실인쇄 가능) 프린터 구성 ≥ 1"이며, 그때 외부 카메라와 같은 "지원·인식" 구분 원칙을 프린터에도 적용할지가 첫 판정 대상이다.

---

## §5 B. `[Test]` 섹션 신규 키 — 외부 카메라 시뮬레이션

### 5.1 키 전수표 (it23 B부 §B3.1에 이어서 — 처리 규약 동일)

| 키 | 타입 | 기본값 | 검증 | 잘못된 값의 처리 | 왜 필요한가 |
|---|---|---|---|---|---|
| `ExternalCamera` | bool | `0`(false) | `IniFile.GetBool`(`true/1/on/yes` ↔ `false/0/off/no`) | 인식 불가 → **false**(안전측 — 시뮬레이션 꺼짐) | 마스터 스위치. 켜지면 [장치 검색]의 관측이 시뮬레이션 입력으로 대체된다(§5.4). **`TestMode=1`일 때만 의미** — TestMode가 꺼져 있으면 `FromIni`가 `Disabled`를 돌려주므로 이 키는 해석조차 되지 않는다(기존 규약) |
| `ExternalCameraType` | enum(int) | `-1`(없음) | `IniFile.GetInt` 후 **명시 매핑 표**(§5.2) 대조: `-1` 또는 레지스트리에 존재하는 `TestTypeCode` | 파싱 실패·목록 밖(`-2`, `99`) → **`-1` + Warning**("[Test] ExternalCameraType 값을 알 수 없습니다(…) — -1(없음)로 실행합니다") | 어느 모델이 인식된 것으로 시뮬레이션할지. `0` = Nikon D5300. 이후 모델 추가 시 코드가 늘어난다(§5.2 확장 규약) |

- `ExternalCamera=0` + `ExternalCameraType=0` 조합: Type은 `ExternalCamera=1`일 때만 의미다 — 경고 없이 무시한다(`QrBlockReason`이 `QrBlocked=1`일 때만 의미인 것과 동일 규약).
- 경고는 기존 규약대로 `TestModeOptions.Warnings`에 담기고 `TestModeService`가 Warning 로그로 흘린다. 배너(TM4)가 이미 화면 표식을 담당한다.

### 5.2 int ↔ 모델 매핑 안정화 — 배열 인덱스 금지

레지스트리 배열 인덱스를 그대로 쓰면 행 순서가 바뀌는 순간 ini에 적힌 숫자의 의미가 조용히 달라진다 — it7 B9(`SelectedIndex` 클로버)와 동형의 사고이며, ini 키는 배포 후 계약이라 더 치명적이다.

**판정: 매핑 코드를 레지스트리 행 자체에 박는다** — `ExternalCameraModel`에 `TestTypeCode` 필드 신설(§7.2의 스키마 확장과 한 번에):

```csharp
// MCPhoto.Core/Devices/ExternalCameraModels.cs — 확장 후의 표(1행)
new ExternalCameraModel(
    Id: "NikonD5300", Manufacturer: "Nikon", ModelName: "D5300",
    Md3FileName: "Type0011.md3", TestTypeCode: 0),

/// <summary>[Test] ExternalCameraType 값 조회. -1·미지 코드는 null(없음) — 보정하지 않는다(Find와 동일 철학).</summary>
public static ExternalCameraModel? FindByTestType(int code) => ...;  // code < 0 이면 즉시 null
```

- **별도 매핑 표(딕셔너리)를 두지 않는 이유**: 표가 둘이면 모델 추가 시 한쪽을 잊는 실수가 구조적으로 가능해진다. 코드가 행 안에 있으면 **"모델 추가 = 표 한 줄 + 법적 절차 1건"**(it23 §3.3 rev2) 규약이 매핑까지 자동 포괄한다.
- **확장 규약**: `TestTypeCode`는 `Id`와 같은 지위다 — **한 번 배정하면 변경·재사용 금지**(ini에 적힌 숫자가 계약). 새 모델은 다음 미사용 코드를 배정한다. 행 순서와 무관하므로 정렬·재배치가 자유롭다.
- **회귀 잠금**: 전 행 `TestTypeCode` 유일 + `>= 0` + `FindByTestType(-1) == null`을 테스트로 고정(T-B3). 중복 코드가 컴파일을 통과해도 테스트가 즉시 잡는다.

### 5.3 운영 설정과의 구분 — 어느 스위치를 봐야 하는가

| | `[MCPhoto] ExternalCameraEnabled` (운영) | `[Test] ExternalCamera` (시뮬레이션) |
|---|---|---|
| 의미 | 촬영 세션이 DSLR 스틸 경로를 **시도**할지 | 설정 화면 [장치 검색]의 **관측 표시를 대체**할지 |
| 읽는 곳 | `CaptureViewModel`(세션 소스 확정)·테스트 모달 항목 노출·설정 하위 패널 노출 (F8) | `SettingsViewModel` 검색 시퀀스 **1곳**(TS1) |
| 영향 범위 | 실동작(연결 시도·강등·배너) — 게스트 세션에도 적용 | 화면 표시(헤드라인·상세·인식 콤보)만. 촬영·모달·업로드 무접촉 |
| 편집 주체·방식 | 설정 화면 토글(`CanEditExternalCamera` 게이트) — 앱이 저장 | ini 직접 편집만(TM5: `[Test]`는 앱이 쓰지 않는다) |
| 켜져 있을 때 화면 표식 | 없음(정상 운영 상태) | 테스트 모드 배너(TM4) + 검색 결과의 W38 라인(TS4) |
| **운영자는** | **이것만 본다** | 실운영 ini에 존재하면 안 된다(M8 배포 체크리스트 대상) |

두 키는 독립이다. 시뮬레이션으로 인식 콤보를 채우려면 설정 화면에서 하위 패널이 보여야 하므로 `ExternalCameraEnabled=on`(토글 켜기)이 함께 필요하지만, 이는 표시 전제조건이지 의미 결합이 아니다 — 토글을 켠 채 저장하면 촬영 세션이 실 연결을 시도하고 **정직하게 W7 강등**한다(§5.5).

### 5.4 시뮬레이션 시나리오 — 순수 함수로 관측 입력을 생성

시뮬레이션은 관측 결과를 **위조해 끼워 넣는 것이 아니라**, 관측 채취 단계를 통째로 대체할 입력을 순수 함수로 만들어 **기존 판정·문구 파이프라인(Judge → ApplyDiscoveryResult)에 그대로 태운다.** 문구 조립 지점이 하나로 유지되므로(F10) 시뮬레이션 상태와 실관측 상태의 화면 표현이 어긋날 수 없다.

```csharp
// MCPhoto.Core/Devices/ExternalCameraSimulation.cs (신규 — 순수, I/O 없음)
/// <summary>[Test] 외부 카메라 시뮬레이션이 만들어 낼 관측 입력 1세트. (it25 §5.4)</summary>
public sealed record ExternalDiscoverySimPlan(
    ExternalCameraReadiness Readiness,   // 항상 (true, null) — "스택 정상" 시나리오만 시뮬레이션한다
    bool Connected,                      // Type 매핑 성공 여부
    ExternalCameraModel? Model);         // 인식된 것으로 표시할 모델(없으면 null)

public static class ExternalCameraSimulation
{
    /// <summary>시뮬레이션 계획. null = 시뮬레이션 없음(실관측 수행). Enabled·ExternalCamera가 모두 참일 때만 계획을 만든다.</summary>
    public static ExternalDiscoverySimPlan? Plan(TestModeOptions options)
    {
        if (options is null || !options.Enabled || !options.ExternalCamera) return null;
        var model = ExternalCameraModels.FindByTestType(options.ExternalCameraType);
        return model is null
            ? new(new ExternalCameraReadiness(true, null), Connected: false, Model: null)   // → Judge: S4
            : new(new ExternalCameraReadiness(true, null), Connected: true,  Model: model); // → Judge: S6
    }
}
```

| ini 조합(`TestMode=1` 전제) | 계획 | Judge 결과 | 화면 |
|---|---|---|---|
| `ExternalCamera=0`(기본) | null — **실관측** | 실경로(현 프로덕션이면 S2) | 현행과 동일 |
| `ExternalCamera=1` + `Type=-1` | (true,null) / false / null | **S4** | W19 "연결 가능한 장치를 찾지 못했습니다…" + W38. 인식 콤보 = `- 선택안함 -`만 — **사용자가 말한 "카메라 없음" 상태의 재현 수단**이자, SDK 없이는 도달 불가능했던 W19 문구의 QA 수단 |
| `ExternalCamera=1` + `Type=0` | (true,null) / true / D5300 행 | **S6** | W21 "Nikon D5300 — 연결 확인됨" + W21a + W38. 인식 콤보 = `- 선택안함 -` + `Nikon D5300` |

- `ExternalCamera=1` + `Type=-1`은 모순이 아니라 **정의된 조합**이다: "시뮬레이션은 켰지만 인식된 장치는 없음" — 빈 인식 상태의 UI(콤보 sentinel 단독·문구)를 결정론적으로 확인한다.
- 시나리오가 S4·S6 둘뿐인 이유: S2(스택 미비)·S3/S5(WMI 감지)는 **현 프로덕션 실경로가 이미 도달**하므로 시뮬레이션할 가치가 없다. 시뮬레이션은 장비·SDK 없이는 볼 수 없는 상태만 공급한다.
- 배터리는 **표시하지 않는다**(null 고정): 잔량 숫자는 이 요구(콤보·토글·문구 확인)에 불요하며, 구체 수치의 날조는 최소 시뮬레이션 원칙에 어긋난다. W21b 라인의 QA는 fake `IExternalCamera` 단위 테스트(기존 T-D1 계열)가 담당한다.

### 5.5 봉인 — 시뮬레이션이 촬영으로 새지 않는 구조

**판정 단일 지점**: `SettingsViewModel.DiscoverExternalCameraAsync`의 관측 채취 직전 1곳.

```csharp
// DiscoverExternalCameraAsync 선두(§8.2 시퀀스) — 조건이 이 한 줄 외에 존재하지 않는다(TS1·TS2).
var plan = ExternalCameraSimulation.Plan(_testMode?.Options ?? TestModeOptions.Disabled);
bool simulated = plan is not null && _testMode!.IsTestUser(_shell.CurrentUser);
// ⚠️ 금지: _testMode?.IsEnabled 단독 분기(TS2) / IExternalCamera 데코레이터(TS1)
```

| 경로 | 시뮬레이션 활성 시 동작 | 왜 안전한가 |
|---|---|---|
| [장치 검색] (설정) | `CheckReadiness`·WMI 프로브·`ConnectAsync` **전부 0회** — plan 입력으로 Judge·문구·인식 콤보만 갱신 + W38 라인 | 표시 전용. TS3·TS4 |
| 촬영 세션 | **무변경** — `ExternalCameraEnabled(ini)` 기준으로 실 `ConnectAsync` → MissingShim → W7 토스트("외부 카메라를 사용할 수 없어 웹캠으로 촬영합니다 (SDK 모듈이 설치되지 않았습니다)") → 웹캠 강등 | `CaptureViewModel`은 `ITestModeService`를 참조하지 않는다(참조 부재가 곧 증명 — T-B7이 잠금). **가짜 사진이 만들어질 코드 경로가 없다** |
| 카메라 테스트 모달 | **무변경** — 외부 항목 선택 시 실 `ConnectAsync` 실패 → W10/W12 정직 표시 | `CameraTestViewModel`도 `ITestModeService` 무참조. 설정의 "연결 확인됨(시뮬레이션)"과 모달의 실패가 나란히 보여도 W38 라인이 모순을 해설한다 |
| QR·업로드·타임랩스 | 무변경(접점 자체가 없음) | 시뮬레이션 산출물은 문자열(화면 문구)뿐 — 이미지·파일을 만들지 않는다 |

**"촬영은 정직하게 실패한다"의 구체 모습**: QA가 `[Test] ExternalCamera=1` + 설정 토글 on 저장 후 촬영을 진입하면, 화면은 W7 토스트로 "SDK 모듈이 설치되지 않았습니다"를 말하고 웹캠으로 촬영한다. 설정 화면의 S6(시뮬레이션)과 어긋나 보이는 이 상태가 **의도된 정직함**이다 — W38 라인("실제 장치 관측이 아닙니다")이 그 간극을 화면에서 설명한다.

### 5.6 배너 접미 — 시뮬레이션의 전역 표식 (TM4 확장, 조건부 채택)

테스트 모드 배너(B9)에 외부 카메라 시뮬레이션 상태를 접미로 표시한다. W38(검색 결과 라인)은 검색 결과 영역에서만 보이므로, 화면 어디서든 보이는 전역 표식은 배너가 담당한다.

**⚠️ 접미 부착 조건 — 반드시 "테스트 계정 로그인 중" 분기에만 붙인다.** `AppShellViewModel.TestModeBannerText`는 이미 3분기(테스트 계정 로그인 중 / 로그아웃 / 실계정 로그인 — B9.3)이며, 접미는 **첫 분기(현재 사용자가 `IsTestUser`인 상태) + `Options.ExternalCamera=true`**에서만 붙는다. `IsEnabled`나 키 값 단독으로 판정해 로그아웃·실계정 분기에도 붙이면, **시뮬레이션이 적용되지 않는 상태**(TS2 — `IsTestUser=false`면 [장치 검색]은 실관측)에서 "시뮬레이션 중"이라고 말하는 **거짓 배너**가 된다 — 이 설계가 없애려는 바로 그 실패 유형이다.

| 배너 분기(B9.3) | 접미 |
|---|---|
| 테스트 계정 로그인 중 + `ExternalCamera=1` | **W40 부착**: ` · 외부 카메라 시뮬레이션({모델 표시명\|없음})` — Type 매핑 성공이면 모델 표시명(예 "Nikon D5300"), `-1`이면 `없음` |
| 테스트 계정 로그인 중 + `ExternalCamera=0` | 접미 없음(현행 문구 그대로) |
| 로그아웃 / 실계정 로그인 | **접미 없음**(현행 문구 그대로 — 시뮬레이션이 적용되지 않는 상태이므로) |

- 구현 지점: `AppShellViewModel`의 배너 문구 조립 분기(기존 `CurrentUserChanged` 재발행 경로 재사용 — 신규 이벤트·INPC 경로 없음). B9.3 동결 문구의 **개정**이며(첫 행에 조건부 접미), 나머지 두 행은 무변경 — §8.3 표에 기록.
- 회귀 잠금: T-B9(§12.2) — 실계정·로그아웃 분기에서 접미 부재를 단정한다.

## §6 C. 모델 콤보 → "인식된 카메라" 목록 (개념 전환)

### 6.1 "인식됨"의 정의 — 딜레마 정면 판정

it24 관측 3원 중 무엇을 "인식"으로 볼 것인가:

| 후보 | 판정 | 왜 |
|---|---|---|
| ① 제어 스택 준비도(`CheckReadiness`) | 기각 | 파일·shim 상태는 장치의 존재와 무관하다 — "인식"이라는 말 자체가 성립하지 않는다 |
| ② WMI 관찰 양성(S3·S5의 감지 신호) | **기각 — 콤보에는 올리지 않는다** | (a) WMI 매칭은 장치명 문자열 우연에 기대는 best-effort(U2 — 제네릭 이름이면 miss, 비카메라 장치가 "Nikon" 문자열을 품을 수도)라 "그 지원 모델이 맞다"를 보장하지 않는다. (b) 콤보는 **저장(ini 기록)으로 이어지는 조작 표면**이다 — 제어 불가 항목을 올리면 운영자가 "선택했는데 촬영이 안 되는" 상태를 스스로 만들 수 있다. (c) S3의 정직한 서술("USB에서 감지되었으나 SDK 모듈이 없어 제어할 수 없습니다")은 검색 결과 영역이 이미 담당한다 — 같은 장치가 확실성이 다른 두 표면에 다르게 뜨면 R5가 막으려던 개념 혼동이 재발한다 |
| ③ **SDK 연결 확인(S6)** | **채택** | "이 앱이 실제로 제어를 확인한 카메라"만이 선택할 가치가 있는 항목이다. S6은 관측 직후 해제하므로 문구도 "연결 확인됨"(과거형 관측) — 인식 목록의 의미와 정확히 일치한다 |

이 판정의 정직한 귀결: **현 프로덕션(SDK 미동봉 기본)에서 실경로의 인식 목록은 항상 비어 있다** — 콤보는 `- 선택안함 -`만 갖는다. 이는 결함이 아니라 사용자가 원문에서 기술한 바로 그 기본 상태이며("연결된 외부 카메라가 만약 없다면 … 선택안함만 존재하면"), 채워진 콤보의 확인 수단이 곧 B부 시뮬레이션이다. S3(감지되었으나 제어 불가)와의 정합: S3에서도 콤보는 비어 있고, 감지 사실은 W20 라인이 말한다 — "물리적으로 보이는 것"과 "선택 가능한 것"의 구분이 상태 전수표(§6.4)에 그대로 남는다.

### 6.2 콤보의 항목·수명

- 항목 타입: `RecognizedCameraOption(string Value, string Display)` — sentinel은 `Value=""`, `Display="- 선택안함 -"`(W34). 인식 항목은 `Value=레지스트리 Id`, `Display=DisplayName`.
- 목록은 **화면 세션 상태**다(ini 비저장): 설정 진입 시 sentinel 단독으로 초기화(S0 — "검색 전"이지 "없음 단정"이 아니다. W33 캡션이 [장치 검색]으로 안내), [장치 검색] 1회마다 재구성.
- 상태별 구성: **S6에서만** sentinel + 인식 모델 1행(실경로 = `Resolve(ExternalCameraModel)` 행, 시뮬레이션 = `FindByTestType` 매핑 행). S0·S1·S2·S3·S4·S5·S7 전부 sentinel 단독.
- 콤보 `IsEnabled = CanEditExternalCamera`만 — 인식 0이어도 **열 수 있어야 한다**(사용자 원문: "선택하려고 콤보박스를 열어도 선택안함만 존재하면"). 빈 목록 Disable을 쓰지 않는다.

### 6.3 저장값과의 관계 — 검색 결과가 설정을 지우지 않는다

콤보 `SelectedValue`를 ini 미러(`ExternalCameraModel`)에 **직접 바인딩하지 않는다.** 직접 바인딩하면 인식 목록이 비는 순간 WPF ComboBox가 매칭 실패한 `SelectedValue`를 null로 되써서 저장값이 소멸한다 — it24 P5가 합성 행으로 막았던 바로 그 함정인데, 이번엔 사용자가 "빈 목록에는 선택안함만"을 명시했으므로 합성 행 해법을 쓸 수 없다. 대신 **선택을 분리**한다:

| 요소 | 규칙 |
|---|---|
| `RecognizedCameraSelection` (VM 신설, string) | 콤보 `SelectedValue` 바인딩 대상. 초기값 `""`(sentinel). WPF가 null을 되쓰면 `""`로 정규화(부분 메서드 훅) — **`ExternalCameraModel`은 건드리지 않는다** |
| `ExternalCameraModel` (기존, ini 미러) | 콤보에서 분리. 사용자가 **인식 항목을 명시 선택**했을 때만 그 Id로 갱신(`_normalizing` 가드 하에서). sentinel 선택·목록 재구성은 이 값을 바꾸지 않는다 |
| 목록 재구성 직후 | 인식 항목의 Id가 `ExternalCameraModel`과 일치(OrdinalIgnoreCase)하면 그 항목을 선택, 아니면 sentinel 선택 |
| Save | 현행 그대로 `s.ExternalCameraModel = ExternalCameraModel`(`CanEditExternalCamera` 게이트). sentinel 상태의 저장은 원값 재기록 = 보존 |
| md3 경로·노출 재적용·USB 키워드 | 현행 그대로 `ExternalCameraModel`(ini) 기준 — 콤보 개편의 영향 0 |

수용하는 트레이드오프: 인식이 비어 있는 동안 **구성된 모델 Id가 화면에 보이지 않는다.** 현 시점 레지스트리가 1행(기본값과 동일)이라 정보 손실이 없고, 다중 모델 시대에는 인식이 곧 표시 경로가 된다. 구성 모델의 흔적은 W11 사유(md3 파일명)와 지원 카메라 오버레이(D부)가 보조한다.

### 6.4 인식 콤보 상태 전수표

| 검색 상태 | 콤보 항목 | 선택 | `ExternalCameraModel`(ini) |
|---|---|---|---|
| S0 (진입 직후) | sentinel | sentinel | 불변 |
| S1 (검색 중) | 직전 상태 유지 | 유지 | 불변 |
| S2·S3·S4·S5·S7 | sentinel | sentinel | 불변 — **검색 결과가 저장값을 지우지 않는다** |
| S6 · 인식 Id = 저장 Id | sentinel + 인식 1행 | **인식 행 자동 선택** | 불변(같은 값) |
| S6 · 인식 Id ≠ 저장 Id (다중 모델 미래) | sentinel + 인식 1행 | sentinel(자동 변경 금지) | 불변 — 사용자가 명시 선택해야 갱신 |
| 사용자가 인식 행 선택 → [저장] | — | 인식 행 | **기록**(일반 편집 규칙) |

### 6.5 다중 모델 미래의 한계 (기록)

인식(S6)은 "현재 구성된 모델의 md3로 연결 시도"의 결과다 — 레지스트리에 모델이 여럿이어도 검색 1회가 확인하는 것은 구성 모델 1종이다. 물리 카메라가 다른 지원 모델이면 S4로 끝나 인식 목록이 비는데, 이는 현 어댑터 구조(`ConnectAsync`가 구성 모델 기준)의 정직한 한계다. 배치된 md3 전수를 순회 프로브하는 확장은 다중 모델이 실재할 때의 별도 이터레이션으로 남긴다(§15.2 비목표) — 지금 설계하면 미검증 가정(모델별 연결 시도의 부작용) 위에 짓게 된다.

---

## §7 D. 지원 카메라 목록 — 별도 표면

### 7.1 형태 판정 — 별도 `Window` 금지, 설정 화면 오버레이

| 후보 | 판정 | 왜 |
|---|---|---|
| 별도 `Window`(모달) | 기각 | headless 테스트가 `Window`를 인스턴스화하지 못해(F14) XAML 회귀(바인딩 오타·리소스 키)를 잡을 수 없다 — 라이선스 고지가 같은 이유로 별도 Window를 피했다. 정적 목록 하나에 카메라 테스트 모달급(전용 다이얼로그 서비스·수명 관리) 비용을 치를 이유도 없다 |
| **SettingsView 내 오버레이** | **채택** | 라이선스 고지 오버레이와 동형 — Visibility 전환 + 스크림(`Brush.Scrim` 재사용). `XamlResourceTests`가 UserControl 로드로 검증 가능. 새 창 상태·포커스·z-order 문제 없음 |

닫기 규약: **하단 중앙 정렬 액션 바 + `Margin="8,0"`** — 설정·진단·라이선스 전부와 동일(F13). 버튼 라벨은 `닫기`(W39) — **화살표·글리프 금지**(라이선스 화면에서 같은 이유로 수정한 이력).

### 7.2 레지스트리 스키마 확장 — 제조사·제품명 분리 + TestTypeCode

현행 `ExternalCameraModel(Id, DisplayName, Md3FileName)`에는 제조사가 분리돼 있지 않다. "제조사, 제품명 별로 정리" 요구와 §5.2 매핑을 **한 번의 스키마 변경**으로 해소한다:

```csharp
/// <param name="Manufacturer">제조사(그룹 헤더). 예: "Nikon".</param>
/// <param name="ModelName">제품명(제조사 제외). 예: "D5300".</param>
/// <param name="TestTypeCode">[Test] ExternalCameraType 매핑 코드. Id와 같은 지위 — 배정 후 변경·재사용 금지(§5.2).</param>
public sealed record ExternalCameraModel(
    string Id, string Manufacturer, string ModelName, string Md3FileName, int TestTypeCode)
{
    /// <summary>표시명(콤보·검색 헤드라인·USB 키워드 유도) — 기존 소비자와의 호환 파생.</summary>
    public string DisplayName => $"{Manufacturer} {ModelName}";
}
```

파급 정리 (기존 소비자 전수):

| 소비자 | 영향 |
|---|---|
| 레지스트리 초기화(1행) | 위치 인자 5개로 재작성 — **모델 추가 = 표 한 줄 규약 유지**(행이 다섯 필드를 가질 뿐) |
| `DisplayName` 소비 3곳: 설정 콤보 표시·S6 헤드라인 폴백·`ModelKeywords()`(F12) | **무영향** — 파생 속성이 같은 문자열("Nikon D5300")을 돌려준다. 키워드 유도(`Split(' ')`)도 동일 결과 |
| `Md3FileName` 소비(`SdkRuntimeProbe`) · `Id` 소비(ini·`Find`/`Resolve`) | 무영향 |
| `CameraTestTarget.External(model)`(테스트 모달) | 무영향(record 참조 전달) |
| 위치 인자로 record를 생성하는 테스트(T-R1 계열) | 컴파일 에러로 표면화(V2) — Step 2에서 재작성 |

### 7.3 오버레이 내용 — "지원"과 "인식"의 혼동 방지

콤보가 인식 목록이 된 뒤로 **이 오버레이가 지원 목록의 유일한 자리**다. 문구가 두 개념의 경계를 직접 말한다:

```
[스크림 위 중앙 카드]
  지원 카메라                                   ← W36 (GroupTitle 재사용)
  이 앱이 SDK 연동을 지원하는 카메라 목록입니다.
  연결 인식 여부와는 무관합니다 — 연결 확인은 [장치 검색].   ← W37 (Text.Caption)
  ─────────────
  Nikon                                        ← 제조사 그룹 헤더(Text.Body + SemiBold — 인라인 속성, 신규 키 0)
    D5300                                      ← ModelName (Text.Body)
  ─────────────
              [닫기]                            ← W39 · 하단 중앙 액션 바 · Margin="8,0"
```

- 데이터는 VM이 레지스트리에서 1회 파생: `SupportedCameraGroup(string Manufacturer, IReadOnlyList<string> Models)` 목록(제조사 오름차순·모델명 오름차순, 순수 LINQ — headless 테스트 가능). `CollectionViewSource` 그룹핑을 쓰지 않는 이유: 정적 소량 데이터에 뷰 계층 그룹핑은 테스트 불가능한 XAML 로직만 늘린다.
- 스크롤: 그룹 목록을 `ScrollViewer`(세로)로 감싼다 — 모델 수가 늘어도 카드가 화면을 넘지 않는다.

### 7.4 진입 버튼·권한

- **위치**: 외부 카메라 하위 패널 안, 인식 콤보 캡션(W33) 바로 아래 우측 정렬 행. 지원 목록이 궁금해지는 유일한 맥락(모델을 고르는 자리)이다.
- **라벨**: `지원 카메라 목록`(W35) — `Button.Secondary`, 글리프 없음.
- **권한 게이트 없음**: 지원 모델 목록은 비밀이 아니다(라이선스 고지·QR 섹션이 게스트에게 보이는 것과 같은 논거). 열람은 편집이 아니므로 `CanEditExternalCamera`도 걸지 않는다. 단 하위 패널 자체가 `ExternalCameraEnabled=on`일 때만 보이므로 실질 노출 범위는 그에 따른다.

---

## §8 UI 명세

### 8.1 설정 화면 — 외부 장치 섹션 (it24 §8.1 대비 diff)

```
외부 장치                                        (섹션 상시 표시 — it24 §4 불변)
  외부 카메라 사용   [로그인 필요†|권한 없음‡] [토글]   (불변)
  ── 이하 ExternalCameraEnabled=on일 때만(기존 구조) ──
  [캡션 W1] 타임랩스 기능은 웹캠으로만 동작됩니다.        (불변)
  인식된 카메라       [ComboBox — SelectedValue]      ← 개편: 라벨 "지원 모델"→"인식된 카메라",
                                                       ItemsSource=RecognizedCameraOptions,
                                                       SelectedValuePath=Value, DisplayMemberPath=Display,
                                                       SelectedValue=RecognizedCameraSelection,
                                                       IsEnabled=CanEditExternalCamera
  [캡션 W33] 연결이 인식된 카메라만 표시됩니다…          ← 신설(W24 대체)
                                  [지원 카메라 목록]    ← 신설 버튼(W35, 우측 정렬, 게이트 없음)
  [캡션 W2] 프리뷰는 웹캠 영상입니다…                   (불변)
  장치 확인          [장치 검색]                       (불변 — 게이트·스피너·단일 비행 그대로)
    {검색 결과 헤드라인 S0~S7}                          (불변 + 시뮬레이션 시 W38 상세 라인 추가)
    {상세 라인들}                                      (불변)
  셔터 속도/조리개/ISO (슬라이더+입력)                   (불변)
  [캡션 W3]                                          (불변)

  프린터 사용        [토글 · IsEnabled="False"]        ← 환원: 게이트 노티 2종 제거, 편집 게이트 제거
  [캡션 W32] 추후 지원 예정                             ← 신설(토글 행 우측, 토글 앞)
  (프린터 하위 패널 전체 삭제 — W25 고지·콤보·[다시 검색]·상태 문구)

[지원 카메라 오버레이 — §7.3 스케치. Visibility=IsSupportedCameraListOpen, 스크림 재사용]
```

XAML 제약 준수: **신규 리소스 키 0**(`GuestGateNote`·`Text.Caption`·`Text.Body`·`Button.Secondary`·`GroupTitle`·`Brush.Scrim`·`Toggle`·`Spinner.Ring` 재사용 — 병합 딕셔너리 교차 `StaticResource` 함정 자동 회피). 콤보는 `SelectedValue` 값 기반(it7 B9). 문구 전부 `TextWrapping="Wrap"`·고정폭 금지(고정폭 컬럼 잘림 이력). 그룹 헤더 SemiBold는 인라인 속성으로(스타일 키 신설 금지).

### 8.2 VM 변경 전수 (`SettingsViewModel` — 바인딩 누락 방지)

신설:

| 멤버 | 형 | 역할 |
|---|---|---|
| `RecognizedCameraOptions` | `ObservableCollection<RecognizedCameraOption>` | 인식 콤보 목록. 초기값 sentinel 단독(§6.2) |
| `RecognizedCameraSelection` | string [ObservableProperty] | 콤보 SelectedValue. null 되쓰기 → `""` 정규화, 인식 Id 선택 시에만 `ExternalCameraModel` 갱신(§6.3) |
| `OpenSupportedCameraListCommand` / `CloseSupportedCameraListCommand` | RelayCommand | 오버레이 열기/닫기(게이트 없음) |
| `IsSupportedCameraListOpen` | bool [ObservableProperty] | 오버레이 Visibility |
| `SupportedCameraGroups` | `IReadOnlyList<SupportedCameraGroup>` (get-only) | 레지스트리 파생(§7.3) — 불변이라 INPC 불요 |
| ctor 인자 `ITestModeService? testMode = null` | 마지막 선택 파라미터 | 시뮬레이션 판정 입력(TS2). 기존 위치 인수 호출부(테스트 다수) 무변경 — B5.3 선례 |

제거(A부): `PhotoPrinterName`·`IsEnumeratingPrinters`·`PrinterStateText`·`HasPrinterStateText`·`HasPrinters`·`PrinterOptions`·`PrinterEnumerationTask`·`RefreshPrintersCommand`·`OnPhotoPrinterEnabledChanged` 훅·`ApplyPrinterEnumeration`·`PrinterOptionItem`·프린터 문구 상수 6종·ctor `IPrinterEnumerator?` 인자. `PhotoPrinterEnabled`는 존치(토글 표시).

변경: `DiscoverExternalCameraAsync` 선두에 §5.5 시뮬레이션 분기, `ApplyDiscoveryResult`에 인식 모델 파라미터 추가(S6에서 콤보 재구성, 그 외 sentinel 초기화), 시뮬레이션 시 W38 라인 추가. `LoadSettings`에서 `PhotoPrinterName` 로드 제거, `SaveSettings`에서 프린터 2줄 제거.

타 VM 변경: `AppShellViewModel` — 배너 문구 조립의 **테스트 계정 로그인 분기에만** W40 접미(§5.6). 신규 멤버·이벤트 없음(기존 `TestModeBannerText` 조립과 `CurrentUserChanged` 재발행 경로 재사용).

### 8.3 동결 문구 표 (it24 W15~W31에 이어서)

신설:

| ID | 위치 | 문구 |
|---|---|---|
| W32 | 프린터 토글 캡션 | `추후 지원 예정` |
| W33 | 인식 콤보 캡션 | `연결이 인식된 카메라만 표시됩니다. 인식 확인은 [장치 검색], 지원 모델은 [지원 카메라 목록]에서 확인하세요.` |
| W34 | 인식 콤보 sentinel 항목 | `- 선택안함 -` |
| W35 | 오버레이 진입 버튼 | `지원 카메라 목록` |
| W36 | 오버레이 제목 | `지원 카메라` |
| W37 | 오버레이 안내 | `이 앱이 SDK 연동을 지원하는 카메라 목록입니다. 연결 인식 여부와는 무관합니다 — 연결 확인은 [장치 검색].` |
| W38 | 시뮬레이션 명시 라인(검색 상세 말미) | `테스트 모드 시뮬레이션 결과입니다 — 실제 장치 관측이 아닙니다.` |
| W39 | 오버레이 닫기 버튼 | `닫기` |
| W40 | 배너 접미(§5.6 — 테스트 계정 로그인 분기 한정) | ` · 외부 카메라 시뮬레이션({모델 표시명\|없음})` |
| (라벨) | 콤보 행 라벨 | `지원 모델` → **`인식된 카메라`** |

it24 동결 문구 개정·폐기 목록 (이 표가 개정의 단일 진실 — it24 문서는 수정하지 않는다):

| ID | 처분 | 왜 |
|---|---|---|
| W24 (`이 앱이 SDK 연동을 지원하는 모델 목록입니다. 연결된 장치 목록이 아닙니다 — …`) | **폐기 → W33이 대체** | 콤보의 의미가 지원→인식으로 바뀌어 문구가 거짓이 된다(팀 지시 지적 그대로) |
| W25 (`인쇄 기능은 아직 제공되지 않습니다…`) | **폐기 → W32가 대체** | 프린터 하위 패널 제거 |
| W26·W27·W28·W29·W30·W31 (프린터 상태·표시·버튼 일체) | **폐기(대체 없음)** | 표면 자체가 사라진다. 계약 타입(`PrinterEnumerationResult`)의 P2/P4 구분은 스캐폴드에 남는다 |
| W15~W23 (검색 버튼·S0~S7 문구·참고 라인) | **유지(무변경)** | 검색 파이프라인 불변 |
| W16 (`장치를 검색하지 않았습니다…`) | 유지 | S0 문구는 인식 콤보 초기 상태의 안내까지 겸한다(W33과 함께) |
| B9.3 배너 문구(it23 동결) — "테스트 계정 로그인 중" 행 | **개정**: `ExternalCamera=1`일 때만 W40 접미 부착 | §5.6. 로그아웃·실계정 행은 무변경 — 시뮬레이션이 적용되지 않는 상태에 접미를 붙이면 거짓 배너 |

---

## §9 설정 스키마 변경표

### 9.1 `[MCPhoto]` 섹션 — 변경 없음 (전 키 무변경)

| 키 | 처분 | 비고 |
|---|---|---|
| `ExternalCameraEnabled`·`ExternalCameraModel`·`ExternalShutterSpeed`·`ExternalAperture`·`ExternalIso` | 무변경 | 직렬화·Clamp·Clone 전부 그대로 |
| `PhotoPrinterEnabled` | 무변경(의미 서술만 환원 — §4.3) | UI 편집 불가로 복귀, 값은 라운드트립 보존 |
| `PhotoPrinterName` | 무변경(**잔존 키로 강등** — §4.3) | UI 표면 없음. `WriteFrom`에서 빼지 않는다(첫 저장 소멸 함정) |

### 9.2 `[Test]` 섹션 — 2키 신설 (§5.1 표)

| 키 | 타입 | 기본값 | Clamp/폴백 |
|---|---|---|---|
| `ExternalCamera` | bool | `0` | 인식 불가 → false(무경고 — bool 규약 동일) |
| `ExternalCameraType` | int | `-1` | 파싱 실패·`FindByTestType` 미지 → `-1` + Warning |

### 9.3 코드 갱신 지점

| 파일 | 갱신 |
|---|---|
| `TestModeOptions.cs` | record에 `bool ExternalCamera`·`int ExternalCameraType` 추가(+XML 주석: 표시 시뮬레이션 전용·TS 계열 참조), `Disabled`에 기본값, `FromIni`에 §9.2 파싱·경고. 생성자 호출 지점은 `Disabled`·`FromIni`뿐(F5) — 테스트는 `FromIni` 경유라 파급 최소 |
| `ExternalCameraModels.cs` | record 스키마 확장(§7.2) + `FindByTestType` + 표 1행 재작성 |
| `ExternalCameraSimulation.cs` (신규) | §5.4 코드 블록 |
| `SettingsViewModel.cs` | §8.2 전수 |
| `SettingsView.xaml` | §8.1 diff |
| `ServiceRegistration.cs` | 프린터 등록 주석 갱신(§4.2-2)만 — 등록 자체 무변경 |
| `IPrinterEnumerator.cs`·`SystemPrinterEnumerator.cs` | 스캐폴드 표식 주석(§4.2-1)만 — 코드 무변경 |

---

## §10 실패·부재 경로 전수표 (it24 E12~E20에 이어서)

| ID | 상황 | 감지 지점 | 표시 | 강등 동작 |
|---|---|---|---|---|
| E21 | `[Test] ExternalCameraType` 범위 밖·파싱 실패 | `TestModeOptions.FromIni` | 배너(기존) + Warning 로그 | `-1`(없음)로 폴백 — 시뮬레이션은 S4 시나리오로 동작 |
| E22 | TestMode ini ON + **실계정 로그인** 상태에서 [장치 검색] | `IsTestUser` 게이트(TS2) | 실관측 결과(현 프로덕션이면 S2) — W38 없음 | 시뮬레이션 미적용. 배너는 "실제 계정으로 로그인" 문구(B9.3) 유지 |
| E23 | 시뮬레이션 활성 + `ExternalCameraEnabled=on`으로 **촬영 진입** | `CaptureViewModel`(실경로 — `[Test]` 무참조) | W7 토스트(사유 W10) | 웹캠 강등 — **가짜 스틸 생성 경로 없음**(TS1, T-B7) |
| E24 | 시뮬레이션 활성 + 카메라 테스트 모달 외부 항목 | `CameraTestViewModel`(실경로) | 정보 패널 W10/W12 | 현행 강등 그대로 — 모달 무변경 |
| E25 | 인식 목록 재구성으로 콤보가 SelectedValue를 null 되쓰기 | `RecognizedCameraSelection` 정규화 훅 | 표시상 sentinel | `ExternalCameraModel` 불변(§6.3) — 저장값 클로버 없음 |
| E26 | 잔존 `PhotoPrinterName` 값이 있는 ini | (표면 없음) | 아무것도 표시하지 않음 | 저장 시 원값 그대로 재기록(§4.3) |
| E27 | 지원 카메라 오버레이 열린 채 화면 이탈 | VM Transient 수명 | — | 상태 소멸(다음 진입 시 닫힘 초기값). 구독·자원 없음 — 누수 불가 |
| E28 | 시뮬레이션 중 예외(이론상 — Plan은 순수) | VM 커맨드 catch(기존 E13) | W22(S7) | `finally`로 `IsDiscovering=false` — 기존 경로 재사용 |

어느 행도 크래시·무한 대기·거짓 단정으로 가지 않는다. E22·E23이 이 표의 핵이다 — **시뮬레이션이 꺼져야 할 자리에서 꺼진다**는 것이 봉인의 관측 가능한 형태다.

---

## §11 스레딩 · 수명 · 인코딩

| 항목 | 규칙 |
|---|---|
| `ExternalCameraSimulation.Plan` | 순수 함수 — 스레드 무관. 검색 시퀀스의 기존 `Task.Run` 경계 구조 무변경(시뮬레이션 활성 시 `Task.Run` 관측 구간을 **건너뛴다** — I/O 자체가 없으므로) |
| 인식 콤보 갱신 | `ApplyDiscoveryResult` 내부 = await 복귀 후 UI 컨텍스트(기존 `DiscoveryDetailLines`와 동일) |
| 오버레이 | 이벤트 구독 0·타이머 0·비동기 0 — 순수 Visibility 전환 |
| 이벤트 구독 | **신규 구독 0** — 기존 `ExposureParameters` 구독 해제(OnLeaveAsync)만 유지. 프린터 훅(`OnPhotoPrinterEnabledChanged`) 제거로 구독 아닌 훅도 하나 줄어든다 |
| 단일 비행 | `IsDiscovering` 기존 그대로(시뮬레이션 경로도 같은 플래그·`finally` 확정을 지난다 — 분기 지점이 플래그 안쪽이므로 자동) |
| 인코딩 | 수정·신규 .cs 전부 UTF-8 no BOM(F16). XAML·문서 기존 인코딩 유지. 확인: `head -c 3 <file> | od -An -tx1` ≠ `ef bb bf` |

---

## §12 테스트 전략 (`tests/MCPhoto.Tests`, headless — 장비·SDK 불요)

### 12.1 Core 순수 (신규)

| ID | 테스트 | 검증 |
|---|---|---|
| T-B1 | `TestModeOptions.FromIni` — 2키 결측 → `(false, -1)` / `ExternalCamera=1`+`Type=0` → `(true, 0)` / bool 표기 변형(`true`/`on`) | §9.2 기본값·파싱 |
| T-B2 | 상동 — `Type=-2`·`99`·`abc` → `-1` + Warning **정확 1건**(문구 포함) / `ExternalCamera` 인식 불가 → false 무경고 / `ExternalCamera=0`+`Type=0` → 무경고 무시 | E21·§5.1 |
| T-B3 | 레지스트리 — 전 행 `TestTypeCode` 유일·`>= 0`, `FindByTestType(-1)`=null, `FindByTestType(0)`=D5300 행, 미지 코드 null | §5.2 매핑 안정성(행 순서 무관 회귀 잠금) |
| T-B4 | `ExternalCameraSimulation.Plan` 전수 — `Enabled=false`→null / `ExternalCamera=false`→null / `Type=-1`→(CanControl=true, Connected=false, Model=null) / `Type=0`→(true, true, D5300). 각 plan을 `Judge`에 태우면 S4/S6 | §5.4 표 그대로 |
| T-R1' | 스키마 확장 후 레지스트리 — `Default`의 Id·Manufacturer("Nikon")·ModelName("D5300")·Md3FileName·**`DisplayName == "Nikon D5300"`(파생 호환)** | §7.2 — DisplayName 소비자 무영향의 증명 |

### 12.2 SettingsViewModel — 인식 콤보·시뮬레이션·봉인

| ID | 테스트 | 검증 |
|---|---|---|
| T-C1 | 설정 진입 직후 — 콤보 sentinel 단독(`- 선택안함 -`)·선택 sentinel·`ExternalCameraModel` 불변 | §6.4 S0 |
| T-C2 | 실경로 S6(fake `IExternalCamera` 스크립트) — 콤보 = sentinel + 모델 1행, 저장 Id 일치 시 그 행 자동 선택 | §6.4 |
| T-C3 | 실경로 S2·S4 — 콤보 sentinel 단독 + **`ExternalCameraModel`·ini 원값 불변** | §6.4 클로버 금지 |
| T-C4 | 인식 행 명시 선택 → `ExternalCameraModel` 갱신 → Save 기록 / sentinel 선택·null 되쓰기 → 불변 | §6.3, E25 |
| T-C5 | `SupportedCameraGroups` — Nikon 그룹 1개·D5300 1행·정렬. Open/Close 커맨드로 `IsSupportedCameraListOpen` 왕복 | §7.3 |
| T-B5 | **시뮬레이션 S6**: TestMode+`ExternalCamera=1`+`Type=0`+테스트 유저 로그인 → [장치 검색] = W21 헤드라인 + **W38 라인** + 콤보에 D5300 + fake의 `CheckReadiness`/`ConnectAsync`/probe 델리게이트 **호출 0회** | TS1·TS3·TS4 |
| T-B6 | **시뮬레이션 S4**: `Type=-1` → W19 + W38 + 콤보 sentinel 단독 | §5.4 |
| T-B7 | **봉인(촬영)**: 시뮬레이션 활성 ini 하에서 `CaptureViewModel` 세션 — fake `IExternalCamera` 호출 시퀀스가 시뮬레이션 없음과 **완전 동일**(연결 실패 시 웹캠 강등·W7)·가짜 스틸 0. 보조 정적 검사: `CaptureViewModel`·`CameraTestViewModel` 생성자에 `ITestModeService` 부재 | TS1 — "새지 않음"의 회귀 게이트 |
| T-B8 | **봉인(실계정)**: TestMode ini ON + 다른 `User` 인스턴스 로그인 → [장치 검색]이 **실관측 경로**(probe 호출 1회) + W38 없음 | TS2, E22 |
| T-B9 | **배너 접미**: 테스트 계정 로그인 + `ExternalCamera=1` → `TestModeBannerText`에 W40 포함(Type=0이면 모델 표시명, -1이면 "없음") / `ExternalCamera=0`·로그아웃·**실계정 로그인** 분기 → W40 **부재** | §5.6 — 거짓 배너 회귀 게이트 |
| T-A1' | 프린터 환원 — 전 역할에서 Save 후 `PhotoPrinterEnabled`/`PhotoPrinterName` ini 원값 보존(미기록) + `OnEnterAsync`가 어떤 열거자도 접촉하지 않음 | §4.1·§4.3 |
| T-X1'' | headless XAML 로드 — 개편 섹션·오버레이 바인딩 경로 유효, 프린터 토글 `IsEnabled=False` 리터럴, 신규 리소스 키 0 | §8.1 |

### 12.3 it24에서 깨지는 기존 테스트 전수 목록과 재작성 방향 (삭제 금지 — 단정을 지우면 회귀를 못 잡는다)

`tests/MCPhoto.Tests/SettingsViewModelDiscoveryTests.cs` 기준(라인은 현행):

| 깨지는 테스트 | 재작성 방향 |
|---|---|
| `P3_Lists_Printers_With_Default_Suffix`(:431)·`P2_Empty_Success_Says_None_Installed`(:466)·`P4_Enumeration_Failure_Says_Undetermined_Not_None`(:479)·`Enumerator_Exception_Degrades_To_Undetermined`(:493)·`Missing_Enumerator_Service_Degrades_To_Undetermined`(:505) | VM 표면이 사라지므로 VM 수준에서는 폐기하되, **P2≠P4 구분·예외 무투과 단정은 열거자 단위 테스트로 이관**(fake 없이 `SystemPrinterEnumerator`+`PrinterEnumerationResult` 계약 — §12.4). "명제 구분(R4)"의 회귀 감시는 계약 계층에 남는다 |
| `P5_Saved_Name_Absent_From_List_Is_Kept_As_Synthetic_Row`(:518)·`Empty_Saved_Name_Adds_No_Synthetic_Row`(:548)·`P4_Preserves_Saved_Name_Even_Without_A_List`(:559) | 합성 행 메커니즘 자체가 사라진다 → **보존 단정만 T-A1'로 승계**("어떤 경로도 `PhotoPrinterName`을 지우지 않는다") |
| `User_Saves_Printer_Two_Keys`(:580) | **단정 반전 재작성**: User여도 2키 미기록(원값 보존) — 이름도 `Save_Never_Writes_Printer_Keys`로 |
| `TempUser_And_Guest_Save_Preserve_Printer_Ini_Values`(:605) | T-A1'에 흡수(전 역할 보존으로 일반화) |
| `Toggling_Printer_On_Enumerates_Once`(:635)·`Load_Does_Not_Double_Enumerate_On_Entry`(:658)·`Saving_Does_Not_Retrigger_Enumeration`(:671)·`Printer_Enumeration_Is_Single_Flight`(:710)·`Printer_Panel_Off_Does_Not_Enumerate`(:452) | 트리거 자체가 사라진다 → **"설정 VM은 어떤 시점에도 열거자를 접촉하지 않는다"**(T-A1' 후반) 하나로 압축 재작성 |
| `Guest_Sees_Section_Values_But_Cannot_Edit`(:360) | 프린터 부분 단정을 "토글 항상 Disable + W32 캡션"으로 교체(가시성·외부 카메라 부분은 유지) |
| `S6_Connected_Shows_Model_Battery_And_Test_Hint`(:235) 등 S계열 | 깨지지 않음 — **확장**(인식 콤보 단정 추가, T-C2와 병합 가능) |
| 레지스트리 record를 위치 인자로 생성·단정하는 테스트(T-R1 계열 — `ExternalDeviceScaffoldTests`/`ExternalCameraSettingsTests` 내 위치는 구현 시 컴파일 에러로 특정) | 5필드 스키마로 재작성 + T-R1'의 DisplayName 파생 단정 추가 |
| `XamlResourceTests` 중 SettingsView 바인딩 검사 | 제거된 프린터 멤버·신설 멤버 반영(T-X1'') |

### 12.4 스캐폴드 계약 테스트 — 존치

`Real_Printer_Enumeration_Never_Throws`(:695)와 열거자·`PrinterEnumerationResult` 단위 테스트는 **그대로 존치**한다 — 스캐폴드의 "예외를 던지지 않는다"·"P2≠P4" 계약이 인쇄 이터레이션까지 살아 있음을 잠그는 것이 목적이다(§4.2-3).

### 12.5 실물 없이 검증 불가능한 것 (정직 목록)

실 S6 경로의 인식 콤보 채움(V1 — fake로는 배선까지, 실기로는 it24 Step 9 계승), U1·U2·U6(it24 승계). 자동 테스트 전부가 통과해도 증명되는 것은 "관측·시뮬레이션 입력이 이렇게 들어오면 화면은 이렇게 말하고, 촬영은 이렇게 접촉한다"까지다.

---

## §13 `docs/analysis/` 갱신 지점

| 문서 | 절 | 갱신 내용 |
|---|---|---|
| `11-exe-app-features.md` | §11 설정 화면 | 프린터: 열거·선택 서술 → "추후 지원 예정 placeholder(잔존 키 보존)" 환원. 모델 콤보 → 인식된 카메라 콤보(빈 목록 = 선택안함) + 지원 카메라 오버레이 |
| `11-exe-app-features.md` | §20 역할별 테스트 모드 | 외부 카메라 시뮬레이션 2키·표시 전용 경계(촬영 무접촉)·W38 표식 요약 |
| `12-exe-app-settings-and-config.md` | §1 AppSettings 표 | `PhotoPrinterEnabled` 서술 환원("예약 플래그·UI 편집 불가"), `PhotoPrinterName` 행에 "잔존 키(it25 — UI 없음, 값 보존)" 주기 |
| `12-exe-app-settings-and-config.md` | §7 `[Test]` 섹션 | 키 표에 `ExternalCamera`·`ExternalCameraType` 2행 + 폴백 규칙 + TS1~TS4 요약 + ini 샘플 갱신 |
| `13-client-behavior-spec.md` | 설정·외부 장치 관련 절(해당 시) | 인식 콤보 의미·프린터 환원 정합화. 폐기 표시는 스펙 폐기 관례(번호 재배열 금지·이력 보존) 준수 |
| `10-exe-app-architecture.md` | DI 표 | `IPrinterEnumerator` 행에 "소비자 0 스캐폴드(it25)" 주기 |

it24 설계 문서는 **수정하지 않는다** — §7(프린터)·§6(지원 모델 표시)은 본 문서 §4·§6이 대체함을 여기(it25)에만 기록한다(스펙 폐기 관례: 소급 편집 금지).

---

## §14 구현 WBS (템플릿: `docs/templates/WBS_BLUEPRINT.md`)

> 검증된 사실 = §1(F1~F16), 미검증 가정 = §2(V1·V2 + it24 승계 U1·U2·U6 — 전부 단계 매핑).
> **Step 1~7 전부 장비·SDK 없이 완료 가능**하다(이번 요구의 목적 그 자체). 장비가 필요한 것은 실 S6 인식 실측(V1)뿐이며 이는 it24 Step 9(실물 단계)에 편입되고 **이번 WBS에 없다.**
> 공통 검증 명령: `build-verify` 스킬(없으면 `dotnet build MCPhoto.sln` + `dotnet test tests/MCPhoto.Tests`).
> 병렬성: Step 1은 독립. Step 2 → 3 → (4 → 5) → 6 → 7 순차. Step 2·3은 Step 1과 병렬 가능.

### Step 1: A부 — 프린터 표면 환원 + 스캐폴드 표식
- **Context Brief**: it24가 연 프린터 열거·선택·저장 UI를 사용자 지시로 "추후 제공" placeholder로 되돌린다(§4). 열거자 코드는 삭제하지 않고 의도된 스캐폴드로 남긴다(리포 관례: `IPhotoPrinter`). ini 2키는 라운드트립 보존 — `WriteFrom`에서 키를 빼면 기존 값이 첫 저장에서 소멸하므로 `AppSettings`/`IniSettingsService`는 무접촉.
- **대상 파일**: `src/MCPhoto.App/ViewModels/SettingsViewModel.cs`(§8.2 제거 목록 + Load/Save/OnEnter 정리), `src/MCPhoto.App/Views/SettingsView.xaml`(§8.1 프린터 diff), `src/MCPhoto.Core/Devices/IPrinterEnumerator.cs`·`src/MCPhoto.App/Services/SystemPrinterEnumerator.cs`(스캐폴드 주석)·`ServiceRegistration.cs`(주석), `tests/MCPhoto.Tests/SettingsViewModelDiscoveryTests.cs`(§12.3 프린터 계열 재작성 → T-A1' 및 열거자 단위 테스트 이관), `XamlResourceTests.cs`
- **선행 조건**: 없음
- **구현 내용**: §4.1 표 그대로. 프린터 토글 행: `IsEnabled="False"` + W32 캡션, 게이트 노티 제거. 하위 패널 삭제. VM 프린터 멤버 8종+상수 제거, ctor `IPrinterEnumerator?` 인자 제거.
- **검증 명령**: build-verify
- **완료 기준**: [관측] T-A1' 통과 + 재작성 테스트 전부 통과 + 기존 검색(S계열)·설정 테스트 무손상 / [non-goal] `AppSettings`·`IniSettingsService` diff 0, `SystemPrinterEnumerator` 코드 diff는 주석뿐, 외부 카메라 검색·노출 UI diff 0 / [trigger] 설정 진입·저장·토글 어느 경로도 스풀러 접촉 없음(열거자 소비자 0)
- **롤백**: 커밋 revert(후속과 독립)

### Step 2: Core — 레지스트리 스키마 확장 (Manufacturer·ModelName·TestTypeCode)
- **Context Brief**: §7.2. `DisplayName`을 파생 속성으로 유지해 기존 소비자(콤보 표시·S6 헤드라인·USB 키워드 유도)를 무영향으로 지킨다. `TestTypeCode`는 배열 인덱스 대신 행에 박는 명시 매핑(§5.2 — it7 B9 교훈).
- **대상 파일**: `src/MCPhoto.Core/Devices/ExternalCameraModels.cs`(record 확장 + 표 재작성 + `FindByTestType`), 레지스트리 위치 인자 생성 지점(컴파일 에러로 전수 특정 — V2), 테스트(T-R1'·T-B3)
- **선행 조건**: 없음(Step 1과 병렬 가능)
- **구현 내용**: §7.2 코드 블록 + §5.2 조회 함수. `Find`/`Resolve`/`Default` 시그니처 불변.
- **검증 명령**: build-verify
- **완료 기준**: [관측] T-R1'·T-B3 통과, `DisplayName=="Nikon D5300"` 단정으로 파생 호환 증명, 전체 테스트 무손상 / [non-goal] `Id`·`Md3FileName` 값 불변(ini 호환), `SdkRuntimeProbe`·`SettingsViewModel` diff 0 / [trigger] 없음(순수 타입)
- **롤백**: 커밋 revert

### Step 3: Core — `[Test]` 2키 파싱 + 시뮬레이션 계획 순수 함수
- **Context Brief**: §5.1·§5.4. 기존 `[Test]` 규약(순수 `FromIni`·Warnings 반환·기본값 폴백) 위에 2키를 얹는다. `ExternalCameraSimulation.Plan`은 관측 입력만 만든다 — 판정(`Judge`)·문구는 기존 파이프라인 재사용이 요점.
- **대상 파일**: `src/MCPhoto.Core/Settings/TestModeOptions.cs`(record 2필드 + `Disabled` + `FromIni`), `src/MCPhoto.Core/Devices/ExternalCameraSimulation.cs`(신규), 테스트(T-B1·T-B2·T-B4)
- **선행 조건**: Step 2(`FindByTestType`)
- **구현 내용**: §9.2·§5.4 코드 블록 그대로. 경고 문구는 기존 Warning 서식("[Test] … 값을 알 수 없습니다(…) — …")과 동형.
- **검증 명령**: build-verify
- **완료 기준**: [관측] T-B1·T-B2·T-B4 통과, 기존 TestMode 테스트 무손상 / [non-goal] `TestModeService`·`ITestModeService`·배너·PIN·QR 주입 경로 diff 0, `[Test]` 쓰기 없음(TM5) / [trigger] `Plan`은 I/O·로그 없음(순수)
- **롤백**: 커밋 revert

### Step 4: VM — 인식 콤보 전환 (실경로)
- **Context Brief**: §6. 콤보 선택과 ini 미러의 분리(§6.3)가 핵 — 직접 바인딩하면 빈 목록에서 WPF가 SelectedValue를 null로 되써 저장값이 소멸한다(it24 P5·it7 B9 계열 함정). 문구 조립은 `ApplyDiscoveryResult` 1곳 유지.
- **대상 파일**: `src/MCPhoto.App/ViewModels/SettingsViewModel.cs`(`RecognizedCameraOptions`/`RecognizedCameraSelection`/`RecognizedCameraOption`·`ApplyDiscoveryResult` 확장·`ExternalCameraModelOptions` 제거), 테스트(T-C1~T-C4, §12.3의 S계열 확장·모델 콤보 테스트 재작성)
- **선행 조건**: Step 2
- **구현 내용**: §6.2~§6.4 그대로. S6에서만 인식 행 추가(실경로 = `Resolve(ExternalCameraModel)` 행), 그 외 sentinel 초기화. 라벨·캡션 문구는 Step 6에서 XAML에 배치되므로 이 단계는 VM·테스트만.
- **검증 명령**: build-verify
- **완료 기준**: [관측] T-C1~T-C4 통과, 검색 S계열 기존 테스트(확장분 포함) 통과 / [non-goal] `ExternalCameraModel` 저장 게이트·노출 3요소·`ModelKeywords` diff 없음(ini 미러 기준 동작 불변), `ExternalDiscoveryJudge`·문구 상수 W15~W23 무변경 / [trigger] 어떤 검색 상태도 `ExternalCameraModel`을 자동 변경하지 않음(T-C3·T-C4가 잠금)
- **롤백**: 커밋 revert

### Step 5: VM — 시뮬레이션 배선 + 배너 접미 + 봉인 테스트
- **Context Brief**: §5.5·§5.6. 분기는 검색 시퀀스 선두 1곳(TS1), 게이트는 `IsTestUser`(TS2). 시뮬레이션 활성 시 관측 I/O 전부 생략 + W38 라인(TS4). 배너 접미(W40)는 **테스트 계정 로그인 분기에만** — 다른 분기에 붙이면 거짓 배너다. 촬영·모달은 무접촉 — T-B7·T-B8이 봉인의 회귀 게이트다.
- **대상 파일**: `src/MCPhoto.App/ViewModels/SettingsViewModel.cs`(ctor `ITestModeService? testMode = null` + `DiscoverExternalCameraAsync` 분기 + W38 상수), `src/MCPhoto.App/ViewModels/AppShellViewModel.cs`(배너 조립 분기에 W40 — §5.6), 테스트(T-B5~T-B9)
- **선행 조건**: Step 3·4
- **구현 내용**: §5.5 코드 블록 + §5.6 표 그대로. 시뮬레이션 S6의 인식 행 = `plan.Model`. 배터리 라인 없음(§5.4).
- **검증 명령**: build-verify
- **완료 기준**: [관측] T-B5~T-B9 통과 / [non-goal] `CaptureViewModel`·`CameraTestViewModel`·`ServiceRegistration`의 `IExternalCamera` 배선 diff 0, `IExternalCamera`·`INikonSdkShim` 계약 diff 0, 배너의 로그아웃·실계정 분기 문구 diff 0(W40은 테스트 계정 분기 한정) / [trigger] 시뮬레이션 분기 조건에 `IsTestUser` 외 어떤 술어도 단독으로 쓰이지 않음(코드 리뷰 체크 + T-B8), 배너 접미 판정에 `IsEnabled` 단독 사용 금지(T-B9)
- **롤백**: 커밋 revert

### Step 6: XAML — 인식 콤보·지원 카메라 오버레이
- **Context Brief**: §8.1 diff + §7.3 오버레이. 신규 리소스 키 0, 별도 Window 금지(F14), 닫기 버튼 하단 중앙 액션 바 + `Margin="8,0"`(F13), 라벨 글리프 금지.
- **대상 파일**: `src/MCPhoto.App/Views/SettingsView.xaml`, `SettingsViewModel.cs`(오버레이 멤버 — `IsSupportedCameraListOpen`·커맨드·`SupportedCameraGroups`), 테스트(T-C5·T-X1'')
- **선행 조건**: Step 4·5(바인딩 대상 멤버)
- **구현 내용**: §8.1 스케치 + W32~W39 배치. 오버레이는 라이선스 고지 오버레이의 컨테이너 구조(스크림+중앙 카드+Visibility)를 복제.
- **검증 명령**: build-verify + 앱 기동 스모크: ① 게스트/로그인 설정 진입(콤보 sentinel·W33) ② `[Test]` 시뮬레이션 ini로 [장치 검색] → S6+W38+콤보 채움 확인 ③ [지원 카메라 목록] 열기/닫기 ④ 프린터 행이 Disable+W32
- **완료 기준**: [관측] T-C5·T-X1'' 통과 + 스모크 4항 육안 확인 / [non-goal] 다른 섹션 diff 없음, `grep x:Key` diff 0(신규 리소스 키 0) / [trigger] 오버레이 열림이 어떤 장치·파일 I/O도 유발하지 않음(정적 데이터)
- **롤백**: 커밋 revert

### Step 7: 문서 동기화 — `docs/analysis/`
- **Context Brief**: §13 표. 기능·구성 변경은 분석 문서와 함께 갱신하는 리포 관례(analysis-docs).
- **대상 파일**: `docs/analysis/11-exe-app-features.md`(§11·§20), `12-exe-app-settings-and-config.md`(§1·§7), `13-client-behavior-spec.md`(해당 절), `10-exe-app-architecture.md`(DI 표)
- **선행 조건**: Step 6
- **구현 내용**: §13 표 그대로. 폐기 표시는 스펙 폐기 관례(이력 보존·번호 재배열 금지) 준수.
- **검증 명령**: 문서 diff 육안 + 갱신 절에 it25 표기
- **완료 기준**: [관측] §13 표의 문서 갱신 커밋 / [non-goal] it23·it24 설계 문서 무수정 / [trigger] 없음
- **롤백**: 문서 revert

---

## §15 리스크와 명시적 비목표

### 15.1 리스크

| 리스크 | 완화 |
|---|---|
| QA가 시뮬레이션 S6 스크린샷을 실기 검증 증거로 오인 | W38 라인이 결과 안에 상시 포함(TS4) + 전역 배너(TM4). 두 표식이 동시에 사라질 수 없다 |
| 시뮬레이션 ini(`[Test] ExternalCamera=1`)가 배포물에 섞여 나감 | 신규 위험이 아니다 — B부 M8(배포 체크리스트: publish 폴더 ini에 `[Test]` 부재 확인)이 그대로 포괄한다. 섞여도 서버 권한 0(TM1)·촬영 무영향(TS1)·배너 발각 |
| 인식 콤보가 실경로에서 "항상 비어 보인다"는 인상(SDK 미동봉 기본) | 의도된 정직함(§6.1) — W33이 이유([장치 검색]으로 확인)와 대안([지원 카메라 목록])을 함께 안내. S2 사유(W10)가 SDK 배치라는 다음 행동을 말한다 |
| 프린터 기능을 쓰던(켜 봤던) 운영자가 콤보 소실을 회귀로 인식 | it24는 실인쇄가 없는 준비 표면이었다(W25 상시 고지). 값은 보존되며(§4.3) 인쇄 이터레이션에서 되살아난다 — analysis 12 §1에 잔존 키 주기로 기록 |
| record 스키마 확장이 숨은 위치 인자 생성 지점을 놓침 | 컴파일 에러로 전수 표면화(V2) — 조용한 오동작 형태가 없다 |
| 다중 모델 시대에 `TestTypeCode` 중복 배정 | T-B3(유일성)이 표 수정 즉시 잡는다 |

### 15.2 명시적 비목표

| 항목 | 왜 비목표인가 |
|---|---|
| **시뮬레이션의 촬영·테스트 모달 확장**(가짜 프리뷰·가짜 셔터·가짜 스틸) | §0.2 판정 그 자체 — 표시 표면 밖의 시뮬레이션은 거짓 산출물을 만든다. 실기 검증은 실기로만 |
| 배터리·capability 수치 시뮬레이션 | 최소 시뮬레이션 원칙(§5.4) — 콤보·토글·문구 확인이라는 요구에 불요 |
| 배치된 md3 전수 순회 프로브(다중 모델 인식) | §6.5 — 모델이 실재할 때의 별도 이터레이션. 지금 설계하면 미검증 가정 위에 짓는다 |
| WMI 감지 항목의 콤보 등재 | §6.1 판정 ② 기각 사유 — 저장 표면에 불확실 항목 금지 |
| 실제 인쇄·프린터 표면의 재도입 | 사용자 지시("추후 제공"). 스캐폴드(§4.2)가 재도입 비용을 최소화해 둔다 |
| `[Test]` 키의 런타임 재읽기(앱 재시작 없는 시뮬레이션 토글) | `TestModeService` 1회 캐시 방침(B5) 그대로 — 재시작이 정직하다 |
| 지원 카메라 오버레이의 상세 스펙(센서·마운트 등) 표시 | 요구는 제조사·제품명 정리까지. 레지스트리에 없는 정보를 지어내지 않는다 |

---

## §16 완결성 게이트 (developer 전달 전 자체 검사)

- [x] 검증된 사실(§1 F1~F16) / 미검증 가정(§2 V1·V2 + it24 승계 U1·U2·U6) 분리 — 가정 전부 단계 매핑
- [x] 시뮬레이션 경계 판정(§0.2)과 근거(§3.2·§5.5) 명시 — 판정 단일 지점 + 불변식 TS1~TS4 + 회귀 테스트(T-B5~T-B8)
- [x] it23·it24 원칙(R1~R5·TM1~TM5·상태 전수표) 전수 점검(§3.1) — 깨지는 것 없음, W24는 의미 변화로 폐기·대체
- [x] 모든 Step에 7필드 기재, 완료 기준 관측 기반 3문 형식(UI 단계 Step 1·6은 non-goal·trigger 포함)
- [x] 검증 명령 자동 실행 가능(build-verify) — **장비 필요 단계 0**(실기 확인은 it24 Step 9로 격리)
- [x] View 변경에 대응 VM 멤버 전수 명세(§8.2 — 바인딩 누락 없음, 제거 멤버까지 열거)
- [x] 이벤트 구독 신설 0(§11) — 해제 경로 논의 불요, 프린터 훅 제거로 순감소
- [x] 신규 리소스 키 0(§8.1) — 병합 딕셔너리 교차 참조·키 충돌 원천 회피, 별도 Window 금지(F14)
- [x] 실패 전수표(§10 E21~E28) — 크래시·무한 대기·거짓 단정·저장값 클로버 경로 없음
- [x] 깨지는 기존 테스트 전수 목록 + 각각 재작성 방향(§12.3) — 삭제로 회귀 감시가 사라지는 항목 없음
- [x] ini 키 계약 안정성: `[MCPhoto]` 무변경, `[Test]` 2키는 오타 교정된 이름으로 신설, int 매핑은 행 순서 무관(§5.2)
- [x] 파일 인코딩 규칙 명시(§11)

**미해결 확인 사항 (구현 착수 전 사용자 확인 권장)**:

- **USER-DECISION 1 (§6.3)**: 인식 목록이 비는 동안 구성된 모델 Id(`ExternalCameraModel`)가 화면에 보이지 않는다 — 현 레지스트리 1행(기본값과 동일)이라 정보 손실이 없다고 판정했다. 구성값을 항상 보여야 한다면 캡션 1줄 추가로 끝난다(구조 무변경).
- **USER-DECISION 2 (§5.4)**: 시뮬레이션 S6에 배터리 수치를 표시하지 않는다(날조 최소화). QA가 배터리 라인(W21b)까지 화면에서 봐야 한다면 `[Test]` 키 1개(`ExternalCameraBattery`) 추가로 대응 가능하다.
- **USER-DECISION 3 (§7.4)**: [지원 카메라 목록] 버튼을 외부 카메라 하위 패널 안(토글 on일 때만)에 뒀다. 토글 off 상태에서도 지원 목록을 열람하게 하려면 버튼을 토글 행 옆으로 올리면 된다(오버레이 구조 무변경).
