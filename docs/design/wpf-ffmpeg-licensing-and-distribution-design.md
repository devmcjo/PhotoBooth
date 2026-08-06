# ffmpeg 라이선스 준수 & 배포 형태 설계

> 입력: 사용자 질의(§0.1 원문), 현행 코드 전수 확인(§1), 번들 바이너리 실측(§1.3), 배포처 공개 정보(§8)
> 작성: 2026-07-31 (검토·기록 전용) → **경로 1 착수·완료: 2026-08-06**
>
> ## 상태
>
> | 경로 | 상태 |
> |------|------|
> | **경로 1 — 준수 이행**(§5.1) | ✅ **완료(2026-08-06)**. 구현 결과·검증은 **§10** 참조. **P2(라이선스 위반 상태) 해소됨** |
> | 경로 2 — LGPL 빌드 + `h264_mf`(§5.2) | ⏸ 미착수. UV-1 게이트(§7)부터. **현행 GPL 번들에 `h264_mf`가 있음은 재확인**(§10.4) |
> | 경로 3 — MF 직접 호출(§5.3) | ⏸ 미착수 |
> | 방안 A — 임베드(§3.1) | ⏸ 미착수(P1 전용, 라이선스와 무관) |
>
> 경로 2·3은 **P1(97MB 동반)** 만 남은 문제이며 법적 압박이 없다 — 일정에 맞춰 결정하면 된다(§4.2).

---

## §0 개요

### 0.1 질의 원문 (축약 금지)

> windows 앱에서 tool/ffmpeg/ffmpeg.exe 가 항상 따라다녀야하는데, 이거를 내 MCPhoto.exe 에 이식할 방법이 있어? 혹시 이게 저작권이나 이런거에 위배되는 사항이야?

> ffmpeg.exe 를 같이 배포하는 것 자체는 위반이라는거지? 어떻게 방법이 없어?

### 0.2 두 개의 서로 다른 문제

질의에는 성격이 다른 두 문제가 섞여 있다. **분리해서 판단해야 한다.**

| # | 문제 | 성격 | 현재 상태 |
|---|------|------|-----------|
| **P1** | ffmpeg.exe 97MB가 배포물에 따라다닌다 | **배포 편의** — 미해결이어도 법적 문제 없음 | 허용 가능한 불편 |
| **P2** | GPLv3 바이너리를 조건 미이행 상태로 배포 중 | **라이선스 준수** — 실제 위반 상태 | ⚠️ **미해결. 즉시 조치 대상** |

P1을 해결하는 방법(임베드·커스텀 빌드)은 **P2를 해결하지 않는다.** 반대로 P2는 코드를 한 줄도 바꾸지 않고 해결할 수 있다. 두 문제를 한 덩어리로 다루면 "단일 exe를 만들었으니 라이선스도 해결됐다"는 잘못된 결론에 도달한다.

### 0.3 핵심 판정 요약

| # | 쟁점 | 판정 | 근거 절 |
|---|------|------|---------|
| 1 | ffmpeg.exe 동봉 배포 자체가 위반인가 | **아니다.** GPL은 재배포를 명시적으로 허용한다. **조건 미이행**이 위반이며 현재 그 상태다 | §2.2 |
| 2 | MCPhoto 소스를 공개해야 하는가 | **아니다.** 별도 프로세스 + CLI/파이프 통신은 파생저작물이 아니다. MIT 유지 가능 | §2.3 |
| 3 | 현재 이행해야 할 의무 | ① GPLv3 전문 동봉 ② 저작권·사용 고지 ③ **대응 소스 접근 제공** | §2.4 |
| 4 | exe에 임베드하면 의무가 사라지는가 | **아니다.** 동일하다. 오히려 고지 누락 위험이 커진다 | §3.1 |
| 5 | 커스텀 최소 빌드는 유리한가 | **불리하다.** 소스 제공 책임과 CVE 재빌드 책임이 제3자에서 **우리로 이전**된다 | §3.3 |
| 6 | GPL을 벗어나는 방법이 있는가 | **있다.** LGPL 빌드 + `h264_mf`(§5.2), 또는 ffmpeg 제거(§5.3) | §5 |
| 7 | 권고 | **경로 1(준수 이행) 즉시 → 경로 2(LGPL 전환) 검증 → 필요 시 경로 3** | §4.2 |

---

## §1 검증된 사실 (verified facts)

### 1.1 코드 — ffmpeg 사용 지점 전수

| VF | 사실 | 근거 |
|----|------|------|
| VF-1 | ffmpeg는 **별도 프로세스**로 실행된다. 라이브러리 링크·P/Invoke 없음 | `src/MCPhoto.Capture/FfmpegRunner.cs:60-72`(`ProcessStartInfo`+`Process.Start`), `:175` |
| VF-2 | 통신은 **CLI 인자 + stdin 파이프(rawvideo BGR24) + stderr** 뿐이다 | `FfmpegRunner.cs:63-65`, `:78`(`StandardInput.BaseStream`), `:107` |
| VF-3 | 탐색 순서 = `{BaseDir}/tools/ffmpeg/ffmpeg.exe` → `{BaseDir}/ffmpeg.exe` → **PATH의 `ffmpeg`** | `FfmpegRunner.cs:35-48` |
| VF-4 | 부재 시 안전 실패한다(`IsAvailable`=`File.Exists`). 타임랩스는 `null` 반환 | `FfmpegRunner.cs:30`, `src/MCPhoto.Capture/TimelapseService.cs:26-30` |
| VF-5 | **인코더는 `libx264` 고정**(녹화·타임랩스 양쪽) | `src/MCPhoto.Core/Capture/FfmpegArgs.cs:50`, `:74` |
| VF-6 | 사용하는 필터는 **`crop`·`setpts`·`fps` 3개뿐**이다 | `FfmpegArgs.cs:30`(`EvenDimensionCrop`), `:67` |
| VF-7 | 화질 지정은 **CRF 방식**(`-crf 20 -preset veryfast`) | `FfmpegArgs.cs:51-52`, `:75` |
| VF-8 | 배속 산출은 **순수 로직**이라 인코더와 무관하게 재사용 가능 | `FfmpegArgs.cs:84-90`(`ComputeSpeedFactor`), `:93`(`ExpectedOutputSeconds`) |
| VF-9 | `FfmpegRunner`는 **구상 타입 그대로** 3곳에 물려 있다(인터페이스 없음) | `OpenCvCameraService.cs:26`·`:34`·`:42`·`:45`·`:283`, `TimelapseService.cs:12`·`:18`, `DiagnosticsViewModel`(직접 주입 — 근거: [it11 설계 §290-298](./wpf-it11-deferred-features-design.md)) |
| VF-10 | DI는 Singleton 등록 | `src/MCPhoto.App/ServiceRegistration.cs:53` (근거: [analysis/10 §93](../analysis/10-exe-app-architecture.md)) |

### 1.2 배포 경로

| VF | 사실 | 근거 |
|----|------|------|
| VF-11 | csproj가 빌드 출력과 publish 산출물 양쪽에 ffmpeg.exe를 복사한다 | `src/MCPhoto.App/MCPhoto.App.csproj:52-72` (`None` 항목 + `CopyFfmpegToPublish` 타겟) |
| VF-12 | 인스톨러가 publish 산출물 **전체**를 담는다 → ffmpeg.exe가 고객 PC로 간다 | `installer/MCPhoto.iss:37-40` |
| VF-13 | publish는 self-contained 단일 파일이지만 **ffmpeg.exe는 번들 밖 별도 파일**이다 | `publish.ps1:57-64`, `MCPhoto.App.csproj:65-72`(주석에 그 이유 기록) |
| VF-14 | ffprobe는 이미 제외됐다(코드 미사용) | `MCPhoto.App.csproj:52` 주석, [analysis/90 §1](../analysis/90-roadmap-and-future-work.md) "ffprobe 잔존 — 정리 완료" |
| VF-15 | MCPhoto 자체 라이선스는 **MIT** | `LICENSE:1` |

### 1.3 번들 바이너리 실측 (2026-07-31 확인)

```
ffmpeg version 8.1.2-essentials_build-www.gyan.dev
파일: tools/ffmpeg/ffmpeg.exe · 97.18 MB · 2026-06-27
configuration: --enable-gpl --enable-version3 --enable-static --disable-autodetect
  --enable-libx264 --enable-libx265 --enable-libxvid --enable-libaom --enable-libvpx
  --enable-mediafoundation --enable-libass --enable-librubberband --enable-libgme
  --enable-libmp3lame --enable-libopus --enable-libvorbis --enable-nvenc --enable-amf ...
```

| VF | 사실 | 함의 |
|----|------|------|
| VF-16 | **`--enable-gpl` + `--enable-version3` → 최종 라이선스는 GPLv3** | 재배포 시 GPLv3 의무 발생 |
| VF-17 | `--enable-static` — 모든 외부 라이브러리가 이 exe 하나에 정적 링크됨 | 대응 소스 범위 = ffmpeg + **링크된 모든 GPL/LGPL 라이브러리** |
| VF-18 | GPL 라이브러리가 **다수** 포함(x264, x265, xvid, vidstab, rubberband, gme …). 실제 사용은 **libx264 하나** | 불필요하게 넓은 준수 범위(§3.3) |
| VF-19 | **`h264_mf`(H264 via MediaFoundation) 인코더가 이미 포함되어 있다** | LGPL 전환의 열쇠(§5.2). `-encoders` 실행으로 확인 |
| VF-20 | `h264_nvenc`·`h264_amf`·`h264_qsv`·`h264_d3d12va`도 포함 | 하드웨어 경로도 GPL 불요(단 기기 의존) |

### 1.4 배포처 공개 정보

| VF | 사실 | 출처 |
|----|------|------|
| VF-21 | gyan.dev는 essentials/full 모두 **GPL 빌드만** 제공한다 | [gyan.dev/ffmpeg/builds](https://www.gyan.dev/ffmpeg/builds/) |
| VF-22 | BtbN은 **win64 LGPL 정적 빌드**를 제공한다 — `ffmpeg-n8.1.2-32-gcfa62de001-win64-lgpl-8.1.zip`(현행과 동일 8.1.2 계열) | [BtbN/FFmpeg-Builds Wiki: Latest](https://github.com/BtbN/FFmpeg-Builds/wiki/Latest) |
| VF-23 | BtbN 빌드 변형은 `variants/`의 `win64-gpl.sh`/`win64-lgpl.sh`가 각각 `defaults-gpl.sh`/`defaults-lgpl.sh`를 source 하는 구조 | [BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds) |

> ⚠️ **미검증(§7 UV-1)**: BtbN **LGPL** 빌드에 `h264_mf`가 실제로 포함되는지는 바이너리로 확인하지 않았다. MediaFoundation은 ffmpeg configure에서 GPL을 요구하지 않는 항목이고, BtbN 이슈 #33에서 GPL·LGPL 빌드 **양쪽**이 `MFPlat.DLL`을 요구했다는 보고가 있어 포함 가능성이 높지만, **착수 시 zip을 받아 `ffmpeg -encoders`로 직접 확인해야 한다.**

---

## §2 라이선스 판정

### 2.1 무엇이 어떤 라이선스인가

| 대상 | 라이선스 | 비고 |
|------|----------|------|
| MCPhoto 전체 소스 | **MIT** (`LICENSE:1`) | 변경 불요 |
| `tools/ffmpeg/ffmpeg.exe` | **GPLv3** | `--enable-gpl --enable-version3` 결합 결과(VF-16) |
| 그 안에 정적 링크된 libx264 | GPLv2 or later | 이것이 GPL을 끌어들이는 주범 |
| FFmpeg 자체 코어 | LGPL 2.1+ | GPL 라이브러리를 빼면 LGPL로 배포 가능 |

### 2.2 "동봉 배포 = 위반"은 오해다

GPL의 목적은 재배포 **금지**가 아니라 재배포 시 **수령자의 권리 보장**이다. 조건을 지키는 재배포는 완전히 합법이며, 상용 제품에 GPL 도구를 동봉하는 것도 마찬가지다.

```
GPL 바이너리 동봉 배포  →  그 자체로는 합법
        └─ 조건 미이행 →  위반  ← ★ 현재 여기
        └─ 조건 이행   →  합법  ← 경로 1(§5.1)이 도달하는 지점
```

즉 **현재 상태의 문제는 "ffmpeg를 넣었다"가 아니라 "넣고 아무 고지도 소스 안내도 하지 않았다"** 이다. 이 구분이 이 문서 전체의 전제다.

### 2.3 MCPhoto 소스 공개 의무는 없다

GPL의 전염은 **파생저작물(derivative work)** 에 미친다. 판단 기준은 결합의 긴밀도이며, 통용되는 해석은 다음과 같다.

| 결합 형태 | 파생저작물인가 | 이 프로젝트 |
|-----------|----------------|-------------|
| 정적 링크(libav*를 exe에 링크) | **예** — 전체가 GPL | ❌ 해당 없음 |
| 동적 링크(DLL P/Invoke, FFmpeg.AutoGen 등) | 예(GPL의 경우) | ❌ 해당 없음 |
| **별도 프로세스 + CLI/파이프** | **아니오** — aggregation | ✅ **현재 구조**(VF-1, VF-2) |

`Process.Start`로 exe를 띄우고 인자와 stdin으로만 통신하는 구조는 파생저작물로 보지 않는 것이 FSF를 포함한 일반적 해석이다. **MCPhoto는 MIT를 유지한다.**

> ⚠️ 이 성질은 **구조에 의존한다.** 성능을 이유로 libav*를 직접 링크하는 순간 MCPhoto 전체가 GPLv3가 되어 MIT 배포가 불가능해진다. 어떤 방안을 택하든 **직접 링크는 선택지에서 영구 제외**한다.

### 2.4 이행해야 할 의무 (GPLv3)

| # | 의무 | 근거 조항 | 구체적 산출물 |
|---|------|-----------|---------------|
| **O1** | 라이선스 전문 전달 | §4 | `licenses/FFmpeg-COPYING.GPLv3.txt` |
| **O2** | 저작권 고지 유지 + GPL 적용 사실 명시 | §4 | 설치 폴더 `licenses/README.txt` + 앱 내 고지(§5.1) |
| **O3** | **대응 소스(Corresponding Source) 접근 제공** | §6 | §2.5 참조 — 가장 실수하기 쉬운 항목 |
| **O4** | 수정했다면 수정 사실 표시 | §5(a) | 해당 없음(바이너리 무수정 재배포) |
| **O5** | 추가 제약 부과 금지 | §10 | EULA에 ffmpeg 리버스엔지니어링 금지 등을 걸지 말 것 |

### 2.5 O3(소스 제공)의 방식 선택 — 배포 매체에 따라 달라진다

GPLv3 §6은 배포 형태별로 다른 옵션을 준다. **이 프로젝트는 두 매체를 동시에 쓸 가능성이 높아 둘 다 봐야 한다.**

| 배포 형태 | 적용 조항 | 해야 할 일 |
|-----------|-----------|------------|
| **웹에서 인스톨러 다운로드** | §6(d) | 바이너리를 받는 곳 **옆에** 소스 위치를 명시. 소스는 제3자 서버(gyan.dev)여도 되지만, 바이너리를 제공하는 동안 접근이 유지되어야 한다 |
| **USB·현장 설치 등 물리 매체** | §6(a) 또는 §6(b) | (a) 소스를 같은 매체에 동봉, **또는** (b) **3년간 유효한 서면 소스 제공 오퍼**를 동봉 |

> ⚠️ **주의**: 키오스크 현장에 USB로 설치하는 동선이 있다면 §6(d)만으로는 부족하다. 실무적으로 가장 안전한 조합은 **자사 서버에 소스 사본을 미러링 + 배포물에 URL과 3년 오퍼를 함께 기재**하는 것이다.

**대응 소스의 범위**(VF-17로 인해 넓다): ffmpeg 8.1.2 소스 + **정적 링크된 모든 GPL/LGPL 라이브러리 소스** + 빌드 스크립트·configuration. gyan.dev가 릴리스별 소스와 빌드 스크립트를 공개하므로 재배포자는 그것을 가리키거나 미러링하면 된다 — **직접 빌드하지 않는 한** 이 부담은 상대적으로 가볍다(§3.3과 대비).

### 2.6 라이선스와 무관한 별건들

| 항목 | 내용 | 판단 |
|------|------|------|
| **H.264 특허** | Via LA(구 MPEG LA) 특허풀 대상. 저작권 라이선스와 **완전히 별개** | 상용 확대 시 검토 대상. `h264_mf`/MF 직접 호출로 가면 OS에 이미 라이선스된 인코더를 쓰는 것이라 부담이 실질적으로 이관된다 |
| **FFmpeg 상표** | 제품명·브랜딩에 "FFmpeg"를 쓰면 상표 정책 대상 | 해당 없음(내부 도구로만 사용). 고지문에 "uses FFmpeg" 표현은 문제 없음 |
| **GPLv3 설치 정보(§6 후단)** | "User Product"에 설치된 경우 설치 정보 제공 의무 | 일반 Windows PC 설치라 사용자가 자유롭게 교체 가능 → 해당 없음. 단 **완전 락다운된 전용 기기로 납품하는 형태가 생기면 재검토** |

---

## §3 P1(배포물 동반) 해법과 그 한계

### 3.1 방안 A — EmbeddedResource + 런타임 추출

ffmpeg.exe를 어셈블리 리소스로 넣고 첫 실행 시 디스크에 풀어 실행한다.

```xml
<EmbeddedResource Include="$(FfmpegSource)" LogicalName="ffmpeg.exe" />
```

추출 위치는 `%LOCALAPPDATA%\MCPhoto\ffmpeg\{버전해시}\ffmpeg.exe`. `ResolveFfmpegPath()`(VF-3)의 후보 목록 **맨 앞**에 이 경로를 추가하면 나머지 코드는 무변경이다.

| 항목 | 평가 |
|------|------|
| 단일 exe | ✅ 달성 |
| 코드 변경 | 소(추출 유틸 + `ResolveFfmpegPath` 후보 1줄) |
| exe 크기 | +30~40MB(압축 후) |
| **라이선스** | ❌ **변화 없음.** O1~O3 그대로 |
| 리스크 | ⚠️ 서명 없는 exe를 런타임에 드롭 → **백신 휴리스틱 오탐**. 코드 서명으로 완화 / Program Files 아래는 쓰기 불가라 위치 선정 주의 / 첫 실행 지연 |

> **고지 관점 주의**: 임베드하면 사용자 눈에서 ffmpeg가 사라진다. 임베드 자체는 GPL 위반이 아니지만("mere aggregation"), 고지 없이 숨기면 O2 위반이 더 선명해진다. **임베드한다면 앱 내 고지는 필수다.**

### 3.2 방안 B — `IncludeAllContentForSelfExtract=true`

**채택 불가.** 코드 0줄이라 매력적이지만 `Frame/`(`MCPhoto.App.csproj:75-79`)과 `branding.ini.sample`(`:82-84`)까지 전부 번들에 들어가 임시 추출 폴더로 이동한다. **고객이 프레임을 교체·추가하는 설계(PRD §9 #11 우선순위 ②)가 깨진다.** 항목별 선택 제어 수단이 없다.

### 3.3 방안 C — 커스텀 최소 빌드 (⚠️ 역효과)

실제 사용 범위(VF-5·VF-6)만 남기면 5~10MB까지 줄어든다.

```
--disable-everything --enable-gpl --enable-libx264
--enable-decoder=rawvideo,h264 --enable-encoder=libx264
--enable-demuxer=rawvideo,mov --enable-muxer=mp4
--enable-filter=crop,setpts,fps --enable-protocol=file,pipe
```

**그럼에도 권장하지 않는다. 라이선스 부담을 줄이는 게 아니라 늘리기 때문이다.**

| 지금(재배포) | 직접 빌드 후 |
|---------------|--------------|
| 소스 제공을 gyan.dev 공개 소스·빌드 스크립트로 충족 가능 | **내 configuration에 대응하는 소스는 세상에 나뿐** → 미러링·오퍼를 내가 지속 운영 |
| ffmpeg CVE 발생 시 gyan.dev 갱신본 교체로 끝 | **CVE마다 내가 재빌드·재검증·재배포** |
| 빌드 환경 불요 | MSYS2/mingw + libx264 빌드 체인을 팀이 유지. 담당자 이탈 시 보안 패치 정지 |

용량 92MB를 줄이는 대가로 **GPL 배포자 책임을 더 무겁게 떠안는 거래**다. §5.2가 성립하면 이 방안은 존재 이유가 사라진다.

---

## §4 방안 종합 비교

### 4.1 비교표

| 경로 | 배포물 증가 | 남는 라이선스 의무 | 코드 변경 | 공수 | P1 | P2 |
|------|-------------|--------------------|-----------|------|----|----|
| **1. 현행 + 준수 이행**(§5.1) | 0 (97MB 유지) | GPLv3 O1~O3 (이행됨) | 없음 | 반나절 | ❌ | ✅ |
| **2. LGPL 빌드 + `h264_mf`**(§5.2) | 감소(실측 필요) | LGPL 고지 + 소스 링크 | `FfmpegArgs` 2개 메서드 | 1~2일 | △ | ✅✅ |
| **3. MF 직접 호출**(§5.3) | **-97MB** | **없음** | 신규 추상화 + 구현 | 1~2주 | ✅ | ✅✅✅ |
| A. 임베드(§3.1) | 단일 exe화 | 변화 없음 | 소 | 1일 | ✅ | ❌ |
| B. SelfExtract(§3.2) | — | 변화 없음 | 0 | — | — | **채택 불가** |
| C. 커스텀 빌드(§3.3) | -90MB | **증가** | 없음 | 3~5일+상시 | ✅ | ❌(악화) |

A는 1·2·3 어느 경로와도 **조합 가능한 부가 옵션**이다(3을 택하면 불필요해진다).

### 4.2 권고 — 1 → 2 → (필요 시) 3

1. **경로 1을 먼저, 단독으로 한다.** 반나절이며 코드를 바꾸지 않는다. 이후 어떤 경로로 가든 그 사이 나가는 배포를 합법 상태로 만든다. 이것을 뒤로 미룰 이유가 없다.
2. **경로 2를 검증한다.** UV-1(§7)이 통과하면 경로 3의 큰 공수와 COM 인터롭 리스크 없이 GPL을 벗어난다. 코드 변경이 `FfmpegArgs`의 인코더 지정 두 줄 수준이다.
3. **그래도 "파일이 따라다니는 게 싫다"가 남으면 경로 3 또는 A.** 이 시점의 잔여 문제는 P1(배포 편의)뿐이며, 라이선스 압박이 없으므로 일정에 맞춰 결정하면 된다.

**경로 3을 처음부터 택하지 않는 이유**: P2를 1~2주간 방치하게 된다. 경로 1이 반나절인데 그럴 이유가 없다.

---

## §5 경로별 실행 설계

### 5.1 경로 1 — 준수 이행 (코드 무변경)

| Step | 작업 | 대상 |
|------|------|------|
| 1-1 | `licenses/FFmpeg-COPYING.GPLv3.txt` 추가(GPLv3 전문) | 신규 |
| 1-2 | `licenses/FFmpeg-README.txt` 추가 — 버전(8.1.2-essentials, gyan.dev), **`ffmpeg -version`의 configuration 문자열 전문**, 소스 URL, 3년 오퍼 문구 | 신규 |
| 1-3 | 자사 서버(또는 사내 저장소)에 해당 릴리스 **소스 사본 미러링** + 그 URL을 1-2에 기재 | 인프라 |
| 1-4 | 인스톨러가 `licenses/`를 설치하도록 `[Files]` 항목 추가 | `installer/MCPhoto.iss` |
| 1-5 | csproj가 `licenses/`를 출력에 복사 | `MCPhoto.App.csproj` |
| 1-6 | 앱 내 고지 — 진단 창(`DiagnosticsWindow`)에 "오픈소스 라이선스" 표기 + 라이선스 폴더 열기 버튼. `ILogFolderService`의 폴더 열기 패턴을 재사용 | `Views/DiagnosticsWindow.xaml`, `ViewModels/DiagnosticsViewModel.cs` |

> 1-6의 위치는 [analysis/90 §6](../analysis/90-roadmap-and-future-work.md)의 "개발자 문의 공간"(설정 → 버전 확인 모달) 구상과 **같은 모달에 함께 넣는 것이 자연스럽다.** 그 항목을 착수할 때 묶으면 UI 작업이 한 번으로 끝난다.

**검증**: 클린 PC에 인스톨러 설치 → `licenses/` 존재 확인 → 기재된 소스 URL이 실제로 살아 있는지 확인.

### 5.2 경로 2 — LGPL 빌드 + `h264_mf`

**착상**: GPL을 끌어들이는 것은 libx264 하나뿐이다(VF-18). H.264 인코딩을 **Windows 내장 인코더**(`h264_mf`, VF-19)로 대체하면 libx264가 필요 없고, LGPL 빌드로 교체할 수 있다.

| Step | 작업 | 대상 |
|------|------|------|
| 2-0 | **UV-1 검증** — BtbN LGPL zip을 받아 `ffmpeg -encoders`로 `h264_mf` 확인. 실패 시 경로 2 폐기, 경로 3으로 | 선행 게이트 |
| 2-1 | `-c:v libx264 -crf 20 -preset veryfast` → `-c:v h264_mf -b:v {계산값}` | `FfmpegArgs.cs:50-52`, `:74-75` |
| 2-2 | 비트레이트 산식 추가(해상도·fps 기반). CRF 대체물이므로 **순수 로직 + 단위 테스트** | `FfmpegArgs.cs`(신규 메서드), `tests/MCPhoto.Tests/FfmpegArgsTests.cs` |
| 2-3 | 번들 바이너리를 LGPL 빌드로 교체 | `tools/ffmpeg/ffmpeg.exe` |
| 2-4 | 고지를 LGPL 기준으로 재작성(경로 1 산출물 갱신) | `licenses/` |
| 2-5 | 화질·속도 A/B 검증 — 동일 세션 원본으로 libx264 결과와 비교 | 실기 |

**남는 의무**: LGPL도 고지와 소스 접근 제공은 필요하다. 다만 ① GPL 감염 논쟁이 원천 소멸하고 ② 별도 exe라 "라이브러리 교체 가능성"(LGPL §4/§6) 조건이 구조적으로 자동 충족되며 ③ 준수 범위가 LGPL 라이브러리로 좁아진다.

**주의**: `h264_mf`에는 CRF가 없다. 화질 제어 모델이 품질 기준 → 비트레이트 기준으로 바뀌므로 `-crf 20`과 동등한 결과를 얻으려면 실측 튜닝이 필요하다. **이 숙제는 경로 3으로 가도 동일하게 발생한다** — 경로 2에서 먼저 풀어두면 경로 3의 선행 작업이 된다.

### 5.3 경로 3 — Media Foundation 직접 호출 (ffmpeg 제거)

**전제**: 현재 ffmpeg 사용 범위가 극히 좁다(VF-5·VF-6). 아래 대응이 성립한다.

| 현재 ffmpeg | MF 대응 |
|-------------|---------|
| rawvideo BGR24 stdin → libx264 mp4 (`FfmpegArgs.cs:36`) | `IMFSinkWriter` — 입력 `MFVideoFormat_RGB24`, 출력 `MFVideoFormat_H264` |
| `crop=trunc(iw/2)*2:trunc(ih/2)*2` (`FfmpegArgs.cs:30`) | 짝수 보정을 C#에서 산술 처리(이미 순수 로직) |
| `setpts=(1/N)*PTS, fps=30` (`FfmpegArgs.cs:67`) | `IMFSourceReader`로 디코드 → 프레임 샘플링 + 타임스탬프 재계산 |
| `-an` | 오디오 스트림 미추가 |
| `-crf 20` | `MF_MT_AVG_BITRATE` (경로 2에서 만든 산식 재사용) |

**선행 리팩터링(필수)**: VF-9대로 `FfmpegRunner`가 구상 타입으로 3곳에 직접 물려 있다. `IVideoRecorder`(녹화) / `IVideoTranscoder`(타임랩스) 추상화를 먼저 도입해야 두 구현을 나란히 둘 수 있다.

```
IVideoRecorder     ← FfmpegVideoRecorder | MediaFoundationVideoRecorder
IVideoTranscoder   ← FfmpegTranscoder    | MediaFoundationTranscoder
        │
        └─ ini `VideoEncoder=MediaFoundation|Ffmpeg` 로 선택(폴백 경로 유지)
```

**핵심**: **ffmpeg 코드를 삭제할 필요가 없다.** GPL 의무는 *배포*에서 발생하므로 `MCPhoto.App.csproj:52-72`의 번들 복사와 인스톨러에서 ffmpeg.exe만 빼면 그 시점에 의무가 소멸한다. `ResolveFfmpegPath()`의 **PATH 폴백**(VF-3)이 이미 있어, 현장에서 MF 인코딩에 문제가 생기면 그 PC에만 ffmpeg를 두어 즉시 우회할 수 있다.

**타임랩스 대안 설계**: `session.mp4`를 다시 디코드하는 대신, 녹화 중 타임랩스용 프레임을 별도 스풀했다가 종료 시 인코딩하는 방식도 가능하다. 디코드가 통째로 불필요해지지만, 배속 N이 세션 종료 후에야 확정되므로(`ComputeSpeedFactor`, VF-8) 충분히 촘촘한 간격으로 스풀해야 한다. **디스크·메모리 비용과 맞바꾸는 선택**이며 착수 시 판단한다.

---

## §6 리스크

| # | 리스크 | 영향 | 완화 |
|---|--------|------|------|
| R-1 | **현 상태 방치** — 준수 미이행 배포 지속 | 라이선스 위반 상태 유지. 고객사 오픈소스 검수에서 지적 가능 | **경로 1 즉시 실행**(반나절) |
| R-2 | UV-1 실패(LGPL 빌드에 `h264_mf` 없음) | 경로 2 폐기 | 게이트를 Step 2-0에 배치해 조기 판정. 폐기 시 경로 3 |
| R-3 | `h264_mf`/MF 화질이 libx264 대비 열위 | 결과물 품질 저하 — 포토부스 산출물이 제품의 전부 | 동일 세션 A/B 실측 필수. 수용 불가 시 경로 1 유지 + A(임베드) |
| R-4 | PC별 인코더 편차(GPU MFT 개입) | 현장마다 다른 결과 | MF는 기본이 SW 인코더다(하드웨어 MFT는 `MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS` 명시 필요) → **명시하지 않아 편차를 줄인다** |
| R-5 | 방안 A의 백신 오탐 | 설치·실행 실패 | 코드 서명. 서명 없이 진행 금지 |
| R-6 | 경로 3의 COM 인터롭 난이도 | 실패가 HRESULT로만 드러나 디버깅 지연 | `IVideoRecorder` 뒤에 ffmpeg 구현 유지 → 언제든 ini로 롤백 |
| R-7 | 홀수 해상도 회귀 | `FfmpegArgs.cs:16-29`에 기록된 실패(exit=-542398533)가 MF 경로에서 재발 | 짝수 보정 로직을 인코더 무관 순수 함수로 뽑고 테스트 유지 |
| R-8 | 커스텀 빌드(C) 채택 시 장기 유지보수 부채 | CVE 대응 정지 | **C 미채택**(§3.3) |

---

## §7 착수 전 확인 필요 (미검증 항목)

| UV | 항목 | 확인 방법 |
|----|------|-----------|
| **UV-1** | BtbN LGPL 빌드에 `h264_mf` 포함 여부 — **경로 2의 성립 조건** | zip 다운로드 → `ffmpeg -encoders \| findstr h264` |
| UV-2 | LGPL 빌드 실제 파일 크기 | 압축 해제 후 실측 |
| UV-3 | `h264_mf`가 rawvideo BGR24 **파이프 입력**을 정상 처리하는지 | 실제 세션 인코딩 |
| UV-4 | `-crf 20` 대비 동등 화질의 비트레이트 값 | 해상도별 A/B 눈검사 |
| UV-5 | `MFVideoFormat_RGB24`의 채널 순서가 OpenCV BGR과 일치하는지(경로 3) | Windows DIB 관례상 일치 예상 — 실물 확인 필요 |
| UV-6 | 물리 매체(USB) 배포 동선 존재 여부 — **§2.5의 §6(a)/(b) 적용 여부를 가른다** | 운영 확인 |
| UV-7 | 소스 미러 호스팅 위치(자사 서버 vs 사내 저장소) | 인프라 결정 |

---

## §8 참고 출처

- [CODEX FFMPEG @ gyan.dev](https://www.gyan.dev/ffmpeg/builds/) — 현행 번들 배포처(GPL 빌드만 제공)
- [BtbN/FFmpeg-Builds — Latest](https://github.com/BtbN/FFmpeg-Builds/wiki/Latest) — win64 LGPL 정적 빌드 목록
- [BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds) — 빌드 변형 구조(`variants/win64-lgpl.sh`)

---

## §9 착수 시 갱신할 기존 문서

| 문서 | 갱신 내용 |
|------|-----------|
| [`docs/design/README.md`](./README.md) | §3.2 Windows 표에 이 문서 등재 |
| [`docs/analysis/90-roadmap-and-future-work.md`](../analysis/90-roadmap-and-future-work.md) | §1 표에 "ffmpeg GPL 준수 미이행" 항목 추가 → 경로 1 완료 시 해소 표기 |
| [`docs/design/wpf-architecture.md`](./wpf-architecture.md) | `:61` 라이선스 주의 문구와 `:345` **R4** 항목을 판정 결과로 갱신 |
| [`docs/analysis/80-build-and-deployment.md`](../analysis/80-build-and-deployment.md) | 번들 구성·`licenses/` 산출물 반영 |
| [`docs/analysis/14-media-pipeline-spec.md`](../analysis/14-media-pipeline-spec.md) | 경로 2·3 채택 시 인코더 규격(플랫폼 중립 서술) 갱신 |
| [`docs/analysis/70-logging-and-troubleshooting.md`](../analysis/70-logging-and-troubleshooting.md) | `:145-151` ffmpeg 탐색·실패 로그 표 — 경로 3 채택 시 갱신 |
| [`docs/web-client/04-media-pipeline-web.md`](../web-client/04-media-pipeline-web.md) | 웹은 이미 브라우저 인코딩(WebCodecs)이라 **영향 없음**. 참고만 |

---

## §10 경로 1 구현 결과 (2026-08-06 완료)

§5.1의 Step 1-1~1-6을 구현했다. **코드 로직은 한 줄도 바꾸지 않았다** — 인코더·파이프라인·탐색 순서 모두 그대로다.

### 10.1 산출물

| 파일 | 내용 | 의무 |
|------|------|------|
| `licenses/FFmpeg-COPYING.GPLv3.txt` | GPLv3 전문(gnu.org 원문 674줄, 무가공) | O1 |
| `licenses/FFmpeg-README.txt` | 버전·**저작권 표시**·**configuration 문자열 전문(실측)**·소스 URL 2곳·**3년 서면 오퍼**·무수정 재배포 명시·추가제약 없음 명시·상표 고지 | O2·O3·O4·O5 |
| `licenses/README.txt` | 고지 인덱스. MC포토 본체는 MIT임을 명시, 재배포 대상과 아닌 것을 구분 | O2 |
| `licenses/MCPhoto-LICENSE-MIT.txt` | MC포토 본체 MIT 전문. **물리 사본이 아니라 리포 루트 `LICENSE`를 csproj가 링크 복사**한다(단일 소스 유지) | — |

configuration 문자열은 문서 작성 시점의 §1.3 발췌가 아니라 **번들 바이너리에서 직접 뽑았다**(`ffmpeg -version`). 실측 결과 §1.3에 없던 항목이 다수 있었다(`--disable-w32threads`, `--enable-cairo`, `--enable-libvmaf`, `--enable-libzimg`, `--enable-libopenmpt` 등) — 대응 소스 범위를 좁게 적으면 O3 이행이 불완전해지므로 전문을 그대로 실었다.

### 10.2 배포 경로 배선

| 대상 | 변경 |
|------|------|
| `src/MCPhoto.App/MCPhoto.App.csproj` | `LicensesSource` 프로퍼티 + `None`(빌드 출력 복사) + **`CopyLicensesToPublish` 타겟**(publish 산출물 복사). ffmpeg와 같은 이중 안전 — 라이선스 누락은 법적 문제다 |
| `installer/MCPhoto.iss` | **변경 없음.** `[Files]`가 `{#PublishDir}\*`를 `recursesubdirs`로 담으므로 publish에만 들어가면 인스톨러는 자동 포함이다(§5.1 Step 1-4는 불필요했다) |

**실측 검증**: `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true` 실행 결과 `licenses/` 3개 파일과 `tools/ffmpeg/ffmpeg.exe`가 모두 산출물에 존재함을 확인했다.

### 10.3 앱 내 고지 (Step 1-6)

진단·상태 창(설정 → 고급)에 **"오픈소스 라이선스" 카드**를 신설했다. 로그 카드와 개발자 문의 카드 사이에 둔다.

- 안내 문구(FFmpeg=GPLv3 사용 / MC포토 본체=MIT) + **라이선스 폴더 절대 경로**(읽기 전용 TextBox — 열기가 실패해도 수동 탐색 가능) + **[라이선스 폴더 열기]** 버튼.
- **누락 경고**: 폴더가 없으면 = 고지 없이 배포된 것이므로 경고 배너를 띄우고 버튼을 비활성한다. 배포 사고를 현장에서 잡는 마지막 그물이다.
- 신규 `ILicenseFolderService` / `LicenseFolderService`(`Services/`). `LogFolderService`와 같은 패턴(opener 주입 가능 → 테스트가 explorer를 실제로 띄우지 않는다)이되 **한 가지가 다르다**: 폴더가 없을 때 **생성하지 않는다.** 빈 폴더를 만들어 열면 누락 사실을 은폐하기 때문이다.
- `DiagnosticsViewModel`에 `LicenseFolderPath` / `HasLicenseFolder` / `IsLicenseFolderMissing` / `OpenLicenseFolderCommand` 추가(생성자에 서비스 1개 주입).

### 10.4 UV-1 부분 진전

경로 2의 게이트인 UV-1(BtbN **LGPL** 빌드의 `h264_mf` 포함 여부)은 **여전히 미검증**이다(zip을 받지 않았다). 다만 **현행 GPL 번들에는 `h264_mf`가 실재함을 재확인**했다(VF-19 확정).

```
$ ffmpeg -hide_banner -encoders | grep h264
 V....D libx264 / libx264rgb / h264_amf / h264_d3d12va
 V....D h264_mf        H264 via MediaFoundation (codec h264)   ← VF-19 확인
 V....D h264_nvenc / h264_qsv / h264_vaapi
```

즉 **인코더 교체(2-1·2-2)는 지금 번들 그대로 선행 실험이 가능하다** — LGPL zip 없이도 `-c:v h264_mf`로 화질·속도 A/B(UV-3·UV-4)를 먼저 끝낼 수 있다. 경로 2에 착수한다면 이 순서가 위험이 낮다(바이너리 교체는 A/B 통과 후에).

### 10.5 회귀 방지

`tests/MCPhoto.Tests/LicenseComplianceTests.cs` 신설(9건). 고지 파일은 코드가 아니라서 파일 정리 중 조용히 사라지기 쉽다 — 그때 **테스트가 실패하도록** 고정했다.

| 테스트 | 고정하는 것 |
|--------|-------------|
| `GplV3_Full_Text_Is_Bundled` | 전문 존재 + 조항 표제 + 600줄 초과(요약본 교체 방지) |
| `Ffmpeg_Notice_Has_Version_Config_Source_And_Written_Offer` | 버전·`--enable-gpl`·소스 URL 2곳·3년 오퍼·연락처·추가제약 없음 문구 |
| `License_Index_Lists_Ffmpeg_And_Keeps_Mcphoto_Mit` | 인덱스가 FFmpeg/GPL/MIT를 모두 언급 |
| `Csproj_Copies_Licenses_To_Output_And_Publish` | 배포 배선(빌드 출력 + publish 타겟) |
| **`If_Ffmpeg_Is_Bundled_Then_Notice_Must_Exist`** | **번들과 고지의 연결** — ffmpeg 복사 규칙이 살아 있으면 고지 3종이 반드시 있어야 한다. 경로 3으로 ffmpeg를 빼면 이 검사는 스스로 무효화된다 |
| `Mcphoto_Mit_License_Is_Shipped_Into_Licenses_Folder` | 루트 `LICENSE` 존재 + csproj 링크 복사 배선 + **`licenses/`에 물리 사본을 두지 않음**(두 곳 관리 방지) |
| `Service_*` 3건 | 경로 산출 / 존재 시 열기 / **없을 때 생성도 열기도 하지 않음** |

`DiagnosticsViewModelTests`에 VM 배선 4건 추가(경로 노출·커맨드 위임·누락 플래그 반전 2케이스).

**전체 테스트 971건 통과 / 실패 0** (직전 베이스라인 958 + 신규 13). 빌드 오류 0, 경고 증가 0.

### 10.5.1 리뷰에서 잡은 결함 2건 (구현 직후 수정)

테스트가 다 통과한 뒤 산출물을 직접 열어보고 찾은 것들이다. **둘 다 "문서가 거짓말을 하는" 유형**이라 자동 검증으로는 드러나지 않았다.

| # | 결함 | 조치 |
|---|------|------|
| D-1 | `licenses/README.txt`가 "설치 폴더의 LICENSE 파일 참조"라고 안내하는데 **MIT 전문이 배포물에 없었다**(csproj에 복사 규칙 부재). 안내가 거짓이었다 | 루트 `LICENSE`를 `licenses/MCPhoto-LICENSE-MIT.txt`로 링크 복사(빌드·publish 양쪽) + 인덱스 문안 정정 + 테스트 추가 |
| D-2 | **FFmpeg 저작권 표시가 없었다.** 버전·라이선스만 적었는데 GPLv3 §4는 저작권 고지 **유지**를 요구한다 | `Copyright (c) 2000-2026 the FFmpeg developers` + 정적 링크 라이브러리들의 저작권 소재 문단 추가 + 테스트로 고정 |

### 10.6 남은 작업 (사용자 액션 필요)

| # | 항목 | 왜 코드로 못 하나 |
|---|------|-------------------|
| **U-1** | §5.1 Step 1-3 **소스 미러링** — 자사 서버에 ffmpeg 8.1.2 소스 사본을 올리고 그 URL을 `FFmpeg-README.txt` 3항에 추가 | 인프라 결정·호스팅 필요(UV-7). 현재는 제3자(gyan.dev·ffmpeg.org)를 가리키고 있고 이는 §6(d)로 허용되지만, **바이너리를 배포하는 동안 그 링크가 살아 있어야 한다**는 조건이 제3자에 걸려 있다 |
| **U-2** | UV-6 확인 — **USB·현장 설치 동선이 있는지** | 운영 사실 확인. 있으면 §6(a)/(b)가 적용되는데, 서면 오퍼(4항)를 이미 넣어 두어 **현재 문안으로도 충족**된다 |
| **U-3** | 실기 확인 — 인스톨러로 클린 PC 설치 후 `licenses/` 존재 및 진단 창 카드 동작 | 실제 설치 환경 필요 |

U-1이 없어도 **현재 상태는 준수 상태**다(제3자 소스 링크 + 3년 서면 오퍼). U-1은 제3자 링크가 끊길 위험에 대한 자체 보험이다.
