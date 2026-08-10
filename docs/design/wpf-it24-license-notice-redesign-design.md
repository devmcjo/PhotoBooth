# it24 설계 — 프로젝트 라이선스 고지 화면 재설계 · 고지 문서 상용화

> 입력: 사용자 피드백 원문(§0.1) · 현행 코드·배포 파일 전수 확인(§1) · 업계 통용 형식 조사(§2.3·§9)
> 작성: 2026-08-11 · 선행: [it23 C부](./wpf-it23-session-testmode-license-design.md#c부--오픈소스-라이선스-전문을-설정-안에서-직접-노출) · 법적 근거: [ffmpeg 라이선스·배포 설계](./wpf-ffmpeg-licensing-and-distribution-design.md)
>
> **범위**: 설정 → 고급의 라이선스 고지 화면을 요약 중심으로 재설계하고, `licenses/` 고지 텍스트를 상용 서비스 수준으로 재작성하는 규격. 코드·txt는 이 문서에 쓰지 않는다(설계 전용).

---

## §0 요구와 판정

### 0.1 사용자 피드백 원문 (축약 금지)

> "라이선스 창이 너무 없어보여. 문서를 README.txt 이런식으로 하지 말고, 프로젝트 라이선스 고지. 정도로 제목을 주고 문서 제목은 보여줄 필요 없어. FFmpeg 같은 경우에도 텍스트를 다 보여주지 말고 선택한 내용에 대한 라이선스 정보가 노출되는 수준이면 될 것 같아. 그리고 txt 구조 자체가 너무 못생겼어. 좀 더 많이 사용하는 방식을 사용해줘. 문구 같은 경우도 상용 서비스 수준으로 txt 파일을 보완해줘."

### 0.2 요구 분해와 판정

| # | 요구 | 판정 | 절 |
|---|------|------|----|
| **R1** | 창 제목을 "프로젝트 라이선스 고지" 수준으로. **파일명을 UI에 노출하지 않는다** | 채택. 오버레이 제목·설정 버튼 라벨을 모두 `프로젝트 라이선스 고지`로 통일하고, 정상 경로에서 파일명·문서 자체 제목을 **한 곳도 보여주지 않는다** | §3.1·§3.9 |
| **R2** | 전문을 통째로 쏟지 않는다. **선택 항목의 라이선스 정보 요약** 수준 | 채택. 화면의 기본 상태를 **컴포넌트 요약 카드**로 바꾸고, 전문은 `[라이선스 전문 보기]`로 한 단계 들어간다(2단 구조) | §2·§3.1 |
| **R3** | txt 구조·문구를 상용 수준으로. **"좀 더 많이 사용하는 방식"** | 채택. 파일명·서식·목차를 업계 통용 형식(`NOTICE`/`THIRD-PARTY` 관례 + SPDX 식별자)으로 재편 | §2.3·§4 |
| **R4** | 전체적으로 "없어 보이지 않게" | 채택. 좌측 파일 목록 + 우측 원문 덤프라는 **개발자 도구 형태를 폐기**하고, 카드·배지·정의 목록으로 채운다 | §3.2·§3.3 |

### 0.3 ⚠️ 법적 경계 — R2를 만족시키면서 GPLv3를 지키는 방법

이 앱은 **ffmpeg(GPLv3) 바이너리를 재배포**한다. 관련 의무는 [ffmpeg 설계 §2.4](./wpf-ffmpeg-licensing-and-distribution-design.md)의 O1~O5다.

R2("텍스트를 다 보여주지 말고")를 **"요약만 남기고 전문 경로를 없앤다"** 로 구현하면 안 된다. 채택 구조는 **요약 카드 + `[라이선스 전문 보기]`** 이며, 근거는 다음과 같다.

| 의무 | 산출물 | it24의 영향 | 판정 |
|------|--------|-------------|:---:|
| **O1** 라이선스 전문 전달(GPLv3 §4) | `licenses/FFmpeg-COPYING.GPLv3.txt` **배포물 동봉** | **불변.** 파일·csproj 동봉 배선·전문 내용을 건드리지 않는다. 앱 내 도달 경로도 **유지**된다(클릭 1회 → 2회) | ✅ 유지 |
| **O2** 저작권 고지 유지 + GPL 적용 사실 명시(§4) | 고지 txt + 앱 내 고지 | **강화.** 종전에는 사용자가 `FFmpeg-README.txt`를 **골라서 읽어야** GPL 적용 사실과 저작권자를 알 수 있었다. 요약 카드는 라이선스 이름·**SPDX 식별자**·**저작권 표시**를 열자마자 첫 화면에 띄운다 | ✅ 강화 |
| **O3** 대응 소스 접근 제공(§6) | 소스 URL 2곳 + 3년 서면 오퍼 | **강화.** 요약 카드에 `소스 제공` 행을 두어 첫 화면에서 그 사실을 알리고, 상세는 `[소스 코드 제공 안내]`로 연다 | ✅ 강화 |
| **O4** 수정 사실 표시 | 무수정 재배포 | 요약 카드 `배포 형태` 행에 "무수정 재배포"를 명시 | ✅ 유지 |
| **O5** 추가 제약 부과 금지 | 고지 txt 절 | 고지 txt에 그대로 유지(§4.6) | ✅ 유지 |

**결론**: 요약은 O2·O3의 **가시성을 높이고**, O1은 ① 배포물 동봉 파일(법적 산출물 자체) ② 앱 내 `[라이선스 전문 보기]`(발견성 보조) 두 경로로 유지된다. 즉 R2는 **전문을 삭제하라는 요구가 아니라 기본 화면에서 밀어내라는 요구**이며, 2단 구조가 그 해석을 그대로 구현한다.

| ⛔ 이 설계에서 금지 | 이유 |
|------|------|
| 전문 파일을 배포물에서 빼거나 요약본으로 대체 | GPLv3 §4 위반. `LicenseComplianceTests.GplV3_Full_Text_Is_Bundled`(`tests/MCPhoto.Tests/LicenseComplianceTests.cs:45`)가 600줄 초과를 잠그고 있다 |
| GPLv3 전문 파일의 **1글자라도 수정**(줄바꿈·머리말 포함) | 전문은 원문 그대로여야 효력이 있다. 서식 통일 대상에서 **제외**한다(§4.2) |
| `[라이선스 전문 보기]`에 `IsEnabled` 게이트 부착 | it23 AC-C1 — 고지 접근은 로그인·역할 무관(게스트·테스트 모드 포함) |
| 전문 파일 부재 시 버튼을 **숨기거나 비활성** | 누락을 감추는 것이다. 버튼은 활성 상태를 유지하고 **사유를 표시**한다(§2.7) |

### 0.4 it23이 세운 원칙 2개 — 약화 여부 확인

| 원칙 | it23 근거 | it24에서 |
|------|-----------|----------|
| **로그인·역할 무관 전원 접근** | AC-C1·AC-C2, `SettingsViewModel.cs:492-650`의 `[license-viewer:begin/end]` 구역이 계정을 참조하지 않음, 정적 검사 `T14b`(`SettingsViewModelLicenseTests.cs:294`) | **그대로 유지.** 신규 멤버도 전부 이 구역 안에 두고 `CurrentUser`·`IsLoggedIn`·`IsGuest`·`IsTempUser`·`Role`·`TestMode` 문자열을 쓰지 않는다(§3.7 규칙). 설정 버튼의 `IsEnabled` 미부착도 유지 |
| **누락을 감추지 않는다** | `LicenseNoticeService`가 폴더를 생성하지 않음(`LicenseNoticeService.cs:15-17`), 진단 화면의 존재 여부 행(`DiagnosticsViewModel.cs:143-162`) | **강화.** 매니페스트 기반 요약은 **열거로는 불가능했던 "있어야 할 파일이 없다"를 탐지**한다(§2.6). 진단 행은 그대로 두고 표기 문구만 갱신 검토(§7) |

### 0.5 검증된 사실 / 미검증 가정

**검증된 사실**은 §1에 VF-1~VF-18로 정리한다(전부 이번에 직접 확인).

| UV | 미검증 가정 | 검증 단계 |
|----|-------------|-----------|
| **UV-1** ✅ **해소(2026-08-11 실측)** | `licenses/`에 둔 `.json` 파일이 빌드 출력·publish 산출물·인스톨러에 **그대로 실린다** — 빌드 출력·테스트 출력·`dotnet publish` 산출물 **세 곳 모두**에서 `notice-manifest.json`(2,130 B) 확인. 인스톨러 `Excludes`의 7패턴 중 어느 것에도 걸리지 않음. 원래 서술:(csproj는 `**\*.*`이므로 실릴 것으로 보이나 `.json`으로 실측하지 않았다. 인스톨러 `Excludes`는 `*firebase*credentials*.json` 등 특정 패턴만 제외한다 — `installer/MCPhoto.iss:39-40`) | Step 1 |
| **UV-2** ✅ **해소(2026-08-11 실측)** | 테스트 실행 폴더(`AppContext.BaseDirectory/licenses`)에 신규 매니페스트가 포함되어 **배포 정합 테스트를 출력 폴더 기준으로 쓸 수 있다**(현재 `tests/MCPhoto.Tests/bin/*/licenses/`에 txt 4개가 실려 있음은 확인했다 — VF-6) | Step 1 |
| **UV-3** ⏸ **미해소** | 요약 카드 2장 + 헤더가 **창모드 하한 800×600**에서 세로 스크롤로 정상 조작된다(레이아웃 계산상 스크롤 필요 — §3.2). `ScrollViewer.VerticalScrollBarVisibility="Auto"`는 구현했으나 **렌더 결과를 육안 확인하지 않았다** | Step 6(사람 관측) |
| **UV-4** ⏸ **미해소** | `UserControl.InputBindings`의 `Key="Escape"`가 본문 `TextBox`에 포커스가 있을 때도 동작한다(`TextBox`는 Escape를 처리하지 않아 버블링될 것으로 보이나 실측 없음). VM의 3분기는 단위 테스트로 고정했고 `KeyBinding` 배선은 정적 테스트로 고정했으나 **실제 키 입력을 실측하지 않았다** | Step 6(사람 관측) |
| **UV-5** ☑ **기본값으로 진행** | 오버레이 제목·버튼 라벨을 `프로젝트 라이선스 고지`로 바꾸는 것이 사용자 의도와 일치한다(원문은 "제목"에 대한 지시이며 설정 버튼 라벨까지 지시하지는 않았다 — §3.9 각주). §11 Q1의 기본값(통일)으로 구현했다 — 되돌리려면 버튼 `Content` 1곳 + 테스트 정규식 1곳만 바꾸면 된다 | Step 0(사용자 사후 확인) |

---

## §1 현행 사실 (verified facts, 2026-08-11 직접 확인)

### 1.1 배포 산출물과 배선

| VF | 사실 | 근거 |
|----|------|------|
| VF-1 | 리포 `licenses/`에 **txt 3개**: `README.txt`(2,376 B) · `FFmpeg-README.txt`(7,791 B) · `FFmpeg-COPYING.GPLv3.txt`(35,149 B) | `ls licenses/` |
| VF-2 | 배포물에는 **4번째 파일** `MCPhoto-LICENSE-MIT.txt`가 추가된다. 물리 사본이 아니라 **리포 루트 `LICENSE`를 csproj가 링크 복사**한다 | `MCPhoto.App.csproj:85-98`(`McPhotoLicenseFile` + `Link="licenses\MCPhoto-LICENSE-MIT.txt"`) |
| VF-3 | csproj는 `licenses\**\*.*`를 **확장자 무관 전체 복사**하고 `%(RecursiveDir)`을 보존한다 → **폴더에 파일을 넣으면 그대로 배포된다** | `MCPhoto.App.csproj:89-92` |
| VF-4 | publish 산출물에도 `CopyLicensesToPublish` 타겟이 **명시 복사**한다(단일 파일 publish에서 `None` 조건이 뒤집히는 문제 대비 이중 안전) | `MCPhoto.App.csproj:103-115` |
| VF-5 | 인스톨러는 publish 폴더 전체를 `recursesubdirs`로 담는다. `Excludes`는 서비스 계정 키 류 특정 패턴만 제외한다 | `installer/MCPhoto.iss:36-40` |
| VF-6 | 테스트 프로젝트 출력 폴더에도 고지가 복사된다 — `tests/MCPhoto.Tests/bin/Debug/net8.0-windows/licenses/`에 **MIT 포함 4파일**이 실재한다 | 파일 목록 확인 |
| VF-7 | 고지 파일은 전부 **UTF-8 no BOM · CRLF**이며 `README.txt`·`FFmpeg-README.txt`는 한글을 포함한다 | it23 VF-14 재확인 + `Service_Reads_Real_Repo_License_Files`가 U+FFFD 부재를 잠금(`LicenseComplianceTests.cs:354`) |
| VF-8 | 루트 `LICENSE`는 MIT 전문 21줄(`Copyright (c) 2025 devmcjo`)이며 **csproj가 참조하는 단일 소스**다 | `LICENSE:1-21`, `MCPhoto.App.csproj:86` |

### 1.2 코드 (서비스 · VM · XAML)

| VF | 사실 | 근거 |
|----|------|------|
| VF-9 | `ILicenseNoticeService`는 4멤버: `FolderPath`·`Exists`·`ListDocuments()`·`ReadText(LicenseDocument)` | `Services/ILicenseNoticeService.cs:60-76` |
| VF-10 | `ListDocuments()`는 `licenses/**/*.txt` **재귀 열거** + `README.txt` 최상단 고정 + 나머지 `OrdinalIgnoreCase` 오름차순. 폴더 없음·열거 실패는 **빈 목록**(예외 없음), **폴더를 생성하지 않는다** | `LicenseNoticeService.cs:31`(`IndexFileName`), `:67-106` |
| VF-11 | `ReadText()`는 UTF-8 + BOM 감지, 선두 U+FEFF 1회 제거, `MaxDisplayBytes = 2 MB` 상한, 0바이트·실패를 **문구로** 반환 | `LicenseNoticeService.cs:28`, `:116-150` |
| VF-12 | VM 라이선스 구역은 `[license-viewer:begin]`(`:492`)~`[license-viewer:end]`(`:650`) 사이에 격리되어 있고 **계정·역할·테스트 모드를 참조하지 않는다** | `SettingsViewModel.cs:491-650` |
| VF-13 | 본문 로드는 `Task.Run` 오프로드 + **선택 스냅샷 비교로 stale 폐기** + `LicenseLoadTask`가 테스트 대기 이음새 | `SettingsViewModel.cs:542`, `:608-648` |
| VF-14 | 화면은 설정 화면 위 **오버레이**(`Grid.RowSpan="2"`로 sticky 저장 바까지 덮음) · scrim + 불투명 `Card`(`Brush.Bg`) · **신규 리소스 키 0개** | `SettingsView.xaml:562-667` |
| VF-15 | 현재 좌측은 **파일명을 그대로** 노출(`ListBox` + `DisplayName`), 우측은 **전문을 통째로** 렌더(`TextBox`, `NoWrap`, `Consolas`) — 사용자가 지적한 두 지점 | `SettingsView.xaml:615-627`, `:635-643` |
| VF-16 | `SettingsView.xaml`은 자체 `UserControl.Resources`에 로컬 스타일(`RowLabel`·`SettingRow`·`GroupTitle` 등)을 정의하며, **테마 키 검증 테스트가 로컬 `x:Key`를 제외**한다 → 로컬 스타일 추가는 안전하다 | `SettingsView.xaml:7-40`, `XamlResourceTests.cs:552-566` |
| VF-17 | 진단 모달은 고지를 **1줄 상태 행**으로만 표시한다(`정상(N개)` / `누락 — 배포 산출물에 고지가 없습니다`) | `DiagnosticsViewModel.cs:143-162`, `DiagnosticsWindow.xaml:316-321` |
| VF-18 | 앱에서 `System.Text.Json`을 쓰는 곳은 `MCPhoto.Http`뿐이며 공통 옵션은 camelCase + 대소문자 무시다. `MCPhoto.App`에는 JSON 소비 선례가 **없다** | `src/MCPhoto.Http/Dto/BackendJson.cs:12-17`, `grep System.Text.Json src/` |

### 1.3 테스트가 잠그고 있는 것 — 전수 (파일 구조를 바꿀 때 함께 갱신할 목록)

`tests/MCPhoto.Tests/LicenseComplianceTests.cs` (9건)

| 줄 | 테스트 | 무엇을 잠그는가 | it24에서 |
|----|--------|-----------------|----------|
| `:45` | `GplV3_Full_Text_Is_Bundled` | `FFmpeg-COPYING.GPLv3.txt` 존재 + 조항 표제 + 600줄 초과 | **무변경**(파일명·내용 불변) |
| `:64` | `Ffmpeg_Notice_Has_Version_Config_Source_And_Written_Offer` | `:66`이 **`FFmpeg-README.txt`를 읽는다**. 버전 `8.1.2`·`gyan.dev`·저작권 문장·`--enable-gpl`·`--enable-version3`·`--enable-libx264`·소스 URL 2곳·`3년`·연락처·`제한하지 않습니다`·전문 파일명 언급 | ⚠️ **개명 반영 필수**(`:66`) + SPDX 단정 추가 |
| `:97` | `License_Index_Lists_Ffmpeg_And_Keeps_Mcphoto_Mit` | `:99`가 **`README.txt`를 읽고** `FFmpeg`·`GPL`·`MIT`·`FFmpeg-README.txt`(`:104`)·`MCPhoto-LICENSE-MIT.txt`(`:107`) 언급 | ⚠️ **개명 반영 필수**(`:99`·`:104`) |
| `:116` | `Mcphoto_Mit_License_Is_Shipped_Into_Licenses_Folder` | 루트 `LICENSE` 존재 + csproj `McPhotoLicenseFile` 링크 규칙 + **`licenses/`에 MIT 물리 사본 없음** | **무변경**(루트 `LICENSE`·MIT 파일명 불변) |
| `:137` | `Csproj_Copies_Licenses_To_Output_And_Publish` | `LicensesSource`·`Link="licenses\`·`CopyLicensesToPublish`·`AfterTargets="Publish"` | **무변경**(csproj 손대지 않는다 — §4.7) |
| `:153` | `If_Ffmpeg_Is_Bundled_Then_Notice_Must_Exist` | ffmpeg 번들 규칙이 살아 있으면 `:161`의 **3파일명**이 반드시 존재 | ⚠️ **개명 + 매니페스트 추가 반영 필수**(`:161`) |
| `:196` | `Service_Path_Is_Licenses_Under_Base_Directory` | 경로 산출 + `Exists=false` | 무변경 |
| `:210` | `Service_Does_Not_Create_When_Missing` | 폴더를 **생성하지 않는다** | 무변경 |
| `:224` | `Service_Enumerates_Txt_Recursively_With_Index_First` | `:236`이 **`README.txt` 최상단**을 단정(임시 파일) | ⚠️ 색인 파일명 상수 개명 반영(`:226`·`:236`) |
| `:246`·`:263`·`:286`·`:307`·`:323` | `Service_Reads_Utf8_Korean…` / `…Strips_Bom…` / `…Rejects_Oversized…` / `…Reports_Empty_File` / `…Read_Failure_Returns_Message…` | 인코딩·BOM·상한·빈 파일·읽기 실패 문구 | 무변경(문구 동결 유지) |
| `:340` | `Service_Reads_Real_Repo_License_Files` | `:345`가 **`docs[0] == "README.txt"`**, `:346`이 필수 3파일명, `:357-362`이 한글 온전성·GPLv3 600줄 | ⚠️ **개명 반영 필수**(`:345`·`:346`) |

`tests/MCPhoto.Tests/SettingsViewModelLicenseTests.cs` (13건)

| 줄 | 테스트 | it24에서 |
|----|--------|----------|
| `:131`·`:152`·`:175`·`:189`·`:202`·`:218`·`:236` | 열기·선택 변경·빈 목록·서비스 null·읽기 실패·닫기 해제·재열거 | ⚠️ **재작성**(요약 경로가 기본이 되므로 단정 대상이 바뀐다) |
| `:258` | `T14_Works_For_Guest_Real_And_Test_Account` (AC-C1) | 유지 + **요약 화면 기준으로 단정 갱신** |
| `:294` | `T14b_License_Region_Has_No_Account_Or_Role_References` (AC-C2 정적) | **그대로 유지.** 금지 문자열 6종(`CurrentUser`·`IsLoggedIn`·`IsGuest`·`IsTempUser`·`Role`·`TestMode`)을 신규 멤버 이름에 쓰지 않는다 |
| `:317` | `License_Button_Is_Always_Enabled` | ⚠️ 정규식이 `Content="오픈소스 라이선스"`를 찾는다 → **라벨 변경 시 함께 갱신**(`:320`) |
| `:331` | `No_Folder_Path_In_Ui` | **그대로 유지 + 신규 문구도 이 단정을 통과해야 한다** — `FolderPath` 문자열 XAML 부재, 그리고 **공개 실패 문구에 `/`와 `:\`가 없어야 한다**. ⚠️ 신규 매니페스트 실패 문구에 `licenses/notice-manifest.json` 같은 경로·슬래시를 쓰면 **이 테스트가 깨진다** |
| `:353` | `License_Body_TextBox_Is_Selectable_NoWrap_And_Self_Scrolling` | 유지. 전문 `TextBox`가 `{Binding LicenseText}`·`NoWrap`·자체 스크롤·오버레이 `Grid.RowSpan="2"`임을 잠근다 → **본문 렌더링 규격을 바꾸지 않는 근거** |

`tests/MCPhoto.Tests/XamlResourceTests.cs`

| 줄 | 테스트 | it24에서 |
|----|--------|----------|
| `:548` | `SettingsView_StaticResource_Keys_Resolve_In_Theme` | 유지. **로컬 `x:Key`는 검증 제외**(`:552-554`)이므로 `SettingsView.xaml`에 로컬 스타일을 추가해도 통과한다 |
| `:884` | `SettingsView_License_Viewer_Bindings_Exist_On_Vm` (C-T16) | ⚠️ **바인딩 멤버 목록 갱신 필수**(`:891-893`). `:910-913`의 `DisplayName` 항목 템플릿 단정은 **폴백 목록**에 같은 속성을 유지하면 통과한다(§2.7) |

`tests/MCPhoto.Tests/DiagnosticsViewModelTests.cs:408` (`C-T15`) — 진단 상태 행. 표기 문구를 바꾸면 함께 갱신.

### 1.4 사용자가 지적한 지점의 정확한 소재

| 지적 | 소재 |
|------|------|
| "문서를 README.txt 이런식으로" | `SettingsView.xaml:622`(`Text="{Binding DisplayName}"`) + `ILicenseNoticeService.cs:8-11`(별칭 금지 근거 — **이 근거가 it24에서 무효화된다**, §2.1) |
| "제목" | `SettingsView.xaml:588`(`오픈소스 라이선스`) |
| "FFmpeg … 텍스트를 다 보여주지 말고" | `SettingsView.xaml:635-643`(전문 `TextBox`가 기본 상태) + `SettingsViewModel.cs:582`(열자마자 첫 문서 본문 로드) |
| "txt 구조 자체가 너무 못생겼어" | `licenses/README.txt:1-4`·`FFmpeg-README.txt:1-4`의 80열 `====` 배너와 `----` 벽(§4.1) |

---

# A부 · 정보 모델 — "요약"이 무엇인가

## §2.1 단위는 파일이 아니라 **컴포넌트**다

현행은 **파일 1개 = 목록 항목 1개**다(VF-10·VF-15). 이 매핑이 사용자 불만의 근원이다.

| 파일 | 사용자가 보는 것 | 사용자가 보고 싶은 것 |
|------|------------------|----------------------|
| `README.txt` | 목록 첫 줄 "README.txt" | — (색인은 UI가 하는 일이다. 파일로 볼 이유가 없다) |
| `FFmpeg-README.txt` | "FFmpeg-README.txt" | **FFmpeg가 GPLv3다 · 버전 8.1.2 · 동영상 인코딩에 쓴다 · 소스는 이렇게 받는다** |
| `FFmpeg-COPYING.GPLv3.txt` | "FFmpeg-COPYING.GPLv3.txt" | 위 항목의 **전문**(필요할 때만) |
| `MCPhoto-LICENSE-MIT.txt` | "MCPhoto-LICENSE-MIT.txt" | **MC포토 본체는 MIT다** |

즉 4개 파일은 **2개 컴포넌트**(MC포토 본체, FFmpeg)를 4조각으로 흩어 놓은 것이다. 사용자 요구("선택한 내용에 대한 라이선스 정보")의 "내용"은 컴포넌트이며, 파일은 그 컴포넌트의 **첨부물**이다.

> ⚠️ it23이 별칭을 금지한 근거(`ILicenseNoticeService.cs:8-11`: "`README.txt`가 다른 파일을 파일명으로 상호 참조하므로 친절한 별칭을 붙이면 안내가 목록과 어긋난다")는 **it24에서 무효화된다.** 색인의 역할을 UI(요약 카드)가 가져가고, txt 간 상호 참조는 §4.6에서 "같은 폴더의 다음 파일" 형태로 유지하되 **UI가 그 문장을 대신 읽어주지 않기 때문**이다. 이 무효화를 명시적으로 기록해 두지 않으면 다음 사람이 "왜 별칭을 쓰냐"고 되돌린다.

## §2.2 컴포넌트 요약 필드 규격

| 필드 | 예(FFmpeg) | 필수 | 근거 / 의무 |
|------|-----------|:---:|------|
| `name` | `FFmpeg` | ✅ | 사용자가 아는 이름 |
| `version` | `8.1.2-essentials_build-www.gyan.dev` | — | 어떤 바이너리인지 특정 가능해야 한다(대응 소스의 대상 확정, O3). **MC포토 본체는 비운다** — 어셈블리 버전과 이중 관리가 되면 반드시 어긋난다(§2.5 규칙 M4) |
| `licenseName` | `GNU General Public License v3.0 or later` | ✅ | 사람이 읽는 라이선스 이름(O2) |
| `spdxId` | `GPL-3.0-or-later` | ✅ | 업계 표준 식별자(§2.3). 배지로 강조 표시 |
| `copyright` | `Copyright (c) 2000-2026 the FFmpeg developers` | — | **GPLv3 §4는 저작권 고지 유지를 요구**한다(O2). ffmpeg 설계 §10.5.1 D-2가 이 누락을 결함으로 잡은 항목이다 |
| `purpose` | `동영상 녹화 · 타임랩스 인코딩` | — | "왜 이 소프트웨어가 여기 있나"에 답한다. 상용 고지 화면의 관례 |
| `distribution` | `별도 실행 파일로 동봉 · 무수정 재배포 · 서브프로세스 호출` | — | O4(수정 사실 표시) + MIT 유지 근거(파생저작물 아님, ffmpeg 설계 §2.3)를 한 줄로 |
| `sourceOffer` | `GPLv3 제6조에 따라 대응 소스 코드를 제공합니다.` | — | **O3의 첫 화면 노출**. 값이 있으면 카드에 행이 생기고 `[소스 코드 제공 안내]` 버튼이 붙는다 |
| `fullTextFile` | `FFmpeg-COPYING.GPLv3.txt` | ✅ | `[라이선스 전문 보기]`의 대상(O1 도달 경로) |
| `noticeFile` | `FFmpeg-NOTICE.txt` | — | `[소스 코드 제공 안내]`의 대상. 없으면 버튼 미생성 |
| `kind` | `redistributed` \| `self` | ✅ | 섹션 구분("이 앱 본체" / "동봉된 오픈소스"). 재배포 대상과 아닌 것의 구분은 현행 `README.txt`도 하고 있는 정보다 |

> `id`는 **두지 않는다.** 표시에 쓰이지 않고 코드가 분기하지 않는다(분기하면 매니페스트가 데이터가 아니라 코드가 된다). 순서는 배열 순서가 곧 표시 순서다.

## §2.3 업계 통용 형식 — 조사 근거

사용자 요구 R3("좀 더 많이 사용하는 방식")에 대한 조사 결과다.

| 관례 | 무엇인가 | it24 적용 |
|------|----------|-----------|
| **SPDX 식별자** | 라이선스마다 표준 짧은 식별자(`MIT`, `GPL-3.0-or-later`)·정식 이름·전문·정규 URL이 부여된 목록. "짧은 식별자를 쓰면 전문을 중복 재현하지 않고 라이선스를 정확·간결·언어중립·기계처리 가능하게 지목할 수 있다"([SPDX License List](https://spdx.org/licenses/), [Handling License Info](https://spdx.dev/learn/handling-license-info/)) | 요약 카드의 **배지**와 매니페스트 `spdxId`, 그리고 고지 txt의 `SPDX-License-Identifier:` 줄로 채택. **정확히 R2가 요구하는 것** — 전문을 쏟지 않고 라이선스를 특정하는 표준 수단이다 |
| **`SPDX-License-Identifier:` 헤더 줄** | 파일에 한 줄만 추가하면 라이선스를 지목할 수 있고, **기존 저작권 고지는 지우거나 수정하지 않는다**([Annex E, SPDX 2.3](https://spdx.github.io/spdx-spec/v2.3/using-SPDX-short-identifiers-in-source-files/)) | 고지 txt의 컴포넌트 절 머리에 1줄 추가(§4.6). "저작권 고지를 지우지 않는다"는 규칙은 GPLv3 §4 O2와 같은 방향이다 |
| **`NOTICE` 파일** | 배포물에 라이선스·고지 텍스트를 모아 두는 관례. AOSP가 `NOTICE` 파일로 제3자 고지를 관리한다 | 색인 파일명을 `README.txt` → **`NOTICE.txt`**(§4.3). `README`는 개발자 문서로 읽히고, 사용자가 명시적으로 거부한 이름이다 |
| **Google Play services `oss-licenses`**: 메타데이터와 전문의 **분리** | 플러그인이 `res/raw/third_party_license_metadata`(의존성 이름 목록)와 `res/raw/third_party_licenses`(라이선스 텍스트)를 **각각** 생성한다. `OssLicensesMenuActivity`가 메타데이터로 **목록을 그리고**, 항목을 누르면 `OssLicensesActivity`가 **실제 전문을 보여준다**([Include open source notices](https://developers.google.com/android/guides/opensource), [OssLicensesActivity](https://developers.google.com/android/reference/com/google/android/gms/oss/licenses/OssLicensesActivity)) | **it24의 2단 구조·2파일 구조가 이 관례와 동형이다.** 목록용 메타데이터(`notice-manifest.json`) + 전문(txt)의 분리, "목록 → 눌러서 전문"의 내비게이션까지 같다. 즉 §2.4의 (c)안은 우리 발명이 아니라 모바일 플랫폼의 표준 형태다 |
| **`THIRD-PARTY-NOTICES.txt`**(.NET/Microsoft 관례) | 배포물 루트에 제3자 고지를 컴포넌트별 절로 모은 단일 txt | 채택하지 **않는다** — 컴포넌트별 파일 1:1 매핑이 매니페스트·UI와 정합하고(§4.3 판정), 하나로 합치면 `[소스 코드 제공 안내]`가 무관한 컴포넌트의 절까지 보여준다 |

## §2.4 ⭐ 판정 — 요약 메타데이터를 어디에 두는가

| 항목 | (a) 코드 하드코딩 | (b) 기존 txt에서 파싱 | **(c) 구조화 파일 + 전문 txt 분리** |
|------|-------------------|----------------------|-----------------------------------|
| 형태 | `LicenseComponent[]` 상수를 `Services/`에 둔다 | `FFmpeg-README.txt`의 "버전 / Version : …" 행을 정규식으로 긁는다 | `licenses/notice-manifest.json` 신설, 전문·상세는 txt 유지 |
| **배포 누락 감지** | △ 카드는 항상 그려지므로 **전문 파일이 없어도 정상처럼 보인다**(별도 존재 검사 필요) | ❌ 파일이 없으면 카드도 사라져 **누락이 곧 침묵**이다(현행 열거 방식의 결함) | ✅ **매니페스트가 "있어야 할 파일"을 선언**하므로 부재를 탐지해 화면에 띄울 수 있다 |
| 새 고지 추가 시 | ❌ **코드 수정 + 재빌드**. 폴더에 파일만 넣으면 요약에 안 나온다(it23이 하드코딩 목록을 거부한 그 실패, `LicenseNoticeService.cs:59-61`) | △ 파일 추가로 반영되지만 서식을 정확히 맞춰야 한다 | ✅ 매니페스트 항목 1개 추가. **재빌드 불요**(csproj가 폴더를 통째로 복사 — VF-3) |
| 유지보수 | ❌ 법적 문구가 **코드와 txt 두 곳**에 산다. 어긋나도 아무도 모른다 | ✅ 단일 소스 | △ 매니페스트와 txt에 버전·저작권이 중복 → **§2.5 M3의 정합 테스트로 봉인** |
| 파싱 실패 처리 | 해당 없음 | ❌ **한국어 산문의 문구를 고치면 카드가 조용히 깨진다.** 사용자가 요구한 "문구 개선"과 정면 충돌 | ✅ 실패 지점이 기계용 파일 1개로 한정 → 폴백 규칙을 명확히 정의 가능(§2.7) |
| csproj 복사 규칙 | 변경 불요 | 변경 불요 | ✅ **변경 불요**(VF-3 — `**\*.*`가 `.json`도 복사한다. 단 UV-1로 실측) |
| 테스트 가능성 | △ 코드 상수는 스스로와 일치할 뿐이다 | ❌ 문구를 바꾸면 테스트가 함께 깨져 회귀 신호가 아니라 잡음이 된다 | ✅ **매니페스트 ↔ 실제 파일 ↔ txt 내용의 3자 정합**을 단정할 수 있다(§6) |
| 업계 선례 | — | — | ✅ Play services `oss-licenses`가 메타데이터·전문을 분리한다(§2.3) |
| **판정** | ❌ | ❌ | ✅ **채택** |

**(a)를 버리는 결정적 이유**: 법적 고지의 단일 소스는 **배포물에 동봉된 파일**이어야 한다(ffmpeg 설계 §10.1의 전제). 요약을 exe 안 상수로 옮기면, 고지 폴더를 교체·수정한 배포물에서 **화면과 파일이 서로 다른 말을 한다.** 그것도 라이선스 사고의 한 형태다.

**(b)를 버리는 결정적 이유**: 사용자가 이번에 지시한 작업이 바로 **"txt 문구를 상용 수준으로 다시 써라"** 다. 문구를 UI 파서의 입력으로 만들면, 앞으로 문구를 손질할 때마다 화면이 깨질 위험을 안는다. 산문과 기계 데이터를 같은 파일에 섞지 않는다.

## §2.5 채택안 상세 — `licenses/notice-manifest.json`

**형식은 JSON**(`System.Text.Json`). INI를 쓰지 않는 이유: 컴포넌트가 배열이고 값에 문장이 들어가므로 섹션·키 구조로는 어색하고, `IniFile`은 `[MCPhoto]` 설정 파일 전용 자산이다(주석·개행 손실 이력 — it23 §B4). 확장자를 `.json`으로 두면 **`ListDocuments()`의 `*.txt` 패턴에 걸리지 않아 전문 목록에 기계용 파일이 섞이지 않는다**(VF-10).

```jsonc
{
  "schemaVersion": 1,
  "updatedOn": "2026-08-11",
  "components": [
    {
      "kind": "self",
      "name": "MC포토 (MCPhoto)",
      "version": null,                       // M4: 본체 버전은 매니페스트에 적지 않는다
      "licenseName": "MIT License",
      "spdxId": "MIT",
      "copyright": "Copyright (c) 2025 devmcjo",
      "purpose": "포토부스 촬영·편집·인쇄 애플리케이션",
      "distribution": "이 소프트웨어 본체",
      "sourceOffer": null,
      "fullTextFile": "MCPhoto-LICENSE-MIT.txt",
      "noticeFile": null
    },
    {
      "kind": "redistributed",
      "name": "FFmpeg",
      "version": "8.1.2-essentials_build-www.gyan.dev",
      "licenseName": "GNU General Public License v3.0 or later",
      "spdxId": "GPL-3.0-or-later",
      "copyright": "Copyright (c) 2000-2026 the FFmpeg developers",
      "purpose": "동영상 녹화 · 타임랩스 인코딩",
      "distribution": "별도 실행 파일로 동봉 · 수정하지 않고 재배포 · 서브프로세스로 호출",
      "sourceOffer": "GPLv3 제6조에 따라 대응 소스 코드를 제공합니다(제공 경로·3년 서면 오퍼 포함).",
      "fullTextFile": "FFmpeg-COPYING.GPLv3.txt",
      "noticeFile": "FFmpeg-NOTICE.txt"
    }
  ]
}
```

| # | 규칙 | 이유 |
|---|------|------|
| **M1** | 역직렬화 옵션은 `PropertyNamingPolicy = CamelCase` + `PropertyNameCaseInsensitive = true` + `ReadCommentHandling = Skip` + `AllowTrailingCommas = true` | 앞 두 개는 리포 선례(`BackendJson.cs:12-17`). 뒤 두 개는 **사람이 손으로 편집하는 파일**이라 주석·후행 콤마를 허용한다(설명 주석을 달아 둘 수 있어야 유지보수가 산다) |
| **M2** | 배열 순서 = 표시 순서. 코드가 **재정렬하지 않는다**. 단 `kind:"self"` 항목이 첫 번째여야 한다(테스트로 고정) | 편집자가 순서를 통제한다. "본체 → 동봉 구성요소" 순서는 현행 `README.txt`의 구성과 같다 |
| **M3** | 매니페스트의 `version`·`copyright`는 **해당 컴포넌트의 `noticeFile` 본문에 그 문자열이 그대로 존재해야 한다**(테스트로 고정) | 중복을 없앨 수 없으니 **어긋남을 CI가 잡게** 한다. 이것이 (c)안의 유일한 약점에 대한 대가 |
| **M4** | `kind:"self"` 항목의 `version`은 **반드시 `null`**(테스트로 고정) | 앱 버전은 어셈블리 리소스가 단일 소스다(it18). 매니페스트에 적으면 릴리스마다 어긋난다. 화면은 `버전` 행을 생략한다 |
| **M5** | `fullTextFile`·`noticeFile`은 **파일명만**(경로 구분자·`..`·드라이브 문자 금지). 위반 항목은 파일 참조를 **무효로 간주**해 §2.7 D3으로 처리 + Warning 로그 | 매니페스트는 배포물의 데이터다. 경로 탈출을 허용하면 고지 화면이 임의 파일 리더가 된다 |
| **M6** | 필수 필드(`kind`·`name`·`licenseName`·`spdxId`·`fullTextFile`) 중 하나라도 비면 **매니페스트 전체를 손상으로 판정**(§2.7 D2) | 부분적으로 맞는 법적 고지 목록은 명시적 강등보다 위험하다. 이 파일은 우리가 쓰고 테스트가 잠그므로 이 경로는 "깨진 빌드"에서만 발생한다 |
| **M7** | `schemaVersion != 1`이면 D2(손상)로 처리 | 앱보다 새로운 매니페스트를 억지로 해석하면 잘못된 고지를 표시한다 |
| **M8** | 빈 문자열은 파서에서 **`null`로 정규화**한다 | 화면의 행 표시 여부가 `null` 하나로 결정된다(`Has*` 계산 속성 — §3.3) |
| **M9** | 파일 크기 상한은 기존 `MaxDisplayBytes`(2 MB)를 재사용 | 별도 상한을 새로 만들 이유가 없다 |

## §2.6 열거 ∪ 매니페스트 — 양방향 diff (누락을 감추지 않는다)

매니페스트만 보면 **폴더에 새로 들어온 고지 파일이 안 보인다**(it23이 하드코딩을 거부한 그 실패). 열거만 보면 **있어야 할 파일이 없는 것을 모른다**(현행 결함). 둘을 교차한다.

| 방향 | 상황 | 화면 | 개발 시점 방어 |
|------|------|------|----------------|
| 매니페스트 → 파일 | `fullTextFile`/`noticeFile`이 폴더에 **없다** | 카드는 **그대로 표시**하고, 그 카드 안에 경고 1줄(F7). `[라이선스 전문 보기]`는 **활성 유지**하고 누르면 사유를 표시한다 | 배포 정합 테스트가 CI에서 실패(§6 T-M2) |
| 파일 → 매니페스트 | 폴더의 `.txt`가 어떤 항목에서도 참조되지 **않는다** | Level 1 하단에 `그 외 동봉 고지 문서` 섹션으로 **파일명 그대로 나열**(누르면 전문 표시) | 리포 자산 테스트가 실패(§6 T-M3) — 우리 배포물에는 미참조 문서가 없어야 한다 |

> **파일명 노출의 유일한 예외**가 이 `그 외 동봉 고지 문서` 섹션과 §2.7의 폴백 목록이다. 근거: 매니페스트에 없는 파일에 대해 우리가 아는 정보는 **파일명뿐**이며, 이름을 감추면 "무엇이 안 실려야 할 것이 실렸는지"를 아무도 알 수 없다. 요구 R1은 정상 경로의 화면 품질에 대한 것이고, 이 섹션은 **정상 배포물에서는 렌더링되지 않는다**(항목 0개면 `Collapsed`).

## §2.7 파싱·읽기 실패 → 무엇을 보여주는가 (침묵 실패 금지)

| # | 상황 | 감지 | 화면 등급 | 문구(동결) |
|---|------|------|-----------|------------|
| **D1** | 매니페스트 파일 **없음** | `File.Exists == false` | **강등**: 경고 배너 + 폴백 문서 목록(파일명 기반, it23 형태) | `라이선스 요약 정보를 찾을 수 없어 동봉된 고지 문서를 그대로 표시합니다. 배포 산출물이 불완전할 수 있으므로 개발자에게 알려주세요.` |
| **D2** | JSON 손상 · `schemaVersion` 불일치 · 필수 필드 누락 · 항목 0개 | 역직렬화 예외 또는 M6·M7 위반 | **강등**(D1과 동일 UI) | `라이선스 요약 정보를 읽을 수 없어 동봉된 고지 문서를 그대로 표시합니다. 배포 산출물이 불완전할 수 있으므로 개발자에게 알려주세요.` |
| **D3** | 항목이 가리키는 파일 부재 · M5 위반 | 파일 존재 검사 | **정상 화면 유지 + 카드 내 경고 1줄**(F7) | `이 항목의 고지 파일이 배포물에 없습니다. 개발자에게 알려주세요.` |
| **F1·F2·F6** | 고지 폴더 부재 · `.txt` 0개 · 열거 실패 | it23과 동일 | 배너만(목록·카드 없음) | **it23 문구 그대로 유지**(`SettingsViewModel.cs:501-511`) |
| **F3·F4·F5** | 전문 읽기 실패 · 2 MB 초과 · 0바이트 | it23과 동일 | 전문 화면 자리에 문구 | **it23 문구 그대로 유지**(`LicenseNoticeService.cs:34-35`, `:53-56`) |

| 규칙 | 이유 |
|------|------|
| 강등(D1·D2)에서도 **전문 도달 경로를 유지**한다 | GPLv3 §4 이행의 마지막 그물. 요약이 깨졌다고 전문을 못 보게 되면 이 재설계가 법적 후퇴가 된다 |
| 강등 배너는 **경고색**(`Brush.Warning.Surface`/`Brush.Warning`)이고 닫을 수 없다 | 배포 사고를 현장에서 드러낸다(it23 원칙 승계) |
| 모든 문구에 **경로·슬래시·파일명을 넣지 않는다** | 요구("경로를 적어주지 말고") + `No_Folder_Path_In_Ui`(`SettingsViewModelLicenseTests.cs:331`)가 공개 문구에서 `/`·`:\`를 금지한다. ⚠️ `licenses/notice-manifest.json`처럼 쓰면 **테스트가 깨진다** |
| 경로·파일명·예외는 **Warning 로그**에만 | 개발자는 로그로 충분하다(it23 §C6 규칙 승계) |
| 크래시 금지 | 예외가 새면 `DispatcherUnhandledException` → 설정 화면이 통째로 닫힌다 |

## §2.8 서비스 계약 확장 (`ILicenseNoticeService`)

기존 4멤버(VF-9)를 **유지**하고 2개를 더한다. 기존 멤버를 바꾸지 않는 이유: 폴백 경로(D1·D2)와 `그 외 문서` 섹션이 그대로 쓰고, 잠금 테스트 5건(`:246`~`:340`)이 계속 유효하다.

| 멤버 | 형태 | 비고 |
|------|------|------|
| `LicenseSummary ReadSummary()` | 신규 | 매니페스트 읽기 + 파일 존재 교차 검사 + 미참조 문서 산출. **예외를 던지지 않는다** |
| `LicenseTextResult ReadText(string fileName)` | 신규 오버로드 | 매니페스트가 이름으로 지목한 파일을 읽는다. **M5 검사**(구분자·`..`·루트 금지 + 결합 결과가 고지 폴더 하위인지 재확인) 후 기존 `ReadText(LicenseDocument)` 경로로 위임 |
| `FolderPath` · `Exists` · `ListDocuments()` · `ReadText(LicenseDocument)` | **무변경** | 폴백·진단·`그 외 문서`가 사용 |

```csharp
// 요약 1건 — 화면 카드 1장에 대응. 표시 여부용 Has* 는 계산 속성(신규 컨버터 0개 — §3.6)
public sealed record LicenseComponent(
    bool IsSelf, string Name, string? Version, string LicenseName, string SpdxId,
    string? Copyright, string? Purpose, string? Distribution, string? SourceOffer,
    string FullTextFile, string? NoticeFile, bool IsFullTextMissing, bool IsNoticeMissing);

// 요약 전체. DegradedMessage != null 이면 화면은 §2.7 강등 등급으로 간다.
public sealed record LicenseSummary(
    IReadOnlyList<LicenseComponent> Components,
    IReadOnlyList<LicenseDocument> UnlistedDocuments,
    string? DegradedMessage);
```

| 결정 | 근거 |
|------|------|
| 동기 API 유지(`async` 없음) | 호출자(VM)가 `Task.Run`으로 감싼다. it23 §C7.1의 판정·`LogFolderService` 선례와 동형 |
| `baseDirectory` 주입 이음새 **보존** | 테스트 8건이 임시 폴더로 검증한다(`LicenseComplianceTests.cs:173-186`) |
| `ReadSummary()`가 **파일 존재까지 확인**한다(VM이 아니라) | 존재 검사와 매니페스트 해석이 한 트랜잭션이어야 D3 판정이 한 곳에서 난다. VM은 표시만 한다 |
| 매니페스트 파일명 상수는 서비스 내부 `private const` | 화면·문구에 이름이 새지 않는다(§2.7 규칙) |

---

# B부 · UI 재설계

## §3.1 구조 결정 — 2단(요약 → 전문), 좌우 분할 폐기

| 안 | 형태 | 판정 |
|----|------|:---:|
| ① 좌측 컴포넌트 목록 + 우측 요약 상세(현행 골격 유지) | 파일명만 컴포넌트명으로 교체 | ❌ **항목이 2개다.** 260px 컬럼에 2줄, 아래는 400px 공백 — 사용자가 지적한 "없어보여"가 오히려 심해진다 |
| **② 요약 카드 세로 목록 → `[전문 보기]`로 페이지 전환** | Level 1 = 카드, Level 2 = 전문 | ✅ **채택.** 카드 2장이 화면을 채우고, 선택 상태가 필요 없으며(항목이 곧 상세), 전문은 한 단계 뒤로 밀린다(R2) |
| ③ 카드 목록 + 하위 오버레이(오버레이 위 오버레이) | 전문을 2중 오버레이로 | ❌ scrim 2겹으로 어두워지고 Esc 처리가 3단이 된다. 이득 없음 |
| ④ 새 `Window` | 전문 전용 창 | ❌ headless 테스트에서 인스턴스화 불가(it23 C2-8) |

**Level 1 ↔ Level 2는 같은 오버레이 안에서 `Visibility` 전환**이다(`Grid` 두 개를 형제로 두고 하나만 보인다). 새 `AppState`·새 `Window`·`Frame` 내비게이션을 쓰지 않는다 — 촬영 상태 기계를 라이선스 표시 때문에 건드리지 않는다(it23 §C3 ④ 판정 승계).

> ⚠️ **it23의 함정 재확인**: Level 1은 `ScrollViewer`가 필요하고(§3.2), Level 2의 전문 `TextBox`는 **`ScrollViewer` 안에 들어가면 안 된다**(무한 높이 요구 → 자체 스크롤 사망). 두 페이지가 **형제**이므로 이 조건이 구조적으로 성립한다. 절대 Level 2를 Level 1의 `ScrollViewer` 안에 넣지 말 것.

## §3.2 레이아웃 스케치

### Level 1 — 요약 (기본 상태)

```
┌─ Grid.RowSpan=2 · Background=Brush.Scrim ──────────────────────────────────────┐
│  ┌─ Border Style=Card · Background=Brush.Bg · MaxWidth=1000 · Margin=40 ─────┐ │
│  │ row0  프로젝트 라이선스 고지                                    [ 닫기 ]   │ │  Text.H2(24) / Button.Ghost
│  │       이 소프트웨어가 사용·동봉하는 오픈소스 구성 요소와 라이선스입니다.   │ │  Text.Caption(13, Muted)
│  │       ─────────────────────────────────────────────────────────────────── │ │  Brush.Divider 1px, Margin 0,16,0,16
│  │ row1  (강등 시에만) ⚠ 경고 배너                                           │ │  Brush.Warning.Surface
│  │ row2  ScrollViewer(Vertical=Auto)                                         │ │
│  │       ┌─ 카드: 이 소프트웨어 ─────────────────────────────────────────┐   │ │
│  │       │ MC포토 (MCPhoto)                              [ MIT ]        │   │ │  Text.Title(20) / 배지
│  │       │ 포토부스 촬영·편집·인쇄 애플리케이션                          │   │ │  Text.Body(16)
│  │       │ ───────────────────────────────────────────────────────────  │   │ │
│  │       │ 라이선스   MIT License                                       │   │ │  라벨 Text.Label(14,Tertiary)
│  │       │ 저작권     Copyright (c) 2025 devmcjo                        │   │ │  값   Text.Body(16,Secondary)
│  │       │ 배포 형태   이 소프트웨어 본체                                │   │ │
│  │       │                                    [ 라이선스 전문 보기 ]     │   │ │  Button.Secondary, 우측 정렬
│  │       └─────────────────────────────────────────────────────────────┘   │ │
│  │       ┌─ 카드: 동봉된 오픈소스 ──────────────────────────────────────┐   │ │  Space.M(16) 간격
│  │       │ FFmpeg                              [ GPL-3.0-or-later ]     │   │ │
│  │       │ 동영상 녹화 · 타임랩스 인코딩                                 │   │ │
│  │       │ ───────────────────────────────────────────────────────────  │   │ │
│  │       │ 라이선스   GNU General Public License v3.0 or later          │   │ │
│  │       │ 버전       8.1.2-essentials_build-www.gyan.dev               │   │ │
│  │       │ 저작권     Copyright (c) 2000-2026 the FFmpeg developers      │   │ │
│  │       │ 배포 형태   별도 실행 파일로 동봉 · 수정하지 않고 재배포 …      │   │ │
│  │       │ 소스 제공   GPLv3 제6조에 따라 대응 소스 코드를 제공합니다 …    │   │ │
│  │       │            [ 소스 코드 제공 안내 ]  [ 라이선스 전문 보기 ]     │   │ │
│  │       └─────────────────────────────────────────────────────────────┘   │ │
│  │       (항목 있을 때만) 그 외 동봉 고지 문서                              │ │
│  │       · MCPhoto-LICENSE-MIT.txt …                                        │ │
│  │ row3  고지 문서는 설치 폴더에 함께 배포됩니다.            (2026-08-11 기준) │ │  Text.Caption(13,Muted)
│  └───────────────────────────────────────────────────────────────────────────┘ │
└────────────────────────────────────────────────────────────────────────────────┘
```

### Level 2 — 전문

```
│ row0  [ ← 뒤로 ]  FFmpeg · GPL-3.0-or-later               [ 닫기 ]  │  ← 뒤로=Button.Ghost
│       라이선스 전문                                                │  Text.Caption
│ row2  ┌─ TextBox (IsReadOnly · AcceptsReturn · NoWrap ·  ─────┐    │  ⚠️ ScrollViewer로 감싸지 않는다
│       │  Vertical/Horizontal ScrollBar=Auto ·               │    │
│       │  Consolas, D2Coding, Malgun Gothic 13 ·             │    │
│       │  Brush.Surface.Alt + Brush.Divider 1px)             │    │
│       └────────────────────────────────────────────────────┘    │
│ row3  (실패 시 F3~F5 문구 · 로딩 중 `불러오는 중…`)                │
```

| 치수 규칙 | 값 | 근거 |
|-----------|-----|------|
| 오버레이 Card | `MaxWidth=1000` · `Margin=40` · `Padding` = `Pad.L`(24) | 현행 유지(VF-14). 1280×800 기준 내부 폭 ≈ 950 |
| 카드 간격 | `Space.M`(16) | 설정 화면 PC 밀도 관례(it5 U7 — 행 간격 8, 블록 간격 16) |
| 카드 내부 | `Padding=Pad.L`(24) · 제목↔용도 `Space.XS`(4) · 구분선 위아래 `Space.M`(16) · 메타 행 간격 `Space.S`(8) | 기존 `Card` 스타일(`Controls.xaml:291-298`)이 이미 `Pad.L`이다 |
| 메타 그리드 | 라벨 컬럼 **고정 96** + 값 `*` | ⚠️ 리포에 고정폭 잘림 이력이 있다 → 라벨은 최장 `배포 형태`(5자)이고 `Text.Label` 14px 기준 96px로 충분하다. 값은 `TextWrapping=Wrap`(`Text.Body` 기본) |
| 헤더 구분선 | `Brush.Divider` 1px · `Margin=0,16,0,16` | 설정 화면 `GroupDivider` 로컬 스타일과 동형 |
| **세로 스크롤** | Level 1 `ScrollViewer.VerticalScrollBarVisibility="Auto"` | 창모드 하한이 **800×600**(it21)이다. 그때 카드 영역 가용 높이 ≈ 600−80(Margin)−48(Padding)−120(헤더·푸터) ≈ 350 → 스크롤 필수(UV-3) |
| 반응 | 카드 폭은 `*`(스트레치). 고정 폭을 쓰지 않는다 | 전체화면(1920)에서도 `MaxWidth=1000`이 상한을 잡는다 |

## §3.3 요약 카드 규격

| 요소 | 규격 |
|------|------|
| 섹션 머리 | `이 소프트웨어` / `동봉된 오픈소스` — `Text.Label`(14, Tertiary) + `Space.S` 아래 여백. `kind`로 그룹핑하되 **그룹 헤더는 해당 그룹에 항목이 있을 때만** 표시 |
| 카드 컨테이너 | `Border Style="{StaticResource Card}"`. 배경은 스타일 기본값 `Brush.Surface`(오버레이 Card가 `Brush.Bg`=흰색이므로 **카드가 한 단계 떠 보인다** — 이것이 "없어 보임"을 해소하는 핵심 대비다) |
| 컴포넌트 이름 | `Text.Title`(20, SemiBold, Primary) |
| SPDX 배지 | `Border` + `Background=Brush.Accent.Soft` + `CornerRadius=Radius.Pill` + `Padding=10,3` + `TextBlock Style=Text.Caption Foreground=Brush.Accent.Text`. 우측 상단 정렬 |
| 용도 | `Text.Body`(16, Secondary). 값이 없으면 행 자체를 `Collapsed` |
| 메타 행 | `라이선스` · `버전` · `저작권` · `배포 형태` · `소스 제공` 순서 고정. 각 행은 `Has*` 계산 속성 + `BoolToVis`로 표시 제어 |
| **누락 경고(F7)** | `IsFullTextMissing`일 때 카드 하단에 `Brush.Warning.Surface` 배경 1줄. 카드를 숨기지 않는다(§2.6) |
| 액션 버튼 | 우측 정렬 `StackPanel Orientation=Horizontal`. `[소스 코드 제공 안내]`는 `Button.Ghost`(부차), `[라이선스 전문 보기]`는 `Button.Secondary`(주). 전자는 `HasNoticeFile`일 때만 표시 |
| ⛔ 금지 | 카드 어디에도 **파일명·확장자·경로·파일 크기를 쓰지 않는다**(R1) |
| 커맨드 배선 | 항목 템플릿 안에서는 `Command="{Binding DataContext.ShowLicenseFullTextCommand, RelativeSource={RelativeSource AncestorType=UserControl}}"` + `CommandParameter="{Binding}"`. ⚠️ `{Binding ShowLicense…}`로 쓰면 **항목(LicenseComponent)에서 커맨드를 찾아 조용히 아무 일도 하지 않는다** — 리포에서 반복되는 함정이다 |

## §3.4 전문 렌더링 — 현행 규격을 그대로 승계한다

35 KB / 674줄 전문의 표시 방식은 **바꾸지 않는다.** it23에서 결정·구현·검증됐고 잠금 테스트(`SettingsViewModelLicenseTests.cs:353`)가 있다.

| 항목 | 값 | 이유(요약) |
|------|-----|------------|
| 컨트롤 | `TextBox` `IsReadOnly` `IsReadOnlyCaretVisible` `AcceptsReturn` | **선택·복사 가능**해야 한다(전문 인용) |
| 스크롤 | 자체 `Vertical/Horizontal=Auto`, **`ScrollViewer` 미포장** | 포장하면 무한 높이 요구로 스크롤이 죽는다 |
| 줄바꿈 | `NoWrap` + 수평 스크롤 | 원문이 ~70열로 정렬돼 있어 `Wrap`은 문단을 재배치한다 |
| 폰트 | `Consolas, D2Coding, Malgun Gothic` 13 | 등폭이 구분선·들여쓰기를 보존(한글은 폴백) |
| 성능 | 35 KB는 부담 아님(it23 실측). **로드 시점이 오히려 개선된다** — 종전은 오버레이를 열 때 색인 본문을 무조건 읽었고, 이제 Level 2 진입 시에만 읽는다 | |
| 바인딩 | `{Binding LicenseText}` **이름 유지** | 잠금 테스트가 이 경로를 정규식으로 찾는다 |

## §3.5 키보드 · 포커스 · 접근성

| 항목 | 규격 | 근거 |
|------|------|------|
| **Esc** | `UserControl.InputBindings`에 `<KeyBinding Key="Escape" Command="{Binding EscapeLicenseViewerCommand}" />` 1개. 커맨드가 상태로 분기한다: **Level 2 → Level 1**, **Level 1 → 닫기**, **오버레이 닫힘 → 아무 것도 하지 않는다**(설정 화면을 Esc로 닫는 동작을 새로 만들지 않는다) | 리포에 오버레이 Esc 선례가 없어(`grep Escape` 결과 `PinPromptWindow`의 코드비하인드뿐) **XAML `InputBindings` + 단일 커맨드**로 코드비하인드를 만들지 않는다. UV-4로 실측 |
| 커맨드 1개로 합치는 이유 | `KeyBinding`은 하나의 커맨드만 지목할 수 있고, `CanExecute` 배선을 추가하면 `[NotifyCanExecuteChangedFor]` 연쇄가 늘어난다. 상태 분기를 VM 안에 두면 **단위 테스트로 3분기를 직접 검증**할 수 있다 | |
| 뒤로 | Level 2의 `[← 뒤로]` 버튼 + Esc. 두 경로 모두 **`LicenseText`를 비운다**(수십 KB 해제) | |
| 초기 포커스 | 지정하지 않는다(오버레이 열림 시 자동 포커스 이동 없음) | 키오스크 터치 조작이 기본이고, 강제 포커스는 스크린리더의 낭독 위치를 흔든다 |
| 스크린리더 | 오버레이 루트 `Grid`에 `AutomationProperties.Name="프로젝트 라이선스 고지"`. 카드 `Border`에 `AutomationProperties.Name="{Binding Name}"`. 배지·아이콘성 요소에 `AutomationProperties.Name`으로 라이선스 이름 제공(`GPL-3.0-or-later`는 낭독이 어색하므로 배지에는 `licenseName`을 넣는다) | `AutomationProperties`는 신규 리소스 키가 아니다 |
| 탭 순서 | XAML 선언 순서 = 헤더 닫기 → 카드1 액션 → 카드2 액션 → 그 외 문서. `TabIndex`를 수동 지정하지 않는다 | |
| 히트테스트 | scrim이 흡수 → 오버레이 열림 중 설정 편집 불가(현행과 동일, 별도 `IsEnabled` 배선 없음) | |

## §3.6 리소스 규약 — 테마 무변경

| 규칙 | 내용 |
|------|------|
| `Themes/` **무변경** | 병합 딕셔너리 간 `StaticResource` 교차 참조로 창이 안 뜬 사고 이력이 있다 |
| 재사용 키(전부 기존) | `Brush.Scrim` · `Brush.Bg` · `Brush.Surface` · `Brush.Surface.Alt` · `Brush.Divider` · `Brush.Border` · `Brush.Accent.Soft` · `Brush.Accent.Text` · `Brush.Warning` · `Brush.Warning.Surface` · `Brush.Text.Muted` · `Brush.Text.Tertiary` · `Card` · `Shadow.Pop` · `Button.Secondary` · `Button.Ghost` · `Text.H2` · `Text.Title` · `Text.Body` · `Text.Label` · `Text.Caption` · `Radius.S` · `Radius.Pill` · `Pad.L` · `Space.XS/S/M` |
| 반복 요소는 **`SettingsView.xaml`의 `UserControl.Resources`에 로컬 스타일**로 정의 | 배지·메타 라벨·메타 값·섹션 머리 4종. 로컬 `x:Key`는 테마 검증 테스트가 제외한다(VF-16) → 안전하고, 이미 `RowLabel`·`GroupTitle` 등 6개 선례가 있다 |
| 컨버터 **신규 0개** | 표시 분기는 전부 `bool` + 기존 `BoolToVis`/`InverseBoolToVis`. ⚠️ `NullToVis`는 **null일 때 Visible**(`src/MCPhoto.App/Converters/CommonConverters.cs:73-77`)이므로 "값이 없으면 숨김"에 쓸 수 없다 → `Has*` 계산 속성을 레코드에 둔다(§2.8) |

## §3.7 VM 계약 (`SettingsViewModel` 라이선스 구역)

모든 신규 멤버는 **`[license-viewer:begin]`~`[license-viewer:end]` 구역 안**에 둔다(`SettingsViewModel.cs:492`·`:650`). 구역 밖에 두면 AC-C2 정적 검사가 보호하지 못한다.

| 멤버 | 형태 | 상태 | 비고 |
|------|------|:---:|------|
| `IsLicenseViewerOpen` | `[ObservableProperty] bool` | 유지 | 오버레이 `Visibility` |
| `LicensePage` | `[ObservableProperty] LicenseViewerPage` (`Summary`/`FullText`) | **신규** | `[NotifyPropertyChangedFor]`로 아래 2 bool 갱신 |
| `IsLicenseSummaryPage` / `IsLicenseFullTextPage` | `bool` 계산 | **신규** | XAML은 `BoolToVis`만 쓴다(§3.6) |
| `LicenseComponents` | `ObservableCollection<LicenseComponent>` | **신규** | 요약 카드 소스. 열 때마다 재구성 |
| `HasLicenseComponents` | `bool` 계산 | **신규** | 0개면 카드 영역 `Collapsed`(배너만) |
| `LicenseDocuments` | `ObservableCollection<LicenseDocument>` | **의미 변경** | 이제 **미참조 문서 + 강등 폴백 목록** 전용 |
| `HasLicenseDocuments` | `bool` 계산 | **신규** | `그 외 동봉 고지 문서` 섹션 표시 |
| `SelectedLicenseDocument` | `[ObservableProperty] LicenseDocument?` | 유지 | 폴백·미참조 목록의 선택 → 변경 시 전문 로드 + `LicensePage=FullText` |
| `LicenseDegradedMessage` / `HasLicenseDegraded` | `string` + `bool` 계산 | **신규** | D1·D2 배너 |
| `LicenseErrorMessage` / `HasLicenseError` | 유지 | 유지 | F1·F2·F6(치명) 및 F3~F5(전문 읽기 실패) |
| `LicenseText` | `[ObservableProperty] string` | 유지 | **이름 고정**(잠금 테스트) |
| `LicenseFullTextCaption` | `[ObservableProperty] string` | **신규**(`LicenseSelectionSummary` 대체) | Level 2 헤더. 정상 = `{컴포넌트} · {SPDX}` + 부제(`라이선스 전문` \| `소스 코드 제공 안내`), 폴백 = `{파일명} · {크기}` |
| `IsLicenseLoading` | 유지 | 유지 | `불러오는 중…` |
| `LicenseLoadTask` | `Task?` | 유지 | 테스트 대기 이음새 |
| `OpenLicenseViewerCommand` | `[RelayCommand] async` | 유지(내용 변경) | 요약 구성 → `LicensePage=Summary`. **전문을 읽지 않는다** |
| `CloseLicenseViewerCommand` | `[RelayCommand]` | 유지 | 전체 초기화(+`LicensePage=Summary`로 되돌림) |
| `ShowLicenseFullTextCommand(LicenseComponent)` | **신규** `[RelayCommand] async` | | `component.FullTextFile` 로드 → `FullText` |
| `ShowLicenseNoticeCommand(LicenseComponent)` | **신규** `[RelayCommand] async` | | `component.NoticeFile` 로드 → `FullText` |
| `BackToLicenseSummaryCommand` | **신규** `[RelayCommand]` | | `LicenseText=""` · `SelectedLicenseDocument=null` · `LicenseErrorMessage=""` · `LicensePage=Summary` |
| `EscapeLicenseViewerCommand` | **신규** `[RelayCommand]` | | §3.5의 3분기 |

| 규칙 | 이유 |
|------|------|
| **금지 문자열**: `CurrentUser`·`IsLoggedIn`·`IsGuest`·`IsTempUser`·`Role`·`TestMode` | AC-C2 정적 검사(`SettingsViewModelLicenseTests.cs:294`). 신규 타입·속성 이름에도 이 부분 문자열이 들어가면 안 된다 |
| 요약 읽기도 `Task.Run` 오프로드 + `ConfigureAwait(true)` | 매니페스트 읽기 + 파일 존재 검사 N회 = 디스크 접근. 느린/네트워크 저장소에서 UI를 멈추지 않는다(리포 규약) |
| 전문 로드의 **stale 폐기 유지** | 현행 방식(`SettingsViewModel.cs:633`)을 승계하되 비교 대상을 "현재 요청 토큰"으로 일반화한다 — 요청 출처가 3개(컴포넌트 전문·컴포넌트 고지·폴백 문서 선택)로 늘었으므로 `ReferenceEquals(document, SelectedLicenseDocument)` 만으로는 부족하다. **단조 증가 정수 요청 ID**를 두고 도착 시 `id == _currentLicenseRequestId`만 반영한다 |
| 열 때마다 재구성 | 파일 교체·삭제를 반영(현행 규약 승계) |
| 서비스 `null` 허용 유지 | 생성자 마지막 선택 파라미터(`SettingsViewModel.cs:182`). null이면 F6로 축퇴 |
| 닫을 때 `LicenseText`·컬렉션 해제 | 수십 KB 상주 방지(현행 승계) |

## §3.8 화면 상태 매트릭스 (전수)

| 상태 | 배너 | 카드 | `그 외 문서` | Level 2 도달 | Esc |
|------|:---:|:---:|:---:|:---:|:---:|
| 정상(매니페스트 2항목, 파일 전부 존재) | — | 2장 | 숨김 | ✅ 카드 버튼 | 닫기 |
| 정상 + 미참조 `.txt` 존재 | — | 2장 | 표시(파일명) | ✅ 카드 버튼 · ✅ 문서 선택 | 닫기 |
| D3(항목의 파일 부재) | — | 2장(해당 카드에 F7 1줄) | 숨김 | ✅ 버튼 활성 → 누르면 F3 문구 | 닫기 |
| D1·D2(매니페스트 없음·손상) | ⚠️ 강등 | 숨김 | **폴백 목록**(파일명) | ✅ 문서 선택 | 닫기 |
| F2(폴더 있고 `.txt` 0개) | ⚠️ 오류 | 숨김 | 숨김 | ❌(볼 것이 없다) | 닫기 |
| F1(폴더 없음) | ⚠️ 오류 | 숨김 | 숨김 | ❌ | 닫기 |
| F6(서비스 미주입·열거 실패) | ⚠️ 오류 | 숨김 | 숨김 | ❌ | 닫기 |
| Level 2(전문 표시 중) | — | 숨김 | 숨김 | 현재 위치 | **뒤로** |
| Level 2 + F3/F4/F5 | ⚠️ 오류(본문 자리) | 숨김 | 숨김 | 현재 위치 | 뒤로 |

## §3.9 동결 문구 (전 문구)

| 위치 | 문구 |
|------|------|
| 설정 고급 그룹 버튼 | `프로젝트 라이선스 고지` ※ |
| 오버레이 제목 | `프로젝트 라이선스 고지` |
| 오버레이 부제 | `이 소프트웨어가 사용·동봉하는 오픈소스 구성 요소와 라이선스입니다.` |
| 섹션 머리(자체) | `이 소프트웨어` |
| 섹션 머리(제3자) | `동봉된 오픈소스` |
| 메타 행 라벨 | `라이선스` · `버전` · `저작권` · `배포 형태` · `소스 제공` |
| 카드 주 버튼 | `라이선스 전문 보기` |
| 카드 부 버튼 | `소스 코드 제공 안내` |
| 미참조 문서 섹션 머리 | `그 외 동봉 고지 문서` |
| Level 1 푸터 | `고지 문서는 설치 폴더에 함께 배포됩니다.` |
| Level 1 푸터(우측) | `{updatedOn} 기준` (매니페스트 값. 값이 없으면 미표시) |
| Level 2 뒤로 | `← 뒤로` |
| Level 2 부제(전문) | `라이선스 전문` |
| Level 2 부제(고지) | `소스 코드 제공 안내` |
| 닫기 | `닫기` |
| 로딩 | `불러오는 중…` (it23 유지) |
| **F7**(항목 파일 부재) | `이 항목의 고지 파일이 배포물에 없습니다. 개발자에게 알려주세요.` |
| **D1** | `라이선스 요약 정보를 찾을 수 없어 동봉된 고지 문서를 그대로 표시합니다. 배포 산출물이 불완전할 수 있으므로 개발자에게 알려주세요.` |
| **D2** | `라이선스 요약 정보를 읽을 수 없어 동봉된 고지 문서를 그대로 표시합니다. 배포 산출물이 불완전할 수 있으므로 개발자에게 알려주세요.` |
| F1·F2·F3·F4·F5·F6 | **it23 §C10 문구 그대로**(`SettingsViewModel.cs:501-511`, `LicenseNoticeService.cs:34-35`·`:53-56`) — 변경 금지 |

> ※ **버튼 라벨 변경 근거**: 사용자 지시는 "제목"에 대한 것이지만, 진입점 라벨(`오픈소스 라이선스`)과 도착 화면 제목(`프로젝트 라이선스 고지`)이 다르면 어디로 가는지 알 수 없다. 동일 문자열로 통일한다. **it23 §C10의 동결 문구 2건(`오픈소스 라이선스`)은 이 문서로 대체된다.** 이 변경은 `SettingsViewModelLicenseTests.cs:320`의 정규식과 `docs/analysis/11:278`의 서술을 함께 갱신해야 한다(UV-5 — 착수 전 사용자 확인 항목).

---

# C부 · 고지 텍스트 파일 보완

## §4.1 현행 4파일 진단 — 무엇이 부족한가

**전문을 다 읽고 판단한 결과**다(`licenses/README.txt` 52줄 · `FFmpeg-README.txt` 142줄 · `FFmpeg-COPYING.GPLv3.txt` 674줄 · 루트 `LICENSE` 21줄).

| # | 결함 | 소재 | 등급 |
|---|------|------|:---:|
| **X1** | **80열 `====`/`----` 벽**이 파일마다 6~7개. 화면 폭에 무관하게 고정된 ASCII 박스는 현대적 고지 파일에서 쓰지 않는다 | `README.txt:1,4,17,25,38,47` · `FFmpeg-README.txt:1,4,18,64,76,99,121,133` | 미관(사용자 지적) |
| **X2** | **한국어와 영어를 문단마다 교차**한다. 같은 내용을 두 번 읽게 되고 어느 쪽이 정본인지 모른다 | `README.txt:6-14` · `FFmpeg-README.txt:6-16,29-41,80-87,103-111` | 가독성 |
| **X3** | **SPDX 식별자가 없다.** 라이선스를 산문으로만 지목한다 | 전 파일 | 업계 관례 미충족(R3) |
| **X4** | `README.txt`가 **파일명 중심 색인**이다(`전문: MCPhoto-LICENSE-MIT.txt`). 사람에게 필요한 요약(버전·용도·저작권)이 색인에 없다 | `README.txt:21-32` | 구조 |
| **X5** | `configuration` 문자열 62줄이 **1항 중간에** 있어 그 파일에서 가장 눈에 띄는 덩어리가 됐다. 실제로는 대응 소스 범위의 근거자료(부록성)다 | `FFmpeg-README.txt:46-61` | 구조 |
| **X6** | 파일명이 개발자 문서로 읽힌다(`README.txt`) — **사용자가 명시적으로 거부** | — | R1·R3 |
| **X7** | `FFmpeg-README.txt:11`이 `설치 폴더의 LICENSE 파일 참조`라고 안내하지만 배포물의 실제 파일명은 `MCPhoto-LICENSE-MIT.txt`다. **ffmpeg 설계 §10.5.1 D-1에서 `README.txt`만 정정하고 이 파일은 남았다** — 안내가 여전히 부정확하다 | `FFmpeg-README.txt:11` | 🟥 **사실 오류** |
| **X8** | 상용 고지 문서에 통상 있는 항목이 없다: **고지 기준일**, 문의 창구의 역할 구분(소스 요청 vs 일반 문의), "이 문서를 왜 받았는지" 도입부 | 전 파일 | 상용 수준 미달 |
| **X9** | `README.txt:42-44`가 NuGet 패키지를 "대부분 MIT/Apache-2.0"이라고 **추정형으로** 서술한다. 상용 고지에서 추정 표현은 신뢰를 떨어뜨린다 | `README.txt:42-44` | 문구 |

## §4.2 ⛔ 건드리지 않는 것 (경계)

| 대상 | 조치 | 이유 |
|------|------|------|
| `licenses/FFmpeg-COPYING.GPLv3.txt` | **1바이트도 수정 금지.** 서식 통일·줄바꿈 정리·머리말 추가 대상에서 제외 | 라이선스 전문은 **원문 그대로**여야 효력이 있다(GPLv3 자체가 "이 라이선스의 사본"을 요구한다). 줄바꿈·들여쓰기까지 gnu.org 원문(674줄)이다. `LicenseComplianceTests.cs:45-57`이 조항 표제와 600줄 초과를 잠근다 |
| 리포 루트 `LICENSE` | **수정·개명·이동 금지** | ① MIT 자체가 "위 저작권 고지와 이 허가 고지를 모든 사본에 포함"할 것을 요구하므로 문안을 손대면 안 된다 ② csproj가 이 파일을 `licenses\MCPhoto-LICENSE-MIT.txt`로 **링크 복사하는 단일 소스**다(`MCPhoto.App.csproj:86`·`:95-98`·`:110-113`) ③ `Mcphoto_Mit_License_Is_Shipped_Into_Licenses_Folder`(`:116-130`)가 존재·링크 규칙·**licenses에 물리 사본 없음**을 잠근다. 이 파일에 한국어 안내를 덧붙이려는 시도는 **금지**한다 — 필요한 안내는 `NOTICE.txt`에 쓴다 |
| 배포 파일명 `MCPhoto-LICENSE-MIT.txt` | 유지 | csproj `Link` 값·테스트 2곳이 이 이름을 잠근다. 바꿀 이득이 없다 |
| csproj 라이선스 복사 배선 | 유지 | §4.7 |

## §4.3 파일 구성 재편

| 현행 | 이후 | 조치 |
|------|------|------|
| `licenses/README.txt` | **`licenses/NOTICE.txt`** | 개명 + **전면 재작성**(§4.6.1) |
| `licenses/FFmpeg-README.txt` | **`licenses/FFmpeg-NOTICE.txt`** | 개명 + **전면 재작성**(§4.6.2). 내용 항목은 보존(O2~O5 전부) |
| `licenses/FFmpeg-COPYING.GPLv3.txt` | 동일 | **무수정** |
| (빌드 시 생성) `MCPhoto-LICENSE-MIT.txt` | 동일 | **무수정** |
| — | **`licenses/notice-manifest.json`** | 신규(§2.5) |

| 판정 | 근거 |
|------|------|
| `NOTICE.txt`로 개명 | `NOTICE`는 배포물 고지 파일의 가장 널리 통용되는 이름이다(AOSP·Apache 관례, §2.3). `README`는 개발자 문서로 읽히고 사용자가 거부했다 |
| 컴포넌트별 `*-NOTICE.txt` 유지(단일 `THIRD-PARTY-NOTICES.txt`로 합치지 않음) | ① 매니페스트의 `noticeFile`이 **1:1로 지목**되어야 `[소스 코드 제공 안내]`가 그 컴포넌트만 보여준다 ② 컴포넌트가 늘 때 파일 추가가 diff가 깔끔하다 ③ 합치면 FFmpeg 안내를 보려는 사용자가 무관한 절까지 스크롤한다 |
| 파일을 **더 쪼개지 않는다**(예: 소스 오퍼 별도 파일) | 고지 파일이 많아질수록 폴더를 직접 여는 사람(법적 산출물 경로)이 길을 잃는다. 현재 컴포넌트 2개에 파일 4+1개면 충분하다 |
| 확장자는 `.txt` 유지 | Windows에서 더블클릭으로 열린다. `.md`로 바꾸면 메모장에서 마크업이 그대로 보이고, `ListDocuments()`의 `*.txt` 패턴(VF-10)도 넓혀야 한다 |

## §4.4 서식 규약 (플레인텍스트 스타일 가이드)

| 항목 | 규약 | 근거 |
|------|------|------|
| 인코딩·개행 | **UTF-8 no BOM · CRLF** 유지 | 현행과 동일(VF-7). 리포 관례이며 `Service_Reads_Real_Repo_License_Files`가 한글 온전성을 잠근다 |
| 줄 폭 | **78열**에서 수동 줄바꿈 | 메모장·등폭 `TextBox`(폭 13px 기준) 양쪽에서 가로 스크롤 없이 읽힌다. 전문 `TextBox`가 `NoWrap`이므로 파일 자체가 접혀 있어야 한다 |
| 문서 제목 | 1행 제목 + **다음 줄에 제목 길이만큼의 `=`** (setext) | 80열 고정 벽(X1)을 없애면서 플레인텍스트의 표준 제목 표기를 쓴다 |
| 절 제목 | `1. 제목` 형태 + **다음 줄에 제목 길이만큼의 `-`** | 번호가 있으면 문의 메일에서 "3항 참조"로 지목할 수 있다(현행 장점 승계) |
| 구분 벽 | **금지**(`====================================…` 80열, `-------…` 80열) | X1 |
| 정의 목록 | `  라벨      : 값` — 라벨 좌측 정렬, 콜론 열 맞춤(들여쓰기 2, 라벨 폭 12) | 요약 정보를 표처럼 읽히게 한다 |
| 강조 | `**굵게**` 같은 마크업 **금지**. 대신 절 분리·들여쓰기로 구조를 만든다 | `.txt`에서 마크업은 잡음이다 |
| 목록 | `  - 항목` (하이픈 + 공백 1) | |
| 빈 줄 | 절 사이 2줄, 문단 사이 1줄 | |
| 파일 끝 | 개행 1개로 끝낸다 | |
| SPDX 줄 | 각 컴포넌트 절 머리에 `SPDX-License-Identifier: <ID>` 1줄 | §2.3. **기존 저작권 고지를 대체하지 않고 추가**한다 |

## §4.5 언어 규약

| 규칙 | 내용 |
|------|------|
| 기본 언어 | **한국어.** 각 절은 한국어 본문을 먼저 완결하고, 필요한 경우 절 끝에 영어 단락을 **한 덩어리로** 둔다(문단 교차 금지 — X2) |
| 영어 병기 대상 | ① GPLv3 §6 이행 문언(서면 오퍼) ② 저작권 표시 ③ 상표 고지 ④ "No additional restrictions" 진술. **국제 검수·해외 재배포자가 읽는 부분만** |
| **영문 유지(번역 금지)** | 라이선스 이름(`GNU General Public License v3.0 or later`) · SPDX 식별자(`GPL-3.0-or-later`·`MIT`) · 저작권 문장(`Copyright (c) 2000-2026 the FFmpeg developers`) · ffmpeg `configuration` 문자열 · 원문 인용 · URL |
| 이유 | 라이선스 이름과 SPDX 식별자는 **식별자**다. 번역하면 지목 대상이 달라진다. 저작권 표시는 GPLv3 §4가 "유지(retain)"를 요구하므로 원문 형태를 보존해야 한다 |
| 문체 | 상용 서비스 안내문 톤(존댓말 서술체). "~해 주십시오"체 유지. 추정 표현(`대부분`·`~일 것입니다`) 금지 — X9 |

## §4.6 파일별 목차 규격

### 4.6.1 `licenses/NOTICE.txt` (색인 · 전면 재작성)

| 절 | 내용 | 필수 문안 요소 |
|----|------|----------------|
| 제목 | `MC포토 라이선스 고지` + setext `=` | |
| 도입 | 이 문서가 무엇인지 2~3문장: 이 소프트웨어에 포함·동봉된 구성 요소의 라이선스와 저작권을 알리는 문서이며, 오픈소스 라이선스가 요구하는 고지를 이행하기 위한 것 | X8 |
| `1. 이 소프트웨어` | MC포토 본체: `SPDX-License-Identifier: MIT` · 저작권 · 전문 파일 안내 | O2 |
| `2. 동봉된 오픈소스 구성 요소` | 컴포넌트별 5행 요약(이름 · 버전 · SPDX · 용도 · 상세 문서). **FFmpeg가 GPLv3이며 별도 실행 파일로 동봉되고 MC포토 소스는 GPL 적용을 받지 않는다**는 한 문단 | O2 · ffmpeg 설계 §2.3 |
| `3. 대응 소스 코드` | GPLv3 대상 구성 요소의 소스 제공 사실 + 상세는 `FFmpeg-NOTICE.txt` 3·4항 | O3 |
| `4. 동봉되지 않는 구성 요소` | 빌드 시점 참조 라이브러리(NuGet)는 **바이너리를 재배포하지 않으므로** 이 폴더의 고지 대상이 아니라는 사실 + 목록 확인 방법. ⚠️ 추정 표현 제거(X9) | X9 |
| `5. 문의` | 소스 코드 요청과 일반 문의를 **구분**해 안내(메일 제목 예시 포함) | X8 |
| 꼬리 | `이 고지의 기준일: YYYY-MM-DD` — **매니페스트 `updatedOn`과 동일 값**(테스트로 정합 확인) | X8 |

### 4.6.2 `licenses/FFmpeg-NOTICE.txt` (전면 재작성 · 내용 보존)

| 절 | 내용 | 잠금 테스트가 요구하는 문자열 |
|----|------|------------------------------|
| 제목 | `FFmpeg 고지 및 소스 코드 제공 안내` + setext | |
| 도입 | MC포토가 동영상 녹화·타임랩스에 FFmpeg를 사용하며, **별도 실행 파일로 동봉하고 서브프로세스로만 호출**한다는 사실. MC포토 본체는 MIT이며 **전문은 같은 폴더 `MCPhoto-LICENSE-MIT.txt`** — ⚠️ X7 정정 | |
| `1. 동봉 바이너리` | 파일 · 버전(`8.1.2-essentials_build-www.gyan.dev`) · `SPDX-License-Identifier: GPL-3.0-or-later` · 저작권(`Copyright (c) 2000-2026 the FFmpeg developers`) · 배포처(`gyan.dev`) · **무수정 재배포** · 정적 링크 라이브러리의 저작권 소재 | `8.1.2` · `gyan.dev` · `Copyright (c) 2000-2026 the FFmpeg developers` (`LicenseComplianceTests.cs:69-73`) |
| `2. 라이선스 전문` | GPLv3 전문 위치(`FFmpeg-COPYING.GPLv3.txt`) + ffmpeg 라이선스 정책 URL | `FFmpeg-COPYING.GPLv3.txt` (`:92`) |
| `3. 대응 소스 코드` | GPLv3 제6조 근거 + 소스 URL 2곳 + 대응 소스 범위(정적 링크 라이브러리 전부 + 빌드 스크립트) | `https://github.com/GyanD/codexffmpeg` · `ffmpeg.org` (`:81-82`) |
| `4. 서면 소스 제공 오퍼` | 3년 유효 · 실비 · 사본 보유자 누구에게나 · 연락처 · 요청 시 알려줄 정보. 영문 단락 병기 | `3년` · `devmcjo@gmail.com` (`:85-86`) |
| `5. 추가 제약 없음` | MC포토 약관이 GPLv3 권리를 제한하지 않음 | `제한하지 않습니다` (`:89`) |
| `6. 상표` | FFmpeg 상표 귀속 + 보증·추천 아님 | |
| `부록 A. 빌드 구성` | `ffmpeg -version`의 configuration 문자열 전문(실측값 그대로). **문서 끝으로 이동**(X5) | `--enable-gpl` · `--enable-version3` · `--enable-libx264` (`:76-78`) |
| 꼬리 | `이 고지의 기준일: YYYY-MM-DD` | |

> ⚠️ **잠금 문자열을 옮길 때 주의**: `Ffmpeg_Notice_Has_Version_Config_Source_And_Written_Offer`는 **파일 전체 문자열 포함**만 보므로 절 순서 변경은 안전하다. 그러나 `3년`을 `3 년`으로, `제한하지 않습니다`를 `제한하지 않습니다.` 앞뒤가 바뀌는 정도가 아니라 **표현 자체를 바꾸면 실패**한다 — 문안을 다듬을 때 이 8개 문자열은 그대로 남긴다(§6 T-C1이 목록을 재확인).

## §4.7 csproj 정합 — **변경 불요** (근거)

| 확인 | 결과 |
|------|------|
| 파일 개명 | `None Include="$(LicensesSource)\**\*.*"`(`:90`)과 `LicenseFiles Include="$(LicensesSource)\**\*.*"`(`:105`)가 **와일드카드**다 → 개명해도 자동 반영 |
| `.json` 신규 파일 | 같은 와일드카드가 `*.*`이므로 포함된다(**UV-1로 실측 필요**) |
| MIT 링크 복사 | `:95-98`·`:110-113` 무변경(파일명 유지) |
| 인스톨러 | `{#PublishDir}\*` + `recursesubdirs`(`installer/MCPhoto.iss:39`). `Excludes`는 `*firebase*credentials*.json` 등 특정 패턴만 → `notice-manifest.json`은 걸리지 않는다(**UV-1로 실측**) |
| 주석 1건만 갱신 | `MCPhoto.App.csproj:93`의 주석이 `licenses/README.txt`를 언급한다 → `NOTICE.txt`로 문구 수정(기능 무영향) |

---

# D부 · 변경 파일 · 테스트 · 문서

## §5 변경·신규 파일 목록

| 파일 | 변경 |
|------|------|
| `licenses/NOTICE.txt` | **신규**(= `README.txt` 개명 + 전면 재작성, §4.6.1) |
| `licenses/README.txt` | **삭제**(개명) |
| `licenses/FFmpeg-NOTICE.txt` | **신규**(= `FFmpeg-README.txt` 개명 + 전면 재작성, §4.6.2) |
| `licenses/FFmpeg-README.txt` | **삭제**(개명) |
| `licenses/notice-manifest.json` | **신규**(§2.5) |
| `licenses/FFmpeg-COPYING.GPLv3.txt` · 루트 `LICENSE` | **불변**(§4.2) |
| `src/MCPhoto.App/Services/ILicenseNoticeService.cs` | `LicenseComponent`·`LicenseSummary` 레코드 추가 · `ReadSummary()`·`ReadText(string)` 추가. 기존 4멤버 유지. `LicenseDocument`의 `DisplayName` 주석(별칭 금지 근거)을 §2.1 판정으로 갱신 |
| `src/MCPhoto.App/Services/LicenseNoticeService.cs` | 매니페스트 파싱·교차 검사 구현 · `IndexFileName` 상수 `README.txt` → `NOTICE.txt`(`:31`) · M5 경로 검사 |
| `src/MCPhoto.App/ViewModels/SettingsViewModel.cs` | 라이선스 구역(`:492-650`) 재작성 — §3.7 멤버. 구역 표식 유지 |
| `src/MCPhoto.App/Views/SettingsView.xaml` | 고급 그룹 버튼 라벨 변경(`:525`) · 오버레이(`:562-667`) 재작성(Level 1/2) · `UserControl.Resources`에 로컬 스타일 4종 · `UserControl.InputBindings` Esc 1건 |
| `src/MCPhoto.App/MCPhoto.App.csproj` | **주석 1줄만**(`:93`) |
| `tests/MCPhoto.Tests/LicenseComplianceTests.cs` | 개명 반영 5곳(§1.3) + **신규 4건**(T-C1·T-M1~T-M3) |
| `tests/MCPhoto.Tests/SettingsViewModelLicenseTests.cs` | 7건 재작성 + 라벨 정규식 갱신(`:320`) + **신규 6건**(T-V1~T-V6) |
| `tests/MCPhoto.Tests/XamlResourceTests.cs` | C-T16 바인딩 목록 갱신(`:891-893`) + **신규 1건**(T-X1: 카드 템플릿이 파일명을 바인딩하지 않음) |
| `tests/MCPhoto.Tests/DiagnosticsViewModelTests.cs` | 표기 문구를 바꾸는 경우에만 `:408` 갱신(§7 판단) |
| **불변** | `ServiceRegistration.cs:54`(인터페이스 이름 유지) · `DiagnosticsViewModel`·`DiagnosticsWindow.xaml`(§7-3 판정) · `installer/MCPhoto.iss` · `publish.ps1` · `Themes/*` |

## §6 테스트 전략

### 6.1 신규 — 배포 정합(핵심)

| # | 대상 | 케이스 | 유형 |
|---|------|--------|------|
| **T-M1** | 매니페스트 자체 | 리포 `licenses/notice-manifest.json`이 파싱되고 `schemaVersion==1` · 항목 ≥2 · **첫 항목이 `kind:"self"`** · 필수 필드 전부 채워짐 · 모든 `spdxId`가 알려진 집합(`MIT`,`GPL-3.0-or-later`) · **`self` 항목의 `version`이 null**(M4) | 리포 자산 |
| **T-M2** | 매니페스트 → 파일 | **출력 폴더 기준**(`AppContext.BaseDirectory/licenses`, VF-6): 모든 `fullTextFile`·`noticeFile`이 실제로 존재하고 읽힌다. 즉 **매니페스트가 거짓말을 하지 않는다** | 배포물 |
| **T-M3** | 파일 → 매니페스트 | 출력 폴더의 모든 `.txt`가 어떤 항목에서 참조된다(미참조 0건). 새 고지를 추가하고 매니페스트를 잊으면 실패한다 | 배포물 |
| **T-M4** | 내용 정합(M3) | 각 항목의 `version`·`copyright` 문자열이 그 항목의 `noticeFile` 본문에 **그대로** 존재한다. `NOTICE.txt`의 기준일 == 매니페스트 `updatedOn` | 리포 자산 |
| **T-C1** | 고지 문안 동결 | `FFmpeg-NOTICE.txt`가 §4.6.2 표의 8개 잠금 문자열을 모두 포함 + `SPDX-License-Identifier: GPL-3.0-or-later` · X7 정정 확인(`MCPhoto-LICENSE-MIT.txt` 언급, `설치 폴더의 LICENSE` 표현 부재) | 리포 자산 |

> T-M2·T-M3을 **출력 폴더 기준**으로 쓰는 이유: 리포 소스에는 `MCPhoto-LICENSE-MIT.txt`가 없다(빌드 시 링크 복사, VF-2). 소스 폴더만 보면 매니페스트가 존재하지 않는 파일을 가리키는 것처럼 보인다. 출력 폴더는 **실제 배포되는 집합**이므로 정합의 참 기준이다(UV-2로 실측).

### 6.2 신규 — 서비스

| # | 케이스 | 기대 |
|---|--------|------|
| T-S1 | 정상 매니페스트 + 파일 전부 존재 | `Components` 순서 보존 · `IsFullTextMissing=false` · `DegradedMessage=null` · `UnlistedDocuments` 0건 |
| T-S2 | 매니페스트 파일 없음 | `DegradedMessage` = **D1 문구** · `Components` 0건 · `UnlistedDocuments`에 폴더의 `.txt` 전부 |
| T-S3 | JSON 손상 / `schemaVersion=2` / 필수 필드 누락 / 항목 0개 (4케이스) | 전부 `DegradedMessage` = **D2 문구**, 예외 없음 |
| T-S4 | 항목이 없는 파일을 가리킴 | 카드는 유지 · `IsFullTextMissing=true` · `DegradedMessage=null` |
| T-S5 | 미참조 `.txt` 1개 존재 | `UnlistedDocuments` 1건(파일명) · 카드 정상 |
| T-S6 | **M5 경로 탈출** — `fullTextFile`이 `..\..\secret.txt` / `sub/x.txt` / `C:\x.txt` | 참조 무효 처리(`IsFullTextMissing=true`) · 폴더 밖 파일을 **읽지 않는다** |
| T-S7 | `ReadText(string)` 정상·부재 | 성공 시 본문, 부재 시 F3 문구(예외 없음) |
| T-S8 | 주석·후행 콤마가 있는 매니페스트 | 정상 파싱(M1) |
| T-S9 | 빈 문자열 필드 | `null`로 정규화(M8) → `Has*`가 false |

### 6.3 신규 — VM

| # | 케이스 | 기대 |
|---|--------|------|
| T-V1 | 열기 | `IsLicenseViewerOpen=true` · `LicensePage=Summary` · 카드 채워짐 · **`LicenseText`가 비어 있다**(전문을 읽지 않았다 — 스텁의 `ReadText` 호출 0회로 단정) |
| T-V2 | `ShowLicenseFullTextCommand` | `LicensePage=FullText` · `LicenseText`에 해당 파일 본문 · `LicenseFullTextCaption`에 **컴포넌트명·SPDX가 들어가고 파일명이 없다** |
| T-V3 | `ShowLicenseNoticeCommand` | 고지 파일 본문 + 부제 `소스 코드 제공 안내` |
| T-V4 | `BackToLicenseSummaryCommand` | `LicensePage=Summary` · `LicenseText` 비움 · 카드 유지(재열거 없음) |
| T-V5 | `EscapeLicenseViewerCommand` 3분기 | Level 2 → Summary / Level 1 → 닫힘 / 닫힌 상태 → **무변화**(`IsLicenseViewerOpen`이 false로 유지되고 다른 상태도 변하지 않음) |
| T-V6 | 강등(D1·D2) | `HasLicenseDegraded=true` · 문구 일치 · 폴백 목록 채워짐 · 문서 선택 시 전문 표시(**전문 도달 유지**) |
| T-V7 | stale 폐기 | 전문 A 요청 직후 B 요청 → 최종 `LicenseText`는 **B**(요청 ID 비교) |
| T-V8 | 닫기 | 전 상태 초기화 + `LicensePage=Summary` 복귀 |
| T-V9 | 서비스 null | 크래시 없음 + F6 문구 |
| **T-V10** | **AC-C1 승계** | 게스트·실계정·테스트 계정 3상태에서 열기→카드→전문 보기까지 **전부 동일 동작**(`SettingsViewModelLicenseTests.cs:258`의 `[Theory]` 확장) |
| **T-V11** | **AC-C2 승계**(정적) | 라이선스 구역에 금지 문자열 6종 부재(`:294` 그대로 유지 — 신규 코드가 이 검사를 통과해야 한다) |

### 6.4 신규 — XAML 정적

| # | 케이스 |
|---|--------|
| **T-X1** | 요약 카드 `DataTemplate`이 `Name`·`SpdxId`·`LicenseName`을 바인딩하고, **`FullTextFile`·`NoticeFile`을 바인딩하지 않는다**(R1 — 파일명 미노출을 정적으로 잠근다) |
| T-X2 | C-T16 갱신: §3.7의 신규 멤버 전부가 XAML에 바인딩되고 VM에 실재. 커맨드 5종 존재 |
| T-X3 | 카드 액션 버튼이 `RelativeSource AncestorType=UserControl` 경유로 커맨드를 바인딩한다(§3.3 함정) |
| T-X4 | Esc `KeyBinding`이 `EscapeLicenseViewerCommand`를 지목한다 |
| T-X5 | 유지: 오버레이 `Grid.RowSpan="2"` · 전문 `TextBox` 규격 · 버튼 `IsEnabled` 미부착 · `FolderPath` 문자열 부재 |

### 6.5 회귀 기준

`dotnet test`(현행 통과 건수 기준) **실패 0** · 빌드 경고 증가 0 · `licenses/` 관련 테스트가 개명 후에도 전부 통과.

## §7 문서 갱신 지점

| # | 파일:절 | 갱신 내용 |
|---|---------|-----------|
| **7-1** | `docs/analysis/11-exe-app-features.md §19`(`:374-383`) | **전면 재작성**: 파일 목록 열거 → **컴포넌트 요약(매니페스트) + 전문 2단 구조**. 매니페스트가 단일 소스라는 사실, 양방향 diff(§2.6), 강등 규칙(§2.7), 파일명 미노출 원칙과 그 **유일한 예외 2곳**을 명시. `버튼 라벨` 변경도 반영 |
| **7-2** | `docs/analysis/11 §11`(`:278-279`) | 고급 그룹 버튼 라벨 `오픈소스 라이선스` → `프로젝트 라이선스 고지`. `IsEnabled` 미부착 규격 문장은 **그대로 유지** |
| **7-3** | `docs/analysis/11 §17`(`:364`) | 진단 화면 서술은 **유지**(고지 존재 여부 행은 그대로). 단 진단 행의 `정상(N개)`이 이제 **`.txt` 개수**여서 매니페스트 존재 여부를 반영하지 않는다 → "고지 문서 개수이며 요약 정보(매니페스트) 유효성과는 별개"라는 1줄 추가. **판정: 진단 행의 계산 로직·문구는 바꾸지 않는다** — 바꾸면 `DiagnosticsViewModel`·XAML·테스트 3곳이 파급되고, 요약 손상은 라이선스 화면 자체가 배너로 알린다 |
| **7-4** | `docs/analysis/41-local-data-and-file-formats.md` | **`notice-manifest.json` 형식 절 신설** — 스키마·필드·정규화 규칙(M8)·검증 규칙(M5~M7)·강등 등급. 이 리포에서 앱이 읽는 JSON 파일의 첫 사례다(VF-18) |
| **7-5** | `docs/analysis/80-build-and-deployment.md:119` | 산출물 목록 갱신: `NOTICE.txt` · `FFmpeg-NOTICE.txt` · `FFmpeg-COPYING.GPLv3.txt` · `MCPhoto-LICENSE-MIT.txt`(링크 복사) · **`notice-manifest.json`** |
| **7-6** | `docs/analysis/13-client-behavior-spec.md §9`(모달 규격) | 라이선스 오버레이 규격을 2단 구조로 갱신(요약 카드 · 전문 페이지 · Esc 2단 · 강등 배너) |
| **7-7** | `docs/design/wpf-ffmpeg-licensing-and-distribution-design.md` | `:152`(O2 산출물 `licenses/README.txt`) · `:261`(Step 1-2 파일명) · `:375-377`(§10.1 산출물 표) · `:436`(D-1 서술) · `:443`(U-1의 "`FFmpeg-README.txt` 3항") — **파일명 5곳 갱신**. §10.1 표에 매니페스트 행 추가 |
| **7-8** | `docs/design/wpf-it23-session-testmode-license-design.md` | C부 앞에 **후속 문서 안내 1줄**(§C3.1·§C5.1·§C10이 it24로 대체됨). ⚠️ **폐기 표시 관례**를 따른다 — 절을 삭제하지 않고 "→ it24로 대체" 링크를 남긴다(이력 보존) |
| **7-9** | `docs/design/README.md` | §3.2 Windows 표에 이 문서 등재 + "라이선스 고지를 바꾼다" 행 추가 |
| 7-10 | `docs/analysis/README.md` | 갱신 이력 줄에 it24 표기(관례 확인 후) |

## §8 리스크

| # | 리스크 | 영향 | 완화 |
|---|--------|------|------|
| R-1 | 개명 누락으로 **고지 파일이 배포물에서 사라진다** | 라이선스 위반 상태 배포 | `If_Ffmpeg_Is_Bundled_Then_Notice_Must_Exist`(`:153`)를 새 파일명으로 갱신 + T-M2가 출력 폴더를 검사. 두 그물이 같은 사실을 다른 각도에서 본다 |
| R-2 | 매니페스트와 txt의 버전·저작권이 **어긋난다** | 화면이 거짓 정보를 표시 | T-M4가 문자열 포함으로 정합을 잠근다(M3) |
| R-3 | 요약만 보고 **전문 경로가 사라졌다고 오해** | GPLv3 §4 이행 후퇴 | 카드마다 `[라이선스 전문 보기]` **상시 노출**(게이트 없음) + 강등 경로에서도 전문 도달 유지(§2.7) + T-V6 |
| R-4 | `.json`이 인스톨러 `Excludes`나 publish에서 **빠진다** | 항상 강등 화면 | UV-1을 Step 1에서 실측. 빠지면 대안: 매니페스트를 `notice-manifest.txt`로 두고 `ListDocuments()`에서 이름으로 제외(설계 변경 최소) |
| R-5 | 오버레이 XAML이 커져(약 +200줄) 설정 화면 파싱 회귀 | 창이 안 뜬다 | `XamlResourceTests`의 STA 파싱 테스트가 이미 있다(`:548`). 로컬 스타일만 쓰고 `Themes/`를 건드리지 않는다 |
| R-6 | Esc `KeyBinding`이 특정 포커스에서 동작하지 않는다 | 키보드 사용자 불편(터치 조작은 무영향) | UV-4 실측. 실패 시 대안은 코드비하인드 `PreviewKeyDown` 1개(리포에 `FrameEditorView` 선례) |
| R-7 | 카드 2장으로도 **여전히 비어 보인다** | 요구 미충족 | Step 6에서 실기 스크린샷 확인 → 부족하면 푸터 안내 확장·카드 내 행 추가로 조정(레이아웃 재설계 아님) |
| R-8 | 폴백 경로가 **파일명을 노출**해 R1 위반으로 지적됨 | 사용자 불만 | 정상 배포물에서는 렌더링되지 않음을 T-M3가 보장(미참조 0건). 문서에 예외 근거 명기(§2.6) |
| R-9 | GPLv3 전문 파일에 서식 규약을 **잘못 적용** | 법적 효력 훼손 | §4.2 경계 + `GplV3_Full_Text_Is_Bundled`가 조항 표제·600줄을 검사. Step 3의 검증 명령에 `git diff --stat licenses/FFmpeg-COPYING.GPLv3.txt`가 **빈 결과**임을 포함 |

## §9 참고 출처

- [SPDX License List](https://spdx.org/licenses/) — 표준 짧은 식별자·정식 이름·전문·정규 URL
- [Handling License Info — SPDX](https://spdx.dev/learn/handling-license-info/) — 짧은 식별자가 전문 중복 없이 라이선스를 지목하는 수단이라는 근거
- [Annex E: Using SPDX short identifiers in Source Files (SPDX 2.3)](https://spdx.github.io/spdx-spec/v2.3/using-SPDX-short-identifiers-in-source-files/) — `SPDX-License-Identifier:` 1줄 규약, 기존 저작권 고지를 지우지 않는다
- [Include open source notices — Google Play services](https://developers.google.com/android/guides/opensource) — 메타데이터 파일과 라이선스 텍스트 파일의 분리 생성
- [OssLicensesActivity / OssLicensesMenuActivity](https://developers.google.com/android/reference/com/google/android/gms/oss/licenses/OssLicensesActivity) — "목록(메타데이터) → 항목 선택 → 전문" 2단 화면 관례
- 리포 내부: [ffmpeg 라이선스·배포 설계](./wpf-ffmpeg-licensing-and-distribution-design.md) §2.4·§10 · [it23 C부](./wpf-it23-session-testmode-license-design.md)

---

## §10 WBS

> 검증된 사실 = §1(VF-1~VF-18) · 미검증 가정 = §0.5(UV-1~UV-5, 각 검증 단계 매핑됨).
> 형식은 `docs/templates/WBS_BLUEPRINT.md`.

### Step 0: 착수 게이트 — 사용자 확인 2건 (코드 변경 없음)
- **Context Brief**: 라이선스 고지 화면·문서 재설계 착수 전에 되돌리기 비용이 큰 결정 2개를 사용자에게 확인한다. 둘 다 문구·파일명이라 나중에 바꾸면 테스트 8곳·문서 6곳이 재파급된다.
- **대상 파일**: 없음(문서 §3.9·§4.3 확정)
- **선행 조건**: 없음
- **구현 내용**: ① 설정 고급 그룹 버튼 라벨을 `오픈소스 라이선스` → `프로젝트 라이선스 고지`로 통일해도 되는지(UV-5) ② 고지 파일 개명(`README.txt`→`NOTICE.txt`, `FFmpeg-README.txt`→`FFmpeg-NOTICE.txt`)을 승인하는지. 두 답을 이 문서 §3.9·§4.3에 확정 기록.
- **검증 명령**: 없음(사용자 답변 수령)
- **완료 기준**:
  - [관측] §3.9 각주와 §4.3 표에 사용자 결정이 "확정"으로 기록됨
  - [non-goal] 이 단계에서 코드·txt·테스트를 **한 줄도 바꾸지 않는다**
  - [trigger] 사용자 명시 답변이 있을 때만 Step 1로 진행. 미답변이면 Step 1을 **현행 파일명 유지** 변형으로 재작성한 뒤 진행
- **롤백**: 해당 없음
- [ ] 완료 — **미수행.** 사용자 답변을 받지 않고 §11 Q1·Q2의 **기본값**(버튼 라벨 통일 + 파일 개명)으로 Step 1 이후를 진행했다. 되돌리기 비용은 설계 우려보다 작다: Q1은 `SettingsView.xaml` 버튼 `Content` 1곳 + `License_Button_Is_Always_Enabled` 정규식 1곳, Q2는 `git mv` 2회 + `LicenseNoticeService.IndexFileName` + 매니페스트 `noticeFile` + 테스트 파일명 문자열이다. **사후 확인 대상**

### Step 1: 고지 txt 개명 + 상용 수준 재작성 (C부)
- **Context Brief**: `licenses/`의 고지 텍스트가 80열 `====` 벽·한영 문단 교차·SPDX 부재로 개발자 메모처럼 보인다(§4.1 X1~X9). 여기서 파일을 업계 관례 이름으로 개명하고 §4.4~4.6 규격으로 다시 쓴다. ⚠️ `FFmpeg-COPYING.GPLv3.txt`와 리포 루트 `LICENSE`는 **절대 수정하지 않는다**(§4.2) — 라이선스 전문은 원문 그대로여야 효력이 있다.
- **대상 파일**: `licenses/NOTICE.txt`(신규) · `licenses/README.txt`(삭제) · `licenses/FFmpeg-NOTICE.txt`(신규) · `licenses/FFmpeg-README.txt`(삭제) · `src/MCPhoto.App/Services/LicenseNoticeService.cs:31`(색인 상수) · `src/MCPhoto.App/MCPhoto.App.csproj:93`(주석) · `tests/MCPhoto.Tests/LicenseComplianceTests.cs`(`:66`·`:99`·`:104`·`:161`·`:226`·`:236`·`:345`·`:346`)
- **선행 조건**: Step 0
- **구현 내용**: §4.6.1·§4.6.2 목차대로 두 파일 작성(UTF-8 no BOM·CRLF·78열·setext 제목·번호 절). §4.6.2 표의 잠금 문자열 8개와 X7 정정(`MCPhoto-LICENSE-MIT.txt` 언급) 포함. `git mv`로 개명해 이력 보존. 테스트의 파일명 문자열 갱신 + SPDX 단정 추가.
- **검증 명령**:
  `git diff --stat licenses/FFmpeg-COPYING.GPLv3.txt LICENSE` → **빈 출력**
  `dotnet test tests/MCPhoto.Tests --filter "FullyQualifiedName~LicenseComplianceTests"`
  `powershell -c "Get-Content licenses/NOTICE.txt -Encoding Byte -TotalCount 3"` → BOM(`239 187 191`) **아님**
- **완료 기준**:
  - [관측] 라이선스 테스트 전건 통과 + GPLv3 전문·루트 `LICENSE`의 diff가 0바이트 + 두 신규 파일에 `SPDX-License-Identifier:` 줄 존재
  - [non-goal] `licenses/FFmpeg-COPYING.GPLv3.txt`·`LICENSE`·csproj 복사 규칙·앱 코드 동작 불변(주석·상수 1개 외)
  - [trigger] 파일 개명은 `git mv`로만 — 새로 만들고 옛 파일을 지우면 이력이 끊긴다
- **롤백**: 이 단계 커밋 revert(`licenses/`와 테스트만 포함되므로 단독 revert 가능)
- [x] 완료

### Step 2: `notice-manifest.json` 신설 + 배포 배선 실측 (UV-1·UV-2)
- **Context Brief**: 요약 카드의 데이터 원본을 배포물 안 구조화 파일로 둔다(§2.4 (c)안 채택). csproj는 `licenses\**\*.*`를 와일드카드로 복사하므로 이론상 `.json`도 실리지만(VF-3) 실측하지 않았다. **이 단계가 UV-1·UV-2를 판정하는 게이트**이며, 실패하면 §8 R-4의 대안(`.txt` 매니페스트)으로 전환해야 하므로 코드보다 먼저 확인한다.
- **대상 파일**: `licenses/notice-manifest.json`(신규) · `tests/MCPhoto.Tests/LicenseComplianceTests.cs`(T-M1·T-M4·T-C1 추가)
- **선행 조건**: Step 1(파일명이 확정돼야 매니페스트가 그 이름을 가리킬 수 있다)
- **구현 내용**: §2.5 스키마대로 2항목 작성(주석 허용). T-M1(스키마·필수·M2·M4) · T-M4(버전·저작권·기준일 정합) · T-C1(고지 문안 동결) 추가. 빌드·publish 산출물에 파일이 실리는지 확인.
- **검증 명령**:
  `dotnet build src/MCPhoto.App/MCPhoto.App.csproj -c Debug` → `src/MCPhoto.App/bin/Debug/net8.0-windows/licenses/notice-manifest.json` 존재
  `dotnet test tests/MCPhoto.Tests --filter "FullyQualifiedName~LicenseComplianceTests"` → `tests/MCPhoto.Tests/bin/Debug/net8.0-windows/licenses/notice-manifest.json` 존재(**UV-2 판정**)
  `dotnet publish src/MCPhoto.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true` → `publish/licenses/`에 txt 4개 + json 1개(**UV-1 판정**)
- **완료 기준**:
  - [관측] 세 위치(빌드 출력·테스트 출력·publish)에 `notice-manifest.json`이 실재하고 T-M1·T-M4·T-C1 통과
  - [non-goal] csproj·`installer/MCPhoto.iss`·`publish.ps1` 무변경(와일드카드로 자동 포함되는지가 이 단계의 관측 대상이다)
  - [trigger] publish 산출물 확인은 실제 `dotnet publish` 실행 결과로만 판정 — 추론 금지
- **롤백**: 매니페스트·테스트 삭제(앱 코드 미변경이라 영향 0). UV-1 실패 시 §8 R-4 대안으로 설계 갱신 후 재착수
- [x] 완료

### Step 3: 서비스 확장 — `ReadSummary()` · `ReadText(string)`
- **Context Brief**: `LicenseNoticeService`는 현재 `licenses/**/*.txt` 열거와 본문 읽기만 한다(VF-9~VF-11). 매니페스트를 해석해 컴포넌트 요약을 만들고, 매니페스트가 선언한 파일의 **부재를 탐지**하는 책임을 추가한다(§2.6·§2.8). 실패는 예외가 아니라 결과값으로 돌려주는 기존 계약을 지킨다.
- **대상 파일**: `src/MCPhoto.App/Services/ILicenseNoticeService.cs` · `LicenseNoticeService.cs` · `tests/MCPhoto.Tests/LicenseComplianceTests.cs`(T-M2·T-M3·T-S1~T-S9)
- **선행 조건**: Step 2(스키마 확정)
- **구현 내용**: `LicenseComponent`·`LicenseSummary` 레코드(+`Has*` 계산 속성) · 매니페스트 파싱(M1 옵션) · M5~M8 검증 · 파일 존재 교차 검사 · 미참조 문서 산출 · D1~D3 판정 · `ReadText(string)` 오버로드(경로 탈출 차단). 기존 4멤버 시그니처·동작 불변.
- **검증 명령**: `dotnet test tests/MCPhoto.Tests --filter "FullyQualifiedName~LicenseComplianceTests"`
- **완료 기준**:
  - [관측] T-S1~T-S9·T-M2·T-M3 통과. 특히 T-S6에서 `..\..`·하위경로·절대경로 참조가 **폴더 밖 파일을 읽지 않고** `IsFullTextMissing=true`로 강등
  - [non-goal] 기존 서비스 테스트 8건(`:196`~`:340`) 무수정 통과 — 열거·인코딩·상한·빈 파일·실패 문구 동작이 바뀌지 않았다는 증거
  - [trigger] 요약 산출은 `ReadSummary()` 호출 시에만 — 생성자·`ListDocuments()`에서 매니페스트를 읽지 않는다(진단 화면의 개수 계산이 매니페스트에 의존하면 §7-3 판정이 깨진다)
- **롤백**: 이 단계 커밋 revert(VM·XAML 미변경이라 앱은 it23 동작으로 되돌아간다)
- [x] 완료

### Step 4: VM 재작성 — 2단 페이지 상태
- **Context Brief**: `SettingsViewModel`의 `[license-viewer:begin]`~`[license-viewer:end]` 구역(`:492-650`)이 "문서 목록 + 전문"을 관리한다. 이를 "요약 카드 + 전문 페이지" 2단으로 바꾼다. ⚠️ 이 구역은 **계정·역할·테스트 모드를 참조하면 안 된다**(AC-C2, 정적 검사 `SettingsViewModelLicenseTests.cs:294`) — 신규 타입·속성 이름에도 `Role`·`TestMode` 등 6개 부분 문자열이 들어가면 실패한다.
- **대상 파일**: `src/MCPhoto.App/ViewModels/SettingsViewModel.cs`(라이선스 구역만) · `tests/MCPhoto.Tests/SettingsViewModelLicenseTests.cs`
- **선행 조건**: Step 3
- **구현 내용**: §3.7 멤버 전체 · 요약 로드 `Task.Run` 오프로드 · 단조 증가 요청 ID로 stale 폐기 · Esc 3분기 커맨드 · 닫기 시 전체 초기화. 기존 7건 재작성 + T-V1~T-V11 추가.
- **검증 명령**: `dotnet test tests/MCPhoto.Tests --filter "FullyQualifiedName~SettingsViewModelLicenseTests"`
- **완료 기준**:
  - [관측] T-V1에서 **열기 직후 `ReadText` 호출 0회**(전문을 읽지 않는다) · T-V5의 3분기 · T-V10의 3계정 상태 동일 동작 · T-V11 정적 검사 통과
  - [non-goal] 설정 화면의 저장·닫기·카메라·외부장치 로직 불변(라이선스 구역 밖 수정 0줄). 실패 문구 F1~F6 문자열 상수 불변
  - [trigger] 전문 로드는 `ShowLicenseFullTextCommand`/`ShowLicenseNoticeCommand`/폴백 문서 선택 3경로에서만 — 오버레이 열기는 요약만 만든다
- **롤백**: 이 단계 커밋 revert(XAML 미변경이므로 되돌리면 화면이 it23 상태로 복귀)
- [x] 완료

### Step 5: XAML 재작성 — Level 1 카드 · Level 2 전문 · Esc
- **Context Brief**: `SettingsView.xaml:562-667`의 오버레이가 좌측 파일명 목록 + 우측 전문 덤프다(VF-15 — 사용자가 지적한 화면). 요약 카드 목록과 전문 페이지를 **형제 `Grid`로 두고 `Visibility`로 전환**한다. ⚠️ 전문 `TextBox`를 Level 1의 `ScrollViewer` 안에 넣으면 자체 스크롤이 죽는다(it23이 실측한 함정, §3.1). ⚠️ `Themes/`에 리소스 키를 추가하지 않는다(병합 딕셔너리 교차 참조로 창이 안 뜬 사고 이력) — 반복 요소는 `UserControl.Resources`의 로컬 스타일로 둔다(VF-16).
- **대상 파일**: `src/MCPhoto.App/Views/SettingsView.xaml` · `tests/MCPhoto.Tests/XamlResourceTests.cs`
- **선행 조건**: Step 4(바인딩 대상 멤버 존재)
- **구현 내용**: 버튼 라벨 변경(`:525`) · 오버레이 재작성(§3.2 스케치·§3.3 카드 규격) · 로컬 스타일 4종 · `UserControl.InputBindings` Esc 1건 · `AutomationProperties.Name`(§3.5) · 카드 액션 커맨드는 `RelativeSource AncestorType=UserControl` 경유. C-T16 갱신 + T-X1~T-X5.
- **검증 명령**: `dotnet test tests/MCPhoto.Tests --filter "FullyQualifiedName~XamlResourceTests|FullyQualifiedName~SettingsViewModelLicenseTests"`
- **완료 기준**:
  - [관측] STA 파싱 테스트 통과 + T-X1(카드가 `FullTextFile`·`NoticeFile`을 바인딩하지 않음) + `License_Button_Is_Always_Enabled`가 새 라벨로 통과 + `No_Folder_Path_In_Ui` 통과
  - [non-goal] `Themes/*.xaml` 무변경 · 전문 `TextBox` 규격(`NoWrap`·자체 스크롤·`IsReadOnly`) 불변 · 오버레이 `Grid.RowSpan="2"` 유지 · 버튼에 `IsEnabled` 미부착
  - [trigger] Level 2 진입은 카드 버튼·폴백 문서 선택·Esc 뒤로 3경로로만 — 오버레이 열기 시 초기 페이지는 항상 Summary
- **롤백**: 이 단계 커밋 revert(VM은 남지만 화면만 되돌아간다 → 단독 revert 가능)
- [x] 완료

### Step 6: 실기 확인 (UV-3·UV-4) — 앱 실행
- **Context Brief**: 이 변경의 목적은 "없어 보이지 않게"라는 **시각 품질**이고, 그것은 테스트로 판정되지 않는다. 또한 창모드 하한 800×600에서의 스크롤(UV-3)과 Esc 동작(UV-4)은 실측이 필요하다. 게스트 접근(AC-C1)은 단위 테스트로 잠겨 있지만 도달 경로는 실기로 한 번 걸어본다.
- **대상 파일**: 없음(실행 확인)
- **선행 조건**: Step 5
- **구현 내용**: ① 게스트 상태에서 설정 → 고급 → `프로젝트 라이선스 고지` 진입(클릭 3회) ② 카드 2장·배지·메타 행 확인 스크린샷 ③ `[라이선스 전문 보기]` → GPLv3 전문 렌더·수평/수직 스크롤·복사 ④ Esc로 뒤로 → Esc로 닫힘 ⑤ **창모드 800×600**에서 Level 1 세로 스크롤 ⑥ 매니페스트를 일부러 손상시켜 D2 강등 배너 + 폴백 목록에서 전문 도달 확인 후 원복.
- **검증 명령**: `dotnet run --project src/MCPhoto.App`(또는 publish 산출물 실행) + 위 6항목 육안 확인 · `git status licenses/` → clean(⑥ 원복 확인)
- **완료 기준**:
  - [관측] 6항목 전부 관측되고 ①~④ 스크린샷 확보. 800×600에서 카드가 잘리지 않고 스크롤로 전부 도달. Esc 2단 동작
  - [non-goal] 촬영·프레임·설정 저장 흐름 무영향(오버레이를 닫은 뒤 저장이 정상 동작) · 손상 실험 후 `licenses/`가 원상 복구
  - [trigger] 강등 화면은 매니페스트를 손상시킨 경우에만 나타난다 — 정상 배포물에서 배너가 보이면 결함이다
- **롤백**: 해당 없음(관측 단계). 문제 발견 시 Step 4·5로 회귀
- [ ] 완료 — **미수행(사람 관측 필요).** 자동 검증으로 대체 가능한 부분은 모두 테스트로 옮겼다: 카드 바인딩·파일명 미노출·커맨드 조상 경유·Esc `KeyBinding`·로컬 스타일 선언 순서(전방 참조)·2단 형제 구조·전문 `TextBox` 규격은 정적 테스트, 강등·부재·경로 탈출·3분기 Esc는 단위 테스트, 매니페스트↔파일↔txt 3자 정합은 출력 폴더 기준 테스트가 잠근다. **남은 것은 테스트로 판정할 수 없는 항목뿐이다** — ① 시각 품질("없어 보이지 않는가") ② 800×600 세로 스크롤(UV-3) ③ 실제 Esc 키 입력(UV-4) ④ 강등 화면 육안 확인

### Step 7: 문서 갱신 (§7 7-1~7-10)
- **Context Brief**: `docs/analysis/`는 **현행 코드의 진실원**이므로 코드가 바뀐 뒤 갱신한다. it23의 라이선스 서술(파일 목록 열거 방식)과 ffmpeg 설계의 산출물 파일명이 이제 사실과 다르다.
- **대상 파일**: `docs/analysis/11-exe-app-features.md`(§11·§17·§19) · `docs/analysis/41-local-data-and-file-formats.md` · `docs/analysis/80-build-and-deployment.md:119` · `docs/analysis/13-client-behavior-spec.md §9` · `docs/design/wpf-ffmpeg-licensing-and-distribution-design.md`(5곳) · `docs/design/wpf-it23-session-testmode-license-design.md`(대체 안내) · `docs/design/README.md`
- **선행 조건**: Step 6(실측 결과가 문서에 반영돼야 한다)
- **구현 내용**: §7 표의 10개 항목. it23 문서는 **절을 삭제하지 않고** "→ it24로 대체" 안내만 남긴다(폐기 표시 관례).
- **검증 명령**: `grep -rn "FFmpeg-README\|licenses/README" docs/ src/ tests/` → **잔존 참조 0건**(설계 문서의 이력 서술은 예외로 허용하되 각 줄에 "→ it24에서 개명" 표기)
- **완료 기준**:
  - [관측] grep 결과가 0건이거나 개명 표기가 병기된 이력 서술만 남음. `analysis/41`에 매니페스트 형식 절이 신설됨
  - [non-goal] `docs/billing/`·`docs/web-client/`는 무변경(웹 클라이언트는 ffmpeg를 배포하지 않아 이 고지 대상이 아니다)
  - [trigger] 문서 갱신은 Step 1~6이 커밋된 뒤 — 코드보다 먼저 쓰면 진실원이 예언서가 된다
- **롤백**: 문서 커밋 revert
- [x] 완료

### 완결성 게이트 자체 점검

- [x] 검증된 사실(§1)/미검증 가정(§0.5) 분리
- [x] 모든 가정에 검증 단계 매핑(UV-1·UV-2→Step 2, UV-3·UV-4→Step 6, UV-5→Step 0)
- [x] 8단계 전부 7개 필드 기재
- [x] 완료 기준 전부 관측 3문 형식(UI 단계 Step 5·6에 non-goal·trigger 포함)
- [x] 검증 명령이 자동 실행 가능(Step 0·6은 사용자 확인·육안 단계임을 명시)

---

## §11 미결정 · 사용자 확인 필요

| # | 항목 | 기본값(미답변 시) |
|---|------|-------------------|
| **Q1** | 설정 버튼 라벨을 `프로젝트 라이선스 고지`로 통일(UV-5) | 통일한다(§3.9 각주). 반대 시 버튼은 `오픈소스 라이선스` 유지, 제목만 변경 |
| **Q2** | 고지 파일 개명(`NOTICE.txt`·`FFmpeg-NOTICE.txt`) | 개명한다. 반대 시 §4.4~4.6의 **재작성만** 적용하고 파일명 유지(테스트 갱신 5곳이 불필요해진다) |
| **Q3** | `updatedOn`(고지 기준일)의 운영 규칙 — 고지 내용을 고칠 때만 갱신? ffmpeg 버전 교체 시에도? | **고지 내용이 바뀔 때마다** 갱신. T-M4가 `NOTICE.txt` 기준일과 매니페스트 값의 일치만 검사하고 최신성은 검사하지 않는다(시한폭탄 테스트 회피) |
| **Q4** | 카드에 `사용 목적`을 노출하는 것이 영업상 문제가 되는지(내부 구현 노출) | 노출한다. "동영상 녹화·타임랩스 인코딩" 수준은 사용자가 화면에서 보는 기능명이다 |
| **Q5** | ffmpeg 설계 §10.6 **U-1**(자사 서버 소스 미러링) 미완 상태 | 이 문서 범위 밖. 현행 제3자 링크 + 3년 서면 오퍼로 준수 상태는 유지된다. 미러 URL이 확정되면 `FFmpeg-NOTICE.txt` 3항에 **줄 추가만** 하면 된다(테스트 무영향) |
