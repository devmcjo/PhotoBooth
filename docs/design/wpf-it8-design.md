# MC포토 — 이터레이션 8 설계 (유휴 재설계·프레임 저장구조·설정·필터·카메라)

| 항목 | 값 |
|------|-----|
| 문서 | WPF 이터레이션 8 설계 본문(대규모 A1~A7) |
| 작성일 | 2026-07-22 |
| 상태 | 초안 v1 (구현 착수 전) |
| 1차 준거 | `docs/prd/iteration-8-idle-frames-settings-filters.md` |
| 계약 | `docs/design/firebase-contract.md`(frameTemplates — A2 영향) |
| 상위 준거 | it2~it7, PRD v2.7 §9 |
| 구현 WBS | `docs/design/wpf-it8-wbs.md` |
| 코드 베이스 | `E:\Study\photobooth\src\`. it2~it7 반영, Firebase 실배포 완료 |

> 대규모 배치: **A1 유휴 재설계(최우선, 로그아웃 절대 금지)**, **A2 프레임 저장 하이브리드(파워=DB+로컬캐시/user=로컬전용)**, **A3 프레임 삭제 UI**, **A4 설정 sticky 하단바**, **A5 QR off→on 세부 자동 on**, **A6 필터 설정화+실처리**, **A7 카메라 Ready 강화**. SSO·세션만료는 비범위.

---

## 0. 검증된 사실 / 미검증 가정

### 검증된 사실 (코드 인스펙션으로 직접 확인)

- **VF-1. 자동 로그아웃은 유휴 타임아웃 한 곳만 남았다**: it5(B8) 이후 `DoneViewModel`(세션 완료)·`HomeViewModel`(촬영 시작)은 이미 `clearUser:false`. `clearUser:true`가 남은 유일한 곳은 **`AppShellViewModel.cs:203` `OnIdleTimeout` → `ReturnHome("유휴 타임아웃", clearUser: true)`**. (근거: `grep clearUser` 전수 — `AppShellViewModel.cs:203`, `DoneViewModel.cs:23,37`, `HomeViewModel.cs:20`)
- **VF-2. 유휴는 `IdleWatchdog`(Timer) → `IdleTimeout` 이벤트 → `OnIdleTimeout` → 즉시 `ReturnHome`**: 경고·카운트다운 단계 없음. `IdleWatchdog.Start(sec)`/`Reset()`/`Stop()`, 1회 발생 후 정지(`IdleWatchdog.cs`). `AppShellViewModel.UpdateIdleWatch`가 `IsSessionActive`면 `_idle.Start(IdleTimeoutSeconds=75)`. (근거: `IdleWatchdog.cs`, `AppShellViewModel.cs:183-203`)
- **VF-3. 유휴 감시 대상은 `SessionStateMachine.IsSessionActive`**: FrameSelect~Qr(it4에서 FrameEditor 제외). Home·Settings·Login·Account는 비대상. `UpdateIdleWatch`가 상태 진입마다 Start/Stop. (근거: `SessionStateMachine.cs`, it4 §4)
- **VF-4. `SessionContext.Reset(clearUser)`**: clearUser=false면 촬영 데이터만 폐기·계정 보존, true면 추가로 `Logout()`(CurrentUserChanged 통지). (근거: `SessionContext.cs:65-80`)
- **VF-5. 프레임 저장은 전부 DB(Firestore+Storage)**: `FrameRepository.SaveAsync`가 userId 유무 무관 Firestore `frameTemplates` + Storage `frames/{owner}/{id}.png` 저장(`FrameRepository.cs:44-75`). **로컬 저장 경로 없음** — A2의 user 로컬 전용·파워 로컬 캐시가 신규. (근거: `FrameRepository.cs`)
- **VF-6. 프레임 로딩 우선순위 = DB isDefault → 번들 Frame/ → fallback**: `FrameCatalogService.GetDefaultFramesAsync`. user 프레임은 `GetUserFramesAsync`가 Firestore `WhereEqualTo("userId")`. 번들은 `Frame/{name}.png` + `.slots`(5필드 "index,x,y,w,h"). (근거: `FrameCatalogService.cs`)
- **VF-7. `.slots` 포맷은 "index,x,y,w,h"(5필드)**: 종횡비는 w/h에서 파생(`Slot.AspectRatio` 계산 프로퍼티, it4). A2가 "종횡비 저장" 명시하나 w/h로 이미 결정 — 명시 필드 추가는 선택. (근거: `FrameCatalogService.cs:113-124`, `Slot.cs`)
- **VF-8. 프레임 삭제는 `FrameRepository.DeleteAsync`(Firestore 문서 + Storage 이미지)**: 존재. **로컬 삭제·프레임 선택 화면 삭제 UI 없음** — A3 신규. (근거: `FrameRepository.cs:77-100`, `FrameSelectView`)
- **VF-9. 프레임 선택 VM은 삭제·소유자 구분 없음**: `FrameSelectViewModel`이 기본+user 프레임 로드만. 카드 X·확인 팝업·삭제 커맨드 없음. (근거: `FrameSelectViewModel.cs`)
- **VF-10. 필터 4종(원본/흑백/밝기/뷰티)은 ResultView에 하드코딩 버튼, 실처리 구현됨**: `ResultView.xaml:28-47`에 4버튼 `SetFilterCommand`. `Filters.cs`가 흑백(Grayscale)·밝기(ConvertScaleAbs)·뷰티(BilateralFilter+블렌드) **실제 OpenCV 처리 구현 완료**. → A6은 "실처리 구현"이 아니라 **설정 노출·개별 on/off + 켜진 필터만 노출**이 신규(원본은 항상·Disable). (근거: `ResultView.xaml`, `Filters.cs`)
- **VF-11. 카메라 Ready = 첫 프레임 1회 수신(it5)**: `CaptureViewModel.WaitForFirstFrameAsync`가 `FrameReady` 첫 1회로 Ready 판정(8초 타임아웃). → A7은 "첫 프레임 이후 **안정적 프리뷰**까지" 강화 — 첫 프레임만으로 부족(실가동 지연). (근거: `CaptureViewModel.cs:78-117`)
- **VF-12. QR 하위 토글(SendPhoto/SendTimelapse)은 it7에서 구현**: `AppSettings.SendPhoto/SendTimelapse`, `QrDeliveryPolicy.Normalize`(둘 다 off→QR off). → A5는 **off→on 재활성 시 둘 다 on 강제** 추가. (근거: it7 설계, `AppSettings`)
- **VF-13. 설정 저장/닫기 버튼은 스크롤 내부**: `SettingsView.xaml`이 `ScrollViewer` 안에 [저장]·[닫기](스크롤해야 보임). → A4 sticky 하단바. (근거: `SettingsView.xaml`)
- **VF-14. `frameTemplates`는 웹 접근 없음(WPF 전용, 계약 §5)**: 웹은 resultSessions만. → A2 user 로컬 전용화의 **계약 영향 최소**(user 문서가 Firestore에 안 생길 뿐, 웹 무관). (근거: `firebase-contract.md:180,198`)
- **VF-15. 기존 테스트**: `SettingsTests`·`AppStateTests`·`SlotLayoutTests`·`FrameEditorViewModelTests`·`CropCalculatorTests`·`QrDeliveryPolicyTests` 등. (근거: `tests/MCPhoto.Tests/`)

### 미검증 가정 (구현 시 검증 — WBS Step 매핑)

- **OA-1. 유휴 경고 팝업(2분→10초 카운트다운)을 오버레이로 넣고 clearUser:true를 false로 바꿔도 무인 안정성이 유지된다**(홈 복귀는 여전히 발생, 로그아웃만 제거) → **검증: WBS Step 1**(유휴 카운트다운 순수 로직 + 상태 테스트 + 육안).
- **OA-2. 로컬 프레임 저장/로딩(`<계정>_<이름>.png` + `.slots`)이 번들 규약과 공존하고 DB 폴백과 정합** → **검증: Step 2**(로컬 경로·명명·slots 라운드트립 단위 테스트).
- **OA-3. 파워 프레임 로컬 캐시(없을 때만 DB 다운로드)가 오프라인·캐시 히트에서 정확** → **검증: Step 3**(캐시 히트/미스 분기 테스트).
- **OA-4. 프레임 삭제(로컬 항상 + 파워 서버 옵션)가 소유자·권한별로 정확** → **검증: Step 4**(삭제 정책 단위 + 육안).
- **OA-5. 카메라 "안정적 프리뷰" 판정(연속 N프레임 or 최소 경과)이 실사용 가능 시점과 부합** → **검증: Step 7**(판정 로직 단위 + 육안).

---

## 1. 요구 → 설계 매핑 (한눈에)

| 요구 | 현황(VF) | 설계 조치 | WBS Step |
|---|---|---|---|
| **A1** 유휴 재설계(로그아웃 금지) | 유휴만 clearUser:true(VF-1), 즉시 홈(VF-2) | 유휴 경고 팝업(2분→10초 카운트다운, [이어서]/[메인]), clearUser:true→false, 자동 로그아웃 전면 제거 | §2, Step 1 |
| **A2** 프레임 저장 하이브리드 | 전부 DB(VF-5), 로컬 없음 | 파워=DB+로컬캐시, user=로컬전용(`<계정>_<이름>.png`+`.slots`), 로딩 로컬우선/DB폴백 | §3, Step 2·3 |
| **A3** 프레임 삭제 UI | 삭제 UI 없음(VF-8·9) | 선택창 카드 X(관리자만)→확인 팝업(파워=서버제거 체크)→로컬삭제(+옵션 DB) | §4, Step 4 |
| **A4** 설정 sticky | 버튼 스크롤 내부(VF-13) | ScrollViewer 밖 하단 고정 바(저장/닫기) | §5, Step 5 |
| **A5** QR off→on 세부 on | it7 하위토글(VF-12) | off→on 전환 시 SendPhoto/SendTimelapse 둘 다 on 강제 | §6, Step 5 |
| **A6** 필터 설정화+실처리 | 실처리 완료·설정 노출 없음(VF-10) | 설정에서 필터 개별 on/off(원본 Disable), 켜진 필터만 결과 노출 | §7, Step 6 |
| **A7** 카메라 Ready 강화 | 첫 프레임 1회(VF-11) | 안정적 프리뷰(연속 프레임/최소 경과)까지 waiting | §8, Step 7 |

---

## 2. A1 — 유휴 타임아웃 재설계 (로그아웃 절대 금지) [최우선]

### 2.1 현황·목표

현재 유휴는 2분(설정 75초)이 아니라 `IdleTimeoutSeconds=75`이고, 만료 시 **경고 없이 즉시 홈 복귀 + 로그아웃**(`OnIdleTimeout`→`ReturnHome(clearUser:true)`, VF-1·2). 목표:
- **2분(120초) 무동작** → 경고 팝업 "**XX초 후 메인 화면으로 돌아갑니다**" + **10초 카운트다운**.
- 팝업 [**이어서 진행하기**](취소·타이머 리셋·현재 화면 유지) / [**메인 화면으로**](즉시 홈).
- 카운트다운 0 → **홈 복귀, 로그아웃 절대 금지**.
- **로그아웃은 로그아웃 버튼 전용** — 유휴·세션완료·화면이동 모두 자동 로그아웃 금지.

### 2.2 설계 — 2단계 유휴(경고 → 카운트다운 → 홈)

- **타이밍 상수**: `IdleWarningSeconds = 120`(2분 무동작 후 경고), `IdleCountdownSeconds = 10`(경고 팝업 카운트다운). `IdleTimeoutSeconds`(기존 75) → `IdleWarningSeconds=120`으로 대체.
- **`IdleWatchdog` 확장 or 2단 사용**:
  - **방식 A(권장)**: `IdleWatchdog`을 2단계로 — `Start(warningSec)` 후 무동작 만료 시 `IdleWarning` 이벤트(홈 복귀 아님). 셸이 경고 팝업 표시 + **별도 카운트다운 타이머**(10초, `DispatcherTimer`) 구동. 카운트다운 중 사용자 활동(또는 [이어서])이면 리셋(경고 해제·warning 타이머 재시작). 0 도달 or [메인]이면 홈 복귀(clearUser:false).
  - `IdleWatchdog`에 이벤트 하나 더 추가하거나, 기존 `IdleTimeout`을 "경고 트리거"로 의미 변경 + 카운트다운은 셸/전용 VM이 담당.
  - **채택**: `IdleWatchdog`은 "경고 트리거"만(단순 유지), **카운트다운은 셸 레벨 `IdleWarningViewModel`/오버레이가 담당**(테스트 가능한 순수 카운트다운 로직 분리).
- **경고 팝업(오버레이)**: 상태머신 전이가 아니라 **모달 오버레이**(MainWindow에 Popup/오버레이 레이어, 설정 팝오버처럼). `AppShellViewModel.IsIdleWarningVisible` + `IdleCountdownRemaining`(10→0). 팝업 위 "메인 화면으로 돌아갑니다 · {N}초" + 2버튼.
  - 오버레이라 현재 화면(프레임 선택 등)을 유지한 채 위에 뜸 → [이어서] 시 그 화면 그대로.
- **활동 리셋**: `NotifyUserActivity`(기존 MainWindow PreviewMouseDown/KeyDown)가 (a)경고 전이면 warning 타이머 리셋, (b)경고 팝업 표시 중이면 팝업 유지(활동해도 자동 해제하지 않음 — 사용자가 [이어서]를 명시적으로 눌러야 함, 또는 활동 시 자동 [이어서]로 해석 — **설계: 팝업 표시 중 활동은 [이어서]와 동일 처리**(자연스러움), 단 요구가 버튼 2개 명시하므로 **버튼 우선, 활동은 카운트다운만 멈추지 않음**. 최종: **팝업 뜨면 버튼으로만 해제**(활동 무시) — 명확·요구 충실. 활동 리셋은 경고 팝업 전 단계에서만).

### 2.3 로그아웃 경로 전면 제거

- `AppShellViewModel.cs:203`: `ReturnHome("유휴 타임아웃", clearUser: true)` → **`clearUser: false`**. (유휴로 홈 복귀하되 로그인 유지.)
- 전수 확인(VF-1): Done·Home은 이미 false. **`clearUser:true` 호출이 코드에서 0이 되도록**(유휴 수정 후). `ReturnHome`의 `clearUser` 파라미터 자체는 남기되(로그아웃 버튼이 `Logout()`→`_session.Logout()` 별도 경로라 무관), **모든 자동 경로는 false**.
- `Logout()` 커맨드만 `_session.Logout()`(명시적). 로그아웃 버튼 전용.
- 문서화: it3/it5의 "다음 손님 위해 유휴 로그아웃" 정책을 **it8에서 폐기**(로그아웃=수동 only). PRD 갱신 반영(요구 명시).

### 2.4 상태머신·유휴 감시 영향

- `IsSessionActive`(유휴 감시 대상)는 유지(FrameSelect~Qr). 단 **경고는 어느 화면에서든**? 요구는 "프레임 선택 등 어느 화면이든 2분 무동작". → 유휴 감시를 세션 화면뿐 아니라 **로그인 후 정적 화면(FrameSelect·FrameEditor 등)에도** 걸어야 할 수 있음. 현재 FrameEditor는 it4에서 유휴 제외(편집 중 로그아웃 방지). **A1로 로그아웃이 사라지므로 FrameEditor도 유휴 감시 복원 가능**(홈 복귀만, 로그인 유지) — 단 편집 중 홈 복귀는 작업 손실이라 **FrameEditor는 여전히 제외**(또는 경고만). 설계: **유휴 경고는 IsSessionActive 화면 + FrameSelect에 적용, FrameEditor는 제외 유지**(편집 손실 방지). 미검증 가정(OA-1)로 두고 육안 조정.
- 홈·설정·로그인·계정은 유휴 비대상(기존).

### 2.5 검증 (headless)

- 단위: `IdleCountdown`(10→0) 순수 로직 — Tick마다 감소, 0에서 완료 이벤트. [이어서] 시 리셋. `IsSessionActive`·경고 대상 상태 테스트. `ReturnHome`이 유휴 경로에서 clearUser:false(grep + 단위).
- **grep**: 코드에 `clearUser: true` 호출 0(유휴 수정 후).
- 사용자 확인(육안): 2분 무동작→경고 팝업·10초 카운트다운→[이어서] 유지/[메인] 홈/0초 홈, **모든 경우 로그인 유지**.

---

## 3. A2 — 프레임 저장 하이브리드 (파워=DB+로컬캐시 / user=로컬전용)

> **정정(2026-07-22, 사용자 확정)**: (1) 로컬 저장 폴더 = **앱 실행 폴더 `AppContext.BaseDirectory\Frame`**(%ProgramData% 아님) — 실행 폴더는 항상 쓰기 가능 전제, 파워 캐시·user 프레임 모두 이 폴더. **번들 기본 프레임과 동일 폴더**(`FrameCatalogService.BundleFolder`)라 한 폴더에 번들+파워캐시+user가 공존 → §3.1.1 구분 규칙으로 정리. (2) 파일명 **sanitize 제거**(`_`→`-` 치환 없음) — 이름 원문 그대로. 파워 유저(본인)가 명명 관리하므로 접두 충돌 예외 불요, 접두 파싱 모호성은 수용(사용자 확정). (3) 파워 프레임 게스트 노출 유지.

### 3.1 저장 구조 다이어그램

```
로컬 폴더 = AppContext.BaseDirectory\Frame  (실행 폴더, 쓰기 가능 전제)
  = FrameCatalogService.BundleFolder 와 동일 → 번들 + 파워캐시 + user 프레임 공존

프레임 생성(FrameEditor 저장)
 ├─ 파워(admin/manager): isDefault=true (공용)
 │    ├─ DB: Firestore frameTemplates(userId=null, isDefault=true) + Storage frames/default/{id}.png  (기존 FrameRepository)
 │    └─ 로컬 캐시: Frame/{프레임이름}.png + Frame/{프레임이름}.slots  (접두 없음 = 공용, 저장 시 함께 기록)
 └─ user(일반): 로컬 전용 (본인만)
      └─ 로컬: Frame/{계정명}_{프레임이름}.png + Frame/{계정명}_{프레임이름}.slots  (접두 있음 = user 전용, DB 미업로드)

프레임 로딩(FrameSelect)
 ├─ 공용 프레임(게스트 포함 노출): Frame/ 의 접두 없는 파일(번들 + 파워 캐시)
 │    ① 로컬에 있으면 사용(캐시 히트, 다운로드 안 함)
 │    ② 없으면 DB isDefault 조회 → Frame/{이름}.png + .slots 로 1회 다운로드(캐시) → 사용
 │    ③ DB도 없으면 fallback (기존)
 └─ user 프레임: Frame/{자기계정명}_*.png (본인 접두만, 타 계정 미노출)
```

### 3.1.1 폴더 공존 구분 규칙 (번들/파워캐시 vs user)

한 폴더(`Frame\`)에 세 종류가 공존하므로 **접두 규칙**으로 구분한다:

| 종류 | 파일명 규칙 | 노출 대상 | 삭제 대상 |
|---|---|---|---|
| 번들 기본 프레임 | 접두 없음 `{이름}.png`(+`.slots`) | 게스트 포함 공용 | 삭제 불가(설치 자산) |
| 파워 캐시(DB isDefault) | 접두 없음 `{이름}.png`(+`.slots`) | 게스트 포함 공용 | 파워 삭제 가능(로컬+옵션 DB) |
| user 프레임 | **접두 `{계정명}_`** → `{계정명}_{이름}.png`(+`.slots`) | 해당 user 로그인 시만 | 해당 user 삭제 가능(로컬) |

- **판별**: 파일명에 **`_`가 있고 그 앞부분이 로그인 계정명과 일치**하면 user 전용, 아니면 공용. 로딩 시:
  - 공용 목록 = `Frame\`의 파일 중 **`{로그인계정명}_` 접두로 시작하지 않는** 전부(번들+파워캐시). 게스트도 이 목록.
  - user 목록 = 로그인 계정명 `{name}` 있을 때 **`{name}_` 접두 파일만**.
- **번들 vs 파워캐시 구분은 불필요**(둘 다 공용·접두 없음). 파워캐시는 `IsDefault=true`로 표시(삭제 시 DB 옵션 노출 판단에 사용). 번들은 삭제 불가 표시(설치 경로 자산 or 별도 마킹).
- **중복 집계 방지**: `FrameCatalogService`가 DB isDefault를 조회해 로컬에 없는 것만 캐시하되, **로딩 목록은 로컬 `Frame\` 스캔 1회로 통합**(DB 목록과 로컬 파일을 이름 기준 병합 — 이미 로컬에 있으면 DB 항목 스킵). 같은 이름이 번들·캐시로 중복되지 않게 파일명(이름) 유일성 기준 dedup.
- **접두 모호성(수용)**: 계정명이나 프레임 이름에 `_`가 포함되면 파싱이 모호할 수 있으나(예: `a_b_c.png`가 계정 `a`의 `b_c`인지 `a_b`의 `c`인지), **사용자 확정으로 수용** — "첫 `_` 앞 = 계정명" 규칙으로 단순 파싱(로그인 계정명과 정확히 일치할 때만 user 전용, 아니면 공용 취급). 파워 유저가 명명을 관리하는 전제.

### 3.2 로컬 저장 규약

- **폴더**: **`AppContext.BaseDirectory\Frame`**(실행 폴더, 쓰기 가능 전제). `FrameCatalogService.BundleFolder`와 **동일** → 저장·로딩·번들이 한 폴더. (%ProgramData% 폴더는 A2에서 사용 안 함. fallback 캐시(`App.DataFolder\cache`)는 별개 유지.)
- **명명**(sanitize 없음, 원문 그대로):
  - user 프레임: `{계정명}_{프레임이름}.png` + `{계정명}_{프레임이름}.slots`. 접두 = user 전용 표시.
  - 파워 캐시/생성: `{프레임이름}.png` + `{프레임이름}.slots`(접두 없음 = 공용). (기존 설계의 frameId 기반 → **이름 기반**으로 변경: 사용자가 이름으로 관리·번들과 일관.) DB 문서 id는 FrameTemplate.Id로 별도 유지(삭제 시 DB 매칭용).
  - **파일명 안전화(sanitize) 제거**: `_`→`-` 치환·특수문자 변환 없이 이름 원문 사용. 단 파일 시스템 금지문자(`\ / : * ? " < > |`)는 OS가 거부하므로 최소 방어(금지문자 포함 이름은 저장 거부·안내) — 이는 sanitize가 아니라 유효성 검사(사용자 확정 "원문 그대로"와 무충돌).
- **`.slots` 포맷**(기존 "index,x,y,w,h" 유지 + 메타): 파일 첫 줄 `#imagesize=W,H`(프레임 원본 크기, 슬롯 좌표 기준) + 이후 "index,x,y,w,h" 줄. 종횡비는 슬롯 w/h 파생(별도 필드 불요). 하위호환: `#` 주석·5필드 파싱(기존 번들 `.slots`도 읽힘).
- **소유자 구분 로딩**: §3.1.1 접두 규칙. user는 `{자기계정명}_` 접두만, 공용은 접두 없는 전부.

### 3.3 저장/로딩 파이프라인

- **`ILocalFrameStore`(신규, Core/App)**: 루트 = `AppContext.BaseDirectory\Frame`. `SaveLocal(FrameTemplate, imageBytes, ownerName?)` → ownerName 있으면 `{owner}_{이름}`(user 전용), 없으면 `{이름}`(공용/파워) png+slots 기록(§3.1.1). `LoadPublic()` → 접두 없는 파일(번들+파워캐시) 목록. `LoadUser(ownerName)` → `{owner}_` 접두 파일만. `DeleteLocal(frame)` → png+slots 삭제(A3). `CacheFromDb(FrameTemplate)` → DB 프레임 이미지 다운로드 후 `{이름}.png`+`.slots`(공용, 접두 없음) 기록.
- **`FrameEditorViewModel.Save` 분기**: 로그인 역할로 분기 —
  - 파워: `FrameRepository.SaveAsync`(DB, isDefault=true) + `ILocalFrameStore.SaveLocal(ownerName: null)`(공용 캐시, 접두 없음).
  - user: `ILocalFrameStore.SaveLocal(ownerName: 계정id)`만(DB 미호출, `{계정}_{이름}` 접두).
- **`FrameCatalogService` 로딩 개편**: 공용 프레임 = `ILocalFrameStore.LoadPublic()`(접두 없는 로컬=번들+파워캐시) 우선 → DB isDefault 중 **로컬에 이름이 없는 것만** `CacheFromDb`로 캐시 후 병합(이름 기준 dedup, 중복 집계 방지 §3.1.1) → 없으면 fallback. user 프레임 = `ILocalFrameStore.LoadUser(userId)`(접두 파일). DB `GetUserFramesAsync`는 **미사용**(user는 로컬 전용, 파워 isDefault만 DB).

### 3.4 계약 영향 (firebase-contract)

- **user 프레임이 Firestore `frameTemplates`에 더는 생성되지 않는다**(로컬 전용). 파워 프레임만 `isDefault=true, userId=null`로 DB. `frameTemplates`는 웹 접근 없음(VF-14)이라 **웹·보안 규칙 영향 없음**.
- 계약 §2.2에 "user 커스텀 프레임은 WPF 로컬 전용(DB 미저장), DB `frameTemplates`는 공용 기본 프레임(isDefault=true, userId=null)만" 명문화. 계정당 10개 제한은 로컬 파일 수로(파워 DB는 별개).
- Storage `frames/{userId}/`는 파워 공용이면 `frames/default/`만 사용(userId=null). 기존 경로 규약 유지.

### 3.5 검증 (headless)

- 단위(`LocalFrameStoreTests`): 루트가 `AppContext.BaseDirectory\Frame`. `SaveLocal`(ownerName=계정) → `{계정}_{이름}.png`+`.slots`(접두), `SaveLocal`(ownerName=null) → `{이름}.png`(접두 없음). slots 라운드트립(개수·좌표·크기·imagesize 메타). `LoadPublic()` → 접두 없는 파일만, `LoadUser(계정)` → `{계정}_` 접두만. 이름 원문 그대로(sanitize 없음, `_` 치환 없음). 첫 `_` 앞=계정 파싱. 금지문자 이름 저장 거부.
- 단위(`FrameCatalogService`): 로컬 공용 프레임 존재 시 DB 미조회(목 repo 호출 0), 미스 시 다운로드+캐시. 이름 기준 dedup(번들·캐시 중복 집계 없음).
- 사용자 확인(육안): 파워 생성→DB+로컬(접두 없음, 공용 노출), user 생성→로컬만(`계정_이름`, 본인만), 재시작 후 로딩(로컬 우선, 번들과 공존).

---

## 4. A3 — 프레임 삭제 UI (선택창)

### 4.1 설계

- **프레임 선택 화면 카드**: 로그인 상태가 **관리자(user 이상 = 로그인 계정 전체?)** — 요구는 "관리자(일반·파워 모두)". MC포토에서 user=일반 관리자(자기 프레임 관리), manager/admin=파워. **로그인 사용자면 카드 우상단 X 노출**(게스트·비로그인 미노출).
  - 단, 삭제 대상: 본인 소유(user 로컬) 프레임 + (파워면) 공용 프레임. user는 자기 프레임만 X. 기본/번들 fallback 프레임은 삭제 불가(X 미노출 or 비활성).
- **X 클릭 → 확인 팝업** "삭제하시겠습니까?": 
  - **파워 로그인 시** 팝업에 **"서버에서도 제거" 체크박스**(기본 OFF).
  - [확인] → 항상 **로컬 삭제**(`ILocalFrameStore.DeleteLocal`: png+slots). "서버에서도 제거" 체크 시(파워만) **`FrameRepository.DeleteAsync`**(Firestore+Storage).
  - [취소] → 아무것도 안 함.
- **권한 게이트**: 게스트/비로그인 X 미노출. user는 서버 제거 체크박스 미노출(로컬만). 파워만 서버 제거 옵션.

### 4.2 VM/View

- `FrameSelectViewModel`: `bool CanDeleteFrames`(로그인 여부), `bool IsPower`. `DeleteFrameCommand(FrameTemplate)` → 확인 팝업 표시(오버레이 or 다이얼로그 VM). 확인 결과(+서버 제거 여부)로 로컬/DB 삭제 후 목록 갱신(`OnEnterAsync` 재로드 or 컬렉션에서 제거).
- 카드 X 버튼: `FrameSelectView` DataTemplate 우상단, `Visibility={Binding DataContext.CanDeleteFrames}`(부모 바인딩). 삭제 확인 팝업(오버레이).
- 삭제 대상 판별: 프레임이 로컬(user prefix 본인) or 공용(파워). 번들/fallback은 삭제 불가.

### 4.3 검증 (headless)

- 단위: 삭제 정책 — user는 로컬만·서버옵션 없음, 파워는 로컬+서버옵션. 게스트 CanDeleteFrames=false. `ILocalFrameStore.DeleteLocal`이 png+slots 삭제.
- 사용자 확인(육안): 로그인 시 X 노출·게스트 미노출, 확인 팝업(파워=서버체크), 삭제 후 목록 갱신.

---

## 5. A4·A5 — 설정 sticky 하단바 + QR off→on 세부 자동 on

### 5.1 A4 — sticky 하단바

- `SettingsView.xaml`: 현재 [저장]·[닫기]가 `ScrollViewer` 안(VF-13). → **`ScrollViewer` 밖 하단 고정 바**(DockPanel/Grid로 스크롤 영역과 분리). 레이아웃:
  ```
  Grid(설정 페이지)
   ├─ ScrollViewer (설정 항목들, 스크롤)          — Row *
   └─ Border(하단 고정 바) [저장][닫기]+저장 안내  — Row Auto, VerticalAlignment=Bottom
  ```
- 하단 바는 배경 `Brush.Bg` + 상단 구분선/그림자(스크롤 콘텐츠와 시각 분리). 저장 성공/실패 토스트(it3 BoolToNoticeBrush)도 하단 바에.
- 라이트 토큰·U7(it5) PC 밀도 유지. 터치 최소 히트.

### 5.2 A5 — QR off→on 세부 자동 on

- it7 `QrDeliveryPolicy`(둘 다 off→QR off) 위에, **QR off→on 전환 시 SendPhoto/SendTimelapse 둘 다 on 강제**.
- `SettingsViewModel`: `EnableQrDelivery` 변경 감지 — **false→true 전환이면 `SendPhoto=true; SendTimelapse=true`**(무조건). true→false는 하위 숨김(값 보존 or 무관). 개별 조절은 QR on 상태에서 가능(it7).
- 순수 로직 확장: `QrDeliveryPolicy.OnEnableChanged(wasOn, nowOn, ...)` 또는 VM의 `OnEnableQrDeliveryChanged`에서 처리. 테스트로 off→on 시 둘 다 on 고정.

### 5.3 검증 (headless)

- 단위: `QrDeliveryPolicy` off→on → SendPhoto·SendTimelapse 둘 다 true. `SettingsTests`로 전환 반영.
- 사용자 확인(육안): 저장/닫기 항상 보임(스크롤 무관), QR 껐다 켜면 사진·타임랩스 둘 다 켜짐.

---

## 6. A6 — 이미지 필터 설정화 + 실처리

### 6.1 현황

`Filters.cs`가 흑백·밝기·뷰티 **실처리 이미 구현**(OpenCV, VF-10). ResultView에 4버튼 하드코딩. A6 신규 = **설정에서 필터 개별 on/off + 켜진 필터만 결과 노출 + 원본 항상(Disable)**.

### 6.2 설계

- **설정 필드**: `AppSettings.FilterGrayscale`/`FilterBrightness`/`FilterBeauty`(bool, 기본 전부 true). 원본(None)은 필드 없음(항상 제공). INI 영속.
- **설정 UI**: SettingsView에 "필터" 그룹 — 흑백/밝기/뷰티 토글(기본 on). **원본은 토글 표시하되 Disable(체크 고정·비활성)** 또는 "원본(항상 제공)" 안내 텍스트. 요구: "원본은 토글 불가(Disable)".
- **결과 화면 노출**: `ResultViewModel`이 설정에서 켜진 필터만 노출. 현재 하드코딩 4버튼 → **VM이 `AvailableFilters`(항상 None + 켜진 것) 리스트 제공**, ResultView가 `ItemsControl`로 바인딩(하드코딩 버튼 → 동적). 켜진 필터만 버튼 생성.
- **실처리 검증**: `Filters.Apply`가 각 FilterKind에 대해 원본과 다른 결과를 내는지 단위 테스트(흑백=채널 동일값, 밝기=평균 밝기 증가, 뷰티=스무딩). 이미 구현됐으나 A6가 "검증 필요" 명시하니 테스트 추가.

### 6.3 검증 (headless)

- 단위(`FiltersTests` 신규): `Apply(src, Grayscale)` → R=G=B(그레이), `Brightness` → 평균 픽셀값 증가, `Beauty` → 분산 감소(스무딩). `None` → 원본 동일.
- 단위(`ResultViewModel`): 설정 필터 on/off → `AvailableFilters`에 켜진 것만(+항상 None). 전부 off여도 None은 존재.
- `SettingsTests`: 필터 3토글 INI 라운드트립.
- 사용자 확인(육안): 설정에서 필터 끄면 결과 화면에 그 버튼 없음, 원본은 항상·토글 불가, 각 필터 실제 적용.

---

## 7. A7 — 카메라 Ready 강화 (실사용 가능 시점까지 waiting)

### 7.1 현황·목표

it5는 **첫 프레임 1회 수신**으로 Ready 판정(VF-11). 그러나 첫 프레임 후에도 카메라 실가동(안정적 프리뷰)까지 지연이 있어, waiting UI가 사라진 뒤 화면이 비어 보임. 목표: **안정적 프리뷰(실사용 가능) 시점까지 waiting 유지**.

### 7.2 설계 — "안정적 프리뷰" 판정 강화

- **판정 기준**(택1·조합):
  - **연속 N프레임 수신**(예: 5~10프레임 연속) — 첫 프레임 후 스트림이 실제로 흐르는지.
  - **최소 경과 시간**(예: 첫 프레임 후 300~500ms 추가 대기) — 초기 노출·화이트밸런스 안정화.
  - **프레임레이트 안정**(`ICameraService.CurrentFps`가 임계 이상, 예: 10fps+) — 진단 fps 활용(존재).
  - **채택: 연속 N프레임(예 8) 수신 OR 첫 프레임 후 500ms 경과 중 늦은 쪽**, 그리고 `CurrentFps > 0` 확인. 과대 지연 방지 위해 전체 타임아웃(기존 8초) 유지.
- `CaptureViewModel.WaitForFirstFrameAsync` → `WaitForStablePreviewAsync`: 첫 프레임 후 **N프레임 카운트 + 최소 경과**를 만족할 때 Ready. 미달·타임아웃이면 Failed(무한 로딩 방지).
- `CameraLoadState`(Initializing/Ready/Failed) 유지, Ready 조건만 강화. 로딩 오버레이(it5)는 Ready까지 표시 — 자연히 실사용 시점까지 유지됨.
- **판정 로직 순수화**: `PreviewReadiness`(프레임 카운트·경과 누적 → Ready 판정) 순수 클래스로 분리해 단위 테스트(프레임 이벤트·시간 모킹).

### 7.3 검증 (headless)

- 단위(`PreviewReadinessTests`): N프레임 미만·최소경과 미달 → not ready, 둘 다 충족 → ready, 타임아웃 → failed. fps 0이면 not ready.
- 사용자 확인(육안): 촬영 진입 시 실제 프리뷰가 안정적으로 나올 때까지 waiting, 사라진 직후 바로 영상.

---

## 8. 파일 변경 요약

| 파일 | 변경 | 요구 |
|---|---|---|
| `src/MCPhoto.App/AppShellViewModel.cs` | 유휴 경고(2단계)·카운트다운·`clearUser:true→false`(203행), 경고 오버레이 상태 | A1 |
| `src/MCPhoto.Core/Navigation/IdleWatchdog.cs`(+`IIdleWatchdog`) | 경고 트리거 의미(단순 유지 or 2단) | A1 |
| `src/MCPhoto.App/`(신규 유휴 경고 오버레이 XAML/VM 로직) | 팝업 UI + 10초 카운트다운(순수 로직 분리) | A1 |
| `src/MCPhoto.App/MainWindow.xaml` | 유휴 경고 오버레이 레이어 | A1 |
| `src/MCPhoto.Core/Frames/`(신규 `ILocalFrameStore`/`LocalFrameStore`) | 로컬 png+slots 저장/로딩/삭제/캐시 | A2·A3 |
| `src/MCPhoto.App/Services/FrameCatalogService.cs` | 로컬 우선 로딩·파워 캐시·user 로컬 | A2 |
| `src/MCPhoto.App/ViewModels/FrameEditorViewModel.cs` | 역할별 저장 분기(파워=DB+캐시, user=로컬) | A2 |
| `src/MCPhoto.App/ViewModels/FrameSelectViewModel.cs`·`Views/FrameSelectView.xaml` | 카드 X·확인 팝업·삭제(권한별) | A3 |
| `src/MCPhoto.App/Views/SettingsView.xaml` | sticky 하단바(ScrollViewer 밖) | A4 |
| `src/MCPhoto.App/ViewModels/SettingsViewModel.cs` | QR off→on 세부 자동 on, 필터 토글 | A5·A6 |
| `src/MCPhoto.Core/Settings/AppSettings.cs`·`IniSettingsService.cs` | FilterGrayscale/Brightness/Beauty INI | A6 |
| `src/MCPhoto.App/ViewModels/ResultViewModel.cs`·`Views/ResultView.xaml` | 켜진 필터만 동적 노출(AvailableFilters) | A6 |
| `src/MCPhoto.App/ViewModels/CaptureViewModel.cs`(+신규 `PreviewReadiness`) | 안정적 프리뷰 Ready 판정 | A7 |
| `src/MCPhoto.Core/`(신규 `QrDeliveryPolicy` 확장) | off→on 세부 자동 on | A5 |
| `docs/design/firebase-contract.md` | user 프레임 로컬 전용·frameTemplates 공용만 명문화 | A2 |
| `tests/MCPhoto.Tests/` | `IdleCountdownTests`·`LocalFrameStoreTests`·`FiltersTests`·`PreviewReadinessTests`·`QrDeliveryPolicyTests`(off→on)·`SettingsTests`(필터) | A1·A2·A6·A7·A5 |

---

## 9. 리스크 & 의존

| # | 리스크 | 영향 | 완화 | 검증 |
|---|---|---|---|---|
| R1 | 유휴 경고 오버레이가 촬영 등 몰입 화면과 충돌 | 오조작 | 유휴 감시 대상(IsSessionActive)에만·촬영 중 경고는 카운트다운으로 촬영 취소 고지. FrameEditor 제외 유지 | Step 1 |
| R2 | clearUser 전면 false로 무인 키오스크에 이전 손님 계정 잔존 | 계정 오용 | 요구 확정(로그아웃=수동 only). 무인 우려는 사용자 승인 사항. 홈 복귀는 유지 | Step 1(정책) |
| R3 | 실행 폴더 `AppContext.BaseDirectory\Frame` 쓰기 권한(Program Files 설치 시 제한 가능) | 저장 실패 | 실행 폴더 쓰기 가능은 사용자 확정 전제. 저장 실패 시 오류 안내(크래시 금지). Program Files 설치 시 권한 이슈는 배포 시 검토(범위 밖) | Step 2 |
| R4 | 파워 캐시 무효화(DB 프레임 갱신 시 로컬 stale) | 옛 프레임 사용 | id 기반 캐시(id 같으면 동일), 갱신은 새 id or 명시 갱신(범위 밖 — 미검증 가정) | Step 3 |
| R5 | user 로컬 전용화로 기기 이전 시 프레임 유실 | 데이터 이동성 | 로컬 전용은 요구 확정(user=로컬). 백업은 범위 밖 | Step 2 |
| R6 | 필터 동적 노출이 ResultView 하드코딩 리팩터 회귀 | 필터 미표시 | ItemsControl 바인딩 + None 항상. FiltersTests·ResultVM 테스트 | Step 6 |
| R7 | 안정적 프리뷰 판정이 과대 지연(느린 카메라) | waiting 길어짐 | 전체 타임아웃(8초) 유지, N프레임+최소경과 균형 | Step 7 |
| R8 | A2 계약 변경(user DB 미저장)이 계정 삭제 cascade와 상호작용 | 고아 데이터 | user 프레임이 DB에 없으니 cascade는 파워/기존 문서만. 로컬은 계정별 파일 | Step 2·계약 |

**의존**: Step 1(A1) 최우선·독립. Step 2(A2 로컬 저장)→Step 3(A2 캐시 로딩)→Step 4(A3 삭제, 로컬 store 재사용). Step 5(A4·A5 설정)·Step 6(A6 필터)·Step 7(A7 카메라)는 독립. Step 2·3·4가 `ILocalFrameStore` 공유(Step 2에서 정의).

---

## 10. 사용자 확인 필요 목록 (UI 육안 — headless 불가)

> WBS 완료 기준은 전부 headless(build/test/grep). 아래는 사용자 육안(각 Step trigger/non-goal로 분리).

1. **A1**: 2분 무동작→"XX초 후 메인 복귀" 팝업·10초 카운트다운→[이어서] 유지·[메인] 홈·0초 홈. **모든 경우 로그인 유지**(로그아웃 버튼만 로그아웃).
2. **A2**: 파워 프레임 생성→DB+로컬, 재사용 시 로컬 캐시(다운로드 안 함). user 프레임 생성→로컬만(`계정_이름.png`), 재시작 후 로딩.
3. **A3**: 로그인 시 카드 X 노출(게스트 미노출), 확인 팝업(파워=서버제거 체크), 로컬/(옵션)DB 삭제·목록 갱신.
4. **A4**: 설정 저장/닫기 버튼이 스크롤 무관 항상 하단 노출.
5. **A5**: QR 껐다 켜면 사진·타임랩스 둘 다 자동 on.
6. **A6**: 설정에서 필터 개별 on/off(원본 Disable), 켜진 필터만 결과 화면 노출, 흑백/밝기/뷰티 실제 적용.
7. **A7**: 촬영 진입 시 실제 프리뷰가 안정적으로 나올 때까지 waiting, 사라진 직후 영상 정상.

## 부록. 참고

- it3/it5 유휴·clearUser 정책(폐기 대상): `wpf-it3-design.md` §2, `wpf-it5-design.md` §4
- it4 편집기 좌표·유휴 제외: `wpf-it4-design.md`
- it5 카메라 CameraLoadState: `wpf-it5-design.md` §7
- it7 QrDeliveryPolicy·프레임 슬롯: `wpf-it7-design.md`
- 프레임 계약: `firebase-contract.md` §2.2·§5, `FrameRepository.cs`, `FrameCatalogService.cs`
