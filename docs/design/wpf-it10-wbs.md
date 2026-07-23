# MC포토 이터레이션 10 WBS — 베타/QA PC 서버 연동

> 설계: [wpf-it10-server-connectivity-design.md](wpf-it10-server-connectivity-design.md)
> 각 단계는 self-contained — fresh 에이전트가 그 단계만 읽고 실행 가능해야 한다.
> **기존 파일 수정 시 현행 인코딩(UTF-8, BOM 유무) 보존.**

## 검증된 사실 (verified facts)
- 키 부재 시 `FirebaseClient.IsInitialized=false`로 완화, 탐색은 실행폴더 → `%ProgramData%\MCPhoto\` — `src/MCPhoto.Firebase/FirebaseClient.cs:47-52,92-102`
- 미초기화 시 로그인은 인메모리 시드 `devmcjo/1111`만 통과 — `src/MCPhoto.Firebase/AccountService.cs:36-41`
- 미초기화 시 `GetDefaultFramesAsync`가 빈 목록 → 프레임 다운로드 0건 — `src/MCPhoto.Firebase/FrameRepository.cs:32`
- 프레임 다운로드는 `FrameSelectViewModel.OnEnterAsync`에서만 트리거 — `src/MCPhoto.App/ViewModels/FrameSelectViewModel.cs:60,74`
- DB 프레임 `ImageUrl`은 공개 토큰 URL(무인증 HttpClient 다운로드 가능) — `src/MCPhoto.Core/Upload/UploadContract.cs:26-29`, `src/MCPhoto.App/Services/FrameCatalogService.cs:120-127`
- publish.ps1은 키를 복사하지 않음; `.gitignore`가 `serviceAccountKey.json`·`publish/` 커버 — `publish.ps1:33-38`, `.gitignore:36-44,56`
- 개발 PC에 `C:\ProgramData\MCPhoto\serviceAccountKey.json`(2,383B) 존재 — 실측(Test-Path=True)
- `IFirebaseClient.IsInitialized` 공개 계약 기존재 — `src/MCPhoto.Core/Upload/IFirebaseClient.cs:12`
- `FrameCatalogService`는 다운로드 함수 주입 가능(테스트 시임) — `src/MCPhoto.App/Services/FrameCatalogService.cs:27-36`
- `App.OnStartup`에 fire-and-forget 선례(`EnsureSeedAsync`) — `src/MCPhoto.App/App.xaml.cs:73-92`

## 미검증 가정 (open assumptions)
- A1. QA PC 아웃바운드 443(googleapis 3개 도메인) 허용 → 검증: Step 7 부분검증(개발 PC), **QA 실PC는 사용자 확인**(설계 §8-5)
- A2. Firestore `users`에 manager/user 문서 실존 → 검증: Step 7 (devmcjo 로그인 → 계정 관리 확인, 부재 시 앱 내 생성)
- A3. `frameTemplates`(isDefault) 문서의 `imageUrl` 유효(토큰 URL) → 검증: Step 7 (fresh Frame 다운로드)
- A4. `FirestoreDbBuilder.Build()`는 오프라인에서도 예외 없이 성공(지연 연결 — `IsInitialized`≠네트워크 보장) → 검증: Step 3 (예외 catch 경로 메시지 유지 확인)

---

### Step 1: publish.ps1 — 서비스 계정 키 번들
- **Context Brief**: 베타 exe(`publish\MCPhoto\`)에 서비스 계정 키가 없어 QA PC에서 Firebase 미초기화
  → 로그인·프레임 다운로드 불가. 앱은 실행폴더의 `serviceAccountKey.json`을 1순위로 로드하므로
  (`FirebaseClient.cs:96-97`) publish 출력에 키만 복사하면 코드 변경 없이 연결된다. 키는 git 미포함 유지.
- **대상 파일**: `publish.ps1`
- **선행 조건**: 없음
- **구현 내용**:
  - `param([string]$KeyPath = '', [switch]$NoServiceKey)` 추가.
  - publish 성공 후(`$LASTEXITCODE -eq 0` 블록) 키 소스 탐색 — 첫 존재 파일 채택:
    ① `$KeyPath` ② `$env:MCPHOTO_SERVICE_KEY` ③ `$env:ProgramData\MCPhoto\serviceAccountKey.json` ④ 리포 루트 `serviceAccountKey.json`
  - `-NoServiceKey`면 탐색·복사 생략. 채택 파일을 `publish\MCPhoto\serviceAccountKey.json`으로 복사.
  - 복사 시 경고 배너(예: "WARNING: Admin service key INCLUDED - internal beta only. Do NOT distribute externally.") 출력,
    미발견 시 노란 경고("Service key NOT found - offline build") 출력 후 계속(실패 아님).
  - 요약에 `Service key: INCLUDED (source: ...)` / `NOT INCLUDED` 한 줄 출력. 헤더 주석 갱신.
  - 주의: publish.bat 경유 실행이 기본이므로 스크립트는 파라미터 없이도 동작해야 함(ProgramData 폴백이 기본 경로).
- **검증 명령**: `powershell -NoProfile -ExecutionPolicy Bypass -File .\publish.ps1` 후
  `Test-Path .\publish\MCPhoto\serviceAccountKey.json`(True) && `Test-Path .\publish\MCPhoto\MCPhoto.exe`(True);
  `git status --porcelain`에 serviceAccountKey.json·publish/ 미출현;
  `powershell ... -File .\publish.ps1 -NoServiceKey` 후 동일 Test-Path가 False.
- **완료 기준**:
  - [관측] 기본 실행 시 `publish\MCPhoto\serviceAccountKey.json` 생성 + 콘솔에 INCLUDED 배너; `-NoServiceKey` 시 미생성 + NOT INCLUDED 표시
  - [non-goal] `git status`에 키/publish 산출물 미노출; exe 빌드 결과(크기·ffmpeg 번들) 불변; 키 미발견 환경에서도 publish 성공
  - [trigger] 키 포함은 publish.ps1 실행 시에만 — csproj/dotnet publish 직접 호출로는 절대 포함되지 않음
- **롤백**: `git checkout -- publish.ps1` + `publish\MCPhoto\serviceAccountKey.json` 수동 삭제
- [ ] 완료

### Step 2: FirebaseClient — 키 탐색 진단 로그 강화
- **Context Brief**: 키 미발견 경고가 최종 폴백 경로 1개만 로그되어(`FirebaseClient.cs:50`)
  QA가 로그만으로 "키를 어디에 놓아야 하는지" 알 수 없다. 후보 전체와 사용된 키 경로를 로그에 남긴다.
- **대상 파일**: `src/MCPhoto.Firebase/FirebaseClient.cs`, `tests/MCPhoto.Tests/`(신규 테스트 클래스)
- **선행 조건**: 없음 (Step 1과 병렬 가능)
- **구현 내용**:
  - `public static string[] KeyCandidatePaths()` 신설: `[실행폴더\serviceAccountKey.json, %ProgramData%\MCPhoto\serviceAccountKey.json]` 순서 반환.
  - `DefaultKeyPath()`는 `KeyCandidatePaths()` 순회로 재구성(동작 불변: 첫 존재 파일, 없으면 마지막 후보 반환).
  - 생성자 미발견 경고를 후보 전부+존재 여부 나열로 교체; 초기화 성공 로그(`:78`)에 `key={사용 경로}` 추가.
  - 테스트: `KeyCandidatePaths()`가 2개·실행폴더 우선 순서인지.
- **검증 명령**: `dotnet build src/MCPhoto.App/MCPhoto.App.csproj -c Debug` +
  `dotnet test tests/MCPhoto.Tests/MCPhoto.Tests.csproj --filter "FullyQualifiedName~KeyCandidate"`
- **완료 기준**:
  - [관측] 테스트 PASS; 키 없는 환경 실행 로그에 후보 2경로가 모두 존재 여부와 함께 출력, 키 있으면 성공 로그에 사용 경로 포함
  - [non-goal] 키 탐색 우선순위(실행폴더 → ProgramData) 불변 — 기존 개발 PC 동작 동일
  - [trigger] 로그는 FirebaseClient 생성(앱 시작) 시 1회
- **롤백**: 해당 커밋 revert (다른 Step과 독립)
- [ ] 완료

### Step 3: 로그인 오프라인 UX (배너 + 메시지 분기 + 시드 로그)
- **Context Brief**: Firebase 미초기화 시 비시드 계정 로그인이 "아이디 또는 비밀번호가 올바르지 않습니다"로
  표시되어(`LoginGuestViewModel.cs:41-45`) 서버 미연결 원인을 숨긴다. 오프라인 상태를 화면에 노출하고
  메시지를 분기한다. 시드 폴백은 유지(설계 D1 권장)하되 오프라인 로그인 시 Warning 로그를 남긴다.
- **대상 파일**: `src/MCPhoto.App/ViewModels/LoginGuestViewModel.cs`, `src/MCPhoto.App/Views/LoginGuestView.xaml`,
  `src/MCPhoto.Firebase/AccountService.cs`, `tests/MCPhoto.Tests/`(신규 VM 테스트)
- **선행 조건**: 없음 (Step 1·2와 병렬 가능)
- **구현 내용**:
  - `LoginGuestViewModel` 생성자에 `IFirebaseClient` 주입, `public bool IsServerOffline => !_firebase.IsInitialized` 노출.
  - `Login()`: `user is null && IsServerOffline && LoginId.Trim() != "devmcjo"` → ErrorMessage = "서버 미연결 상태에서는 이 계정으로 로그인할 수 없습니다.". 그 외 null → 기존 메시지, catch → 기존 네트워크 메시지 유지(가정 A4 대비).
  - `LoginGuestView.xaml`: 입력 폼 상단에 `IsServerOffline` 바인딩 배너("서버 미연결 상태입니다. 오프라인 관리자 계정으로만 로그인할 수 있습니다.") — 기존 팔레트 경고 리소스 재사용, 신규 리소스 키 금지.
  - `AccountService.LoginAsync` 오프라인 시드 성공 경로(`:38-39`)에 `_logger?.LogWarning("오프라인 시드 로그인 — DB 미연결")` 추가.
  - VM 테스트: fake `IFirebaseClient(IsInitialized=false)` + fake `IAccountService(null 반환)`로 ①오프라인 메시지 분기 ②`IsServerOffline=true`; fake(IsInitialized=true)로 기존 메시지 유지.
- **검증 명령**: `dotnet test tests/MCPhoto.Tests/MCPhoto.Tests.csproj --filter "FullyQualifiedName~LoginGuest"` +
  `dotnet test tests/MCPhoto.Tests/MCPhoto.Tests.csproj --filter "FullyQualifiedName~XamlResource"` + `dotnet build src/MCPhoto.App/MCPhoto.App.csproj -c Debug`
- **완료 기준**:
  - [관측] VM 테스트 PASS(메시지 분기 2케이스+프로퍼티); 키 없는 실행에서 로그인 화면에 배너 표시, manager 로그인 시도 → 오프라인 메시지
  - [non-goal] 키 있는(초기화 성공) 실행에서는 배너 미표시·기존 메시지·기존 로그인 흐름 완전 동일; `IAccountService` 계약(시그니처·null 의미) 불변
  - [trigger] 배너는 미초기화 상태에서 로그인 화면 진입 시에만; 오프라인 메시지는 로그인 버튼 클릭 실패 시에만
- **롤백**: 해당 커밋 revert (다른 Step과 독립)
- [ ] 완료

### Step 4: FrameCatalogService — 직렬화 게이트·진단·`_` 이름 경고
- **Context Brief**: Step 5에서 앱 시작 시 prefetch가 추가되면 FrameSelect 진입과 `GetDefaultFramesAsync`가
  경합해 같은 프레임을 2회 다운로드할 수 있다(이름 dedup 검사 `FrameCatalogService.cs:57`가 다운로드 완료 전).
  또 이름에 `_` 포함 기본 프레임은 공용 목록·dedup 집합에서 제외되어(`LocalFrameStore.cs:57-59,70-80`) 매번
  재다운로드된다 — 경고 로그로 표면화한다(동작 변경 없음).
- **대상 파일**: `src/MCPhoto.App/Services/FrameCatalogService.cs`, `tests/MCPhoto.Tests/`(테스트 추가)
- **선행 조건**: 없음 (Step 5의 선행)
- **구현 내용**:
  - `SemaphoreSlim(1,1)` 인스턴스 필드 추가, `GetDefaultFramesAsync` 본문 전체를 `await WaitAsync` / `finally Release`로 직렬화(싱글턴 서비스 — `ServiceRegistration.cs:80`).
  - `TryCacheAsync` 성공 시 Info 로그("기본 프레임 캐시: {Name} ← DB({Id})"), `f.Name.Contains('_')`면 Warning 로그(재다운로드 규약 충돌 안내) — 캐시·반환 동작은 현행 유지.
  - 테스트(주입 다운로드 함수 활용): ①지연 다운로드 함수로 2회 동시 호출 시 프레임당 다운로드 1회 ②`_` 포함 이름도 목록에 정상 포함(동작 불변).
- **검증 명령**: `dotnet test tests/MCPhoto.Tests/MCPhoto.Tests.csproj --filter "FullyQualifiedName~FrameCatalog"` + `dotnet build src/MCPhoto.App/MCPhoto.App.csproj -c Debug`
- **완료 기준**:
  - [관측] 동시 호출 테스트에서 다운로드 호출 수 = 프레임 수(중복 0); `_` 이름 테스트 PASS
  - [non-goal] 반환 목록 내용·순서·폴백 경로(번들/fallback) 불변; UI 스레드 블로킹 없음(비동기 대기만)
  - [trigger] 직렬화는 동시 호출 시에만 대기 발생 — 단일 호출 경로는 지연 없음
- **롤백**: 해당 커밋 revert
- [ ] 완료

### Step 5: App 시작 시 기본 프레임 prefetch
- **Context Brief**: 프레임 다운로드가 FrameSelect 진입 시에만 일어난다(`FrameSelectViewModel.cs:60,74`).
  요구사항은 "실행 시" 확보 — `App.OnStartup`의 기존 fire-and-forget 선례(`EnsureSeedAsync`, `App.xaml.cs:73-92`)와
  동일 패턴으로 `FrameCatalogService.GetDefaultFramesAsync()`를 1회 호출한다(부수효과인 로컬 캐시가 목적).
- **대상 파일**: `src/MCPhoto.App/App.xaml.cs`
- **선행 조건**: Step 4 (직렬화 게이트 — FrameSelect 진입과의 경합 안전)
- **구현 내용**: `OnStartup`의 `_ = EnsureSeedAsync();` 아래에 `_ = PrefetchDefaultFramesAsync();` 추가.
  private async 메서드: try/catch로 `_host.Services.GetService<FrameCatalogService>()` 획득 후 `GetDefaultFramesAsync()` await,
  실패는 `Log.Warning`(앱 동작 영향 없음 — FrameSelect 진입 시 재시도됨). UI 접근 없음.
- **검증 명령**: `dotnet build src/MCPhoto.App/MCPhoto.App.csproj -c Debug`; 실행 확인:
  실행폴더 `Frame\`에서 DB 캐시 파일 제거(번들 외) 후 앱 실행 → 화면 조작 없이 대기 →
  `%ProgramData%\MCPhoto\logs\mcphoto-*.log`에 "기본 프레임 캐시" Info 출현 + `Frame\`에 png/.slots 생성.
- **완료 기준**:
  - [관측] 키 있는 환경에서 앱 실행만으로(FrameSelect 미진입) Frame 폴더에 DB isDefault 프레임 캐시 생성, 로그에 캐시 Info
  - [non-goal] 시작 화면 표시 지연 없음(백그라운드); 키 없는 환경에선 조용히 스킵(경고 로그만, 크래시·팝업 없음); 기존 캐시 파일은 재다운로드하지 않음
  - [trigger] prefetch는 앱 시작 시 1회만 — 이후 다운로드는 기존 FrameSelect 진입 경로
- **롤백**: 해당 커밋 revert (Step 4와 독립 revert 가능)
- [ ] 완료

### Step 6: 설정 화면 서버 연결 상태 표시
- **Context Brief**: QA 트러블슈팅 시 "지금 서버에 붙어 있는가"를 로그 열지 않고 확인할 수단이 없다.
  설정 화면(웹 연동 섹션, StorageBucket 편집 근처)에 읽기 전용 상태 행을 추가한다.
- **대상 파일**: `src/MCPhoto.App/ViewModels/SettingsViewModel.cs`, `src/MCPhoto.App/Views/SettingsView.xaml`, `tests/MCPhoto.Tests/`(테스트 추가)
- **선행 조건**: 없음 (Step 1~5와 병렬 가능)
- **구현 내용**:
  - `SettingsViewModel` 생성자(`SettingsViewModel.cs:84-86`)에 `IFirebaseClient` 주입.
    읽기 전용 프로퍼티: `IsServerConnected`(= IsInitialized), `ServerStatusText`(연결 시 "연결됨 — {Bucket}", 미연결 시 "미연결 — 서비스 계정 키 없음(로그 참조)").
  - `SettingsView.xaml` 웹 연동 섹션에 표시 전용 행 1줄(기존 팔레트 리소스 재사용, 신규 리소스 키 금지). 저장 로직 무간섭(편집 대상 아님).
  - 테스트: fake `IFirebaseClient` 두 상태로 `ServerStatusText`/`IsServerConnected` 검증.
- **검증 명령**: `dotnet test tests/MCPhoto.Tests/MCPhoto.Tests.csproj --filter "FullyQualifiedName~SettingsViewModel"` +
  `dotnet test tests/MCPhoto.Tests/MCPhoto.Tests.csproj --filter "FullyQualifiedName~XamlResource"` + `dotnet build src/MCPhoto.App/MCPhoto.App.csproj -c Debug`
- **완료 기준**:
  - [관측] VM 테스트 PASS; 키 있는 실행 → 설정에 "연결됨 — mcphoto-955fb.firebasestorage.app", 키 없는 실행 → "미연결 —..." 표시
  - [non-goal] 설정 저장/취소 동작·저장 항목 불변(상태 행은 저장 대상 아님); 기존 설정 화면 레이아웃 파손 없음(1280px 최소 폭 겹침 없음)
  - [trigger] 상태는 화면 표시 시 평가 — 사용자 입력으로 변하지 않음(읽기 전용)
- **롤백**: 해당 커밋 revert
- [ ] 완료

### Step 7: 통합 스모크 — fresh PC 시나리오 재현 (가정 A1~A3 검증)
- **Context Brief**: QA PC를 재현한다: "베타 폴더(exe+키)만 있고 ProgramData 키·Frame 캐시가 없는 상태"에서
  DB 계정 로그인과 프레임 자동 다운로드를 검증한다. 개발 PC의 `C:\ProgramData\MCPhoto\serviceAccountKey.json`이
  간섭하지 않도록 검증 중 임시 개명한다(종료 후 원복 필수).
- **대상 파일**: 없음(검증 전용)
- **선행 조건**: Step 1~6 완료
- **구현 내용**(검증 절차):
  1. `publish.ps1` 실행 → `publish\MCPhoto\`에 exe+키 확인.
  2. `C:\ProgramData\MCPhoto\serviceAccountKey.json` → `serviceAccountKey.json.bak` 개명(간섭 차단).
  3. `publish\MCPhoto\` 전체를 임시 폴더(예: `%TEMP%\mcphoto-beta\`)로 복사, 그 안의 `Frame\`에서 번들 외 캐시 파일 삭제.
  4. 임시 폴더의 MCPhoto.exe 실행 → (a) 로그에 "Firebase 초기화 완료 ... key={임시폴더 경로}" (b) 화면 조작 없이 Frame\에 DB 프레임 캐시 생성[A3] (c) manager(또는 user) 로그인 성공[A2] (d) 설정 화면 "연결됨" 표시.
  5. 키 파일을 임시 폴더에서 제거 후 재실행 → 로그인 화면 오프라인 배너 + manager 로그인 시 오프라인 메시지 + 설정 "미연결" 표시.
  6. **원복**: ProgramData 키 `.bak` 제거(원래 이름으로), 임시 폴더 삭제.
- **검증 명령**: 절차 4·5의 관측 항목 체크리스트 + `Get-Content %ProgramData%\MCPhoto\logs\mcphoto-*.log -Tail 50`으로 초기화/캐시 로그 확인 (반자동 — 실행·화면 확인 포함)
- **완료 기준**:
  - [관측] 절차 4의 (a)~(d) 전부 성공(= A1 개발망 기준·A2·A3 검증됨); 절차 5의 오프라인 3종 표시 확인
  - [non-goal] 스모크 종료 후 개발 PC 상태 원복(ProgramData 키 복원·임시 폴더 삭제); 리포 작업 트리에 변경 없음
  - [trigger] QA 실PC(사내망) 검증은 사용자 몫 — A1의 최종 확증은 QA PC 1대에서 절차 4 반복
- **롤백**: 검증 전용 — 원복 절차(6) 수행만
- [ ] 완료

---

## 완결성 게이트 (자체 검사 결과)
- [x] 검증된 사실 / 미검증 가정 분리
- [x] 모든 가정(A1~A4)에 검증 단계 매핑 (A1: Step 7+사용자, A2·A3: Step 7, A4: Step 3)
- [x] 전 단계 7개 필수 필드 충족
- [x] 완료 기준 관측 기반 3문 형식 (UI 단계 3·6은 non-goal·trigger 포함)
- [x] 검증 명령 자동 실행 가능(dotnet build/test --filter, Test-Path, powershell 스크립트; Step 5·7 일부 반자동은 절차 명시)

## 구현 우선순위
1. **Step 1** (근본 해결 — 이것만으로 베타 연동 성립)
2. Step 2·3·4·6 (병렬 가능)
3. Step 5 (Step 4 후)
4. Step 7 (전체 후 통합 검증)
