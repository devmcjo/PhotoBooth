# web-fix (2026-08-01) · 웹 클라이언트를 Windows 앱과 거의 동일한 디자인으로

| 항목 | 값 |
|------|-----|
| 문서 | 사용자 실사용 이슈 **②**(전체 배경·디자인 컨셉이 Windows 앱과 많이 다르다)의 진단 + 정합 설계 |
| 이슈 ①③④ | 별 문서 → [web-fix-20260801-login-fullscreen-camera](./web-fix-20260801-login-fullscreen-camera.md) |
| 작성 | `js-architect` (2026-08-01) |
| 다음 단계 | `js-developer` → `js-code-reviewer` |
| **진실원** | `src/MCPhoto.App/Themes/*.xaml`(팔레트·타이포·컨트롤) + `src/MCPhoto.App/Views/*.xaml`(레이아웃) |
| 검증 수단 | **스크린샷을 찍을 수 없으므로 이 문서의 값 대조표가 검증 수단이다.** 리뷰어는 표의 "목표" 열과 코드를 대조한다 |

---

## 0. 계획 헤더 — 검증된 사실 / 미검증 가정

### 0.1 검증된 사실 (verified facts)

| # | 사실 | 근거 |
|---|------|------|
| **W-1** | **Windows 앱은 라이트 테마 전용이다.** 배경 `#FFFFFF`, 잉크 텍스트 `#241F2B`, 로즈 강조 `#FF4D79`("코튼 캔디" 라이트 팔레트, Direction A) | `src/MCPhoto.App/Themes/Colors.xaml` 전량 |
| **W-2** | **웹은 다크 우선이다.** `color-scheme: dark light` + `--bg:#0e0e12`가 기본이고 라이트는 미디어 쿼리 오버라이드다 | `webclient/src/ui/theme/tokens.css:11,14,46-56` |
| **W-3** | **웹의 라이트 모드는 지금 깨져 있다.** `main.css:6-7`이 `:root`에 `--bg`·`--fg`를 다시 정의하는데, `main.tsx:23`(tokens) → `:24`(main) 순서라 **뒤에 오는 main.css가 tokens.css의 라이트 값을 이긴다**. 결과: 라이트 모드에서 `--bg-elevated:#ffffff` 카드 위에 `--fg:#f4f4f7` 글자가 올라간다 | `main.css:5-9` · `tokens.css:46-56` · `main.tsx:23-24` |
| **W-4** | 그 중복은 **의도된 상태가 아니다** — `main.css:1-3` 주석이 "Step 1 최소 스타일. Step 4에서 tokens.css로 대체된다"고 적혀 있으나 대체되지 않았다 | `main.css:1-3` |
| **W-5** | Windows 팔레트는 **그라데이션을 하나도 쓰지 않는다.** 전부 `SolidColorBrush`다 | `Themes/Brushes.xaml` 전량 (`LinearGradientBrush` 0건) |
| **W-6** | 웹 CSS에도 그라데이션이 **0곳**이다(`linear-gradient`/`radial-gradient`/`background-image` 전무) | `webclient/src/**/*.css` 14파일 전수 |
| **W-7** | **Home 화면에 파스텔 장식 원 2개가 있다** — 이것이 사용자가 말한 "전체 배경"의 실체다. 웹에는 없다 | `Views/HomeView.xaml:9-12` — `Ellipse 360×360 Fill=#FFE7EE Opacity=.6 (좌상단 Margin -120,-120)` + `Ellipse 300×300 Fill=#DFF6F1 Opacity=.6 (우하단 Margin 0,0,-100,-100)` |
| **W-8** | 웹 CSS 커스텀 프로퍼티는 **22개**뿐이고, WPF 색 토큰은 **34개** + 타이포 8 + 메트릭 17이다. 대응이 없는 토큰이 압도적으로 많다 | `tokens.css` vs `Colors/Typography/Metrics.xaml` |
| **W-9** | **CSS 변수를 안 쓰고 색이 직접 박힌 곳이 모듈 CSS에 16곳** 있다 — 토큰을 바꿔도 따라오지 않는다 | §2.3 표 |
| **W-10** | **PWA/브라우저 크롬 색 3곳이 `#0e0e12`로 박혀 있다** — CSS 토큰을 바꿔도 스플래시·탭 색은 다크로 남는다 | `webclient/index.html:9` · `public/manifest.webmanifest:11,12` |
| **W-11** | `--font` 토큰이 **어디에서도 소비되지 않는다.** `main.css:9`가 `:root`에 스택을 직접 박았고, 그 스택은 토큰과 달리 `"Apple SD Gothic Neo"`가 빠져 있다 | `tokens.css:37` · `main.css:9` |
| **W-12** | **reduced-motion에서 스피너가 사실상 멈춘다.** `main.css:71-76`의 전역 `!important`(0.01ms)가 `components.module.css:66-70`의 `2s`를 이긴다 | 두 파일 · main.css가 나중 로드 + `!important` |
| **W-13** | WPF 버튼 스타일 8종의 `Padding` setter가 **템플릿에 바인딩되지 않아 무효**다. CSS로 그대로 옮기면 원본보다 넓어진다 | `Themes/Controls.xaml` Primary(30)·Secondary(75)·Ghost(114)·Danger(154)·Icon.Pill(218)·Filter(224)·Segment(627)·ComboBox(508) |
| **W-14** | WPF는 **거의 모든 버튼에서 포커스 비주얼을 제거**한다(`FocusVisualStyle={x:Null}` / `NoFocusVisual`)고, 대체 링을 만들지 않았다 | `Themes/Controls.xaml` |
| **W-15** | 웹의 `:focus-visible` 스타일은 **단 2곳**뿐이다(`.button`, `.slot`). Select/TextField/range에는 없다 | `components.module.css:29-32` · `frameEditor.module.css:53-57` |
| **W-16** | Windows 화면 레이아웃에 **반복 규약**이 있다: 화면 좌우 여백 **40**, 상단 오프셋 **88**, 가로 버튼 간격 **16**(각 `Margin="8,0"`), 모달 내 버튼 간격 **12**, 카드 간 간격 **16** | `Views/*.xaml` 16파일 전수 |
| **W-17** | Windows 촬영·카메라테스트 화면만 **예외적 다크 배경** `Brush.CaptureBg #111114`를 쓴다 | `Views/CaptureView.xaml:7` · `Views/CameraTestWindow.xaml` |
| **W-18** | 웹 `.cutCard`·`.stage`는 `#000`으로 박혀 있어 `#111114`와 다르다 | `screens.module.css:163` · `cameraPreview.module.css:15` |
| **W-19** | 웹 버튼 최소 높이는 **48px**(`--touch-min`)인데 WPF CTA는 **56**(`Touch.CTA`)이다 | `components.module.css:4` vs `Metrics.xaml` |
| **W-20** | WPF `Button.Primary`의 `OnAccent #FFFFFF` on `Accent #FF4D79`는 **대비 3.19:1**로 WCAG AA(4.5:1) **미달**이다. 현재 웹(`#1a0a11` on `#ff5c8a`)은 **6.53:1**로 통과한다 | 상대휘도 계산(§4.4) |

### 0.2 미검증 가정 (open assumptions)

| # | 가정 | 검증 단계 |
|---|------|-----------|
| **B-1** | WPF `DropShadowEffect.BlurRadius`를 CSS `box-shadow` blur로 옮길 때 **약 1/2**이 시각적으로 근사하다(래스터화 방식이 달라 1:1 대응이 없다) | **Step V3** 구현 후 **실측 V27-1**(눈 대조). 값은 §4.3에 고정해 두고 어긋나면 그 표만 고친다 |
| **B-2** | §4.2의 **다크 파생 팔레트**(웹 전용 · Windows에 원본이 없다)가 실기기에서 판독 가능하다 | **실측 V27-2**. 코드로는 닫히지 않는다 |
| **B-3** | 태블릿 OS를 라이트 모드로 두면 손님이 보는 화면이 Windows와 동일해진다(다크 파생이 실제로 노출되지 않는다) | **Step V5**의 운영 문서 절차 + **실측 V27-3** |
| **B-4** | 웹 `<select>`(네이티브)의 chevron·팝업을 WPF ComboBox(chevron 12×8 `#6E6878`, 팝업 `Shadow.Pop` maxH 240)와 동일하게 만들 수 없다 → **가장 가까운 대체**로 처리한다 | **Step V3** — 재현 불가 항목으로 `12`에 등재 |

---

## 1. 진단 — 무엇이 왜 다른가

사용자가 "전체 배경이나 디자인 컨셉이 windows 앱과 많이 차이난다"고 한 것은 **정확한 관찰**이다. 원인은 셋이다.

| # | 원인 | 크기 |
|---|------|------|
| **D-1** | **웹이 다크 우선이고 Windows는 라이트 전용이다.** 배경이 `#0e0e12` vs `#FFFFFF` — 색상 하나가 아니라 **컨셉 전체**가 반대다 | ★★★ 지배적 |
| **D-2** | **토큰 어휘가 다르다.** 웹은 22개 범용 토큰(`--bg`/`--fg`/`--accent`…), Windows는 34개 역할 토큰(`Surface`/`Surface.Hover`/`Accent.Soft`/`Text.Tertiary`…). 웹에는 **hover·press·soft·disabled 단계 자체가 없다** | ★★★ |
| **D-3** | **컴포넌트 형태가 다르다.** WPF Danger는 *연분홍 배경 + 붉은 글자*인데 웹은 *붉은 배경 + 흰 글자*다. 버튼 높이 56 vs 48. 카드 그림자 3단 vs 없음 | ★★ |
| 부수 | **웹 라이트 모드 자체가 깨져 있다**(W-3) — "라이트로 바꾸면 되지 않나"가 지금은 통하지 않는다 | ★★ |

---

## 2. 1:1 대조표 — WPF 테마 리소스 ↔ 웹 CSS 변수

> 판정 기호: **✗** 값 불일치 · **∅** 웹에 대응 토큰 **부재** · **≈** 근사(반응형/단위 차이) · **✓** 일치
> "목표" 열이 developer가 넣을 값이다. **라이트(기본)** 기준이며 다크 파생은 §4.2.

### 2.1 색 — 배경·표면 (`Colors.xaml` → `tokens.css`)

| WPF 키 | WPF 값 | 웹 현재 토큰 | 웹 현재값(다크/라이트) | 판정 | **목표(신설 토큰 · 라이트)** |
|--------|--------|--------------|------------------------|------|------------------------------|
| `Color.Bg` | `#FFFFFF` | `--bg` | `#0e0e12` / `#f7f7fa` | ✗ | `--bg: #FFFFFF` |
| `Color.Bg.Elevated` | `#FAF8FC` | `--bg-elevated` | `#1a1a22` / `#ffffff` | ✗ | `--bg-elevated: #FAF8FC` |
| `Color.Surface` | `#F4F1F7` | — | — | ∅ | `--surface: #F4F1F7` |
| `Color.Surface.Alt` | `#ECE8F0` | — | — | ∅ | `--surface-alt: #ECE8F0` |
| `Color.Surface.Hover` | `#E4DEEC` | — | — | ∅ | `--surface-hover: #E4DEEC` |
| `Color.Surface.Press` | `#DAD2E4` | — | — | ∅ | `--surface-press: #DAD2E4` |
| `Color.Border` | `#ECE8F0` | `--border` | `#2c2c38` / `#dcdce4` | ✗ | `--border: #ECE8F0` |
| `Color.Divider` | `#E4DEEC` | — | — | ∅ | `--divider: #E4DEEC` |

### 2.2 색 — 텍스트

| WPF 키 | WPF 값 | 웹 현재 | 판정 | **목표** |
|--------|--------|---------|------|----------|
| `Color.Text.Primary` | `#241F2B` | `--fg` `#f4f4f7`/`#16161c` | ✗ | `--fg: #241F2B` |
| `Color.Text.Secondary` | `#4A4453` | — | ∅ | `--fg-secondary: #4A4453` |
| `Color.Text.Tertiary` | `#6E6878` | — | ∅ | `--fg-tertiary: #6E6878` |
| `Color.Text.Muted` | `#8A8494` | `--fg-muted` `#8b8b98`/`#5c5c68` | ✗ | `--fg-muted: #8A8494` |

### 2.3 색 — 강조(로즈)·보조(민트)

| WPF 키 | WPF 값 | 웹 현재 | 판정 | **목표** |
|--------|--------|---------|------|----------|
| `Color.Accent` | `#FF4D79` | `--accent` `#ff5c8a`(라이트 오버라이드 **없음**) | ✗ | `--accent: #FF4D79` |
| `Color.Accent.Hover` | `#FF6B8F` | — | ∅ | `--accent-hover: #FF6B8F` |
| `Color.Accent.Press` | `#E43C67` | — | ∅ | `--accent-press: #E43C67` |
| `Color.Accent.Text` | `#D6376A` | — | ∅ | `--accent-text: #D6376A` |
| `Color.Accent.Soft` | `#FFE7EE` | — | ∅ | `--accent-soft: #FFE7EE` |
| `Color.OnAccent` | `#FFFFFF` | `--accent-fg` `#1a0a11`/`#ffffff` | ✗ | `--on-accent: #FFFFFF` ⚠️§4.4 |
| `Color.Accent2` | `#37C9B0` | — | ∅ | `--accent2: #37C9B0` |
| `Color.Accent2.Soft` | `#DFF6F1` | — | ∅ | `--accent2-soft: #DFF6F1` |
| `Color.Accent2.Text` | `#128A76` | — | ∅ | `--accent2-text: #128A76` |

### 2.4 색 — 시맨틱·상태·오버레이

| WPF 키 | WPF 값 | 웹 현재 | 판정 | **목표** |
|--------|--------|---------|------|----------|
| `Color.Success` | `#128A76` | `--success` `#2ec27e` | ✗ | `--success: #128A76` |
| `Color.Success.Surface` | `#DFF6F1` | — | ∅ | `--success-surface: #DFF6F1` |
| `Color.Danger` | `#D92D4E` | `--danger` `#ff5c5c` | ✗ | `--danger: #D92D4E` |
| `Color.Danger.Hover` | `#C22645` | — | ∅ | `--danger-hover: #C22645` |
| `Color.Danger.Surface` | `#FDE8EC` | — | ∅ | `--danger-surface: #FDE8EC` |
| `Color.Warning` | `#B26A00` | — | ∅ | `--warning: #B26A00` |
| `Color.Warning.Surface` | `#FFF3DE` | — | ∅ | `--warning-surface: #FFF3DE` |
| `Color.Disabled.Bg` | `#ECE8F0` | — | ∅ | `--disabled-bg: #ECE8F0` |
| `Color.Disabled.Fg` | `#B4AEBE` | — | ∅ | `--disabled-fg: #B4AEBE` |
| `Color.Scrim` | `#66241F2B` (= Ink 40%) | `--bg-scrim` `rgb(0 0 0/65%)` / `/45%` | ✗ **검정 기반** | `--scrim: rgb(36 31 43 / 40%)` |
| `Color.CaptureBg` | `#111114` | 하드코딩 `#000` 2곳 | ✗ | `--capture-bg: #111114` |
| `Color.Shadow` | `#241F2B` | `--shadow` 안 `rgb(0 0 0/35%)` | ✗ | `--shadow-color: 36 31 43` |
| — | — | `--info` `#5c9dff` | **웹 전용** | **유지**(WPF에 대응 없음. 토스트 info 톤에 쓰인다 → `12 §E`에 등재) |

### 2.5 타이포 (`Typography.xaml`)

| WPF 스타일 | 크기/두께/색 | 웹 현재 | 판정 | **목표** |
|-----------|--------------|---------|------|----------|
| `Font.Primary` | `Segoe UI, Malgun Gothic` | `--font`(**미소비**) + `main.css:9` 직접 스택 | ✗ W-11 | `--font`를 **유일 정의**로 하고 `main.css`는 `font-family: var(--font)`만 |
| `Text.Display` | 64 / Bold / `#241F2B` | — | ∅ | `--fs-display: 4rem` (Home 전용, §5.1에서 화면별 오버라이드) |
| `Text.H1` | 32 / Bold / `#241F2B` | `--fs-title` `clamp(1.75rem,6vw,3rem)`=28~48px | ≈ | `--fs-h1: 2rem`(32px) — **clamp를 버린다**(WPF는 고정값이고, 반응형이 곧 "다르게 보임"의 원인이다). 좁은 화면 대응은 §4.5 |
| `Text.H2` | 24 / SemiBold | — | ∅ | `--fs-h2: 1.5rem` |
| `Text.Title` | 20 / SemiBold | 하드코딩 `1.35rem`(21.6px) 3곳 | ✗ | `--fs-title-sm: 1.25rem`(20px) |
| `Text.Body` | 16 / Normal / `#4A4453` | `--fs-body` `clamp(1rem,2.4vw,1.15rem)`=16~18.4px | ≈ | `--fs-body: 1rem`(16px) — clamp 제거 |
| `Text.Label` | 14 / Normal / `#6E6878` | — | ∅ | `--fs-label: 0.875rem` |
| `Text.Caption` | 13 / Normal / `#8A8494` | `--fs-caption` `0.75rem`(12px) | ✗ | `--fs-caption: 0.8125rem`(13px) |
| 두께 | Bold=700 / SemiBold=600 / Medium=500 / Normal=400 | 리터럴 600/700/800 산재 | ✗ | `--fw-bold:700` `--fw-semibold:600` `--fw-medium:500`. **800은 쓰지 않는다**(WPF에 없다 — `.countdown`·`main.css:48` 정정) |

### 2.6 메트릭 (`Metrics.xaml`)

| WPF 키 | WPF 값 | 웹 현재 | 판정 | **목표** |
|--------|--------|---------|------|----------|
| `Space.XS` | 4 | — | ∅ | `--space-xs: 4px` |
| `Space.S` | 8 | `--gap-sm` `0.5rem` | ✓ | `--space-s: 8px`(별칭 유지) |
| `Space.M` | 16 | `--gap` `1rem` | ✓ | `--space-m: 16px` |
| `Space.L` | 24 | — | ∅ | `--space-l: 24px` |
| `Space.XL` | 40 | `--gap-lg` `2rem`=32px | ✗ | `--space-xl: 40px` |
| `Space.XXL` | 64 | — | ∅ | `--space-xxl: 64px` |
| `Radius.S` | 8 | `--radius-sm` `8px` | ✓ | 유지 |
| `Radius.M` | 14 | `--radius` `14px` | ✓ | 유지 |
| `Radius.L` | 24 | — | ∅ | `--radius-lg: 24px` |
| `Radius.Pill` | 999 | 리터럴 `999px` 3곳 | ✗ 토큰화 안 됨 | `--radius-pill: 999px` |
| `Touch.Min` | 48 | `--touch-min` `48px` | ✓ | 유지 |
| `Touch.CTA` | 56 | — (버튼 min-height가 48) | ∅ W-19 | `--touch-cta: 56px` → `.button`에 적용 |
| `Touch.IconBtn` | 56 | — | ∅ | `--touch-icon: 56px` |
| `Shadow.Sm` | Blur8 Depth1 Dir270 Op.06 `#241F2B` | — | ∅ | `--shadow-sm: 0 1px 4px rgb(var(--shadow-color) / 6%)` |
| `Shadow.Card` | Blur20 Depth4 Op.08 | — | ∅ | `--shadow-card: 0 4px 10px rgb(var(--shadow-color) / 8%)` |
| `Shadow.Pop` | Blur32 Depth8 Op.14 | `--shadow` `0 10px 30px rgb(0 0 0/35%)` | ✗ | `--shadow-pop: 0 8px 16px rgb(var(--shadow-color) / 14%)` |

> 그림자 변환식: **CSS `box-shadow: 0 {ShadowDepth}px {BlurRadius / 2}px rgb(Ink / {Opacity})`**.
> WPF `Direction=270`은 **아래 방향**이므로 y 오프셋이 양수다. `BlurRadius/2`는 가정 **B-1**.

### 2.7 하드코딩 색 — 토큰을 바꿔도 따라오지 않는 곳 (전량 16곳)

| # | 파일:줄 | 현재 | 판정 | **조치** |
|---|---------|------|------|----------|
| 1 | `main.css:6` | `--bg: #0e0e12` | ✗ **W-3 원인** | **줄 삭제**(tokens.css가 유일 정의) |
| 2 | `main.css:7` | `--fg: #f4f4f7` | ✗ **W-3 원인** | **줄 삭제** |
| 3 | `main.css:8` | `--muted: #8b8b98` | ✗ 중복 토큰 | **줄 삭제** + 소비처 2곳(`main.css:54,65`)을 `var(--fg-muted)`로 |
| 4 | `components.module.css:43` | `.danger { color: #fff }` | ✗ | `var(--danger)` (WPF Danger는 **연분홍 배경 + 붉은 글자** — §3.1) |
| 5 | `screens.module.css:72` | `.frameThumb { background:#fff }` | ✗ | `var(--bg)` (WPF는 카드 `Surface`가 비친다 → 라이트에서 동일, 다크에서 따라온다) |
| 6 | `screens.module.css:133` | `.countdown { color:#fff }` | **유지** | 촬영 화면은 다크 배경(`--capture-bg`) 위다. `var(--on-accent)`로 토큰화만 |
| 7 | `screens.module.css:134` | `text-shadow: 0 4px 24px rgb(0 0 0/60%)` | **유지** | 다크 배경 위 가독성. 토큰화 불필요(주석으로 이유 명시) |
| 8 | `screens.module.css:143` | `.flash { background:#fff }` | **유지 · 의도적** | 물리적 플래시. 주석 추가 |
| 9 | `screens.module.css:163` | `.cutCard { background:#000 }` | ✗ W-18 | `var(--capture-bg)` (`#111114`) |
| 10 | `screens.module.css:217` | `.qrCanvas { background:#fff }` | **유지 · 의도적** | **QR 스캐너 호환 — 다크에서도 반전 금지**(`:208-211` 주석이 이미 명시) |
| 11 | `screens.module.css:235` | `.resultImage { background:#fff }` | ✗ | `var(--bg)` |
| 12 | `cameraPreview.module.css:15` | `.stage { background:#000 }` | ✗ W-18 | `var(--capture-bg)` |
| 13 | `cameraPreview.module.css:35` | `.overlay { rgb(0 0 0/55%) }` | ✗ | `var(--scrim)` |
| 14 | `frameSelect.module.css:98` | `.thumb { background:#fff }` | ✗ | `var(--bg)` |
| 15 | `frameSelect.module.css:129` | `.unavailableThumb { background:#fff }` | ✗ | `var(--bg)` |
| 16 | `frameSelect.module.css:146` | `.deleteScrim { rgb(0 0 0/55%) }` | ✗ | `var(--scrim)` |
| 17 | `frameEditor.module.css:35` | `.frameImage { background:#fff }` | ✗ | `var(--bg)` |
| 18 | `frameEditor.module.css:165` | `.pickerThumb { background:#fff }` | ✗ | `var(--bg)` |
| 19 | `cameraTest.module.css:14` | `.flash { background:#fff }` | **유지 · 의도적** | 8번과 동일 |
| 20 | `frameSelect.module.css:29,87` | `color-mix(in srgb, var(--bg) 82%/70%, transparent)` | ≈ | 유지하되 82%/70% 비율을 **주석으로 근거 명시** |
| 21 | `frameEditor.module.css:46` | `.slot { color-mix(--accent 18%) }` | ✗ | WPF 슬롯은 `#33FF4D79` = **accent 20%** → `color-mix(in srgb, var(--accent) 20%, transparent)` |

**CSS 밖 (토큰이 닿지 않는 곳)**

| # | 파일:줄 | 현재 | 조치 |
|---|---------|------|------|
| 22 | `webclient/index.html:9` | `<meta name="theme-color" content="#0e0e12">` | `#FFFFFF` |
| 23 | `webclient/index.html:10` | `<meta name="color-scheme" content="dark light">` | `light dark` |
| 24 | `public/manifest.webmanifest:11` | `background_color: #0e0e12` | `#FFFFFF` |
| 25 | `public/manifest.webmanifest:12` | `theme_color: #0e0e12` | `#FFFFFF` |
| 26 | `adapters/qr/qrService.ts:71,74` | canvas `#ffffff`/`#000000` | **유지** — QR 픽셀. 스캐너 호환 |
| 27 | `adapters/frames/fallbackFrame.ts:41` | canvas `#ffffff` | **유지** — 이미지 픽셀 |

---

## 3. 컴포넌트 대조 — WPF `Controls.xaml` ↔ 웹 컴포넌트

> `Padding` 열은 **W-13 주의**: WPF의 Padding setter는 템플릿 미바인딩으로 대부분 무효다. 웹에서 CSS `padding`은 항상 먹으므로 **원본 폭 = MinWidth + 콘텐츠**로 맞춘다. 아래 "목표"의 padding은 그 점을 반영한 값이다.

### 3.1 버튼 5종

| 항목 | WPF 실제값 | 웹 현재(`components.module.css`) | **목표** |
|------|-----------|----------------------------------|----------|
| **Primary** | bg `#FF4D79` · fg `#FFFFFF` · r14 · **h56** · **minW120** · 16/Bold · 테두리 없음<br>hover `#FF6B8F` · press `#E43C67` + **scale .98** · disabled bg `#ECE8F0` fg `#B4AEBE` | `.primary`(35-40): bg `--accent` · fg `--accent-fg`(#1a0a11) · r14 · min-h **48** · min-w 48 · padding `.75rem 1.5rem` · `--fs-body` · 700 · active `scale(.98)` | bg `var(--accent)` · color `var(--on-accent)` · `border:0` · `border-radius: var(--radius)` · **`min-height: var(--touch-cta)`(56)** · **`min-width:120px`** · `padding: 0 1.5rem` · `font-size:var(--fs-body)` · `font-weight:var(--fw-bold)`<br>`:hover` bg `var(--accent-hover)` · `:active` bg `var(--accent-press)` + `scale(.98)` · `:disabled` bg `var(--disabled-bg)` color `var(--disabled-fg)` |
| **Secondary**(= 웹 기본 `.button`) | bg `#ECE8F0` · fg `#241F2B` · border **1 `#ECE8F0`** · r14 · h56 · minW120 · 16/SemiBold<br>hover `#E4DEEC` · press `#DAD2E4` | `.button`(3-17): bg `--bg-elevated` · border 1 `--border` · r14 · min-h 48 | bg `var(--surface-alt)` · color `var(--fg)` · `border:1px solid var(--surface-alt)` · min-height 56 · min-width 120 · 600<br>hover `var(--surface-hover)` · active `var(--surface-press)` |
| **Ghost** | transparent · fg `#4A4453` · border **1 `#ECE8F0`** · r14 · h56 · **minW100** · 16/**Medium(500)**<br>hover bg `#E4DEEC` + border `#6E6878` + fg `#241F2B` · press `#DAD2E4` · disabled fg `#B4AEBE`(배경 불변) | `.ghost`(47-49): background transparent만 | background `transparent` · color `var(--fg-secondary)` · `border:1px solid var(--border)` · min-width **100px** · `font-weight: var(--fw-medium)`<br>hover 3속성 동시 변경(bg·border·color) |
| **Danger** | **bg `#FDE8EC`(연분홍) · fg `#D92D4E`(붉은 글자)** · **r8** · h56 · minW100 · 16/SemiBold<br>hover **반전**(bg `#D92D4E` fg `#FFFFFF`) · **press 없음** | `.danger`(42-43): bg `--danger`(#ff5c5c) · **color `#fff` 하드코딩** — **완전히 반대다** | bg `var(--danger-surface)` · color `var(--danger)` · `border:0` · **`border-radius: var(--radius-sm)`(8 — 다른 CTA와 다르다. WPF 그대로)** · min-width 100px · 600<br>`:hover` bg `var(--danger)` color `var(--on-accent)` |
| **Icon** | 56×56 **완전 원형**(r999) · transparent · fg `#241F2B` · **fs15**<br>hover `#E4DEEC` · press `#DAD2E4` · **disabled 변화 없음** | 없음 | `.iconButton` 신설: `width:var(--touch-icon)` `height:var(--touch-icon)` `border-radius:var(--radius-pill)` `font-size:0.9375rem` |

### 3.2 컨테이너·카드

| 항목 | WPF 실제값 | 웹 현재 | **목표** |
|------|-----------|---------|----------|
| **Card** | bg `#F4F1F7` · border 1 `#ECE8F0` · r14 · **padding 24** · **Shadow.Card** | `settings.module.css:41` `.section`: bg `--bg-elevated` · border 1 `--border` · r14 · **padding 1rem** · **그림자 없음** | bg `var(--surface)` · border 1px `var(--border)` · r `var(--radius)` · `padding: var(--space-l)`(24) · `box-shadow: var(--shadow-card)` |
| **FrameCard**(버튼형) | bg `#F4F1F7` · border **2 transparent** · r14 · **Shadow.Sm**<br>hover → **Shadow.Card**(테두리·배경 불변) · selected → border `#FF4D79` + Shadow.Card | `screens.module.css:52-63` / `frameSelect.module.css:51-73`: bg `--bg-elevated` · border 2 `--border` · 그림자 없음 | bg `var(--surface)` · `border:2px solid transparent` · `box-shadow: var(--shadow-sm)`<br>hover `box-shadow: var(--shadow-card)` · `[aria-pressed="true"]` border `var(--accent)` + `var(--shadow-card)` |
| **FrameCard 컨테이너 여백** | `Margin=10` → 항목 간 **20px** | grid `gap` | `gap: 20px`(= `--space-s`+`--space-xs`*3이 아니라 리터럴. WPF가 리터럴이므로 주석으로 근거) |
| **FrameCard 본체 크기** | **200×280 고정** | `aspect-ratio: 3/4` + `minmax(140px,1fr)` | ⚠️ **웹은 고정 크기를 쓰지 않는다**(태블릿 폭이 다양하다). `minmax(200px, 1fr)` + `aspect-ratio: 200/280`(=5/7)로 **비율만 맞춘다** → `12`에 등재 |
| **FrameCard 이름 바** | Border **h36** · bg Scrim · **r `0 0 12 12`**(하드코딩 12) · 텍스트 흰색 14/SemiBold · 하단 정렬 | 웹은 카드 밖 별도 텍스트 | 오버레이 이름 바 도입: `position:absolute; inset:auto 0 0 0; height:36px; background:var(--scrim); border-radius:0 0 12px 12px; color:var(--on-accent); font-size:var(--fs-label); font-weight:var(--fw-semibold)` |
| **슬롯 미리보기** | fill `#33FF4D79`(accent 20%) · stroke `#FF4D79` | `frameEditor.module.css:46` accent **18%** · **2px 점선** · r2 | fill `color-mix(in srgb, var(--accent) 20%, transparent)` · stroke `var(--accent)`. **점선/실선은 웹 유지**(WPF는 실선이나 편집기에서 점선이 조작 대상을 더 잘 알린다 → `12`에 등재) |
| **Modal 카드** | Card + **Shadow.Pop** + bg `Brush.Bg`(#FFFFFF) + 중앙. 폭: QR **440 고정** · 오버레이 **MinWidth 380** · 피커 **MinWidth720/MaxWidth1100/MaxHeight620** | `.dialog`(86-93): `min(560px,100%)` · max-h 90vh · bg `--bg-elevated` · `--shadow` | bg `var(--bg)` · `box-shadow: var(--shadow-pop)` · 폭은 §5.1 화면별 |
| **Scrim** | `#66241F2B`(Ink 40%) | `--bg-scrim` 검정 65%/45% | `var(--scrim)` = `rgb(36 31 43 / 40%)` |

### 3.3 입력·토글

| 항목 | WPF 실제값 | 웹 현재 | **목표** |
|------|-----------|---------|----------|
| **TextBox** | bg `#FFFFFF` · fg `#241F2B` · **caret `#FF4D79`** · border 1 `#ECE8F0` · **r8** · padding `10,8` · **minH48** · fs16<br>focus → border `#FF4D79` **1→2px** · disabled bg `#ECE8F0` fg `#B4AEBE` | `fields.module.css:82-92` `.textField`: bg `--bg-elevated` · r8 · min-h `--touch-min` · `--fs-body` · **focus 링 없음** | bg `var(--bg)` · `caret-color: var(--accent)` · border 1px `var(--border)` · r `var(--radius-sm)` · padding `8px 10px`<br>`:focus-visible` → `border-color:var(--accent); border-width:2px; padding:7px 9px`(**레이아웃 시프트 방지** — WPF는 시프트하지만 웹에서는 padding 보정으로 흡수한다 → `12` 등재) |
| **Select(ComboBox)** | bg `#FFFFFF` · r8 · minH48 · fs16 · chevron **12×8 `#6E6878`** · 팝업 Shadow.Pop maxH240 r8 | `fields.module.css:65-76` 네이티브 `<select>` | 색·라운드·높이만 맞춘다. **chevron·팝업은 재현하지 않는다**(가정 B-4 → `12` 등재) |
| **Toggle(스위치)** | 히트 **56×48** · track **52×30 r15** bg `#ECE8F0` border1 · thumb **원 24** 흰색 + Shadow.Sm · checked → track `#FF4D79` · **애니메이션 없음(즉시 스냅)** | `fields.module.css:44-58` `.toggle` — **버튼형**(min-width 88px), 스위치가 아니다 | 스위치 형태로 교체. ⚠️ **`transition`을 넣지 마라** — WPF는 즉시 스냅이다. 넣으면 "다르게 보인다" |
| **Segment(Choice)** | h48 · **minW64** · r8 · transparent · fg `#6E6878` · 15/SemiBold<br>checked → bg `#FF4D79` fg `#FFFFFF` **+ Bold**(폭 변동) · hover는 **미선택일 때만** `#E4DEEC` | `fields.module.css:61` `.choice` min-width 64px | WPF와 동일. ⚠️ checked에서 **Bold로 굵어지는 것까지** 재현한다(레이아웃 흔들림 포함 — 원본 동작이다) |

### 3.4 기타

| 항목 | WPF 실제값 | 웹 현재 | **목표** |
|------|-----------|---------|----------|
| **Spinner** | Ellipse **56×56** · stroke `#FF4D79` **5px** · dash `"4 2"`(두께 배수 → 실제 **20/10**) · **1초** 선형 무한 · IsVisible=false에서 일시정지 | `components.module.css:51-58`: 32×32 · 3px border · **800ms** | SVG 또는 conic으로 56×56 · 5px · `20 10` dash · `animation: 1s linear infinite`. ⚠️ **W-12 수정 필요**(§4.6) |
| **Toast** | Settings 화면에만 존재. `Border` r**999** · padding `24,12` · Shadow.Pop · bg `Brush.Surface` · 상단 `Margin 0,96,0,0` · `IsHitTestVisible=False` | `components.module.css:253-283`: **하단** 중앙 · r8 · 좌측 6px 컬러 바 · bg `--bg-elevated` | ⚠️ **위치를 옮기지 않는다**(웹은 하단이 손가락에 가깝고 상단바와 겹치지 않는다). 색·라운드만 맞춘다: bg `var(--surface)` · `box-shadow: var(--shadow-pop)`. **좌측 컬러 바는 유지**(색만으로 구분하지 않기 위한 웹 접근성 장치 — `12` 등재) |
| **TopBar** | WPF에 대응 컨트롤 없음(각 View가 상단 오프셋 88로 자리를 비운다) | `components.module.css:287-306`: min-h 48 · 하단 1px border | **웹 전용.** 높이를 **56**(`--touch-cta`)으로 올려 WPF의 88 오프셋 감각에 근접시킨다. bg `var(--bg)` · border-bottom 1px `var(--divider)` |
| **Banner**(전체화면 이탈) | WPF에 없음 | bg `--accent` 풀블리드 · r0 | **웹 전용.** bg `var(--warning-surface)` · color `var(--warning)`로 바꾼다 — 로즈 풀블리드는 CTA 색과 충돌해 "누르는 곳"으로 오독된다. `12` 등재 |
| **PinKeypad** | WPF는 PasswordBox 입력(키패드 없음) | `components.module.css:162-206` 키 h**56** | **웹 전용.** 56은 이미 `--touch-cta`와 같다 → 토큰화만 |
| **Capture 배경** | `#111114` | `#000` 2곳 | `var(--capture-bg)` |
| **Capture 상단 칩** | Scrim bg · **r6** · padding `14,8` · margin 20 · fs20 Bold | `screens.module.css:121` `.progress` fs 1.1rem | r6 · padding `8px 14px` · bg `var(--scrim)` · fs 1.25rem/Bold |
| **셔터 버튼** | 88×88 · 흰 링 4px · 내부 원 64 `#FF4D79` · press scale .9 | 웹은 일반 Button([바로 촬영]) | **우선순위 낮음**(§5.2 P3). 도입 시 위 값 그대로 |

---

## 4. 핵심 판정

### 4.1 P2-1 — **라이트를 기본으로 뒤집는다** (가장 큰 변경)

```
현재:  :root { color-scheme: dark light; --bg:#0e0e12; … }
       @media (prefers-color-scheme: light) { :root { --bg:#f7f7fa; … } }   ← 7개만, 게다가 깨져 있다

목표:  :root { color-scheme: light dark; --bg:#FFFFFF; … }                   ← WPF 팔레트 전량
       @media (prefers-color-scheme: dark) { :root { … } }                   ← §4.2 파생
```

**왜 "라이트 전용"이 아니라 "라이트 기본 + 다크 파생"인가**: 팀 리드 지시 5("다크모드 대응을 깨지 마라")를 지킨다. 미디어 쿼리는 계속 동작하고 대비도 유지된다.

**왜 그래도 Windows와 같아 보이는가**: 키오스크 운영 절차에 **"태블릿/PC OS를 라이트 모드로 두라"** 를 추가한다([`09 §4`](../web-client/09-kiosk-operations.md) 전원 설정 옆). 손님이 보는 화면은 항상 라이트 = Windows와 동일하고, 다크는 개발·검수용 안전망으로만 남는다(가정 B-3).

### 4.2 다크 파생 팔레트 (웹 전용 — `12 §E`에 등재)

Windows에 원본이 없으므로 **파생 규칙**을 명시한다: 중성 램프(bg/surface/text/border)만 뒤집고, **강조·보조·라운드·간격·타이포는 라이트와 동일**하게 둔다. 다크에서 판독 불가가 되는 색만 별도로 조정한다.

| 토큰 | 라이트(=WPF) | **다크 파생** | 파생 근거 |
|------|--------------|---------------|-----------|
| `--bg` | `#FFFFFF` | `#14121A` | Ink(`#241F2B`) 계열을 더 어둡게 — 순검정은 OLED에서 대비가 과하다 |
| `--bg-elevated` | `#FAF8FC` | `#1D1A24` | bg보다 한 단계 밝게 |
| `--surface` | `#F4F1F7` | `#241F2B` | **Ink 그 자체**(램프 반전의 대칭점) |
| `--surface-alt` | `#ECE8F0` | `#2E2937` | |
| `--surface-hover` | `#E4DEEC` | `#383142` | |
| `--surface-press` | `#DAD2E4` | `#423A4D` | |
| `--border` | `#ECE8F0` | `#2E2937` | surface-alt와 동일(라이트도 그렇다) |
| `--divider` | `#E4DEEC` | `#383142` | |
| `--fg` | `#241F2B` | `#F4F1F7` | Surface를 뒤집는다 |
| `--fg-secondary` | `#4A4453` | `#D3CDDB` | |
| `--fg-tertiary` | `#6E6878` | `#ADA5B8` | |
| `--fg-muted` | `#8A8494` | `#8A8494` | **동일** — 중간톤이라 양쪽에서 통한다 |
| `--accent` / `-hover` / `-press` | `#FF4D79` / `#FF6B8F` / `#E43C67` | **동일** | 브랜드색은 뒤집지 않는다 |
| `--on-accent` | `#FFFFFF` | **동일** | |
| `--accent-soft` | `#FFE7EE` | `#3A1B27` | soft는 "배경 틴트"이므로 다크에선 어두워야 한다 |
| `--accent-text` | `#D6376A` | `#FF8AA8` | ⚠️ `#D6376A`는 다크 배경에서 대비 부족 → 밝게 |
| `--accent2` | `#37C9B0` | **동일** | |
| `--accent2-soft` | `#DFF6F1` | `#12332E` | |
| `--accent2-text` | `#128A76` | `#5BD9C3` | 동상 |
| `--success` | `#128A76` | `#37C9B0` | 다크에서 `#128A76`는 어둡다 |
| `--success-surface` | `#DFF6F1` | `#12332E` | |
| `--danger` | `#D92D4E` | `#FF6B85` | |
| `--danger-hover` | `#C22645` | `#FF4D6A` | |
| `--danger-surface` | `#FDE8EC` | `#3A1B22` | |
| `--warning` | `#B26A00` | `#E0A34A` | |
| `--warning-surface` | `#FFF3DE` | `#3A2C15` | |
| `--disabled-bg` / `-fg` | `#ECE8F0` / `#B4AEBE` | `#2E2937` / `#6E6878` | |
| `--scrim` | `rgb(36 31 43 / 40%)` | `rgb(0 0 0 / 55%)` | ⚠️ 다크에선 Ink scrim이 배경과 구분되지 않는다 |
| `--shadow-color` | `36 31 43` | `0 0 0` | 다크에선 검정 그림자 |
| `--shadow-sm/card/pop` | Op 6/8/14% | Op **20/28/40%** | 다크 표면 위에서는 그림자가 훨씬 약하게 보인다 |
| `--capture-bg` | `#111114` | **동일** | 촬영 화면은 원래 다크다 |
| 라운드·간격·타이포 전량 | — | **동일** | 파생하지 않는다 |

### 4.3 P2-2 — 그라데이션을 **새로 만들지 않는다**

Windows 팔레트에 그라데이션 브러시가 **0건**이다(W-5). 사용자가 지목한 "전체 배경"의 실체는 **다크 vs 흰색**(D-1)과 **Home의 파스텔 원 2개**(W-7)다. 그라데이션을 도입하면 오히려 원본에서 멀어진다.

대신 **Home 장식 원을 이식한다** — 이것이 "디자인 컨셉"의 가시적 핵심이다.

```css
/* screens.module.css — HomeView 전용. WPF Views/HomeView.xaml:9-12 대응.
   ⚠️ 장식이므로 aria-hidden 요소이거나 ::before/::after 여야 한다(스크린리더 무시). */
.homeDecorTopLeft {
  position: fixed; left: -120px; top: -120px;
  width: 360px; height: 360px; border-radius: 50%;
  background: var(--accent-soft); opacity: 0.6; pointer-events: none;
}
.homeDecorBottomRight {
  position: fixed; right: -100px; bottom: -100px;
  width: 300px; height: 300px; border-radius: 50%;
  background: var(--accent2-soft); opacity: 0.6; pointer-events: none;
}
```
⚠️ `position: fixed` + `pointer-events:none` + `z-index: 0`(콘텐츠보다 아래). **`overflow-x: hidden`이 이미 `main.css:24`에 있어** 음수 오프셋이 가로 스크롤을 만들지 않는다 — 이 전제가 깨지면 안 된다.

### 4.4 ⚠️ P2-3 — **명암비 판정 (team-lead 승인 완료 · 2026-08-01)**

| | 전경/배경 | 대비 | WCAG AA(일반 텍스트 4.5:1) |
|---|---|---|---|
| **WPF 현행**(목표값) | `#FFFFFF` on `#FF4D79` | **3.19:1** | ✗ 미달 |
| **웹 종전** | `#1a0a11` on `#ff5c8a` | **6.53:1** | ✓ 통과 |

즉 **WPF와 똑같이 만들면 Primary 버튼의 명암비가 6.53 → 3.19로 떨어진다.** 팀 리드 지시 5("접근성을 깨지 마라")와 지시 1·2("WPF와 거의 동일하게")가 정면으로 충돌하는 유일한 지점이다.

**판정: WPF와 일치시킨다(`--on-accent: #FFFFFF`).** 2026-08-01 팀 리드가 승인했고, **조건이 붙었다** — "버튼 텍스트가 실제로 large-text 기준을 충족하는지 CSS에서 확인하고 수치를 문서에 남길 것". §4.4.1이 그 실측이다.

근거:

1. WCAG "large text"(**≥18.66px Bold** 또는 ≥24px 일반)는 **3:1**이면 된다. `#FFFFFF` on `#FF4D79` = 3.19:1 → **large text면 통과**한다.
2. 웹만 다른 색을 쓰면 두 제품이 눈에 띄게 갈라지고(흰 글자 vs 검은 글자) 사용자가 바로 지적한다.
3. 근본 해소는 `Colors.xaml`의 `Color.Accent`를 어둡게 하는 것인데(참고: `Accent.Press #E43C67`도 4.09:1로 여전히 미달), 그건 **Windows 디자인 시스템 변경**이라 이 작업의 범위 밖이다 → [`12 §H8`](../web-client/12-web-vs-windows-differences.md)에 등재돼 있다.

#### 4.4.1 실측 — accent 계열 배경 위 텍스트 전수 (2026-08-01)

> 계산식: WCAG 2.x 상대휘도 `L = 0.2126R + 0.7152G + 0.0722B`(각 채널 sRGB 역감마) → `(L₁+0.05)/(L₂+0.05)`.
> "전수"의 범위는 **모듈 CSS에서 `background`가 `--accent*`·`--danger`인 규칙 전부**다(`grep "background:.*accent|danger"`).

**배경색별 대비(전경 후보 2종)**

| 배경 토큰 | 값 | `#FFFFFF` | `#241F2B`(잉크) |
|---|---|---:|---:|
| `--accent` | `#FF4D79` | **3.19:1** | **5.05:1** |
| `--accent-hover` | `#FF6B8F` | 2.71:1 | 5.94:1 |
| `--accent-press` | `#E43C67` | 4.09:1 | 3.94:1 |
| `--danger`(라이트) | `#D92D4E` | **4.73:1** | 3.41:1 |
| `--danger`(다크 파생) | `#FF6B85` | 2.73:1 | **5.89:1** |

> ⚠️ **`#FF4D79` 위에서 4.5:1을 만족하는 밝은 색은 수학적으로 존재하지 않는다.** 필요한 상대휘도가 1.43(>1)이다. 즉 "흰색 대신 살짝 어두운 흰색"으로는 절대 해결되지 않는다 — **글자를 키우거나(3:1 기준) 잉크로 뒤집는(5.05:1) 두 길뿐**이다.

**요소별 폰트·판정 (수정 후)**

| # | 요소 | 파일 | 배경 | 폰트(수정 전 → 후) | large text? | 대비 | 판정 |
|---|------|------|------|---------------------|:---:|---:|---|
| 1 | `.primary` (Primary 버튼 전체) | `components.module.css` | `--accent` | 16px/700 → **19px/700** | ✗ → **✓** | 3.19:1 | **✓ AA(large)** — 폰트를 키워 해소 |
| 2 | `.primary:hover` | 〃 | `--accent-hover` | 19px/700 | ✓ | 2.71:1 | ⚠️ **미달로 남긴다** — hover는 포인터 기기 전용(`@media (hover:hover)`)의 **일시 상태**이고, 키오스크는 터치라 도달하지 않는다. 해소하려면 `Color.Accent.Hover` 자체를 양 플랫폼에서 바꿔야 한다(H8과 같은 조건) |
| 3 | `.primary:active` | 〃 | `--accent-press` | 19px/700 | ✓ | 4.09:1 | ✓ AA(large) |
| 4 | `.danger:hover`(라이트) | 〃 | `--danger` | 16px/600 | ✗ | **4.73:1** | ✓ **AA(일반)** — 크기와 무관하게 통과. 변경 없음 |
| 5 | `.danger:hover`(다크) | 〃 | `--danger` 파생 | 16px/600 | ✗ | 2.73 → **5.89:1** | **✓** — 다크에서만 `--on-accent-ink`로 뒤집었다(라이트는 WPF 그대로) |
| 6 | `.choice[aria-pressed="true"]`(세그먼트 선택) | `fields.module.css` | `--accent` | 15px/700 | ✗ | 3.19 → **5.05:1** | **✓ AA(일반)** — 색으로 해소. 폰트를 키우면 미선택(15px)과 폭이 어긋나 누를 때마다 흔들린다 |
| 7 | `.autoBadge`("자동" 배지) | `screens.module.css` | `--accent` | 13px/700 | ✗ | 3.19 → **5.05:1** | **✓ AA(일반)** — 인라인 배지라 19px 불가 → 색으로 해소 |
| 8 | `.cutOrder`(컷 순번 배지) | 〃 | `--accent` | 14px/700 | ✗ | 3.19 → **5.05:1** | **✓ AA(일반)** — 28×28 원 안이라 19px 불가 → 색으로 해소 |
| 9 | `.progress`(촬영 상단 칩) | 〃 | `--scrim` on `--capture-bg` | 20px/700 | ✓ | ≫ 7:1 | ✓ — accent가 아니라 잉크 40% 스크림 + `#111114` 위다 |
| 10 | `.countdown` | 〃 | 프리뷰(다크) | 64~144px/700 | ✓ | — | ✓ — 흰 글자 + 검정 글로우 |

**수정한 것 (그 버튼/요소만)**

| 대상 | 변경 | 다른 곳 영향 |
|------|------|--------------|
| `.primary` | `font-size: 1.1875rem`(19px) 추가 | `.button`·`.ghost`·`.danger`·`.iconButton`은 `--fs-body`(16px) 그대로 |
| `--on-accent-ink` 토큰 신설(`#241F2B`) | 라이트·다크 **동일**(accent가 다크에서 뒤집히지 않으므로) | 신규 토큰이라 기존 소비처 없음 |
| `.choice[aria-pressed="true"]` · `.autoBadge` · `.cutOrder` | `color`를 `--on-accent` → `--on-accent-ink` | 미선택 세그먼트·다른 배지 없음 |
| `.danger:hover` | `@media (hover:hover) and (prefers-color-scheme: dark)`에서만 잉크 | 라이트 모드 무변경 |

**⚠️ WPF와의 차이(의도적)**: ① Primary 폰트 16 → 19px, ② 세그먼트 선택·배지 2종의 글자색 흰색 → 잉크. 둘 다 [`12 §H8`](../web-client/12-web-vs-windows-differences.md)에 등재. **색(`#FF4D79`·`#FFFFFF`)은 한 값도 바꾸지 않았다.**

**회귀 방지**: `webclient/tests/unit/ui/themeInvariants.test.ts`의 **THEME-2** 블록이 `.primary`의 font-size ≥ 18.66px, `--on-accent`/`--on-accent-ink` 값, 작은 요소 3곳의 토큰 사용을 고정한다. 값을 되돌리면 테스트가 깨진다.

### 4.5 P2-4 — 반응형 clamp를 버리되 좁은 화면은 지킨다

WPF는 폰트 고정값(64/32/24/20/16/14/13)이고 웹은 `clamp(1.75rem,6vw,3rem)`처럼 뷰포트에 따라 변한다(W-8). **같은 화면 폭에서 다르게 보이는 직접 원인**이므로 고정값으로 바꾼다.

단 **가로 스크롤 금지**(01 §8)와 태블릿 세로 모드를 지켜야 하므로:
- 기본은 고정값.
- `@media (max-width: 480px)`에서 **Display 64→40 · H1 32→26** 두 단계만 축소한다(WPF에도 화면별 오버라이드가 있으므로 성질이 같다).
- Body 이하(16/14/13)는 **어떤 폭에서도 줄이지 않는다** — 가독성 하한이다.

### 4.6 P2-5 — 접근성 3건은 **강화**한다 (지시 5 준수)

| 항목 | 지금 | 목표 | 왜 |
|------|------|------|-----|
| 터치 타깃 | `.button` min-height **48** | **56**(`--touch-cta`) | WPF `Touch.CTA`와 일치 + 접근성 **강화**. `--touch-min: 48px`는 남겨 아이콘·보조 요소에 계속 쓴다 |
| 포커스 링 | `:focus-visible`이 `.button`·`.slot` **2곳뿐**(W-15). 색은 `--accent` → **로즈 버튼 위에서 안 보인다** | **전역 폴백** `:focus-visible { outline: 3px solid var(--fg); outline-offset: 2px }`를 `main.css`에 두고, 개별 규칙은 제거하거나 동일 색으로 통일. Select·TextField·range에도 적용 | ⚠️ **WPF의 `FocusVisualStyle={x:Null}`을 따라하지 마라**(W-14). 포커스 제거는 웹 접근성 위반이다 → **의도된 차이**로 `12`에 등재 |
| reduced-motion | `main.css:73`의 전역 `!important`가 스피너 2s를 이겨 **사실상 정지**한다(W-12) | 전역 규칙에서 `.spinner`(및 로딩 인디케이터)를 제외하거나, 스피너 규칙에도 `!important`를 붙여 **2s로 느리게 돌게** 한다 | 로딩 표시가 멈추면 "앱이 멈춘 것"으로 보인다. reduced-motion의 의도는 "정지"가 아니라 "완화"다 |

---

## 5. 화면별 레이아웃 정합 — 13화면 우선순위

### 5.1 Windows 공통 레이아웃 규약 (W-16) → 웹 토큰

| 규약 | WPF 값 | 웹 목표 |
|------|--------|---------|
| 화면 좌우 여백 | **40** | `--screen-pad-x: var(--space-xl)`(40px). 현재 `screens.module.css`는 `1rem`(16) |
| 상단 오프셋 | **88** | 웹은 TopBar가 실물이므로 `TopBar(56) + var(--space-l)(24) = 80`. **88을 그대로 쓰지 않는다**(웹에는 상단바가 있고 WPF의 88은 그 자리를 비워 둔 값이다) → `12` 등재 |
| 하단 여백 | 40 | `--space-xl` |
| 가로 버튼 간격 | **16**(각 `Margin="8,0"`) | `.actions { gap: var(--space-m) }` |
| 모달 내 버튼 간격 | **12** | `.overlayActions { gap: 12px }` |
| 카드 간 간격 | **16** | `gap: var(--space-m)` |
| 세로 버튼 간격 | 12 | `gap: 12px` |
| 섹션 제목 하단 | H2 + **16** | `margin-bottom: var(--space-m)` |
| 라벨 → 입력 | 라벨 하단 **4** | `margin-bottom: var(--space-xs)` |
| 카드 목록 거터 | 컨테이너 `Margin=10` → **20** | `gap: 20px` |
| 모달 카드 폭 | 440(QR·Login·Guide) · 380(오버레이) · 720~1100(피커) | 각 화면에서 `min(440px, 100%)` 등으로 |

### 5.2 처리 우선순위 (차이가 큰 것부터)

| P | 화면 | 현재 웹 | Windows | 차이 크기 | **이번에 어디까지** |
|---|------|---------|---------|-----------|---------------------|
| **P1** | **Home** | 다크 배경 · 장식 없음 · `--fs-title` clamp · 버튼 min-h48 | 흰 배경 + **파스텔 원 2개** · Display **64/Bold** · 부제 H2 `#6E6878` + `margin 0,8,0,48` · 버튼 **h72 fs24 padding 72,0** | ★★★ 사용자가 첫눈에 보는 화면 | **전부**(장식 원 포함) |
| **P1** | **Guide** | `.screen` 중앙 · `dl` 목록 | **Card(MinWidth 440)** 안에 · 제목 H1 + `margin 0,0,0,28` · 정보행 3개(각 `margin 0,0,0,12`, 값은 **Accent+Bold** 우측 정렬) · 캡션 중앙 `0,16,0,28` · **[촬영 시작] h64 fs20 HA=Stretch** + [취소] Ghost 중앙(12px) | ★★★ | **전부** |
| **P1** | 공통 셸(TopBar·Modal·Toast·Banner·Button·Card·Field) | §3 | §3 | ★★★ 모든 화면에 파급 | **전부**(Step V3) |
| **P2** | **Result** | 세로 스택 | **2열 `* | 340`** · 좌 Card(`Margin 40,88,20,40` **Padding 16**) · 우 필터 세로 목록(항목 간 **8**) + [다음] Stretch + [취소] Ghost | ★★ | 2열 레이아웃 + 필터 pill(`Button.Filter` h48 · selected `#FFE7EE`/border `#FF4D79`/fg `#D6376A`) |
| **P2** | **CutSelect** | grid `minmax(110px,1fr)` · 카드 bg **#000** · 3px 테두리 | 3행(`Auto|*|Auto`) · WrapPanel 중앙 · 항목 **Width 200 고정** `Margin=8`(간격 16) **r12 border3** Shadow.Sm · 배지 **28×28 r14** 우상단 `Margin=6` | ★★ | 색·라운드·테두리·배지 크기. **고정 200은 `minmax(200px,1fr)`로 대체** |
| **P2** | **FrameSelect** | grid `minmax(140px,1fr)` · 카드 bg elevated | WrapPanel **가로 스크롤** · 카드 200×280 · 거터 20 · **이름 바 h36 Scrim** · 삭제 ✕ **28×28** `Margin=6` | ★★ | 카드 형태·거터·이름 바·삭제 버튼 크기. **가로 스크롤은 채택하지 않는다**(웹은 세로 그리드가 자연스럽고 `01 §8` 가로 스크롤 금지와 충돌) → `12` 등재 |
| **P3** | **Capture** | bg `#000` · 카운트다운 `clamp(4rem,18vw,9rem)`/800 | bg **`#111114`** · 칩 `r6 padding 14,8 margin 20 fs20 Bold` · 카운트다운 **Viewbox h220** · 셔터 88 원형 `margin 0,0,0,40` · 취소 우상단 `fs15 padding 16,8 margin 20` | ★★ | 배경색·칩·취소 버튼. **셔터 버튼 도입은 선택**(웹은 [바로 촬영] 텍스트 버튼) |
| **P3** | **Qr** | 세로 스택 | 모달 Card **Width 440 Padding 48** · QR Border 흰 배경 `padding16 r8` Shadow.Sm · **Image 240×240** · 진행바 **h8** | ★★ | 카드 폭·패딩·QR 프레임. ⚠️ **QR 캔버스 `#fff` 고정 유지** |
| **P3** | **Done** | `.screen` | Display **48**(Home의 64와 다름) · 부제 `margin 0,12,0,40` · 버튼 `padding 48,0` | ★ | 폰트 크기·여백 |
| **P3** | **Login** | `.screen` | **Card Width 440 + MaxWidth 440** · 제목 H1 `0,0,0,12` · 캡션 `0,0,0,24` · [Google 로그인] **HA=Stretch** `0,0,0,12` · [닫기] Ghost 중앙 | ★★ | Card로 감싼다 |
| **P4** | **Settings** | 섹션 카드 padding 1rem · sticky 바 | 스크롤 `Padding 40,88,40,16` · **MaxWidth 1200** · 카드 `margin 0,0,0,16` padding 24 · **2열 그리드(간격 40)** · sticky 바 `Padding 40,12` + 상단 1px Divider + **Shadow.Sm** | ★★ | 카드·간격·sticky 바. **2열 그리드는 기존 웹 레이아웃 유지**(반응형이 이미 다르다) |
| **P4** | **Account** | padding 2rem · MaxWidth 없음 | 스크롤 `Padding 40,88,40,40` · **MaxWidth 560 중앙** · 카드 3개 `margin 0,0,0,16` · **하단 고정 바 없음** | ★ | MaxWidth 560 + 카드 간격 |
| **P4** | **UserMgmt** | ≥720 table / <720 카드 | `Margin 40,80,40,40`(**상단 80 — 유일한 불일치**) · 컬럼 폭 300/128/150/96/232/240 · 헤더 h42 · 행 h58 · **셀 컨트롤 h38(터치 예외)** | ★ | 색·행 높이·구분선. **웹의 반응형 2형태는 유지**(WPF는 데스크톱 전용) |
| **P4** | **FrameEditor** | 좌 스테이지 + 우 320 패널 | 좌 Card `Margin 40,0,20,40 Padding 16` + 우 **320 고정** · 배너 `Warning.Surface r14 padding 16,10` · 슬라이더 10~300 · 저장/취소 12px | ★ | 색·배너·패널 폭 |
| **P4** | **모달 3종**(CameraTest·PinPrompt·Diagnostics) | 각자 CSS | CameraTest bg **CaptureBg** · PinPrompt `Margin 28` 버튼 **우측 정렬 8px** · Diagnostics `Padding 32,28,32,16` + 카드 4개 + 하단 바 `32,12` | ★ | 색·패딩 |

> ⚠️ **웹에서 채택하지 않는 Windows 수치**(전부 `12`에 등재): 고정 폭 카드(200×280 → 비율), FrameSelect 가로 스크롤, 상단 오프셋 88(웹은 TopBar 실물), UserMgmt 상단 80(WPF 자체의 불일치 — 88로 통일한다), PinPrompt 버튼 우측 정렬(웹은 중앙 통일).

### 5.3 `12-web-vs-windows-differences.md`에 등재할 항목 (재현 불가 / 의도적 차이)

| # | 항목 | Windows | Web(대체) | 왜 |
|---|------|---------|-----------|-----|
| **B-n1** | 그림자 | `DropShadowEffect` BlurRadius/ShadowDepth/Direction(비트맵 래스터화) | `box-shadow` — blur ≈ WPF/2, y = Depth | 렌더 모델이 다르다. 수치 1:1 대응 없음(가정 B-1) |
| **B-n2** | ComboBox 팝업·chevron | 커스텀 `Popup`(Fade, Shadow.Pop, maxH240) + `Path` 12×8 | 네이티브 `<select>` | 네이티브 드롭다운의 팝업·화살표는 OS가 그린다. 커스텀 구현은 접근성·IME 회귀 위험이 크다(가정 B-4) |
| **B-n3** | 포커스 비주얼 | `FocusVisualStyle={x:Null}` — **없음** | `:focus-visible` 3px `var(--fg)` 링 | 웹 접근성 필수. **의도적으로 다르게 만든다** |
| **B-n4** | 다크 모드 | **없음**(라이트 전용) | `prefers-color-scheme: dark` 파생 팔레트 | 브라우저·OS가 제공하는 기대 동작. 운영은 라이트 고정 권장 |
| **B-n5** | 카드 크기 | 200×280 **고정 px** | `minmax(200px,1fr)` + `aspect-ratio: 5/7` | 태블릿 폭이 다양하다. 고정 px는 가로 스크롤을 만든다 |
| **B-n6** | FrameSelect 스크롤 | **가로** 스크롤(WrapPanel H=Auto) | **세로** 그리드 | `01 §8` 가로 스크롤 금지 |
| **B-n7** | 타이포 스케일 | 고정 px | 기본 고정 + `max-width:480px`에서 Display·H1만 2단계 축소 | 모바일 세로에서 64px 제목이 넘친다 |
| **B-n8** | 이탈 배너 색 | (해당 없음) | `--warning-surface` (로즈 아님) | 로즈 풀블리드는 CTA로 오독된다 |
| **B-n9** | Toast 위치·형태 | 상단 중앙 r999 pill(Settings 전용) | 하단 중앙 r8 + 좌측 컬러 바 | 손가락 도달 거리 + 색만으로 구분하지 않기 |
| **B-n10** | 상단 오프셋 | 88(상단바 없음, 여백만) | TopBar 56 + 24 = 80 | 웹은 상단바가 실물이다 |
| **B-n11** | TextBox 포커스 | border 1→2px(**레이아웃 시프트 발생**) | border 1→2px + padding 보정(시프트 없음) | 시프트는 결함이지 규격이 아니다 |
| **B-n12** | `--info` 토큰 | 대응 없음 | `#5c9dff` 유지 | 토스트 3톤(info/success/error) 구분에 필요 |

---

## 6. 구현 단계 (WBS)

> 형식: [`docs/templates/WBS_BLUEPRINT.md`](../templates/WBS_BLUEPRINT.md).
> **공통 검증 기준선**: `cd webclient && npx tsc --noEmit && npx vitest run` → **1926 통과 / 84파일** · `npx vite build` 성공.
> **모든 단계 공통 non-goal**: `src/**/*.ts(x)` 의 **로직 변경 0**. 이 작업은 CSS·토큰·마크업 클래스에 한정한다(예외: Step V4의 Home 장식 요소 2개, Step V3의 Toggle 마크업).

### Step V1: 토큰 전면 교체 — WPF 팔레트 · 라이트 기본 · `main.css` 중복 제거

- **Context Brief**: `webclient/src/ui/theme/tokens.css`가 다크 우선 22개 토큰만 갖고 있고, Windows 앱(`src/MCPhoto.App/Themes/Colors|Typography|Metrics.xaml`)은 라이트 전용 34색 + 타이포 8 + 메트릭 17을 갖는다. 게다가 `webclient/src/main.css:5-9`가 `:root`에 `--bg`·`--fg`·`--muted`를 **다시 정의**하는데 `main.tsx`가 tokens.css를 먼저, main.css를 나중에 import하므로 **라이트 모드가 실제로 깨져 있다**(흰 카드 위 흰 글자). 이 단계는 토큰 계층만 바꾸고 컴포넌트는 다음 단계에서 맞춘다.
- **대상 파일**: `webclient/src/ui/theme/tokens.css` · `webclient/src/main.css` · `webclient/index.html` · `webclient/public/manifest.webmanifest`
- **선행 조건**: 없음
- **구현 내용**:
  1. `tokens.css`를 §2.1~§2.6의 **"목표" 열 전량**으로 다시 쓴다. `:root`는 **라이트(WPF 값)**, `@media (prefers-color-scheme: dark)`는 §4.2 파생. `color-scheme: light dark`.
  2. **기존 토큰 이름을 지우지 않는다** — `--bg`·`--fg`·`--fg-muted`·`--border`·`--accent`·`--accent-fg`·`--radius`·`--radius-sm`·`--gap`·`--gap-sm`·`--gap-lg`·`--touch-min`·`--fs-title`·`--fs-body`·`--fs-caption`·`--shadow`·`--transition`·`--bg-scrim`·`--bg-elevated`·`--success`·`--danger`·`--info`는 **값만 바꾸거나 새 토큰의 별칭으로 남긴다**. 13개 모듈 CSS가 이 이름들을 쓰고 있어 한 번에 다 바꾸면 리뷰가 불가능해진다. 별칭 예: `--shadow: var(--shadow-pop);` `--bg-scrim: var(--scrim);` `--accent-fg: var(--on-accent);`
  3. `main.css:5-9`의 `:root` 블록에서 **`color-scheme`·`--bg`·`--fg`·`--muted`·`font-family` 스택을 제거**하고 `font-family: var(--font)`만 남긴다. `--muted` 소비처 2곳(`main.css:54`, `:65`)을 `var(--fg-muted)`로.
  4. `main.css:47` `.boot__title`의 `clamp(...)` → `var(--fs-display)`, `:64` `.version-caption` `0.75rem` → `var(--fs-caption)`, `:48` `font-weight:800` → `var(--fw-bold)`(700).
  5. `main.css:71-76`의 reduced-motion 전역 `!important` 규칙에 **`:not(.spinner):not([data-keep-motion])`** 류 제외를 넣거나, `components.module.css:66-70`의 스피너 규칙에 `!important`를 붙여 **2s가 이기게** 한다(§4.6).
  6. `index.html:9` theme-color → `#FFFFFF`, `:10` color-scheme → `light dark`. `manifest.webmanifest:11,12` → `#FFFFFF`.
- **검증 명령**:
  ```bash
  cd E:/Study/photobooth/webclient
  npx tsc --noEmit && npx vitest run && npx vite build
  grep -n "^\s*--bg:\|^\s*--fg:\|^\s*--muted:" src/main.css     # 0줄이어야 한다
  grep -c "0e0e12" src/ index.html public/manifest.webmanifest  # 0이어야 한다
  ```
- **완료 기준**:
  - [관측] `src/main.css`에 `:root { --bg | --fg | --muted }` 정의가 **0건**이고, 저장소 전체에서 `#0e0e12`가 **0건**이다. `tokens.css`의 `:root`가 §2 표의 목표값을 전부 포함한다(리뷰어가 표와 1:1 대조). `vitest` **1926 통과**(감소 없음).
  - [non-goal] **모듈 CSS 13개 파일을 이 단계에서 수정하지 않는다**(별칭으로 흡수). 기존 토큰 이름이 하나도 사라지지 않아 `var(--…)` 참조가 깨지지 않는다. `--info`는 유지된다.
  - [trigger] 다크 팔레트는 **`prefers-color-scheme: dark`에서만** 적용된다. 기본(no-preference·light)에서는 항상 WPF 라이트 값이다.
- **롤백**: 이 단계 커밋 revert. 토큰 파일 2개 + 정적 자산 2개만 바뀌므로 독립적이다.
- [ ] 완료

### Step V2: 하드코딩 색·치수 토큰화

- **Context Brief**: 모듈 CSS 13개에 CSS 변수를 쓰지 않고 색이 직접 박힌 곳이 **16곳** 있다(`#fff`·`#000`·`rgb(0 0 0/55%)`). Step V1에서 토큰을 바꿔도 **이 값들은 따라오지 않아** 라이트 팔레트에서 즉시 어긋난다(흰 배경 위 검은 카드 등). 단 **3종류는 의도적 고정**이라 남겨야 한다: QR 캔버스 배경(`screens.module.css:217` — 스캐너 호환, 주석에 이미 명시), 플래시 오버레이 2곳(물리적 흰색), canvas 픽셀 색(`qrService.ts`·`fallbackFrame.ts`).
- **대상 파일**: `webclient/src/ui/components/components.module.css` · `ui/views/screens.module.css` · `ui/views/cameraPreview.module.css` · `ui/views/frameSelect.module.css` · `ui/views/frameEditor.module.css` · `screens/modals/cameraTest/cameraTest.module.css`
- **선행 조건**: **Step V1**(토큰이 있어야 참조할 수 있다)
- **구현 내용**: §2.7 표의 "조치" 열을 그대로 적용한다(#1~#3은 V1에서 처리됨 → #4~#21).
  - 추가로 리터럴 `999px` 3곳(`screens.module.css:99,189` · `frameSelect.module.css:86`) → `var(--radius-pill)`.
  - `components.module.css:199` `.pinKey { min-height: 56px }` → `var(--touch-cta)`.
  - **유지 항목에는 반드시 한국어 주석으로 근거를 남긴다** — 다음 사람이 "토큰화 누락"으로 오해하고 고치는 것을 막는다. 예: `/* ⚠️ 토큰화 금지 — QR 스캐너 호환을 위해 다크에서도 흰 배경을 유지한다 */`
- **검증 명령**:
  ```bash
  cd E:/Study/photobooth/webclient
  npx tsc --noEmit && npx vitest run && npx vite build
  grep -rn "#fff\b\|#ffffff\|#000\b\|#000000\|rgb(0 0 0" src/ui src/screens --include=*.css
  # 남는 것은 정확히 4곳이어야 한다: screens:143(.flash) · screens:217(.qrCanvas) ·
  #                                    screens:134(text-shadow) · cameraTest:14(.flash)
  ```
- **완료 기준**:
  - [관측] 위 grep 결과가 **정확히 4곳**이고 각각 바로 위 줄에 유지 근거 한국어 주석이 있다. `vitest` 1926 통과.
  - [non-goal] `adapters/qr/qrService.ts:71,74`와 `adapters/frames/fallbackFrame.ts:41`의 canvas `fillStyle`은 **건드리지 않는다**(픽셀 데이터이지 UI 스타일이 아니다). 골든 이미지 테스트(`tests/golden/golden.test.ts`)가 통과한다.
  - [trigger] 색 변경은 CSS 변수 값이 바뀔 때만 따라온다 — 컴포넌트에 색 리터럴을 새로 넣지 않는다.
- **롤백**: 이 단계 커밋 revert.
- [ ] 완료

### Step V3: 공통 컴포넌트 정합 (Button 5종·Card·Field·Modal·Toast·TopBar·Banner·Spinner)

- **Context Brief**: `src/MCPhoto.App/Themes/Controls.xaml`의 컨트롤 스타일과 웹 컴포넌트가 형태부터 다르다. 특히 **Danger 버튼이 정반대**다(WPF=연분홍 배경+붉은 글자, 웹=붉은 배경+흰 글자). 버튼 높이도 WPF 56 vs 웹 48. WPF에는 hover/press/disabled 3단계가 전부 정의돼 있으나 웹에는 거의 없다. ⚠️ WPF 버튼 스타일 8종의 `Padding` setter는 템플릿에 바인딩되지 않아 **무효**이므로 그대로 옮기면 웹 버튼이 원본보다 넓어진다 — 폭은 `min-width` + 콘텐츠로 결정된다.
- **대상 파일**: `webclient/src/ui/components/components.module.css` · `ui/components/fields.module.css` · `ui/components/index.tsx`(Toggle 마크업만) · `ui/components/fields.tsx`(Toggle 마크업만) · `webclient/src/main.css`(전역 `:focus-visible`)
- **선행 조건**: **Step V1**
- **구현 내용**: §3.1~§3.4의 "목표" 열 전량.
  - 버튼 5종(Primary/Secondary/Ghost/Danger/Icon) — 색·높이 56·min-width·hover·active·disabled 전부.
  - Card·FrameCard·Modal·Scrim — 그림자 3단(`--shadow-sm/card/pop`) 도입.
  - TextBox/Select — caret 색·포커스 border 1→2px + **padding 보정으로 시프트 흡수**.
  - Toggle을 **스위치 형태**로(track 52×30 r15 · thumb 24 원 + `--shadow-sm`). ⚠️ **`transition`을 넣지 마라**(WPF는 즉시 스냅).
  - Segment(`.choice`) checked에서 **Bold로 굵어지는 것까지** 재현.
  - Spinner 56×56 · 5px · dash `20 10` · 1s linear.
  - Banner를 `--warning-surface`/`--warning`으로.
  - **전역 `:focus-visible` 폴백**을 `main.css`에 추가(`outline: 3px solid var(--fg); outline-offset: 2px`). Select·TextField·range가 포커스 링을 갖게 된다(§4.6).
- **검증 명령**:
  ```bash
  cd E:/Study/photobooth/webclient
  npx tsc --noEmit && npx vitest run && npx vite build && npx playwright test
  grep -n "min-height" src/ui/components/components.module.css   # .button 이 --touch-cta 여야 한다
  grep -rn "outline" src/main.css src/ui                          # 전역 폴백 1건 + 기존 2건
  ```
- **완료 기준**:
  - [관측] `.button`의 `min-height`가 `var(--touch-cta)`(56px)이고 `.danger`가 `background: var(--danger-surface); color: var(--danger)`다. `:hover`·`:active`·`:disabled` 규칙이 버튼 4종 모두에 존재한다. Playwright **44건 통과**(레이아웃 변경이 셀렉터를 깨지 않았다).
  - [non-goal] **터치 타깃이 48px 미만으로 내려간 요소가 하나도 없다**(전부 48 이상, CTA는 56). `outline: none`·`outline: 0`이 **어디에도 없다**. Toggle에 `transition`이 **없다**. `components.module.css:276`의 "색만으로 구분하지 않는다" 장치(토스트 좌측 컬러 바 + 아이콘 접두)가 유지된다.
  - [trigger] hover 스타일은 `:hover`에서만, press는 `:active`에서만 적용된다 — 터치 기기에서 hover가 눌린 상태로 고착되지 않도록 `@media (hover: hover)`로 감싼다.
- **롤백**: 이 단계 커밋 revert.
- [ ] 완료

### Step V4: P1 화면 정합 — Home(장식 원 포함) · Guide · Login

- **Context Brief**: 사용자가 "전체 배경이나 디자인 컨셉이 다르다"고 지목했을 때 실제로 본 것은 **Home 화면**이다. Windows `src/MCPhoto.App/Views/HomeView.xaml:9-12`에는 **파스텔 장식 원 2개**가 있다 — 좌상단 `Ellipse 360×360 Fill=Accent.Soft(#FFE7EE) Opacity=.6 Margin=-120,-120,0,0`, 우하단 `Ellipse 300×300 Fill=Accent2.Soft(#DFF6F1) Opacity=.6 Margin=0,0,-100,-100`. 웹에는 없다. Guide는 Windows에서 **Card(MinWidth 440)** 안에 들어 있고 정보 값이 Accent+Bold 우측 정렬이며 [촬영 시작]이 **h64 fs20 전폭**이다. Login도 **Card(Width 440)** 안이다.
- **대상 파일**: `webclient/src/ui/views/screens.module.css` · `ui/views/FlowViews.tsx`(HomeView·GuideView 마크업) · `ui/views/LoginView.tsx`
- **선행 조건**: **Step V1** · **Step V3**
- **구현 내용**:
  1. **Home**: §4.3의 장식 원 2개를 `aria-hidden="true"` div로 추가(또는 `.screen::before/::after`). 제목 `var(--fs-display)`(64) Bold, 부제 `var(--fs-h2)`(24) `var(--fg-tertiary)` + `margin: 8px 0 48px`, 버튼은 **`min-height:72px; font-size:1.5rem; padding:0 4.5rem`** 오버라이드.
     ⚠️ 장식 원은 `position:fixed; pointer-events:none; z-index:0`이고 콘텐츠는 `z-index:1` 이상. **`main.css:24`의 `overflow-x: hidden`이 음수 오프셋을 흡수한다** — 그 규칙을 지우지 마라.
  2. **Guide**: 기존 `dl` 을 **Card**(`background:var(--surface); border:1px solid var(--border); border-radius:var(--radius); padding:var(--space-l); box-shadow:var(--shadow-card); min-width:min(440px,100%)`)로 감싼다. 제목 H1 + `margin-bottom:28px`. 정보 행은 `display:flex; justify-content:space-between; margin-bottom:12px`이고 **값이 `color:var(--accent-text); font-weight:var(--fw-bold)`**. [촬영 시작]은 `min-height:64px; font-size:var(--fs-title-sm); width:100%`, [취소]는 Ghost 중앙 + 12px 간격.
     ⚠️ Step F7(다른 문서)이 이 화면에 카메라 권한 블록을 추가한다 — **클래스 이름이 충돌하지 않게** `.guideCard` 안에서 구조를 잡는다.
  3. **Login**: `.screen` 안을 Card(`width:min(440px,100%)`)로 감싼다. 제목 `margin-bottom:12px`, 안내 캡션 `margin-bottom:24px`, [Google로 로그인] **전폭**, [닫기] Ghost 중앙.
- **검증 명령**:
  ```bash
  cd E:/Study/photobooth/webclient
  npx tsc --noEmit && npx vitest run && npx vite build && npx playwright test
  ```
- **완료 기준**:
  - [관측] `HomeView`가 `aria-hidden` 장식 요소 **2개**를 렌더하고 각각 `--accent-soft`·`--accent2-soft` 배경 + `opacity:.6`을 갖는다. Guide의 정보 값이 `--accent-text` 색이다. Playwright 44건 통과.
  - [non-goal] 장식 원이 **가로 스크롤을 만들지 않는다**(`document.documentElement.scrollWidth <= clientWidth` — Playwright로 확인 가능). 장식 원이 **클릭을 가로채지 않는다**(`pointer-events:none`). 스크린리더가 장식을 읽지 않는다(`aria-hidden="true"`). Home의 **CTA는 여전히 1개**다.
  - [trigger] 장식은 `Home` 화면에서만 렌더된다 — 다른 화면으로 전이하면 사라진다(`position:fixed`이므로 반드시 화면 컴포넌트 안에 두어야 하고, 셸에 두면 전 화면에 남는다).
- **롤백**: 이 단계 커밋 revert.
- [ ] 완료

### Step V5: 나머지 10화면 정합 + 차이 보고서 등재 + 운영 문서

- **Context Brief**: P1 화면(Home·Guide·Login)과 공통 컴포넌트가 끝나면 남는 것은 Result·CutSelect·FrameSelect·Capture·Qr·Done·Settings·Account·UserMgmt·FrameEditor + 모달 3종이다. 대부분 **공통 토큰·컴포넌트 변경으로 이미 대부분 따라온다** — 이 단계에서 맞출 것은 §5.2 표의 "이번에 어디까지" 열에 적힌 화면별 여백·정렬·고유 수치뿐이다. 또한 웹에서 다르게 만든 것은 **전부 `docs/web-client/12`에 등재해야 한다**(등재되지 않은 차이는 버그로 취급하는 것이 이 저장소의 규칙).
- **대상 파일**: `webclient/src/ui/views/screens.module.css` · `settings.module.css` · `frameSelect.module.css` · `frameEditor.module.css` · `account.module.css` · `userMgmt.module.css` · `cameraPreview.module.css` · `screens/modals/*/*.module.css` · `docs/web-client/12-web-vs-windows-differences.md` · `docs/web-client/09-kiosk-operations.md` · `docs/web-client/03-screens-spec.md`(§1 디자인 토큰 절) · `docs/web-client/15-implementation-conventions.md`
- **선행 조건**: **Step V1~V4**
- **구현 내용**:
  1. §5.1의 공통 규약을 화면 CSS에 반영: 화면 좌우 여백 **40**(`--space-xl`), 가로 버튼 간격 **16**, 모달 내 버튼 간격 **12**, 카드 간격 **16**, 카드 목록 거터 **20**, 섹션 제목 하단 **16**, 라벨→입력 **4**.
  2. §5.2 표 P2~P4의 화면별 항목(Result 2열 `* | 340` · CutSelect 배지 28×28 r14 · FrameSelect 이름 바 h36 · Capture 칩 r6 · Qr 카드 440/padding 48 + QR 240 · Account MaxWidth 560 · Settings sticky 바 padding `40px 12px` + `--shadow-sm` 등).
  3. **`docs/web-client/12`에 §5.3의 B-n1~B-n12 등재.** §B(기술만 다름) 또는 §C(동작이 다름) 중 성격에 맞는 절에 넣고, 절 제목의 건수를 함께 갱신한다.
  4. **`docs/web-client/09`에 "표시 설정" 절 신설** — *"부스 기기의 OS를 **라이트 모드**로 둔다. 웹 클라이언트는 OS 설정을 따라가며, 다크 모드에서는 Windows 앱과 색이 달라진다(의도된 웹 전용 파생 — 12 B-n4)."* Windows/Android/iPadOS/macOS 4행 절차.
  5. **`docs/web-client/03 §1`(디자인 토큰)** 을 §2 표의 목표값으로 갱신 — 화면 명세가 낡은 토큰 이름을 가리키면 다음 사람이 되돌린다.
  6. **`docs/web-client/15 §3.4`** 불변식 표에 **`THEME-1`** 추가: *"`src/ui`·`src/screens`의 CSS에 색 리터럴이 정확히 4곳뿐이다(플래시 2 · QR 캔버스 1 · 카운트다운 text-shadow 1)."* → `tests/unit/ui/themeInvariants.test.ts` 신설로 고정.
- **검증 명령**:
  ```bash
  cd E:/Study/photobooth/webclient
  npx tsc --noEmit && npx vitest run && npx vite build && npx playwright test
  cd E:/Study/photobooth
  grep -c "B-n" docs/web-client/12-web-vs-windows-differences.md   # 12 이상
  grep -n "라이트 모드" docs/web-client/09-kiosk-operations.md       # 1줄 이상
  ```
- **완료 기준**:
  - [관측] 위 grep 2건이 기대대로다. 신설 `THEME-1` 테스트가 통과하며 색 리터럴 4곳을 확인한다. `vitest`가 **1926 + THEME-1 테스트 수**만큼 통과하고 Playwright 44건 통과.
  - [non-goal] `docs/spec-vectors/*`·`tests/golden/*` 를 **한 개도 건드리지 않는다**(픽셀 규격은 이 작업과 무관). Windows 소스(`src/MCPhoto.App/**`)를 **한 글자도 고치지 않는다** — 이 작업은 웹을 Windows에 맞추는 단방향이다.
  - [trigger] 다크 파생 팔레트는 OS가 다크일 때만 노출된다. 운영 문서의 라이트 고정 절차를 따르면 손님 화면에는 나타나지 않는다.
- **롤백**: 이 단계 커밋 revert(CSS + 문서).
- [ ] 완료

### 6.1 완결성 게이트 (self-check)

- [x] 검증된 사실(20) / 미검증 가정(4) 목록이 분리돼 있다
- [x] 모든 가정(B-1~B-4)에 검증 단계 또는 실측 항목이 매핑돼 있다
- [x] 5개 단계 전부에 7개 필수 필드가 있다
- [x] 완료 기준이 전부 관측 기반 3문 형식이다(전 단계가 UI이므로 전부 non-goal·trigger 포함)
- [x] 검증 명령이 전부 자동 실행 가능한 CLI다

---

## 7. 기존 불변식과의 관계

| 불변식 | 영향 | 판정 |
|--------|------|------|
| `WM1`(CSS 반전 금지 — `scaleX(-1)`·`rotateY(180deg)` 0건) | CSS를 대량 수정한다 | **주의.** 어떤 변환도 새로 넣지 않는다. 정적 테스트가 자동으로 막는다 |
| `01 §8` 가로 스크롤 금지 | Home 장식 원의 음수 오프셋 · 카드 고정 폭 | **설계로 회피**: 장식은 `overflow-x:hidden`이 흡수, 카드는 고정 px 대신 `minmax`. Step V4 완료 기준에 관측 항목으로 넣었다 |
| `01 §8` 터치 타깃 48px | CTA를 56으로 **올린다** | **강화**(위반 아님) |
| `01 §8` 다크·라이트 양쪽 지원 | 기본을 뒤집는다 | **유지** — `prefers-color-scheme: dark`가 계속 동작한다 |
| `01 §8` `prefers-reduced-motion` 존중 | 전역 `!important` 규칙을 손댄다 | **강화** — 지금은 스피너까지 멈춰 오히려 정보를 잃고 있다(W-12) |
| `SET-1`·`SET-2`(설정 화면 렌더 가드) | `settings.module.css`만 바꾼다 | **무영향**(TSX 로직 무변경) |
| `FR-5`·`FR-8`(화면 로컬 오버레이 · `pushModal` 금지) | 오버레이 CSS를 바꾼다 | **무영향** — 식별자·모달 구조를 건드리지 않는다 |
| `DIAG-1`(게이트 키 노출 금지) | `diagnostics.module.css`만 | **무영향** |
| 골든 이미지(`tests/golden/`) | canvas 픽셀 색은 건드리지 않는다 | **무영향**(Step V2 non-goal에 명시) |
| **신설 `THEME-1`** | 색 리터럴 4곳 고정 | 제약 **추가** |

**깨야 하는 불변식: 없음.**

⚠️ **team-lead 판정이 필요한 항목 1건**: §4.4의 **명암비 3.19:1**. WPF와 동일하게 만들면 AA 미달이고, AA를 지키면 Windows와 눈에 띄게 달라진다. 이 문서는 **WPF 일치**로 설계했고 후속 제안(H8)으로 등재한다. **다르게 판정하면 Step V1의 `--on-accent` 한 줄만 바꾸면 된다.**

---

## 8. 남는 실측 항목 (사람 몫 · [`14 §10`](../web-client/14-handoff-and-user-actions.md)에 V27로 신설 제안)

| # | 항목 | 확인 |
|---|------|------|
| V27-1 | **그림자가 Windows와 비슷해 보이는가**(가정 B-1) | Windows 앱과 웹을 나란히 띄우고 카드·모달 그림자를 비교. 너무 진하면 `--shadow-*`의 blur를 키우고 opacity를 낮춘다 |
| V27-2 | **다크 모드가 판독 가능한가**(가정 B-2) | OS를 다크로 전환해 13화면을 훑는다. 흰 글자 위 흰 배경 같은 조합이 **0건** |
| V27-3 | **라이트 모드에서 Windows와 나란히 놓고 비교**(가정 B-3) | Home·Guide·Result·FrameSelect 4화면. 배경·버튼색·카드 그림자·폰트 크기 |
| V27-4 | **태블릿 세로에서 가로 스크롤이 없다** | Home 장식 원이 있는 상태에서 좌우로 밀어 본다 |
| V27-5 | **터치 타깃이 56으로 커진 뒤 레이아웃이 넘치지 않는다** | Settings·UserMgmt처럼 컨트롤이 많은 화면 |
| V27-6 | **PWA 스플래시·탭 색이 흰색이다** | 설치 후 실행 · 모바일 탭 색 |
