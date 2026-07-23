# MC포토 이터레이션 10 설계 — 베타/QA PC 서버(Firebase) 연동

> 목표: **베타 exe를 받은 QA가 자기 PC에서 DB 계정(manager/user)으로 로그인하고,
> 기본 프레임이 자동 다운로드되어 촬영까지 가능**하게 한다.
> 관리자 앱계정 공유 금지, Frame 수동 복사 금지 — 즉 베타 exe가 서버에 연결되어야 한다.
>
> 구현 단계는 [wpf-it10-wbs.md](wpf-it10-wbs.md) 참조.

---

## 1. 배경·근본 원인 진단

### 1.1 증상
- 다른 PC(베타 exe)에서 `devmcjo` 외 계정 로그인 불가, `devmcjo/1111`만 통과.
- 다른 PC에서 로그인해도 기본 프레임이 다운로드되지 않음(번들/fallback만 표시).

### 1.2 근본 원인 — **서비스 계정 키 부재 (단일 원인)**

| # | 사실 | 근거 |
|---|------|------|
| 1 | Firebase 초기화는 서비스 계정 키 파일이 필수. 키 없으면 `IsInitialized=false`로 조용히 완화(경고 로그 1줄) | `src/MCPhoto.Firebase/FirebaseClient.cs:47-52` |
| 2 | 키 탐색: ①실행폴더\serviceAccountKey.json → ②%ProgramData%\MCPhoto\serviceAccountKey.json | `FirebaseClient.cs:92-102` (`DefaultKeyPath`) |
| 3 | 키는 비밀로 취급 — `.gitignore`가 `serviceAccountKey.json` 제외, publish 산출물에도 미포함 | `.gitignore:36-44`, `publish.ps1:33-38`(복사 없음) |
| 4 | 개발 PC에만 `C:\ProgramData\MCPhoto\serviceAccountKey.json`(2,383B) 존재 — 실측 확인 | `Test-Path` = True |
| 5 | 미초기화 시 로그인: Db null → 인메모리 시드 `devmcjo/1111`만 통과, 그 외 전부 null | `src/MCPhoto.Firebase/AccountService.cs:36-41` |
| 6 | 미초기화 시 프레임: `IFrameRepository.GetDefaultFramesAsync`가 빈 목록 → 다운로드 0건 → 번들/fallback | `src/MCPhoto.Firebase/FrameRepository.cs:32`, `src/MCPhoto.App/Services/FrameCatalogService.cs:54-84` |

→ **로그인 불가·프레임 미다운로드는 모두 "QA PC에 키가 없음"의 파생 증상**이다.
초기화만 되면: 로그인은 Firestore `users/{id}` 조회(Admin SDK, 규칙 우회 — `AccountService.cs:43-47`),
프레임은 공개 토큰 URL(`UploadContract.cs:26-29`)을 무인증 HttpClient로 다운로드
(`FrameCatalogService.cs:120-127`)하므로 **어느 PC에서든 동작할 코드 경로가 이미 존재**한다.

### 1.3 부수 결함(키 해결 후에도 남는 것)
- **로그인 오류 오도**: 미초기화 상태에서 비시드 계정 로그인 시 "아이디 또는 비밀번호가
  올바르지 않습니다"로 표시(`LoginGuestViewModel.cs:41-45`) — 실제 원인(서버 미연결)을 숨김.
- **프레임 다운로드 트리거가 화면 진입 시점뿐**: `FrameSelectViewModel.OnEnterAsync`
  (`FrameSelectViewModel.cs:60,74`)에서만 다운로드. 요구사항은 "실행 시" 확보.
- **진단 로그 빈약**: 키 미발견 경고가 최종 폴백 경로 1개만 출력(`FirebaseClient.cs:50`) —
  QA가 "어디에 키를 놓아야 하는지" 로그만으로 알 수 없음.
- **동시 호출 중복 다운로드 가능성**: 시작 시 prefetch를 추가하면 FrameSelect 진입과 경합 →
  이름 dedup 검사(`FrameCatalogService.cs:57`)가 다운로드 완료 전이라 같은 프레임 2회 다운로드 가능.
- **이름에 `_` 포함 기본 프레임**: 공용/user 구분이 `_` 유무 규약(`LocalFrameStore.cs:57-59`)이라
  `_` 포함 이름은 캐시 파일이 공용 목록·dedup 집합에서 제외 → **매 진입 재다운로드**(표시는 됨).

---

## 2. 검증된 사실 / 미검증 가정

### 검증된 사실 (verified facts)
- F1. `FirebaseClient` 생성자: 키 로드 실패/부재 시 `IsInitialized=false`, 예외 삼킴 — `FirebaseClient.cs:45-84`
- F2. 키 탐색 순서 = 실행폴더 우선 → ProgramData 폴백 — `FirebaseClient.cs:92-102`
- F3. `AccountService.LoginAsync`: Db 있으면 `users/{id}` 평문 비교, 없으면 시드만 — `AccountService.cs:33-48`
- F4. 시드 계정 DB 보장은 앱 시작 시 fire-and-forget — `App.xaml.cs:73-92` (`EnsureSeedAsync`)
- F5. 프레임 카탈로그: 로컬 공용 우선 → DB isDefault 중 로컬에 없는 이름만 다운로드·캐시 — `FrameCatalogService.cs:45-84`
- F6. DB 프레임 `ImageUrl` = 공개 토큰 URL(`firebasestorage.googleapis.com/...&token=...`) — `FrameRepository.cs:67`, `UploadContract.cs:26-29`
- F7. 캐시 저장 = 실행폴더\Frame\{이름}.png + .slots(#dbid 보존) — `LocalFrameStore.cs:24-53`, DI 루트 `ServiceRegistration.cs:75-76`
- F8. publish = 자체 포함 단일 exe, `publish\MCPhoto\` 출력, 키 복사 없음 — `publish.ps1:20-46`
- F9. csproj에 AfterTargets=Publish 복사 선례(ffmpeg) 존재 — `MCPhoto.App.csproj:43-48`
- F10. `.gitignore`가 `serviceAccountKey.json`(전역 패턴 포함)과 `publish/`를 제외 — `.gitignore:36-44,56`
- F11. `FirebaseClient`는 DI 싱글턴, 버킷은 `AppSettings.StorageBucket`("mcphoto-955fb.firebasestorage.app") 주입 — `ServiceRegistration.cs:59-66`, `AppSettings.cs:113`
- F12. `IFirebaseClient.IsInitialized`가 이미 공개 계약에 존재 — `src/MCPhoto.Core/Upload/IFirebaseClient.cs:12`
- F13. 개발 PC에 `C:\ProgramData\MCPhoto\serviceAccountKey.json` 존재(2,383B) — 실측
- F14. 테스트 프로젝트 `tests/MCPhoto.Tests` 존재, `FrameCatalogService`는 다운로드 함수 주입 가능(테스트 시임 지점) — `FrameCatalogService.cs:27-36`

### 미검증 가정 (open assumptions)
- A1. QA PC에서 `firestore.googleapis.com`/`firebasestorage.googleapis.com`/`oauth2.googleapis.com` 아웃바운드 443 허용
  → 검증: WBS Step 7(개발 PC 스모크) + **QA 실PC 확인은 사용자 몫**(§8)
- A2. Firestore `users`에 manager/user 계정 문서가 실존
  → 검증: WBS Step 7(devmcjo 로그인 → 계정 관리 화면 확인). 부재 시 앱 내 생성 가능(콘솔 불필요)
- A3. `frameTemplates`(isDefault=true) 문서들의 `imageUrl`이 유효한 토큰 URL(무인증 다운로드 가능)
  → 검증: WBS Step 7(fresh Frame 폴더 다운로드 스모크)
- A4. `FirestoreDbBuilder.Build()`는 네트워크 미연결이어도 예외 없이 성공(지연 연결) —
  즉 `IsInitialized=true`가 "네트워크 연결됨"을 보장하지 않음
  → 검증: WBS Step 3(로그인 실패 catch 경로의 "네트워크 확인" 메시지 유지 — `LoginGuestViewModel.cs:50-54`)
- A5. 미캐시 프레임(http `ImageUrl`)의 WPF 이미지 표시 동작 — 이번 설계는 캐시 경로를 유지하므로 **비의존**(검증 불필요)

---

## 3. 설계

이터레이션은 4개 워크스트림(S1~S4)으로 구성한다. **S1이 근본 해결이며 나머지는 정합·견고화·진단이다.**

### S1. 자격증명 전달 — publish 산출물에 서비스 계정 키 번들 (권장안 A)

#### 결정: publish.ps1 스크립트 레벨 복사 (csproj 아님)

| 방식 | 판단 |
|------|------|
| **(채택) publish.ps1이 키를 `publish\MCPhoto\`로 복사** | 키는 빌드 자산이 아니라 **배포 판단이 필요한 비밀**. 스크립트 레벨이면 포함 여부·출처가 콘솔에 명시적으로 드러나고, 개발자가 `dotnet publish`를 직접 호출하는 경로(스크립트 우회)에서는 절대 포함되지 않음 |
| (기각) csproj AfterTargets=Publish (ffmpeg 방식) | 모든 publish에 무조건 포함 → IDE/CLI publish에서 의도치 않은 키 유출 위험. ffmpeg(공개 바이너리)와 비밀은 성격이 다름 |
| (기각·장기 백로그) Firebase 클라이언트 SDK + 보안 규칙 (B안) | Admin SDK 탈피 = FirebaseClient/AccountService/FrameRepository/UploadService 전면 재작성 + 규칙 설계 + 웹 계약 영향. 릴리즈 전 이전 대상으로 백로그화(§7) |

#### publish.ps1 변경 명세
- 파라미터 추가: `-KeyPath <string>`(명시 경로), `-NoServiceKey`(제외 스위치).
- 키 소스 탐색 순서(첫 번째 존재 파일 채택):
  1. `-KeyPath` 인자
  2. 환경변수 `MCPHOTO_SERVICE_KEY`
  3. `%ProgramData%\MCPhoto\serviceAccountKey.json` (개발 PC 실존 위치 — F13)
  4. 리포 루트 `serviceAccountKey.json` (gitignore 커버 — F10)
- 동작: publish 성공 후 키를 `publish\MCPhoto\serviceAccountKey.json`으로 복사.
  - 복사 시 콘솔에 **경고 배너** 출력: "Admin 서비스 계정 키 포함 — 내부 베타 전용. 외부 배포 금지."
  - 키 미발견 시: 노란 경고("서버 미연동 빌드 — 키 없음") 출력 후 **계속**(오프라인 빌드도 유효 산출물).
  - 요약 줄에 `Service key: INCLUDED / NOT INCLUDED` 명시.
- publish.bat 변경 없음(ps1 호출 그대로).
- git 안전: `publish/`·`serviceAccountKey.json` 모두 기존 `.gitignore`가 커버(F10) — **gitignore 변경 불필요**.
- 앱 코드 변경 불필요: `DefaultKeyPath`가 실행폴더를 이미 1순위로 탐색(F2) → exe 옆 키가 그대로 로드됨.

#### 보안 트레이드오프 (필수 명시)
- **exe 배포 폴더를 받은 사람은 누구나 이 프로젝트의 Firestore/Storage에 admin 접근 가능**하다.
  앱 내 역할 게이트(manager/user)는 UI 표면일 뿐, 키 보유자는 데이터를 직접 읽고 쓸 수 있다.
- 수용 조건: **사내 QA 한정 배포**, 배포 채널 통제(공유 드라이브/메신저 사내 한정), 외부 반출 금지.
- 릴리즈 전 필수 대책(백로그, §7): ① GCP에서 **키 회전**(베타 종료 시 기존 키 폐기) ② 클라이언트 SDK + 보안 규칙 이전(B안).

### S2. 로그인 UX·폴백 정합

#### S2-1. 오프라인 원인 노출 (오도 메시지 제거)
- `LoginGuestViewModel`에 `IFirebaseClient` 주입(F12 — 인터페이스 기존재, UI 타입 아님 → 테스트 가능성 유지).
- 신규 관측 프로퍼티 `IsServerOffline`(= `!IsInitialized`, 진입 시 1회 평가로 충분 — 키는 시작 시 결정되고 런타임 변화 없음).
- `LoginGuestView.xaml` 상단에 오프라인 배너(경고 톤):
  "서버 미연결 상태입니다. 오프라인 관리자 계정으로만 로그인할 수 있습니다."
- 로그인 실패 메시지 분기(`Login()` 내):
  - `IsServerOffline && 입력 id != 시드` → "서버 미연결 상태에서는 이 계정으로 로그인할 수 없습니다."
  - 그 외 null 반환 → 기존 "아이디 또는 비밀번호가 올바르지 않습니다." 유지
  - 예외(catch) → 기존 "로그인할 수 없습니다. 네트워크를 확인해 주세요." 유지(A4 대비)
- `AccountService` 계약 변경 없음(반환 null 의미 유지) — 분기는 VM에서 `IsInitialized`로 판단.

#### S2-2. 인메모리 시드 폴백 — **유지 + 명시화 (권장)**
- 결정 지점(상위 보고 대상): 시드 `devmcjo/1111` 인메모리 폴백(`AccountService.cs:36-41`)을 유지할지.
- **권장: 유지.** 근거: 키오스크가 오프라인인 현장에서 관리자가 설정(카메라 변경 등)에 진입하려면
  로그인이 필요하다. 제거하면 오프라인 현장 대응 수단이 사라진다.
- 단, "아무 데서나 뚫리는 백도어" 인식을 없애기 위해:
  - S2-1 배너가 오프라인 모드임을 **항상** 표시(DB 로그인과 혼동 불가).
  - 오프라인 시드 로그인 성공 시 로그에 `Warning` 1줄("오프라인 시드 로그인 — DB 미연결") 기록.
- 대안(사용자 선택 시): 폴백 완전 제거 — devmcjo도 DB로만. 오프라인 설정 진입 불가 트레이드오프 감수.

### S3. 프레임 다운로드 견고화

#### S3-1. 실행 시 백그라운드 prefetch
- `App.OnStartup`에 `EnsureSeedAsync`(F4)와 동일 패턴으로 `_ = PrefetchDefaultFramesAsync();` 추가.
  - 내부: `FrameCatalogService.GetDefaultFramesAsync()` 1회 호출(결과 무시 — 부수효과인 로컬 캐시가 목적).
  - try/catch + `Log.Warning`(실패는 앱 동작에 영향 없음 — FrameSelect 진입 시 재시도됨).
- 효과: 게스트 포함 어떤 흐름이든 **앱 실행 직후** Frame 폴더가 채워진다(요구 2 충족).
  기존 FrameSelect 진입 시 로드(F5)는 그대로 유지(2중 안전망).

#### S3-2. 동시 호출 직렬화 (중복 다운로드 방지)
- `FrameCatalogService`에 `SemaphoreSlim(1,1)` 게이트 추가 — `GetDefaultFramesAsync` 본문 전체를
  `WaitAsync`/`Release`로 감쌈. prefetch(S3-1)와 FrameSelect 진입이 경합해도 두 번째 호출은
  첫 호출 완료 후 로컬 캐시를 보게 되어 재다운로드 없음.
- 비동기 대기(UI 스레드 블로킹 없음). 싱글턴 서비스(`ServiceRegistration.cs:80`)라 인스턴스 필드로 충분.

#### S3-3. 다운로드 경로 진단·엣지 케이스
- 캐시 성공 시 Info 로그 추가: "기본 프레임 캐시: {Name} ← DB({Id})" (현재는 실패 warning만 — `FrameCatalogService.cs:107-110`).
- 이름에 `_` 포함 기본 프레임(§1.3): `TryCacheAsync`에서 감지 시 Warning 로그
  "기본 프레임 이름에 '_' 포함 — 로컬 공용 규약과 충돌, 매 실행 재다운로드됨: {Name}".
  동작은 현행 유지(캐시 저장·세션 표시 정상 — 파괴적 변경 없음). **데이터 규약**으로
  "기본(isDefault) 프레임 이름에 `_` 금지"를 §8 사용자 액션에 포함.
- 실패 처리 현행 유지: 개별 프레임 실패는 스킵·로그(`FrameCatalogService.cs:107-111`), DB 조회 실패는 로컬 폴백(`:62-65`).

### S4. 진단 가시성 (QA 트러블슈팅)

#### S4-1. 키 탐색 로그 강화
- `FirebaseClient`에 정적 `KeyCandidatePaths()`(후보 2경로 배열 반환) 신설, `DefaultKeyPath()`는 이를 사용하도록 정리(동작 불변 — 실행폴더 우선).
- 생성자에서 키 미발견 시 **후보 전부**를 존재 여부와 함께 로그:
  "서비스 계정 키 없음 — 탐색: [실행폴더경로]=없음, [ProgramData경로]=없음. 서버 기능 비활성(오프라인 모드)."
- 초기화 성공 시 기존 로그(`FirebaseClient.cs:78`)에 **사용한 키 경로** 추가:
  "Firebase 초기화 완료: project=..., bucket=..., key={사용된 경로}".

#### S4-2. 설정 화면 서버 연결 상태 표시
- `SettingsViewModel`(`SettingsViewModel.cs:84-86`)에 `IFirebaseClient` 주입, 읽기 전용 프로퍼티 노출:
  - `ServerStatusText`: 연결 시 "연결됨 — {Bucket}", 미연결 시 "미연결 — 서비스 계정 키 없음(로그 참조)"
  - `IsServerConnected`(bool, 색상 트리거용)
- `SettingsView.xaml` 웹 연동 섹션(StorageBucket 편집 근처)에 읽기 전용 상태 행 1줄 추가.
  기존 저장/편집 로직과 완전 무간섭(표시 전용, 저장 대상 아님).

---

## 4. 변경/신규 파일 목록

| 파일 | 변경 | 워크스트림 |
|------|------|-----------|
| `publish.ps1` | 수정 — 키 소스 탐색·복사·배너·요약 출력, `-KeyPath`/`-NoServiceKey` | S1 |
| `src/MCPhoto.Firebase/FirebaseClient.cs` | 수정 — `KeyCandidatePaths()` 신설, 생성자 진단 로그 강화 | S4-1 |
| `src/MCPhoto.App/ViewModels/LoginGuestViewModel.cs` | 수정 — `IFirebaseClient` 주입, `IsServerOffline`, 메시지 분기 | S2-1 |
| `src/MCPhoto.App/Views/LoginGuestView.xaml` | 수정 — 오프라인 배너(경고 톤, 기존 팔레트 리소스 사용) | S2-1 |
| `src/MCPhoto.Firebase/AccountService.cs` | 수정 — 오프라인 시드 로그인 시 Warning 로그 1줄 | S2-2 |
| `src/MCPhoto.App/App.xaml.cs` | 수정 — `PrefetchDefaultFramesAsync` fire-and-forget 추가 | S3-1 |
| `src/MCPhoto.App/Services/FrameCatalogService.cs` | 수정 — SemaphoreSlim 직렬화, 성공 로그, `_` 이름 경고 | S3-2/3 |
| `src/MCPhoto.App/ViewModels/SettingsViewModel.cs` | 수정 — `IFirebaseClient` 주입, 상태 프로퍼티 | S4-2 |
| `src/MCPhoto.App/Views/SettingsView.xaml` | 수정 — 서버 연결 상태 행 | S4-2 |
| `tests/MCPhoto.Tests/...(신규 테스트)` | 신규 — §6 headless 테스트 | 전체 |

신규 소스 파일 없음. **기존 파일 수정 시 인코딩 보존 필수**(UTF-8, BOM 유무는 각 파일 현행 유지).
XAML 신규 리소스 키 없음(기존 팔레트/스타일 재사용) — 리소스 키 충돌 없음.

## 5. 스레딩 모델
- prefetch(S3-1)·시드 보장(F4)은 UI 스레드 밖 fire-and-forget + 예외 로그 — UI 블로킹 없음.
- `FrameCatalogService` 게이트는 `SemaphoreSlim.WaitAsync`(비동기 대기) — Dispatcher 무관, UI 갱신 없음(파일 캐시만).
- VM 프로퍼티(`IsServerOffline`, `ServerStatusText`)는 생성/진입 시 1회 평가(불변 상태) — 스레드 경합 없음, 이벤트 구독 신설 없음(누수 위험 0).

## 6. Headless 테스트 지점 (tests/MCPhoto.Tests)
1. `FirebaseClient.KeyCandidatePaths()` — 2개 경로, 실행폴더 우선 순서 검증.
2. `FrameCatalogService` 동시 호출 — 지연 주입 다운로드 함수(F14)로 `GetDefaultFramesAsync` 2회 동시 호출 시 다운로드 횟수가 프레임당 1회인지.
3. `FrameCatalogService` `_` 이름 프레임 — 캐시·표시 동작 불변 + (로거 fake로) 경고 발생 확인.
4. `LoginGuestViewModel` 오프라인 분기 — fake `IFirebaseClient(IsInitialized=false)` + fake `IAccountService`로 ①비시드 로그인 → 오프라인 메시지 ②`IsServerOffline=true` 노출.
5. `SettingsViewModel.ServerStatusText` — 연결/미연결 두 상태 문자열.
6. XAML 회귀 — 기존 `XamlResourceTests` 방식으로 `LoginGuestView`/`SettingsView` 로드 무예외.
- publish.ps1은 dotnet test 불가 → WBS Step 1의 스크립트 실행+`Test-Path` 검증으로 대체.

## 7. 비범위 (non-goals) / 백로그
- **B안: Firebase 클라이언트 SDK + 보안 규칙 이전** — 릴리즈 전 필수 백로그(Admin 키 배포 종결책). 이번 범위 아님.
- **키 회전 자동화** — 베타 종료 시 GCP 콘솔에서 수동 회전(§8).
- 비밀번호 해시화(현 평문 비교 — `AccountService.cs:46`), `DisplayMode` 기본값 원복·설정 기본값 정리(it9 후속 표기: `AppSettings.cs:95,106,113`) — 릴리즈 하드닝 이터레이션 몫.
- 웹 다운로드 페이지·보안 규칙 변경 없음(Admin SDK는 규칙 우회 — 이번 동작과 무관).

## 8. 코드/빌드로 해결 불가 — 사용자(콘솔·운영) 몫
1. **Blaze 요금제 유지 확인** — Storage 사용 전제(프레임 이미지 다운로드 원본·업로드). 프로젝트 `mcphoto-955fb`.
2. **서비스 계정 키 권한·유효성 확인** — 현행 키(2,383B)가 Firestore·Storage 접근 권한 보유인지(현 개발 PC에서 동작 중이므로 사실상 확인됨). **베타 종료/유출 의심 시 GCP IAM → 서비스 계정 → 키 회전(기존 키 폐기)**.
3. **QA 계정 준비** — Firestore `users`에 manager/user 문서 확인. 부재 시 **앱 내 계정 관리 화면(devmcjo 로그인)으로 생성 가능 — 콘솔 불필요**. 콘솔은 확인 용도만.
4. **기본 프레임 데이터 점검** — `frameTemplates`(isDefault=true) 문서 존재·`imageUrl` 유효성. **기본 프레임 이름에 `_` 사용 금지 규약**(§S3-3) 준수(기존 문서 중 `_` 포함 이름이 있으면 개명 권장).
5. **QA PC 네트워크** — `firestore.googleapis.com`, `firebasestorage.googleapis.com`, `oauth2.googleapis.com` 아웃바운드 443 허용(사내 프록시/방화벽 환경이면 예외 등록).
6. **베타 배포 채널 통제** — 키 포함 폴더는 사내 한정 공유, 외부 반출 금지(§S1 보안 트레이드오프).

## 9. 결정 필요 사항 (상위 보고 — 임의 확정 금지)
| # | 결정 | 권장 | 대안 |
|---|------|------|------|
| D1 | 오프라인 인메모리 시드(devmcjo/1111) 폴백 | **유지 + 오프라인 배너·로그 명시화**(현장 오프라인 설정 진입 보전) | 완전 제거(devmcjo도 DB 전용 — 오프라인 설정 진입 불가 감수) |
| D2 | publish 키 포함 기본값 | **기본 포함**(이번 목적이 베타 연동, `-NoServiceKey`로 제외 가능) + 콘솔 경고 배너 | 기본 제외 + `-IncludeServiceKey` 옵트인(실수 방지 우선이면) |
| D3 | 기본 프레임 이름 `_` 금지 데이터 규약 | **수용**(코드 가드는 경고 로그, 파괴적 변경 없음) | 파일명 이스케이프 규약 재설계(별도 이터레이션 규모) |

---

## 10. 결정 확정 (2026-07-23)

§9의 결정을 사용자가 아래와 같이 확정했다. 구현은 이 값을 따랐다.

| # | 확정 결정 | 구현 반영 |
|---|-----------|-----------|
| **D1** | **유지** — 오프라인 인메모리 시드(devmcjo/1111) 폴백 유지 + 오프라인 배너·메시지 명시화 | S2-1 배너(`LoginGuestView.xaml`), 메시지 분기(`LoginGuestViewModel.IsServerOffline`), 오프라인 시드 로그인 Warning 로그(`AccountService`) |
| **D2** | **키 기본 포함 + 키 미포함 변형 제공** | `publish.ps1` 기본 키 포함(소스 우선순위 `-KeyPath`→`$env:MCPHOTO_SERVICE_KEY`→`%ProgramData%\MCPhoto\`→리포 루트), `-NoServiceKey` 스위치, 신규 `publish-nokey.bat`, 콘솔 보안 경고 배너. 스크립트는 ASCII 전용. 키는 계속 gitignore |
| **D3** | **수용** — 기본 프레임 이름 `_` 금지는 데이터 규약, 코드는 경고 로그만 | `FrameCatalogService.TryCacheAsync`에서 `_` 포함 이름 감지 시 Warning 로그(동작 불변) |

### D2 상세 — 키 소스 우선순위·변형
- `publish.ps1`은 publish 성공 후 서비스 계정 키를 `publish\MCPhoto\serviceAccountKey.json`으로 **기본 복사**한다.
  - 소스 탐색(첫 존재 파일 채택): ① `-KeyPath` 인자 → ② 환경변수 `MCPHOTO_SERVICE_KEY`
    → ③ `%ProgramData%\MCPhoto\serviceAccountKey.json` → ④ 리포 루트 `serviceAccountKey.json`.
  - 키 미발견 시 노란 경고만 출력하고 계속(키 없이 publish — 오프라인 빌드도 유효).
- `-NoServiceKey` 스위치로 키 복사를 생략한다. 신규 `publish-nokey.bat`이 `publish.ps1 -NoServiceKey`를 호출(키 미포함 배포용).
  기존 `publish.bat`은 변경 없음(키 포함이 기본).
- 키 포함 시 콘솔에 보안 경고 배너(사내 베타 한정·외부 반출 금지)를 1블록 출력한다.
- 스크립트는 한국어 문자열을 넣지 않고 ASCII(영문)만 사용한다(한국어 Windows cmd/PowerShell 5.1 mojibake 방지).
- 앱 코드는 실행폴더 키를 1순위로 로드하므로(`FirebaseClient.KeyCandidatePaths()[0]`) 로드 로직 변경이 불필요하다.

### 향후 고려 — 상용화 시 키 관리 재설계
- 현행 D2는 **사내 QA 베타 한정** 트레이드오프다. exe 배포 폴더를 받은 사람은 누구나 Firestore/Storage에
  admin 접근이 가능하므로(§S1 보안 트레이드오프), 아래를 릴리즈 전 필수 백로그로 유지한다.
  1. **B안(§7): Firebase 클라이언트 SDK + 보안 규칙 이전** — Admin 키 배포 자체를 종결하는 근본책.
  2. **키 회전**: 베타 종료/유출 의심 시 GCP IAM에서 서비스 계정 키 회전(기존 키 폐기).
  3. 배포 채널 통제(사내 한정 공유, 외부 반출 금지)는 운영 규율로 유지.
