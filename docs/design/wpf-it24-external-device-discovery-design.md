# it24 설계 — 설정 · 외부 장치 섹션 재설계 (가시성 · 장치 탐색 · 지원 모델 · 프린터 열거)

> 작성: wpf-architect · 2026-08-11
> 파이프라인: wpf-architect → wpf-developer → wpf-code-reviewer
> 상태: 설계 초안 (실물 DSLR·Nikon SDK **없이** 작성 — §2 미검증 표가 이 문서의 전제)
> 선행 문서: `docs/design/wpf-it23-external-camera-nikon-design.md`(rev2) — 경계·강등·문구 관례를 계승한다

## §0 개요

### 0.1 요구사항 원문 (사용자 피드백, 축약 금지)

> "기존에 있었던 외부 장치 연결 탭이 사라졌어. 카메라(DSLR), 프린터 여기에 추후 연결로 강제로 비활성화 되어 있던 설정을 켜고, 가능한 장치에 대해 찾아보고 연결 가능한 장치가 없습니다를 노출하거나, 연결 가능한 장치에 대한 나열이 있을 줄 알았는데, 너는 어떤 방식으로 개발을 진행한건지 모르겠어. 테스트 화면에도 표시가 되는 것처럼, 외부 장치에 대해서도 지원하는 SDK를 사용하여 개발한 경우는 별도로 어떤 모델이 가능한지정도는 표시가 되었으면 좋겠어."

요구 분해:

1. **가시성**: 외부 장치 섹션이 보여야 한다("탭이 사라졌어" — 원인은 §1 F1의 게스트 Collapsed 게이트).
2. **탐색**: 장치를 실제로 찾아보는 액션이 있어야 한다.
3. **정직한 결과 노출**: 찾은 장치의 나열, 또는 "연결 가능한 장치가 없습니다".
4. **지원 모델 표시**: SDK 기반 연동은 "어떤 모델이 가능한지"를 표시(테스트 모달의 정보 패널처럼).

### 0.2 이 설계의 최우선 제약 — 두 명제를 섞지 않는다

실물 DSLR도 Nikon SDK 실물도 여전히 없다(it23과 동일 조건). 이 상태에서 요구 3을 문자 그대로 구현하면 **거짓말하는 UI**가 된다:

> **"연결 가능한 장치가 없습니다"** (장치 부재의 단정)와 **"장치 연결 여부를 확인할 수 없습니다"** (판정 능력의 부재)는 다른 명제다. SDK가 없으면 카메라가 꽂혀 있어도 SDK 경로로는 그 존재를 알 수 없다 — 이때 전자를 표시하면 운영자가 케이블·전원을 헛되이 점검한다.

따라서 이 문서의 중심 산출물은 화면 목업이 아니라 **상태 전수표**(§5.3, §7.3)다: 어떤 관측 조합에서 어떤 명제를 말할 수 있는지를 먼저 확정하고, UI는 그 명제를 그대로 표시한다. 표기 규약은 it23 §0.2와 동일하다 — (실측) / (공개 근거) / ⚠️ 미검증.

### 0.3 판정 요약

| 쟁점 | 판정 | 왜 |
|---|---|---|
| A. 섹션 가시성 | 게스트 Collapsed **폐지** → 항상 표시 + 읽기 전용(편집 게이트 유지) (§4) | 같은 화면의 다른 로그인 전용 항목은 전부 "보이되 Disable + '로그인 필요' 노티"다(F2). 섹션째 숨김은 이 섹션만의 예외였고, 그 예외가 "탭이 사라졌다"는 사용자 혼란의 직접 원인이다 |
| B. 탐색 방식 | **명시 버튼 [장치 검색]**(자동 스캔 없음) + 3원 관측(SDK 전제 검사 · SDK 연결 시도 · WMI USB 관찰) → 순수 판정 함수 → 상태 S0~S7 (§5) | it23 §9.1의 "설정 진입만으로 USB를 건드리지 않는다"를 유지하면서 "찾아보기" 요구를 충족. 관측과 판정을 분리해야 headless 테스트가 가능하다 |
| B-2. "없습니다"의 자격 | **SDK 제어 스택이 갖춰졌을 때만** "찾지 못했습니다"를 말한다. SDK 미비 시엔 "확인할 수 없습니다" + 사유 (§3) | MissingShim 상태(현 프로덕션)에서 장치 유무는 판정 불가 — 단정은 거짓 |
| B-3. USB 관찰 | WMI `Win32_PnPEntity`(PNPClass WPD/Camera/Image) best-effort — **양성 신호로만 사용**(감지 실패는 무의미) (§5.1) | Nikon 바디는 제네릭 "MTP Portable Device"로 뜰 수 있어(WEB1) 이름 매칭이 미스날 수 있다. 관찰 실패로 "없음"을 강화하면 안 된다 |
| C. 지원 모델 표시 | 기존 모델 ComboBox(레지스트리) 유지 + **"지원 모델 ≠ 연결된 장치" 구분 캡션** 신설 (§6) | 레지스트리(D5300 1행)가 이미 지원 모델의 단일 진실. 표시 형태보다 오해 방지 문구가 본질 |
| D. 프린터 | **(b) Windows 설치 프린터 열거 + 선택 + 저장**까지. 실제 인쇄는 명시적 비목표. 미인쇄 상태 고지 문구 필수 (§7) | "찾아보고 나열" 요구에 부합하는 최소 정직 범위. (a)는 요구 미충족, (c)는 별도 이터레이션 규모 |
| D-2. 프린터 열거 API | `System.Printing`(`LocalPrintServer.GetPrintQueues`) — WPF 동반 어셈블리, 추가 패키지 없음(L2) (§7.2) | 미래 인쇄 구현이 쓸 스택과 동일 — 지금 열거를 같은 API로 하면 재작업이 없다. 실패(스풀러 중지)는 "없습니다"와 구분되는 P4 상태 |
| 검색 후 연결 | 검색은 **순간 관찰** — 성공해도 즉시 `DisconnectAsync`(연결 잔류 금지) (§5.5) | 설정 화면이 USB를 점유한 채 방치되는 상태를 만들지 않는다. 재연결 비용은 촬영/테스트 모달 진입 시 지불 |

---

## §1 검증된 사실 (verified facts)

### 1.1 리포 실측 (전부 코드 직접 확인)

| # | 사실 | 근거 |
|---|---|---|
| F1 | "탭이 사라졌다"의 원인: 외부 장치 섹션 전체가 `Visibility="{Binding IsLoggedIn, …BoolToVis}"`로 묶여 **게스트에게 Collapsed**. it23이 만든 게 아니라 계승한 게이트다 | `src/MCPhoto.App/Views/SettingsView.xaml:374` |
| F2 | 같은 화면의 다른 로그인 전용 항목(거울모드·재촬영·QR·필터)은 전부 **"보이되 Disable + `GuestGateNote`('로그인 필요') 인라인"** 패턴. `GuestGateNote` 스타일은 `IsGuest` DataTrigger로 게스트일 때만 Visible — 재사용 가능 | `SettingsView.xaml:32-45,122-128,154-158,264-267` |
| F3 | 외부 카메라 토글은 it23에서 실배선됨: `IsEnabled="{Binding CanEditExternalCamera}"`(User 이상·TempUser 제외), TempUser에겐 ini 원값 표시 + "권한 없음" 캡션. 프린터 토글은 `IsEnabled="False"` + "추후 지원 예정" placeholder | `SettingsView.xaml:391-398,479-486`, `SettingsViewModel.cs:114-115,332-337` |
| F4 | 프린터 저장은 현재 `if (!IsGuest) s.PhotoPrinterEnabled = …`(게스트 게이트) — 외부 카메라 5필드의 `CanEditExternalCamera` 게이트와 **다른 블록**이다. 단 UI가 Disable이라 TempUser도 값을 바꿀 수 없었으므로 게이트 통일 시 행동 회귀는 없다 | `SettingsViewModel.cs:455-465` |
| F5 | `IPhotoPrinter`/`NullPhotoPrinter`는 item3 스캐폴드(항상 미지원·no-op) — DI Singleton. **이 계약은 "인쇄"용이지 "열거"용이 아니다** | `src/MCPhoto.Core/Devices/IPhotoPrinter.cs`, `NullPhotoPrinter.cs` |
| F6 | Nikon 연결 경로: `ConnectAsync` → ① `SdkRuntimeProbe`(md3 파일 존재) 선행, 부재면 shim 미호출 강등 → ② `shim.OpenAsync`. 프로덕션 shim은 `MissingNikonSdkShim`(항상 `(false, W10)`) — **파일을 수동 배치해도 연결은 실패**한다(shim 미구현) | `src/MCPhoto.Devices.Nikon/NikonExternalCamera.cs:135-151`, `SdkRuntimeProbe.cs:49-69` |
| F7 | 사유 문구는 상수 집약: `NikonCameraReasons.SdkMissing`(W10)·`NotConnected`(W12)·`ModuleFileMissing`(W11). `NikonSdkShim.cs` **파일 부재가 정상**(SDK 도착 신호) | `src/MCPhoto.Devices.Nikon/NikonCameraReasons.cs`, 디렉터리 실측 |
| F8 | 웹캠 열거 선례: 설정 진입 시 자동 + `Task.Run`(0~7 open/close 수백 ms) + 빈 목록이면 Disable + 안내. 동작 기준은 인덱스, WMI FriendlyName은 best-effort 라벨 | `SettingsViewModel.cs:259-264`, `OpenCvCameraService.cs:308-334` |
| F9 | WMI 사용 선례: `CameraNameProbe` — `Win32_PnPEntity WHERE PNPClass='Camera' OR 'Image'`, **I/O와 순수 매핑 분리**, 실패 시 예외 없이 빈 목록 | `src/MCPhoto.Capture/CameraNameProbe.cs:24-47,60-94` |
| F10 | `System.Management` 8.0.0은 `MCPhoto.Capture`에 이미 참조됨 | `MCPhoto.Capture.csproj:13` |
| F11 | 사용자가 말한 "테스트 화면"의 실체: 카메라 테스트 모달의 외부 카메라 정보 패널(모델명·배터리·capability 요약 라인) — it23 §9.3 | `CameraTestViewModel.cs:70-77` |
| F12 | 지원 모델의 단일 진실: `ExternalCameraModels.All`(현재 `NikonD5300`/"Nikon D5300"/`Type0011.md3` 1행). 설정 ComboBox가 이미 이 목록을 표시(SelectedValue 값 기반) | `src/MCPhoto.Core/Devices/ExternalCameraModels.cs`, `SettingsView.xaml:412-416` |
| F13 | `DisconnectAsync`는 재연결 가능 상태로 복귀(shim Close만, Dispose는 앱 종료 1회) — 검색의 "연결→관찰→해제" 패턴에 그대로 쓸 수 있다 | `NikonExternalCamera.cs:472-487` |
| F14 | `ExternalCameraEnabled=false`면 촬영 세션이 외부 카메라를 **한 번도 접촉하지 않는다**(회귀 0 — 테스트가 호출 0회 고정) | `docs/analysis/11-exe-app-features.md:150` |
| F15 | .cs는 UTF-8 no BOM(한글 주석 포함), 설계 문서와 XAML은 기존 인코딩 유지 | agent-memory `source-file-encoding` |

### 1.2 이 개발 머신 실측 (2026-08-11, 장비 없이 확인된 것)

| # | 사실 | 근거 |
|---|---|---|
| L1 | `Win32_PnPEntity WHERE PNPClass='WPD'` 쿼리는 유효하며 이 머신에서 2개 장치 반환("새 볼륨", "PRIVATE"). 즉 **'WPD'는 실재하는 PNPClass 값**이고, **카메라가 아닌 장치(폰 저장소 등)도 WPD로 열거된다** — 이름만으로 카메라를 단정할 수 없음이 실측으로 확인됨 | PowerShell `Get-CimInstance` 실행 결과 |
| L2 | `Microsoft.WindowsDesktop.App.Ref` 8.0.24/8.0.25 참조팩에 `System.Printing.dll`·`ReachFramework.dll` 동봉 — net8.0-windows에서 **PackageReference 추가 없이** 참조 가능(최종 확인은 Step 5 빌드) | `C:\Program Files\dotnet\packs\…\ref\net8.0\` 실측 |

### 1.3 공개 웹 근거

| # | 근거 | 출처 |
|---|---|---|
| WEB1 | Nikon 바디(Z 6 사례)는 Windows 장치 관리자 "Portable Devices" 하위에 뜨되, 이름이 모델명("Nikon Z 6")이 아닌 **제네릭 "MTP Portable Device"로 표시될 수 있다.** Nikon 지원 답변 인용: 현대 Nikon 카메라는 전용 드라이버 없이 표준 PnP(MTP) 드라이버를 쓴다 | [Microsoft Q&A — Nikon camera showing up as MTP Portable Device](https://learn.microsoft.com/en-us/answers/questions/3833641/nikon-camera-showing-up-as-mtp-portable-device-sof) |
| WEB2 | Windows Portable Devices 장치 설치 클래스: 클래스명 **WPD**, GUID `{eec5ad98-8080-425f-922a-dabf3de3f69a}`, Vista 이후 | [System-Defined Device Setup Classes (Windows drivers)](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/system-defined-device-setup-classes-available-to-vendors) |
| WEB3 | `LocalPrintServer`(System.Printing.dll)는 windowsdesktop-8.0 대상 문서가 존재(= .NET 8 WPF에서 지원). `GetPrintQueues()`가 로컬 프린트 서버의 큐 컬렉션을 반환, `DefaultPrintQueue` 제공. Caution: System.Printing은 **Windows 서비스·ASP.NET에서 미지원**(데스크톱 앱은 해당 없음) | [LocalPrintServer Class — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/system.printing.localprintserver?view=windowsdesktop-8.0) |
| WEB4 | PTP는 이미지 전송 표준 프로토콜, MTP는 그 확장 — Nikon 카메라의 PC 연결 모드 | [Nikon — MSC/PTP transfer protocols](https://www.nikonimgsupport.com/na/NSG_article?articleNo=000025693&configured=1&lang=en_SG), [Wikipedia — Media Transfer Protocol](https://en.wikipedia.org/wiki/Media_Transfer_Protocol) |

---

## §2 ⚠️ 미검증 가정 전수표 (open assumptions)

전 항목이 "거짓이어도 크래시·거짓 표시 없음"으로 처리되어 있다. 특히 팀 지시의 **추정 금지 영역**("SDK 없이 USB로 연결된 Nikon 바디를 열거할 수 있는가")은 U1·U2로 분해했고, 둘 다 **"미확인"으로 남긴다** — 설계는 관찰 성공을 전제하지 않는다.

| # | 미검증 가정 | 거짓이면 생기는 일 | 설계상 처리 | 검증 방법 |
|---|---|---|---|---|
| U1 | D5300(PTP 모드)이 USB 연결 시 `Win32_PnPEntity`에 **관찰되기는 하는가**(어느 PNPClass로든). WEB1은 Z 6 + 장치 관리자 사례이며 D5300 + WMI 직접 근거가 아니다 | S3/S5(USB 감지 라인)가 영영 안 뜸 — S2/S4로 축퇴 | USB 관찰은 **양성 신호로만** 사용. 미관찰은 어떤 단정도 강화하지 않는다(§3 R3) | Step 9 실기: D5300 연결 후 `Get-CimInstance` 실측 |
| U2 | 관찰될 때 이름에 "Nikon"/"D5300" 키워드가 포함되는가 — WEB1은 **반증 사례**(제네릭 "MTP Portable Device"), L1도 임의 이름("새 볼륨") 실측 | 키워드 매칭 미스 → S3/S5 대신 S2/S4 + 참고 라인(W23)으로만 표시 | 매칭 미스 허용 설계 + **W23 참고 라인**(비매칭 휴대용 장치 원문 나열)이 운영자의 육안 판단을 보조 | Step 9 실기(이름 실측 후 키워드 보강) |
| U3 | net8.0-windows + `UseWPF=true`가 `System.Printing`을 **자동 참조**하는가(참조팩 동봉은 L2로 확인 — 프로필 포함 여부만 잔여) | 빌드 에러 → `<Reference Include="System.Printing" />` 또는 FrameworkReference 명시 1줄 | Step 5 첫 빌드가 즉시 판정 — 코드 구조 무영향 | Step 5 `dotnet build` |
| U4 | Print Spooler 서비스 중지 시 `LocalPrintServer` 생성/열거가 예외를 던진다(구체 타입 미확정 — `PrintSystemException` 계열 추정) | 없음 — catch-all이라 어느 타입이든 P4로 강등 | 열거자는 전 예외를 잡아 `Succeeded=false` 반환(P4). **P4와 P2("없습니다")는 구조적으로 분리** | Step 5 완료 기준: 개발 머신에서 스풀러 중지 실험(장비 불필요) |
| U5 | `GetPrintQueues`(Connections 포함) 소요 시간 — 네트워크 프린터 다수 환경에서 수 초 가능(추정) | UI 지연 없음(Task.Run 격리). 열거 완료까지 P1 표시 | 웹캠 열거와 동형의 백그라운드 패턴(F8) | Step 5 실측 로그 |
| U6 | SDK 실구현(`NikonSdkShim`) 후 `OpenAsync` 실패가 "모듈 로드 실패"와 "장치 부재"를 **사유 문구로 구분**해 줄 것 | S4 헤드라인이 모듈 실패 코너에서 부정확 → 상세 라인(사유 원문)이 보정 | 헤드라인을 단정 완화형("찾지 못했습니다")으로 채택(§3 R2) + shim 계약 주석에 구분 의무 명기 | it23 §15-C4(shim 구현 시점) |
| U7 | 프린터 목록의 `IsDefault` 판정(`DefaultPrintQueue` 비교)이 항상 유효 — 기본 프린터 미설정 머신에서 null 가능 | "(기본)" 접미 누락뿐 — 기능 무영향 | null 가드(접미 생략) | Step 5 실기 |

**가정 매핑 완결성**: U1~U7 전부 검증 단계 매핑됨. Step 1~8은 U1·U2·U6이 전부 거짓이어도 정상 동작한다(관찰·구분은 보너스 신호). U3~U5·U7은 Step 5에서 장비 없이 해소된다.

---

## §3 명제 구분 원칙 — 이 설계의 핵

모든 표시 문구는 아래 규칙을 통과해야 한다. §5.3·§7.3 전수표의 각 행이 이 규칙의 적용 결과다.

| # | 규칙 | 근거 |
|---|---|---|
| R1 | **"없습니다"(부재 단정)는 부재를 판정할 능력이 있을 때만 말한다.** SDK 제어 스택 미비(MissingShim·md3 부재) 상태에서는 "장치 연결 여부를 확인할 수 없습니다" + 사유(W10/W11)를 말한다 | F6: 현 프로덕션은 파일을 배치해도 shim이 Missing이라 연결이 항상 실패 — 이 상태의 실패는 장치 부재의 증거가 아니다 |
| R2 | 부재 단정도 **단정 완화형**으로 쓴다: "연결 가능한 장치를 찾지 못했습니다". SDK가 정상이어도 "찾기 실패"와 "부재"는 엄밀히 다르고(U6 코너), "찾지 못했다"는 어느 경우에도 참이다 | 사용자 요구 문구("연결 가능한 장치가 없습니다")의 의도를 보존하면서 오단정을 제거 |
| R3 | USB 관찰(WMI)은 **양성일 때만 의미**가 있다: "감지되었습니다"는 말해도 되지만, 미감지를 "없음"의 근거로 쓰지 않는다 | U1·U2 — 관찰 자체가 miss될 수 있음(제네릭 이름·클래스 상이) |
| R4 | 프린터의 "없습니다"는 **"설치된 프린터가 없습니다"**로 한정한다(스풀러 DB 기준 명제 — 장치 전원·연결 상태가 아님). 열거 실패(스풀러 중지 등)는 별도 상태 P4("확인할 수 없습니다") | WEB3: PrintQueue는 스풀러 객체다. 열거 성공·0건과 열거 실패는 다른 관측이다 |
| R5 | "지원 모델"과 "연결된 장치"를 한 목록에 섞지 않는다. 지원 모델 목록에는 연결 상태를 표시하지 않고, 검색 결과에만 연결 상태를 표시한다 | 요구 4 — 사용자가 두 개념을 오해하지 않게 하는 것이 문구 설계의 목적 |

---

## §4 A. 섹션 가시성·권한

### 4.1 판정 — 게스트 Collapsed 폐지, 항상 표시 + 읽기 전용

현행(F1)은 게스트에게 섹션 자체를 숨긴다. **폐지하고 다른 로그인 전용 항목과 같은 패턴(F2)으로 통일한다**: 섹션은 항상 보이고, 편집 컨트롤만 게이트로 잠근다.

근거:

1. **일관성**: 같은 화면의 거울모드·재촬영·QR·필터가 전부 "보이되 Disable + 인라인 노티"다(F2). 섹션째 숨김은 외부 장치 섹션만의 예외였고, 예외에는 애초에 근거가 없었다(it8 시절 관성).
2. **실증된 혼란**: "탭이 사라졌다"는 피드백 자체가 증거다. 게스트 직행 흐름(it2) 때문에 운영자가 게스트 상태로 설정을 열어 보는 일은 실제로 잦다 — 숨김은 발견가능성을 해친다.
3. **비밀성 없음**: 모델명·노출값·프린터명은 비밀이 아니다. QR·Firebase 섹션도 게스트에게 보인다.
4. **안전성 불변**: 편집은 이미 3중 게이트(Load 원값 표시 / Save 미기록 / XAML IsEnabled)로 잠겨 있어, 보인다고 바뀌는 것이 없다.

### 4.2 표시 값 — 게스트에게도 ini 원값 (QR 게이트의 "강제 off"와 다른 선택)

QR 계열 게이트는 게스트에게 Load 시 강제 off를 표시하지만, 외부 장치 섹션은 it23이 TempUser에 대해 확립한 **"ini 원값 그대로 표시"**를 게스트에도 적용한다. 근거: 외부 카메라는 **편집 게이트이지 동작 게이트가 아니다**(it23 §8.2) — 관리자가 켜 둔 DSLR은 게스트 세션에서도 동작하므로, off로 보여 주는 것이 오히려 거짓 표시다. `LoadSettings`의 외부 장치 블록은 현행 그대로(강제 off 없음) 두면 된다 — **코드 변경 없이 자동 성립**.

### 4.3 게이트 3계층 정리

| 계층 | 판정 | 게스트 | TempUser | User 이상 |
|---|---|---|---|---|
| 섹션 표시 | (무조건) | 보임 | 보임 | 보임 |
| 편집 (토글·모델·노출·프린터) | `CanEditExternalCamera` = `IsLoggedIn && Role.CanConfigureExternalCamera()` (기존, 변경 없음) | 불가 | 불가 | 가능 |
| [장치 검색]·[프린터 다시 검색] | `IsLoggedIn` (진단·상태 버튼 선례, `SettingsView.xaml:530-532`) | 불가 | **가능** | 가능 |
| 촬영 세션 동작 | ini `ExternalCameraEnabled` (it23 §8.2 그대로) | 적용됨 | 적용됨 | 적용됨 |

- 검색을 `IsLoggedIn`으로 여는 이유: 검색은 상태를 바꾸지 않는 진단이고, TempUser가 부스 운영 인력일 수 있다 — 진단·상태 모달과 같은 눈높이. `CanConfigureExternalCamera`(편집)보다 넓고 게스트(익명 손님)보다 좁다.
- 인라인 노티 분화: 게스트에게는 기존 `GuestGateNote`("로그인 필요", `IsGuest` 트리거 자동)를 재사용하고, 현행 "권한 없음" 캡션(F3)은 **로그인했으나 편집 불가(TempUser)일 때만** 보이도록 조건을 좁힌다 — VM 파생 속성 `IsExternalEditDenied => IsLoggedIn && !CanEditExternalCamera`(설정 진입 중 불변, INPC 불요) 신설. 게스트에게 "권한 없음"이 뜨면 "로그인하면 되는가?"라는 질문에 답하지 못하는 문구가 된다.
- **기존 테스트 회귀 주의**: 게스트 섹션 Collapsed를 고정하던 it23 T-V3 계열 테스트는 이 결정으로 **의도적으로 깨진다** — "게스트에게 섹션 보임 + 편집 컨트롤 Disable"로 재작성한다(§12). settings-guest-edit-gate 관례의 "게이트 변경이 기존 게스트 테스트를 깨뜨린다" 경고와 동형.

---

## §5 B. 장치 탐색(discovery) — 카메라 경로

### 5.1 관측 3원과 경계 배치

검색 1회는 서로 독립적인 3가지 관측을 수행하고, 그 조합을 **Core 순수 함수**가 상태로 판정한다. 관측(I/O)과 판정(순수)을 분리해야 상태 전수표(§5.3)를 장비 없이 전수 테스트할 수 있다(F9의 I/O·순수 분리 선례와 동형).

| 관측 | 무엇을 아는가 | 구현 위치 | 신뢰도 |
|---|---|---|---|
| ① 전제 검사 `CheckReadiness()` | SDK **제어 스택**이 갖춰졌는가(shim 실구현 + md3 파일). USB 미접촉 | `IExternalCamera` 신규 멤버(아래) | 확실(로컬 파일·코드 사실) |
| ② SDK 연결 시도 `ConnectAsync()` | ①이 참일 때만 수행. SDK가 장치를 잡았는가 | 기존 멤버 그대로(F6) | SDK 기준 확실 |
| ③ USB 관찰 (WMI) | PnP 트리에 휴대용/이미징 장치가 보이는가 — **양성 신호 전용**(R3) | `MCPhoto.Capture/PortableDeviceProbe.cs` 신규 | best-effort(U1·U2) |

**① 신규 멤버 — `IExternalCamera.CheckReadiness()`**

"SDK 있음"을 파일 존재로 판정하면 안 된다: 파일을 수동 배치해도 shim이 `MissingNikonSdkShim`이면 연결은 항상 실패한다(F6). 이 상태에서 ②의 실패를 "장치 없음"으로 읽으면 거짓이다(R1). 그래서 준비도 검사는 **shim 구현 여부 + 파일 존재**를 함께 본다.

```csharp
// MCPhoto.Core/Devices/ExternalCameraTypes.cs 에 추가 (POCO — UI·SDK 무의존)
/// <summary>장치 접촉 없는 로컬 전제 검사 결과. CanControl=false면 장치 유무를 판정할 수 없다(R1).</summary>
public sealed record ExternalCameraReadiness(bool CanControl, string? Reason);

// IExternalCamera 추가 멤버 (기존 시그니처 불파괴 — it23 §3.2와 같은 방식의 멤버 추가)
/// <summary>SDK 제어 스택(shim 실구현 + 런타임 파일)이 갖춰졌는지 검사. USB·장치 미접촉, 동기(파일 존재 검사 수준).</summary>
ExternalCameraReadiness CheckReadiness();
```

- `NullExternalCamera`: `(false, "외부 카메라 미구성")` — 기존 `UnavailableReason` 문구 재사용.
- `NikonExternalCamera`: ⓐ `_shim.IsOperational == false` → `(false, NikonCameraReasons.SdkMissing)` ⓑ `SdkRuntimeProbe.Probe(model)` 실패 → `(false, W11 사유)` ⓒ 그 외 `(true, null)`.
- `INikonSdkShim`에 `bool IsOperational { get; }` 추가: `MissingNikonSdkShim`=false, (미래) `NikonSdkShim`=true, 테스트 Fake=주입값. SDK 이름이 아니므로 shim 계약 순수성(it23 §3.4) 위반이 아니다.
- 파급: 구현 2개(Null·Nikon) + 테스트 fake. 프로덕션 신규 소비자는 SettingsViewModel의 검색 커맨드 1곳.

**③ 신규 프로브 — `PortableDeviceProbe`** (CameraNameProbe와 동형: WMI I/O + 순수 매칭 분리)

```csharp
// MCPhoto.Capture/PortableDeviceProbe.cs (신규) — System.Management는 이미 참조됨(F10)
/// <summary>PnP 트리의 휴대용/이미징 장치 이름 best-effort 조회. 실패 시 예외 없이 빈 목록.</summary>
public static IReadOnlyList<string> TryGetPortableDeviceNames(ILogger? logger = null)
// WQL: SELECT Name FROM Win32_PnPEntity WHERE PNPClass='WPD' OR PNPClass='Camera' OR PNPClass='Image'
//      'WPD' = Windows Portable Devices 설치 클래스(WEB2, L1 실측). 'Camera'/'Image'는 기존 F9 관례 포괄.

/// <summary>순수 함수: 장치명에 키워드가 포함된 것만 추출(OrdinalIgnoreCase Contains).</summary>
public static IReadOnlyList<string> MatchCandidates(IReadOnlyList<string> names, IReadOnlyList<string> keywords)
```

- 키워드는 호출측(VM)이 모델 레지스트리에서 유도: `ExternalCameraModel.DisplayName.Split(' ')` → `["Nikon","D5300"]`. **레지스트리 스키마 변경 없음** — 모델 추가 = 표 한 줄 규약(it23 §3.3) 유지.
- L1 실측이 보여 주듯 WPD에는 비카메라 장치가 섞인다 → 매칭 결과만 "감지" 신호로 쓰고, **비매칭 이름은 W23 참고 라인으로 원문 나열**(최대 4개) — U2(제네릭 이름 miss) 상황에서 운영자의 육안 판단을 보조하는 유일한 수단이다.

### 5.2 검색 시퀀스 (VM 커맨드 — `DiscoverExternalCameraCommand`)

```
[장치 검색] 클릭 (단일 비행: IsDiscovering이면 무시, 버튼도 Disable)
  IsDiscovering = true, 상태 = S1
  try:
    ① readiness = _external.CheckReadiness()                  // 동기·순간
    ③ names     = await Task.Run(PortableDeviceProbe.TryGetPortableDeviceNames)   // WMI는 백그라운드
       candidates = MatchCandidates(names, 모델 키워드)
    ② connected = readiness.CanControl
                   ? await _external.ConnectAsync()            // ConnectTimeout 5s 내장(F6)
                   : false                                     // 스택 미비면 USB를 아예 건드리지 않는다
    스냅샷 채취(connected일 때): ModelName·GetCapabilitiesAsync→배터리
    connected였으면 await _external.DisconnectAsync()          // §5.5 잔류 금지
    상태 = ExternalDiscoveryJudge.Judge(readiness, candidates.Count > 0, connected)  // Core 순수(§5.3)
  catch (Exception ex): 로그 + 상태 = S7
  finally: IsDiscovering = false                               // 로딩 상태는 finally 확정(it20 교훈)
```

- 전 구간이 UI 스레드를 막지 않는다: WMI는 `Task.Run`, ConnectAsync는 내부 `ConfigureAwait(false)` + 타임아웃 내장.
- ②를 ①로 게이트하는 이유: 스택 미비 상태의 ConnectAsync는 어차피 파일 프로브에서 강등(F6)하지만, **"검색 결과가 무엇을 관측한 것인지"를 상태표에서 단순하게 유지**하려면 판정 불가 상태에서 연결 시도 기록 자체를 남기지 않는 편이 낫다.
- 검색과 카메라 테스트 모달은 동시 실행 불가(모달이 modal) — Singleton `IExternalCamera` 경합 없음(it23 §6.2와 동일 논거). 촬영 화면과는 화면 자체가 배타적(오버레이 네비게이션).

### 5.3 카메라 검색 상태 전수표 — 이 문서의 중심

판정은 Core 순수 함수 1곳:

```csharp
// MCPhoto.Core/Devices/ExternalDiscoveryJudge.cs (신규 — 순수, I/O 없음)
public enum ExternalCameraDiscoveryState
{
    NotSearched,               // S0
    Searching,                 // S1 (VM 전용 진행 상태 — Judge 출력 아님)
    UndeterminedStackMissing,  // S2: 제어 스택 미비 — 장치 유무 판정 불가
    DetectedUncontrollable,    // S3: 스택 미비 + USB에서 후보 감지
    NotFound,                  // S4: 스택 정상 + 연결 실패 + USB 후보 없음
    DetectedConnectFailed,     // S5: 스택 정상 + 연결 실패 + USB 후보 있음
    Connected,                 // S6
    SearchFailed               // S7 (예외 — VM이 직접 설정, Judge 출력 아님)
}

public static ExternalCameraDiscoveryState Judge(ExternalCameraReadiness readiness, bool usbCandidateSeen, bool connected)
```

| ID | 조건 (①CanControl / ②연결 / ③USB 매칭) | 헤드라인(동결 문구 §8.2) | 보조 표시 | 후속 액션 |
|---|---|---|---|---|
| S0 | 검색 전 | W16 "장치를 검색하지 않았습니다…" | — | [장치 검색] 활성(게이트 §4.3) |
| S1 | 검색 중 | W17 "장치 검색 중…" + `Spinner.Ring` 재사용 | — | 버튼 Disable(단일 비행) |
| S2 | ①=false / — / 매칭 0 | W18 **"장치 연결 여부를 확인할 수 없습니다"** | 사유 원문(`readiness.Reason` — W10/W11) + 참고 W23(비매칭 휴대용 장치 있으면) | [장치 검색](재시도) |
| S3 | ①=false / — / 매칭 ≥1 | W20 "USB에서 장치가 감지되었습니다: {이름들}" + W20a "SDK 모듈이 없어 제어할 수 없습니다" | 사유 원문(W10/W11) | 상동 |
| S4 | ①=true / ②=false / 매칭 0 | W19 **"연결 가능한 장치를 찾지 못했습니다 (USB·전원·PTP 모드 확인)"** | 사유 원문(`UnavailableReason` — W12 등) | 상동 |
| S5 | ①=true / ②=false / 매칭 ≥1 | W20 + W20b "SDK 연결에 실패했습니다 — 다른 프로그램의 점유(웹캠 유틸리티 등)·케이블을 확인하세요" | 사유 원문 | 상동 |
| S6 | ①=true / ②=true / — | W21 "{모델 표시명} — 연결 확인됨" | 배터리 % (조회 성공 시) + W21a "세부 확인·셔터 테스트는 [카메라 테스트]에서" | [장치 검색]·[카메라 테스트] |
| S7 | 검색 중 예외 | W22 "장치 검색에 실패했습니다. 다시 시도해 주세요." | 로그에 예외 원문 | [장치 검색] |

- **거짓말 검증**: S2/S3은 "없다"를 말하지 않는다(R1). S4는 단정 완화형(R2). S3/S5의 "감지" 라인은 양성 관측일 때만(R3). 어느 행도 크래시·무한 대기로 가지 않는다(리포 관례).
- **현 프로덕션(SDK 미탑재)의 기본 도달점은 S2**다: `MissingNikonSdkShim.IsOperational=false` → W18 + W10("SDK 모듈이 설치되지 않았습니다"). 이것이 "지금 정직하게 보여줄 수 있는 것"의 전부이며, 운영자에게 다음 행동(SDK 배치)을 정확히 안내한다.
- S3까지 도달하면(U1·U2가 참으로 판명되면) "카메라는 꽂혀 있는데 SDK가 없다"는 **정직한 중간 상태**가 성립한다 — 팀 지시가 요구한 바로 그 상태. 단 설계는 이 도달을 전제하지 않는다.

### 5.4 it23 §9.1 결정("설정 진입만으로 USB를 건드리지 않는다")과의 양립 — 유지 판정

- 재검토 결과 **결정 유지**: 설정은 열람 빈도가 높은 화면이고, 진입 부수효과로 SDK 모듈 로드·USB 세션 성립이 일어나면 A9(웹캠 유틸리티 이중 점유)류 간섭의 표면적이 넓어진다.
- "찾아보기" 요구와의 양립: 탐색을 **명시 버튼 [장치 검색]**으로 두면 "진입은 무접촉, 접촉은 사용자 의사"가 성립한다. 사용자 요구 문장 자체가 "설정을 켜고 → 찾아보고"라는 명시 행위 순서다.
- 버튼 배치는 외부 카메라 토글 **on일 때만 노출되는 하위 패널 안**(§8.1): `ExternalCameraEnabled=false`면 외부 카메라를 한 번도 접촉하지 않는다는 회귀 0 불변식(F14)의 정신을 설정 화면까지 확장 — off 상태에서는 검색 진입점 자체가 없다.
- 예외 없음: 프린터 열거(§7)와 웹캠 열거(F8)는 USB 장치 세션을 만들지 않으므로(스풀러 DB·로컬 버스 조회) 자동 수행해도 이 결정과 충돌하지 않는다.

### 5.5 검색 후 연결 잔류 금지

검색 성공(S6) 시 스냅샷(모델명·배터리) 채취 직후 `DisconnectAsync()`한다(F13 — 재연결 가능 상태 복귀).

- 근거: 설정 화면이 장치를 점유한 채 방치되면 (a) 화면 이탈 시 해제 경로를 새로 설계해야 하고(OnLeave 훅·예외 경로 전부), (b) 촬영·테스트 모달의 연결 상태 가정("자기가 연 것은 자기가 닫는다")이 흔들린다. 검색은 "순간 관찰"로 정의하는 편이 수명 관리가 0이 된다.
- 비용: 검색 직후 촬영·테스트 모달 진입 시 재연결 수 초 — 허용(둘 다 자체 연결 시퀀스를 이미 가진다).
- S6 문구가 "연결됨"이 아니라 **"연결 확인됨"**인 이유: 표시 시점에는 이미 해제되어 있다 — 현재형 "연결됨"은 거짓이 된다.

---

## §6 C. 지원 모델 표시

### 6.1 판정 — 기존 ComboBox 유지 + 개념 구분 캡션 신설

지원 모델의 단일 진실은 `ExternalCameraModels.All`(F12)이고, 설정의 모델 ComboBox가 이미 그 목록을 표시한다. 사용자가 이를 보지 못한 1차 원인은 게스트 Collapsed(F1 — §4가 해소)다. 남는 것은 **표시 의미의 명확화**다:

- 모델 행 라벨을 `모델` → **`지원 모델`**로 변경 — "선택 가능한 것 = 이 앱이 SDK로 지원하는 것"임을 라벨이 직접 말한다.
- ComboBox 아래 캡션 W24 신설: "이 앱이 SDK 연동을 지원하는 모델 목록입니다. **연결된 장치 목록이 아닙니다** — 연결 확인은 [장치 검색]." (R5)
- 모델 1개(현재)와 여러 개(미래)에서 같은 형태를 유지한다: 1개일 때 정적 텍스트로 바꾸는 분기는 모델 추가 시 XAML 재작업을 만들 뿐이다. ComboBox는 항목 1개여도 "목록"의 의미를 전달하고, `SelectedValue` 값 기반 바인딩(it7 B9)이라 항목 수와 무관하게 안전하다.
- **it23 "모델 추가 = 표 한 줄 + 법적 절차 1건" 규약 불변**: 이 설계는 레지스트리를 읽기만 한다. §5.1의 USB 매칭 키워드도 `DisplayName`에서 유도하므로 스키마 확장이 없다.

### 6.2 검색 결과와의 관계

지원 모델 목록에는 어떤 연결 상태 장식도 붙이지 않는다(R5). 연결 상태는 검색 결과 영역(S0~S7)에만 나타나며, S6의 "{모델 표시명} — 연결 확인됨"이 두 개념을 잇는 유일한 지점이다. 테스트 모달 정보 패널(F11)은 현행 유지 — 이 설계는 모달을 변경하지 않는다.

---

## §7 D. 프린터 — 판정 (b): 설치 프린터 열거·선택·저장, 실제 인쇄는 명시적 비목표

### 7.1 세 안의 판정

| 안 | 내용 | 판정 | 왜 |
|---|---|---|---|
| (a) | placeholder 유지 + 문구 개선 | 기각 | "찾아보고 나열" 요구를 정면으로 미충족. 문구만 다듬은 Disable 토글은 이번 피드백("강제로 비활성화 되어 있던")의 재생산 |
| (b) | **Windows 설치 프린터 열거 + 선택 + ini 저장** (실인쇄 없음) | **채택** | 스풀러 DB 조회는 장비·SDK 없이 지금 100% 정직하게 구현·검증 가능. "나열" 요구 충족. 미인쇄 상태는 고지 문구(W25)로 오해 차단 |
| (c) | 전체 인쇄 기능 | 기각(이번 범위) | 용지·여백·색 관리·큐 오류 복구가 별도 이터레이션 규모. `IPhotoPrinter.PrintAsync` 계약(F5)은 이미 있으므로 (b)가 선행돼도 재작업 없음 |

**(b)의 정직성 문제와 해소**: 실제 인쇄가 없는 상태에서 프린터를 선택하게 두면 "선택했으니 인쇄되겠지"라는 오해를 만들 수 있다. 해소 장치 3개 — ① 프린터 하위 패널에 상시 고지 W25("인쇄 기능은 아직 제공되지 않습니다. 선택한 프린터는 인쇄 기능이 추가되면 사용됩니다"), ② 토글 행의 "추후 지원 예정" 캡션을 W25 취지로 대체(삭제가 아니라 정확화), ③ 실인쇄를 §15 비목표에 명기. 이 고지가 있는 한 "미리 골라 두기"는 거짓이 아니라 준비다 — 노출값 자유 입력(it23 §10.3)과 같은 선행 준비 워크플로.

### 7.2 열거 API — `System.Printing` 채택

| 후보 | 판정 | 왜 |
|---|---|---|
| `System.Printing` (`LocalPrintServer.GetPrintQueues`) | **채택** | WPF 동반 어셈블리(L2 — 참조팩 동봉, 추가 패키지 없음). `DefaultPrintQueue`로 기본 프린터 식별이 1급 지원(WEB3). 미래 인쇄 구현(XpsDocumentWriter)과 같은 스택 — 열거를 지금 같은 API로 하면 재작업이 없다 |
| WMI `Win32_Printer` | 기각 | 가능하지만 미래 인쇄 스택과 이원화된다. F9 관례(WMI)와의 일관성보다 "열거와 인쇄가 같은 진실을 본다"가 중요 |
| `System.Drawing.Printing.PrinterSettings` | 기각 | System.Drawing.Common 패키지 추가 필요 — 이득 없이 의존성만 증가 |

주의사항(WEB3 + U3~U5):

- System.Printing은 Windows 서비스·ASP.NET에서 미지원이나 **데스크톱 WPF 앱은 해당 없음**(WEB3 Caution 원문 확인).
- `LocalPrintServer` 생성·열거는 스풀러 서비스에 의존 — 중지 시 예외(U4). 열거자는 **전 예외 catch → `Succeeded=false`** 반환(크래시 금지 관례). 권한: 로컬 큐 열람은 표준 사용자 권한으로 충분 — 관리 액세스(`PrintSystemDesiredAccess.AdministrateServer`)를 **요청하지 않는다**(기본 생성자 사용).
- 열거 대상: `EnumeratedPrintQueueTypes.Local` + `Connections`(네트워크 연결 프린터). 네트워크 환경에서 느릴 수 있어(U5) `Task.Run` 격리.
- `PrintQueue`·`LocalPrintServer`는 `Dispose` 대상 — 열거 후 즉시 해제(스냅샷 POCO로 복사).
- 오프라인 표시(`IsOffline`)는 **표시하지 않는다**: 신뢰성이 낮기로 악명 높고(갱신 없이는 stale), 틀린 "오프라인" 딱지는 R1 위반이다. "설치됨" 명제(R4)까지만 말한다.

### 7.3 프린터 상태 전수표

경계: Core 계약 + App 구현(테스트는 fake 주입).

```csharp
// MCPhoto.Core/Devices/IPrinterEnumerator.cs (신규)
/// <summary>Windows에 설치된 프린터 1행 스냅샷(스풀러 DB 기준 — 장치 전원·연결 상태가 아니다).</summary>
public sealed record InstalledPrinter(string Name, bool IsDefault);

/// <summary>열거 결과. Succeeded=false는 "확인 불가"(P4) — 빈 목록(P2)과 다른 명제다(R4).</summary>
public sealed record PrinterEnumerationResult(bool Succeeded, IReadOnlyList<InstalledPrinter> Printers);

public interface IPrinterEnumerator
{
    /// <summary>설치 프린터 열거. 예외를 던지지 않는다 — 실패는 Succeeded=false.</summary>
    Task<PrinterEnumerationResult> EnumerateAsync(CancellationToken ct = default);
}
// 구현: MCPhoto.App/Services/SystemPrinterEnumerator.cs (System.Printing 사용, Task.Run 내부 수행)
// DI: services.AddSingleton<IPrinterEnumerator, SystemPrinterEnumerator>();  (상태 없음 — Singleton 무해)
```

| ID | 조건 | 표시 | 콤보·저장 동작 |
|---|---|---|---|
| P0 | 열거 전(하위 패널 노출 직후) | 즉시 P1로 — 정지 상태 없음 | 콤보 Disable |
| P1 | 열거 중 | "프린터 확인 중…" (W17 계열) | 콤보 Disable, [다시 검색] Disable |
| P2 | 성공 · 0대 | W26 **"설치된 프린터가 없습니다"** | 콤보 Disable. 저장값은 건드리지 않음(원값 보존) |
| P3 | 성공 · N대 | 콤보 표시. 기본 프린터는 "{이름} (기본)" 접미(U7 null 가드) | `SelectedValue=PhotoPrinterName`(값 기반 — it7 B9). 저장은 §9 게이트 |
| P4 | 실패(스풀러 중지·예외) | W27 **"프린터 목록을 확인할 수 없습니다 (인쇄 스풀러 상태 확인)"** | 콤보 Disable. 저장값 보존 |
| P5 | 성공 · 저장된 `PhotoPrinterName`이 목록에 없음 | 콤보 첫 항목으로 합성 행 W29 "{이름} (설치 확인 필요)" 추가 + 선택 유지 | 저장 시 그 이름 그대로 보존 — **목록 부재를 이유로 저장값을 지우지 않는다**(관리자가 맞춘 값의 클로버 금지, settings-guest-edit-gate와 같은 원칙) |

- 열거 시점: 프린터 하위 패널이 열릴 때(토글 on 상태로 설정 진입, 또는 토글을 on으로 변경) 자동 1회 + [다시 검색] 버튼. 웹캠 열거 선례(F8)와 동형 — USB 장치 세션이 없으므로 §5.4 결정과 충돌하지 않는다.
- 선택 검증은 **사용 시점**(미래 인쇄 구현)의 몫이다: 지금 목록 부재로 값을 지우거나 막으면, 일시적으로 꺼진 프린터가 설정을 파괴한다 — 노출값 문자열의 "적용 시 검증" 철학(it23 §10.2)과 동일.

---

## §8 UI 명세

### 8.1 설정 화면 — 외부 장치 섹션 개편 (`SettingsView.xaml:370-488` 대체)

```
외부 장치                                        ← 섹션: Visibility 게이트 제거(§4.1 — 항상 표시)
  외부 카메라 사용   [로그인 필요†|권한 없음‡] [토글]   ← IsEnabled=CanEditExternalCamera (기존)
  ── 이하 ExternalCameraEnabled=on일 때만(BoolToVis, 기존 구조) ──
  [캡션 W1] 타임랩스 기능은 웹캠으로만 동작됩니다.        (기존 유지)
  지원 모델          [ComboBox — SelectedValue]     ← 라벨 "모델"→"지원 모델"(§6.1), 나머지 기존
  [캡션 W24] 이 앱이 SDK 연동을 지원하는 모델 목록입니다…  (신설)
  [캡션 W2] 프리뷰는 웹캠 영상입니다…                   (기존 유지)
  장치 확인          [장치 검색]                      ← 신설. IsEnabled = IsLoggedIn && !IsDiscovering
    {검색 결과 헤드라인 — S0~S7 문구}                  ← TextBlock, TextWrapping=Wrap
    {상세 라인들}                                    ← ItemsControl<string> (사유 원문·USB 감지·배터리·참고)
  셔터 속도/조리개/ISO (슬라이더+입력)                  (기존 유지 — 이 설계 무접촉)
  [캡션 W3]                                         (기존 유지)

  프린터 사용        [로그인 필요†|권한 없음‡] [토글]   ← IsEnabled="False" 해제 → CanEditExternalCamera.
                                                      "추후 지원 예정" 캡션 삭제(W25가 대체)
  ── 이하 PhotoPrinterEnabled=on일 때만(BoolToVis) ──
  [캡션 W25] 인쇄 기능은 아직 제공되지 않습니다…         (신설 — 상시 고지)
  프린터             [ComboBox]  [다시 검색]           ← SelectedValuePath=Name, DisplayMemberPath=Display
    {상태 문구 — P1/P2/P4일 때만}                     ← P3(정상 목록)이면 빈 문자열 → Collapsed
```

- † `GuestGateNote` 스타일 그대로 재사용(F2 — `IsGuest` 트리거 내장, 신규 키 없음).
- ‡ 기존 "권한 없음" TextBlock의 Visibility를 `CanEditExternalCamera` 역바인딩에서 **신설 `IsExternalEditDenied`(= IsLoggedIn && !CanEditExternalCamera)**로 교체(§4.3) — 게스트에게 "권한 없음"이 중복 표출되는 것을 막는다.
- XAML 제약 준수: **신규 리소스 키 0**(`GuestGateNote`·`Text.Caption`·`Button.Secondary`·`Spinner.Ring`·`Toggle` 재사용), 병합 딕셔너리 교차 `StaticResource` 자동 회피, ComboBox는 `SelectedValue` 값 기반(it7 B9), 결과 문구는 고정폭 금지·`TextWrapping="Wrap"`(고정폭 컬럼 잘림 이력).

### 8.2 동결 문구 표 (it23 W1~W14에 이어서)

| ID | 위치 | 문구 |
|---|---|---|
| W15 | 검색 버튼 | `장치 검색` |
| W16 | S0 | `장치를 검색하지 않았습니다. [장치 검색]으로 연결 상태를 확인하세요.` |
| W17 | S1 | `장치 검색 중…` |
| W18 | S2 헤드라인 | `장치 연결 여부를 확인할 수 없습니다` |
| W19 | S4 헤드라인 | `연결 가능한 장치를 찾지 못했습니다 (USB·전원·PTP 모드 확인)` |
| W20 | S3·S5 감지 라인 | `USB에서 장치가 감지되었습니다: {이름들}` |
| W20a | S3 부연 | `SDK 모듈이 없어 제어할 수 없습니다` |
| W20b | S5 부연 | `SDK 연결에 실패했습니다 — 다른 프로그램의 점유(웹캠 유틸리티 등)·케이블을 확인하세요` |
| W21 | S6 헤드라인 | `{모델 표시명} — 연결 확인됨` |
| W21a | S6 부연 | `세부 확인·셔터 테스트는 [카메라 테스트]에서 할 수 있습니다` |
| W21b | S6 배터리(조회 성공 시) | `배터리 {n}%` |
| W22 | S7 | `장치 검색에 실패했습니다. 다시 시도해 주세요.` |
| W23 | 참고 라인(비매칭 휴대용 장치, 최대 4개) | `참고: 감지된 휴대용 장치(카메라가 아닐 수 있음): {목록}` |
| W24 | 지원 모델 캡션 | `이 앱이 SDK 연동을 지원하는 모델 목록입니다. 연결된 장치 목록이 아닙니다 — 연결 확인은 [장치 검색].` |
| W25 | 프린터 고지 | `인쇄 기능은 아직 제공되지 않습니다. 선택한 프린터는 인쇄 기능이 추가되면 사용됩니다.` |
| W26 | P2 | `설치된 프린터가 없습니다` |
| W27 | P4 | `프린터 목록을 확인할 수 없습니다 (인쇄 스풀러 상태 확인)` |
| W28 | P1 | `프린터 확인 중…` |
| W29 | P5 합성 행 표시명 | `{이름} (설치 확인 필요)` |
| W30 | P3 기본 프린터 접미 | `{이름} (기본)` |
| W31 | 프린터 재열거 버튼 | `다시 검색` |

- S2~S5의 **상세 라인은 동결 대상이 아니다** — `readiness.Reason`/`UnavailableReason` 원문(W10~W12, it23 동결)을 그대로 흘린다. 같은 사유가 화면마다 다르게 설명되는 것을 막는 `NikonCameraReasons` 집약 원칙(F7) 그대로.

### 8.3 VM 신규 멤버 (바인딩 누락 방지 전수 목록 — `SettingsViewModel`)

| 멤버 | 형 | 역할 |
|---|---|---|
| `IsExternalEditDenied` | bool 파생(불변 — INPC 불요) | "권한 없음" 캡션 Visibility(§4.3) |
| `DiscoverExternalCameraCommand` | AsyncRelayCommand | §5.2 시퀀스. CanExecute = `IsLoggedIn && !IsDiscovering` |
| `IsDiscovering` | bool [ObservableProperty] + NotifyCanExecuteChangedFor | S1 표시·단일 비행 |
| `DiscoveryHeadline` | string [ObservableProperty] | S0~S7 헤드라인(초기값 W16) |
| `DiscoveryDetailLines` | `ObservableCollection<string>` | 사유·감지·배터리·참고 라인(F11의 `ExternalCapabilityLines` 관례) |
| `RefreshPrintersCommand` | AsyncRelayCommand | P1~P5 열거. CanExecute = `IsLoggedIn && !IsEnumeratingPrinters` |
| `IsEnumeratingPrinters` | bool [ObservableProperty] | P1 표시·단일 비행 |
| `PrinterOptions` | `ObservableCollection<PrinterOptionItem>` | 콤보 목록. `PrinterOptionItem(string Name, string Display)` — W29/W30 가공은 Display에만 |
| `PhotoPrinterName` | string [ObservableProperty] | 콤보 SelectedValue ↔ ini |
| `HasPrinters` | bool 파생 또는 [ObservableProperty] | 콤보 IsEnabled(P2·P4에서 false) |
| `PrinterStateText` | string [ObservableProperty] | P1/P2/P4 문구(P3이면 빈 값 → Collapsed) |

`OnPhotoPrinterEnabledChanged`(CommunityToolkit partial 훅): on 전환 시 `PrinterOptions`가 비어 있으면 열거 1회 트리거. 설정 진입(`OnEnterAsync`)에서는 `PhotoPrinterEnabled`(ini)가 이미 on일 때만 열거 — off면 하위 패널이 없으므로 열거도 없다.

---

## §9 설정 스키마 변경 (ini `[MCPhoto]` 섹션)

### 9.1 키 신설·변경

| 키 | 타입 | 기본값 | 의미 | Clamp |
|---|---|---|---|---|
| `PhotoPrinterName` | string | `""` | 선택된 설치 프린터 이름(Windows 프린터명 — 시스템 내 유일 식별자). **빈 값 = 미선택.** 목록 부재여도 값 보존(P5) — 검증은 사용 시점(§7.3) | Trim만 |
| `PhotoPrinterEnabled` | bool | `false` | (기존 키 — placeholder에서 **편집 가능으로 승격**) 의미: "인쇄 기능 도입 시 이 프린터 구성을 사용" 준비 플래그 + 설정 하위 패널 노출 게이트. **이번 이터레이션에서 런타임 효과는 설정 UI 밖에 없다**(W25 고지가 이를 화면에서 말한다) | 없음 |

- `ExternalCamera*` 5키(it23 §7.1)는 무변경.
- ini에 키가 없으면 기본값(`IniFile` 손상·누락 폴백 관례) — 마이그레이션 불요.

### 9.2 저장 게이트 이동 — 프린터 2키를 외부 장치 게이트로 통일

현행 `if (!IsGuest) s.PhotoPrinterEnabled = …`(F4)를 `CanEditExternalCamera` 블록으로 옮기고 `PhotoPrinterName`을 함께 넣는다:

```csharp
// SettingsViewModel.SaveSettings — it24: 외부 장치 섹션 7필드 단일 게이트
if (CanEditExternalCamera)
{
    s.ExternalCameraEnabled = ExternalCameraEnabled;
    s.ExternalCameraModel   = ExternalCameraModel;
    s.ExternalShutterSpeed  = _shutterSpeed.Text;
    s.ExternalAperture      = _aperture.Text;
    s.ExternalIso           = _iso.Text;
    s.PhotoPrinterEnabled   = PhotoPrinterEnabled;   // (!IsGuest 블록에서 이동)
    s.PhotoPrinterName      = PhotoPrinterName;      // 신설
}
```

- 행동 회귀 없음 근거: 종전 TempUser는 `!IsGuest`라 `PhotoPrinterEnabled`를 기록할 수 있었지만 UI가 `IsEnabled="False"`여서 값을 바꿀 수단이 없었다(F4) — 기록값은 항상 Load 원값이었으므로 게이트를 좁혀도 관측 가능한 차이가 없다.
- `LoadSettings`: `PhotoPrinterName = s.PhotoPrinterName;` 추가. 외부 장치 블록은 게스트 강제 off 없음(§4.2 — 현행 유지).
- `AppSettings`: 필드 1개 신설 + `Clamp()`에 Trim + **`Clone()`에 복사 추가**(누락 = 편집 취소 시 유실 — it23 T-S3와 같은 회귀 잠금 대상). `IniSettingsService.ReadInto/WriteFrom`에 1키 추가(`nameof` 관례).

---

## §10 실패·부재 경로 전수표 (it23 §11 E1~E11에 이어서)

| ID | 상황 | 감지 지점 | 표시 | 강등 동작 |
|---|---|---|---|---|
| E12 | WMI 조회 실패(권한·WMI 서비스 이상) | `PortableDeviceProbe`(catch-all) | 참고·감지 라인 없이 S2/S4로 축퇴 | 빈 목록 반환 + 로그 — R3에 의해 판정 불변(양성 신호만 쓰므로 안전) |
| E13 | 검색 시퀀스 중 예상 밖 예외 | VM 커맨드 catch | W22(S7) | `finally`로 `IsDiscovering=false` 확정 — 버튼 영구 잠김 금지(it20 교훈) |
| E14 | 검색 중 화면 이탈 | — (별도 취소 없음) | — | 시퀀스는 백그라운드에서 완주(ConnectTimeout 5s 상한) 후 `DisconnectAsync`까지 도달 — 연결 잔류 없음. VM은 Transient라 이후 속성 갱신은 무해. 앱 종료 코너는 it23 §12.2 종료 훅이 커버 |
| E15 | S6 스냅샷 중 배터리·capability 조회 실패 | `GetCapabilitiesAsync` null/예외 | W21b 라인 생략(W21 헤드라인은 유지) | 검색 성공 판정 불변 |
| E16 | 검색 성공 직후 `DisconnectAsync` 실패 | 어댑터 내부 catch(F13) | 표시 불변 | 로그만 — 어댑터가 예외를 삼키는 기존 계약 |
| E17 | 스풀러 중지·System.Printing 예외 | `SystemPrinterEnumerator`(catch-all) | W27(P4) | `Succeeded=false` — P2와 구조적으로 구분(R4) |
| E18 | 저장된 프린터가 열거 목록에 없음 | VM 목록 구성 | W29 합성 행(P5) | 저장값 보존 — 클로버 금지 |
| E19 | 프린터 열거 결과 도착 전 [저장] | — | — | `PhotoPrinterName`은 Load 원값 그대로이므로 원값이 재기록될 뿐 — 유실 없음 |
| E20 | `CheckReadiness` 파일 검사 예외(경로 권한 등) | `SdkRuntimeProbe` 기존 catch(F6) | W11 사유로 S2 | 기존 "예외 = 부재 취급" 계약 재사용 |

어느 행도 크래시·무한 대기·거짓 단정으로 가지 않는다.

---

## §11 스레딩 · 수명 · 인코딩

| 항목 | 규칙 |
|---|---|
| WMI 조회 | `Task.Run`(F8·F9 선례 — 수백 ms 가능). UI 갱신은 await 복귀 후 UI 컨텍스트 |
| `CheckReadiness` | 파일 존재 검사 수준이지만 네트워크 경로 실행 가능성을 고려해 **검색 시퀀스의 `Task.Run` 구간에서 호출** |
| `System.Printing` 열거 | `SystemPrinterEnumerator` 내부에서 `Task.Run` — `PrintQueue`/`LocalPrintServer`는 스냅샷 복사 후 즉시 `Dispose`(§7.2). System.Printing 객체를 VM에 노출하지 않는다(POCO만 — ViewModel의 UI/장치 타입 무의존 원칙) |
| 이벤트 구독 | **신규 구독 0** — 검색은 1회 실행 폴링이라 `ConnectionChanged`를 구독하지 않는다(스냅샷 후 즉시 해제이므로 상태 추적 무의미). 프린터 열거도 이벤트 없음. 따라서 신규 해제 경로도 0 — 기존 `ExposureParameters` 구독 해제(OnLeaveAsync)만 유지 |
| 단일 비행 | `IsDiscovering`/`IsEnumeratingPrinters` + CanExecute — 로딩 상태는 `finally`에서 확정(frame-catalog-wait 교훈) |
| 인코딩 | 수정·신규 .cs 전부 UTF-8 no BOM(F15). XAML은 기존 인코딩 유지. 확인: `head -c 3 <file> | od -An -tx1` ≠ `ef bb bf` |

---

## §12 테스트 전략 (`tests/MCPhoto.Tests`, headless — 장치 없이 검증하는 것 전부)

핵심 경계: `IExternalCamera`(FakeExternalCamera — `CheckReadiness`/`ConnectAsync` 스크립트 주입), `IPrinterEnumerator`(Fake — 결과 주입), `PortableDeviceProbe.MatchCandidates`(순수), `ExternalDiscoveryJudge.Judge`(순수). **WMI·System.Printing 실 I/O는 테스트하지 않는다** — I/O 함수는 catch-all + 빈 결과 폴백뿐이라 실패 경로가 구조적으로 무해하고, 판정은 전부 순수 함수 뒤에 있다.

### 12.1 Core 순수

| ID | 테스트 | 검증 |
|---|---|---|
| T-J1 | `Judge` 전수: (CanControl, usbSeen, connected) 8조합 → S2/S3/S4/S5/S6 매핑, connected=true면 usbSeen 무관 S6 | §5.3 표 그대로 |
| T-J2 | `Judge`는 CanControl=false에서 connected 입력을 무시(방어 — 호출측이 게이트를 어겨도 S2/S3) | §5.2 ② 게이트 |
| T-M1 | `MatchCandidates` — 대소문자 무시 Contains, 키워드 미포함(제네릭 "MTP Portable Device") 미스, 빈 입력 빈 출력 | U2 허용 설계 |
| T-S4 | `AppSettings` — `PhotoPrinterName` Trim·Clone 복사·INI 라운드트립(기존 T-S1~S3 확장) | §9.1 |

### 12.2 준비도·어댑터

| ID | 테스트 | 검증 |
|---|---|---|
| T-R2 | `NikonExternalCamera.CheckReadiness`: shim `IsOperational=false` → `(false, W10)` — **파일이 있어도** false | §5.1 ⓐ (R1의 코드 실체) |
| T-R3 | 상동: shim operational + md3 부재 → `(false, W11 사유)` / 파일 존재 → `(true, null)` (SdkRuntimeProbe 임시 폴더 주입 — 기존 관례) | §5.1 ⓑⓒ |
| T-R4 | `NullExternalCamera.CheckReadiness` → `(false, "외부 카메라 미구성")` | 기본 구현 |
| T-R5 | `MissingNikonSdkShim.IsOperational == false` 고정(상수 회귀 잠금) | §5.1 |

### 12.3 SettingsViewModel — 검색·프린터·게이트

| ID | 테스트 | 검증 |
|---|---|---|
| T-D1 | 검색 커맨드: Fake 조합별로 `DiscoveryHeadline`/`DiscoveryDetailLines`가 §5.3 문구와 일치(S2·S4·S6·S7 각 1) | 상태 전수표 배선 |
| T-D2 | 검색 성공(S6) 후 `DisconnectAsync` 1회 호출 — 연결 잔류 없음 | §5.5 |
| T-D3 | 검색 중 재진입 — 두 번째 호출 무시(`ConnectAsync` 1회) + 예외 시에도 `IsDiscovering=false` 복귀 | 단일 비행·finally |
| T-D4 | CanControl=false면 `ConnectAsync` **0회**(USB 미접촉) | §5.2 ② 게이트 |
| T-D5 | 게스트: 검색 CanExecute=false. TempUser(로그인): CanExecute=true | §4.3 |
| T-P1 | 프린터 열거 성공 N대 → `PrinterOptions` N행 + 기본 프린터 W30 접미, `HasPrinters=true` | P3 |
| T-P2 | 성공 0대 → W26, 콤보 Disable / 열거 실패 → W27 — **두 상태가 문구·상태 모두 구분** | P2 vs P4 (R4) |
| T-P3 | 저장된 이름이 목록에 없음 → W29 합성 행 + 선택 유지 + 저장 시 원문 보존 | P5·E18 |
| T-P4 | TempUser·게스트 Save → `PhotoPrinterEnabled`/`PhotoPrinterName` ini 원값 보존(미기록). User Save → 반영 | §9.2 (기존 `Guest_Save_Preserves_Ini_*` 동형 복제) |
| T-P5 | `PhotoPrinterEnabled` off→on 전환 시 열거 1회 트리거, 이미 목록 있으면 재열거 없음 | §8.3 훅 |
| T-V3' | **재작성**: 게스트에게 섹션 보임 + 토글·모델·노출·프린터 편집 Disable + 검색 Disable (구 "섹션 Collapsed" 테스트 대체 — §4.3) | 가시성 정책 변경 |
| T-X1' | headless XAML 로드(기존 `XamlResourceTests` 관례) — 개편 섹션 바인딩 경로 유효·신규 리소스 키 0 | §8.1 |

### 12.4 실물 없이 검증 불가능한 것 (정직 목록)

D5300의 WMI 관찰 여부·관찰 이름(U1·U2), SDK 정상 상태의 S4~S6 실거동(U6), 실 프린터 환경의 열거 시간(U5). 자동 테스트 전부가 통과해도 증명되는 것은 **"관측이 이렇게 들어오면 화면은 이렇게 말한다"**까지다 — 관측 자체의 실측은 §14 Step 9.

---

## §13 `docs/analysis/` 갱신 지점

| 문서 | 절 | 갱신 내용 |
|---|---|---|
| `11-exe-app-features.md` | §11 설정 화면(273행 부근) | 외부 장치 섹션: "로그인 전용 섹션" → 항상 표시+읽기 전용, [장치 검색]·상태 전수표 요약, 프린터 열거·선택 |
| `12-exe-app-settings-and-config.md` | §1 AppSettings 표(49~54행) | `PhotoPrinterName` 행 신설, `PhotoPrinterEnabled` 서술 갱신("미지원 스캐폴드" → 준비 플래그+고지) |
| `12-exe-app-settings-and-config.md` | §1 게이트 노트(65행 부근) | `CanConfigureExternalCamera` 대상 "5키" → "7키"(프린터 2키 편입), 게스트 표시 정책(원값 표시) 병기 |
| `10-exe-app-architecture.md` | DI 표(93·95행 부근) | `IPrinterEnumerator`→`SystemPrinterEnumerator` 행 추가, `IPhotoPrinter` 서술에 "열거는 별도 계약" 주기 |
| `13-client-behavior-spec.md` | 설정 관련 절(해당 시) | 게스트 설정 화면 노출 범위 서술이 있으면 정합화 |

- it23 문서(`wpf-it23-external-camera-nikon-design.md`)는 **수정하지 않는다** — §9.1(게스트 Collapsed)·§9.2는 당시 판정으로 유효하며, 본 문서가 이를 대체함을 이 문서에 명시(스펙 폐기 관례: 이력 보존, 소급 편집 금지).

---

## §14 구현 WBS (템플릿: `docs/templates/WBS_BLUEPRINT.md`)

> 검증된 사실 = §1(F1~F15·L1~L2·WEB1~4), 미검증 가정 = §2(U1~U7 → 검증 단계 매핑).
> **Step 1~8은 장비·SDK 없이 완료 가능**하며 U1·U2·U6에 의존하지 않는다. Step 9만 실물 D5300 필요.
> 공통 검증 명령: `build-verify` 스킬(없으면 `dotnet build MCPhoto.sln` + `dotnet test tests/MCPhoto.Tests`).

### Step 1: Core 계약 — 준비도 POCO·판정 순수 함수
- **Context Brief**: 검색의 관측/판정 분리(§5.1·§5.3). "없습니다"와 "확인할 수 없습니다"의 구분(R1·R2)을 타입으로 강제한다. `IExternalCamera`는 it23에서 멤버 추가로 확장된 전례가 있고 프로덕션 소비자가 적다(설정·촬영·테스트 모달 VM + DI).
- **대상 파일**: `src/MCPhoto.Core/Devices/ExternalCameraTypes.cs`(`ExternalCameraReadiness` 추가), `IExternalCamera.cs`(`CheckReadiness()` 멤버 추가), `NullExternalCamera.cs`(구현), `ExternalDiscoveryJudge.cs`(신규 — enum+Judge), `tests/…`(T-J1·T-J2·T-R4)
- **선행 조건**: 없음
- **구현 내용**: §5.1 코드 블록 + §5.3 enum/Judge. Judge는 §5.3 표의 조건열을 그대로 옮긴다
- **검증 명령**: build-verify
- **완료 기준**: [관측] T-J1(8조합 전수)·T-J2·T-R4 통과, 기존 전체 테스트 무손상 / [non-goal] 기존 `IExternalCamera` 4+7멤버 시그니처 diff 없음, Core에 `System.Windows`·WMI·System.Printing 참조 없음 / [trigger] 없음(순수 타입)
- **롤백**: 커밋 revert(후속과 독립)

### Step 2: Nikon 준비도 — shim `IsOperational` + `CheckReadiness`
- **Context Brief**: "SDK 있음"을 파일 존재가 아니라 **shim 실구현 + 파일**로 판정(§5.1 — F6: 파일을 배치해도 MissingShim이면 연결 불가). `NikonSdkShim.cs` 파일 부재가 정상이라는 it23 신호 규약 불변.
- **대상 파일**: `src/MCPhoto.Devices.Nikon/INikonSdkShim.cs`(`bool IsOperational { get; }` 추가), `MissingNikonSdkShim.cs`(false 반환), `NikonExternalCamera.cs`(`CheckReadiness` 구현 — ⓐshim ⓑ`SdkRuntimeProbe` ⓒok), 테스트(T-R2·T-R3·T-R5, FakeNikonSdkShim에 IsOperational 주입 추가)
- **선행 조건**: Step 1
- **구현 내용**: §5.1 ⓐⓑⓒ. 사유 문구는 `NikonCameraReasons` 상수만 사용(신규 문구 없음)
- **검증 명령**: build-verify
- **완료 기준**: [관측] T-R2·R3·R5 통과 / [non-goal] `ConnectAsync`·`CaptureAsync` 등 기존 멤버 diff 없음, 이 프로젝트에 신규 패키지 참조 없음 / [trigger] `CheckReadiness`는 어떤 USB·SDK 호출도 하지 않음(파일 검사만)
- **롤백**: 커밋 revert

### Step 3: Capture — `PortableDeviceProbe` (WMI USB 관찰)
- **Context Brief**: PnP 트리 best-effort 관찰(§5.1 ③). `CameraNameProbe`(F9)와 동형의 I/O·순수 분리. WPD 클래스 포함(WEB2·L1). 관찰은 양성 신호 전용(R3)이므로 실패가 판정을 오염시키지 않는다.
- **대상 파일**: `src/MCPhoto.Capture/PortableDeviceProbe.cs`(신규), 테스트(T-M1 — 순수 함수만)
- **선행 조건**: 없음(Step 1·2와 병렬 가능)
- **구현 내용**: §5.1 코드 블록의 WQL·매칭. catch-all → 빈 목록 + 경고 로그(F9 관례)
- **검증 명령**: build-verify
- **완료 기준**: [관측] T-M1 통과 / [non-goal] `CameraNameProbe`·`OpenCvCameraService` diff 없음 / [trigger] 호출 없이는 WMI 접촉 없음(정적 메서드)
- **롤백**: 파일 삭제(소비자는 Step 6에서 등장)

### Step 4: 설정 스키마 — `PhotoPrinterName`
- **Context Brief**: §9.1. 빈 값=미선택, 목록 부재여도 보존(P5). `Clone()` 누락은 편집 취소 유실 회귀(it23 T-S3 동형).
- **대상 파일**: `src/MCPhoto.Core/Settings/AppSettings.cs`(필드·Clamp·Clone), `IniSettingsService.cs`(ReadInto/WriteFrom), 테스트(T-S4)
- **선행 조건**: 없음(병렬 가능)
- **구현 내용**: §9.1 표 그대로. `PhotoPrinterEnabled` 주석을 "준비 플래그" 의미로 갱신
- **검증 명령**: build-verify
- **완료 기준**: [관측] T-S4 통과 + 기존 설정 라운드트립 무손상 / [non-goal] 기존 키 직렬화 불변(ini diff는 신설 1키뿐) / [trigger] 없음
- **롤백**: 커밋 revert

### Step 5: 프린터 열거 — Core 계약 + App 구현 + DI
- **Context Brief**: §7.2·§7.3. `System.Printing`은 WPF 동반 어셈블리(L2 — U3을 이 단계 빌드가 판정). 실패는 `Succeeded=false`(P4)로 — P2와 구조 분리(R4). System.Printing 타입은 이 파일 밖으로 나가지 않는다(POCO 스냅샷).
- **대상 파일**: `src/MCPhoto.Core/Devices/IPrinterEnumerator.cs`(신규 — §7.3 코드), `src/MCPhoto.App/Services/SystemPrinterEnumerator.cs`(신규), `ServiceRegistration.cs`(Singleton 등록 1줄), 테스트(계약 fake는 Step 6에서 소비 — 이 단계는 빌드·수동 검증)
- **선행 조건**: 없음(병렬 가능)
- **구현 내용**: `Task.Run` 내부에서 `LocalPrintServer` 생성 → `GetPrintQueues(Local+Connections)` → 큐당 try-catch로 Name·IsDefault 스냅샷 → 전부 Dispose. `AdministrateServer` 액세스 요청 금지(§7.2)
- **검증 명령**: build-verify + 수동 2건: ① 개발 머신 실행 시 설치 프린터 열거 확인(로그) ② **Print Spooler 서비스 중지 후 실행 → P4 경로(`Succeeded=false`) 확인(U4 해소 — 장비 불필요)**
- **완료 기준**: [관측] 빌드 성공(U3 해소)·수동 2건 확인 / [non-goal] Core가 System.Printing을 참조하지 않음(csproj 검사), 관리자 권한 불요 / [trigger] 호출 없이는 스풀러 접촉 없음
- **롤백**: DI 1줄 + 파일 2개 제거

### Step 6: SettingsViewModel — 가시성·검색 커맨드·프린터 목록·게이트
- **Context Brief**: §4·§5.2·§7.3·§8.3·§9.2의 VM 배선 전부. 기존 게스트 Collapsed 고정 테스트(T-V3 계열)는 **의도적으로 깨지므로 재작성**(§4.3). 검색·열거는 단일 비행 + `finally` 확정.
- **대상 파일**: `src/MCPhoto.App/ViewModels/SettingsViewModel.cs`(§8.3 멤버 + §5.2 커맨드 + §9.2 Load/Save + `IsExternalEditDenied`), `MCPhoto.App.csproj`(변경 없음 예상 — U3 결과에 따라), 테스트(T-D1~D5·T-P1~P5·T-V3' + FakeExternalCamera에 `CheckReadiness` 스크립트, FakePrinterEnumerator 신설)
- **선행 조건**: Step 1~5 전부
- **구현 내용**: §5.2 시퀀스 그대로(①은 `Task.Run` 구간에서 — §11). 문구는 §8.2 동결표만 사용. 프린터 열거 트리거는 §8.3 훅 규칙
- **검증 명령**: build-verify
- **완료 기준**: [관측] T-D1~D5·T-P1~P5·T-V3' 통과, 기존 설정 VM 테스트 무손상(T-V1·T-V2 등) / [non-goal] `CaptureViewModel`·`CameraTestViewModel` diff 없음(이 설계는 촬영·모달 무접촉), `ExternalCameraEnabled=off`에서 검색 진입점 부재 → 외부 카메라 접촉 0(F14 확장) / [trigger] `ConnectAsync`는 [장치 검색] 클릭 + `CanControl=true`에서만(T-D4)
- **롤백**: 커밋 revert

### Step 7: SettingsView.xaml — 섹션 개편
- **Context Brief**: §8.1 레이아웃. 신규 리소스 키 0, `GuestGateNote` 재사용, ComboBox `SelectedValue`, 문구 Wrap. 게스트 Collapsed 게이트(F1) 제거가 핵심 diff.
- **대상 파일**: `src/MCPhoto.App/Views/SettingsView.xaml`(370~488행 대체), 테스트(T-X1')
- **선행 조건**: Step 6
- **구현 내용**: §8.1 스케치 전부 + W15~W31 배치. 프린터 토글 `IsEnabled="False"` 해제 → `CanEditExternalCamera`
- **검증 명령**: build-verify(headless XAML 테스트 포함) + 앱 기동 스모크(설정 진입: 게스트/로그인 각 1회)
- **완료 기준**: [관측] T-X1' 통과, 게스트 설정 진입 시 섹션 표시 + 전 편집 컨트롤 Disable + "로그인 필요" 노티, 로그인 시 [장치 검색] 클릭 → S2(W18+W10) 표시 / [non-goal] 다른 섹션 diff 없음, 신규 리소스 키 0(`grep x:Key` diff로 검사) / [trigger] 설정 진입만으로 `ConnectAsync`·WMI·스풀러 접촉 없음(프린터 열거는 `PhotoPrinterEnabled=on`일 때만)
- **롤백**: 커밋 revert

### Step 8: 문서 동기화 — `docs/analysis/`
- **Context Brief**: §13 표. 코드와 문서가 어긋난 채 남는 것을 막는 마지막 단계(analysis-docs 관례).
- **대상 파일**: `docs/analysis/11-exe-app-features.md`, `12-exe-app-settings-and-config.md`, `10-exe-app-architecture.md`, (필요시) `13-client-behavior-spec.md`
- **선행 조건**: Step 7
- **구현 내용**: §13 표 그대로
- **검증 명령**: 문서 diff 육안 + 갱신 절에 it24 표기
- **완료 기준**: [관측] §13 표의 4문서 갱신 커밋 / [non-goal] it23 설계 문서 무수정 / [trigger] 없음
- **롤백**: 문서 revert

### Step 9 (실물 D5300 필요): USB 관찰 실측·문구 확정
- **Context Brief**: U1·U2 해소. 이 단계 전까지 S3/S5는 **도달 불가능할 수 있는 상태**로 남는 것이 정상이다(설계가 도달을 전제하지 않음 — §5.3).
- **대상 파일**: (실측 결과에 따라) `PortableDeviceProbe` 키워드 유도 규칙, `docs/analysis/11` 비고
- **선행 조건**: Step 1~8 + 실물 D5300(SDK는 불요 — WMI 관찰은 SDK와 무관)
- **구현 내용**: D5300을 PTP 모드로 연결 → `Get-CimInstance -Query "SELECT Name, PNPClass FROM Win32_PnPEntity WHERE PNPClass='WPD' OR PNPClass='Camera' OR PNPClass='Image'"` 실측 → ① 관찰 여부(U1) ② 이름(U2) 기록 → 제네릭 이름이면 W23 참고 라인의 실효성 확인, 모델명이면 키워드 매칭으로 S3 도달 확인
- **검증 명령**: 실기 [장치 검색] — SDK 미배치 상태에서 S2 또는 S3 표시 확인
- **완료 기준**: [관측] U1·U2 실측값이 이 문서 §2 표에 추기됨 / [non-goal] Judge·상태표 구조 변경 없음(문구·키워드만 조정 허용) / [trigger] 실측 전 키워드 보강 금지(추측으로 채우지 않는다 — SdkRuntimeProbe.RequiredCompanionFiles와 같은 원칙)
- **롤백**: 해당 없음(기록 단계)

---

## §15 리스크와 명시적 비목표

### 15.1 리스크

| 리스크 | 완화 |
|---|---|
| U1·U2 거짓(D5300이 WMI에 안 보이거나 제네릭 이름) → S3/S5 영구 미도달 | 기능 저하이지 오작동이 아니다 — S2/S4 문구는 그 상태에서도 참(R1·R3). W23 참고 라인이 운영자 육안 판단을 보조 |
| `GetPrintQueues`가 네트워크 프린터 다수 환경에서 수 초 지연(U5) | `Task.Run` + P1 표시 — UI 무영향. 실측 후 필요 시 `Local`만으로 축소(상수 1곳) |
| 큐 개별 속성 접근 예외(고아 큐 등) | 큐 단위 try-catch — 한 큐의 이상이 전체 열거를 죽이지 않는다(Step 5) |
| 게스트 가시성 변경이 키오스크 운영 화면을 복잡하게 보이게 함 | 편집 불가 + 인라인 노티는 기존 QR·필터와 동일 밀도 — 새 패턴이 아님. 문제 제기 시 섹션 접기(Expander)로 후속 대응 가능(이번 비목표) |
| 검색 버튼을 눌러도 현 프로덕션에선 항상 S2 → "기능이 없다"는 인상 | S2 문구가 원인(SDK 미설치)과 해법(모듈 배치)을 정확히 말한다 — it23 D2의 "부재 강등이 곧 정규 설치 절차 안내"와 일관 |

### 15.2 명시적 비목표

| 항목 | 왜 비목표인가 |
|---|---|
| **실제 인쇄**(`IPhotoPrinter.PrintAsync` 배선·용지/여백/색 관리) | §7.1 판정 (b) — 별도 이터레이션. 계약(F5)은 이미 있어 재작업 없음 |
| WPD COM API(`IPortableDeviceManager`) 직접 열거 | WMI로 충분한 best-effort. COM interop 추가 비용 대비 이득은 U1·U2가 참일 때만 발생 — Step 9 실측 후 필요 시 재검토 |
| 프린터 오프라인/상태 표시(`IsOffline` 등) | 신뢰성 낮음 — 틀린 딱지는 R1 위반(§7.2) |
| 설정 진입 시 DSLR 자동 검색 | it23 §9.1 유지 판정(§5.4) |
| 카메라 테스트 모달 변경 | 정보 패널(F11)은 현행으로 충분 — 검색 S6이 모달로 유도(W21a) |
| 검색 결과의 연결 유지(핫 스탠바이) | §5.5 — 수명 관리 0이 이득 |
| 웹캠 열거 UI 개편 | 기존 카메라 장치 섹션(F8) 무접촉 |
| USB 장치 변화 실시간 감시(WM_DEVICECHANGE·WMI 이벤트) | 명시 버튼 재검색으로 충분 — 상시 감시는 구독 수명·오탐 비용만 추가 |

---

## §16 완결성 게이트 (developer 전달 전 자체 검사)

- [x] 검증된 사실(§1 F/L/WEB) / 미검증 가정(§2 U1~U7) 분리 — 가정 전부 검증 단계 매핑
- [x] 팀 지시의 추정 금지 영역("SDK 없이 USB 열거 가능?")을 U1·U2로 명시하고 **미확인으로 유지** — 설계가 관찰 성공을 전제하지 않음(R3)
- [x] "없습니다" vs "확인할 수 없습니다" 명제 분리(§3 R1~R5)가 상태 전수표(§5.3·§7.3) 전 행에 적용됨
- [x] 모든 Step에 7필드 기재, 완료 기준 관측 기반 3문 형식(UI 단계 Step 6·7은 non-goal·trigger 포함)
- [x] 검증 명령 자동 실행 가능(build-verify) + 장비 필요 단계(Step 9) 격리
- [x] View 변경에 대응 VM 멤버 전수 명세(§8.3 — 바인딩 누락 없음)
- [x] 이벤트 구독 신설 0 — 해제 경로 논의 자체가 불요(§11), 기존 구독 해제 관례 유지
- [x] UI/백그라운드 경계 명시(§11), 로딩 상태 finally 확정
- [x] 신규 리소스 키 0(§8.1) — 병합 딕셔너리 교차 참조·키 충돌 원천 회피
- [x] 실패 전수표(§10 E12~E20) — 크래시·무한 대기·거짓 단정 경로 없음
- [x] ViewModel은 UI·장치 타입 무의존(System.Printing·WMI는 서비스·프로브 뒤 POCO)
- [x] 파일 인코딩 규칙 명시(§11)

**미해결 확인 사항 (구현 착수 전 사용자 확인 권장)**:

- **USER-DECISION 1 (§4.3)**: [장치 검색]·[다시 검색] 게이트를 `IsLoggedIn`(TempUser 포함)으로 판정했다 — 진단·상태 모달과 같은 눈높이. TempUser에게도 막아야 한다면 CanExecute 조건 1곳 수정으로 끝난다(구조 무변경).
- **USER-DECISION 2 (§9.1)**: `PhotoPrinterEnabled`를 "인쇄 기능 도입 시 사용" 준비 플래그로 승격했다(런타임 효과는 아직 설정 UI뿐, W25 고지 상시). 토글 자체를 인쇄 기능 출시까지 Disable로 남기는 대안도 가능하나, 그 경우 사용자 피드백("강제로 비활성화 되어 있던 설정을 켜고")을 재생산한다 — 채택안 유지 권장.
- **USER-DECISION 3 (§4.1)**: 게스트에게 섹션을 보이되 읽기 전용으로 여는 판정 — QR 계열의 "게스트 강제 off 표시"와 달리 ini 원값을 보여 준다(§4.2 근거). 게스트에게 값 노출 자체를 막아야 할 운영 사유가 있다면 알려 달라(현재 근거로는 없음).
