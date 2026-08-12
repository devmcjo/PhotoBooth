# it26 설계 — 앱 쓰기 위치 이관 · 유휴 팝업에 결과물 폴더 열기

> 작성: wpf-architect · 2026-08-12
> 파이프라인: wpf-architect → wpf-developer → wpf-code-reviewer
> 상태: 설계 초안 (rev2 — B부 전제 정정: **완료 팝업 신설 폐기**, 대상은 기존 유휴 경고 팝업)
> ⚠️ 파일명의 `completion-popup`은 rev1(정정 전) 브리프에서 확정된 경로라 **그대로 둔다**. 실제 대상은 유휴 경고 팝업이며 제목·본문이 기준이다.
> 선행 문서·이력: `wpf-it24-license-notice-redesign-design.md`(Hyperlink 규약 · 오버레이 대 Window 판정) · `wpf-it25-recognized-camera-and-test-simulation-design.md`(설정 키 추가 관례) · 커밋 `9b59fb6`(완료 화면 폐지 → 홈 복귀 + 완료 토스트 — **이번에 건드리지 않는다**) · it8 A1 유휴 경고(리포에는 별도 설계 파일 없이 `AppShellViewModel` 주석·테스트로 존속)

## §0 개요

### 0.1 요구사항 원문 (사용자, 축약 금지)

최초 요구:

> "기본 저장 위치를 옮길거면, 촬영 후에 앱에서 저장 폴더를 여는 방안이 있어야할 것 같아. (홈으로 돌아간다는 메시지 노출을 홈으로 가기 전에 노출시키고, 10초 카운팅으로 변경하고, 해당 팝업에 결과물 폴더 열기 같은 버튼(하이퍼링크 스타일)을 만들고 버튼을 누르면 해당 폴더가 열리는 방안으로 채택.) 단, 해당 팝업의 닫기버튼이나 10초가 지나면 홈으로 그냥 돌아가는 것으로 진행."

정정(사용자):

> "**유휴 감시 팝업에 해당 버튼을 추가하라는 뜻이였어.**"
> "링크를 눌러도 카운트다운을 멈추지마 괜찮아."
> "세션폴더만 열되, 이것도 옵션화하고 창모드가 아니더라도 옵션화했으니 상관없을 것 같아. 지원해도돼.(설정에 따른 것)"
> "LocalSavePath 명시하면 해당 경로를 우선하는 것으로해."

**정정이 바꾼 것**: 최초 요구의 "팝업"은 새로 만드는 완료 팝업이 아니라 **이미 존재하는 유휴 감시 팝업**이었다. 그러면 최초 요구의 세부 항목 대부분이 **이미 충족된 상태**다:

| 최초 요구 항목 | 정정 후 상태 |
|---|---|
| "홈으로 돌아간다는 메시지를 홈으로 가기 전에 노출" | **이미 그렇다.** 유휴 팝업은 세션 화면 위에 뜨고 그 뒤에 홈으로 간다 |
| "10초 카운팅으로 변경" | **이미 10초다**(`AppShellViewModel.IdleCountdownSeconds = 10`) → **변경 불요. 상수를 건드리지 않는다** |
| "닫기 버튼 또는 10초 경과 시 홈으로" | **이미 그렇다**(`GoHomeFromIdleCommand` = 즉시 홈, 카운트다운 만료 = 홈) |
| "팝업에 결과물 폴더 열기 링크(하이퍼링크 스타일)" | **이번에 추가하는 유일한 UI 변경** |

요구 분해(최종):

1. **A. 쓰기 위치 이관** — 앱이 설치 폴더(`C:\Program Files\MCPhoto\`)에 쓰는 3종(`MCPhoto.ini` · `Frame\` · `result\`)의 목적지 판정. **이 설계의 무게 중심이다.**
2. **B. 유휴 팝업에 [결과물 폴더 열기] 링크 1줄 추가** — 기존 오버레이·기존 커맨드·기존 카운트다운은 **전부 현행 유지**.
3. **C. 폴더 열기의 위험 2건** — 다른 손님 사진 노출(→ 세션 폴더만) · 잠금 키오스크의 파일시스템 통로(→ ini 옵션 단일 게이트, 기본 off).

### 0.2 이 설계의 최우선 제약 — 두 개의 "절대 금지"

**① 손님 사진을 잃는 경로가 하나도 없어야 한다.** `result\`에는 손님의 사진·타임랩스가 들어 있고 그것은 서버에 없을 수도 있다(로컬 저장은 QR 전송과 독립이며, QR off·업로드 실패에도 로컬은 남는다 — `QrPopupViewModel.cs:129-131`). 위치를 옮기는 변경은 **파일을 옮기지 않는 방식**으로 설계한다. 이동·삭제·덮어쓰기를 한 줄도 만들지 않으면 유실 경로가 원천적으로 존재하지 않는다.

**② 링크가 붙는 곳은 손님이 보는 팝업이다.** 유휴 팝업은 완료 시점이 아니라 **세션 진행 중 2분 무동작**이면 뜬다 — 즉 **손님(게스트) 앞에서, 무인 상태로** 뜨는 화면이다. 거기에 탐색기 진입점을 놓는 것은 손님에게 파일 브라우저를 건네는 것과 같다. 그래서:

| 봉인 | 내용 |
|---|---|
| **기본값은 off** | 신규 ini 키 `EnableResultFolderOpen = 0`. 설치 직후의 부스가 **모르는 채로** 손님에게 탐색기를 열어 주는 상태가 되어서는 안 된다(§5.2) |
| **옵션이 유일한 게이트** | 사용자 정정("옵션화했으니 상관없을 것 같아. 지원해도돼")에 따라 `DisplayMode` 게이트·로그인 게이트를 **넣지 않는다.** 그 대가로 **위험은 전부 운영자가 옵션을 켤 때 감수하는 것**이 되므로, 설정 항목 캡션이 그 위험을 명시적으로 말해야 한다(문구 M8) |
| **열 폴더가 없으면 링크가 없다** | 링크 노출은 "옵션 on **AND** 이 세션의 로컬 저장이 실제 성공"의 AND다. `SaveLocalCopy=false`·저장 실패는 경로가 `null`이라 자동으로 숨겨진다(별도 분기 불요) |
| **기존 유휴 거동 불변** | `IdleWarningSeconds=120` · `IdleCountdownSeconds=10` · 두 버튼 · 문구 · 타이머 구조 전부 그대로. 링크는 **얹기만** 한다 |

### 0.3 판정 요약

| 쟁점 | 판정 | 왜 |
|---|---|---|
| **A-1.** `result\` 목적지 | **`%ProgramData%\MCPhoto\result`** (`LocalSavePath`가 빈 값일 때의 기본값) (§3.3) | 인스톨러가 이미 `users-modify`로 만든다(권한 문제 0) · 계정이 바뀌어도 한 곳에 모인다 · OneDrive/사진 라이브러리 동기화 대상이 아니다(손님 사진이 운영자 개인 클라우드로 새지 않는다) |
| **A-2.** `result\` 기존 데이터 | **이동하지 않고, 읽지도 않는다.** 앱은 `result`를 쓰기만 하므로 구 폴더는 그대로 남고 아무도 건드리지 않는다. 인스톨러의 "`{app}\result` 절대 삭제 금지" 규약을 유지·강화 (§3.6) | 이동은 승격 권한·부분 실패·잠금 위험을 만든다. 유실 경로 0의 가장 강한 형태는 **파일을 만지지 않는 것**이다 |
| **A-3.** `LocalSavePath` 명시값 | **항상 우선.** 이관은 "빈 값의 기본값"만 바꾼다. 명시값이 있으면 한 글자도 건드리지 않는다 (§3.7) | 사용자 확정 지시. 운영자 설정을 덮어쓰는 이관은 클로버다(리포 관례: it24 P5 · it25 A-3) |
| **A-4.** `Frame\` 목적지 | **캐시 루트를 `%ProgramData%\MCPhoto\Frame`로 이관.** `{exe}\Frame`은 **읽기 전용 번들**로 남긴다(`FrameCatalogService.BundleFolder` 불변) (§3.4) | 비승격 실행에서 프레임 다운로드 캐시 기록이 실패하는 실결함을 고친다. "번들=설치물(읽기), 캐시=쓰기"는 종전에 한 폴더로 뭉쳐 있던 두 개념의 정상 분리다 |
| **A-5.** `Frame\` 기존 데이터 | **구 루트를 읽기 소스로 상시 포함**(쓰기·삭제는 새 루트만). 이동 없음 (§3.4.3) | 개인 프레임은 로컬 캐시가 유일 사본일 수 있고, 공용도 재다운로드는 it20이 개선한 대기 UX를 되돌린다. 이동 실패 시 "목록에서 사라짐"이 곧 자산 유실로 보인다 |
| **A-6.** `SettingsPathResolver` 실행경로 우선 | **정책 불변(변경 없음).** 대신 ini가 Program Files 하위일 때 **시작 시 Warning 로그 1줄** (§3.5) | 순서를 바꾸면 ① 기존 설치가 자기 설정을 잃거나 ② 개발 실행이 설치본의 ini를 공유해 서로를 클로버한다(`[Test]` 인증 우회가 전파된다). ini는 운영자 자산(재구성 가능)이고 `result`와 위험 등급이 다르다 |
| **A-7.** 이관 범위의 완결성 | **설치 폴더를 쓰는 코드는 정확히 3곳**이고 나머지 6곳은 읽기 전용임을 전수 확인했다(§3.9) | "3종을 옮기면 끝"이라는 주장이 검증된 사실이어야 이관이 닫힌다 |
| **B-1.** 팝업 신설 | **하지 않는다.** `CompleteSession`(홈 복귀 + 완료 토스트, `9b59fb6`)은 **현행 유지**. 새 `AppState`·새 오버레이·새 카운트다운 0 (§4.1) | 사용자 정정. 그 결과 "모달 부활" 논점·"두 카운트다운 충돌" 논점이 **모두 소멸**한다 |
| **B-2.** 유휴 팝업 거동 | **전부 현행 유지**(120초·10초·두 버튼·문구). 팀리드 판정 승계 (§4.2) | 유휴 감시는 `IsSessionActive`에서만 돌고 **홈에서는 뜨지 않으므로**, 팝업이 뜬 시점의 상태는 항상 세션 활성이다 → "홈으로 돌아갑니다"는 **항상 참**이고 고칠 이유가 없다 |
| **B-3.** 카운트다운 간섭 | **링크를 눌러도 멈추지도 연장되지도 않는다** (§4.4) | 사용자 명시 지시("멈추지마 괜찮아"). 탐색기 창은 앱 상태와 독립이므로 앱이 홈으로 가도 열린 폴더는 닫히지 않는다. 일시정지 로직 자체를 만들지 않는 것이 단순함 요구에 맞다 |
| **B-4.** 세션 폴더 경로 보관 | **현재 세션 범위**(`SessionContext.LocalSaveFolder`), `Reset()`에서 `null`. "마지막 완료 세션을 앱 수명 동안 기억"하는 장치는 **만들지 않는다** (§4.5) | 팝업은 세션 활성 중에만 뜨므로 현 세션 경로만 있으면 충분하다. 앱 수명 보관은 **다음 손님의 팝업이 이전 손님 폴더를 가리키는** 경로를 새로 만든다 |
| **B-5.** 링크가 실제로 닿는 창구 | **`Qr` 상태가 사실상 유일**하다(저장 완료 후 손님이 QR을 찍는 동안 2분 무동작 → 유휴 팝업 → 폴더 열기). 상태별 전수표를 남긴다 (§4.3) | 이 사실을 적지 않으면 "촬영 후 폴더를 열 수 있다"는 기대와 실제 도달 가능성이 어긋난다 |
| **C-1.** 무엇을 여는가 | **그 세션 폴더만.** 경로는 `ILocalSaveService.SaveAsync`의 **반환값**을 쓴다 — `SessionFolderName`으로 재계산 금지 (§5.1) | 재계산은 `MakeUniqueFolder`의 `-2`·`-3` 접미를 재현할 수 없어 **다른 손님 폴더를 연다**(`LocalSaveService.cs:38-40`) |
| **C-2.** 노출 게이트 | **ini `EnableResultFolderOpen` 단일 게이트, 기본 `0`(off).** `DisplayMode`·로그인 게이트 없음 (§5.2) | 사용자 정정("옵션화했으니 … 지원해도돼"). 기본 off인 이유는 팝업이 **게스트 앞에서 무인으로** 뜨기 때문이다 — fail-safe 기본값 |
| **C-3.** 링크 숨김 vs 비활성 | **숨김(`Collapsed`)** (§4.6) | 누를 수 없는 링크를 보여 줄 이유가 없고, 비활성 `Hyperlink` 시각 규격이 리포에 없어 신규 키를 부른다. 카운트다운이 도는 급한 화면이라 요소가 적을수록 낫다 |
| **C-4.** 열기 실패 | **예외 금지 · best-effort.** 팝업 안 캡션으로 경로 노출, 카운트다운은 계속 (§5.3) | `LogFolderService.cs:27-39`의 관례 계승 |
| **C-5.** 열기 구현 | 신규 `IFolderOpener`(App 계층, `opener` 주입). `ILogFolderService`는 **불변** (§5.3) | VM이 `Process.Start`를 직접 만지지 않는 경계 규약(`IClipboardService` 주석) 계승 |
| **D-1.** 신규 테마 리소스 키 | **0개** | 병합 딕셔너리 교차 참조로 창이 안 뜬 사고 이력 |
| **D-2.** 후속 후보(구현 대상 아님) | 소유자 상시 접근 경로(설정 화면의 [저장 폴더 열기])는 **기록만** 한다 (§14) | 유휴 팝업은 발견 경로로는 실용성이 낮다(2분 무동작 대기). 사용자 요구가 유휴 팝업이므로 그대로 구현하되 한계를 정직하게 남긴다 |

---

## §1 검증된 사실 (verified facts — 전부 코드·인스톨러 직접 확인, 2026-08-12)

### 1.1 로컬 저장·경로

| # | 사실 | 근거 |
|---|---|---|
| F1 | 로컬 저장 기본 경로가 **호출부에 인라인**돼 있다: `LocalSavePath`가 공백이면 `Path.Combine(AppContext.BaseDirectory, "result")` | `ResultViewModel.cs:141-143` |
| F2 | `ILocalSaveService.SaveAsync`는 **세션 폴더 절대경로를 반환**하는데(실패 시 `null`) 호출부가 그 반환값을 **버린다** | `LocalSaveService.cs:23,57,64` / `ResultViewModel.cs:144` |
| F3 | 세션 폴더명은 `mcphoto_yyMMdd_HHmm`이고, 같은 분에 두 세션이면 `MakeUniqueFolder`가 `-2`·`-3`… 접미를 붙인다 → **폴더명은 시각만으로 재계산할 수 없다** | `LocalSaveService.cs:20-21,38-40,67-77` |
| F4 | 로컬 저장은 실패해도 예외를 던지지 않고 `null`을 반환한다(크래시 금지 규약). 로그에는 남는다 | `LocalSaveService.cs:59-64` |
| F5 | 로컬 저장은 QR 전송과 독립이고 QR 분기 **이전**에 일어난다 → QR off·업로드 실패에도 로컬 사본은 남는다 | `ResultViewModel.cs:137-156` / `QrPopupViewModel.cs:112-131` |
| F6 | `App.DataFolder` = `%ProgramData%\MCPhoto`이고 로그·`cache`·`sessions`가 이미 그 아래에 있다 | `App.xaml.cs:18-19,28-43` / `FrameCatalogService.cs:61` / `CaptureViewModel.cs:190` |
| F7 | ini 경로는 실행경로 → `%ProgramData%\MCPhoto` → `%LocalAppData%\MCPhoto` 순으로 **실제 쓰기 가능한 첫 곳**을 고른다. `Save()`는 성공 경로를 `_iniPath`로 승격하고 외래 섹션(`[Test]` 등)을 보존한다(it23에서 수정 완료) | `SettingsPathResolver.cs:29-36` / `IniSettingsService.cs:80-109,142-146,277-278` |
| F8 | `AppSettings`에 `SaveLocalCopy`(기본 true)·`LocalSavePath`(기본 `string.Empty`)가 있고 **게스트도 이 두 키를 저장한다**(다른 키와 달리 `!IsGuest` 가드가 없다) | `AppSettings.cs:102,105` / `SettingsViewModel.cs:488,490` |
| F9 | 설정 화면 "로컬 저장 경로" 입력란은 빈 값일 때 **실제 경로를 말해 주지 않는다**(placeholder·캡션 없음) | `SettingsView.xaml:448-450` |
| F10 | 게스트도 PIN 없이 설정 화면에 진입할 수 있다(`OpenSettings`의 게이트는 로그인 사용자에게만 적용) | `AppShellViewModel.cs:559-566` |

### 1.2 프레임 저장소

| # | 사실 | 근거 |
|---|---|---|
| F11 | `LocalFrameStore` 루트 = `AppContext.BaseDirectory\Frame` (DI에서 조립) | `ServiceRegistration.cs:139-141` |
| F12 | `FrameCatalogService.BundleFolder`도 같은 `{exe}\Frame`이며, **번들 스캔은 로컬 저장소가 아무것도 못 찾았을 때만** 동작한다(`ResolveLocalFrames`의 ③) | `FrameCatalogService.cs:60,279-299,444-447` |
| F13 | `LocalFrameStore`는 **서명된 `.slots`가 있는 png만** 프레임으로 인정한다 — `.slots` 없는 이미지는 `LoadBundleFrames`(번들 폴백)만 집는다. **두 경로의 대상 파일 집합이 겹치지 않는다** | `LocalFrameStore.cs:146-175` / `FrameCatalogService.cs:449-453` |
| F14 | `.slots` 서명 입력은 (owner, imageSize, slots, dbId) **payload뿐이며 파일 경로가 아니다** → 파일을 다른 폴더로 옮겨도 서명이 깨지지 않는다 | `SlotsFileCodec.cs:69,93-95,126-133` |
| F15 | 개인 프레임은 **서버가 정본**이다(`POST /frames/mine` 성공 후에만 로컬 캐시 기록, 부분 성공 금지). 공용도 동일 | `FrameEditorViewModel.cs:649-665,689-703` |
| F16 | 캐시 기록에 실패한 서버 문서 id는 `_cacheFailedIds`에 들어가 **이번 실행 동안 목록에 오르지 않는다**(재다운로드 루프 방지의 정상 동작) | `FrameCatalogService.cs:22-31` |
| F17 | `DeleteLocal`은 `frame.ImageUrl`(절대경로) 기반이라 루트가 어디든 지운다 | `LocalFrameStore.cs:90-102` |

### 1.3 인스톨러

| # | 사실 | 근거 |
|---|---|---|
| F18 | 인스톨러는 `{commonappdata}\MCPhoto`(+`logs`,`cache`)를 `users-modify` 권한으로 만든다 | `installer/MCPhoto.iss:76-80` |
| F19 | 제거 시 `{app}\MCPhoto.ini`·`{app}\branding.ini`·`{app}\Frame`을 지우고, **`{app}\result`는 절대 지우지 않으며** 그 존재가 `dirifempty`를 막아 설치 폴더까지 보존한다 | `installer/MCPhoto.iss:93-108` |
| F20 | `[Files]`는 화이트리스트이고 `Frame\`·`result\`·`MCPhoto.ini`를 **담지 않는다**(기본 프레임은 서버에서 내려받는다) | `installer/MCPhoto.iss:57-74` |

### 1.4 유휴 경고 팝업 (B부 대상)

| # | 사실 | 근거 |
|---|---|---|
| F21 | 유휴 감시는 **`SessionStateMachine.IsSessionActive(CurrentState)`일 때만 시작**되고 아니면 `_idle.Stop()`이다. `IsSessionActive` = `FrameSelect · Guide · Capture · CutSelect · Result · Qr` → **홈 화면에서는 유휴 팝업이 뜨지 않는다** | `AppShellViewModel.cs:498-506` / `SessionStateMachine.cs:45-52` |
| F22 | 카운트다운은 **이미 10초**다(`IdleCountdownSeconds = 10`), 경고까지는 120초(`IdleWarningSeconds`). 팝업은 모달 오버레이(scrim + Card)이며 `Grid.Row="1"`에 있다(배너를 덮지 않기 위함) | `AppShellViewModel.cs:37-41` / `MainWindow.xaml:173-196` |
| F23 | 팝업 구조 = 제목 → (숫자 `Brush.Accent.Text` 28pt Bold + "초 후 메인 화면으로 돌아갑니다") → [이어서 진행하기](`Button.Primary`) → [메인 화면으로](`Button.Ghost`) | `MainWindow.xaml:180-194` |
| F24 | 만료 시 `ReturnHome("유휴 타임아웃", clearUser:false)` — **로그아웃하지 않는다**(it8 A1) | `AppShellViewModel.cs:528-538` |
| F25 | 경고 팝업이 떠 있는 동안 `NotifyUserActivity()`는 **무시**된다(버튼으로만 해제). 그리고 `NotifyUserActivity`의 호출부는 **촬영 화면 2곳뿐**이다 | `AppShellViewModel.cs:492-496` / `CaptureViewModel.cs:393,401` |
| F26 | 화면 전이 시 `UpdateIdleWatch()`가 **경고 오버레이를 내리고**(`HideIdleWarning()`) 감시를 재설정한다 | `AppShellViewModel.cs:498-506` |
| F27 | 카운트다운 규칙은 순수 클래스 `IdleCountdown`(headless 테스트 있음)이고 UI 타이머(`DispatcherTimer`)는 셸이 구동한다 | `IdleCountdown.cs` / `tests/MCPhoto.Tests/IdleCountdownTests.cs` |
| F28 | `CompleteSession` = `ReturnHome(clearUser:false)` + `ShowToast(SessionCompleteMessage)`. 완료 경로는 둘(QR 미사용 · QR 팝업 [완료])이며 단일 지점을 지난다 | `AppShellViewModel.cs:456-460` / `ResultViewModel.cs:156` / `QrPopupViewModel.cs:189` |
| F29 | `ShowToast`의 소비자가 완료 외에 3곳 더 있다(외부 카메라 강등 2 · 촬영 중단 1) → **토스트 인프라는 유지해야 한다** | `CaptureViewModel.cs:231,238,308` |

### 1.5 링크·폴더 열기 관례

| # | 사실 | 근거 |
|---|---|---|
| F30 | 앱 최초이자 유일한 `Hyperlink` 규약: 색 `Brush.Accent.Text` · 밑줄 상시 · hover 색 변경 없음 · **신규 리소스 키 0** · `AutomationProperties` 불요(링크 글자가 이름) | `SettingsView.xaml:545-560` |
| F31 | 폴더 열기 관례: `Process.Start("explorer.exe", "\"경로\"")` + `UseShellExecute=true`, 예외는 Warning 로그로 삼키고, **열기 동작은 주입 가능**(`opener`)해 테스트가 실제 explorer를 띄우지 않는다. 경로 텍스트는 UI에 항상 노출한다 | `LogFolderService.cs:9-47` |
| F32 | `MainWindow.xaml`은 **소스 스캔 방식으로 이미 테스트된다** — `StaticResource` 키가 테마에서 전부 해석되는지 검증(Window 인스턴스화 없음). 전체 파싱 로드 테스트는 `Themes/*.xaml`에만 있다 | `XamlResourceTests.cs:179-247,942-952` |
| F33 | `.cs`는 UTF-8 **BOM 없음**(한글 주석 포함), XAML·문서는 기존 인코딩 유지(설계 문서는 CRLF) | agent-memory `source-file-encoding` |

### 1.6 ⚠️ 설치 폴더 접근 전수 조사 (A-7의 근거)

`AppContext.BaseDirectory`를 쓰는 코드 **9곳 전부**를 확인했다. 쓰기는 정확히 3곳이다.

| 지점 | 대상 | 쓰기? |
|---|---|---|
| `ResultViewModel.cs:142` | `{exe}\result` (손님 사진) | **쓰기** ← A-1 |
| `ServiceRegistration.cs:140` | `{exe}\Frame` (`LocalFrameStore` 루트) | **쓰기** ← A-4 |
| `IniSettingsService.cs:144` | `{exe}\MCPhoto.ini` (1순위 후보) | **쓰기** ← A-6(불변) |
| `FrameCatalogService.cs:60` | `{exe}\Frame` (`BundleFolder` 스캔) | 읽기 |
| `LicenseNoticeService.cs:89` | `{exe}\licenses\` | 읽기 |
| `FfmpegRunner.cs:37` | `{exe}\tools\ffmpeg\ffmpeg.exe` 등 후보 탐색 | 읽기 |
| `IniBrandingService.cs:70` | `{exe}\branding.ini` | 읽기 |
| `SoundEffects.cs:22` | `{exe}\Assets\shutter.wav` | 읽기 |
| `SdkRuntimeProbe.cs:34` | `{exe}\`의 SDK 모듈 파일 | 읽기 |

→ **이관 범위는 닫혀 있다.** 3곳을 처리하면 앱이 설치 폴더에 쓰는 경로는 남지 않는다(읽기 6곳은 설치물을 읽는 정상 용도이므로 그대로 둔다).

---

## §2 ⚠️ 미검증 가정 (open assumptions)

| # | 가정 | 위험 | 검증 단계 |
|---|---|---|---|
| **U1** | `%ProgramData%\MCPhoto\result`에 **비승격 프로세스가 실제로 쓸 수 있다**(인스톨러 `[Dirs]`의 `users-modify`가 하위 신규 폴더에 상속된다) | 실패하면 `SaveAsync`가 `null`을 반환해 손님 사진이 저장되지 않는다(F4 — 조용한 실패) | **Step 2**(비승격 실행으로 저장 1회 + 폴더 확인). 인스톨러에 `result`를 `[Dirs]`로 **명시 추가**해 상속에 의존하지 않게 한다(§3.8) |
| **U2** | `%ProgramData%\MCPhoto\Frame`에 비승격 프로세스가 프레임 png/.slots를 쓸 수 있다 | 캐시 기록 실패 → F16으로 이번 실행 배제 → 프레임이 목록에 안 보인다 | **Step 3**(비승격 실행에서 기본 프레임 다운로드 1회) |
| **U3** | 이번 이관 이전에 배포된 설치본에 `{app}\result` 또는 `{app}\Frame` **데이터가 실제로 존재하는 PC가 있다** | 없으면 §3.4.3의 구 루트 읽기 폴백은 죽은 코드가 된다(무해하지만 복잡도만 남는다) | **Step 3**(폴백을 **경로 존재 시에만** 동작하게 설계 → 없는 환경에서 비용 0). 실측은 사용자 확인 사항(§14 UA-1) |
| **U4** | 탐색기가 열린 뒤 앱이 **자기 창을 앞으로 끌어오지 않는다**(`ReturnHome`·`NavigateAsync`는 `Window.Activate`를 호출하지 않는다 — 코드상 확인했으나 실행 관측 미실시) | 앱이 포커스를 훔치면 사용자가 보던 탐색기가 가려지고, 카운트다운 무간섭 판정(§4.4)의 근거가 약해진다 | **Step 6**(실행 관측: 링크 클릭 → 탐색기 확인 → 10초 대기 → 탐색기가 여전히 앞에 있는지) |
| **U5** | 잠금 키오스크(셸 교체·정책 제한) 환경에서 `explorer.exe` 실행이 **차단될 수 있다** | 차단되면 링크가 아무 일도 하지 않는 것처럼 보인다 | **Step 6**(실패 안내 문구 확인). 코드상으로는 `try/catch` + 캡션 노출로 이미 정직해진다(§5.3) |
| **U6** | 승격 실행으로 만들어진 기존 `{app}\MCPhoto.ini`를 가진 PC에서, 이번 변경(§3.5는 정책 무변경)이 설정 거동을 **바꾸지 않는다** | 바뀌면 기존 설치가 설정을 잃는다 — 이 설계에서 가장 회피하려는 사고 | **Step 1·4**(`SettingsPathResolver`·`IniSettingsService`의 경로 결정 코드에 **한 줄도 손대지 않는다**는 것을 diff와 테스트 T23으로 고정) |
| **U7** | 유휴 팝업이 `Qr` 상태에서 실제로 뜬다(업로드 후 QR 표시 화면에서 2분 무동작 → 경고) | 뜨지 않으면 링크가 사실상 도달 불가가 된다 — **기능의 존재 근거가 사라진다** | **Step 6**(실행 관측: QR 화면에서 2분 방치 → 팝업 + 링크 노출). ⚠️ 코드상 `Qr`은 `IsSessionActive`에 포함되므로(F21) 뜰 것으로 판단하지만, 이 기능의 유일한 실사용 창구이므로 반드시 실측한다 |

> 전 단계 완료 후에도 남는 미검증: 장기 운영에 따른 `%ProgramData%` 볼륨 압박. `result` 자동 정리 정책은 현재 없고(로컬 저장은 "TTL 무관·영구", `LocalSaveService.cs:8`) 이번에도 만들지 않는다.

---

## §3 A부 — 앱 쓰기 위치 이관 (이 설계의 무게 중심)

### 3.1 현행: 설치 폴더에 쓰는 것 3종

| 쓰는 것 | 경로 산출 지점 | 승격 실행 | 비승격 실행(= 정상) | 자산 등급 |
|---|---|---|---|---|
| `MCPhoto.ini` | `SettingsPathResolver.DefaultCandidates` → 실행경로 1순위, 쓰기 가능한 첫 곳 (F7) | `{app}\MCPhoto.ini` | `%ProgramData%\MCPhoto\MCPhoto.ini` | 운영자 설정 — **재구성 가능** |
| `Frame\` (다운로드·개인 프레임 캐시) | `ServiceRegistration.cs:140` (F11) | `{app}\Frame\` | **쓰기 실패** → 캐시 기록 실패 → 이번 실행 배제(F16) | 서버가 정본 (F15) — 재취득 가능하나 대기 UX 비용 |
| `result\` (손님 사진·타임랩스) | `ResultViewModel.cs:141-143` (F1) | `{app}\result\` | **쓰기 실패** → `SaveAsync`가 `null`, **조용히 미저장**(F4) | **손님 자산 — 유실 불가** |

문제는 세 층위다:

1. **같은 PC에서 실행 방식에 따라 목적지가 갈린다.** 승격/비승격이 다른 파일을 쓰므로 "설정을 바꿨는데 반영되지 않는다"가 재현 불가능한 형태로 발생한다.
2. **비승격이 정상 실행인데 조용히 실패한다.** Program Files 설치본의 정상 실행은 비승격이다. 그 경로에서 손님 사진이 저장되지 않고 **아무 안내도 없다** — F4의 크래시 금지 규약이 여기서는 조용한 유실로 작동한다.
3. **손님 자산이 Program Files에 쌓인다.** 백업·동기화 대상이 아니고, 제거 시 판단을 강요하며(F19가 이미 방어), 업그레이드 시 거동이 미정의다.

### 3.2 목적지 후보 비교

| 후보 | `result`(손님 사진) | `Frame`(캐시) | `MCPhoto.ini`(설정) |
|---|---|---|---|
| **현행 유지(실행경로)** | ❌ 비승격에서 조용히 미저장 · 제거·업그레이드와 얽힌다 | ❌ 비승격에서 캐시 기록 실패 → 프레임 미노출 | ⭕ 개발 실행과 설치본이 **격리**된다(이 정책의 실질 효용) · 기존 설치의 설정 유실 0 |
| **`%ProgramData%\MCPhoto`** | ⭕ 인스톨러가 이미 `users-modify`로 만든다(F18) · 계정이 바뀌어도 한 곳 · 동기화·인덱싱 대상 아님 · 제거 시 `dirifempty`로 보존됨(F19) · 로그·cache·sessions와 같은 지붕(F6) | ⭕ 같은 이유. 번들(`{exe}\Frame`)과 캐시가 개념상 분리된다 | ❌ 개발 실행이 **설치본과 같은 ini를 공유**한다 → dev에서 `[Test]`(인증 우회)를 켜면 설치본도 켜진다 · 기존 설치가 `{app}` ini를 쓰고 있어 순서를 바꾸면 설정을 잃는다 |
| **`%LocalAppData%\MCPhoto`** | ❌ 키오스크에서 로그온 계정이 바뀌면 결과물이 갈린다 — 운영자가 손님 사진을 못 찾는다 | ❌ 같은 이유 + 계정별 중복 다운로드 | (현행 3순위 그대로 두면 됨) |
| **`내 사진`(`SpecialFolder.MyPictures`)** | ⚠️ 발견성 최고 · **그러나** OneDrive/사진 앱이 동기화·인덱싱한다 → **손님 사진이 운영자 개인 클라우드로 나간다**(개인정보 사고) · 계정별로 갈린다 | ❌ 캐시를 사진 폴더에 두는 것은 의미 오류 | ❌ 해당 없음 |

### 3.3 판정 — `result` → `%ProgramData%\MCPhoto\result`

**`LocalSavePath`가 빈 값일 때의 기본 경로를 `Path.Combine(App.DataFolder, "result")`로 바꾼다.**

근거(우선순위 순):

1. **권한**: 인스톨러가 이미 만드는 폴더의 하위이며 `users-modify`다(F18). 승격 여부와 무관하게 쓸 수 있다 → 3.1의 문제 2(조용한 미저장)가 사라진다. ※ 상속에 의존하지 않도록 `result`를 `[Dirs]`에 명시 추가한다(§3.8, U1).
2. **한 곳에 모인다**: 키오스크는 계정이 하나뿐인 경우가 많지만 **바뀌는 경우**가 문제다. `%LocalAppData%`·`내 사진`은 계정이 바뀌면 사진이 두 곳으로 갈리고, 운영자는 "어제 사진이 없다"를 겪는다. `%ProgramData%`는 전 사용자 공유라 그 사고가 없다.
3. **유출 경로를 만들지 않는다**: `내 사진`은 발견성이 가장 높지만 그 발견성의 정체가 "사진 앱·OneDrive가 자동으로 집어간다"는 것이다. 손님 사진이 운영자 개인 클라우드로 올라가는 것은 이 앱이 만들어서는 안 되는 경로다(QR 업로드는 손님이 QR을 찍는 행위가 동의를 구성하지만, 클라우드 자동 동기화에는 그 동의가 없다).
4. **제거 안전성이 이미 성립**: F19가 `{commonappdata}\MCPhoto`를 `dirifempty`로만 지우므로, `result`가 있으면 데이터 폴더째 보존된다. 새 위치를 위해 인스톨러 규약을 새로 만들 필요가 없다.
5. **발견성 보완**: 3번의 대가로 잃는 발견성은 ① 유휴 팝업의 [결과물 폴더 열기](옵션 on일 때, §4) ② 설정 화면 "로컬 저장 경로" 아래 **실경로 캡션**(F9가 지금 비어 있는 자리, §6.3)이 메운다. ⚠️ ①은 도달 창구가 좁으므로(§4.3) **②가 발견성의 주력**이다.

> **기각: `내 사진`으로 옮기고 OneDrive 제외 설정을 안내한다.** ❌ 앱이 통제할 수 없는 사용자 환경 설정에 개인정보 보호를 의탁하는 설계다. 안내를 읽지 않은 부스 하나가 사고 하나다.

### 3.4 판정 — `Frame` 캐시 → `%ProgramData%\MCPhoto\Frame`, 번들은 `{exe}\Frame` 유지

#### 3.4.1 두 개념의 분리

지금 `{exe}\Frame` 한 폴더가 두 가지를 겸한다(it8 A2에서 의도적으로 합쳤던 것이다):

| 개념 | 성격 | 읽는 코드 | 새 위치 |
|---|---|---|---|
| **번들 프레임** | 배포물·운영자가 배치하는 읽기 전용 자산. `.slots` 없이 png만 두면 격자 자동 배치 | `FrameCatalogService.LoadBundleFrames` (F12·F13) | **`{exe}\Frame` 유지(불변)** |
| **프레임 캐시** | 서버에서 내려받은 공용 + 개인 프레임. `.slots` 서명 필수, 앱이 **쓴다** | `LocalFrameStore` (F13) | **`%ProgramData%\MCPhoto\Frame`** |

F13이 이 분리를 안전하게 만든다: **두 경로의 대상 파일 집합이 겹치지 않는다.** 그래서 폴더를 갈라도 우선순위 규약(§9 #11: 로컬 공용 → 번들 → fallback)이 그대로 성립한다. F14가 이동 안전성을 보장한다(서명이 경로에 묶이지 않는다).

현 배포물은 `Frame\`을 담지 않으므로(F20) 설치 환경에서 `{exe}\Frame`은 **애초에 없다** → 번들 스캔은 즉시 빈 목록을 반환하고(F12의 `Directory.Exists` 가드) 비용 0이다. 이 폴더는 "운영자가 프레임을 직접 배치할 수 있는 자리"로 남는다.

#### 3.4.2 왜 캐시를 옮기는가

비승격 실행(= 설치본의 정상 실행)에서 캐시 기록이 실패하면 단순한 "느림"이 아니다: 실패한 id는 F16에 따라 **이번 실행 동안 목록에 오르지 않는다.** 즉 **비승격 설치본은 기본 프레임이 안 보일 수 있다.**

#### 3.4.3 기존 데이터 — 구 루트를 읽기 소스로 상시 포함(이동 없음)

`LocalFrameStore`에 **읽기 전용 보조 루트**(legacy root)를 준다.

| 동작 | 새 루트(`%ProgramData%\MCPhoto\Frame`) | 구 루트(`{exe}\Frame`) |
|---|---|---|
| 읽기(`LoadPublic`·`LoadUser`·`PublicFrameNames`·`UserFrameNames`·`Inspect`) | ⭕ | ⭕ (폴더가 존재할 때만) |
| 쓰기(`SaveDefaultFrame`·`SaveUserFrame`) | ⭕ | ❌ 절대 쓰지 않는다 |
| 삭제(`DeleteLocal`) | ⭕ | ⭕ — F17로 이미 성립(서버에서 삭제된 프레임의 캐시 정리가 구 루트에도 미쳐야 한다) |
| 이름 충돌 | **새 루트 우선** | — |

이동(move/copy)을 하지 않는 이유:

- **승격 권한**: `{app}` 하위에서 파일을 지우려면 승격이 필요하다. 비승격이 정상이므로 이동은 대개 "복사만 성공, 원본 잔존"으로 끝난다 → 같은 프레임이 두 벌 남는다.
- **부분 실패**: png는 옮겼는데 `.slots`가 잠겨 실패하면 그 프레임은 **양쪽에서 모두 프레임이 아니다**(F13) → 사용자에게는 "프레임이 사라졌다"로 보인다.
- **개인 프레임**: 서버가 정본이지만(F15) 서버 도달 실패·재로그인 전에는 로컬 캐시가 화면에 프레임을 띄우는 유일한 사본이다.
- **비용이 낮다**: 구 폴더가 없으면 열거를 건너뛴다(U3 대응) — 신규 설치·개발 환경에서 비용 0.

> **기각: 무시(구 데이터 버림).** ❌ 공용은 재다운로드가 발생해 it20이 개선한 최초 진입 대기를 되돌린다. 개인 프레임은 재로그인·서버 도달이 성립해야 복구된다 → 오프라인 부스에서 자산이 사라진 것처럼 보인다.

### 3.5 판정 — `SettingsPathResolver` 정책 불변 + 관측 가능성만 추가

**`SettingsPathResolver`·`IniSettingsService`의 경로 결정 코드는 한 줄도 바꾸지 않는다.**

1. **기존 설치가 설정을 잃는다.** 승격으로 설치·실행해 온 PC는 `{app}\MCPhoto.ini`에 자기 설정(백엔드 URL·프린터·외부 카메라 노출값 등)을 갖고 있다. 후보 순서를 `%ProgramData%` 우선으로 바꾸면 그 파일이 **읽히지 않고**, 앱은 기본값으로 시작한 뒤 첫 종료(`MainWindow.OnClosing`이 무조건 `Save()`)에 새 위치에 기본값을 기록한다. 되돌릴 수 없는 유실이다.
2. **개발 실행과 설치본의 격리가 이 정책의 실질 효용이다.** 순서를 바꾸면 개발 실행이 설치된 키오스크와 **같은 ini를 공유**한다 — 개발자가 `[Test]`(인증 우회)를 켜면 같은 PC의 설치본에도 켜진다. 설정 혼동이 아니라 보안 사고다.
3. **위험 등급이 다르다.** ini는 운영자 자산이고 재구성 가능하다. `result`는 손님 자산이고 재구성 불가다.
4. **실제 사고 원인은 위치 정책이 아니라 실행 방식의 비결정성**이다. 설치본은 비승격 실행이 정상이고, 승격 실행은 운영자가 명시적으로 누른 예외 조작이다. 그 예외를 **관측 가능하게** 만드는 것이 옳은 대응이다.

추가하는 것(둘 다 저비용·무위험):

| 추가 | 내용 |
|---|---|
| **시작 시 경고 로그 1줄** | `App.OnStartup`에서 `ISettingsService.IniPath`가 `%ProgramFiles%`/`%ProgramFiles(x86)%` 하위이면 Warning(M9). 판정은 순수 함수 `SettingsPathDiagnostics.IsUnderProgramFiles(path, programFiles, programFilesX86)`로 두어 headless 테스트 대상으로 만든다 |
| **진단 화면은 이미 노출 중** | it23이 `ISettingsService.IniPath`를 계약에 올려 진단 모달에 표시한다 — **추가 UI 없음**. 이 설계는 그 결정을 근거로 삼기만 한다 |

### 3.6 기존 데이터 처리 전수표 — "유실 경로 0" 증명

| 기존 데이터 | 이번 변경이 그것에 하는 일 | 유실 가능성 |
|---|---|---|
| `{app}\result\mcphoto_*\`(손님 사진) | **아무 것도 하지 않는다.** 앱은 `result`를 쓰기만 하고 읽지 않는다(§1.6에서 전수 확인) → 새 기본값은 새 세션만 새 폴더로 보낸다. 인스톨러는 이미 이 폴더를 지우지 않는다(F19) | **없음** (읽지도·쓰지도·지우지도 않음) |
| 운영자가 `LocalSavePath`로 지정한 폴더 | 명시값 우선이 불변이므로 **경로가 바뀌지 않는다**(§3.7) | **없음** |
| `{app}\Frame\*.png/.slots`(공용 캐시) | 읽기 소스로 계속 포함. 쓰기는 새 루트 | **없음** |
| `{app}\Frame\users\{hash}\*`(개인 캐시) | 동일 | **없음** |
| `{app}\MCPhoto.ini` | 정책 무변경 → 계속 1순위로 읽고 쓴다 | **없음** |
| `%ProgramData%\MCPhoto\{logs,cache,sessions}` | 무관(변경 없음) | 없음 |

**운영자가 구 폴더를 찾을 수 있어야 한다** → 시작 시 `{exe}\result`가 존재하면 Warning 로그 M10. UI는 만들지 않는다(손님 앞 화면에 구 경로를 띄울 이유가 없다).

### 3.7 단일 지점 — `LocalSavePathResolver`

기본 경로 산출이 지금 `ResultViewModel`에 인라인이다(F1). 소비자가 늘어나므로(저장 · 설정 캡션) **Core의 순수 함수**로 옮긴다.

```
MCPhoto.Core/LocalSave/LocalSavePathResolver.cs
  public static string Resolve(string? configuredPath, string dataFolder)
      → configuredPath가 공백이 아니면 configuredPath.Trim()
      → 아니면 Path.Combine(dataFolder, "result")
```

- **명시값이 항상 우선**이라는 규칙이 코드 한 곳에만 존재한다(A-3의 기계적 보증).
- `dataFolder`를 인자로 받는다 → Core가 `App.DataFolder`(WPF 계층)에 의존하지 않고, 테스트가 임시 폴더를 넣을 수 있다.
- 소비자: `ResultViewModel.Next`(저장) · `SettingsViewModel`(캡션). **유휴 팝업은 이 함수를 쓰지 않는다** — 열 경로는 F2의 반환값이다(§5.1).

### 3.8 인스톨러 갱신 (`installer/MCPhoto.iss`)

| 절 | 변경 | 이유 |
|---|---|---|
| `[Dirs]` | `{commonappdata}\MCPhoto\result`(users-modify) · `{commonappdata}\MCPhoto\Frame`(users-modify) **추가** | U1·U2를 상속에 의존하지 않고 명시 보장. 비승격 첫 실행이 폴더 생성부터 실패하는 경로를 없앤다 |
| `[UninstallDelete]` | `{app}\Frame` 삭제 행 **유지**(구 캐시는 재취득 가능) · `{app}\result` 삭제 금지 주석 **강화**(이관 후에는 "구 버전이 남긴 손님 자산"이므로 더 중요) · `{commonappdata}\MCPhoto\Frame`을 **캐시로서 삭제 대상에 추가** · `{commonappdata}\MCPhoto\result`는 **절대 추가하지 않는다**(주석으로 명시) | 캐시는 지우고 자산은 남기는 현행 원칙을 새 위치로 확장 |
| 헤더 주석 | "데이터 폴더는 %ProgramData%\MCPhoto"에 **결과물·프레임 캐시 포함**을 반영 | 주석이 곧 배포 규약 문서다 |

⚠️ `[Files]`는 **바꾸지 않는다** — `Frame\`·`result\`·`MCPhoto.ini`는 계속 담지 않는다(F20 화이트리스트 원칙).

### 3.9 이관 후 경로 지도 (문서·진단의 기준표)

| 경로 | 성격 | 쓰기 주체 | 제거 시 |
|---|---|---|---|
| `{app}\MCPhoto.exe` · `licenses\` · `tools\ffmpeg\` · `Assets\` | 배포물 | 인스톨러 | 삭제 |
| `{app}\Frame\` | **읽기 전용 번들**(운영자 배치) | 없음(이관 후) | 삭제(F19 유지) |
| `{app}\branding.ini` | 읽기 전용(운영자 배치) | 없음 | 삭제 |
| `{app}\MCPhoto.ini` | 설정(**승격 실행 시에만** 생김) | 앱 | 삭제 |
| `{app}\result\` | **구 버전 손님 자산** | 없음(이관 후) | **보존** |
| `%ProgramData%\MCPhoto\MCPhoto.ini` | 설정(비승격 실행) | 앱 | 삭제 |
| `%ProgramData%\MCPhoto\Frame\` | **프레임 캐시(신규)** | 앱 | 삭제 |
| `%ProgramData%\MCPhoto\result\` | **손님 자산(신규 기본)** | 앱 | **보존** |
| `%ProgramData%\MCPhoto\{logs,cache,sessions}\` | 로그·fallback 캐시·세션 임시물 | 앱 | 삭제 |
| `%LocalAppData%\MCPhoto\MCPhoto.ini` | 설정(3순위 폴백) | 앱 | (현행대로 미삭제) |

---

## §4 B부 — 유휴 팝업에 [결과물 폴더 열기] 링크 추가

### 4.1 만들지 않는 것 (전제 정정의 결과)

| 항목 | 상태 |
|---|---|
| 완료 팝업(홈 복귀 전 오버레이) | **만들지 않는다** |
| `CompleteSession`(홈 복귀 + 완료 토스트, `9b59fb6`) | **현행 유지** — 한 줄도 바꾸지 않는다 |
| `SessionCompleteMessage` 상수·완료 토스트 | **현행 유지**(F29의 다른 소비자와 함께 존속) |
| 새 `AppState`·화면 VM·`DataTemplate` | 0개. `AppStateTests.Done_State_Is_Retired` 계속 통과 |
| 새 카운트다운·새 `DispatcherTimer` | 0개 |
| `IdleWarningSeconds`(120) · `IdleCountdownSeconds`(10) | **상수 무변경** — 요구 "10초 카운팅"은 이미 충족(F22) |
| 두 팝업 동시 노출 방지 불변식 | **불필요**(팝업이 하나뿐이다) |

이 정정으로 rev1이 다루던 "모달 부활이 `9b59fb6`의 실패를 재현하는가", "완료 팝업과 유휴 경고의 카운트다운 충돌" 두 논점이 **모두 소멸**했다. 남은 것은 **기존 오버레이에 링크 1줄을 얹는 변경**이다.

### 4.2 유휴 팝업 거동 — 현행 유지 (판정)

**변경 없음.** 근거:

- 유휴 감시는 `IsSessionActive`에서만 시작되고 그 집합에 `Home`이 없다(F21) → **홈에서는 팝업이 뜨지 않는다.**
- 따라서 팝업이 뜬 시점의 `CurrentState`는 **항상 세션 활성 상태**이고, "초 후 메인 화면으로 돌아갑니다"는 **항상 참**이다. "이미 홈인데 또 홈으로 간다"는 모순은 성립하지 않으므로 문구를 고칠 이유가 없다.
- `CompleteSession`은 즉시 홈으로 가고 그 시점에 `UpdateIdleWatch()`가 감시를 멈춘다(F21·F26) → 완료 후에는 이 팝업이 뜨지 않는다.
- `IdleCountdownSeconds`는 이미 10이다(F22).

### 4.3 ⚠️ 링크가 실제로 닿는 창구 (정직한 도달 가능성)

유휴 팝업은 세션 활성 상태 **어디서든** 뜬다. 그런데 링크는 "이 세션의 로컬 저장이 이미 끝났을 때"만 의미가 있다. 상태별로:

| 상태 | 이 시점에 로컬 저장이 끝났는가 | 링크 |
|---|---|---|
| `FrameSelect` · `Guide` · `Capture` · `CutSelect` | 아니다(저장은 Result의 [다음]에서 일어난다) | **숨김**(경로 `null`) |
| `Result` | 아니다 — [다음] 처리 중의 아주 짧은 구간(저장 완료 후 화면 전이 직전)만 예외 | 사실상 숨김 |
| **`Qr`** | **끝났다** — 저장 → 업로드 → QR 표시 순서다(`ResultViewModel.Next`) | **노출**(옵션 on이면) |

**즉 이 기능의 실사용 흐름은 하나다: 손님이 QR을 찍는 동안(또는 업로드 실패 안내를 보는 동안) 2분 무동작 → 유휴 팝업 → [결과물 폴더 열기].** 문서·테스트는 이 흐름을 기준으로 쓴다.

> **한계를 명시한다**: 부스 소유자가 "지금 저장 폴더를 열고 싶다"고 해서 이 링크에 도달하려면 **QR 화면에서 2분을 무동작으로 기다려야** 한다. 발견 경로로는 실용성이 낮다. 사용자 요구가 유휴 팝업이므로 그대로 구현하되, 상시 접근 경로는 §14의 후속 후보로만 기록한다(이번 범위 아님).

### 4.4 카운트다운 간섭 — 없음 (판정)

**[결과물 폴더 열기]를 눌러도 카운트다운은 멈추지도 연장되지도 않는다.** 사용자 명시 지시("링크를 눌러도 카운트다운을 멈추지마 괜찮아").

- 일시정지·연장 로직을 **아예 만들지 않는다** — 단순함이 요구다.
- 코드상 추가 조치가 **필요 없다**: `NotifyUserActivity()`는 경고 표시 중 무시되고(F25), 링크 커맨드는 그 경로를 지나지도 않는다. `ContinueSession`(=[이어서 진행하기])만이 유일한 리셋 경로이며 그것도 변경 없다.
- 만료 시 동작은 현행 그대로: `HideIdleWarning()` → `ReturnHome("유휴 타임아웃", clearUser:false)`.
- 피해가 없는 이유: 탐색기 창은 별 프로세스이고 앱 상태와 독립이다 — 앱이 홈으로 가도 **열린 폴더 창은 닫히지 않는다**(U4에서 실측).
- 부수 규칙: **앱은 자기 창을 앞으로 끌어오지 않는다.** `Window.Activate`·`Topmost` 조작이나 `RequestApplyDisplayMode()` 호출을 새로 넣지 않는다.

### 4.5 세션 폴더 경로 — 보관 위치와 수명

| 항목 | 판정 |
|---|---|
| 보관 위치 | `SessionContext.LocalSaveFolder`(`string?`, 신설) |
| 채우는 지점 | `ResultViewModel.Next` — `ILocalSaveService.SaveAsync`의 **반환값**(F2). 저장을 시도하지 않았거나 실패하면 `null` |
| 수명 | **현재 세션.** `SessionContext.Reset()`에서 `null`로 초기화한다 → 홈 복귀·유휴 만료·로그아웃·완료 어느 경로로든 세션이 끝나면 사라진다 |
| 앱 수명 보관(마지막 완료 세션 기억) | **하지 않는다** |

앱 수명 보관을 기각하는 이유: 팝업은 세션 활성 중에만 뜨므로(F21) 현 세션 경로만 있으면 충분하고, 앱 수명 보관은 **다음 손님의 세션 중에 뜬 팝업이 이전 손님 폴더를 가리키는** 경로를 새로 만든다 — §5.1이 막으려는 바로 그 사고다.

⚠️ `Reset()`에서 `null` 초기화는 **필수**다. 누락하면 위 사고가 실제로 발생한다(테스트 T9로 잠근다).

### 4.6 링크 가시성 — 순수 함수 1곳

```
MCPhoto.Core/LocalSave/ResultFolderLinkPolicy.cs
  public static bool ShouldShow(string? sessionFolder, bool enableResultFolderOpen)
      => enableResultFolderOpen && !string.IsNullOrEmpty(sessionFolder);
```

| 입력 | 출처 |
|---|---|
| `sessionFolder` | `SessionContext.LocalSaveFolder`(§4.5) — `SaveLocalCopy=false`·저장 실패는 자동으로 `null` |
| `enableResultFolderOpen` | `AppSettings.EnableResultFolderOpen`(신설, 기본 **false**, §7) |

- **로그인 게이트 없음**(사용자 정정 — 옵션이 유일한 게이트, §5.2).
- 값은 `ShowIdleWarning()` 시점에 **1회 계산**해 `[ObservableProperty] IsResultFolderLinkVisible`에 담는다(바인딩마다 재계산하지 않는다).
- **숨김**(`Collapsed`)이며 비활성이 아니다 — 카드 높이가 줄어 팝업이 종전과 같은 모양을 유지한다.

---

## §5 C부 — 폴더 열기의 위험 2건

### 5.1 개인정보 — 세션 폴더만 연다

`LocalSaveService`는 `{저장경로}\mcphoto_{yyMMdd_HHmm}\`에 **세션별로** 저장한다. 저장 루트를 열면 **직전 손님들의 사진이 전부 보인다** — 폴더명이 촬영 시각이므로 "몇 시 몇 분에 온 손님"까지 드러난다.

**판정: 그 세션 폴더만 연다. 루트를 여는 경로는 만들지 않는다.**

경로 출처(정확성이 걸린 부분):

| 방법 | 판정 |
|---|---|
| `SaveAsync`의 **반환값**을 `SessionContext.LocalSaveFolder`에 보관 | ✅ **채택.** 실제로 만들어진 폴더 그 자체다 |
| `Path.Combine(저장경로, LocalSaveService.SessionFolderName(session.SessionTime))`로 **재계산** | ❌ **금지.** F3: 같은 분에 두 세션이 겹치면 실제 폴더는 `mcphoto_260812_1445-2`인데 재계산은 `mcphoto_260812_1445`를 만든다 → **직전 손님의 폴더를 연다.** 게다가 `Reset()`이 `SessionTime`을 갱신하므로 시점에 따라 값도 달라진다 |
| `Directory.GetDirectories(루트)`에서 최신 폴더 선택 | ❌ 루트를 읽어야 하고, 동시 세션·시계 변경에 취약하며, 결국 추측이다 |

### 5.2 키오스크 보안 — ini 옵션이 유일한 게이트

탐색기를 여는 것은 잠금 키오스크에서 **파일시스템 접근 통로**다. 세션 폴더만 열어도 주소창·상위 폴더 이동으로 어디로든 갈 수 있다 — **세션 폴더 제한은 §5.1(다른 손님 노출)을 해결하지만 §5.2(시스템 접근)를 해결하지 못한다.**

사용자 정정이 이 절의 형태를 확정했다: *"이것도 옵션화하고 창모드가 아니더라도 옵션화했으니 상관없을 것 같아. 지원해도돼.(설정에 따른 것)"*

**판정: 노출 여부는 ini `EnableResultFolderOpen` 하나가 결정한다. `DisplayMode=Windowed` 게이트도, 로그인 게이트도 넣지 않는다.**

| 넣지 않는 게이트 | 이유 |
|---|---|
| `DisplayMode=Windowed`일 때만 | 사용자 명시 기각("창모드가 아니더라도 … 지원해도돼"). 실운영은 전체화면이라 그 게이트는 기능을 사실상 없앤다 |
| 로그인(비게스트)일 때만 | 사용자 정정이 "옵션에 따른 것"으로 정리했다. 게이트를 둘로 만들면 "옵션을 켰는데 안 보인다"는 혼동이 생기고, 운영자가 게스트 상태로 부스를 돌리는 경우 자기도 못 쓴다 |
| power 역할만 | 부스 운영자는 대개 일반 `User`다 |

**그 대가: 위험은 전부 운영자가 옵션을 켤 때 감수하는 것이 된다.** 따라서 두 가지가 필수다.

1. **기본값 off** (`EnableResultFolderOpen = 0`). 근거:
   - 링크가 붙는 팝업은 **손님(게스트) 앞에서 무인으로** 뜬다(§0.2 ②). 설치 직후의 부스가 **모르는 채로** 손님에게 파일 브라우저를 건네는 상태가 되어서는 안 된다 — fail-safe 기본값이다.
   - 이 기능의 사용자는 부스 소유자이고, 소유자는 정의상 설정을 열어 켤 수 있다. 반대로 손님은 옵션을 끌 수 없다. **끄는 쪽이 어려운 값을 기본값으로 둔다.**
   - 리포 관례: 새 기능 토글은 off로 들어온다(`FlashMode`·`ShutterSound`·`RetakeEnabled`·`ExternalCameraEnabled`·`PhotoPrinterEnabled` 전부 기본 off).
   - 대가: 기본 상태에서는 링크가 보이지 않는다. 이것을 "요구 미충족"으로 보지 않는 이유는 **요구 자체가 "옵션화"였기** 때문이다(옵션의 기본값은 요구에 명시되지 않았다). 설정 화면 행 라벨·캡션이 기능의 존재를 알린다.
2. **설정 항목 캡션이 위험을 말한다**(문구 M8): 잠금 키오스크에서 탐색기를 통해 다른 폴더에 접근할 수 있다는 사실을 켜기 전에 읽게 한다.

> **잔여 위험(문서화 필수)**: `EnableResultFolderOpen=1`인 부스에서는, QR 화면에서 2분 무동작으로 유휴 팝업이 뜬 손님이 **[결과물 폴더 열기]로 탐색기를 열 수 있다**(그 세션 폴더로 열리지만 거기서 임의 위치로 이동 가능). 노출 창은 팝업이 떠 있는 10초이며, 옵션을 끄면 표면이 완전히 사라진다. 이 위험은 운영자의 명시적 선택으로 남는다.

> **기각: 링크 대신 "폴더 경로를 클립보드에 복사"**(`IClipboardService`가 이미 있다). ❌ 사용자 요구가 "버튼을 누르면 해당 폴더가 열리는 방안으로 채택"이다.

### 5.3 열기 실패 — best-effort, 예외 금지

`LogFolderService`(F31)의 관례를 계승하되 **기존 서비스는 건드리지 않고** 신규 서비스를 만든다.

```
MCPhoto.App/Services/IFolderOpener.cs
  bool TryOpen(string path);      // 성공 여부만 반환 — 예외를 밖으로 내보내지 않는다

MCPhoto.App/Services/FolderOpener.cs
  ctor(ILogger<FolderOpener>? logger = null, Action<string>? opener = null)   // opener 주입 = 테스트 이음새
  TryOpen:
    - 경로 공백 → false (호출부가 이미 링크를 숨겼어야 하는 상태)
    - !Directory.Exists(path) → Warning 로그 + false   ⚠️ CreateDirectory 하지 않는다
    - Process.Start("explorer.exe", "\"{path}\"") { UseShellExecute = true } → true
    - 예외 → Warning 로그 + false
```

| 결정 | 이유 |
|---|---|
| `ILogFolderService`를 일반화하지 않는다 | it24 관례(인터페이스 이름·소비자 유지). 중복은 4줄이고, 진단 화면의 검증된 경로를 건드릴 이득이 없다 |
| `Directory.CreateDirectory`를 **하지 않는다** | `LogFolderService`는 로그 폴더가 없을 수도 있어 만든다. 여기서는 **폴더가 없다는 사실 자체가 정보**다(사진이 없다는 뜻) — 빈 폴더를 만들어 "저장된 것처럼" 보이게 하면 거짓이다 |
| DI 등록 | `services.AddSingleton<IFolderOpener, FolderOpener>()` (`ILogFolderService` 등록 옆) |
| 실패 시 UI | 팝업 안 캡션으로 **경로를 노출**한다(수동 탐색 가능, F31 관례) + 링크는 그대로 둔다(재시도 가능). **카운트다운은 계속** |
| VM 경계 | 셸 VM은 `System.Diagnostics`를 참조하지 않는다(`IClipboardService` 주석의 경계 규약) |

⚠️ 실패 캡션은 **팝업 안**에만 표시한다. 홈 복귀 후 토스트로 남기지 않는다 — 다음 손님 화면에 이전 손님의 저장 경로가 뜨는 것을 막는다. 그리고 `HideIdleWarning()`·`ShowIdleWarning()` 양쪽에서 캡션을 비워 **다음 팝업에 stale 오류가 남지 않게** 한다.

---

## §6 UI 명세

### 6.1 유휴 팝업 — 추가되는 2줄 (`MainWindow.xaml:173-196`)

기존 마크업은 **그대로 두고**, `[메인 화면으로]` 버튼 **뒤**에 형제 2개를 추가한다.

```
  └ StackPanel                                  ← 기존
    ├ TextBlock  "잠시 자리를 비우셨나요?"            ← 기존(불변)
    ├ StackPanel (카운트다운 숫자 + 문구)              ← 기존(불변)
    ├ Button     "이어서 진행하기"  Button.Primary     ← 기존(불변)
    ├ Button     "메인 화면으로"    Button.Ghost       ← 기존(불변)
    │
    ├ TextBlock  Style="{StaticResource Text.Caption}"           ← 신규 ①(링크)
    │            HorizontalAlignment="Center" Margin="0,14,0,0"
    │            Visibility="{Binding IsResultFolderLinkVisible, Converter={StaticResource BoolToVis}}"
    │   └ Hyperlink Foreground="{StaticResource Brush.Accent.Text}"
    │               Command="{Binding OpenResultFolderCommand}">결과물 폴더 열기</Hyperlink>
    │
    └ TextBlock  Style="{StaticResource Text.Caption}"           ← 신규 ②(열기 실패 안내)
                 Foreground="{StaticResource Brush.Text.Muted}"
                 HorizontalAlignment="Center" TextWrapping="Wrap" Margin="0,8,0,0"
                 MaxWidth="380"
                 Text="{Binding ResultFolderOpenError}"
                 Visibility="{Binding HasResultFolderOpenError, Converter={StaticResource BoolToVis}}"
```

| 항목 | 값 | 근거 |
|---|---|---|
| 배치 | 두 버튼 **아래**(카드 최하단) | 링크는 3차 액션이다. 위에 두면 주 CTA([이어서 진행하기])의 시각 위계를 깬다 |
| 숨김 방식 | `Visibility=Collapsed`(BoolToVis) | 숨겨지면 카드 높이가 줄어 **종전과 똑같은 팝업**이 된다(기본 off이므로 대부분의 부스에서 이 모습이다) |
| 링크 스타일 | `Hyperlink` + 인라인 `Foreground="{StaticResource Brush.Accent.Text}"` | F30 규약(밑줄 상시·hover 색 변경 없음·`AutomationProperties` 불요·신규 키 0) |
| 오류 캡션 | `Text.Caption` + `Brush.Text.Muted` + `MaxWidth=380` | 경로가 길어 카드가 무한히 넓어지지 않게(`MinWidth=420`인 카드 안쪽 여백 고려) |
| 카운트다운 숫자·문구·두 버튼 | **불변** | F23 |
| 신규 리소스 키 | **0개** | D-1. `Text.Caption`·`Brush.Accent.Text`·`Brush.Text.Muted`·`BoolToVis`는 모두 기존 |
| `Grid.Row="1"`·scrim·`MinWidth=420` | **불변** | F22(배너 보존 = TM4) |

### 6.2 설정 화면 — 로컬 저장 경로 실경로 캡션 (§3.3 근거 5의 주력)

`SettingsView.xaml:448-450`의 "로컬 저장 경로" 입력란 **아래**에 캡션 1줄을 추가한다(F9가 비워 둔 자리).

```
StackPanel Style="{StaticResource FullRow}"
├ TextBlock "로컬 저장 경로"        Style=Text.Label          ← 기존
├ TextBox   {Binding LocalSavePath}                          ← 기존
└ TextBlock {Binding LocalSavePathEffectiveNote}  Style=Text.Caption    ← 신규
            Foreground="{StaticResource Brush.Text.Muted}"  TextWrapping=Wrap  Margin=0,4,0,0
```

- `SettingsViewModel.LocalSavePathEffectiveNote` = `FormatLocalSavePathNote(LocalSavePathResolver.Resolve(LocalSavePath, App.DataFolder))`
- `LocalSavePath` 변경 시 갱신 — `[ObservableProperty]` 선언에 `[NotifyPropertyChangedFor(nameof(LocalSavePathEffectiveNote))]`를 붙인다.
- **비어 있을 때가 이 캡션의 존재 이유다** — 빈 값이 어디를 뜻하는지 화면이 말해 준다. 값이 있으면 입력값과 같은 문자열이 되지만 그대로 표시한다(분기하면 "언제 보이는지" 규칙이 하나 늘어난다).

### 6.3 설정 화면 — `결과물 폴더 열기` 옵션 행

"로컬 저장" 행 **다음**에 하위 행으로 추가한다(로컬 저장의 하위 개념 → `↳` 접두, 기존 QR 하위 행 관례).

```
Grid Style="{StaticResource SettingRow}"
├ TextBlock "↳ 유휴 팝업에 결과물 폴더 열기"  Style=RowLabel
└ ToggleButton Style=Toggle  HorizontalAlignment=Right  IsChecked="{Binding EnableResultFolderOpen}"
StackPanel Style="{StaticResource FullRow}"
└ TextBlock (캡션 M8)  Style=Text.Caption  Foreground=Brush.Text.Muted  TextWrapping=Wrap
```

라벨에 "유휴 팝업"을 명시하는 이유: 이 옵션을 켜도 **완료 직후에 링크가 보이지 않는다**(§4.3). 라벨이 어디에 나타나는지 말하지 않으면 운영자는 켜고 나서 찾지 못한다.

### 6.4 동결 문구표 (한 글자도 임의로 바꾸지 않는다)

| # | 위치 | 문구 | 상수 |
|---|---|---|---|
| M1 | 유휴 팝업 링크 | `결과물 폴더 열기` | XAML 리터럴 |
| M2 | 폴더 열기 실패 안내 | `폴더를 열 수 없습니다. 저장 위치: {경로}` | `AppShellViewModel.FormatResultFolderOpenError(path)` |
| M3 | 설정 — 로컬 저장 경로 캡션 | `실제 저장 위치: {경로}` | `SettingsViewModel.FormatLocalSavePathNote(path)` |
| M4 | 설정 — 옵션 행 라벨 | `↳ 유휴 팝업에 결과물 폴더 열기` | XAML 리터럴 |
| M5 | 설정 — 옵션 캡션 | `자리를 비웠을 때 뜨는 안내 팝업에서 그 세션의 저장 폴더를 열 수 있습니다. 손님이 조작하는 잠금 키오스크에서는 끄세요 — 탐색기를 통해 다른 폴더에 접근할 수 있습니다.` | XAML 리터럴 |
| M6 | 시작 로그(설치 폴더 ini) | `설정 파일이 설치 폴더에 있습니다: {Path} — 승격 실행 여부에 따라 설정이 갈릴 수 있습니다` | `App.xaml.cs` Warning |
| M7 | 시작 로그(구 result 잔존) | `이전 버전이 설치 폴더에 저장한 결과물이 있습니다: {Old} — 새 저장 위치는 {New}입니다` | `App.xaml.cs` Warning |

**불변 문구**(건드리지 않는다): `잠시 자리를 비우셨나요?` · `초 후 메인 화면으로 돌아갑니다` · `이어서 진행하기` · `메인 화면으로` · `AppShellViewModel.SessionCompleteMessage`.

⚠️ M5에서 "유휴"라는 내부 용어를 쓰지 않고 `자리를 비웠을 때 뜨는 안내 팝업`으로 풀어 쓴다 — 팝업 본문(`잠시 자리를 비우셨나요?`)과 같은 어휘라 운영자가 어느 화면인지 즉시 안다.

---

## §7 설정 스키마 변경표

### 7.1 ini 키

| 키 | 섹션 | 형 | 기본값 | Clamp | Clone | Read/Write | 게스트 |
|---|---|---|---|---|---|---|---|
| `EnableResultFolderOpen` | `[MCPhoto]` | bool | **`false`** | 없음(`GetBool`이 파싱 실패 시 기본값 폴백) | ⭕ `AppSettings.Clone()`에 1행 | `ini.GetBool` / `ini.SetBool` | Load 시 `IsGuest`면 표시값 `false`, Save 시 `if (!IsGuest)` 가드 |
| `LocalSavePath` | `[MCPhoto]` | string | `string.Empty` (**의미 변경**: 빈 값 = `%ProgramData%\MCPhoto\result`, 종전 `{exe}\result`) | 변경 없음 | 기존 유지 | 기존 유지 | 기존 유지(F8 — 가드 없음, 이번에도 바꾸지 않는다) |

- **키 이름 선택**: `EnableResultFolderOpen`. `ShowResultFolderLink`(표시 여부)도 후보였으나, 이 키가 허가하는 것은 **탐색기 실행**이라 `Enable…`이 실체에 맞고 `EnableQrDelivery` 선례와도 맞다. ini를 손으로 읽는 운영자가 "링크 라벨 토글"로 오해하지 않게 한다.
- **마이그레이션 불요**: 키가 없으면 기본값 폴백이 규약이다(it23·it24·it25 동일). 기존 ini에 키가 없으면 `false`로 동작하고 첫 저장에 기록된다.
- **`LocalSavePath` 값 자체는 절대 건드리지 않는다.** 이관은 "빈 값의 해석"만 바꾼다(A-3).
- **게스트 게이트를 두는 이유**: 게스트도 PIN 없이 설정에 진입한다(F10). 링크 노출에 로그인 게이트가 **없으므로**(§5.2) 이 키가 유일한 방어선이고, **손님이 그것을 켤 수 있어서는 안 된다.** 규약은 it12 R1의 "표시 전용 off + Save 미기록"을 그대로 쓴다.

> ⚠️ rev1과의 차이: rev1은 링크에 로그인 게이트가 있어 게스트가 키를 켜도 무효였다. rev2에서는 **게스트가 키를 켜면 실제로 링크가 노출된다** → 게스트 편집 게이트가 선택이 아니라 **필수**가 되었다.

### 7.2 코드 상수

이번 이터레이션은 새 코드 상수를 만들지 않는다. `IdleWarningSeconds`(120) · `IdleCountdownSeconds`(10) · `ToastSeconds`(5) **전부 불변**.

---

## §8 계약·파일별 변경 명세

### 8.1 신규 파일

| 파일 | 책임 | 계층 |
|---|---|---|
| `src/MCPhoto.Core/LocalSave/LocalSavePathResolver.cs` | 순수 — 설정값/기본값 해석 단일 지점(§3.7) | Core |
| `src/MCPhoto.Core/LocalSave/ResultFolderLinkPolicy.cs` | 순수 — 링크 2조건 판정(§4.6) | Core |
| `src/MCPhoto.Core/Settings/SettingsPathDiagnostics.cs` | 순수 — `IsUnderProgramFiles`(§3.5) | Core |
| `src/MCPhoto.App/Services/IFolderOpener.cs` | 폴더 열기 계약 | App |
| `src/MCPhoto.App/Services/FolderOpener.cs` | best-effort 구현 + `opener` 이음새(§5.3) | App |

### 8.2 수정 파일

| 파일 | 변경 |
|---|---|
| `src/MCPhoto.App/ServiceRegistration.cs` | ① `LocalFrameStore` 루트를 `%ProgramData%\MCPhoto\Frame`로, 구 루트를 legacy 읽기 루트로 전달 ② `IFolderOpener` 등록 |
| `src/MCPhoto.Core/Frames/LocalFrameStore.cs` | ctor에 `string? legacyReadRoot = null`. 읽기 경로가 두 루트를 순회하고 **이름 기준 새 루트 우선** dedup. 쓰기는 `_root`만(변경 없음) |
| `src/MCPhoto.App/ViewModels/ResultViewModel.cs` | 기본 경로 인라인(`:141-143`) → `LocalSavePathResolver.Resolve(...)`. `SaveAsync` 반환값을 `session.LocalSaveFolder`에 저장 |
| `src/MCPhoto.App/SessionContext.cs` | `string? LocalSaveFolder { get; set; }` + **`Reset()`에서 `null`**(§4.5 필수) |
| `src/MCPhoto.App/AppShellViewModel.cs` | `ShowIdleWarning`에 링크 가시성 계산 + 오류 캡션 초기화 · `HideIdleWarning`에 캡션·가시성 초기화 · `IsResultFolderLinkVisible`·`ResultFolderOpenError`(+`HasResultFolderOpenError`)·`OpenResultFolderCommand` 추가 · `IFolderOpener` 주입(선택 인자 — 테스트 스텁 보호). ⚠️ **`CompleteSession`·`ReturnHome`·타이머·카운트다운·문구는 무변경** |
| `src/MCPhoto.App/MainWindow.xaml` | 유휴 오버레이에 `TextBlock` 2개 추가(§6.1). 나머지 마크업 불변 |
| `src/MCPhoto.App/App.xaml.cs` | 시작 시 Warning 2건(M6·M7). `DataFolder`·Serilog·`CleanupOnStartup` 불변 |
| `src/MCPhoto.Core/Settings/AppSettings.cs` | `EnableResultFolderOpen` + `Clone()` 1행 |
| `src/MCPhoto.Core/Settings/IniSettingsService.cs` | `ReadInto`·`WriteFrom`에 1행씩. **`ResolveDefaultPath`·`FallbackPaths`·`DefaultCandidates`·`CanWrite`는 한 줄도 바꾸지 않는다**(U6) |
| `src/MCPhoto.App/ViewModels/SettingsViewModel.cs` | `EnableResultFolderOpen`(Load/Save + 게스트 게이트) · `LocalSavePathEffectiveNote` |
| `src/MCPhoto.App/Views/SettingsView.xaml` | 경로 캡션 1줄(§6.2) · 옵션 행 + 캡션(§6.3) |
| `installer/MCPhoto.iss` | §3.8 |
| `src/MCPhoto.Core/Settings/SettingsPathResolver.cs` | **변경 없음**(명시적 non-goal) |
| `src/MCPhoto.App/Services/LogFolderService.cs` | **변경 없음**(명시적 non-goal) |
| `src/MCPhoto.Core/Navigation/IdleCountdown.cs` | **변경 없음** |

### 8.3 `AppShellViewModel` 신규 멤버 (최소)

| 멤버 | 종류 | 설명 |
|---|---|---|
| `IsResultFolderLinkVisible` | `[ObservableProperty] bool` | `ShowIdleWarning`에서 1회 계산 |
| `ResultFolderOpenError` | `[ObservableProperty] string` + `HasResultFolderOpenError` 파생 | M2. `Show`/`Hide` 양쪽에서 비운다 |
| `OpenResultFolderCommand` | `[RelayCommand]` | `_folderOpener?.TryOpen(_session.LocalSaveFolder!)` → 실패 시 M2 설정. **카운트다운 미간섭** |
| `FormatResultFolderOpenError(path)` | `static string` | 문구 단일 지점 |
| `_folderOpener` | `IFolderOpener?`(ctor 선택 인자) | 미주입(기존 테스트 다수)이면 링크 커맨드가 `false` 경로로 안전 동작 |

**이벤트 구독 없음** → 신규 해제 경로 없음. 타이머를 새로 만들지 않으므로 누수 표면이 늘지 않는다.

---

## §9 스레딩 모델

| 작업 | 스레드 | 규칙 |
|---|---|---|
| `ShowIdleWarning`/`HideIdleWarning`·링크 가시성 계산 | **UI 스레드** | `OnIdleTimeout`이 `_dispatcher.BeginInvoke(ShowIdleWarning)`로 마샬링한다(현행). 신규 `[ObservableProperty]`도 그 안에서만 변경된다 |
| `IFolderOpener.TryOpen` | UI 스레드(링크 클릭) | `Process.Start`는 즉시 반환한다. `WaitForExit` 금지 |
| `LocalSaveService.SaveAsync` | 호출부 컨텍스트(현 구현은 동기 + `Task.FromResult`) | 변경 없음 — 파일 복사가 UI 스레드에서 일어나는 성질은 **이번에 바꾸지 않는다**(별도 이슈, 손대면 검증 범위가 커진다) |
| `App.OnStartup`의 Warning 2건 | UI 스레드(시작) | `Directory.Exists` 1~2회 — 무시할 비용 |
| 프레임 legacy 루트 열거 | `Task.Run`(현행 `FrameCatalogService`가 이미 백그라운드로 감쌈) | 루트가 2개가 되어 I/O가 최대 2배지만, 구 폴더 부재 시 0이다 |

---

## §10 실패·부재 경로 전수표 (크래시 금지)

| # | 상황 | 거동 | 사용자에게 보이는 것 |
|---|---|---|---|
| E1 | `EnableResultFolderOpen=0`(기본) | 링크 미노출 | 종전과 **똑같은** 유휴 팝업 |
| E2 | `SaveLocalCopy=false` | 저장 시도 없음 → `LocalSaveFolder=null` → 링크 미노출 | 유휴 팝업(링크 없음) |
| E3 | 저장 실패(권한·디스크·잠금) | `SaveAsync`가 `null`(F4) → 링크 미노출 + 로그 | 유휴 팝업(링크 없음) |
| E4 | 저장 전 상태(FrameSelect~CutSelect·Result) | 경로 `null` → 링크 미노출 | 유휴 팝업(링크 없음) |
| E5 | 저장 후 폴더가 외부에서 삭제됨 | `TryOpen`이 `!Directory.Exists` → `false` | M2 캡션(경로 노출) |
| E6 | `explorer.exe` 실행 차단(잠금 셸·정책) | `Process.Start` 예외 → catch → `false` | M2 캡션 |
| E7 | `IFolderOpener` 미주입(단위 테스트) | 커맨드가 `false` 경로 → M2 캡션 | (테스트 컨텍스트) |
| E8 | 링크 클릭 후 카운트다운 만료 | 현행 그대로 홈 복귀. 탐색기 창은 남는다 | 앱은 홈, 탐색기는 그대로 |
| E9 | 팝업 표시 중 화면 전이(외부 요인) | `UpdateIdleWatch`가 `HideIdleWarning()` → 캡션·가시성 초기화(F26) | 팝업 사라짐 |
| E10 | 다음 세션에서 팝업 재표시 | `Reset()`이 `LocalSaveFolder=null`(§4.5) → 링크 미노출 | 이전 손님 폴더가 **절대 노출되지 않는다** |
| E11 | 구 루트(`{exe}\Frame`) 부재 | `Directory.Exists` false → 열거 건너뜀 | 변화 없음 |
| E12 | 구 루트에 잠긴/손상 `.slots` | 기존 `Enumerate`가 이미 건너뛴다 | 그 프레임만 미노출(현행과 동일) |
| E13 | 새 루트·구 루트에 **같은 이름** 프레임 | 새 루트 채택(§3.4.3) | 프레임 1개 |
| E14 | `%ProgramData%\MCPhoto\result` 생성 실패 | E3과 동일(조용한 미저장 + 로그) | 링크 없음 |
| E15 | `App.DataFolder` 자체 생성 실패 | `App.OnStartup`의 `CreateDirectory`가 이미 실패하는 기존 거동 — 이번 변경이 만드는 실패가 아니다 | 기존과 동일 |

---

## §11 테스트 전략 (전부 headless — `Window` 인스턴스화 금지)

### 11.1 순수 로직

| ID | 대상 | 검증 |
|---|---|---|
| T1 | `LocalSavePathResolver.Resolve` | 명시값 우선 · 공백/`null`/`"   "` → `{dataFolder}\result` · 명시값 Trim · **명시값을 절대 변형하지 않음**(A-3 회귀 잠금) |
| T2 | `ResultFolderLinkPolicy.ShouldShow` | 2조건 진리표 6행(`folder` null/빈문자/값 × 옵션 on/off) |
| T3 | `SettingsPathDiagnostics.IsUnderProgramFiles` | Program Files·(x86) 하위 true · ProgramData·LocalAppData·리포 경로 false · 대소문자 무시 · 후행 슬래시 무관 · **접두가 빈 문자열이면 모든 경로를 true로 만들지 않음**(빈 접두 함정) |
| T4 | `IdleCountdown` | 기존 테스트 유지(무손상 확인) |

### 11.2 셸 동작 (신규 `AppShellIdleFolderLinkTests`)

⚠️ 기존 `AppShellSessionCompleteTests` 5건은 **전부 그대로 통과해야 한다**(`CompleteSession` 무변경).

| ID | 검증 |
|---|---|
| T5 | 옵션 on + `session.LocalSaveFolder` 설정 → `ShowIdleWarning` 경로 후 `IsResultFolderLinkVisible==true` |
| T6 | 옵션 off(**기본값**) → 같은 조건에서 `IsResultFolderLinkVisible==false` |
| T7 | `LocalSaveFolder==null`(저장 전·`SaveLocalCopy=false`·저장 실패) → 링크 미노출 |
| T8 | `OpenResultFolderCommand` 실행 시 fake `IFolderOpener`가 **정확히 그 세션 폴더 경로**를 받는다(§5.1 — 루트가 아니다) |
| T9 | `SessionContext.Reset()` 후 `LocalSaveFolder==null` → 다음 팝업에 링크 미노출(**이전 손님 폴더 노출 금지** 회귀) |
| T10 | fake opener가 `false` 반환 → `HasResultFolderOpenError==true` · 문구 M2 · **팝업 유지** |
| T11 | `HideIdleWarning` 후 `ResultFolderOpenError`가 비고 `IsResultFolderLinkVisible==false` → **다음 팝업에 stale 오류·링크가 남지 않는다** |
| T12 | `OpenResultFolderCommand` 실행 전후로 `IdleCountdownRemaining`이 **변하지 않는다**(B-3 판정 잠금 — 없으면 나중에 "친절한" 연장 코드가 들어온다) |
| T13 | `IdleWarningSeconds==120` · `IdleCountdownSeconds==10` 단정(상수 무변경 잠금) |
| T14 | `SessionStateMachine.IsSessionActive(AppState.Home)==false` 단정 — §4.2 판정("홈에서는 팝업이 뜨지 않는다")의 근거를 코드로 고정 |
| T15 | `AppStateTests.Done_State_Is_Retired` 계속 통과 |

### 11.3 서비스·저장소

| ID | 검증 |
|---|---|
| T16 | `FolderOpener.TryOpen`이 예외를 밖으로 내보내지 않는다(부재 경로 · 빈 문자열 · 잘못된 문자) — `opener` 주입으로 실제 explorer 미실행(F31 관례) |
| T17 | `FolderOpener`가 **폴더를 생성하지 않는다**(부재 경로 호출 후 `Directory.Exists==false` 유지) |
| T18 | `LocalFrameStore` legacy 읽기: ① 구 루트만 있는 프레임이 `LoadPublic`에 나온다 ② 같은 이름이면 새 루트가 이긴다 ③ `SaveDefaultFrame`·`SaveUserFrame`이 **구 루트에 파일을 만들지 않는다** ④ legacy가 `null`·부재일 때 현행과 동일 |
| T19 | `LocalFrameStore` 개인 프레임 legacy: 구 루트 `users\{hash}\`가 `LoadUser`에 병합된다 |
| T20 | ini 라운드트립: `EnableResultFolderOpen` **기본 false** · `1` 기록·복원 · **키 부재 시 false**(마이그레이션 불요 증명) |
| T21 | 게스트 저장 게이트: `IsGuest`로 `SaveSettings` 후 ini 원값(`1`)이 **보존**된다 |
| T22 | `ResultViewModel`이 저장 성공 시 `session.LocalSaveFolder`에 절대경로를 담고, 실패 시 `null`을 담는다(fake `ILocalSaveService`) |
| T23 | ini 경로 정책 회귀: `SettingsPathResolver.DefaultCandidates`의 **1순위가 실행경로**임을 고정(U6 — 정책 변경이 사고로 들어오지 못하게 잠근다) |

### 11.4 XAML (소스 스캔 — F32 방식)

| ID | 검증 |
|---|---|
| T24 | `MainWindow_StaticResource_Keys_Resolve_In_Theme` **계속 통과**(신규 2줄이 참조하는 키 전부 테마 해석) |
| T25 | `MainWindow.xaml`이 `IsResultFolderLinkVisible`·`OpenResultFolderCommand`·`ResultFolderOpenError`·`HasResultFolderOpenError`를 바인딩한다(정규식) — 바인딩 오타는 조용히 빈 칸이 된다 |
| T26 | 유휴 오버레이의 **기존 규격 불변**: `Grid.Row="1"` · `Brush.Scrim` · `MinWidth="420"` · `ContinueSessionCommand`·`GoHomeFromIdleCommand` 존재 · 문구 `잠시 자리를 비우셨나요?`·`초 후 메인 화면으로 돌아갑니다` 존재 |
| T27 | 링크가 `Hyperlink` + `Foreground="{StaticResource Brush.Accent.Text}"`이고 `TextDecorations`·hover 트리거를 **부착하지 않았다**(F30 규약 잠금) |
| T28 | 토스트 마크업(`HasToast`·`DismissToastCommand`)이 **여전히 존재**한다(F29 — 강등 토스트 소비자 보호) |
| T29 | `SettingsView.xaml`이 `LocalSavePathEffectiveNote`·`EnableResultFolderOpen`을 바인딩한다 |
| T30 | `SettingsView_StaticResource_Keys_Resolve_In_Theme` 계속 통과 |

### 11.5 인스톨러 (정적 검증)

| ID | 검증 |
|---|---|
| T31 | `[Dirs]`에 `{commonappdata}\MCPhoto\result`·`{commonappdata}\MCPhoto\Frame`이 있고 `users-modify`다 |
| T32 | `[UninstallDelete]`에 `{app}\result`·`{commonappdata}\MCPhoto\result` **삭제 행이 없다**(손님 자산 보호 잠금) |

> ⚠️ T32는 **부재 검증**이라 오탐이 쉽다. `Type: ...; Name: "..."` 형태의 **완전한 행 패턴**만 매칭하고 주석 줄(`;` 시작)은 제외한다 — 현행 `:102-105`가 주석에 그 경로 문자열을 포함한다.

---

## §12 문서 갱신 지점

| # | 문서 | 갱신 |
|---|---|---|
| 12-1 | `docs/analysis/11-exe-app-features.md` | 유휴 팝업 설명에 [결과물 폴더 열기] 링크 추가(옵션 기본 off · 세션 폴더만 · `Qr` 상태가 실사용 창구). **완료 흐름 서술은 변경 없음** |
| 12-2 | `docs/analysis/12-exe-app-settings-and-config.md` | `EnableResultFolderOpen` 행 추가(기본 0 · 위험 고지) · `LocalSavePath` 빈 값 기본 경로를 `%ProgramData%\MCPhoto\result`로 정정 · ini 위치 정책은 **불변**임을 명시 |
| 12-3 | `docs/analysis/41-local-data-and-file-formats.md` | `:305`(로컬 저장 위치) · `:382`(플랫폼 비교표 Windows 열) 정정 · 프레임 캐시(`%ProgramData%\MCPhoto\Frame`)와 번들(`{exe}\Frame`) **분리** 반영 · §3.9 경로 지도 반영 |
| 12-4 | `docs/analysis/80-build-and-deployment.md` | 인스톨러 `[Dirs]`·`[UninstallDelete]` 갱신 반영 · "앱은 설치 폴더에 쓰지 않는다"를 규약으로 명문화(예외: 운영자가 배치한 `{exe}\Frame`·`branding.ini`는 읽기 전용) |
| 12-5 | `docs/analysis/70-logging-and-troubleshooting.md` | `:80`(기본 위치) 정정 + 신규 Warning 2건(M6·M7)을 진단 단서로 등재 |
| 12-6 | `docs/analysis/13-client-behavior-spec.md` | 모달 규격 절의 유휴 팝업 항목에 링크 1줄 추가(가시성 2조건) |
| 12-7 | `docs/design/README.md` | §3.2 Windows 표에 이 문서 등재 |

⚠️ rev1이 예정했던 `00-overview-and-architecture.md` 갱신은 **불필요해졌다** — `CompleteSession`을 바꾸지 않으므로 "완료는 화면이 아니라 토스트"라는 서술이 계속 참이다.

---

## §13 리스크

| # | 리스크 | 영향 | 완화 |
|---|---|---|---|
| R1 | `%ProgramData%` 하위 쓰기 권한이 실제로 없는 환경 | 손님 사진 미저장(조용히) | U1·U2를 Step 2·3에서 실측 · 인스톨러 `[Dirs]` 명시 · 실패는 이미 로그에 남는다(`LocalSaveService.cs:62`) |
| R2 | 기본 off라 운영자가 기능의 존재를 모른다 | 요구가 체감되지 않는다 | 설정 행 라벨이 "유휴 팝업에"를 명시(§6.3) · 릴리스 노트·`docs/analysis/12`에 등재 · **후속 후보**(상시 접근 경로)를 §14에 기록 |
| R3 | 옵션 on 부스에서 손님이 탐색기 접근 | 보안 | 기본 off · 세션 폴더만 · 노출 창 10초 · 캡션이 위험 고지 · **잔여 위험 문서화**(§5.2) |
| R4 | 다른 손님 폴더를 여는 사고 | 개인정보 | 반환값 사용 강제(§5.1) + `Reset()` `null` 초기화 + T8·T9 |
| R5 | 프레임 legacy 읽기가 중복 표시를 만든다 | 목록 혼란 | 이름 dedup(새 루트 우선) + T18 ② |
| R6 | ini 정책을 "정리하려고" 나중에 바꾼다 | 기존 설치 설정 유실 | T23이 1순위를 잠근다 + §3.5 근거를 문서에 남긴다 |
| R7 | 유휴 팝업 카드가 링크 2줄로 커져 레이아웃이 깨진다 | 표시 불량 | 기본 off라 대부분 종전 그대로 · 오류 캡션 `MaxWidth=380` · T26이 기존 규격을 잠근다 |
| R8 | `Qr` 상태에서 팝업이 실제로 안 뜬다 | **기능 도달 불가** | U7을 Step 6에서 실측(이 기능의 유일한 창구) |
| R9 | 링크 클릭이 유휴 타이머를 리셋하는 코드가 생긴다 | 무인 부스 정지 | T12가 잠근다 |

---

## §14 열린 질문 · 사용자 확인 사항 · 후속 후보

| # | 항목 | 기본값으로 진행 |
|---|---|---|
| **UD-1** | `EnableResultFolderOpen` 기본값 | ⭕ **`0`(off)로 진행**(§5.2). 팀리드 권고와 일치. on으로 바꾸려면 `AppSettings` 기본값 1곳 + T20 1곳만 수정 |
| **UD-2** | 키 이름 | ⭕ `EnableResultFolderOpen`(대안 `ShowResultFolderLink` 기각 — §7.1) |
| **UD-3** | 게스트가 이 옵션을 편집할 수 있게 할 것인가 | ❌ **불가로 진행**(Load 강제 off + Save 미기록). 링크에 로그인 게이트가 없으므로 이 키가 유일한 방어선이다 |
| **UD-4** | 구 `{app}\result`를 **앱이** 새 위치로 옮겨 줄 것인가 | ❌ 옮기지 않는다(§3.6). 필요하면 운영자가 수동 복사한다 — 앱이 손님 사진을 옮기는 코드를 갖지 않는 것이 유실 0의 근거다 |
| **UA-1** | 실측 확인 요청: 사내 배포 설치본 중 `{app}\result`·`{app}\Frame`에 **실제 데이터가 있는 PC가 있는가**(U3) | 있다고 가정해 legacy 읽기 폴백을 넣는다. 없다면 비용 0의 죽은 코드로 남는다 |
| **UA-2** | 실측 확인 요청: 비승격 실행에서 `%ProgramData%\MCPhoto\result`·`\Frame` 쓰기 성공(U1·U2) | Step 2·3의 완료 기준 |
| **UA-3** | 실측 확인 요청: `Qr` 화면 2분 방치 시 유휴 팝업 + 링크 노출(U7) | Step 6의 완료 기준 |

**후속 후보(이번 범위 아님 · 사용자 승인 없음, 기록만 한다)**

> 유휴 팝업은 발견 경로로 실용성이 낮다 — 소유자가 저장 폴더를 열려면 QR 화면에서 2분을 무동작으로 기다려야 한다(§4.3). 상시 접근 경로가 필요하다면 **설정 화면의 로컬 저장 그룹에 [저장 폴더 열기] 링크**를 두는 것이 자연스럽다(진단 모달의 [로그 폴더 열기]와 동형이며, 설정 화면은 게스트도 들어올 수 있으므로 그때는 **저장 루트가 아니라 아무 것도 열지 않거나 로그인 게이트가 필요**하다 — 루트를 열면 §5.1의 개인정보 판정을 정면으로 위반한다). 별도 이터레이션에서 판정할 사안이다.

---

## §15 WBS — 구현 단계

> 공통 검증 명령: `build-verify` 스킬(없으면 `dotnet build MCPhoto.sln` + `dotnet test tests/MCPhoto.Tests`).
> 순서·병렬성: **Step 1 독립** → Step 2·Step 3·Step 5는 **서로 병렬 가능** → Step 4 → Step 6 → Step 7.
> ⚠️ 전 단계 공통 non-goal: **`SettingsPathResolver.cs`와 `IniSettingsService.cs`의 경로 결정 코드(`ResolveDefaultPath`·`FallbackPaths`·`DefaultCandidates`·`CanWrite`)에 한 줄도 손대지 않는다**(U6·R6). **`AppShellViewModel.CompleteSession`·`ReturnHome`·유휴 타이머·유휴 문구·`IdleWarningSeconds`·`IdleCountdownSeconds`도 손대지 않는다**(B-1·B-2). `.cs`는 UTF-8 **BOM 없음**(F33).

### Step 1: 순수 정책 3종 신설

- **Context Brief**: MCPhoto(WPF, .NET10)는 촬영 결과물을 `{LocalSavePath}\mcphoto_yyMMdd_HHmm\`에 저장한다. 기본 경로가 호출부에 인라인돼 있어(`ResultViewModel.cs:141-143`: 공백이면 `AppContext.BaseDirectory\result`) 소비자가 늘면 규칙이 갈린다. 이 단계는 **로직만** 만든다 — 배선은 다음 단계다.
- **대상 파일**: `src/MCPhoto.Core/LocalSave/LocalSavePathResolver.cs`(신규) · `src/MCPhoto.Core/LocalSave/ResultFolderLinkPolicy.cs`(신규) · `src/MCPhoto.Core/Settings/SettingsPathDiagnostics.cs`(신규) · 신규 테스트
- **선행 조건**: 없음
- **구현 내용**: §3.7 · §4.6 · §3.5의 세 함수 + 테스트 T1·T2·T3. `SettingsPathDiagnostics`는 접두 비교 시 `Path.GetFullPath` 정규화 + 후행 구분자 정리 + **빈 접두 방어**.
- **검증 명령**: build-verify
- **완료 기준**: [관측] T1~T3 통과 + 기존 테스트 무손상 / [non-goal] 기존 파일 diff 0(신규 파일만) · `SettingsPathResolver.cs` diff 0 / [trigger] 없음(순수 함수 — I/O·로그 없음)
- **롤백**: 커밋 revert(소비자 0)
- [ ] 완료

### Step 2: `result` 기본 경로 이관 + 세션 폴더 캡처

- **Context Brief**: 설치 배포(`C:\Program Files\MCPhoto\`)에서 손님 사진이 설치 폴더에 쌓이고, 비승격 실행에서는 **조용히 저장 실패**한다(`LocalSaveService`가 예외 대신 `null` 반환). 기본 경로를 `%ProgramData%\MCPhoto\result`(=`App.DataFolder\result`)로 옮긴다. `LocalSavePath` 명시값이 있으면 **그것이 항상 우선**이며 한 글자도 건드리지 않는다. 또 `SaveAsync`의 반환값(실제 세션 폴더 절대경로)이 지금 버려지는데 유휴 팝업 링크가 그 경로를 쓴다. ⚠️ 폴더명을 시각으로 **재계산하면 안 된다** — 같은 분에 두 세션이면 실제 폴더에 `-2` 접미가 붙어(`LocalSaveService.cs:38-40`) **다른 손님 폴더**를 가리킨다.
- **대상 파일**: `src/MCPhoto.App/ViewModels/ResultViewModel.cs` · `src/MCPhoto.App/SessionContext.cs` · `src/MCPhoto.App/App.xaml.cs`(M7) · 테스트
- **선행 조건**: Step 1
- **구현 내용**: `LocalSavePathResolver.Resolve(settings.LocalSavePath, App.DataFolder)` 사용 · `session.LocalSaveFolder = await _localSave.SaveAsync(...)` · `SessionContext.LocalSaveFolder` 추가 + **`Reset()`에서 `null`** · `App.OnStartup`에서 `{exe}\result` 존재 시 Warning M7(`try/catch`로 감싼다). 테스트 T22 + `Reset()` 초기화 테스트.
- **검증 명령**: build-verify + **실행 관측(U1·UA-2)**: 설치본을 **비승격**으로 실행 → 촬영 1회 완료 → `%ProgramData%\MCPhoto\result\mcphoto_*\final.*` 존재 확인
- **완료 기준**: [관측] 비승격 실행에서 `%ProgramData%\MCPhoto\result\mcphoto_*\`에 `final.*` 생성 + 신규·기존 테스트 통과 / [non-goal] `LocalSaveService.cs` diff 0(폴더명 규약·유니크 접미·예외 삼킴 불변) · `AppSettings.LocalSavePath` 기본값·Clamp diff 0 · `LocalSavePath`에 값이 있는 ini로 실행하면 저장 위치가 **그 값 그대로** / [trigger] 저장은 `SaveLocalCopy=true`일 때만 시도된다(off면 `LocalSaveFolder`가 `null`로 남고 폴더가 생기지 않음)
- **롤백**: 커밋 revert. 이미 새 위치에 저장된 사진은 남고 revert 후에는 구 위치를 쓴다(**어느 쪽도 삭제하지 않으므로 유실 없음**)
- [ ] 완료

### Step 3: 프레임 캐시 루트 이관 + 구 루트 읽기 폴백

- **Context Brief**: `LocalFrameStore`(서버에서 내려받은 공용·개인 프레임 캐시)의 루트가 `{exe}\Frame`이라(`ServiceRegistration.cs:140`) 비승격 실행에서 캐시 기록이 실패하고, 실패한 id는 이번 실행 동안 목록에서 배제된다(`FrameCatalogService`의 `_cacheFailedIds`) → **기본 프레임이 안 보일 수 있다.** 캐시 루트를 `%ProgramData%\MCPhoto\Frame`으로 옮기고 `{exe}\Frame`은 `FrameCatalogService.BundleFolder`(운영자가 배치하는 읽기 전용 번들)로 **그대로 남긴다**. 두 경로의 대상 파일 집합은 겹치지 않는다 — `LocalFrameStore`는 서명된 `.slots`가 있는 png만 인정하고(`LocalFrameStore.cs:146-175`) 번들 스캔은 폴더의 모든 이미지를 집는다. `.slots` 서명은 파일 경로를 포함하지 않으므로 폴더가 달라져도 유효하다.
- **대상 파일**: `src/MCPhoto.Core/Frames/LocalFrameStore.cs` · `src/MCPhoto.App/ServiceRegistration.cs` · 테스트
- **선행 조건**: 없음(Step 1·2와 병렬 가능)
- **구현 내용**: §3.4.3 규칙 그대로 — ctor `legacyReadRoot` 추가, 읽기 5메서드가 두 루트 순회 + 이름(대소문자 무시) dedup(새 루트 우선), 쓰기·`UserFolder`는 `_root`만, `DeleteLocal` 무변경. DI: `new LocalFrameStore(Path.Combine(App.DataFolder, "Frame"), legacyReadRoot: Path.Combine(AppContext.BaseDirectory, "Frame"))`. 테스트 T18·T19.
- **검증 명령**: build-verify + **실행 관측(U2)**: 설치본 **비승격** 실행 → 프레임 선택 화면 진입 → `%ProgramData%\MCPhoto\Frame\`에 png/.slots 생성 확인
- **완료 기준**: [관측] 비승격 실행에서 캐시 파일이 `%ProgramData%\MCPhoto\Frame\`에 생성되고 프레임 목록에 표시됨 + T18·T19 통과 + 기존 프레임 테스트 무손상 / [non-goal] `FrameCatalogService.cs` diff 0(`BundleFolder`·단일 비행·`_cacheFailedIds`·우선순위 규약 불변) · `SlotsFileCodec`·`FrameOwnership` diff 0 · 쓰기 경로가 `{exe}\Frame`에 파일을 만들지 않음(T18 ③) / [trigger] legacy 열거는 폴더가 **존재할 때만** 수행(부재 환경에서 I/O 0)
- **롤백**: 커밋 revert(구 루트를 읽기로만 썼으므로 파일 상태 변화 없음)
- [ ] 완료

### Step 4: `EnableResultFolderOpen` 설정 키 + 설정 화면 2행

- **Context Brief**: 유휴 팝업의 폴더 열기 링크는 잠금 키오스크에서 탐색기(=파일시스템 통로)를 여는 표면이고, **그 팝업은 손님 앞에서 무인으로 뜬다.** 그래서 노출은 ini 키 하나가 통제하며 **기본값은 off**다. 링크에는 로그인 게이트가 없으므로 이 키가 유일한 방어선이고, 게스트도 PIN 없이 설정에 들어올 수 있으므로(`AppShellViewModel.cs:559-566`) **게스트 편집 게이트가 필수**다(Load 시 표시값 강제 off, Save 시 `!IsGuest` 가드 — it12 R1 규약).
- **대상 파일**: `src/MCPhoto.Core/Settings/AppSettings.cs` · `src/MCPhoto.Core/Settings/IniSettingsService.cs`(각 1행) · `src/MCPhoto.App/ViewModels/SettingsViewModel.cs` · `src/MCPhoto.App/Views/SettingsView.xaml` · 테스트
- **선행 조건**: Step 1(캡션이 `LocalSavePathResolver`를 쓴다)
- **구현 내용**: §7.1 · §6.2 · §6.3. `AppSettings.EnableResultFolderOpen = false` + `Clone()` 1행 · ini `GetBool`/`SetBool` 1행씩 · VM 프로퍼티 + 게스트 2지점 + `LocalSavePathEffectiveNote`(`[NotifyPropertyChangedFor]`) · XAML 캡션 1줄 + 옵션 행 + 캡션 M5. **신규 리소스 키 0**(기존 `SettingRow`·`RowLabel`·`Toggle`·`FullRow`·`Text.Caption`·`Brush.Text.Muted`만). 테스트 T20·T21·T23·T29·T30.
- **검증 명령**: build-verify
- **완료 기준**: [관측] T20(키 부재 시 false·`1` 라운드트립)·T21(게스트 미기록)·T23(ini 1순위 잠금)·T29·T30 통과 + 기존 설정 테스트 무손상 / [non-goal] `SettingsPathResolver`·`ResolveDefaultPath` diff 0 · 다른 ini 키의 Read/Write 순서·값 diff 0 · `grep 'x:Key' src/MCPhoto.App/Themes` diff 0 / [trigger] 게스트 세션에서 설정을 저장해도 ini의 `EnableResultFolderOpen` 원값이 보존된다
- **롤백**: 커밋 revert(키가 없으면 기본 false → 링크는 어차피 미노출이므로 다음 단계와 독립)
- [ ] 완료

### Step 5: `IFolderOpener` 신설

- **Context Brief**: VM이 `Process.Start`를 직접 만지지 않는 것이 이 리포의 경계 규약이다(`IClipboardService`·`ILogFolderService` 주석). 폴더 열기는 **best-effort**여야 한다 — 잠금 키오스크(셸 교체·정책)에서 `explorer.exe`가 차단될 수 있고 그때 크래시는 금지다(`LogFolderService.cs:27-39`가 같은 관례). 기존 `ILogFolderService`는 **건드리지 않는다**.
- **대상 파일**: `src/MCPhoto.App/Services/IFolderOpener.cs`(신규) · `src/MCPhoto.App/Services/FolderOpener.cs`(신규) · `src/MCPhoto.App/ServiceRegistration.cs`(등록 1행) · 테스트
- **선행 조건**: 없음
- **구현 내용**: §5.3의 계약 그대로. `opener`(`Action<string>?`) 주입 이음새 필수. **`Directory.CreateDirectory` 호출 금지.** 테스트 T16·T17.
- **검증 명령**: build-verify
- **완료 기준**: [관측] T16(예외 미전파)·T17(폴더 미생성) 통과 / [non-goal] `LogFolderService.cs`·`ILogFolderService.cs`·`DiagnosticsViewModel` diff 0 / [trigger] `TryOpen`은 호출될 때만 프로세스를 만든다(생성자·DI 해석 시 부작용 0)
- **롤백**: 커밋 revert(소비자 0)
- [ ] 완료

### Step 6: 유휴 팝업에 링크 배선 + XAML 2줄

- **Context Brief**: 대상은 **이미 존재하는** 유휴 경고 오버레이다(`MainWindow.xaml:173-196`, `IsIdleWarningVisible`). 거동은 **전부 현행 유지**한다 — 120초·10초 상수, 두 버튼, 문구, `DispatcherTimer` 구조를 건드리지 않는다. 추가하는 것은 ① `ShowIdleWarning`에서 링크 가시성을 1회 계산 ② 링크 커맨드가 `IFolderOpener`로 **그 세션 폴더**를 연다 ③ 실패 시 캡션 ④ `HideIdleWarning`에서 상태 초기화. ⚠️ **링크 클릭은 카운트다운에 어떤 영향도 주지 않는다**(사용자 지시). `NotifyUserActivity`는 경고 표시 중 무시되므로(`AppShellViewModel.cs:492-496`) 추가 조치가 필요 없다 — 리셋 코드를 새로 넣지 마라. ⚠️ 링크가 실제로 보이는 상태는 사실상 `Qr` 하나다(저장 → 업로드 → QR 순서이므로) — 스모크는 그 흐름으로 한다.
- **대상 파일**: `src/MCPhoto.App/AppShellViewModel.cs` · `src/MCPhoto.App/MainWindow.xaml` · 신규 테스트 + `XamlResourceTests.cs`
- **선행 조건**: Step 2(세션 폴더) · Step 4(설정 키) · Step 5(`IFolderOpener`)
- **구현 내용**: §8.3 멤버 + §6.1 XAML 2줄. 테스트 T5~T15·T24~T28.
- **검증 명령**: build-verify + **앱 기동 스모크**: ① 옵션 on + `SaveLocalCopy=on` → 촬영 완료해 QR 화면 도달 → **2분 방치** → 유휴 팝업에 링크 노출(U7·UA-3) → 클릭 → **그 세션 폴더**가 열림 ② 링크 클릭 후 **10초 방치** → 앱은 홈으로 가고 **탐색기 창은 그대로 앞에 남아 있음**(U4·U5) ③ 옵션 off(기본) → 같은 흐름에서 링크 없음(팝업이 종전과 동일) ④ 프레임 선택 화면에서 2분 방치 → 링크 없음(저장 전) ⑤ [이어서 진행하기] → 팝업 닫히고 세션 유지
- **완료 기준**: [관측] T5~T15·T24~T28 통과 + 스모크 5항 육안 확인(특히 ①이 U7을, ②가 §4.4 판정을 실증한다) / [non-goal] `CompleteSession`·`ReturnHome`·`ShowToast`·`OnIdleCountdownTick`·`ContinueSession`·`GoHomeFromIdle` diff 0 · `IdleWarningSeconds`·`IdleCountdownSeconds` 값 diff 0 · 유휴 팝업 기존 문구·버튼·`Grid.Row`·`MinWidth` diff 0 · `grep 'x:Key' src/MCPhoto.App/Themes` diff 0 · 새 `AppState`·새 오버레이 0 / [trigger] 링크는 "옵션 on **AND** 세션 저장 경로 존재"일 때만 보인다 · 링크 클릭이 `IdleCountdownRemaining`을 바꾸지 않는다(T12) · `Reset()` 이후 팝업에는 링크가 없다(T9)
- **롤백**: 커밋 revert(설정 키는 남지만 기본 off이므로 무해)
- [ ] 완료

### Step 7: 인스톨러 + 문서 갱신

- **Context Brief**: 새 쓰기 위치(`%ProgramData%\MCPhoto\{result,Frame}`)를 인스톨러가 `users-modify`로 만들어 두면 비승격 첫 실행이 폴더 생성부터 실패하는 경로가 없다. ⛔ `{app}\result`와 `{commonappdata}\MCPhoto\result`는 **제거 시 삭제 대상에 넣지 않는다** — 손님 사진이다. 현행 `[UninstallDelete]`(`:93-117`)에 그 규약과 근거 주석이 이미 있으니 새 위치로 확장한다.
- **대상 파일**: `installer/MCPhoto.iss` · `docs/analysis/{11,12,13,41,70,80}-*.md` · `docs/design/README.md` · 테스트(T31·T32)
- **선행 조건**: Step 2·3(실제 경로 확정 후)
- **구현 내용**: §3.8 + §12 전부. T31·T32 추가(⚠️ T32는 부재 검증 — 주석 줄 제외 + 완전한 행 패턴만 매칭).
- **검증 명령**: `iscc installer\MCPhoto.iss`(publish 선행) 컴파일 성공 + build-verify(T31·T32) + 문서 diff 육안
- **완료 기준**: [관측] 인스톨러 컴파일 성공 · T31·T32 통과 · §12의 7개 문서에 it26 표기 / [non-goal] `[Files]` 화이트리스트 diff 0 · `AppId`·`AppVersion` 산출 방식 diff 0 · `00-overview-and-architecture.md` 무수정(완료 흐름 불변) · it23·it24·it25 설계 문서 무수정 / [trigger] 제거 실행 시 `result` 폴더가 남는다(가상머신 실측 권장, 최소한 `.iss` 정적 검증으로 고정)
- **롤백**: 커밋 revert
- [ ] 완료

### 완결성 게이트 확인

| 항목 | 상태 |
|---|---|
| 모든 가정(U1~U7)이 검증 단계에 매핑됐다 | ⭕ U1→Step 2 · U2→Step 3 · U3→Step 3(설계로 무해화) + UA-1 · U4·U5→Step 6 스모크 ② · U6→Step 1·4 non-goal + T23 · U7→Step 6 스모크 ① |
| 각 단계가 독립 검증 가능하다 | ⭕ Step 6은 2·4·5 산출물을 요구하지만 검증(테스트+스모크)은 그 단계만으로 PASS/FAIL이 결정된다 |
| 단일 리스크 | ⭕ 경로 이관(2·3)·설정(4)·서비스(5)·UI 배선(6)을 분리했다 |
| 완료 기준이 관측 3문 형식이다 | ⭕ UI 단계(4·6)에 non-goal·trigger 명시 |
| 빈 필드 없음 | ⭕ |
