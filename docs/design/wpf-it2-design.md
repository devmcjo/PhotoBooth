# MC포토 — 이터레이션 2 설계 (UI 재디자인 + 설정/로그인/관리자 UI)

| 항목 | 값 |
|------|-----|
| 문서 | WPF 이터레이션 2 설계 본문 |
| 작성일 | 2026-07-20 |
| 상태 | 초안 v2 (색상 방향 Direction A 라이트 확정 반영, 구현 착수 전) |
| 1차 준거 | `docs/prd/iteration-2-ui-and-settings.md` |
| 상위 준거 | `docs/prd/photobooth-prd.md` v2.7 §9(확정 결정 38개, 위반 금지) |
| 기존 설계 | `docs/design/wpf-architecture.md`, `docs/design/wpf-wbs.md` |
| 구현 WBS | `docs/design/wpf-it2-wbs.md`(본 문서의 구현 계획) |
| 코드 베이스 | `E:\Study\photobooth\src\` (MVP 구현·리뷰 PASS 완료 상태) |

> **⚠️ 색상 방향: Direction A(라이트) 확정 — 사용자 선택, 2026-07-20.** 초안 v1의 "기존 다크(#141018)+마젠타 톤 계승" 결정은 **폐기**한다. 앱은 **밝은 화이트 배경 + 로즈/민트 파스텔("코튼 캔디")** 라이트 테마로 재설계한다. 아래 §2(디자인 시스템)·부록 A(치환 매핑)는 이 라이트 팔레트로 개정됐다. 다크 전제로 내렸던 결정(어두운 배경 위 밝은 텍스트, 밝은 서피스로 깊이 표현, glassmorphism/네온 장식 등)은 §2에서 전부 라이트 기준으로 뒤집어 재정립했다.

> 이 문서는 MVP를 **대체하지 않고 완성도를 끌어올린다.** PRD §9의 확정 결정 38개와 아키텍처 문서의 캡처 파이프라인·Firebase 전략·상태머신 골격은 유효하며, 본 문서는 그 위에서 (1) 디자인 시스템 신설, (2) 상단 바 네비게이션, (3) 설정 페이지 신설, (4) 촬영 진입 흐름 변경 4가지를 설계한다.

---

## 0. 검증된 사실 / 미검증 가정

### 검증된 사실 (코드 인스펙션으로 직접 확인)

- **VF-1. MVP는 15 화면·12 VM으로 이미 구현됨**: `src/MCPhoto.App/Views/`에 XAML 14개(HomeView, LoginGuestView, FrameSelectView, GuideView, CaptureView, CutSelectView, ResultView, QrPopupView, DoneView, FrameEditorView, AdminView, UserMgmtView, PreviewView + MainWindow), 대응 ViewModel이 `ViewModels/`와 루트(`AppShellViewModel`)에 존재. 상태머신(`SessionStateMachine`)·DI(`ServiceRegistration`)·전역 예외 핸들러(`App.xaml.cs`)까지 동작한다. (근거: 파일 직접 열람)
- **VF-2. 스타일/템플릿 리소스가 전무하다**: `App.xaml`의 `Application.Resources`에는 컨버터 6개와 VM→View DataTemplate 12개만 있고, 색·타이포·컨트롤 스타일 리소스가 **하나도 없다**. 모든 화면이 `#141018`, `#C44B9B`, `#F5F0FA` 등 **색상 리터럴을 XAML마다 반복 하드코딩**하고 `Button`에 `Background/Foreground/Padding/FontSize`를 인라인 지정한다. (근거: `App.xaml`, 전 View XAML 열람)
- **VF-3. 현재(MVP) 팔레트는 다크 계열로 일관되지만 폐기 대상이다**: 기존은 배경 `#141018`(잉크 다크), 서피스 `#241E30`/`#332B3E`, 텍스트 `#F5F0FA`/`#E8DEF2`/`#B9A7D0`/`#8574A0`, 강조 `#C44B9B`(마젠타). 화면마다 같은 값이 반복될 뿐 색 체계는 어긋나지 않았다. 초안 v1은 이 값을 "리소스로 승격만" 하려 했으나, **색상 방향이 Direction A(라이트)로 확정되면서 이 다크 팔레트는 전면 교체된다.** 리터럴이 화면마다 하드코딩돼 있다는 사실(VF-2)은 여전히 유효 — 라이트 토큰으로의 일괄 치환 대상이라는 뜻이다. (근거: 전 View XAML의 색 리터럴 대조)
- **VF-4. 벤치마크·프레임은 다크 배경이나, 앱 UI 테마와는 분리 가능**: `Example/result_frame2.jpg`·`Example/Frame.png`는 검정 배경 + 핑크/러블리 하트 장식 + 성경구절 세로 3:4 프레임이다. **결과물(프레임 이미지)의 배경색과 앱 UI 테마는 독립**이다 — 프레임은 사용자가 업로드/선택하는 콘텐츠이고, 앱 UI는 그 콘텐츠를 담는 라이트 껍데기다. 라이트 UI(화이트+로즈/민트) 위에 다크 프레임 썸네일을 얹어도 조화된다(로즈 강조색이 프레임의 핑크 하트 장식과 톤이 이어짐). (근거: 이미지 직접 열람 + Direction A 확정)
- **VF-5. 상단 바가 없다**: `MainWindow.xaml`은 `ContentControl` 1개 + 좌상단 80×80 투명 히트영역(`AdminCorner`, 3초 롱프레스 → `AppState.Admin`)만 둔다. 로그인 버튼·설정 버튼이 **존재하지 않는다.** (근거: `MainWindow.xaml`, `MainWindow.xaml.cs`)
- **VF-6. 설정 편집 UI는 관리자 모드 안에만 있다**: `AdminView`가 `AppSettings` 대부분을 편집하나, 진입 경로가 "좌상단 3초 롱프레스 + 로그인"뿐이다. **게스트·일반 사용자가 설정을 여는 경로가 없다.** 요구 문서의 "설정 수정 UI 전무" 결함의 실체는 "설정 화면이 없다"가 아니라 "**접근 불가·게스트 배제**"다. (근거: `AdminView.xaml`, `AdminViewModel.OnEnterAsync`)
- **VF-7. 계정 생성 UI가 전혀 없다**: `IAccountService.CreateAsync`(역할 인자 포함)와 `AccountService.CreateAsync` 구현은 존재하나, 이를 **호출하는 View/VM이 하나도 없다**(`grep` 결과 참조 없음). 요구 2.3(사용자 계정 생성)은 **완전 미구현**이다. (근거: `AccountService.cs`, VM 전수)
- **VF-8. 계정 생성에 역할 권한 규칙이 강제되지 않는다**: `AccountService.CreateAsync(id, pw, role)`는 호출자가 넘긴 role을 그대로 저장한다. "manager/admin만 user 생성, admin만 manager 생성"(PRD §F8, 요구 2.3) 규칙을 **서비스가 강제하지 않는다** — 호출자(현재 없음) 책임으로 남아 있다. (근거: `AccountService.CreateAsync` 본문)
- **VF-9. 비밀번호 변경 UI가 없다**: `ChangePasswordAsync`는 존재하나 자기 비밀번호를 바꾸는 화면·VM이 없다(`UserMgmtViewModel.ResetUserPassword`는 power가 타인 pw를 "0000"으로 초기화하는 것으로, 요구 2.2의 "본인 2회 확인 변경"과 다르다). (근거: `UserMgmtViewModel.cs`)
- **VF-10. 촬영 진입이 로그인/게스트 선택을 강제한다**: `HomeViewModel.Start()` → `NavigateAsync(AppState.Login)`. 상태머신 `Forward[Home] = {Login, FrameSelect, Admin}`이라 `Home→FrameSelect` 전이는 이미 합법이지만, **홈 화면은 Login으로만 보낸다.** 요구 3(선택 없이 게스트 자동 진입) 위반. (근거: `HomeViewModel.cs`, `SessionStateMachine.cs`)
- **VF-11. 로그인 후 커스텀 프레임 흐름은 이미 존재**: `FrameSelectViewModel.OnEnterAsync`가 `Session.CurrentUser` 유무로 기본/커스텀 프레임을 분기 로드하고, `IsLoggedIn`으로 [프레임 만들기] 버튼 노출을 제어한다. → 로그인은 "선택의 전제"가 아니라 "커스텀을 위한 부가 기능"으로 이미 코드가 준비돼 있다. (근거: `FrameSelectViewModel.cs`)
- **VF-12. `AppSettings.OutputFormat`은 편집 UI에 노출되지 않는다**: 모델(`AppSettings.cs`)에는 있으나 `AdminView`에도 없다. 요구 2.1의 "AppSettings 전 항목 노출"을 위해 설정 페이지에 추가 대상. `StorageBucket`도 동일. (근거: `AppSettings.cs` vs `AdminView.xaml`)
- **VF-13. 창은 최소 1280×720**: `MainWindow` `MinHeight=720 MinWidth=1280`. 가로 기준 반응형이 필요하고, 세로(키오스크 세로 설치) 레이아웃은 현재 미구현(모든 View가 `d:DesignWidth=1280 DesignHeight=720` 가로 전제). (근거: `MainWindow.xaml`, View들)

### 미검증 가정 (구현 시 검증 대상 — WBS Step에 매핑)

- **OA-1. 스타일 리소스를 `ResourceDictionary`로 추출·병합해도 기존 화면이 시각적으로 동등하거나 개선된다** → 검증: WBS Step 2·9(빌드 warning 0 + 사용자 육안).
- **OA-2. `AppShellViewModel`에 상단 바 상태(로그인 여부·현재 상태)를 노출하고 `MainWindow`에 오버레이 바를 얹어도 상태머신 전이와 충돌하지 않는다** → 검증: WBS Step 3(`AppStateTests` 신규 케이스, 빌드).
- **OA-3. 설정 페이지를 새 상태(`AppState.Settings`)로 추가하고 어느 화면에서든 진입/복귀할 수 있다** → 검증: WBS Step 4(상태머신 테스트).
- **OA-4. `AccountService.CreateAsync`에 역할 권한 게이트를 추가해도 기존 시드/로그인 경로가 깨지지 않는다** → 검증: WBS Step 6(`AccountTests` 신규).
- **OA-5. 세로 레이아웃은 `MainWindow` 크기 감지 + 화면별 `VisualStateManager`/트리거로 대응 가능(별도 UserControl 스왑 불필요)** → 검증: WBS Step 9(빌드 + 사용자 육안). 위험 시 완화책은 §9 참조.
- **OA-6. INI 라운드트립·클램프는 기존 `IniSettingsService`·`AppSettings.Clamp`로 충분하고 신규 노출 항목(OutputFormat/StorageBucket)도 동일 방식으로 저장된다** → 검증: WBS Step 5(`SettingsTests` 확장).

---

## 1. 이터레이션 2 요구 → 설계 매핑 (한눈에)

| 요구(iteration-2 문서) | 현재 상태(VF) | 이번 설계 조치 | WBS Step |
|---|---|---|---|
| 1.1 좌상단 로그인 버튼(로그인 시 계정 허브) | 없음(VF-5) | 상단 바 좌측 로그인/계정 버튼 신설. 로그인 시 계정 팝오버(로그아웃·비번변경·관리자) | §3, Step 3·7 |
| 1.2 우상단 설정 버튼 | 없음(VF-5) | 상단 바 우측 설정 버튼 → 설정 페이지 | §3, Step 3 |
| 롱프레스 관리자 진입 통합/정리 | 롱프레스만(VF-5·6) | **롱프레스 폐지**, 설정 페이지 내 "관리자" 섹션으로 통합(§3.4 근거) | §3.4, Step 3·6 |
| 2.1 게스트도 설정 수정 | 관리자 전용(VF-6) | 설정 페이지를 게스트 접근 가능하게 신설, AppSettings 전 항목 노출 | §4, Step 4·5 |
| 2.2 로그인 사용자 비번 변경(2회 확인) | UI 없음(VF-9) | 설정 페이지 "계정" 섹션에 비번 변경 카드(신 비번 2회 confirm) | §4.3, Step 6 |
| 2.3 계정 생성(manager/admin→user, admin→manager) | 완전 미구현(VF-7·8) | 설정 "관리자" 섹션에 계정 생성 카드 + **서비스에 역할 게이트 강제** | §4.4, Step 6 |
| 2.3 기존 사용자 관리(목록·삭제·pw초기화) | UserMgmtView 존재 | 설정 "관리자" 섹션에 흡수·일관 배치(기존 VM 재사용) | §4.4, Step 6 |
| 3 촬영 바로 진입(비로그인=게스트 자동) | Login 강제(VF-10) | 홈 [촬영하기] → `FrameSelect` 직행. 로그인/게스트 선택 화면 제거 | §5, Step 7 |
| 4 UI 전면 재디자인 | 스타일 리소스 전무(VF-2) | `ResourceDictionary` 디자인 시스템 신설 + 전 화면 적용 | §2, Step 1·2·8·9 |

---

## 2. 디자인 시스템 (요구 4 — 가장 중요)

### 2.1 설계 근거 (조사 결과 요약)

- **색상 방향 = Direction A(라이트, "코튼 캔디").** 사용자가 3안 중 A를 확정(2026-07-20). 밝은 화이트 배경 + 로즈(`#FF4D79`) 주 강조 + 민트(`#37C9B0`) 보조 포인트의 파스텔 라이트 테마. 초안 v1의 다크 계승은 폐기.
- **키오스크 절대 원칙 = 가시성·큰 터치 타깃·무인 자기설명.** 접근성 가이드 기준 터치 타깃 **최소 44×44px**, 타깃 간 충분한 간격, 100ms 내 시각 피드백. 무인 타임아웃 홈 복귀(이미 구현). 라이트 테마에서는 **본문 텍스트 대비 확보**가 특히 중요 — 밝은 배경 위 옅은 회색 텍스트는 저대비가 되기 쉬우므로 본문은 Ink(`#241F2B`, on-white 16:1)로, 옅은 Muted는 큰 텍스트/보더에만 한정(§2.3 대비표). (근거: touchwall.us 키오스크 가이드, designstudiouiux 2026 트렌드, WCAG AA)
- **2026 트렌드는 두 갈래가 상충한다.** 한쪽은 부드러운 곡선·soft UI(muz.li, orizon), 다른 쪽은 "blur/저대비는 핵심 컨트롤 가시성을 해친다"며 고대비·솔리드 권장(tubikstudio). **키오스크에서는 가시성이 이긴다** → 조작 화면(설정·촬영·컷선택)은 **불투명 솔리드 카드 + 명확한 대비**. 라이트 테마의 깊이는 blur/glass가 아니라 **부드러운 그림자(soft shadow)**로 표현(§2.5). glassmorphism·네온 등 다크 전제 장식은 폐기(§2.7).
- **감성 방향**은 상용 셀프 포토부스(포토이즘/인생네컷/포토그레이)의 밝고 산뜻한 무드에 맞춘다: 화이트·에어리한 배경 + 로즈 포인트 + 넉넉한 여백 + 부드러운 그림자. 결과 프레임(다크 배경 콘텐츠)은 라이트 UI 위에 썸네일로 얹혀 로즈 강조와 톤이 이어진다(VF-4).
- **로즈는 CTA·핵심 액션에 집중, 민트는 보조 포인트로 제한(과용 금지).** 나머지 표면은 화이트/연회색/Ink로 조용하게 둔다(요구 7). 로즈 남발 시 시선이 분산되고 CTA 위계가 무너진다.

### 2.2 리소스 딕셔너리 구조 (색→브러시→스타일→템플릿 계층)

`src/MCPhoto.App/Themes/` 신설, `App.xaml`에서 `MergedDictionaries`로 순서 병합:

```
src/MCPhoto.App/Themes/
├─ Colors.xaml        — Color 원자값(팔레트). SolidColorBrush 아님, 순수 Color.
├─ Brushes.xaml       — Colors 참조 SolidColorBrush(테마 토큰). Freezable(자동 frozen).
├─ Typography.xaml    — 폰트 패밀리 + FontSize/Weight 스케일(Style TargetType=TextBlock 키별).
├─ Metrics.xaml       — 간격(Thickness)·CornerRadius·터치 타깃 크기·그림자(DropShadowEffect).
├─ Controls.xaml      — Button/TextBox/PasswordBox/ComboBox/CheckBox/ToggleSwitch/Card/ListBox 스타일·템플릿.
└─ Theme.xaml         — 위 5개를 MergedDictionaries로 묶는 진입점.
```

`App.xaml`:
```xml
<Application.Resources>
  <ResourceDictionary>
    <ResourceDictionary.MergedDictionaries>
      <ResourceDictionary Source="Themes/Theme.xaml" />
    </ResourceDictionary.MergedDictionaries>
    <!-- 기존 컨버터·DataTemplate는 이 아래 유지 -->
  </ResourceDictionary>
</Application.Resources>
```

> **성능(PRD §7·아키텍처 §8)**: 모든 브러시는 `SolidColorBrush`(WPF가 리소스로 선언 시 자동 Freeze). 그림자는 `DropShadowEffect`를 **정적 카드 스타일에만** 쓰고 프리뷰/카운트다운 등 30fps 렌더 표면에는 쓰지 않는다(BitmapEffect 회피). 리스트는 기존 `VirtualizingStackPanel` 기본 유지.

### 2.3 색 팔레트 → 브러시 토큰 (Colors.xaml / Brushes.xaml) — Direction A 라이트

확정 팔레트(사용자 선택)를 축으로, 라이트 테마에 필요한 상태·시맨틱·엘리베이션 색을 보강한다. 브러시 키는 역할 기반(테마 무관 네이밍이라 재테마 시 값만 교체). **모든 색은 §2.3.1 대비표로 용도가 제약된다.**

**확정 6색(오케스트레이터 지정, 변경 불가):**

| 역할 | 색값 |
|---|---|
| Ground(배경) | `#FFFFFF` |
| Surface(카드/입력) | `#F4F1F7` |
| Ink(본문 텍스트) | `#241F2B` |
| Muted(보조 텍스트/보더) | `#8A8494` |
| Accent Primary(로즈, CTA) | `#FF4D79` |
| onAccent | `#FFFFFF` |
| Accent Secondary(민트, 포인트) | `#37C9B0` |

**브러시 토큰(확정색 + 라이트 적응 보강값):**

| 브러시 키 | 색값 | 용도 | 비고 |
|---|---|---|---|
| `Brush.Bg` | `#FFFFFF` | 앱 배경(화이트) | 확정 |
| `Brush.Bg.Elevated` | `#FAF8FC` | 배경 위 살짝 뜬 영역(연한 라일락 화이트) | 보강 |
| `Brush.Surface` | `#F4F1F7` | 카드·패널·입력 표면 | 확정 |
| `Brush.Surface.Alt` | `#ECE8F0` | 보조 버튼·세그먼트 트랙 배경 | 보강(Surface보다 한 톤 진함) |
| `Brush.Surface.Hover` | `#E4DEEC` | 표면 hover | 보강 |
| `Brush.Surface.Press` | `#DAD2E4` | 표면 press | 보강 |
| `Brush.Border` | `#ECE8F0` | 카드/입력 기본 테두리(1px, 저강조) | 보강 |
| `Brush.Divider` | `#E4DEEC` | 구분선/디바이더 | 보강 |
| `Brush.Text.Primary` | `#241F2B` | 제목·본문(Ink) | 확정 |
| `Brush.Text.Secondary` | `#4A4453` | 일반 본문(Ink 소폭 연함, on-white ~9:1) | 보강(옛 Muted는 본문 대비 부족) |
| `Brush.Text.Tertiary` | `#6E6878` | 라벨·설명(중간, on-white ~5:1) | 보강 |
| `Brush.Text.Muted` | `#8A8494` | 힌트·비활성·placeholder(대형/보더 한정) | 확정(본문 사용 금지, §2.3.1) |
| `Brush.Accent` | `#FF4D79` | 주 강조(로즈, Primary CTA·선택) | 확정 |
| `Brush.Accent.Hover` | `#FF6B8F` | Accent hover(밝게) | 보강 |
| `Brush.Accent.Press` | `#E43C67` | Accent press(어둡게) | 보강 |
| `Brush.Accent.Text` | `#D6376A` | **로즈 텍스트 전용**(흰 배경 위 로즈 텍스트, ~4.5:1) | 보강(본문색 로즈가 필요할 때. `Brush.Accent` 원색은 본문 대비 3.19:1이라 큰 텍스트만) |
| `Brush.Accent.Soft` | `#FFE7EE` | 로즈 10% 틴트 배경(선택 칩/카드 배경) | 보강 |
| `Brush.OnAccent` | `#FFFFFF` | 로즈/민트 배경 위 텍스트(굵은 16px+ 한정) | 확정 |
| `Brush.Accent2` | `#37C9B0` | 보조 포인트(민트, 비텍스트 강조·성공 아이콘) | 확정 |
| `Brush.Accent2.Soft` | `#DFF6F1` | 민트 틴트 배경 | 보강 |
| `Brush.Accent2.Text` | `#128A76` | **민트 텍스트 전용**(흰 배경 위, ~4.8:1) | 보강(민트 원색은 텍스트 대비 FAIL 2.07:1) |
| `Brush.Success` | `#128A76` | 성공 텍스트(민트 계열 진한 톤) | 보강(민트와 통일감, 대비 확보) |
| `Brush.Success.Surface` | `#DFF6F1` | 성공 안내 배경 | 보강 |
| `Brush.Danger` | `#D92D4E` | 오류·삭제 텍스트(진한 레드, 로즈와 구분) | 보강(로즈 #FF4D79와 혼동 방지 위해 더 어둡고 붉게) |
| `Brush.Danger.Hover` | `#C22645` | 삭제 버튼 hover | 보강 |
| `Brush.Danger.Surface` | `#FDE8EC` | 위험 안내/버튼 배경 | 보강 |
| `Brush.Warning` | `#B26A00` | 경고 텍스트(앰버, 흰 배경 대비 확보) | 보강 |
| `Brush.Warning.Surface` | `#FFF3DE` | 경고 배경 | 보강 |
| `Brush.Disabled.Bg` | `#ECE8F0` | 비활성 버튼 배경 | 보강 |
| `Brush.Disabled.Fg` | `#B4AEBE` | 비활성 텍스트 | 보강 |
| `Brush.Scrim` | `#66241F2B` | 팝업/오버레이 스크림(Ink 40%) | 보강(라이트: 어둡게 덮되 과하지 않게) |
| `Brush.CaptureBg` | `#111114` | 촬영 화면 몰입 배경(프리뷰용, 예외적 다크) | 보강(§부록 A 예외) |

> **폐기**: `Brush.Glass`(glassmorphism 표면)는 라이트 테마에서 제거. 깊이는 그림자로만(§2.5·§2.7).

### 2.3.1 대비/접근성 검증 (WCAG AA, 계산값)

확정 팔레트를 상대 휘도 공식으로 실측했다(값은 계산 근거). **AA 일반 텍스트=4.5:1, AA 큰 텍스트(18.66px+Bold 또는 24px+)=3.0:1** 기준.

| 전경 / 배경 | 대비비 | 판정 | 사용 규칙 |
|---|---|---|---|
| Ink `#241F2B` on White | **16.08:1** | AA 일반 통과 | 본문·제목 기본 |
| Ink on Surface `#F4F1F7` | **14.37:1** | AA 일반 통과 | 카드 위 본문 |
| Text.Secondary `#4A4453` on White | ~9:1 | AA 일반 통과 | 보조 본문 |
| Text.Tertiary `#6E6878` on White | ~5:1 | AA 일반 통과 | 라벨·설명 |
| Muted `#8A8494` on White | **3.62:1** | 큰 텍스트만 | **본문 금지** — 힌트/placeholder/보더/대형 라벨에만 |
| Muted on Surface | 3.23:1 | 큰 텍스트만 | 동일 |
| onAccent `#FFFFFF` on 로즈 `#FF4D79` | **3.19:1** | 큰 텍스트만 | CTA 라벨은 **Bold 16px+**(=큰 텍스트)라 통과. 작은 흰 텍스트를 로즈 위에 두지 말 것 |
| Ink `#241F2B` on 로즈 | 5.05:1 | AA 일반 통과 | 로즈 배경에 작은 텍스트 필요 시 Ink 사용(안전 대안) |
| 로즈 `#FF4D79` on White(텍스트) | 3.19:1 | 큰 텍스트만 | 로즈 본문 텍스트는 `Brush.Accent.Text`(`#D6376A`, ~4.5:1) 사용 |
| 민트 `#37C9B0` on White | **2.07:1** | **FAIL** | 민트 위 흰 텍스트·흰 배경 위 민트 텍스트 **금지**. 민트는 비텍스트(아이콘/칩/구분/토글 채움)에만 |
| onAccent 흰 on 민트 | **2.07:1** | **FAIL** | 민트 배경엔 텍스트를 얹지 말 것(얹어야 하면 Ink=7.77:1) |
| Ink `#241F2B` on 민트 | 7.77:1 | AA 일반 통과 | 민트 배경에 텍스트 필요 시 Ink만 |

**핵심 규칙 3가지(구현 강제):**
1. **본문 텍스트는 Ink/Secondary/Tertiary만.** Muted는 힌트·placeholder·대형 라벨·보더로 한정.
2. **로즈 위 텍스트는 굵은 대형만**(CTA 라벨 Bold 16px+). 작은 텍스트가 필요하면 Ink를 얹거나, 흰 배경 위 로즈 텍스트는 `Accent.Text`(#D6376A).
3. **민트 위/민트로는 텍스트를 쓰지 않는다.** 민트는 순수 비텍스트 강조(아이콘·칩 배경·토글 채움·구분 점). 민트 톤 텍스트가 필요하면 `Accent2.Text`(#128A76).

### 2.4 타이포 스케일 (Typography.xaml)

폰트: 시스템 안전 조합 `Segoe UI, Malgun Gothic`(한글 포함, 번들 불필요). 폰트 리소스는 `Fonts/`에 커스텀 번들 시 교체 가능하도록 `FontFamily` 리소스 키(`Font.Primary`)로 간접화.

| 스타일 키 | FontSize | Weight | 용도 |
|---|---|---|---|
| `Text.Display` | 64 | Bold | 홈 타이틀("MC포토") |
| `Text.H1` | 32 | Bold | 화면 제목("프레임 선택") |
| `Text.H2` | 24 | SemiBold | 섹션 제목("필터", 설정 그룹) |
| `Text.Title` | 20 | SemiBold | 카드 제목 |
| `Text.Body` | 16 | Regular | 본문·버튼 |
| `Text.Label` | 14 | Regular | 입력 라벨 |
| `Text.Caption` | 13 | Regular | 힌트·부가 설명 |
| `Text.Countdown` | (Viewbox 스케일) | Bold | 카운트다운 숫자(크기는 Viewbox가 결정) |

각 스타일은 `Style TargetType=TextBlock`에 `x:Key` 부여(암묵 적용 아님), `Foreground` 기본은 `Brush.Text.Secondary`(라이트: Ink 소폭 연함), `FontFamily`는 `Font.Primary`. 제목류(Display/H1/H2)는 `Brush.Text.Primary`(Ink). 라벨은 `Text.Tertiary`, 힌트/Caption은 `Text.Muted` 허용(§2.3.1 규칙 1 준수 — Caption은 보조 정보라 Muted 가능하나 본문은 금지).

### 2.5 간격·모서리·터치·엘리베이션 (Metrics.xaml)

- **Spacing 스케일**(Thickness/double): `Space.XS=4`, `Space.S=8`, `Space.M=16`, `Space.L=24`, `Space.XL=40`, `Space.XXL=64`. 화면 외곽 여백 기본 `Space.XL`(40).
- **CornerRadius**: `Radius.S=8`(입력·작은 버튼), `Radius.M=14`(카드·주요 버튼), `Radius.L=24`(대형 표면·팝업), `Radius.Pill=999`(상단 바 아이콘 버튼).
- **터치 타깃**: `Touch.Min=48`(최소 높이, 44px 가이드 +여유), Primary CTA 높이 `Touch.CTA=56`, 상단 바 아이콘 버튼 `Touch.IconBtn=56×56`.
- **엘리베이션(라이트 전용, soft shadow):** 다크에서는 밝은 서피스로 깊이를 줬지만, 화이트 배경에서는 서피스 명도차가 작아 **부드러운 그림자로 깊이를 표현**한다. 그림자는 검정 저투명(살짝 로즈-라일락 틴트 허용)으로 은은하게. 3단계 토큰:
  - `Shadow.Sm`(DropShadowEffect BlurRadius=8, ShadowDepth=1, Opacity=0.06, Color=`#241F2B`) — 입력/작은 버튼 hover.
  - `Shadow.Card`(BlurRadius=20, ShadowDepth=4, Opacity=0.08, Color=`#241F2B`) — 카드 기본 엘리베이션.
  - `Shadow.Pop`(BlurRadius=32, ShadowDepth=8, Opacity=0.14, Color=`#241F2B`) — 팝업/팝오버/QR 다이얼로그.
  - 카드/팝업 한정. 프리뷰/카운트다운 등 30fps 렌더 표면엔 그림자 금지(성능). 그림자 투명도가 낮아 라이트 배경에서 과하지 않게.
- **테두리 규약(라이트):** 화이트 배경 위 카드/입력은 그림자만으로도 뜨지만, 저강조 경계가 필요하면 `Brush.Border`(#ECE8F0) 1px. 그림자 + 얇은 보더 병용 가능(라이트에서 흔한 패턴).

### 2.6 컨트롤 스타일·템플릿 (Controls.xaml)

상태별(hover/press/disabled/focus) 트리거 포함. 모두 `x:Key`로 명시 참조(기존 화면을 점진 교체할 수 있게 암묵 스타일은 최소화하되, `TextBox`/`PasswordBox`/`CheckBox`/`ComboBox`는 종류가 적어 **암묵 스타일**로 전역 적용해도 안전 → 입력류는 암묵, 버튼은 키 기반으로 결정).

> 라이트 테마 상태 스타일 원칙: 배경 표면은 hover/press로 **한 톤씩 진하게**(`Surface`→`Surface.Hover`→`Surface.Press`), 로즈 CTA는 hover=밝게/press=어둡게. 포커스는 로즈 2px 링. disabled는 연회색 배경 + 흐린 텍스트.

| 스타일 키 | 대상 | 사양(라이트) |
|---|---|---|
| `Button.Primary` | 주 CTA(촬영하기·다음·저장·로그인) | `Brush.Accent`(로즈) 배경, `Brush.OnAccent`(흰) **Bold 16**(대형이라 대비 3.19:1 통과), 높이 `Touch.CTA`, `Radius.M`, `Shadow.Sm`. hover=`Accent.Hover`, press=`Accent.Press`+`ScaleTransform` 0.98, disabled=`Disabled.Bg`/`Disabled.Fg`. |
| `Button.Secondary` | 보조(프레임 만들기·뒤로) | `Surface.Alt` 배경, `Text.Primary`(Ink) 텍스트, 1px `Border`, 나머지 Primary와 동일. hover=`Surface.Hover`, press=`Surface.Press`. |
| `Button.Ghost` | 취소·닫기 | 투명 배경, `Text.Tertiary` 텍스트, 테두리 없음, hover 시 `Surface.Hover` 배경 + `Text.Primary`. |
| `Button.Danger` | 앱 종료·삭제 | `Danger.Surface`(연분홍) 배경 + `Danger`(#D92D4E) 텍스트, hover=`Danger.Surface` 진하게 or `Danger` 보더. (로즈 CTA와 색으로 구분 — §2.3 danger는 더 어둡고 붉음) |
| `Button.Icon` | 상단 바 로그인/설정 | `Touch.IconBtn`, `Radius.Pill`, **투명 배경**(glass 폐기), hover 시 `Surface.Hover`. 아이콘 글리프는 `Text.Primary`(Ink)로 그려 흰 배경 대비 확보. 콘텐츠=아이콘 + 접근 라벨. |
| `Button.Filter` | 필터 칩(원본/흑백/밝게/뷰티) | `Surface.Alt` 기본 + `Text.Primary`, **선택 시 `Accent.Soft`(연로즈) 배경 + `Accent`(로즈) 2px 테두리 + `Accent.Text` 텍스트**(선택 표시). |
| `Button.FrameCard` | 프레임 선택 카드 | `Card` 스타일 + 선택 시 `Accent` 2px 테두리 + `Shadow.Card`(선택은 그림자 강조). |
| `Card` | 설정/계정/관리 카드 컨테이너 | `Surface`(#F4F1F7) 배경, `Radius.M`, `Border` 1px(선택), 내부 패딩 `Space.L`, `Shadow.Card`(라이트 soft shadow로 깊이). |
| `TextInput`(암묵 TextBox) | 모든 입력 | `Bg`(흰) 또는 `Surface` 배경 + 1px `Border`, `Text.Primary`, placeholder=`Text.Muted`, `Radius.S`, 패딩 10, focus 시 `Accent` 2px 테두리 + `Shadow.Sm`. |
| `PasswordInput`(암묵 PasswordBox) | 비번 입력 | TextInput과 동일 룩. |
| `Toggle`(ToggleButton 기반 스위치) | 거울/플래시/QR/로컬 on-off | 체크박스 대신 **토글 스위치** 템플릿(키오스크 가독성). 트랙 off=`Surface.Alt`, **on=`Accent`(로즈)**, thumb=흰 원(그림자). 라벨 텍스트는 옆에 Ink로. 민트는 트랙에 쓰지 않음(텍스트 없어 무방하나 CTA색=로즈로 통일). |
| `Segmented`(컷수/카운트다운/포맷 선택) | 세그먼트 컨트롤 | 트랙=`Surface.Alt`, **active 세그먼트=`Accent`(로즈) 배경 + `OnAccent` Bold**(대형), inactive=`Text.Tertiary`. 라디오 대체 UX. |
| `ScreenTitle` | 화면 제목 TextBlock | `Text.H1`(Ink) + 상단 여백 규약. |

### 2.7 애니메이션·모션

- **화면 전이 페이드**: `ContentControl`(MainWindow) 콘텐츠 교체 시 0.15s opacity 페이드인. `AppShell` 레벨 트리거 또는 `ContentControl` 스타일의 `VisualStateManager`. 과한 슬라이드 지양(무인·키오스크는 절제).
- **버튼 press**: `ScaleTransform` 0.98, 80ms. 100ms 내 피드백 원칙 충족.
- **카운트다운**: 숫자 전환 시 scale 1.2→1.0 + opacity 펄스(기존 정적 대비 생동감). 기존 `Viewbox` 유지.
- **플래시**: 기존 `FlashOverlay`(흰 화면) opacity 0→1→0 스토리보드(현재는 Visibility만) — 셔터감 개선. **라이트 테마에서도 유효**(촬영 배경은 예외적 다크 `Brush.CaptureBg`라 흰 플래시가 여전히 대비된다).
- **토스트/저장 안내**: `SavedNotice`류를 3초 후 자동 페이드아웃(현재 텍스트 잔존). 성공=`Success`(#128A76), 오류=`Danger`(#D92D4E).
- **다크 전용 장식 폐기**: glassmorphism/네온/블러 표면은 제거(§2.3 `Brush.Glass` 삭제). 라이트의 시각적 흥미는 **부드러운 그림자 + 로즈/민트 파스텔 포인트 + 넉넉한 여백**으로 낸다.
- 모든 애니메이션은 `Storyboard`(선언적), 코드비하인드 최소.

### 2.8 가로/세로 레이아웃 (PRD #20, VF-13)

- `MainWindow` `SizeChanged`로 종횡비 판정 → `AppShellViewModel.Orientation`(Landscape/Portrait) 노출.
- 각 화면은 `DataTrigger`(바인딩 `Orientation`) 또는 `VisualStateManager`로 주요 `Grid`의 행/열을 재배치. 예: ResultView·Admin(설정)은 가로=좌우 2열, 세로=상하 2행.
- 프레임 미리보기·프리뷰는 자기 비율대로 중앙 정렬(`Stretch=Uniform`) — 방향 무관 안전.
- **완화책(OA-5 위험 시)**: 방향별 전용 UserControl 스왑은 하지 않고 세로 기본값을 "가로 레이아웃을 세로 창에 그대로 중앙 배치(레터박스)"로 폴백 — 최소 동작 보장.

---

## 3. 상단 바 네비게이션 (요구 1)

### 3.1 배치·구조

`MainWindow`에 **오버레이 상단 바**를 추가한다(콘텐츠 위 `Grid` 레이어). 상태머신·화면 VM과 독립된 셸 수준 UI이므로 `AppShellViewModel`에 바인딩한다.

```
MainWindow Grid
 ├─ ContentControl (CurrentViewModel)         ← 기존, 전체 채움
 ├─ TopBar (Grid, VerticalAlignment=Top)      ← 신설, 높이 ~72, 배경=투명(라이트 콘텐츠 위 얹힘)
 │   ├─ 좌: Button.Icon "로그인"/"{계정ID}"   → 계정 팝오버 or 로그인 페이지
 │   └─ 우: Button.Icon "설정"(gear)          → 설정 페이지
 └─ (AdminCorner 롱프레스 영역 — 폐지, §3.4)
```

- **라이트 테마 상단 바**: 배경 투명(화이트 앱 배경 위에 자연스럽게 얹힘). 아이콘 글리프는 Ink(`Text.Primary`)로 그려 흰 배경 대비 확보. 콘텐츠가 아이콘 밑까지 스크롤되는 화면(설정 등)에서는 상단 바 배경을 `Brush.Bg`(흰) 불투명 + `Shadow.Sm`으로 얇게 띄워 겹침 시에도 아이콘 가독 유지(스크롤 위치에 따라 그림자 표시).
- **가시성 규칙**: 상단 바는 **홈·프레임선택·설정·결과·완료 등 정적 화면에서 표시**, **촬영(Capture)·카운트다운·QR 팝업 등 몰입/모달 화면에서는 숨김**(오조작·산만 방지). `AppShellViewModel.IsTopBarVisible`(현재 상태 기반 계산 프로퍼티)로 제어.
- 상단 바 버튼도 `NotifyUserActivity`(유휴 리셋) 대상 — 기존 `PreviewMouseDown` 핸들러가 창 전체를 덮으므로 자동 충족.

### 3.2 좌측 — 로그인/계정 버튼

`AppShellViewModel`에 계정 상태를 노출한다(현재 `Session.CurrentUser`는 있으나 셸이 관찰 프로퍼티로 안 올림):

- 신규 `[ObservableProperty] User? CurrentUser`(또는 `Session.CurrentUser` 변경 통지 래핑) + 파생 `bool IsLoggedIn`, `string AccountLabel`(비로그인="로그인", 로그인=계정 ID).
- **비로그인** 클릭 → `NavigateAsync(AppState.Login)`. 로그인 성공 시 이전 화면 복귀 또는 홈(§3.3).
- **로그인** 클릭 → **계정 팝오버**(Popup): 계정 ID·역할 표시 + [비밀번호 변경](→설정 계정 섹션) + [로그아웃] + (power면)[관리자 설정](→설정 관리자 섹션). 요구 1.1 "계정 허브 겸용" 충족.
- 로그아웃 = `Session.CurrentUser=null` + 상단 바 갱신 + 홈 복귀.

### 3.3 로그인 페이지의 역할 변화

기존 `LoginGuestView`는 "로그인/게스트 선택"이 목적이었으나, 촬영 진입이 게스트 자동화(§5)되면서 **"게스트로 계속" 버튼의 의미가 사라진다**. 재정의:

- `LoginGuestView` → **로그인 전용 화면**으로 축소(뷰 이름은 유지하되 게스트 버튼 제거, 또는 `LoginView`로 리네이밍 — WBS에서 결정). 상단 바 로그인 버튼·프레임 선택의 "로그인하면 커스텀 프레임" 유도로 진입.
- 로그인 성공 → `Session.CurrentUser` 설정 후 **직전 화면으로 복귀**(상단 바에서 눌렀으면 그 화면, 프레임 선택에서 유도됐으면 `FrameSelect` 재진입해 커스텀 프레임 로드). 복귀 대상은 `AppShellViewModel`이 진입 전 상태를 기억(`_returnStateAfterLogin`).
- `LoginGuestViewModel.ContinueAsGuestCommand`는 제거 대상(참조 정리).

### 3.4 롱프레스 폐지 결정 (요구 1: 통합/정리, 근거 기록)

**결정: 좌상단 3초 롱프레스 관리자 진입을 폐지하고, 관리자 기능을 설정 페이지 "관리자" 섹션으로 통합한다.**

근거:
1. **발견성**: 롱프레스는 숨겨진 제스처라 무인 키오스크에서 관리자조차 접근이 불편하다. 명시적 설정 버튼(요구 1.2)이 이미 우상단에 생기므로 진입점을 이중화할 이유가 없다.
2. **요구 정합**: 요구 2.3이 "관리자 기능을 **설정 페이지 내** 조건부 표시"로 명시한다. 관리자 전용 화면(AdminView)을 별도 상태로 두는 대신 설정 페이지에 흡수하는 것이 요구 문언과 일치.
3. **우발 진입 방지**: 히트영역 롱프레스는 어린이·다중 터치 환경에서 우발 진입 위험. 설정→로그인 게이트가 더 안전.
4. **오조작 표면 축소**: `MainWindow`의 투명 히트영역과 `DispatcherTimer` 롱프레스 코드(코드비하인드)를 제거해 셸이 단순해진다(MVVM 순수성↑).

이관: `MainWindow.xaml`의 `AdminCorner` Border + `OnAdminCornerDown/Up`·`_longPressTimer` 제거. `AppState.Admin` 상태는 **유지하되 설정 페이지의 서브뷰로 재배치**(§4.4). 관리자 진입 = 설정 페이지에서 로그인 상태가 power일 때 "관리자" 섹션 노출(로그인 안 됐으면 설정 내 "관리자 로그인" 카드).

---

## 4. 설정 페이지 (요구 2 — 최우선 결함 해소, 신설)

### 4.1 개념

새 상태 `AppState.Settings`. 우상단 설정 버튼으로 진입, [뒤로/닫기]로 직전 화면 복귀. **역할에 따라 섹션이 조건부 표시**되는 단일 스크롤 페이지(섹션 카드 스택). 게스트도 진입 가능.

```
설정 페이지 (SettingsView / SettingsViewModel)
├─ [앱 설정] 섹션           — 게스트 포함 전원(요구 2.1)
├─ [계정] 섹션              — 로그인 사용자만(요구 2.2)
└─ [관리자] 섹션            — power(manager/admin)만, 로그인 게이트(요구 2.3)
```

- 각 섹션은 `Card` 스타일. 조건부 표시는 `Visibility` 바인딩(`IsGuest`/`IsLoggedIn`/`IsPower`).
- 기존 `AdminView`/`AdminViewModel`·`UserMgmtView`/`UserMgmtViewModel` 자산은 **설정 페이지 서브영역으로 재사용/이관**(§4.5).

### 4.2 [앱 설정] 섹션 (요구 2.1 — 게스트 포함)

`AppSettings` **전 항목**을 UI로 노출·수정하고 [저장] 시 INI flush(`ISettingsService.Save`, 기존 그대로).

| 항목 | 컨트롤 | 비고 |
|---|---|---|
| 촬영 컷 수(6/8/10) | 세그먼트/토글 그룹 or ComboBox | 허용값 목록(`AppSettings.AllowedCutCounts`) |
| 카운트다운(3/6/8/10) | 세그먼트/ComboBox | `AllowedCountdownSecs` |
| 거울모드 | `Toggle` | 요구 2.1 예시 명시 |
| 플래시 | `Toggle` | |
| 출력 포맷(JPG/PNG) | ComboBox | **신규 노출**(VF-12) |
| QR 전송 | `Toggle` | |
| 로컬 저장 | `Toggle` | 요구 2.1 예시 명시 |
| 로컬 저장 경로 | TextBox + [폴더 선택] | |
| 보관 시간(1~72h) | Slider or TextBox(클램프) | |
| 카메라 장치 인덱스 | ComboBox(열거) or TextBox | |
| 표시 모드(전체/창) | ComboBox | **신규 노출** |
| Hosting Base URL | TextBox | 기존 |
| Storage Bucket | TextBox | **신규 노출**(VF-12) |

- **저장 정책**: [저장] 버튼에서 일괄 flush(입력 중 실시간 파일쓰기 없음 — WBS Step 5 non-goal 유지). 저장 성공 시 `Success` 토스트(3초 자동 소멸).
- **범위 강제**: 저장 시 `AppSettings.Clamp()`(기존) 호출 — 잘못된 값 자동 보정.
- **게스트 접근**: 이 섹션은 로그인 무관 항상 표시·수정 가능(요구 2.1 명시).

### 4.3 [계정] 섹션 (요구 2.2 — 로그인 사용자)

- 표시 조건: `IsLoggedIn`. 미로그인 시 이 자리에 "로그인하면 비밀번호를 변경할 수 있어요" 안내 + [로그인] 버튼.
- **비밀번호 변경 카드**: 현재 비번(선택·검증용) + 새 비번 + 새 비번 확인 3개 `PasswordBox`. **새 비번 2회 일치 확인**(요구 2.2) 후 `IAccountService.ChangePasswordAsync(CurrentUser.Id, newPw)` 호출. 불일치·빈 값 시 인라인 오류(`Danger`), 성공 시 `Success` 토스트.
- [로그아웃] 버튼(계정 팝오버와 중복 허용).

### 4.4 [관리자] 섹션 (요구 2.3 — power 조건부, 로그인 게이트)

- 표시 조건: 로그인 상태가 `IsPower`(manager/admin). 비로그인·user에게는 **숨김**(요구 2.3). 대신 설정 페이지 하단에 접힌 "관리자 로그인" 카드(로그인 게이트)를 두어 power 로그인 유도.
- **하위 그룹**:
  1. **계정 생성**(요구 2.3 핵심 미구현):
     - 입력: 새 ID·새 PW.
     - **역할 선택은 로그인 역할에 따라 동적**:
       - `manager` 로그인 → 생성 가능 역할 = **user만**.
       - `admin` 로그인 → 생성 가능 역할 = **user, manager**(admin 생성 불가 — 최종 1인 규칙, PRD §F8).
     - VM이 후보 역할 목록(`CreatableRoles`)을 로그인 역할로 산출해 ComboBox 바인딩.
     - **서비스가 최종 방어**: `IAccountService.CreateAsync`에 `actingRole`(호출자 역할) 인자를 추가하고, 서비스에서 규칙 위반 시 예외(§7·VF-8 해소).
  2. **사용자 관리**(기존 `UserMgmtView` 흡수): 목록·삭제(cascade)·pw 초기화·manager 지정(admin만). 기존 `UserMgmtViewModel` 재사용.
  3. **공용 기본 프레임 관리**(PRD §F8 power): 기본 프레임 등록/삭제 진입(기존 프레임 편집기 재사용, `IsDefault=true` 저장 경로 — MVP 수준 유지, 본 이터레이션 신규 기능 아님, §비범위).
  4. **앱 설정 고급/앱 종료**: 기존 AdminView의 [앱 종료](`Application.Current.Shutdown`) 이관. AppSettings 편집 자체는 [앱 설정] 섹션이 담당하므로 관리자 섹션에서 중복 제거.

### 4.5 기존 자산 재사용/이관 방침

| 기존 자산 | 이번 처리 |
|---|---|
| `AdminViewModel` (설정 편집 로직·LoadSettings/SaveSettings) | **`SettingsViewModel`로 승격·확장**. 앱설정 필드는 [앱 설정] 섹션이 이어받고, 로그인 게이트는 [관리자] 섹션 로그인 카드로 이동, 신규 항목(OutputFormat/DisplayMode/StorageBucket) 추가. |
| `AdminView.xaml` | `SettingsView.xaml`로 재작성(디자인 시스템 적용, 섹션 카드 구조). |
| `UserMgmtViewModel` / `UserMgmtView` | **거의 그대로 재사용**. 설정 페이지 [관리자] 섹션의 사용자 관리 하위뷰로 임베드하거나 서브 네비(`AppState.UserMgmt` 유지) — 스타일만 디자인 시스템 적용. |
| `AppState.Admin` | 설정 페이지로 대체(제거) 또는 내부 서브상태로 흡수. **결정: `AppState.Admin` 제거, `AppState.Settings` 신설.** `UserMgmt`는 설정의 자식 네비로 유지. |
| `IAccountService.ChangePasswordAsync` | 계정 섹션에서 그대로 사용(자기 비번 변경). |
| `IAccountService.CreateAsync` | **시그니처 확장**(actingRole 게이트) 후 계정 생성 카드에서 사용. |

---

## 5. 촬영 진입 흐름 (요구 3)

### 5.1 변경점

- **홈 [촬영하기]** → `HomeViewModel.Start()`가 `AppState.Login` 대신 **`AppState.FrameSelect`로 직행**. `Session.Reset()`은 유지(비로그인=게스트).
- 로그인/게스트 선택 화면(현 `LoginGuestView`의 게스트 분기)을 **강제로 거치지 않는다.** 게스트는 프레임 선택에서 기본 프레임만 보이고(이미 `FrameSelectViewModel`이 분기), 커스텀이 필요하면 상단 바 로그인 또는 [프레임 만들기] 유도(로그인 요구).
- 결과적으로 **비로그인 세션은 홈→프레임선택→…→완료**로 흐르고, 로그인은 세션 중 언제든 상단 바로 부가.

### 5.2 상태머신 변경 (SessionStateMachine)

```diff
  [AppState.Home]      = { Login, FrameSelect, Admin }
+ [AppState.Home]      = { FrameSelect, Login, Settings }   // Admin 제거, Settings 추가; Home→FrameSelect가 주 경로
  [AppState.Login]     = { FrameSelect, Admin, FrameEditor }
+ [AppState.Login]     = { FrameSelect, FrameEditor, Settings }
- [AppState.Admin]     = { UserMgmt, FrameEditor }
+ (Admin 제거)
+ [AppState.Settings]  = { Login, UserMgmt, FrameEditor }   // 설정→로그인(계정), →사용자관리, →기본프레임편집
+ [AppState.UserMgmt]  = { Settings }                        // 뒤로=설정
  [AppState.FrameEditor] = { FrameSelect, Admin, Login }
+ [AppState.FrameEditor] = { FrameSelect, Settings, Login }
```

- **Settings는 어느 화면에서든 진입 가능해야** 한다(상단 바). 구현: `Home→Settings`, `FrameSelect→Settings`, `Result→Settings` 등 개별 전이를 Forward에 넣기보다, `CanTransition`에 **"to==Settings는 항상 허용"** 규칙을 추가(현재 `to==Home` 항상 허용과 동일 패턴). 설정에서 복귀는 `AppShellViewModel`이 진입 전 상태를 기억해 되돌린다(`_returnStateAfterSettings`).
- **로그인도 상단 바로 어디서든** 진입 → 유사하게 `to==Login` 허용 완화 또는 진입 전 상태 기억 후 복귀. **결정**: Settings·Login·Home 3개를 "오버레이성 진입"으로 보고 `CanTransition`에서 특례 허용 + 복귀 상태 스택(단순화 위해 단일 `_returnState` 보관).
- `IsSessionActive`(유휴 감시 대상)에서 **Settings·Login은 제외**(설정 조작 중 유휴 홈복귀는 사용자 이탈 유발) — 단, 촬영 세션 데이터가 살아있는 채로 설정을 오래 열어두는 케이스는 §9 리스크에 기록.

### 5.3 네비게이션 복귀 로직 (AppShellViewModel)

- `NavigateToOverlayAsync(AppState target)`: 현재 상태를 `_returnState`에 저장 후 전이(Settings/Login용).
- `ReturnFromOverlay()`: `_returnState`로 복귀(없으면 Home). 설정/로그인 [뒤로]·로그인 성공 후 사용.
- 촬영 세션 진행 중(FrameSelect~Result) 설정을 열었다 닫으면 원래 세션 화면으로 정확히 복귀 → `SessionContext` 보존(Reset 호출 금지).

---

## 6. View ↔ ViewModel 매핑 (변경/신설 종합)

| 화면(View) | ViewModel | 상태 | 변경 유형 | 핵심 변경 |
|---|---|---|---|---|
| MainWindow | AppShellViewModel | (셸) | **변경** | 상단 바 오버레이 추가, `IsTopBarVisible`/`IsLoggedIn`/`AccountLabel`/`Orientation` 노출, 롱프레스 코드 제거, 오버레이 네비(`NavigateToOverlayAsync`/`ReturnFromOverlay`) |
| HomeView | HomeViewModel | Home | **변경** | Start → `FrameSelect` 직행. 라이트 디자인 시스템 적용. (선택) 홈에 로즈/민트 파스텔 장식·soft shadow |
| (신규) SettingsView | SettingsViewModel | **Settings(신규)** | **신설** | 앱설정/계정/관리자 3섹션. AdminViewModel 로직 승격 |
| LoginGuestView → LoginView | LoginGuestViewModel(→LoginViewModel) | Login | **변경** | 게스트 버튼 제거, 로그인 후 복귀 로직, 디자인 적용 |
| FrameSelectView | FrameSelectViewModel | FrameSelect | **경미** | 로직 유지, 디자인 시스템·카드 선택 강조·세로 레이아웃 |
| GuideView | GuideViewModel | Guide | **경미** | 디자인 적용 |
| CaptureView | CaptureViewModel | Capture | **경미** | 디자인(카운트다운 모션·플래시 스토리보드), 상단 바 숨김 |
| CutSelectView | CutSelectViewModel | CutSelect | **경미** | 디자인·선택 강조 |
| ResultView | ResultViewModel | Result | **변경** | 필터 칩 선택 표시, 세로 레이아웃, 디자인 |
| QrPopupView | QrPopupViewModel | Qr | **경미** | 디자인, 팝업 스크림 |
| DoneView | DoneViewModel | Done | **경미** | 디자인 |
| FrameEditorView | FrameEditorViewModel | FrameEditor | **경미** | 디자인, 세로 대응 |
| ~~AdminView~~ | ~~AdminViewModel~~ | ~~Admin~~ | **제거/승격** | SettingsView/VM으로 흡수 |
| UserMgmtView | UserMgmtViewModel | UserMgmt | **경미** | 설정 [관리자] 하위로 재배치, 디자인 |
| PreviewView | PreviewViewModel | (촬영 프리뷰) | **경미** | 디자인(테스트/진단용) |

---

## 7. 계정 서비스 변경 (역할 권한 게이트 — VF-8 해소)

`IAccountService.CreateAsync`에 **호출자 역할**을 받아 규칙을 서비스에서 강제한다(호출자 신뢰 금지, 방어적 설계).

```csharp
// 변경 전
Task<User> CreateAsync(string id, string password, UserRole role = UserRole.User, CancellationToken ct = default);

// 변경 후 (권한 게이트)
Task<User> CreateAsync(string id, string password, UserRole role, UserRole actingRole, CancellationToken ct = default);
```

규칙(PRD §F8, 요구 2.3):
- `actingRole == Admin`: `role ∈ {User, Manager}` 허용, `Admin` 생성 거부(최종 1인).
- `actingRole == Manager`: `role == User`만 허용, `Manager`/`Admin` 거부.
- `actingRole == User` 또는 그 외: 생성 전면 거부.
- 위반 시 `UnauthorizedAccessException`(또는 도메인 예외) — VM은 인라인 오류로 표시.

- `EnsureSeedAccountAsync`(시드 admin 생성)는 시스템 부트스트랩 경로이므로 이 게이트를 우회(내부 전용, 기존 유지).
- 기존 호출부는 없으므로(VF-7) **호환성 파손 없음** — 신규 계정 생성 카드가 유일한 호출자.
- `SetRoleAsync`(manager 지정)는 `UserMgmtViewModel.PromoteToManager`가 `IsAdmin` 게이트로 이미 보호하나, 방어적으로 서비스에도 actingRole 게이트 추가 권장(WBS 선택 항목).

> 테스트(`AccountTests`): admin→user OK, admin→manager OK, admin→admin 거부, manager→user OK, manager→manager 거부, user→any 거부.

---

## 8. 리소스 딕셔너리 최종 구조 & 적용 순서 (요약)

1. `Themes/Colors.xaml`, `Brushes.xaml`, `Typography.xaml`, `Metrics.xaml`, `Controls.xaml` 작성 → `Theme.xaml`로 병합 → `App.xaml` MergedDictionaries 등록. (Step 1·2)
2. 화면별로 색 리터럴 → 브러시 키(부록 A **역할 전환** 매핑), 인라인 버튼 속성 → `Style="{StaticResource Button.Primary}"` 치환. **다크→라이트 반전 주의**: 흰색 텍스트를 흰 배경에 그대로 두지 않도록 Ink로 반전. 정적 확인: `grep`로 View XAML에 남은 `#` 색 리터럴 0에 근접(촬영 배경 `Brush.CaptureBg`·스크림 등 불가피 제외 명시). (Step 8·9)
3. 신규/변경 화면(Settings·Login·상단 바)은 처음부터 디자인 시스템으로 작성. (Step 3·4·7)

---

## 9. 리스크 & 완화

| # | 리스크 | 영향 | 완화 | 검증 |
|---|---|---|---|---|
| R1 | 다크→라이트 반전 치환 중 시각 회귀(흰 배경에 흰 텍스트 등 저대비) | 텍스트 소실·판독 불가 | 부록 A 역할 전환 매핑 + §2.3.1 대비 규칙 준수. 화면별 점진 치환 + 빌드 warning 0. grep로 잔존 리터럴 확인 | Step 8·9 빌드 + 사용자 육안 |
| R1b | 로즈/민트 위 저대비 텍스트(로즈 흰텍스트 3.19:1·민트 텍스트 FAIL) | 접근성 미달 | §2.3.1 강제 규칙: 로즈 위 텍스트=Bold 대형만, 민트엔 텍스트 금지. 텍스트색 로즈/민트는 Accent.Text/Accent2.Text 사용 | Step 8 grep + 사용자 육안 |
| R2 | 상단 바 오버레이가 화면 콘텐츠와 겹침/터치 가림 | 조작 방해 | 정적 화면만 표시(`IsTopBarVisible`), 촬영/팝업 숨김. 콘텐츠 상단 패딩 확보 | Step 3 빌드 + 육안 |
| R3 | Settings/Login "어디서든 진입" 특례가 불법 전이 허용 남발 | 상태머신 붕괴 | 특례는 Settings/Login/Home 3개로 한정, 복귀는 단일 `_returnState`. 단위 테스트로 전이표 고정 | Step 4 `AppStateTests` |
| R4 | 촬영 세션 중 설정 열고 유휴 → 세션 데이터 처리 모호 | 데이터 잔존/유실 | Settings는 유휴 감시 제외하되, 설정에서 홈 복귀 시 세션 Reset. 세션 유지 복귀는 SessionContext 보존 | Step 4·7 육안 |
| R5 | 계정 생성 역할 게이트 시그니처 변경이 테스트/DI 파손 | 빌드 실패 | 기존 호출부 없음(VF-7). 인터페이스·구현·테스트 동시 변경 | Step 6 `dotnet build`/`AccountTests` |
| R6 | 세로 레이아웃 다수 화면 대응 공수 과다 | 일정 초과 | VSM/트리거로 주요 화면만 우선, 나머지는 중앙 정렬 폴백(OA-5 완화책) | Step 9 빌드 + 육안 |
| R7 | 토글 스위치 등 커스텀 템플릿 접근성/터치 타깃 미달 | 조작 실패 | `Touch.Min=48` 강제, 템플릿에 히트영역 확보 | Step 2 육안 |

---

## 10. 사용자 확인 필요 목록 (UI 육안 — headless 불가)

> WBS의 어떤 Step도 "앱 실행 후 관측"을 완료 기준으로 쓰지 않는다. 아래는 구현 완료 후 **사용자가 직접 실행해 육안 확인**할 항목(각 WBS Step의 trigger/non-goal로 분리 기술됨).

1. 디자인 시스템 적용 후 전 화면이 일관된 라이트 톤(화이트+로즈/민트)으로 보이는지, "올드함"이 해소됐는지, 다크 잔재(어두운 배경/흰 배경 위 흰 텍스트)가 없는지, 로즈 CTA가 과하지 않고 위계가 살아있는지.
2. 상단 바 로그인/설정 버튼이 정적 화면에 보이고 촬영 화면에서 숨는지, 터치가 콘텐츠를 가리지 않는지.
3. 홈 [촬영하기] → 선택 없이 프레임 선택으로 직행(게스트)하는지.
4. 설정 페이지: 게스트가 앱설정 수정·저장·재시작 복원, 로그인 사용자 비번 2회 확인 변경, power의 계정 생성(역할 규칙)·사용자 관리가 동작하는지.
5. 가로/세로 창에서 레이아웃이 깨지지 않는지.
6. 카운트다운·플래시·전이 페이드 등 애니메이션이 자연스러운지.
7. 롱프레스 폐지 후에도 관리자 진입(설정→관리자 로그인)이 가능한지.

---

## 부록 A. 색 리터럴 → 브러시 토큰 매핑표 (치환 가이드, 라이트)

> ⚠️ **값 계승이 아니라 역할 전환이다.** 기존 다크 리터럴을 같은 값으로 리소스화하는 게 아니라, 그 리터럴이 **화면에서 하던 역할**을 라이트 토큰으로 바꿔 매핑한다(예: 다크 배경 `#141018`은 라이트에서 흰 배경 역할 → `Brush.Bg`=#FFFFFF). developer는 각 View의 리터럴을 아래 역할 기준으로 치환한다.

| 현재 리터럴(다크 MVP) | 화면 내 역할 | → 브러시 키(라이트 값) |
|---|---|---|
| `#141018` | 앱/화면 배경 | `Brush.Bg` (#FFFFFF) |
| `#241E30` | 카드·패널 표면 | `Brush.Surface` (#F4F1F7) |
| `#332B3E` | 보조 버튼·입력 배경 | `Brush.Surface.Alt` (#ECE8F0) |
| `#F5F0FA` | 제목·강조 텍스트 | `Brush.Text.Primary` (#241F2B, Ink) |
| `#E8DEF2` | 일반 본문 텍스트 | `Brush.Text.Secondary` (#4A4453) |
| `#B9A7D0` | 라벨·설명 텍스트 | `Brush.Text.Tertiary` (#6E6878) |
| `#8574A0` | 힌트·비활성·placeholder | `Brush.Text.Muted` (#8A8494, 본문 금지) |
| `#C44B9B` | 주 강조·CTA 배경 | `Brush.Accent` (#FF4D79, 로즈) |
| `#C44B9B`(텍스트로 쓰인 경우) | 강조 텍스트(흰 배경 위) | `Brush.Accent.Text` (#D6376A) |
| `#FF7B7B` | 오류·삭제 텍스트 | `Brush.Danger` (#D92D4E) |
| `#7BE6A0` | 성공 안내 텍스트 | `Brush.Success` (#128A76) |
| `#5A2B2B` | 위험 버튼 배경 | `Brush.Danger.Surface` (#FDE8EC) |
| `White`/`#FFFFFF`(Accent 위 텍스트) | 로즈/민트 위 텍스트 | `Brush.OnAccent` (#FFFFFF, Bold 대형만) |
| `White`/밝은색(일반 배경 위 텍스트) | 흰 배경 위 텍스트 | Ink 계열로 **반전** — `Brush.Text.Primary`(절대 흰색 유지 금지) |
| `Black`(촬영 배경) | 프리뷰 몰입 배경 | **유지** — `Brush.CaptureBg`(#111114, 예외적 다크). 라이트 치환 제외 |
| `#88000000`/`#66000000`(오버레이) | 팝업 스크림 | `Brush.Scrim` (#66241F2B) |
| (신규) 민트 포인트 필요 위치 | 비텍스트 강조·성공 아이콘 | `Brush.Accent2` (#37C9B0) — 텍스트엔 사용 금지 |

**치환 시 주의(라이트 회귀 방지):**
- 다크에서 "밝은 텍스트 on 어두운 배경"이던 모든 조합은 라이트에서 **Ink 텍스트 on 흰/연회색 배경**으로 뒤집힌다. 흰색 텍스트를 그대로 두면 흰 배경에 사라진다 — 반드시 반전.
- 로즈/민트 위 작은 텍스트, 흰 배경 위 옅은 회색 본문은 §2.3.1 대비표 위반이므로 금지.

## 부록 B. 참고 출처

- 색상 방향 확정: Direction A(라이트, 코튼 캔디) — 사용자 선택 2026-07-20, 오케스트레이터 지정 팔레트
- 대비/접근성 계산: WCAG 2.x 상대 휘도 공식(§2.3.1 실측값)
- 키오스크 UI·터치 타깃·자기설명 흐름: touchwall.us/blog/photo-booth-software-kiosk-public-events-guide
- 2026 모바일/UI 트렌드(미니멀·soft UI·라이트): designstudiouiux.com/blog/mobile-app-ui-ux-design-trends, muz.li/blog, orizon.co/blog
- 2026 고대비·가시성 우선(blur/저대비 지양) 관점: tubikstudio.com/blog/ui-design-trends-2026
- 셀프 포토부스 무드(포토이즘/인생네컷/포토그레이): instagram.com/photoism.kr 외(밝고 산뜻한 무드 참고)
- 벤치마크·프레임: `Example/BM.pdf`, `Example/result_frame2.jpg`, `Example/Frame.png`(프레임 콘텐츠는 다크이나 앱 UI 테마와 독립)
