# icon-forge — MC포토 앱 아이콘 생성기

`.exe`·작업표시줄·타이틀바에 쓰이는 앱 아이콘(`src/MCPhoto.App/Assets/app.ico`)을
**SVG 심볼 + 배경 합성**으로 굽는 도구다. 후보 8종과 원본 SVG를 함께 보관하므로,
나중에 디자인을 바꾸고 싶으면 아이콘을 새로 구할 필요 없이 여기서 교체하면 된다.

> 이 프로젝트는 `MCPhoto.sln`에 포함되지 않는다(앱 빌드와 무관한 개발 도구).

---

## 1. 현재 채택본

| 항목 | 값 |
|---|---|
| 채택 디자인 | **`03-rose-shutter`** — 로즈 그라디언트 배경 + 흰색 셔터(조리개) |
| 적용 위치 | `src/MCPhoto.App/Assets/app.ico` |
| 배선 | `MCPhoto.App.csproj` 의 `<ApplicationIcon>` |
| 포함 해상도 | 16 · 20 · 24 · 32 · 40 · 48 · 64 · 128 · 256 px (9프레임) |

`Window.Icon`은 **일부러 지정하지 않는다.** XAML로 지정하면 WPF가 단일 프레임을 골라
축소하느라 16px 타이틀바가 흐려진다. 미지정 시 Windows가 exe 리소스에서 용도별로
정확한 크기의 프레임을 선택하므로 더 선명하다.

---

## 2. 후보 목록

미리보기: `candidates/_preview-sheet.png` (위=밝은 배경, 아래=어두운 작업표시줄 시뮬레이션,
각 후보를 192·48·32·24·16px로 나열)
확대 검증: `candidates/_zoom-03.png` (채택본의 16~48px을 9배 nearest-neighbor 확대)

| 이름 | 심볼 | 배경 | 심볼색 | 메모 |
|---|---|---|---|---|
| `01-rose-camera` | photo_camera | 로즈 그라디언트 | 흰색 | 브랜드 정합성·소형 가독성 모두 무난. 안전한 대안 |
| `02-plum-camera` | photo_camera | 딥 플럼 | 로즈 | 차분함. 어두운 작업표시줄에서 배경과 섞임 |
| **`03-rose-shutter`** | camera(셔터) | 로즈 그라디언트 | 흰색 | **채택.** 추상적·브랜드적. 16px에선 날개가 다소 뭉갬 |
| `04-plum-shutter` | camera(셔터) | 딥 플럼 | 흰색 | 모노톤. 브랜드 컬러가 드러나지 않음 |
| `05-light-camera` | photo_camera | 화이트→연핑크 | 로즈 | 미니멀. 어두운 배경에서 흰 타일이 강하게 튐 |
| `06-rose-library` | photo_library | 로즈 그라디언트 | 흰색 | 사진 묶음 은유. 카메라보다 덜 직관적 |
| `07-plum-mint` | photo_camera | 딥 플럼 | 민트 | Accent2(`#37C9B0`) 조합 |
| `08-rose-bscam` | camera-fill | 로즈 그라디언트 | 흰색 | 01과 유사하나 렌즈 표현이 덜 정제됨 |

색상은 앱 팔레트(`src/MCPhoto.App/Themes/Colors.xaml`)를 계승한다 —
Accent `#FF4D79`, Accent2 `#37C9B0`, Text.Primary `#241F2B`.

---

## 3. 아이콘 교체

### 3-1. 기존 후보로 바꾸기

```powershell
Copy-Item tools\icon-forge\candidates\01-rose-camera.ico `
          src\MCPhoto.App\Assets\app.ico -Force
dotnet build src\MCPhoto.App\MCPhoto.App.csproj -c Release
```

> 탐색기·작업표시줄은 아이콘을 캐시한다. 바뀌지 않아 보이면 캐시를 지우고 탐색기를 재시작한다:
> `ie4uinit.exe -show` (Win11) 또는 재로그인.

### 3-2. 새 디자인 추가

1. `svg/` 에 SVG를 넣는다(단색 `fill` 방식 권장 — stroke 기반은 렌더되지 않는다).
2. `Program.cs` 의 `Designs` 배열에 항목을 추가한다.
3. 다시 굽는다:

```powershell
dotnet run --project tools\icon-forge\IconForge.csproj -c Release -- `
    tools\icon-forge\svg tools\icon-forge\candidates
```

`candidates/` 에 `<이름>.ico`, `<이름>-256.png`, 그리고 미리보기 시트가 갱신된다.

### 3-3. 동작 원리 (의존성 없음)

ImageMagick·Inkscape·Pillow 없이 **WPF만으로** 처리한다.

- SVG `path`의 `d` 속성은 WPF `Geometry.Parse`와 문법이 거의 같다.
  다만 SVG가 허용하는 `a.5.5 0` 같은 연속 소수점 토큰은 WPF 파서가 못 읽어
  `NormalizePath()`가 공백을 끼워 분리한다.
- 각 해상도를 **개별 렌더**한다(큰 이미지를 축소하지 않음). 작은 사이즈일수록
  여백을 회수해(16px→7%, 256px→17.5%) 형태가 뭉개지는 것을 늦춘다.
- `.ico` 컨테이너는 직접 인코딩한다. 64px 이상은 PNG 압축, 그 미만은
  32bpp DIB(구형 셸 호환). 알파는 32bpp 채널이 담당하므로 AND 마스크는 0으로 채운다.

---

## 4. 라이선스

모두 **귀속 표시 없이 상업적 이용이 가능한** 라이선스만 사용했다.
상용 배포(인스톨러) 시 고지 문서에 아래를 포함하면 안전하다.

| 원본 | 파일 | 라이선스 | 출처 |
|---|---|---|---|
| Google Material Symbols / Material Icons | `ms_*.svg`, `mi_*.svg` | Apache License 2.0 | https://github.com/google/material-design-icons |
| Bootstrap Icons | `bs_*.svg` | MIT | https://github.com/twbs/icons |
| Lucide | `lucide_*.svg` | ISC | https://github.com/lucide-icons/lucide |

`svg/` 안에는 실제 디자인에 쓰이지 않은 심볼도 함께 보관한다
(`ms_local_see`, `ms_auto_awesome`, `mi_photo_camera`, `lucide_camera`) — 향후 교체용 재료다.
`lucide_camera`는 stroke 기반이라 현재 렌더러로는 채워지지 않으므로 그대로는 쓸 수 없다.
