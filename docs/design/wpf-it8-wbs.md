# MC포토 — 이터레이션 8 구현 WBS

| 항목 | 값 |
|------|-----|
| 대상 | `MCPhoto.sln`(WPF) — 이터레이션 8(유휴·프레임구조·설정·필터·카메라, 대규모 A1~A7) |
| 설계 근거 | `docs/design/wpf-it8-design.md`, `docs/prd/iteration-8-idle-frames-settings-filters.md`, `firebase-contract.md` |
| 형식 | `docs/templates/WBS_BLUEPRINT.md` 준수 |
| 작성일 | 2026-07-22 |
| 빌드 검증 | `dotnet build MCPhoto.sln -c Release`(error 0, 변경 프로젝트 warning 0) / `dotnet test` |

> 각 Step은 self-contained다. fresh 에이전트가 그 Step과 `wpf-it8-design.md`만 읽고 실행할 수 있게 작성했다.
> **완료 기준은 headless(dotnet build/test·grep)로만 판정.** UI 육안은 각 Step "사용자 확인 필요"로 분리, 전체는 `wpf-it8-design.md` §10.
> ⚠️ **앱 실행 금지**(사용자 PC 사용 중 + UI 실행 차단 훅). 검증은 build/test/grep만.
> 색·토큰=라이트 A(it2). 순수 로직(유휴 카운트다운·로컬 프레임 경로/명명/slots·필터·프리뷰 판정·QR off→on)은 단위 테스트화.

---

## 검증된 사실 (verified facts)

- **VF-1**: 자동 로그아웃은 `AppShellViewModel.cs:203`(유휴 `clearUser:true`) 한 곳만. Done·Home은 이미 false(it5). (근거: `grep clearUser` 전수)
- **VF-2**: 유휴 = `IdleWatchdog`(Timer)→`IdleTimeout`→`OnIdleTimeout`→즉시 `ReturnHome`. 경고·카운트다운 없음. (근거: `IdleWatchdog.cs`, `AppShellViewModel.cs:183-203`)
- **VF-3**: 유휴 감시 대상 = `IsSessionActive`(FrameSelect~Qr, FrameEditor 제외 it4). `UpdateIdleWatch`가 상태별 Start/Stop. (근거: `SessionStateMachine.cs`)
- **VF-4**: `SessionContext.Reset(clearUser)` — false=촬영데이터만·계정보존, true=Logout 통지. (근거: `SessionContext.cs:65-80`)
- **VF-5**: `FrameRepository.SaveAsync`가 모든 프레임 DB(Firestore+Storage) 저장, 로컬 경로 없음. (근거: `FrameRepository.cs:44-75`)
- **VF-6**: 로딩 = DB isDefault→번들 Frame/→fallback. 번들 `.slots`="index,x,y,w,h"(5필드). (근거: `FrameCatalogService.cs`)
- **VF-7**: `Slot.AspectRatio`는 w/h 계산 프로퍼티(별도 저장 불필요). (근거: `Slot.cs`, it4)
- **VF-8**: `FrameRepository.DeleteAsync`(Firestore+Storage) 존재, 로컬 삭제·선택창 삭제 UI 없음. (근거: `FrameRepository.cs:77-100`)
- **VF-10**: 필터 실처리(`Filters.cs` 흑백/밝기/뷰티 OpenCV) 구현 완료. ResultView 4버튼 하드코딩. 설정 노출 없음. (근거: `Filters.cs`, `ResultView.xaml:28-47`)
- **VF-11**: 카메라 Ready=첫 프레임 1회(`WaitForFirstFrameAsync`, 8초 타임아웃). (근거: `CaptureViewModel.cs:78-117`)
- **VF-12**: it7 `AppSettings.SendPhoto/SendTimelapse`·`QrDeliveryPolicy`(둘 다 off→QR off) 반영됨. (근거: it7)
- **VF-13**: 설정 [저장]·[닫기]가 ScrollViewer 내부(스크롤해야 보임). (근거: `SettingsView.xaml`)
- **VF-14**: `frameTemplates`는 웹 접근 없음(WPF 전용, 계약 §5). (근거: `firebase-contract.md:180,198`)
- **VF-15**: 기존 테스트 자산 다수(`SettingsTests`·`AppStateTests`·`SlotLayoutTests`·`FrameEditorViewModelTests`·`QrDeliveryPolicyTests` 등). (근거: `tests/MCPhoto.Tests/`)

## 미검증 가정 (open assumptions)

- **OA-1**: 유휴 경고 팝업+clearUser:false가 무인 안정성 유지(홈 복귀는 발생, 로그아웃만 제거) → **검증: Step 1**.
- **OA-2**: 로컬 프레임 저장/로딩(`<계정>_<이름>` + `.slots`)이 번들·DB폴백과 공존 → **검증: Step 2**.
- **OA-3**: 파워 캐시(없을 때만 DB 다운로드)가 캐시 히트/미스에서 정확 → **검증: Step 3**.
- **OA-4**: 삭제(로컬 항상+파워 서버옵션)가 권한별 정확 → **검증: Step 4**.
- **OA-5**: 안정적 프리뷰 판정(N프레임/최소경과)이 실사용 시점과 부합 → **검증: Step 7**.

> 모든 가정이 검증 Step에 매핑됨(완결성 게이트 통과).

---

## 단계 의존 그래프 (병렬 식별)

```
Step 1 (A1 유휴 재설계·로그아웃 제거)   ── 최우선·독립
Step 2 (A2 로컬 프레임 저장 ILocalFrameStore + 역할별 저장) ── 독립
Step 3 (A2 로컬 우선 로딩 + 파워 캐시)  ← Step 2(ILocalFrameStore)
Step 4 (A3 프레임 삭제 UI)             ← Step 2(DeleteLocal)
Step 5 (A4 sticky + A5 QR off→on)      ── 독립(SettingsView/VM)
Step 6 (A6 필터 설정화 + 실처리 검증)  ── 독립(설정·ResultView·Filters)
Step 7 (A7 카메라 Ready 강화)          ── 독립(CaptureViewModel)
```

- Step 1(A1) 최우선. Step 2→3→4는 로컬 프레임 체인(Step 2에서 `ILocalFrameStore` 정의). Step 5·6·7 독립·병렬 가능.

---

## Step 1: A1 — 유휴 타임아웃 재설계 (경고 팝업 + 로그아웃 절대 제거)

- **Context Brief**: 현재 무동작 시 경고 없이 즉시 홈 복귀 + 로그아웃(`AppShellViewModel.cs:203` clearUser:true, VF-1·2). 요구: 2분 무동작 → "XX초 후 메인 복귀" 팝업(10초 카운트다운)+[이어서 진행하기]/[메인 화면으로], 카운트다운 0→홈 복귀하되 **로그아웃 절대 금지**. 로그아웃은 로그아웃 버튼 전용. 자동 로그아웃 경로(clearUser:true) 전면 제거(설계 §2).
- **대상 파일**: `src/MCPhoto.App/AppShellViewModel.cs`(유휴 2단계·경고 오버레이 상태·카운트다운·clearUser 제거), `src/MCPhoto.Core/Navigation/IdleWatchdog.cs`(경고 트리거), `src/MCPhoto.Core/`(신규 `IdleCountdown` 순수 로직), `src/MCPhoto.App/MainWindow.xaml`(경고 오버레이 레이어), `tests/MCPhoto.Tests/IdleCountdownTests.cs`·`AppStateTests.cs`(확장).
- **선행 조건**: 없음.
- **구현 내용**:
  - 상수: `IdleWarningSeconds=120`(2분), `IdleCountdownSeconds=10`. `IdleTimeoutSeconds`(75) → warning 120으로.
  - `AppShellViewModel`: `OnIdleTimeout`을 "경고 표시"로 변경 — `IsIdleWarningVisible=true` + 카운트다운 시작(`DispatcherTimer` 10→0, `IdleCountdownRemaining` 노출). 카운트다운 0 or [메인] → `ReturnHome("유휴", clearUser: false)`. [이어서] or (경고 전 활동) → 경고 해제·`_idle.Reset()`(warning 재시작). `IsIdleWarningVisible`/`IdleCountdownRemaining`/`ContinueSessionCommand`/`GoHomeFromIdleCommand` 노출.
  - **`clearUser: true` → `false`**(203행). grep로 코드 전체 `clearUser: true` 호출 0 확인.
  - `IdleCountdown`(순수): 시작값(10)·Tick 감소·0 도달 완료 이벤트·리셋. UI 비의존(DispatcherTimer는 셸, 로직은 순수 클래스로 테스트).
  - `MainWindow.xaml`: 유휴 경고 오버레이(스크림 + 카드 "메인 화면으로 돌아갑니다 · {IdleCountdownRemaining}초" + [이어서 진행하기]/[메인 화면으로]). `Visibility={Binding IsIdleWarningVisible}`. 라이트 토큰.
  - 유휴 감시 대상: `IsSessionActive`(기존) 유지 + FrameSelect 포함 확인(요구 "프레임 선택 등 어느 화면"). FrameEditor 제외 유지(편집 손실 방지).
  - 테스트: `IdleCountdownTests`(10→0 Tick·리셋·완료). `AppStateTests`(경고 대상 상태). grep clearUser:true==0.
- **검증 명령**: `dotnet test --filter IdleCountdownTests` + `dotnet test --filter AppStateTests` + `dotnet build -c Release`(error 0, warning 0). `grep`로 `clearUser: true` 호출 0, `AppShellViewModel`에 IsIdleWarningVisible·IdleCountdownRemaining.
- **완료 기준**:
  - [관측] `IdleCountdownTests`·`AppStateTests` 통과. 빌드 통과. `grep`: 코드에 `clearUser: true` 호출 0(유휴 포함 모든 자동 경로 false), `AppShellViewModel`에 유휴 경고 상태·카운트다운·2버튼 커맨드, `MainWindow.xaml`에 경고 오버레이.
  - [non-goal] **로그아웃은 `Logout()`(로그아웃 버튼) 경로만** — 유휴·세션완료·화면이동 자동 로그아웃 0. 홈 복귀 자체는 유지(무인 대기). 촬영 세션 데이터 Reset(clearUser:false)은 유지.
  - [trigger] 경고 팝업=2분 무동작 시. 카운트다운 0 or [메인]=홈 복귀(로그인 유지). [이어서]=현재 화면 유지·타이머 리셋. 로그아웃=버튼만.
  - [사용자 확인 필요] 2분→경고·10초 카운트다운→[이어서]/[메인]/0초, 모든 경우 로그인 유지(design §10-1).
- **롤백**: 이 Step 커밋 revert(유휴 2단계·오버레이·clearUser 원복).
- [ ] 완료

---

## Step 2: A2 — 로컬 프레임 저장(ILocalFrameStore) + 역할별 저장 분기

- **Context Brief**: 현재 프레임은 전부 DB 저장(VF-5). 요구: 파워(admin/manager)=DB+로컬캐시, user=로컬 전용(`<계정명>_<프레임이름>.png`+`.slots`). 이 Step은 로컬 저장소(`ILocalFrameStore`)와 편집기 저장 분기를 만든다. 로딩·캐시는 Step 3(설계 §3).
- **대상 파일**: `src/MCPhoto.Core/Frames/ILocalFrameStore.cs`·`LocalFrameStore.cs`(신규), `src/MCPhoto.App/ViewModels/FrameEditorViewModel.cs`(역할별 저장), `src/MCPhoto.App/ServiceRegistration.cs`(DI), `tests/MCPhoto.Tests/LocalFrameStoreTests.cs`(신규).
- **선행 조건**: 없음.
- **구현 내용**:
  - `ILocalFrameStore`/`LocalFrameStore`: 저장 폴더 = **`AppContext.BaseDirectory\Frame`**(앱 실행 폴더, 쓰기 가능 전제 = `FrameCatalogService.BundleFolder`와 동일). 메서드: `SaveLocal(FrameTemplate frame, byte[] png, string? ownerName)` → ownerName 있으면 `{owner}_{name}.png`+`.slots`(user 전용, 접두), 없으면 `{name}.png`+`.slots`(공용/파워 캐시, 접두 없음). **이름 원문 그대로**(sanitize·`_`치환 없음); 파일시스템 금지문자(`\ / : * ? " < > |`)만 저장 거부(유효성 검사). `.slots` 포맷: 첫 줄 `#imagesize=W,H`, 이후 "index,x,y,w,h"(하위호환 5필드). `LoadPublic()`(접두 없는 파일=번들+파워캐시)·`LoadUser(string ownerName)`(`{owner}_` 접두만)·`DeleteLocal(FrameTemplate)`·`CacheFromDb(FrameTemplate)`(Step 3). 접두 파싱=첫 `_` 앞이 로그인 계정명과 일치할 때만 user 전용(§3.1.1 모호성 수용).
  - `FrameEditorViewModel.Save` 분기: 로그인 역할 —
    - 파워(`IsPower`): `FrameRepository.SaveAsync`(DB, isDefault=true, userId=null) + `LocalFrameStore.SaveLocal(ownerName: null)`(공용 캐시, 접두 없음, 이름 기반).
    - user: `LocalFrameStore.SaveLocal(ownerName: 계정id)`만(DB 미호출, `{계정}_{이름}` 접두). 10개 제한은 로컬 파일 수로.
  - `ServiceRegistration`: `AddSingleton<ILocalFrameStore, LocalFrameStore>()`.
  - 테스트(`LocalFrameStoreTests`): 루트=`AppContext.BaseDirectory\Frame`. user 저장(ownerName)→`계정_이름.png`+`.slots`(접두), 파워 저장(ownerName=null)→`이름.png`(접두 없음). slots 라운드트립(개수·좌표·크기·imagesize). `LoadPublic()`→접두 없는 파일만, `LoadUser(계정)`→`계정_` 접두만. 이름 원문 그대로(sanitize 없음, `_` 보존). 금지문자 이름 저장 거부. 첫 `_` 앞=계정 파싱.
- **검증 명령**: `dotnet test --filter LocalFrameStoreTests` + `dotnet build -c Release`(error 0, warning 0). `grep`로 `LocalFrameStore` 루트가 `AppContext.BaseDirectory`(ProgramData 아님)·sanitize 없음·`LoadPublic`/`LoadUser`, `FrameEditorViewModel` 역할 분기.
- **완료 기준**:
  - [관측] `LocalFrameStoreTests` 통과: user 저장 접두 명명·파워 무접두·slots 라운드트립·LoadPublic/LoadUser 분리·이름 원문(sanitize 없음). 빌드 통과. 루트가 `AppContext.BaseDirectory\Frame`(grep). `FrameEditorViewModel.Save` 역할 분기(파워=DB+무접두 로컬, user=접두 로컬)(grep).
  - [non-goal] 파워 DB 저장(`FrameRepository`)·계약 경로는 **변경하지 않는다**(user만 DB 미호출로). 로딩·캐시·dedup은 Step 3. 슬롯 좌표계(it4)·종횡비(w/h) 불변. **파일명 sanitize·`_`치환 안 함**(이름 원문). %ProgramData% 저장 안 함.
  - [trigger] 로컬 저장은 [저장] 시 역할별. user는 DB 미호출, 접두 명명. 파워는 접두 없는 공용 명명.
  - [사용자 확인 필요] user 생성→로컬만(`계정_이름.png`, 본인만 노출), 파워 생성→DB+로컬(접두 없음, 게스트 포함 공용), 번들과 같은 폴더 공존(design §10-2).
- **롤백**: 이 Step 커밋 revert(LocalFrameStore·편집기 분기·DI·테스트 원복).
- [ ] 완료

---

## Step 3: A2 — 로컬 우선 로딩 + 파워 프레임 캐시(없을 때만 DB 다운로드)

- **Context Brief**: 프레임 로딩을 로컬 우선으로 바꾼다(설계 §3.1·3.3). 로컬 폴더 = 실행 폴더 `AppContext.BaseDirectory\Frame`(번들+파워캐시+user 공존). 공용 프레임(번들+파워캐시, 접두 없음) = 로컬에 있으면 사용(캐시 히트, DB 미접근), 없으면 DB isDefault 다운로드 후 이름 기반 로컬 캐시. user 프레임 = 로컬 `{계정}_` 접두 전용. 이름 기준 dedup(중복 집계 방지). DB 접근·서버 부하 최소화.
- **대상 파일**: `src/MCPhoto.App/Services/FrameCatalogService.cs`(로컬 우선·이름 dedup·공용/user 분리), `src/MCPhoto.Core/Frames/ILocalFrameStore.cs`(`CacheFromDb` 추가), `tests/MCPhoto.Tests/FrameCatalogServiceTests.cs`(신규 or 확장).
- **선행 조건**: Step 2(`ILocalFrameStore`, `LoadPublic`/`LoadUser`).
- **구현 내용**:
  - `FrameCatalogService.GetDefaultFramesAsync` 개편: ① `ILocalFrameStore.LoadPublic()`(접두 없는 로컬=번들+파워캐시) 스캔 → 있으면 사용(**DB 미조회**). ② 로컬에 없는 DB isDefault만 `CacheFromDb`(이름 기반 `{name}.png`+`.slots` 로컬 기록) 후 병합 → 사용. **이름 기준 dedup**(로컬에 이미 있으면 DB 항목 스킵, 중복 집계 없음, §3.1.1). ③ DB도 없으면 fallback. **캐시 히트 시 DB 호출 0**.
  - `GetUserFramesAsync` 개편: DB(`FrameRepository.GetUserFramesAsync`) 대신 **`LocalFrameStore.LoadUser(userId)`**(`{userId}_` 접두 로컬, Step 2). DB user 조회 제거.
  - `ILocalFrameStore.CacheFromDb(FrameTemplate)`: DB 프레임 이미지(ImageUrl) 다운로드 → 로컬 `{name}.png`+`.slots`(공용, 접두 없음). 실패 시 DB 프레임 그대로 사용(폴백).
  - 오프라인: DB 조회 실패 시 로컬 공용/fallback(기존 폴백 유지).
  - 테스트: 로컬에 공용(접두 없는) 프레임 존재 시 목 repo `GetDefaultFramesAsync` 호출 0(캐시 히트). 미존재 시 1회 호출+캐시. 이름 중복 시 dedup(로컬 우선). user는 `LoadUser(계정)`에서 로드(접두).
- **검증 명령**: `dotnet test --filter FrameCatalogServiceTests` + `dotnet build -c Release`(error 0, warning 0). `grep`로 `FrameCatalogService`가 `LoadPublic`/`LoadUser` 사용·로컬 우선·이름 dedup.
- **완료 기준**:
  - [관측] `FrameCatalogServiceTests` 통과: 로컬 공용 존재 시 DB 미조회(repo 호출 0), 미스 시 다운로드+이름기반 캐시, 이름 중복 dedup, user는 `LoadUser` 접두 로드. 빌드 통과. `GetUserFramesAsync`가 `LoadUser` 사용(grep).
  - [non-goal] fallback 폴백(오프라인)은 **유지**. 파워 DB 저장(Step 2)·계약 불변. 번들 프레임은 삭제 불가 공용으로 그대로 노출. 캐시 무효화(DB 갱신 stale)는 범위 밖(이름 기준).
  - [trigger] DB 다운로드는 로컬(이름) 미존재 시에만(캐시 히트면 0). user 로딩은 `{계정}_` 접두 파일.
  - [사용자 확인 필요] 파워 프레임 재사용 시 로컬 캐시(다운로드 안 함)·게스트 포함 공용 노출, user는 본인 접두만, 번들 공존(design §10-2).
- **롤백**: 이 Step 커밋 revert(FrameCatalogService·CacheFromDb 원복 → DB 우선).
- [ ] 완료

---

## Step 4: A3 — 프레임 삭제 UI (선택창 카드 X + 권한별)

- **Context Brief**: 프레임 선택 화면에서 삭제가 불가하다(VF-8·9). 요구: 로그인 관리자 시 카드 우상단 X → 확인 팝업(파워면 "서버에서도 제거" 체크 기본 off) → 로컬 삭제(+옵션 DB). 게스트/비로그인 미노출(설계 §4).
- **대상 파일**: `src/MCPhoto.App/ViewModels/FrameSelectViewModel.cs`(삭제 커맨드·권한·확인), `src/MCPhoto.App/Views/FrameSelectView.xaml`(카드 X·확인 팝업), `tests/MCPhoto.Tests/FrameSelectViewModelTests.cs`(신규 or 확장).
- **선행 조건**: Step 2(`ILocalFrameStore.DeleteLocal`), Step 3(로딩). `FrameRepository.DeleteAsync`(기존).
- **구현 내용**:
  - `FrameSelectViewModel`: `bool CanDeleteFrames`(로그인 여부), `bool IsPower`. `RequestDeleteCommand(FrameTemplate)` → 확인 팝업 표시(오버레이 상태 `IsDeleteConfirmVisible`·`FrameToDelete`·`DeleteAlsoServer`(파워만)). `ConfirmDeleteCommand` → 항상 `ILocalFrameStore.DeleteLocal(frame)`, `DeleteAlsoServer && IsPower`면 `FrameRepository.DeleteAsync(frame.Id)`. 목록 갱신(컬렉션 제거 or 재로드). `CancelDeleteCommand`.
  - 삭제 대상 판별: user는 자기 로컬(`{계정}_` 접두) 프레임만 X. 파워는 공용(접두 없는 파워캐시)+자기. **번들 프레임·fallback은 삭제 불가**(설치 자산, X 미노출). 파워 캐시(공용, 접두 없음)는 파워 삭제 가능(로컬+옵션 DB). 삭제 시 로컬 파일은 이름/접두로 매칭, DB는 `frame.Id`로 매칭.
  - `FrameSelectView.xaml`: 카드 DataTemplate 우상단 X 버튼(`Visibility={Binding DataContext.CanDeleteFrames, RelativeSource=부모}` + 프레임별 삭제 가능 여부). 확인 팝업 오버레이("삭제하시겠습니까?" + 파워면 "서버에서도 제거" CheckBox 기본 off + [확인]/[취소]).
  - 테스트: `CanDeleteFrames`(게스트 false·로그인 true). 삭제 정책 — user는 로컬만·DeleteAlsoServer 무시, 파워는 체크 시 DB도. 목 store/repo로 호출 검증.
- **검증 명령**: `dotnet test --filter FrameSelectViewModelTests` + `dotnet build -c Release`(error 0, warning 0). `grep`로 `FrameSelectViewModel`에 삭제 커맨드·권한, `FrameSelectView.xaml`에 카드 X·확인 팝업.
- **완료 기준**:
  - [관측] `FrameSelectViewModelTests` 통과: 게스트 CanDeleteFrames=false, user 로컬 삭제(서버 옵션 없음), 파워 로컬+체크 시 DB. 빌드 통과. View에 카드 X(권한 조건)·확인 팝업(파워=서버 체크)(grep).
  - [non-goal] 게스트/비로그인 X **미노출**. user는 서버 제거 옵션 **없음**(로컬만). 번들/fallback 삭제 불가. `FrameRepository.DeleteAsync` 로직 불변(호출만).
  - [trigger] 삭제는 X→확인 팝업 [확인] 시. DB 삭제는 파워+"서버에서도 제거" 체크 시만.
  - [사용자 확인 필요] 로그인 시 X 노출·게스트 미노출, 확인 팝업(파워 서버체크), 삭제·목록 갱신(design §10-3).
- **롤백**: 이 Step 커밋 revert(삭제 커맨드·View·테스트 원복).
- [ ] 완료

---

## Step 5: A4·A5 — 설정 sticky 하단바 + QR off→on 세부 자동 on

- **Context Brief**: 설정 저장/닫기가 스크롤 내부라 안 보이고(VF-13, A4), QR을 껐다 켜면 세부 토글이 자동으로 안 켜진다(A5). sticky 하단바 + off→on 시 SendPhoto/SendTimelapse 둘 다 on 강제(설계 §5).
- **대상 파일**: `src/MCPhoto.App/Views/SettingsView.xaml`(sticky 하단바), `src/MCPhoto.App/ViewModels/SettingsViewModel.cs`(off→on 연동), `src/MCPhoto.Core/`(`QrDeliveryPolicy` 확장), `tests/MCPhoto.Tests/QrDeliveryPolicyTests.cs`·`SettingsTests.cs`(확장).
- **선행 조건**: 없음. (it7 QrDeliveryPolicy 재사용.)
- **구현 내용**:
  - A4: `SettingsView.xaml`을 `Grid`(Row *: ScrollViewer 설정항목 / Row Auto: 하단 고정 바)로. 하단 바([저장]·[닫기]+저장 토스트)는 **ScrollViewer 밖**, `VerticalAlignment=Bottom`. 상단 구분선/그림자. 라이트 토큰.
  - A5: `SettingsViewModel.OnEnableQrDeliveryChanged(bool value)` — false→true 전환이면 `SendPhoto=true; SendTimelapse=true`(무조건). `QrDeliveryPolicy`에 `OnReEnabled()` 순수 함수(off→on 시 둘 다 on 반환) 추가. it7의 "둘 다 off→QR off"와 공존.
  - 테스트: `QrDeliveryPolicyTests`(off→on → SendPhoto·SendTimelapse 둘 다 true). `SettingsTests`(전환 반영·INI 라운드트립).
- **검증 명령**: `dotnet test --filter QrDeliveryPolicyTests` + `dotnet test --filter SettingsTests` + `dotnet build -c Release`(error 0, warning 0). `grep`로 `SettingsView.xaml` 하단바(ScrollViewer 밖), `SettingsViewModel` off→on 연동.
- **완료 기준**:
  - [관측] `QrDeliveryPolicyTests`(off→on 둘 다 on)·`SettingsTests` 통과. 빌드 통과. `SettingsView`가 하단 고정 바([저장]/[닫기] ScrollViewer 밖), `SettingsViewModel`이 off→on 시 하위 둘 다 on(grep).
  - [non-goal] it7 "둘 다 off→QR off"·개별 조절(QR on 상태)은 **유지**. 설정 항목·바인딩·저장 로직 불변(레이아웃·연동만). 저장 토스트(it3) 유지.
  - [trigger] 하위 둘 다 on은 QR off→on 전환 시만. 저장은 [저장] 버튼(하단바).
  - [사용자 확인 필요] 저장/닫기 스크롤 무관 노출, QR 껐다 켜면 둘 다 on(design §10-4·5).
- **롤백**: 이 Step 커밋 revert(SettingsView·VM·정책·테스트 원복).
- [ ] 완료

---

## Step 6: A6 — 필터 설정화 + 실처리 검증

- **Context Brief**: 필터(흑백/밝기/뷰티) 실처리는 이미 구현됐으나(`Filters.cs`, VF-10) 설정 노출·개별 on/off가 없다. 요구: 설정에서 필터 개별 on/off(기본 전부 on), 원본은 항상·토글 불가(Disable), 켜진 필터만 결과 화면 노출. 실처리 검증(설계 §6).
- **대상 파일**: `src/MCPhoto.Core/Settings/AppSettings.cs`·`IniSettingsService.cs`(필터 3토글), `src/MCPhoto.App/ViewModels/SettingsViewModel.cs`·`Views/SettingsView.xaml`(필터 그룹), `src/MCPhoto.App/ViewModels/ResultViewModel.cs`·`Views/ResultView.xaml`(동적 노출), `tests/MCPhoto.Tests/FiltersTests.cs`(신규)·`SettingsTests.cs`(확장).
- **선행 조건**: 없음.
- **구현 내용**:
  - `AppSettings`: `bool FilterGrayscale/FilterBrightness/FilterBeauty`(기본 true). INI read/write.
  - 설정 UI: SettingsView "필터" 그룹 — 흑백/밝기/뷰티 토글(기본 on). **원본은 토글 표시하되 Disable**(IsEnabled=false·체크 고정) 또는 "원본(항상 제공)" 안내.
  - `ResultViewModel`: `AvailableFilters` 리스트 = **항상 None** + 설정에서 켜진 것. ResultView의 하드코딩 4버튼 → `ItemsControl`(AvailableFilters 바인딩, `Button.Filter` 스타일, SetFilterCommand). 켜진 필터만 버튼.
  - `FiltersTests`(실처리 검증): `Apply(src, Grayscale)`→R=G=B, `Brightness`→평균 밝기 증가, `Beauty`→분산 감소(스무딩), `None`→원본 동일. (작은 합성 Mat로.)
  - 테스트: `ResultViewModel.AvailableFilters`(설정 반영·None 항상). `SettingsTests`(필터 토글 INI 라운드트립).
- **검증 명령**: `dotnet test --filter FiltersTests` + `dotnet test --filter SettingsTests` + `dotnet build -c Release`(error 0, warning 0). `grep`로 `AppSettings` 필터 토글, `ResultView.xaml` 동적(ItemsControl), SettingsView 필터 그룹(원본 Disable).
- **완료 기준**:
  - [관측] `FiltersTests`(실처리 결과 검증)·`SettingsTests`(토글 라운드트립) 통과. 빌드 통과. `ResultViewModel.AvailableFilters`가 None 항상+켜진 것, ResultView 동적 노출(grep). SettingsView 필터 그룹·원본 Disable.
  - [non-goal] `Filters.cs` 실처리 로직은 **변경하지 않는다**(이미 구현·검증만). 필터 미선택 시 원본. 원본은 끌 수 없음(항상 존재).
  - [trigger] 결과 화면 필터 버튼은 설정 on인 것만(+None). 필터 적용은 버튼 선택 시.
  - [사용자 확인 필요] 설정 필터 개별 on/off(원본 Disable), 켜진 것만 결과 노출, 실제 적용(design §10-6).
- **롤백**: 이 Step 커밋 revert(설정·ResultView 동적·테스트 원복 → 하드코딩 4버튼).
- [ ] 완료

---

## Step 7: A7 — 카메라 Ready 강화 (안정적 프리뷰까지 waiting)

- **Context Brief**: it5는 첫 프레임 1회로 Ready 판정(VF-11)이나, 그 후에도 카메라 실가동 지연으로 waiting이 일찍 사라진다. 요구: 안정적 프리뷰(실사용 가능) 시점까지 waiting 유지 — 연속 N프레임/최소 경과로 판정 강화(설계 §7).
- **대상 파일**: `src/MCPhoto.App/ViewModels/CaptureViewModel.cs`(Ready 판정 강화), `src/MCPhoto.Core/Capture/`(신규 `PreviewReadiness` 순수 판정), `tests/MCPhoto.Tests/PreviewReadinessTests.cs`(신규).
- **선행 조건**: 없음.
- **구현 내용**:
  - `PreviewReadiness`(순수): 프레임 이벤트 카운트 + 최소 경과 시간 누적 → Ready 판정. 파라미터(예: `RequiredFrames=8`, `MinElapsedMs=500`), 둘 다 충족 시 ready. `CurrentFps>0` 확인(선택). 타임아웃(전체 8초) 미충족 시 실패. UI/시간은 주입(테스트 가능).
  - `CaptureViewModel.WaitForFirstFrameAsync` → `WaitForStablePreviewAsync`: 첫 프레임 후 `PreviewReadiness` 조건(N프레임+최소경과) 충족까지 대기. 미달·타임아웃이면 Failed. `CameraLoadState`(Initializing→Ready) 유지, 조건만 강화. 로딩 오버레이(it5)는 Ready까지 표시 → 자연히 실사용 시점까지 유지.
  - 테스트(`PreviewReadinessTests`): N프레임 미만 → not ready, 최소경과 미달 → not ready, 둘 다 충족 → ready, 타임아웃 → failed. (프레임 이벤트·시간 모킹.)
- **검증 명령**: `dotnet test --filter PreviewReadinessTests` + `dotnet build -c Release`(error 0, warning 0). `grep`로 `PreviewReadiness`·`CaptureViewModel` 강화 판정.
- **완료 기준**:
  - [관측] `PreviewReadinessTests` 통과: N프레임+최소경과 충족 시 ready, 미달 not ready, 타임아웃 failed. 빌드 통과. `CaptureViewModel`이 `PreviewReadiness`로 Ready 판정(grep: 첫 프레임 1회 판정 제거).
  - [non-goal] 캡처 시퀀스·크롭·녹화는 **변경하지 않는다**(Ready 게이트 조건만 강화). `ICameraService` 인터페이스 변경 없음(FrameReady/CurrentFps 사용). 전체 타임아웃(8초) 유지(무한 로딩 방지).
  - [trigger] Ready는 안정적 프리뷰(N프레임+최소경과) 충족 시. 로딩 오버레이는 Ready까지. 시퀀스는 Ready 이후.
  - [사용자 확인 필요] 촬영 진입 시 실제 프리뷰 안정될 때까지 waiting, 사라진 직후 영상 정상(design §10-7).
- **롤백**: 이 Step 커밋 revert(PreviewReadiness·CaptureViewModel 원복 → 첫 프레임 판정).
- [ ] 완료

---

## 완결성 게이트 (자체 검사)

- [x] 검증된 사실(VF-1~15) / 미검증 가정(OA-1~5) 분리됨
- [x] 모든 가정에 검증 Step 매핑됨 (OA-1→1, OA-2→2, OA-3→3, OA-4→4, OA-5→7)
- [x] 모든 Step(1~7)에 7개 필수 필드
- [x] 모든 완료 기준이 관측 기반 3문 형식(관측·non-goal·trigger). UI Step은 "사용자 확인 필요" 포함
- [x] 검증 명령이 자동 실행 가능(`dotnet build -c Release`/`dotnet test --filter`/`grep`) — **앱 실행 없음**
- [x] 순수 로직(유휴 카운트다운·로컬 프레임 경로/명명/slots·필터 실처리·프리뷰 판정·QR off→on) 단위 테스트화
- [x] UI 육안은 각 Step "사용자 확인 필요" + `wpf-it8-design.md` §10에 집약

## 진행 상태 어휘 (developer 보고 시)

`inspected` / `changed locally` / `verified locally`(build+test 통과) / `committed` / `pushed` / `blocked`(사유 명시 필수)
