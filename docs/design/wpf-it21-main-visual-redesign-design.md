# it21 설계 — 메인 화면·전역 내비게이션 시각 리디자인 + 창모드 최소 크기 완화

| 항목 | 값 |
|------|-----|
| 대상 | Windows 데스크톱(WPF, .NET 8) — `MCPhoto.sln` |
| 브랜치 | `main`(직접 작업) |
| 범위 | ① Home 화면 시각 리디자인 ② 벡터 아이콘 시스템 신설 ③ 상단바 재구성(홈/계정/설정) ④ 창모드 최소 크기 완화 + 반응형 |
| 비범위 | 웹 클라이언트(`webclient/`) — 파급 항목은 §13에 "웹 후속 정합 대상"으로 목록화만 한다. 색 팔레트 변경. 촬영 파이프라인·상태머신·권한 규칙 |
| 선행 문서 | [wpf-architecture](./wpf-architecture.md) · [analysis/13 클라이언트 동작 규격](../analysis/13-client-behavior-spec.md) · [web-fix 2026-08-01 Windows 디자인 정합](./web-fix-20260801-windows-visual-parity.md) |
| 작성 | 2026-08-05 |

---

## §0 개요

### 0.1 요구사항 원문 (축약 금지)

1. **메인 화면이 너무 단조롭다.** 색 조화 자체는 어울리지만 "Title / SubTitle 말고는 아무것도 없다"고 느낄 만큼 디자인 요소가 없다. **상용 수준 앱의 메인 화면처럼 세련되게** 바꿔라.
2. **설정 아이콘(톱니바퀴)이 너무 둥글둥글해서 설정임을 확실히 알 수 없다.** 유니코드 글리프 `⚙`(FontSize 22)를 쓰고 있어 폰트 폴백에 따라 뭉개진다. 설정임이 즉시 읽히도록 고쳐라.
3. **로그인 버튼을 텍스트가 아니라 통상적으로 많이 쓰는 아이콘으로.**
4. **홈 버튼은 고민이 필요하다.** 사용자 제안: "중앙 Title이 버튼이 되고, 다른 페이지로 넘어가면 작게 중앙 상단에 Title이 버튼이 되는 방식". 확정이 아니며 **"상용 서비스 앱들을 찾아보고 통상적인 방법으로 해결"** 하기를 원한다.
5. **창모드 최소 크기가 너무 크다.** `MinHeight="720" MinWidth="1280"`이 하드코딩돼 표시 모드와 무관하게 걸린다. 창모드일 때 더 작게 만들고, 좁아져서 공간이 부족하면 **H/V 스크롤 또는 반응형 UI**로 대응하라. **전체화면 키오스크 동작은 종전대로 유지**.

> 사용자 지시: "물어보지 말고 스스로 판단해서 리뷰까지 완벽히 마치라." → 이 문서는 되묻지 않고 **판정 + 근거**로 답한다.

### 0.2 문제와 취지

Home은 무인 부스의 **어트랙트(attract) 화면**이다. 손님이 처음 보는 화면이자, 유휴 복귀·세션 완료가 모두 되돌아오는 화면이다. 지금은 흰 배경 + 파스텔 원 2개 + `Text.Display` 타이틀 + `Text.H2` 부제 + CTA 1개가 전부다(`Views/HomeView.xaml` 전 24줄). 요소가 4개뿐이라 "여백이 넓은 세련됨"이 아니라 **"미완성"** 으로 읽힌다.

상단바는 그 화면 위에 얹히는 유일한 전역 UI인데, 세 버튼이 전부 **텍스트/유니코드 글리프 pill**이라 시각 언어가 본문과 구분되지 않는다. `⚙`는 폰트 폴백(Segoe UI Symbol → Segoe UI Emoji)에 따라 컬러 이모지로 렌더될 수도 있어 모양이 통제 불가다.

창 최소 크기는 별개 축이지만 같은 파일을 건드린다. 지금은 **전체화면에서도** `MinWidth=1280`이 걸려 1024×768 같은 작은 키오스크 패널에서 Maximized 창이 화면보다 넓어지는 잠재 결함이 있다.

### 0.3 판정 요약

| # | 쟁점 | 판정 | 근거 절 |
|---|------|------|---------|
| J1 | Home이 단조로운 **구조적** 원인 | 시각 계층이 **1단**(hero)뿐 — 정보 층·안내 층·브랜드 층이 없다. 요소를 늘리는 게 아니라 **층을 늘린다** | §3 |
| J2 | 홈 버튼 = 중앙 타이틀 버튼? | **부분 채택**. "타이틀이 홈 버튼이 된다"는 발상은 채택(로고=홈 관례). **중앙 배치는 기각**, **좌상단**에 배치하고 **눌리는 칩(홈 글리프 + 워드마크)** 으로 렌더 | §4 |
| J3 | 아이콘 소스 | **자체 저작 `PathGeometry`**. 아이콘 폰트·서드파티 아이콘셋(Material/Fluent) **미도입** — 라이선스 표기 의무·`THIRD-PARTY.md` 신설 회피 | §5 |
| J4 | 아이콘 전용 버튼의 라벨 | 상단바는 **툴팁 + `AutomationProperties.Name`**, 그리고 **Home 화면에 로그인 진입점을 명시적으로 하나 더 둔다**(터치에는 hover 툴팁이 없다 — NN/g) | §4.3·§7.4 |
| J5 | 계정 버튼 위치 | 좌 → **우측으로 이동**. 좌=내비게이션(브랜드/홈), 우=액션(설정·계정)이 상용 관례 | §6.1 |
| J6 | 창모드 최소 크기 | **800×600**(창모드 전용). 전체화면은 **하한 없음(0)** — Maximized가 패널 크기를 그대로 따른다 | §8.2 |
| J7 | 반응형 vs 스크롤 | **화면별 혼합**. Home만 브레이크포인트(1008) 기반 축소, 나머지는 기존 wrap/스크롤 + **모달 카드 상한 완화**로 처리 | §8.4 |
| J8 | 터치 규격(48/56) | 전체화면(키오스크)에서는 **불변**. 창모드 Compact에서도 **상단바·CTA는 56 유지** — Compact는 폭 800 이상에서만 발생하므로 축소 없이도 들어간다 | §8.5 |

### 0.4 설계의 핵 — "요소를 더하지 않고 층을 더한다"

Home을 화려하게 만드는 방법은 두 가지다. ① 요소를 많이 놓는다 ② **정보의 층(layer)을 만든다**. 무인 키오스크에서 ①은 오조작·산만으로 직결된다(kiosk UX 공통 지침). 이 설계는 ②를 택한다.

```
층 0 · 배경   : 파스텔 워시 + 소프트 셰이프 3개   (비상호작용)
층 1 · 브랜드 : 앱 마크 타일 + Display 타이틀 + 부제
층 2 · 행동   : 단일 CTA [촬영하기]              ← 유일한 주 액션(불변)
층 3 · 안내   : 3단계 흐름 스트립 (1 프레임 → 2 촬영 → 3 QR)  (비상호작용)
층 4 · 부가   : 게스트 전용 로그인 힌트 1줄       (게스트일 때만)
```

**주 액션은 여전히 1개다.** 늘어난 것은 "이 부스가 무엇을 해주는지"를 말하는 비상호작용 층이고, 이것이 상용 어트랙트 화면과 현재 화면의 실제 차이다.

---

## §1 검증된 사실 (verified facts — 전부 코드 직접 확인)

| # | 사실 | 근거 |
|---|------|------|
| F1 | Home 화면 전체가 24줄이고 요소는 Ellipse 2 + TextBlock 2 + Button 1이 전부다 | `src/MCPhoto.App/Views/HomeView.xaml:1-24` |
| F2 | `HomeViewModel`은 `StartCommand` 하나뿐이고 `AppShellViewModel`을 주입받는다 | `src/MCPhoto.App/ViewModels/HomeViewModel.cs:9-22` |
| F3 | 설정 버튼은 유니코드 글리프 `⚙` + `FontSize="22"` | `src/MCPhoto.App/MainWindow.xaml:45-51` |
| F4 | 계정 버튼은 `Content="{Binding AccountLabel}"` 텍스트 pill(`Button.Icon.Pill`), 홈 버튼은 `Content="⌂ 홈"` | `MainWindow.xaml:30-42` |
| F5 | 상단바는 `Height="72"`, `Background="Transparent"`, 3열(Auto/*/Auto). 좌=홈+계정, 우=설정 | `MainWindow.xaml:20-51` |
| F6 | `MinHeight="720" MinWidth="1280"`이 `Window`에 하드코딩돼 **표시 모드와 무관하게** 적용된다 | `MainWindow.xaml:9` |
| F7 | 전체화면 적용은 `WindowStyle.None + ResizeMode.NoResize + WindowState.Maximized`이고 기하는 건드리지 않는다 | `MainWindow.xaml.cs:55-59` |
| F8 | 표시 모드 판정은 순수 정책 `DisplayApplyPolicy.Decide(target, appliedMode)`가 소유하고, 모드가 같으면 **완전 무동작** | `src/MCPhoto.Core/Settings/DisplayApplyPolicy.cs:24-29` · `MainWindow.xaml.cs:47-81` |
| F9 | `IsTopBarVisible`은 `SessionStateMachine.IsTopBarVisible` = `Capture`·`Qr`에서만 숨김 | `src/MCPhoto.Core/Navigation/SessionStateMachine.cs:66-67` |
| F10 | `IsHome`/`IsSettings`는 `CurrentState` 비교이고 `[NotifyPropertyChangedFor]`로 통지된다 | `AppShellViewModel.cs:51-55, 95-98` |
| F11 | `AccountLabel`은 `CurrentUser?.Id ?? "로그인"`이고 `OnCurrentUserChanged`에서 통지된다 | `AppShellViewModel.cs:88-89, 145-160` |
| F12 | Home VM은 **홈 진입 때마다 새로 생성**된다(`CreateViewModel(AppState.Home) => GetRequiredService<HomeViewModel>()`) | `AppShellViewModel.cs:251-267` |
| F13 | 테마는 6파일이고 **각 딕셔너리가 자기 의존을 자체 병합**한다(형제 교차 참조 금지 규약) | `Themes/Theme.xaml:8-14` · `Brushes.xaml:9-14` · `Metrics.xaml:8-13` · `Typography.xaml:10-15` · `Controls.xaml:11-19` |
| F14 | 테마에 `Geometry`/`Path` 리소스는 **0건**이다(아이콘은 전부 유니코드 글리프) | `grep -rn "PathGeometry\|Geometry x:Key" src/MCPhoto.App --include=*.xaml` → 0 hit |
| F15 | `Touch.Min=48`, `Touch.CTA=56`, `Touch.IconBtn=56` | `Themes/Metrics.xaml:36-38` |
| F16 | `Button.Icon`은 56×56 pill, 배경 Transparent, hover/press만 배경 변경 | `Themes/Controls.xaml:185-212` |
| F17 | `Button.Icon.Pill`은 `MainWindow.xaml`에서만 쓰이고 `XamlResourceTests`의 필수 키 목록에 들어 있다 | `Controls.xaml:215-219` · `XamlResourceTests.cs:78` |
| F18 | 6개 화면이 상단바 오프셋으로 **`88`을 하드코딩**한다(`Margin`/`Padding` 상단값) | `AccountView:8` · `CutSelectView:14` · `FrameEditorView:11,24` · `FrameSelectView:15` · `ResultView:15,25` · `SettingsView:68` |
| F19 | `SettingsView`에 이미 **요소 폭 기반 반응형 폴백**이 있다(`TwoColMinWidth = 760`, `SizeChanged` 코드비하인드) | `Views/SettingsView.xaml.cs:8-33` |
| F20 | `UserMgmtView`는 관리자 전용이라 **터치 48px 규칙의 예외**를 문서화하고 셀 컨트롤을 38px로 쓴다 | `Views/UserMgmtView.xaml:8-16` 주석 |
| F21 | 프레임 피커 오버레이 카드가 `MinWidth="720" MaxWidth="1100" MaxHeight="620"`으로 고정돼 있다 | `Views/FrameEditorView.xaml:143-146` |
| F22 | `FrameSelectView`의 목록은 `HorizontalScrollBarVisibility="Auto"` + `VerticalScrollBarVisibility="Disabled"` | `Views/FrameSelectView.xaml:19-20` |
| F23 | `QrPopupView` 카드는 `Width="440"` 고정 + `Padding="48"`, 스크롤 래핑 없음 | `Views/QrPopupView.xaml:9-10` |
| F24 | `XamlResourceTests`는 ① 테마 필수 키 해석 ② 파일별 자체 StaticResource 해석 ③ View 소스에서 추출한 키의 테마 존재 ④ 바인딩 문자열·VM 멤버 정적 대조 — 4계열이다. **`MainWindow.xaml`·`HomeView.xaml`은 아직 대상이 아니다** | `tests/MCPhoto.Tests/XamlResourceTests.cs:57-541` |
| F25 | 컨버터는 전부 `src/MCPhoto.App/Converters/CommonConverters.cs` 한 파일에 있고 `App.xaml`에서 키로 인스턴스화된다 | `CommonConverters.cs` · `App.xaml:20-36` |
| F26 | `AppShellViewModel` 테스트는 관심사별 파일로 나뉜다(`AppShellOverlayReturnTests`·`AppShellPinGateTests`·`AppShellQrUsageTests`) | `tests/MCPhoto.Tests/` 목록 |
| F27 | `.cs` 소스는 UTF-8 **BOM 없음**(한글 주석 포함) — 프로젝트 관례 | 기존 파일 인코딩 |
| F28 | 검증 실측: `<PathGeometry x:Key FillRule="EvenOdd" Figures="…"/>`가 파싱되고, `Fill="{Binding Foreground, RelativeSource={RelativeSource AncestorType={x:Type ButtonBase}}}"` 스타일이 **버튼 Foreground(#FFFF4D79)를 실제로 따라간다** | 이 설계 작성 중 headless 렌더 검증(§5.4) |

---

## §2 미검증 가정 (open assumptions) — 검증 단계 매핑

| # | 가정 | 검증 단계 |
|---|------|-----------|
| A1 | 자체 저작 톱니바퀴 Geometry가 **24px 실사용 크기에서 톱니가 식별된다** | Step 1 (headless 렌더 테스트 + Step 8 실기 육안) |
| A2 | 새 상단바 3버튼이 **폭 800 창에서도 겹치지 않는다**(브랜드 칩 + 아이콘 2개) | Step 4 (수치 계산은 §6.3에 있음) · Step 8 실기 |
| A3 | 창모드 하한 **800×600에서 모든 화면의 주 액션이 잘리지 않는다**(스크롤 없이 도달 가능) | Step 7 (화면별 실측) · Step 8 |
| A4 | `Window.MinWidth/MinHeight`를 **런타임에 0으로 낮추면** 이미 Maximized인 창이 즉시 축소되지 않고 다음 레이아웃에서 정상 동작한다 | Step 6 (전체화면↔창모드 왕복 실기) |
| A5 | 브레이크포인트 트리거가 `ActualWidth` 바인딩 + 컨버터로 **재평가된다**(초기 0 → 실제 폭) | Step 5 (headless 레이아웃 테스트) |
| A6 | Home에 층을 4개로 늘려도 **유휴 경고 오버레이·버전 표기와 시각 충돌이 없다** | Step 8 실기 |
| A7 | 전체 테스트 베이스라인 **938건**이 그대로 통과한다(신규분 제외) | Step 9 |

---

## §3 현행 진단 — 무엇이 왜 단조로운가 (구조적 원인)

"요소가 적다"는 증상이지 원인이 아니다. 코드를 보면 원인은 4가지다.

### 3.1 시각 계층이 1단뿐이다

`HomeView.xaml:14-22`는 **하나의 중앙 정렬 `StackPanel`** 안에 타이틀·부제·버튼을 세로로 쌓는다. 화면 전체가 "중앙 블록 1개 + 배경"이므로 눈이 머무를 지점이 하나뿐이고, 시선 이동이 없으니 정보량이 실제보다 더 적게 느껴진다. 상용 어트랙트 화면은 최소 3층(브랜드 / 행동 / 안내)을 갖는다.

### 3.2 브랜드가 텍스트로만 존재한다

`Branding.AppName`이 `Text.Display`(64px Bold)로만 나온다. **앱 마크(로고 도형)가 없다.** 상용 앱의 첫 화면이 "세련돼 보이는" 이유의 상당 부분은 워드마크 옆/위의 **도형 마크**다. 도형이 없으면 아무리 폰트를 키워도 "제목이 큰 문서"로 읽힌다.

### 3.3 배경 장식이 "얹혀" 있고 구성에 참여하지 않는다

`HomeView.xaml:9-12`의 두 `Ellipse`는 좌상단·우하단 모서리에 음수 마진으로 걸쳐 있다. 화면 구성(중앙 블록)과 **공간적 관계가 없다** — 지우면 아무것도 안 바뀐다. 장식이 구성 요소가 되려면 hero 뒤에 깔리는 워시(wash)처럼 **중심을 감싸야** 한다.

### 3.4 상단바가 본문과 같은 시각 언어를 쓴다

`Button.Icon`/`Button.Icon.Pill`은 배경 Transparent에 텍스트만 있다(F16). Home처럼 흰 배경 위에서는 **버튼인지 라벨인지 구분되지 않는다.** 게다가 표시 내용이 `⌂ 홈`, `로그인`, `⚙` — 글리프 2개와 텍스트 2개가 섞여 있어 하나의 컨트롤 군으로 읽히지 않는다.

> `⚙`(U+2699)의 렌더 결과는 폰트 체인에 좌우된다. `Font.Primary`는 `Segoe UI, Malgun Gothic`이고 둘 다 U+2699를 갖지 않으므로 **폴백**(Segoe UI Symbol / Segoe UI Emoji)이 일어난다. Windows 11에서 Segoe UI Emoji가 잡히면 **컬러 이모지 톱니**가 나오고, 이것이 사용자가 말한 "너무 둥글둥글하다"의 실체다. `⌂`(U+2302)도 같은 문제를 갖는다. **글리프를 벡터로 바꾸는 것이 유일한 근본 해결이다.**

---

## §4 상용 패턴 조사와 홈 버튼 결론

### 4.1 조사 결과 (출처 명시)

| # | 관찰된 관례 | 출처 |
|---|-------------|------|
| C1 | 사용자는 **로고를 홈 링크로 기대**하며, 링크가 아니면 오히려 짜증을 낸다. 로고 자체에는 링크임을 알리는 표시가 없는데도 그렇다 | [A Logo As Home Button (Medium)](https://medium.com/@paulvddool/a-logo-as-home-button-7faae0ea3777) |
| C2 | "좌상단 로고"는 Nielsen 휴리스틱 #4(일관성과 표준)의 대표 사례로 꼽힌다. 표준을 따르면 학습성이 오르고 혼란이 준다 | [NN/g — Consistency and Standards](https://www.nngroup.com/articles/consistency-and-standards/) |
| C3 | 상단 내비게이션 바는 앱 이름·섹션 제목·로고를 표시하는 자리이며, **로고를 중앙에 두는 배치는 브랜드 정체성을 우선하는 선택으로 Home/Start 화면에 특히 어울린다** | [Mobbin — Top Navigation Bar](https://mobbin.com/glossary/top-navigation-bar) |
| C4 | Material Design 3 상단 앱바는 center-aligned/small/medium/large 4종이며, 실무 관례는 **홈에서 내비게이션 아이콘을 숨기고 하위 화면에서 노출**하는 것이다(공식 문서에 명시 조항은 없음) | [M3 — Top app bar guidelines](https://m3.material.io/components/app-bars/guidelines) |
| C5 | 키오스크 UX 지침은 **눈에 잘 띄는 Back/Home 버튼**을 좌절 방지의 필수 요소로 꼽는다. 터치 타깃 최소 44×44, 요소 간 5mm 패딩 | [FLYX — Kiosk UX/UI best practices](https://www.flyx.cloud/en/blog/effective-ux-ui-design-for-self-service-kiosks-best-practices-tips/) · [Wavetec](https://www.wavetec.com/blog/challenges-in-ux-design-of-self-service-kiosks/) · [KIOSK — Kiosk UI Design](https://kiosk.com/kiosk-ui/) |
| C6 | 키오스크는 **무동작 시 자동으로 홈으로 복귀**해야 한다(자체 타임아웃) | [Touchwall — Photo booth kiosk software guide](https://touchwall.us/blog/photo-booth-software-kiosk-public-events-guide/) |
| C7 | **아이콘 단독은 거의 항상 모호하다. 텍스트 라벨이 필요하고, hover로 라벨을 드러내는 방식은 상호작용 비용이 크고 터치 기기에서 통하지 않는다** | [NN/g — Icon Usability](https://www.nngroup.com/articles/icon-usability/) · [NN/g — Yes, Icons Need Text Labels](https://www.nngroup.com/videos/icon-text-labels/) |
| C8 | 아이콘 전용 버튼은 **이름이 확립된 아이콘**(Bold/Italic 등)이 아니면 툴팁이 필요하고, 접근 이름(aria-label / AutomationProperties.Name)은 **항상** 필요하다 | [Sara Soueidan — Accessible Icon Buttons](https://www.sarasoueidan.com/blog/accessible-icon-buttons/) · [Carbon — Tooltip accessibility](https://carbondesignsystem.com/components/tooltip/accessibility/) |
| C9 | Windows 앱 반응형 브레이크포인트는 **Small <640 / Medium 641–1007 / Large ≥1008**이며, 기준은 **화면 크기가 아니라 앱 창의 크기**다 | [MS Learn — Screen sizes and breakpoints](https://learn.microsoft.com/en-us/windows/apps/design/layout/screen-sizes-and-breakpoints-for-responsive-design) |

### 4.2 홈 버튼 — 결론

**판정: 사용자 제안을 "발상은 채택, 배치는 기각"으로 부분 채택한다.**

| 사용자 제안 요소 | 판정 | 근거 |
|------------------|------|------|
| "타이틀이 홈 버튼이 된다" | **채택** | C1·C2 — 로고=홈은 확립된 관례다. 지금의 `⌂ 홈` 텍스트 pill보다 브랜드 강화와 관례 부합 모두에서 낫다 |
| "홈에선 크게 중앙, 다른 화면에선 작게 상단" | **채택(변형)** | C3·C4 — Home에서만 큰 브랜드 락업을 보여주고, 하위 화면에서는 상단바에 축소 워드마크를 노출하는 전이는 실제 관례다. **단, 하위 화면에서의 위치는 중앙이 아니라 좌상단** |
| "중앙 상단에 배치" | **기각** | ① 하위 화면들은 이미 자기 제목(`프레임 선택`·`컷 선택`·`설정`…)을 콘텐츠 상단 중앙/좌측에 갖는다(F18 계열). 상단바 중앙에 브랜드를 또 두면 **제목이 두 개**가 된다 ② M3에서 center-aligned title은 "현재 화면의 제목" 슬롯이지 홈 링크 슬롯이 아니다(C4) ③ 좌상단은 C2가 말하는 표준 위치다 |
| "타이틀(순수 텍스트)이 그대로 버튼" | **기각** | C5·C7 — 키오스크는 **눈에 띄는** 홈 버튼을 요구하고, 터치에는 hover 힌트가 없다. 아무 장식 없는 텍스트는 첫 사용자에게 버튼으로 읽히지 않는다. → **홈 글리프 + 워드마크를 담은 눌리는 칩**으로 렌더한다 |

**최종 형태**

```
Home 화면        : 상단바 좌측 = 비어 있음.  브랜드는 화면 중앙 hero의 앱 마크 + Display 타이틀이 담당.
그 외 모든 화면  : 상단바 좌측 = [🏠 MCPhoto] 칩 (홈 글리프 + 워드마크, 배경 Brush.Surface, 56px 높이)
```

- **기존 계약 그대로**: 노출 조건은 지금과 동일한 `IsHome` 반전이다(`MainWindow.xaml:35`). VM 변경 없음.
- **홈 복귀 경로는 3중으로 유지된다**: ① 브랜드 칩 ② 각 화면의 [취소]/[메인으로] ③ 유휴 타임아웃 자동 복귀(C6, `AppShellViewModel:332-358`). 브랜드 칩이 유일 경로가 아니므로 "로고 어포던스가 약하다"는 위험이 손님을 가두지 않는다.

### 4.3 아이콘 전용 버튼의 라벨 — 터치 보정

C7이 이 설계에서 가장 무거운 제약이다. **툴팁은 터치에서 뜨지 않는다.** 그런데 요구 2·3은 상단바를 아이콘화하라고 한다. 충돌을 이렇게 해소한다.

| 버튼 | 라벨 전략 | 근거 |
|------|-----------|------|
| 브랜드-홈 | **워드마크가 곧 라벨**(항상 보이는 텍스트) | C7 충족 |
| 설정(톱니) | 아이콘 전용 + 툴팁 + `AutomationProperties.Name="설정"` | 톱니=설정은 C8이 말하는 "이름이 확립된" 소수 아이콘. 게다가 **설정은 손님용이 아니라 운영자용**이라 첫 사용자 발견성 요구가 낮다 |
| 계정 | 아이콘 전용 + 툴팁 + Automation 이름. **추가로 Home 화면 하단에 "로그인하고 내 프레임 쓰기" 텍스트 버튼을 둔다**(게스트일 때만) | 사람 아이콘도 준-보편이지만 손님 접점이라 보정이 필요하다. 라벨을 상단바에 되돌리는 대신 **공간이 남는 Home에 명시적 진입점**을 두어 발견성을 회복한다 — 요구 3(아이콘화)과 C7(라벨 필요)을 동시에 만족시키는 유일한 배치다 |

> 로그인 사용자는 계정 버튼이 **이니셜 아바타**(예: `D`)로 바뀐다. 이는 "누가 로그인돼 있는지"를 텍스트 pill 없이 전달하는 상용 표준(Google/YouTube 계열)이며, 종전 `AccountLabel` 텍스트가 하던 정보 전달을 대체한다.

---

## §5 아이콘 시스템 설계

### 5.1 소스 판정 — 자체 저작 Geometry

| 대안 | 판정 | 이유 |
|------|------|------|
| 유니코드 글리프(현행) | **기각** | §3.4 — 폰트 폴백에 모양이 좌우된다. 요구 2의 직접 원인 |
| 아이콘 폰트 번들(Segoe Fluent Icons 등) | **기각** | 폰트 파일 배포·라이선스 검토 필요. self-contained 단일 exe 배포에 자산이 하나 늘어난다 |
| 서드파티 아이콘셋 SVG(Material Symbols Apache-2.0 / Fluent MIT) | **기각** | 라이선스 자체는 허용적이지만 **표기 의무**가 생긴다. WPF 쪽에는 `THIRD-PARTY.md`가 아직 없다(웹만 있다) — 아이콘 4개를 위해 라이선스 관리 축을 신설하는 것은 비용 대비 손해 |
| **자체 저작 `PathGeometry` (채택)** | **채택** | 4개면 충분하고, 선 두께·톱니 개수·비율을 직접 통제할 수 있다(요구 2가 정확히 그것을 요구한다). 라이선스 의무 0 |

### 5.2 키 명명 규칙

```
Icon.<개념>        : Geometry 원자값        (예: Icon.Gear, Icon.Account, Icon.Home, Icon.Camera)
Icon.Glyph         : Path 렌더 스타일        (크기·정렬·Fill 상속을 한 곳에서 소유)
Button.TopBar[.*]  : 상단바 버튼 컨테이너 스타일
```

- 모든 Geometry는 **24×24 좌표계**로 저작한다(경계는 §5.3 표). 실제 표시 크기는 `Icon.Glyph`의 `Width/Height`가 정한다.
- 기존 키와 충돌 없음: 현재 테마에 `Icon.`·`Button.TopBar` 접두 키는 0건이다(F14, `grep`).

### 5.3 Path 데이터 (그대로 붙여 쓸 수 있는 확정값)

전부 `FillRule="EvenOdd"`다 — 톱니바퀴의 축 구멍과 카메라의 렌즈 구멍이 EvenOdd로 뚫린다.

| 키 | 의미 | 경계(실측) | 비고 |
|----|------|-----------|------|
| `Icon.Gear` | 설정 | `0.70,0.70 22.59×22.59` | 8치, 팁 반지름 11.3 / 뿌리 7.6 / 축 구멍 3.6. **치 깊이가 반지름의 33%** 라 24px에서도 톱니가 읽힌다 |
| `Icon.Account` | 계정·로그인 | `4.00,3.80 16.0×17.4` | 머리 원 r=4 (중심 12,7.8) + 어깨 돔 |
| `Icon.Home` | 홈 | `2.00,3.00 20.0×18.0` | 지붕 삼각 + 몸통 + 문 노치 |
| `Icon.Camera` | 앱 마크 | `2.00,4.00 20.0×16.0` | 바디 + 뷰파인더 융기 + 렌즈 구멍 |

```xml
<!-- Icon.Gear -->
Figures="M 19.45,10.48 L 23.2,10.53 A 11.3,11.3 0 0 1 23.2,13.47 L 19.45,13.52
         A 7.6,7.6 0 0 1 18.34,16.19 L 20.96,18.88 A 11.3,11.3 0 0 1 18.88,20.96
         L 16.19,18.34 A 7.6,7.6 0 0 1 13.52,19.45 L 13.47,23.2
         A 11.3,11.3 0 0 1 10.53,23.2 L 10.48,19.45 A 7.6,7.6 0 0 1 7.81,18.34
         L 5.12,20.96 A 11.3,11.3 0 0 1 3.04,18.88 L 5.66,16.19
         A 7.6,7.6 0 0 1 4.55,13.52 L 0.8,13.47 A 11.3,11.3 0 0 1 0.8,10.53
         L 4.55,10.48 A 7.6,7.6 0 0 1 5.66,7.81 L 3.04,5.12
         A 11.3,11.3 0 0 1 5.12,3.04 L 7.81,5.66 A 7.6,7.6 0 0 1 10.48,4.55
         L 10.53,0.8 A 11.3,11.3 0 0 1 13.47,0.8 L 13.52,4.55
         A 7.6,7.6 0 0 1 16.19,5.66 L 18.88,3.04 A 11.3,11.3 0 0 1 20.96,5.12
         L 18.34,7.81 A 7.6,7.6 0 0 1 19.45,10.48 Z
         M 15.6,12 A 3.6,3.6 0 1 1 8.4,12 A 3.6,3.6 0 1 1 15.6,12 Z"

<!-- Icon.Account -->
Figures="M 16,7.8 A 4,4 0 1 1 8,7.8 A 4,4 0 1 1 16,7.8 Z
         M 12,14 C 7.6,14 4,17.2 4,21.2 L 20,21.2 C 20,17.2 16.4,14 12,14 Z"

<!-- Icon.Home -->
Figures="M 12,3 L 2,12 L 5,12 L 5,21 L 10,21 L 10,15 L 14,15 L 14,21
         L 19,21 L 19,12 L 22,12 Z"

<!-- Icon.Camera -->
Figures="M 9.2,4 L 7.8,6 L 4.4,6 A 2.4,2.4 0 0 0 2,8.4 L 2,17.6
         A 2.4,2.4 0 0 0 4.4,20 L 19.6,20 A 2.4,2.4 0 0 0 22,17.6 L 22,8.4
         A 2.4,2.4 0 0 0 19.6,6 L 16.2,6 L 14.8,4 Z
         M 12,17 A 4.2,4.2 0 1 1 12,8.6 A 4.2,4.2 0 1 1 12,17 Z"
```

> ⚠️ 위 문자열을 `Figures` 속성에 넣을 때 **줄바꿈은 유지해도 무방**하다(mini-language는 공백 구분). 다만 `FillRule`은 `Figures` 문자열의 `F0` 접두가 아니라 **`PathGeometry.FillRule="EvenOdd"` 속성**으로 지정한다 — `Figures`는 `PathFigureCollection` 변환기를 쓰므로 `F0` 접두를 받지 않는다.

### 5.4 검증 실측 (설계 단계에서 이미 확인함 — A1의 절반이 닫혔다)

headless로 위 4개 Geometry를 파싱해 24/48/160px로 렌더한 결과, **24px에서 톱니가 개별 식별된다.** 또한 다음이 실측으로 확인됐다(F28):

| 확인 항목 | 결과 |
|-----------|------|
| `<PathGeometry x:Key="Icon.Gear" FillRule="EvenOdd" Figures="…"/>` 파싱 | 성공, `PathGeometry` 타입, `Bounds = 0.704,0.704,22.59,22.59` |
| `Icon.Glyph` 스타일의 `Fill="{Binding Foreground, RelativeSource={RelativeSource AncestorType={x:Type ButtonBase}}}"` | Button `Foreground=#FF4D79` → `Path.Fill = #FFFF4D79` (일치) |
| `Path.RenderSize` | `24,24` (스타일의 Width/Height 적용됨) |

### 5.5 어느 딕셔너리에 두는가 — 병합 구성 (교차 참조 금지 규약 준수)

**신설: `src/MCPhoto.App/Themes/Icons.xaml`**

```xml
<ResourceDictionary xmlns="…presentation" xmlns:x="…xaml"
                    xmlns:po="http://schemas.microsoft.com/winfx/2006/xaml/presentation/options"
                    mc:Ignorable="po" xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006">
    <!-- MergedDictionaries 없음 — 이 파일은 다른 딕셔너리의 키를 하나도 참조하지 않는다.
         (형제 딕셔너리 StaticResource 교차 참조 금지 규약: 참조가 0이면 자체 병합도 0) -->
    <PathGeometry x:Key="Icon.Gear"    po:Freeze="True" FillRule="EvenOdd" Figures="…" />
    <PathGeometry x:Key="Icon.Account" po:Freeze="True" FillRule="EvenOdd" Figures="…" />
    <PathGeometry x:Key="Icon.Home"    po:Freeze="True" FillRule="EvenOdd" Figures="…" />
    <PathGeometry x:Key="Icon.Camera"  po:Freeze="True" FillRule="EvenOdd" Figures="…" />
</ResourceDictionary>
```

- `po:Freeze="True"`로 동결한다 — 4개 Geometry를 다수 `Path`가 공유하므로 동결이 렌더·메모리 모두에 유리하고, 실수로 런타임 변경하는 경로를 차단한다.
- **`Icons.xaml`은 StaticResource를 0건 참조한다.** 따라서 `MergedDictionaries`가 필요 없고, 교차 참조 사고(창이 안 뜨는 그 사고)의 재발 표면이 아예 없다.

**`Themes/Theme.xaml` 병합 순서 변경**

```xml
<ResourceDictionary Source="Colors.xaml" />
<ResourceDictionary Source="Brushes.xaml" />
<ResourceDictionary Source="Typography.xaml" />
<ResourceDictionary Source="Metrics.xaml" />
<ResourceDictionary Source="Icons.xaml" />     <!-- 신규. 의존 없음 → 위치 자유, Controls 앞에 둔다 -->
<ResourceDictionary Source="Controls.xaml" />
```

**`Themes/Controls.xaml` 확장** — `Icon.Glyph` / `Button.TopBar` 계열은 여기 둔다.

- `Icon.Glyph`는 `Brush`·`Metrics` 키를 **참조하지 않는다**(Fill은 Binding, 크기는 리터럴 24). → Controls.xaml의 기존 자체 병합(Brushes·Metrics·Typography)에 **추가 병합 불필요**.
- `Button.TopBar` 계열은 `Brush.Surface`·`Brush.Surface.Hover`·`Brush.Surface.Press`·`Brush.Text.Primary`·`Radius.Pill`·`Touch.IconBtn`·`Font.Primary`를 쓴다 — **전부 Controls.xaml이 이미 자체 병합한 딕셔너리 안에 있다**(F13). 추가 병합 불필요.
- ❗ **`Button.TopBar` 계열이 `Icon.Gear` 등 Geometry를 참조하면 안 된다.** Geometry는 사용처(`MainWindow.xaml`)에서 `Path.Data`로 주입한다. 스타일이 Geometry를 참조하는 순간 Controls.xaml → Icons.xaml 교차 참조가 생기고, `Each_Theme_File_Resolves_Its_Own_StaticResource_References` 테스트가 즉시 실패한다. **이 규칙이 이번 이터레이션의 1급 함정이다.**

### 5.6 스타일 정의

```xml
<!-- Themes/Controls.xaml -->
<Style x:Key="Icon.Glyph" TargetType="Path">
    <Setter Property="Width" Value="24" />
    <Setter Property="Height" Value="24" />
    <Setter Property="Stretch" Value="Uniform" />
    <Setter Property="HorizontalAlignment" Value="Center" />
    <Setter Property="VerticalAlignment" Value="Center" />
    <!-- 버튼 Foreground를 따라간다 → disabled/hover 색 정책을 버튼 하나가 소유. 실측 확인(§5.4). -->
    <Setter Property="Fill"
            Value="{Binding Foreground, RelativeSource={RelativeSource AncestorType={x:Type ButtonBase}}}" />
</Style>
```

- 크기를 바꾸고 싶은 사용처는 인스턴스에서 `Width`/`Height`만 덮어쓴다(예: Home 앱 마크 44).
- `Stretch="Uniform"`이라 24×24 좌표계가 어떤 크기로도 정확히 맞춰진다. `Icon.Gear`의 경계는 0.70~23.30이므로 Uniform 스케일 후 **미세하게 커 보인다** — 상단바에서 톱니가 계정 아이콘보다 커 보이지 않도록 설정 버튼에서만 `Width="22" Height="22"`를 준다(§6.2 표에 반영).

---

## §6 상단바 재설계

### 6.1 배치 — 좌=내비게이션, 우=액션 (J5)

```
현행:  [⌂ 홈][AccountLabel]                                      [⚙]
       └── 좌측에 내비 + 액션이 섞여 있다                          └ 우측엔 설정만

개편:  [🏠 MCPhoto]                                         [👤][⚙]
       └ IsHome=false 에서만 노출(현행 게이트 그대로)         │    └ IsSettings=false 에서만
         Home에서는 좌측이 비고, 브랜드는 hero가 담당(§4.2)   └ 상시. 로그인 시 이니셜 아바타
```

계정을 우측으로 옮기는 근거는 §0.3 J5 — **좌=브랜드/내비게이션, 우=사용자·설정 액션**이 상용 관례다(C2·C3). 부수 효과로 좌측이 브랜드 전용이 되어 §4.2의 "로고=홈" 관례가 시각적으로도 성립한다.

상단바 골격은 유지한다: `Height="72"`, `Background="Transparent"`, 3열(`Auto`/`*`/`Auto`), `IsTopBarVisible` 게이트(F5·F9). **중앙 열(`*`)은 비워 둔다** — §4.2에서 기각한 "중앙 브랜드" 자리이며, 향후 화면 제목 슬롯으로 예약한다.

### 6.2 버튼 스펙

| 버튼 | 스타일 | 내용 | 크기 | 노출 조건 | Automation / ToolTip |
|------|--------|------|------|-----------|----------------------|
| 브랜드-홈 | `Button.TopBar.Brand` | `Path(Icon.Home)` 20 + `TextBlock(Branding.AppName)` FS16 SemiBold, 간격 10 | H56, `Padding="18,0"` | `IsHome` 반전 | `Name="홈으로"` / ToolTip "홈으로" |
| 계정 | `Button.TopBar` | 게스트: `Path(Icon.Account)` 24 · 로그인: 이니셜 `TextBlock` FS18 Bold on `Brush.Accent` 원 | 56×56 | 상시 | `Name="로그인 또는 계정"` / ToolTip = `AccountLabel` |
| 설정 | `Button.TopBar` | `Path(Icon.Gear)` **22** (§5.6 단서) | 56×56 | `IsSettings` 반전 | `Name="설정"` / ToolTip "설정" |

**`Button.TopBar` 계열 정의** (`Themes/Controls.xaml`) — 기존 `Button.Icon`을 `BasedOn`으로 상속해 hover/press·터치 규격을 재정의하지 않는다(회귀 표면 최소화).

```xml
<Style x:Key="Button.TopBar" TargetType="Button" BasedOn="{StaticResource Button.Icon}">
    <Setter Property="Background" Value="{StaticResource Brush.Surface}" />   <!-- §3.4: 본문과 구분 -->
</Style>
<Style x:Key="Button.TopBar.Brand" TargetType="Button" BasedOn="{StaticResource Button.TopBar}">
    <Setter Property="Width" Value="Auto" />
    <Setter Property="MinWidth" Value="{StaticResource Touch.IconBtn}" />
    <Setter Property="Padding" Value="18,0" />
</Style>
```

§3.4 진단대로 현행 `Button.Icon`은 배경이 Transparent라 흰 배경 위에서 버튼으로 읽히지 않는다. `Brush.Surface`(#F4F1F7)를 기본 배경으로 주어 **세 버튼이 하나의 컨트롤 군으로 보이게** 한다. hover(`Surface.Hover`)·press(`Surface.Press`) 트리거는 `Button.Icon`에서 그대로 상속된다.

> ❗ §5.5의 1급 함정 재확인: 위 스타일 어디에도 `Icon.Gear` 등 **Geometry 키를 참조하지 않는다**. Geometry는 `MainWindow.xaml`이 `Path.Data`로 주입한다.

### 6.3 폭 계산 — 800px 창에서 겹치지 않는가 (A2 해소)

| 요소 | 폭 |
|------|-----|
| 좌 여백 | 16 |
| 브랜드 칩 | `Padding 18` ×2 + 아이콘 20 + 간격 10 + 워드마크 "MCPhoto" FS16 SemiBold ≈ 78 → **약 144** |
| 중앙 여유 | (가변) |
| 계정 56 + 간격 8 + 설정 56 | 120 |
| 우 여백 | 16 |
| **합계(중앙 제외)** | **296** |

폭 800에서 중앙 여유는 **504px**다. 워드마크가 긴 브랜드명으로 교체되어도(`App.xaml.cs:68`이 런타임 교체) 여유가 크다. 안전장치로 워드마크에 `TextTrimming="CharacterEllipsis"` + `MaxWidth="220"`을 준다. → **A2 닫힘(계산). 실기 확인은 A-9.**

### 6.4 계정 팝오버 정렬 (필수 동반 수정)

현행 팝오버는 `PlacementTarget=TopBar` + `Placement=Bottom` + `HorizontalOffset="16"`이라 **좌측**에 열린다(`MainWindow.xaml:54-56`). 계정 버튼이 우측으로 가면 버튼과 팝오버가 화면 양끝으로 분리되어 조작 맥락이 끊긴다.

→ `PlacementTarget`을 **계정 버튼 자신**(`ElementName=AccountButton`)으로 바꾸고 `Placement="Bottom"`, `HorizontalOffset="-184"`, `VerticalOffset="8"`로 우측 정렬한다. 버튼을 기준으로 삼으면 상단바 폭 변화와 무관하게 항상 버튼 아래에 붙는다.

⚠️ **카드 폭을 `Width="240"`으로 고정한다.** `MinWidth`만 두면 계정 ID가 길 때 카드가 넓어져 오프셋 계산(`56 − 240 = -184`)이 깨지고 우측 정렬이 어긋난다. 긴 ID는 카드 안에서 `TextTrimming="CharacterEllipsis"`로 처리한다.

### 6.5 VM 변경 — 이니셜 1개만 추가

| 바인딩 | 상태 |
|--------|------|
| `IsHome` · `IsSettings` · `IsTopBarVisible` · `GoHomeCommand` · `OpenAccountCommand` · `OpenSettingsCommand` · `IsAccountPopupOpen` · `IsPower` · `LogoutCommand` · `OpenAccountManageCommand` · `OpenAdminToolsCommand` | **그대로** |
| `AccountLabel` | Content → **ToolTip으로 이전**(정보 손실 없음) |
| `IsLoggedIn` | **신규 사용처**: 계정 버튼 아이콘↔아바타 전환(`DataTrigger`) |
| `AccountInitial` | **신규 프로퍼티** |

```csharp
/// <summary>계정 버튼 아바타에 표시할 이니셜 1글자(대문자). 게스트는 빈 문자열. (it21 §6.2)</summary>
public string AccountInitial =>
    CurrentUser?.Id is { Length: > 0 } id ? id[..1].ToUpperInvariant() : string.Empty;
```

`OnCurrentUserChanged`(`AppShellViewModel.cs:145-160`)에 `OnPropertyChanged(nameof(AccountInitial));` 한 줄을 추가한다. **컨버터를 새로 만들지 않는다** — 순수 프로퍼티가 단위 테스트하기 쉽고(F26 관례), `CommonConverters.cs`(F25)를 늘리지 않는다.

---

## §7 Home 화면 신규 레이아웃

### 7.1 층 구조 (§0.4의 5층을 실제 배치로)

```
┌──────────────────────────────────────────────────────────────────────┐
│ (상단바 좌측 비어 있음 — §4.2)                              [👤][⚙]  │
│                                                                      │
│         ╭─────────╮                                                  │  층1 브랜드
│         │   📷    │  ← 앱 마크 타일 96×96, Radius.L, Accent.Soft     │
│         ╰─────────╯     내부 Path(Icon.Camera) 44, Fill=Accent        │
│                                                                      │
│                      MCPhoto            ← Text.Display 64            │
│                 self custom photobooth  ← Text.H2 Tertiary           │
│                                                                      │
│              ┏━━━━━━━━━━━━━━━━━━━━━━━━━┓                            │  층2 행동
│              ┃      촬영하기            ┃  H80 FS26 Shadow.Pop       │
│              ┗━━━━━━━━━━━━━━━━━━━━━━━━━┛                            │
│                                                                      │
│    ①─────────── ②─────────── ③───────────                          │  층3 안내
│    프레임 선택    촬영         QR로 받기      ← 흐름 스트립           │
│    원하는 틀을    카운트다운    사진·영상을                            │
│    고르세요       후 자동 촬영  폰으로                                │
│                                                                      │
│              로그인하고 내 프레임 쓰기  ← 게스트일 때만 (층4)         │
│                                                          v1.1.10     │
└──────────────────────────────────────────────────────────────────────┘
```

배경(층0)은 hero **뒤를 감싸는** 워시로 바꾼다(§3.3 진단). 기존 두 `Ellipse`의 음수 마진(`-120,-120` / `-100,-100`)을 걷어내고 중앙 블록과 공간 관계를 갖게 재배치한다.

⚠️ **단색 `Fill` + `Opacity` 조합은 쓰지 않는다.** 렌더 검증에서 원의 **경계선이 그대로 보여** "잘린 도형"으로 읽혔다 — §3.3에서 지적한 바로 그 증상이 형태만 바뀌어 남는다. `RadialGradientBrush`로 바깥을 알파 0까지 페이드시켜 경계를 없앤다. 종단 색은 **같은 색의 알파 0**이어야 한다(`Transparent`로 두면 흰/회색이 섞여 탁해진다).

| 셰이프 | 크기 | 그라디언트 | 배치 |
|--------|------|-----------|------|
| A | 880×620 | `Color.Accent.Soft` → `#00FFE7EE` | `Center`/`Top` `Margin="0,-230,0,0"` — hero 뒤를 감싼다 |
| B | 620×520 | `Color.Accent2.Soft` → `#00DFF6F1` | 좌하단 `Margin="-220,0,0,-180"` |
| C | 460×420 | `Color.Accent.Soft` → `#00FFE7EE` | 우상단 `Margin="0,-40,-120,0"` — 대각 균형 |

전부 `IsHitTestVisible="False"`. 그라디언트는 `Color.*`(브러시가 아니라 **색** 키)를 참조한다.

### 7.2 요소 명세

최상위 `StackPanel`은 `VerticalAlignment=Center` + `Margin="0,56,0,0"`이다 — Home에서도 우측 계정·설정 버튼은 떠 있으므로 상단 바(72)를 피해 내린다.

| 요소 | 정의 |
|------|------|
| 앱 마크 타일 | `Border` 96×96, `CornerRadius={StaticResource Radius.L}`, **`Background=Brush.Accent`**, `Effect=Shadow.Card`. 내부 `Path Data={StaticResource Icon.Camera}` `Width/Height=44` **`Fill=Brush.OnAccent`**.<br>⚠️ 타일을 `Accent.Soft`로 채우면 배경 워시와 같은 톤이라 **경계가 사라져 마크가 배경에 묻힌다**(렌더 검증에서 실제로 그랬다). 로즈로 채우고 글리프를 흰색으로 빼면 앱 아이콘처럼 읽힌다 |
| 워드마크 | `Text.Display` + `{DynamicResource Branding.AppName}` — **DynamicResource 유지 필수**(`App.xaml.cs:68`이 런타임 교체). `Margin="0,20,0,0"` |
| 부제 | `Text.H2`, `Brush.Text.Tertiary`, `Margin="0,6,0,40"` |
| CTA | `Button.Primary` 기반, `Height=80` `FontSize=26` **`MinWidth=320`** `Effect=Shadow.Pop`, `Command={Binding StartCommand}`. **텍스트만** — 아이콘을 넣지 않는다(층1 앱 마크와 카메라 도형이 중복되면 시선이 분산된다).<br>⚠️ **`Padding`으로 폭을 키울 수 없다** — §15 D-1 참조. 폭은 `MinWidth`가 정한다 |
| 흐름 스트립 | `Grid` 3열 균등, 각 셀: 번호 배지(`Ellipse` 32 `Accent.Soft` + `TextBlock` `Accent.Text` Bold 15) + 제목 `Text.Title` + 설명 `Text.Caption`. **`IsHitTestVisible="False"`** — 누를 수 있어 보이면 안 된다(키오스크 오조작 방지) |
| 게스트 로그인 힌트 | `Button.Ghost`, 문구 "로그인하고 내 프레임 쓰기", `Visibility={Binding IsGuest, Converter={StaticResource BoolToVis}}` |

**흐름 스트립을 카드(`Card` Border)로 감싸지 않는 이유**: 카드는 이 앱에서 "조작 가능한 컨테이너"로 일관되게 쓰인다(프레임 카드·설정 그룹). 비상호작용 안내를 카드로 만들면 어포던스가 거짓이 된다. 배경 없이 번호 배지 + 텍스트만 두고, 셀 사이를 `Brush.Divider` 1px 세로선으로 나눈다.

### 7.3 반응형 (J7 — Home만 브레이크포인트)

C9의 Windows 브레이크포인트(Large ≥1008)를 채택한다. **기준은 창 폭**이다.

| 구간 | 마크 타일 | 마크 글리프 | 워드마크 | CTA | 흐름 스트립 | 층4 |
|------|----------|------------|---------|-----|------------|-----|
| ≥1008 (Large) | 96 | 44 | FS 64 | H80 FS26 MinW320 | 3열 표시 | 표시 |
| <1008 (Compact) | 64 | 30 | FS 44 | H64 FS22 MinW260 | **숨김**(`Collapsed`) | 표시 |

- 흐름 스트립은 **안내**이므로 좁은 창에서 가장 먼저 접는다. 주 액션(CTA)과 브랜드는 끝까지 남는다 — J8(창모드에서도 CTA는 터치 규격 유지)과 정합.
- 구현: `HomeView.xaml.cs`에 `SizeChanged` 핸들러로 `VisualStateManager` 대신 **단순 프로퍼티 토글**을 쓴다. 근거 — 이 프로젝트에 이미 같은 패턴의 선례가 있다(`SettingsView.xaml.cs:8-33`, `TwoColMinWidth=760` 코드비하인드 폴백, F19). 새 메커니즘(VSM·컨버터 체인)을 도입하는 것보다 **선례를 따르는 편이 리뷰·회귀 비용이 낮다.**
- A5(브레이크포인트 초기 평가) 위험은 `SizeChanged`가 최초 레이아웃에서도 발화하므로 해소된다. `Loaded`에서도 한 번 호출해 이중 안전.

### 7.4 게스트 로그인 진입점 (J4 보정)

§4.3에서 확정한 대로, 상단바 계정 버튼을 아이콘 전용으로 만드는 대신 **Home에 텍스트 진입점을 하나 둔다**(터치에는 hover 툴팁이 없다 — C7).

`HomeViewModel` 확장:

```csharp
/// <summary>게스트 여부(진입 시점 스냅샷). Home VM은 홈 진입마다 새로 생성되므로 통지 불필요(F12). (it21 §7.4)</summary>
public bool IsGuest => _shell.IsGuest;

/// <summary>[로그인하고 내 프레임 쓰기]: 로그인 페이지로(오버레이). 셸의 기존 진입점을 재사용. (it21 §7.4)</summary>
[RelayCommand]
private Task Login() => _shell.NavigateToOverlayAsync(AppState.Login);
```

`IsGuest`에 통지가 없어도 되는 근거는 F12다 — 홈 진입마다 `HomeViewModel`이 새로 생성되므로 로그인 후 복귀하면 새 인스턴스가 최신 상태를 읽는다. (통지를 붙이려면 `_shell.PropertyChanged` 구독 + `IDisposable` 해제가 필요해 표면이 커진다.)

### 7.5 유휴 오버레이·버전 표기와의 충돌 (A6)

- 유휴 경고는 `Brush.Scrim` 전면 오버레이 + 중앙 카드다(`MainWindow.xaml:87-109`). Home 층 추가와 **겹칠 뿐 간섭하지 않는다**(오버레이가 위).
- 버전 표기는 우하단 `Margin="0,0,16,10"` + `IsHitTestVisible=False`다. 층4 게스트 힌트는 **중앙 정렬**이고 하단 여백 40 이상을 두므로 충돌하지 않는다. Compact(<1008)에서도 층3이 접히므로 세로 여유가 오히려 늘어난다.

---

## §8 창모드 최소 크기 · 반응형

### 8.1 현행 결함

`MinHeight="720" MinWidth="1280"`이 `Window`에 하드코딩돼 **표시 모드와 무관하게** 적용된다(F6). 그런데 이 값은 기본 창 크기(`WindowBounds` 기본 1280×720, `AppSettings.cs:22-23`)와 **같다** → 창모드에서 창을 1픽셀도 줄일 수 없다. 사용자가 체감한 "최소 크기가 너무 크다"의 실체다.

부수 결함: 전체화면은 `Maximized`(F7)인데 하한 1280이 남아 있어, **1024×768 같은 작은 키오스크 패널에서 Maximized 창이 화면보다 넓어진다.**

### 8.2 수치 결정 (J6)

| 모드 | MinWidth | MinHeight | 근거 |
|------|----------|-----------|------|
| 창모드 | **800** | **600** | C9 Medium 구간(641–1007)의 실용 하한. 800×600은 모든 화면의 주 액션이 스크롤 없이 도달 가능한 값(§8.4 실측) |
| 전체화면 | **0** | **0** | Maximized가 패널 크기를 그대로 따르게 한다. 작은 패널에서 창이 화면을 넘기는 결함(§8.1)도 함께 사라진다 |

### 8.3 적용 지점 — XAML 하드코딩 제거, 모드 분기에 편입

`MainWindow.xaml:9`의 `MinHeight`/`MinWidth`를 **삭제**하고, `ApplyDisplaySettings`의 기존 `switch`에 편입한다(`MainWindow.xaml.cs:47-81`).

```csharp
/// <summary>창모드 최소 크기(it21 §8.2). 전체화면은 하한 없음 — Maximized가 패널 크기를 따른다.</summary>
private const double WindowedMinWidth = 800;
private const double WindowedMinHeight = 600;

case DisplayApplyAction.Fullscreen:
    MinWidth = 0; MinHeight = 0;          // ← 추가(하한 해제가 Maximized보다 먼저)
    WindowStyle = WindowStyle.None;
    ResizeMode = ResizeMode.NoResize;
    WindowState = WindowState.Maximized;
    break;

case DisplayApplyAction.WindowedRestoreGeometry:
    MinWidth = WindowedMinWidth; MinHeight = WindowedMinHeight;   // ← 추가
    WindowStyle = WindowStyle.SingleBorderWindow;
    …
```

- **순서 주의**: 하한 해제를 `Maximized` **이전**에 둔다. 반대 순서면 하한이 살아 있는 상태로 Maximized가 적용돼 작은 패널에서 한 프레임 동안 창이 화면을 넘긴다.
- `DisplayApplyAction.None`(모드 불변)은 **손대지 않는다** — it16 §7.2의 "설정 저장이 창 기하를 건드리지 않는다"를 유지한다(F8).
- A4(런타임 하한 변경) 위험: 창모드→전체화면 전환 시 하한을 낮추는 방향이라 이미 큰 창이 강제 축소될 일이 없다. 반대(전체화면→창모드)는 `WindowBounds` 복원값(≥800×600 저장분)이 적용되므로 안전. 저장값이 하한보다 작을 수 있는 경로(설정 파일 수동 편집)는 WPF가 하한으로 클램프한다.

### 8.4 화면별 대응 (J7 — 혼합 전략)

800×600에서 각 화면이 어떻게 되는지 실측 기준 판정이다. **세로 600 − 상단바 72 − 상하 여백 = 약 470px**이 콘텐츠 가용 높이다.

| 화면 | 구조 | 800×600 판정 | 조치 |
|------|------|-------------|------|
| Home | 중앙 정렬 + 브레이크포인트 | Compact 적용 시 층1+2+4 = 약 330px | **§7.3 반응형** |
| Capture | `Viewbox` | 자동 스케일 | 없음 |
| FrameSelect | 3행 + 가로 `ListBox`(H스크롤 Auto, F22) | 카드가 잘리면 가로 스크롤 | 없음 |
| Settings | `ScrollViewer` V-Auto + 폭 기반 2열↔1열 폴백(F19) | 이미 반응형 | 없음 |
| CutSelect / Result / Guide / Done | 고정 열 + 여백 | 압박되나 주 액션 도달 가능 | 없음 |
| FrameEditor | 2열 `* + 320` + 배너행 `MinHeight=88` | 캔버스가 좁아지지만 조작 가능 | 없음 |
| **FrameEditor 피커 오버레이** | `MinWidth=720` `MaxWidth=1100` `MaxHeight=620` (F21) | **`MinWidth=720`이 800폭에서 좌우 여백 40씩을 잡아먹어 빠듯하고, `MaxHeight=620`이 창 높이 600을 넘는다** | **수정**: `MinWidth` 720 → 560, `MaxHeight` 620 → `Binding ActualHeight` 기반 대신 단순히 620 유지하되 **`ScrollViewer`로 래핑** |
| **QrPopup** | `Width=440` 고정 + `Padding=48` (F23) | 폭은 문제없으나 세로 여유 부족 가능 | **수정**: 카드를 `ScrollViewer`로 래핑(`VerticalScrollBarVisibility=Auto`) |

→ **모달 카드 2곳만 상한 완화/스크롤 래핑**하고, 나머지는 기존 wrap·스크롤 메커니즘에 위임한다. 셸 전역에 `ScrollViewer`를 두는 방식은 **채택하지 않는다** — 상단바 고정과 충돌하고, `Viewbox` 기반 Capture에 불필요한 스크롤 표면을 만든다.

### 8.5 터치 규격 (J8)

- 전체화면(키오스크·손님 접점): `Touch.Min=48` / `Touch.CTA=56` / `Touch.IconBtn=56` **전부 불변**(F15).
- 창모드 Compact: 상단바 3버튼과 CTA는 **56 유지**. 800폭에서 상단바 소요는 296px(§6.3)이라 축소 없이 들어간다. Home CTA만 H80→H64로 낮추는데, 64 > 56이므로 규격 위반이 아니다.
- 창모드는 운영자 점검 용도이고 손님은 전체화면으로만 접한다 — C5의 44×44 하한을 모든 구간에서 만족한다.

---

## §9 변경 파일 목록

| 파일 | 변경 | 절 |
|------|------|-----|
| `src/MCPhoto.App/Themes/Icons.xaml` | **신규**. `PathGeometry` 4종(`Icon.Gear/Account/Home/Camera`), `po:Freeze`, MergedDictionaries 없음 | §5.3·§5.5 |
| `src/MCPhoto.App/Themes/Theme.xaml` | 병합 목록에 `Icons.xaml` 추가(Metrics 뒤, Controls 앞) | §5.5 |
| `src/MCPhoto.App/Themes/Controls.xaml` | `Icon.Glyph`, `Button.TopBar`, `Button.TopBar.Brand` 추가. **Geometry 참조 금지** | §5.6·§6.2 |
| `src/MCPhoto.App/MainWindow.xaml` | `MinWidth/MinHeight` 삭제. 상단바 재배치(좌 브랜드 / 우 계정·설정) + 벡터 아이콘 + ToolTip/Automation. 계정 팝오버 우측 정렬 | §6 |
| `src/MCPhoto.App/MainWindow.xaml.cs` | 모드별 최소 크기 상수·적용(하한 해제를 Maximized 앞에) | §8.3 |
| `src/MCPhoto.App/AppShellViewModel.cs` | `AccountInitial` 프로퍼티 + `OnCurrentUserChanged` 통지 1줄 | §6.5 |
| `src/MCPhoto.App/Views/HomeView.xaml` | 전면 재작성(4층 구조) | §7 |
| `src/MCPhoto.App/Views/HomeView.xaml.cs` | `SizeChanged`/`Loaded` 브레이크포인트 토글 | §7.3 |
| `src/MCPhoto.App/ViewModels/HomeViewModel.cs` | `IsGuest`, `LoginCommand` | §7.4 |
| `src/MCPhoto.App/Views/FrameEditorView.xaml` | 피커 오버레이 `MinWidth` 720→560, `ScrollViewer` 래핑 | §8.4 |
| `src/MCPhoto.App/Views/QrPopupView.xaml` | 카드 `ScrollViewer` 래핑 | §8.4 |
| `tests/MCPhoto.Tests/XamlResourceTests.cs` | 검증 추가(§10.1) | §10 |
| `tests/MCPhoto.Tests/AppShellAccountInitialTests.cs` | **신규**. `AccountInitial` 단위 테스트(F26 관례: 관심사별 파일) | §10.2 |
| `docs/analysis/13-client-behavior-spec.md` · `11-exe-app-features.md` · `docs/design/wpf-architecture.md` | 상단바·Home·아이콘 시스템·창 하한 규격 반영 | — |

**손대지 않는 것**: `DisplayApplyPolicy`, `AppSettings`, `SessionStateMachine`, `CommonConverters.cs`, 촬영 파이프라인, `webclient/`.

---

## §10 테스트 계획

### 10.1 XamlResourceTests 확장 (기존 4계열 관례 준수, F24)

| # | 테스트 | 겨냥하는 회귀 |
|---|--------|--------------|
| T1 | `Theme_Loads_And_Core_Keys_Resolve`의 `required`에 `Icon.Gear/Account/Home/Camera`, `Icon.Glyph`, `Button.TopBar`, `Button.TopBar.Brand` 추가 | 키 누락·병합 순서 오류 |
| T2 | `Each_Theme_File_Resolves_Its_Own_StaticResource_References`에 `Icons.xaml` `InlineData` 추가 | Icons.xaml이 교차 참조를 들이는 순간 실패(§5.5 1급 함정) |
| T3 | `Item1a_View_StaticResource_Keys_Resolve_In_Theme`에 `HomeView.xaml` 추가 | 재작성된 Home의 테마 키 미해결 |
| T4 | **신규** `MainWindow_StaticResource_Keys_Resolve_In_Theme` (기존 `PinPromptWindow` 테스트와 동형) | 셸 재작성 시 키 미해결 → **창이 안 뜨는 사고** |
| T5 | **신규** `Icon_Geometries_Resolve_As_Frozen_Geometry` — 4종이 `Geometry`로 해석되고 `IsFrozen`이며 `Bounds`가 24×24 좌표계 안(±1) | 잘못된 Path 데이터·`po:Freeze` 누락 |
| T6 | **신규** `Gear_Icon_Has_Discernible_Teeth` — `Icon.Gear.Bounds`의 폭/높이가 축 구멍 지름(7.2)의 **3배 이상**이고, 팁 반지름(11.3)×2에 근접(±0.5) | **R2 재발**: 톱니가 사라져 둥근 원으로 퇴화하면 Bounds가 줄어 실패 |
| T7 | **신규** `TopBar_Icon_Buttons_Have_Accessibility_Labels` — 상단바 3버튼 각각에 `AutomationProperties.Name`과 `ToolTip`이 모두 존재(소스 정적 검사) | 아이콘 전용화로 라벨 소실(C7·C8) |
| T8 | **신규** `TopBar_Home_Button_Is_Gated_By_IsHome` — 브랜드 칩에 `IsHome` + `InverseBoolToVis` 유지 | 홈에서 홈 버튼이 뜨는 어포던스 거짓말 |
| T9 | **신규** `MainWindow_Has_No_Hardcoded_Minimum_Size` — `MainWindow.xaml`에 `MinWidth=`/`MinHeight=`가 **Window 요소에 없고**, `MainWindow.xaml.cs`에 `WindowedMinWidth = 800` / `WindowedMinHeight = 600` 상수가 있으며 `Fullscreen` 분기에 `MinWidth = 0`이 있다 | R5 회귀(하한이 XAML로 되돌아감) + §8.3 순서 규칙 |
| T10 | **신규** `HomeView_Guest_Login_Hint_Is_Gated_By_IsGuest` + `{Binding StartCommand}`·`{Binding LoginCommand}` 존재, VM에 동명 멤버 존재(F24 4계열) | 층4 게이트 소실(로그인 사용자에게 로그인 버튼 노출) |

T6·T7·T9는 소스 텍스트/Bounds 검사다. 이 프로젝트는 이미 동일 방식을 쓴다(`FrameEditor_LocalOnly_Banner_Is_Gated_By_IsCreateMode`, `FrameSelectView_Waiting_Bindings_Exist_On_Vm`).

### 10.2 VM 단위 테스트 (신규 파일, F26 관례)

`AppShellAccountInitialTests`: ① 게스트 → `""` ② `devmcjo` → `"D"` ③ 소문자 ID 대문자화 ④ 로그인/로그아웃 시 `PropertyChanged(AccountInitial)` 발행.

`HomeViewModelTests`(기존 파일이 있으면 확장): `IsGuest` 반영, `LoginCommand`가 `AppState.Login`으로 전이.

### 10.3 회귀 기준

- 베이스라인 **938건 + 신규분** 전부 통과(A7).
- 빌드 경고 증가 0(기존 xUnit1031 1건 외).

---

## §11 함정 목록 (구현자가 반드시 지킬 것)

| # | 함정 | 결과 | 방어 |
|---|------|------|------|
| P1 | `Button.TopBar` 스타일이 `Icon.Gear`를 `StaticResource`로 참조 | Controls→Icons 교차 참조 → **창이 안 뜬다** | Geometry는 사용처에서 `Path.Data`로 주입. T2가 검출 |
| P2 | `Branding.AppName`을 `StaticResource`로 바꿈 | `App.xaml.cs:68`의 런타임 브랜딩 교체가 무효화 | **`DynamicResource` 유지** (Home 워드마크·브랜드 칩 둘 다) |
| P3 | §8.3에서 `MinWidth=0`을 `Maximized` **뒤**에 둠 | 작은 패널에서 한 프레임 창이 화면 초과 | 순서 고정. T9가 검출 |
| P4 | 계정 팝오버 `PlacementTarget`을 `TopBar`로 남겨둠 | 버튼은 우측, 팝오버는 좌측으로 분리 | `ElementName=AccountButton`으로 변경(§6.4). 실기 A-8 |
| P5 | 흐름 스트립을 `Card`로 감싸거나 hover 효과 부여 | 누를 수 있어 보이는데 안 눌림(키오스크 오조작) | `IsHitTestVisible=False`, 카드 미사용(§7.2) |
| P6 | `DisplayApplyAction.None` 분기에 Min 설정 추가 | it16 §7.2 "설정 저장이 창 기하 불변" 위반 | None은 손대지 않는다(§8.3) |
| P7 | `HomeViewModel.IsGuest`에 통지를 붙이려다 `_shell` 구독 + 미해제 | 이벤트 누수 | 스냅샷으로 충분(F12 근거, §7.4) |

---

## §12 수용 기준 (실기 확인)

| ID | 항목 | 기대 |
|----|------|------|
| A-1 | 앱 시작(전체화면) | Home에 앱 마크·워드마크·부제·CTA·흐름 스트립이 층으로 보이고 "미완성" 인상이 사라진다 |
| A-2 | 설정 아이콘 | **톱니 8개가 육안으로 개별 식별된다.** 폰트·이모지 폴백 흔적 없음 |
| A-3 | 계정 버튼 (게스트) | 사람 아이콘. 호버 툴팁 "로그인". 클릭 시 로그인 페이지 |
| A-4 | 계정 버튼 (로그인) | 로즈 원 + 이니셜 1글자. 툴팁 = 계정 ID. 클릭 시 팝오버 |
| A-5 | 하위 화면 좌상단 | `[🏠 MCPhoto]` 칩 노출, 클릭 시 홈 복귀. **Home에서는 미노출** |
| A-6 | 게스트 Home | "로그인하고 내 프레임 쓰기" 노출. 로그인 상태에서는 미노출 |
| A-7 | 촬영·QR 화면 | 상단바 숨김 유지(F9) |
| A-8 | 계정 팝오버 | 우측 계정 버튼 **바로 아래**에 정렬 |
| A-9 | 창모드 축소 | **800×600까지 축소**되고 상단바 3버튼이 겹치지 않는다. Home은 흐름 스트립이 접히고 CTA는 남는다 |
| A-10 | 전체화면 ↔ 창모드 왕복 | 전환이 즉시 반영되고 창 위치·크기가 튀지 않는다(it16 회귀 없음) |
| A-11 | 프레임 편집기 피커 / QR 팝업 (800×600) | 카드가 잘리지 않고 스크롤로 전체 접근 가능 |
| A-12 | 유휴 경고(2분) | Home 층 추가와 시각 충돌 없음. 카운트다운·복귀 정상 |

---

## §13 웹 후속 정합 대상 (이번 범위 아님)

`web-fix-20260801-windows-visual-parity.md`로 웹이 Windows에 맞춰진 이력이 있다. 이번 변경으로 벌어지는 격차만 기록한다. **웹 코드는 건드리지 않는다**(사용자가 "windows app 위주" 명시).

| # | 항목 | 웹 후속 |
|---|------|---------|
| W1 | Home 4층 구조(앱 마크·흐름 스트립·게스트 로그인 힌트) | 웹 Home 동일 이식 |
| W2 | 아이콘 4종 벡터화 | 동일 Path 데이터를 SVG로 이식(좌표계 24×24 공유 — §5.3 값 그대로 재사용 가능) |
| W3 | 상단바 배치(좌 브랜드 칩 / 우 계정·설정) | 웹 앱바 재배치 |
| W4 | 로그인 시 이니셜 아바타 | 웹 동일 규약 |
| W5 | 계정 팝오버 우측 정렬 | 웹 동일 |
| W6 | 상단바 버튼 배경(`Brush.Surface`)으로 컨트롤 군 구분 | 웹 동일 토큰 적용 |

창모드 최소 크기(§8)는 데스크톱 창 개념이므로 웹 파급 없음.

---

## §14 구현 순서

1. **`Icons.xaml` 신규 + 병합 배선** → T1·T2·T5·T6 먼저 통과시킨다(교차 참조 사고를 초기 차단)
2. `Controls.xaml` 스타일 3종 (P1 준수)
3. `AppShellViewModel.AccountInitial` + 단위 테스트(§10.2)
4. `MainWindow.xaml` 상단바 재배치 + 팝오버 정렬 → T4·T7·T8
5. `MainWindow.xaml.cs` 모드별 최소 크기 (P3 순서) → T9
6. `HomeView.xaml` 전면 재작성 + `HomeViewModel` 확장 → T3·T10
7. `HomeView.xaml.cs` 브레이크포인트
8. 모달 2곳 스크롤 래핑(§8.4)
9. 빌드 + 전체 테스트(938 + 신규)
10. 문서 3종 갱신

---

## §15 구현 중 확정된 사항 (설계 시점에 알 수 없었던 것)

이 절은 **렌더 검증으로 드러난 사실**과 그에 따른 설계 이탈을 기록한다. 앞 절들은 이미 이 결론을 반영해 갱신했으므로, **§1~§14를 그대로 따르면 된다.** 이 절은 "왜 그렇게 됐는지"의 근거다.

### D-1 · `Button.Primary` 계열은 `Padding`을 무시한다 (기존 결함, 이번 범위 밖)

`Controls.xaml`의 `Button.Primary`/`Secondary`/`Ghost`/`Danger` 템플릿은 전부 이 형태다.

```xml
<Border x:Name="Bd" …>
    <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
</Border>
```

`ContentPresenter`에 **`Margin="{TemplateBinding Padding}"`이 없다.** 따라서 스타일의 `Padding` Setter도, 인스턴스의 `Padding="72,0"`도 렌더에 전혀 반영되지 않는다. 종전 Home CTA가 `Padding="72,0"`을 주고도 좁게 나왔던 이유이며, **메인 화면이 빈약해 보인 원인 중 하나**다.

- **이번 조치**: Home CTA만 `MinWidth`로 폭을 지정한다(지역적·안전).
- **하지 않은 것**: 템플릿 수정. `ContentPresenter`에 `Margin`을 걸면 **앱 전체의 모든 버튼 폭이 한꺼번에 넓어진다** — 기존 화면들은 Padding이 무시되는 상태로 레이아웃이 맞춰져 있어 회귀 범위가 이번 이터레이션을 넘는다.
- **후속 과제**: 별도 이터레이션에서 템플릿을 바로잡고 전 화면 버튼 폭을 재검수할 것. 그때까지 **새 코드에서 `Button.*` 계열에 `Padding`으로 폭을 조절하려 하지 말 것**(조용히 무시된다).

### D-2 · 단색 원 배경은 경계선이 보인다 → 방사형 워시

첫 렌더에서 `Fill` + `Opacity` 방식의 배경 원이 **선명한 경계선**을 그려 hero를 가로질렀다. §3.3이 지적한 "잘린 도형"이 형태만 바꿔 재현된 것이다. `RadialGradientBrush`(종단 = 같은 색 알파 0)로 교체해 해결했다 → §7.1 표가 확정값이다.

### D-3 · 앱 마크 타일은 배경 워시와 같은 톤이면 사라진다

`Accent.Soft` 타일 + `Accent` 글리프 조합은 배경 워시(`Accent.Soft`) 위에서 타일 경계가 소실됐다. **로즈 채움 + 흰 글리프**로 반전해 앱 아이콘처럼 읽히게 했다 → §7.2 확정.

### D-4 · 팝오버 오프셋은 카드 폭 **고정**을 전제한다

`HorizontalOffset`은 상수이므로 카드 폭이 가변이면 정렬이 깨진다. 카드를 `Width="240"`으로 고정하고 오프셋을 `-184`로 확정했다 → §6.4.

### D-5 · 렌더 검증 방법 (다음에도 이렇게 확인할 것)

XAML은 **빌드·단위 테스트를 다 통과하고도 시각적으로 틀릴 수 있다.** D-1~D-3은 전부 테스트가 아니라 눈으로 잡았다. 다음 절차를 권장한다.

1. 테스트 프로젝트에 임시 `[Fact]`를 추가한다(리뷰 후 삭제).
2. STA 스레드에서 `Application` 인스턴스를 만들고 `Theme.xaml`을 `Application.Current.Resources`에 병합한다. `App.xaml`은 `Application` 정의라 `ResourceDictionary`로 로드할 수 없으므로 **뷰가 쓰는 App 키(`Branding.*`, 컨버터)만 수동 등록**한다.
3. 대상 `UserControl`을 `Border`에 담아 `Measure`/`Arrange`/`UpdateLayout` 후 `RenderTargetBitmap` → `PngBitmapEncoder`로 저장한다.
4. Large(1280×720)와 Compact(900×600) 두 크기를 뽑아 눈으로 비교한다. 아이콘은 실사용 크기(22px)와 확대(160px)를 함께 뽑아야 톱니 식별성을 판단할 수 있다.

### D-6 · 최종 검증 결과

| 항목 | 결과 |
|------|------|
| 빌드 | 오류 0, 경고 1(기존 `xUnit1031`, 이번 변경과 무관) |
| 테스트 | **958건 통과 / 실패 0** (베이스라인 938 + 신규 20) |
| 기어 아이콘 `Bounds` | `0.70, 0.70, 22.59 × 22.59` — 팁 지름 22.6 설계값과 일치. 22px 실사용 크기에서 톱니 8개 식별 확인 |
| `Icon.Glyph` Fill 바인딩 | 버튼 `Foreground=Red` → `Path.Fill=Red` 실측 확인(투명 렌더 위험 해소) |
| 미확인(실기 필요) | §12 수용 기준 A-1~A-12 전부 — 특히 A-9(창모드 800×600 축소), A-10(모드 왕복), A-11(모달 스크롤) |
