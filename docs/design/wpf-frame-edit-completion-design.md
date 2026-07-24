# item2 설계: 프레임 편집 완성도 — 역할별 편집 권한 + 기본프레임 DB 업데이트 팝업/diff

> 파이프라인: **wpf-architect(본 문서) → wpf-developer → wpf-code-reviewer**
> 코드/배포 금지. 본 문서는 설계 명세만 담는다.
> 루트: `E:\Study\photobooth`

---

## 0. 요약(1분 브리핑)

기존 프레임 편집기는 이미 **LoadForEdit / 편집 진입 게이트(CanEdit) / 역할별 저장 분기(user 로컬, power DB+캐시)**를 갖추고 있다(실측). 이 이터레이션의 실제 작업은 세 가지다.

1. **권한 규칙 엄격화(요구 2)**: user는 **본인이 로컬에서 만든 프레임만** 편집. DB 자동 다운로드된 기본 프레임은 진입·버튼 차단. 현재 `CanEdit`은 `local:` 접두면 무조건 허용해 "본인 것"을 소유(UserId) 기준으로 검증하지 않는다 → 순수 판정 함수로 분리·강화.
2. **manager 기본프레임 편집 저장 플로우 신설(요구 3)**: 저장 대상이 **DB 기본 프레임**이면 "로컬만 / DB도 업데이트" **확인 팝업**을 띄운다. DB 선택 시 **이미지·슬롯 diff**를 순수 함수로 판정해 변경이 있을 때만 `IFrameRepository`로 **같은 frameId 업데이트**를 수행하고 로컬 캐시를 갱신한다. 변경이 없으면 DB 미호출(로컬만/no-op).
3. **편집기 완성도 마감(요구 4)**: 편집 상태 리셋, 원본 스냅샷 보관, 저장 실패 시 화면 유지, 이미지 부재 편집 진입 처리 등 엣지 정리.

핵심 리스크 하나: **HTTP 백엔드 계약이 "같은 id 업데이트"를 지원하지 않는다**(POST /frames가 항상 `randomUUID()`로 신규 문서 생성, 요청에 id 필드 없음). 레거시(Admin) 경로는 `SetAsync(frame.Id)`로 정상 업데이트된다. → 본 설계는 **레거시에서 즉시 성립**하고, HTTP 경로는 계약 확장(서버 변경)이 필요하며 이를 `[CODE]`(서버) 항목으로 분리·표시한다. 확장 전까지 HTTP 모드에서는 "DB 업데이트"가 안전하게 **차단/경고**되도록 설계한다(무회귀·중복문서 방지).

---

## 1. 검증된 사실 / 미검증 가정

### 1.1 검증된 사실 (verified facts)

- **편집 진입 경로 존재**: `FrameSelectViewModel.EditFrame()`(`FrameSelectViewModel.cs:207-211`) → `AppShellViewModel.OpenFrameEditor(SelectedFrame)`(`AppShellViewModel.cs:226-230`) → `CreateFrameEditorViewModel()`가 `vm.LoadForEdit(f)` 호출(`AppShellViewModel.cs:214-223`). "선택 편집" 버튼은 `CanEditSelected`로 노출(`FrameSelectView.xaml:141-143`).
- **편집 게이트 현황**: `FrameSelectViewModel.CanEdit(f)`(`FrameSelectViewModel.cs:214-221`)는 게스트/빈 Id/bundle:/fallback 제외, `local:` 접두면 **무조건 true**, 그 외(접두 없는 DB id)는 `IsPower`. → **UserId 소유 검증 없음**(요구 2 미충족 지점).
- **편집 로드**: `FrameEditorViewModel.LoadForEdit(frame)`(`FrameEditorViewModel.cs:123-160`)가 `_isEditing=true`, `_editingFrameId=frame.Id` 설정, 이미지(`frame.ImageUrl` 로컬 파일)·슬롯(`_baseSlots`)·이름 로드. 이미지 파일 부재 시 `StatusMessage`만 세팅하고 **return하지만 `_isEditing`은 이미 true**(저장 시 이미지 null → CanSave=false로 막힘).
- **저장 분기 현황**: `FrameEditorViewModel.Save()`(`FrameEditorViewModel.cs:244-302`).
  - `isPower`이면 **팝업 없이** `FrameTemplate { Id = EditingServerId() ?? "", UserId=null, IsDefault=true }`로 `_repository.SaveAsync` **항상 호출** + `_localStore.SaveLocal(saved, bytes, ownerName:null)`.
  - user이면 `_localStore.SaveLocal(frame, bytes, ownerName:user.Id)` (DB 미호출).
- **EditingServerId()**(`FrameEditorViewModel.cs:235-242`): `_isEditing && _editingFrameId`가 local:/bundle:/fallback 접두가 아니면 그 id 반환(→DB 업데이트 의도), 아니면 null(→신규 생성). 즉 **실 DB id를 가진 프레임 편집 = 같은 id 저장 의도**가 이미 코드에 있음.
- **레거시 저장소 update 시맨틱**: `FrameRepository.SaveAsync`(`FrameRepository.cs:44-74`) — `frame.Id`가 비면 `Guid.NewGuid()` 부여, 있으면 그대로. `Db.Collection.Document(frame.Id).SetAsync(doc)`(`:72`)로 **같은 문서 덮어쓰기(update) 성립**. Storage 경로도 `frames/{owner}/{frameId}.png`로 같은 키 덮어씀. 10개 제한은 `existing.All(f => f.Id != frame.Id)` 조건으로 **기존 문서 업데이트는 카운트에서 제외**(`:52`).
- **HTTP 저장소 update 불성립**: `HttpFrameRepository.SaveAsync`(`HttpFrameRepository.cs:70-97`)가 보내는 `SaveFrameRequest`(`FrameDtos.cs:40-46`)에 **`Id` 필드가 없다**. 백엔드 `POST /frames`(`routes/frames.ts:58-82`) → `saveFrame`(`services/frames.ts:69-102`)이 `const frameId = randomUUID()`(`:82`)로 **항상 새 문서 생성**. 응답 `res.Frame.Id`는 신규 GUID. → HTTP 모드에서 기본 프레임 "업데이트"를 지금 수행하면 **중복 문서**가 생긴다.
- **DI 분기**: `IFrameRepository`는 `AppSettings.UseBackend`로 팩토리 분기(`ServiceRegistration.cs:134-147`) — OFF=`FrameRepository`(Admin), ON=`HttpFrameRepository`. 기본 OFF(레거시).
- **로컬 저장소 규약**: `LocalFrameStore`(`LocalFrameStore.cs`). 공용(번들·파워캐시)=`{이름}.png`(접두 없음), user 전용=`{계정}_{이름}.png`. `.slots` 첫 줄 `#imagesize=W,H`, 공용은 `#dbid=` 메타 보존(서버 매칭용). `LoadPublic()`은 `_`없는 파일, `LoadUser(owner)`는 `{owner}_` 접두 파일만. Id: `#dbid` 있으면 그 값, 없으면 `local:{fileName}`(`:118`).
- **DB 프레임의 로컬 표현**: DB 기본 프레임은 `FrameCatalogService.GetDefaultFramesAsync`(`FrameCatalogService.cs:50-98`)가 `TryCacheAsync`(`:112-138`)로 `_localStore.CacheFromDb`(→ `#dbid` 메타에 DB GUID 보존) 캐시. 따라서 **FrameSelect에 뜨는 DB 기본 프레임은 Id=DB GUID(접두 없음), IsDefault=true, UserId=null, ImageUrl=로컬 캐시 png 경로**. (즉 편집 시 `frame.ImageUrl`은 로컬 파일이라 `LoadForEdit` 이미지 로드 성립.)
- **슬롯/이미지 도메인**: `Slot{Index,X,Y,Width,Height}`(`Slot.cs`), `ImageSize{Width,Height}`(`FrameTemplate.cs:31-35`). 편집 이미지 바이트는 PNG 재인코딩본(`FrameEditorViewModel.cs:100-101`, `LoadForEdit`은 로컬 png 그대로 `_imageBytes`).
- **순수 로직 위치**: `SlotLayout`(검증·클램프·스케일), `EditorTransform`(좌표 변환), `SlotAspect` 모두 `MCPhoto.Core.Frames`에 순수 함수로 존재 → **diff·권한 판정도 같은 계층에 순수 함수로 추가**하면 테스트 용이(기존 `SlotLayoutTests`·`EditorTransformTests` 관례).
- **테스트 관례**: VM 단위 테스트는 `IFrameRepository`/`ILocalFrameStore` 스텁으로 저장 호출을 캡처(`FrameEditorViewModelTests.cs`, `FrameSelectViewModelTests.cs`). 순수 함수는 xUnit `[Theory]`/`[Fact]`(`SlotLayoutTests`).
- **역할 판정**: `UserRole.IsPower()`=manager|admin(`UserRole.cs:35`). `SessionContext.CurrentUser`(User{Id, Role}).
- **분석 문서**: `docs/analysis/11-exe-app-features.md` §4가 생성·편집·삭제 규칙을 현행대로 서술 → **본 이터레이션 반영 후 §4.1 갱신 대상**.

### 1.2 미검증 가정 (open assumptions)

- **[A1]** manager가 편집하려는 대상이 실제로 "DB 기본 프레임"임을 프레임 Id 규약(접두 없음 + IsDefault=true + `#dbid` 존재)만으로 정확히 판별할 수 있다. → **검증 단계: Step 1** (출처 판정 순수 함수 + 단위 테스트).
- **[A2]** user 계정에서 FrameSelect에 노출되는 "본인 로컬 프레임"은 `LoadUser(user.Id)`가 돌려준 `{user.Id}_` 접두 파일뿐이며, 이 목록의 프레임은 항상 `UserId==user.Id`로 세팅되어 소유 판정이 성립한다. → **검증 단계: Step 1·Step 2** (LoadUser 세팅 확인 + 권한 판정 테스트).
- **[A3]** 원본(편집 진입 시점) 대비 diff 판정에 필요한 "원본 이미지 바이트"를 `LoadForEdit`가 읽는 `_imageBytes`(로컬 png)로 확보할 수 있고, 이 바이트가 DB에 올라간 이미지와 동일(캐시=DB 다운로드본)하다. → **검증 단계: Step 3** (원본 스냅샷 보관 + diff 함수 테스트). *리스크*: 캐시 png가 DB 원본과 바이트 동일하지 않을 가능성(재인코딩) → diff는 **해시 동일 → 변경없음** 방향으로만 신뢰하고, 불일치 시 "변경있음"으로 보수 판정(안전측: 불필요한 업데이트는 나도 데이터 손상 없음).
- **[A4]** HTTP 모드에서 같은 id 업데이트를 지원하려면 서버 계약(`POST /frames` 또는 신규 `PUT /frames/{id}`) 확장이 필요하다. 확장 전에는 HTTP 모드에서 "DB 업데이트"를 차단하는 것이 무회귀 안전이다. → **검증 단계: Step 5** (HTTP 모드 가드 + 계약 확장 여부 결정 `[USER-DECISION-REQUIRED]`).
- **[A5]** 팝업(로컬만/DB도)은 기존 삭제 확인 팝업(`FrameSelectView.xaml:106-129`, `IsDeleteConfirmVisible` 오버레이)과 동일 패턴으로 FrameEditorView에 오버레이로 얹을 수 있다. → **검증 단계: Step 4** (편집기 팝업 XAML + VM 상태).

---

## 2. 프레임 출처 판정 (요구 1 — 순수)

### 2.1 출처 구분 정의

FrameSelect에 노출되는 `FrameTemplate` 하나를 세 출처 + 파생으로 판정한다. **기존 Id/접두 규약 재사용**(신규 규약 도입 금지).

| 출처 | 판정 근거(실측 규약) | 예시 Id | IsDefault | UserId |
|------|----------------------|---------|-----------|--------|
| (a) **본인 로컬 생성분** | `local:` 접두 + `UserId==현재계정` (LoadUser가 `{owner}_` 접두 파일에 `UserId=ownerId` 세팅, `LocalFrameStore.cs:120-128`) | `local:u1_myframe` | false | u1 |
| (b) **DB 기본(자동 다운로드)** | 접두 없음(local:/bundle:/fallback 아님) + `IsDefault==true` (CacheFromDb가 `#dbid` GUID 보존, IsDefault=true) | `a1b2c3-guid` | true | null |
| (c) **번들/설치 자산** | `bundle:` 접두 (`FrameCatalogService.cs:172`) | `bundle:classic` | true | null |
| (c') **fallback(코드 생성)** | `fallback` 접두 (`DefaultFrameProvider`) | `fallback` | true | null |

### 2.2 순수 판정 함수 (신규)

**위치**: `src/MCPhoto.Core/Frames/FrameOrigin.cs` (신규, `MCPhoto.Core.Frames`).

```
enum FrameOriginKind { UserLocal, DbDefault, Bundle, Fallback }

static class FrameOrigin
{
    // 접두·플래그만으로 판정(순수). currentUserId는 UserLocal 소유 판별에만 사용.
    static FrameOriginKind Classify(FrameTemplate frame);        // 접두/IsDefault 기반 종류
    static bool IsOwnedLocal(FrameTemplate frame, string userId); // local: 접두 && UserId==userId
    static bool IsDbDefault(FrameTemplate frame);                 // 접두 없음 && IsDefault==true && Id 비어있지 않음
}
```

판정 규칙(우선순위):
1. `Id`가 `bundle:` 접두 → **Bundle**
2. `Id`가 `fallback` 접두(또는 빈 Id) → **Fallback**
3. `Id`가 `local:` 접두 → **UserLocal**
4. 그 외(접두 없는 실 DB id) → **DbDefault**

`IsOwnedLocal(frame, userId)` = `Classify==UserLocal && !string.IsNullOrEmpty(userId) && frame.UserId == userId`.
`IsDbDefault(frame)` = `Classify==DbDefault && frame.IsDefault`.

> **설계 판단 `[CONFIRM]`**: `IsOwnedLocal`에서 `UserId==userId`를 요구한다(요구 2 "본인이 만든"의 엄격 해석). 실측상 `LoadUser`가 본인 것만 로드해 `UserId`를 채우므로 정상 흐름에 영향 없음. 다만 `#dbid` 없이 `local:{fileName}`으로 로드되는 로컬 파일은 `UserId=ownerId`(LoadUser 경로) 또는 `null`(LoadPublic 경로)일 수 있어, **UserLocal 프레임은 반드시 LoadUser 경로로만 목록에 올라온다**는 사실(A2)에 의존한다. 근거: `FrameSelectViewModel.ReloadFramesAsync`(`:74-81`)가 공용은 `GetDefaultFramesAsync`, 본인 것은 `GetUserFramesAsync(user.Id)`로 분리 로드.

---

## 3. 편집 권한 규칙 (요구 2 — 역할×출처, 순수)

### 3.1 규칙 표

| 출처 \ 역할 | 게스트(비로그인) | user | manager/admin(power) |
|-------------|:---:|:---:|:---:|
| (a) 본인 로컬 생성분 | 편집 불가 | **편집 가능** | **편집 가능** |
| (b) DB 기본(자동 다운로드) | 편집 불가 | **편집 불가** | **편집 가능**(→ §4 저장 팝업) |
| (c) 번들 | 편집 불가 | 편집 불가 | 편집 불가 |
| (c') fallback | 편집 불가 | 편집 불가 | 편집 불가 |

- 게스트: 모든 편집 불가(로그인 필요).
- user: **본인 로컬만**. DB 기본·번들·fallback 차단.
- power: 본인 로컬 + DB 기본. 번들·fallback은 여전히 차단(설치 자산·코드 생성물은 편집 대상 아님).

### 3.2 순수 판정 함수 (신규)

**위치**: `src/MCPhoto.Core/Frames/FrameEditPolicy.cs` (신규, `MCPhoto.Core.Frames`).

```
static class FrameEditPolicy
{
    // 편집 가능 여부(역할×출처). userId=현재 계정 id(게스트면 null/empty).
    static bool CanEdit(FrameTemplate frame, UserRole? role, string? userId);

    // 저장 시 DB 업데이트 팝업을 띄워야 하는 대상인지(power && DB 기본 프레임).
    static bool RequiresDbUpdatePrompt(FrameTemplate frame, UserRole? role);
}
```

`CanEdit` 판정:
- `role is null`(게스트) → false
- `FrameOrigin.Classify(frame)`:
  - `UserLocal` → `FrameOrigin.IsOwnedLocal(frame, userId)` (본인 것만)
  - `DbDefault` → `role.Value.IsPower()`
  - `Bundle` / `Fallback` → false

`RequiresDbUpdatePrompt` = `role?.IsPower()==true && FrameOrigin.IsDbDefault(frame)`.

### 3.3 UI 반영(요구 2 — 진입/버튼 차단)

- **FrameSelectViewModel.CanEdit(f)**(`:214-221`)를 **삭제하고** `FrameEditPolicy.CanEdit(f, role, userId)` 호출로 대체. `OnSelectedFrameChanged`(`:223-224`)에서 `CanEditSelected` 계산도 이 함수로. → "선택 편집" 버튼(`FrameSelectView.xaml:141-143`)이 규칙대로 노출/숨김.
- **편집기 진입 이중 게이트**: `EditFrame()`(`:207-211`)의 `!CanEdit(...)` 가드도 `FrameEditPolicy.CanEdit`로 교체(버튼 숨김 + 명령 가드 동시).
- **FrameEditorViewModel.LoadForEdit**에서 진입 시 권한 재확인은 하지 않는다(진입 경로가 이미 게이트됨) — 대신 **저장 시** `RequiresDbUpdatePrompt`로 팝업 여부를 결정(§4).

> **UI 최소 변경 원칙**: "선택 편집" 버튼·바인딩(`CanEditSelected`)은 이미 존재하므로 XAML 변경 없음. 로직만 순수 함수로 교체.

---

## 4. manager 기본프레임 편집 저장 플로우 (요구 3 — 팝업 + diff)

### 4.1 흐름 개요

```
[저장] 클릭
  └ SlotLayout.IsValid 검증(기존)
  └ FrameEditPolicy.RequiresDbUpdatePrompt(editingFrame, role)?
       ├ NO  → 기존 저장 그대로 (user=로컬, power 신규생성=DB+캐시)
       └ YES → 【확인 팝업 표시】"이 기본 프레임을 어떻게 저장할까요?"
                 ├ [로컬에만 적용]  → 로컬 캐시만 갱신(SaveLocal ownerName=null), DB 미호출
                 ├ [DB에도 업데이트] → diff 판정:
                 │     ├ 변경 있음 → IFrameRepository.SaveAsync(같은 frameId) + 로컬 캐시 갱신 + 안내
                 │     └ 변경 없음 → DB 미호출(no-op) + 로컬 캐시만 + "변경 없음" 안내
                 └ [취소] → 팝업만 닫고 편집 유지(저장 안 함)
```

### 4.2 이미지·슬롯 diff 판정 (순수, 테스트)

**위치**: `src/MCPhoto.Core/Frames/FrameDiff.cs` (신규, `MCPhoto.Core.Frames`).

```
readonly struct FrameChange { bool ImageChanged; bool SlotsChanged; bool NameChanged;
                              bool HasAnyChange => ImageChanged || SlotsChanged || NameChanged; }

static class FrameDiff
{
    // 원본 대비 편집본 변경 여부(순수). 이미지=바이트 해시, 슬롯=개수·좌표·크기, 이름=문자열.
    static FrameChange Compare(
        byte[]? originalImage, byte[]? editedImage,
        IReadOnlyList<Slot> originalSlots, IReadOnlyList<Slot> editedSlots,
        ImageSize originalSize, ImageSize editedSize,
        string originalName, string editedName);

    static bool SlotsEqual(IReadOnlyList<Slot> a, IReadOnlyList<Slot> b); // 개수+각 Index/X/Y/W/H 동일
    static bool ImageEqual(byte[]? a, byte[]? b);                          // 길이 동일 + SHA-256 동일
}
```

판정 규칙:
- **ImageChanged**: `originalImage`·`editedImage` 중 하나만 null → 변경. 둘 다 있으면 길이 다르면 변경, 같으면 SHA-256 비교(다르면 변경). *A3 리스크 대응*: 원본을 확보 못 하면(null) 변경으로 간주(보수적 — 불필요 업데이트는 데이터 무해).
- **SlotsChanged**: `SlotsEqual` 부정. 개수 다르거나, 정렬(Index 순) 후 하나라도 X/Y/Width/Height 불일치.
- **NameChanged**: `originalName != editedName`(Ordinal).
- **SizeChanged는 ImageChanged에 포함**되므로 별도 플래그 불필요(크기 변경은 반드시 이미지 재로드 동반).

> **설계 판단 `[CONFIRM]`**: 슬롯 비교는 **좌표·크기 정수 완전일치**로 한다. 종횡비는 X/Y/W/H가 같으면 자동으로 같으므로 별도 비교 불필요. 드래그로 1px만 움직여도 "변경"으로 판정(정확). "변경 없음"은 원본을 그대로 열어 아무 조작 없이 저장한 경우로 한정.

### 4.3 원본 스냅샷 보관 (편집기 상태)

`FrameEditorViewModel`에 원본 스냅샷 필드 추가(diff 기준):

```
private byte[]? _originalImageBytes;      // LoadForEdit 진입 시 _imageBytes 복사
private List<Slot> _originalSlots = new();// LoadForEdit 진입 시 frame.Slots 복사(깊은 복사)
private string _originalName = "";        // LoadForEdit 진입 시 frame.Name
private ImageSize _originalSize = new();
private FrameTemplate? _editingFrame;     // 편집 대상 원본 참조(Id·IsDefault·UserId 보존)
```

- `LoadForEdit(frame)`에서 위 스냅샷을 세팅. 신규 생성(`CreateFrame`)에서는 `_editingFrame=null`, 스냅샷 비움.
- 저장 시 diff는 `FrameDiff.Compare(_originalImageBytes, _imageBytes, _originalSlots, Slots, _originalSize, new ImageSize{FrameWidth,FrameHeight}, _originalName, FrameName)`.

### 4.4 팝업 상태·명령 (VM)

`FrameEditorViewModel`에 추가:

```
[ObservableProperty] bool _isDbUpdatePromptVisible;   // 팝업 오버레이 표시
[ObservableProperty] string _dbUpdateNotice = "";     // 결과 안내(성공/변경없음/실패/HTTP차단)
[ObservableProperty] bool _dbUpdateNoticeIsError;

[RelayCommand] Task SaveLocalOnly();   // 팝업의 [로컬에만 적용]
[RelayCommand] Task SaveToDb();        // 팝업의 [DB에도 업데이트]
[RelayCommand] void CancelDbUpdatePrompt(); // 팝업 [취소]
```

- 기존 `Save()`(`:244-302`)를 **분리**: 검증 후 `RequiresDbUpdatePrompt`이면 `IsDbUpdatePromptVisible=true`로 전환하고 **저장을 보류**(리턴). 아니면 기존 저장 로직 실행.
- `SaveLocalOnly()`: `_localStore.SaveLocal(BuildFrame(keepId:true), _imageBytes, ownerName:null)` (DB 미호출) → 캐시 갱신 후 FrameSelect로 이동.
- `SaveToDb()`:
  1. `FrameDiff.Compare(...)` → `change.HasAnyChange == false`면 DB 미호출, 로컬 캐시만 갱신, `DbUpdateNotice="변경 사항이 없어 DB 업데이트를 건너뛰었습니다."`(비오류), FrameSelect로 이동.
  2. 변경 있음 → **HTTP 모드 가드**(§5): 백엔드 모드이면서 계약 미확장이면 DB 미호출, `DbUpdateNoticeIsError=true`, "현재 서버 모드에서는 기본 프레임 DB 업데이트를 지원하지 않습니다(로컬만 적용됨)." 후 로컬 캐시만 갱신. *(레거시 모드는 통과)*
  3. 변경 있음 + 지원 모드 → `_repository.SaveAsync(BuildFrame(keepId:true, Id=_editingFrame.Id), _imageBytes)` → 성공 시 `_localStore.SaveLocal(saved, _imageBytes, ownerName:null)`(캐시 갱신), 성공 안내, FrameSelect 이동. 예외 시 화면 유지 + 오류 안내(§6).
- `BuildFrame`: manager DB 기본 프레임이므로 `Id=_editingFrame.Id`(같은 문서), `UserId=null`, `IsDefault=true`, `Name=FrameName`, `ImageSize`, `Slots`.

> **핵심**: `SaveAsync`에 `Id=_editingFrame.Id`를 넘겨 레거시 `SetAsync(frame.Id)`가 **같은 문서를 덮어쓴다**(사실 1.1). 10개 제한은 기본 프레임(UserId=null)에 미적용(`FrameRepository.cs:49`).

### 4.5 팝업 XAML (편집기)

`FrameEditorView.xaml`에 **삭제 팝업과 동일 오버레이 패턴**으로 추가(A5). 기존 2열 Grid에 `Grid` 오버레이를 마지막 자식으로 얹고 `Visibility="{Binding IsDbUpdatePromptVisible, Converter={StaticResource BoolToVis}}"`.

내용:
- 제목: "기본 프레임 저장"
- 본문: "이 프레임은 공용 기본 프레임입니다. 어떻게 저장할까요?"
- 버튼: [로컬에만 적용](Secondary, `SaveLocalOnlyCommand`) / [DB에도 업데이트](Primary, `SaveToDbCommand`) / [취소](Ghost, `CancelDbUpdatePromptCommand`)
- 결과 안내 TextBlock: `DbUpdateNotice` + `BoolToNoticeBrush`(기존 컨버터 재사용, `CommonConverters.cs:97`).

> 컨버터 `BoolToVis`, `BoolToNoticeBrush`는 이미 등록(`App.xaml`) → 신규 컨버터 불필요. 리소스 키 충돌 없음.

---

## 5. IFrameRepository update 시맨틱 검토 (요구 3 — 양 모드)

### 5.1 레거시(Admin) — 성립 `[CODE]`

`FrameRepository.SaveAsync`는 `frame.Id`가 있으면 `Document(frame.Id).SetAsync`로 **같은 문서 덮어쓰기**. Storage도 같은 키 덮어씀. **추가 서버 변경 없이 즉시 동작**. → item2 저장 플로우는 레거시에서 완전 성립.

### 5.2 HTTP(백엔드) — 불성립 → 가드 + 계약 확장안

**현행**: `POST /frames`가 `randomUUID()`로 신규 문서 생성(사실 1.1). 같은 id 업데이트 경로 없음 → 그대로 두면 **중복 문서** 발생(무회귀 위반).

**단기(본 이터레이션, 무회귀 안전)** — `[CODE]`(클라):
- `HttpFrameRepository`에 **update 미지원을 명시적으로 표현**할 방법이 필요. 두 안:
  - **(권장) 저장 플로우에서 모드 감지**: `FrameEditorViewModel`은 `IFrameRepository`가 update-by-id를 지원하는지 판단해야 한다. `IFrameRepository`에 순수 capability 속성 `bool SupportsUpdateById { get; }`를 추가(레거시=true, HTTP=false). §4.4 SaveToDb의 HTTP 가드가 이 값으로 분기 → HTTP 모드에서 "변경 있음"이면 DB 미호출·경고, 로컬만 적용.
  - (대안) VM이 `AppSettings.UseBackend`를 직접 읽어 분기 — 추상화 누수라 비권장.
- 이 가드로 **HTTP 모드에서도 데이터 무결성 유지**(중복문서 0), user/신규생성 경로는 무영향.

**중장기(HTTP 모드 완전 지원)** — `[CODE]`(서버, 계약 변경) `[USER-DECISION-REQUIRED]`:
- 옵션 A: `POST /frames`가 요청 본문 `id`(선택)를 받아, 있으면 해당 문서 `.set()`(upsert). `SaveFrameRequest`에 `Id` 추가, 서버 `saveFrame`이 `input.id ?? randomUUID()`.
- 옵션 B: 신규 `PUT /frames/{id}`(파워) 엔드포인트 — 명시적 update. `HttpFrameRepository`가 편집 시 PUT 사용.
- 두 옵션 모두 **서버(`web/functions`) 변경**이며 firebase-contract 갱신 필요 → **제품/배포 판단**이라 `[USER-DECISION-REQUIRED]`. 본 이터레이션은 단기 가드까지만 CODE로 진행하고, 서버 계약 확장은 사용자 결정 후 별도 이터레이션 권장.

> **결정 요청 `[USER-DECISION-REQUIRED]` #1**: HTTP 백엔드 모드에서 manager의 "기본 프레임 DB 업데이트"를 이번에 완전 지원할지(서버 계약 확장 A/B) 아니면 단기 가드(HTTP=로컬만+경고)로 두고 다음 이터레이션에서 서버를 확장할지. **권장: 단기 가드**(현재 기본 모드가 레거시 OFF이고, 서버 계약 변경은 배포·contract 문서 동반이라 범위가 큼).

---

## 6. 편집기 완성도 마감 (요구 4 — 엣지)

실측 기반 미흡점과 마감 항목:

| # | 미흡/엣지 | 현행 | 마감 설계 |
|---|-----------|------|-----------|
| E1 | **편집 상태 재진입 잔존** | `FrameEditorViewModel`은 DI 싱글턴/스코프에 따라 재사용될 수 있고, `_isEditing`·`_editingFrameId`가 이전 편집에서 남을 수 있음(신규 생성인데 편집 상태로 저장) | `CreateFrame`(신규) 진입 시 편집 상태·스냅샷을 명시적 초기화하는 `ResetForNew()` 추가. `LoadForEdit`는 항상 스냅샷 재세팅. **DI 등록 확인**: FrameEditorViewModel이 Transient면 자연 초기화(검증 단계에서 확인) |
| E2 | **이미지 부재 편집 진입** | `LoadForEdit`에서 `ImageUrl` 파일 없으면 `StatusMessage`만 세팅, `_isEditing=true` 유지 → 저장 시 `_imageBytes=null`이라 `CanSave=false`(막힘)는 되나 사용자에겐 진입만 되고 저장 불가 이유 불명확 | 이미지 부재 시 명확한 안내 유지 + "이미지 다시 불러오기"로 복구 가능(기존 OnLoadImage 버튼 그대로 사용) — **동작 확인만**, 코드 최소 |
| E3 | **저장 실패 시 화면 이탈** | `Save()`는 `await _shell.NavigateAsync(FrameSelect)`를 try 블록 성공 경로에서 호출. 예외는 catch에서 StatusMessage만 → **화면 유지 정상**. 단 팝업 경로(SaveToDb) 신설 시 동일 보장 필요 | SaveToDb/SaveLocalOnly도 예외 시 화면 유지 + 안내(NavigateAsync는 성공 시에만). 팝업은 실패 시 닫되 안내는 편집 화면에 남김 |
| E4 | **기존 슬롯 복원 정확성** | `LoadForEdit`는 슬롯을 `_baseSlots`에 로드 후 `SlotScalePercent=100`로 표시(`:154-159`) — 정상. 단 `SlotCount` clobber 억제(`_suppressArrange`)가 이미 있음 | 회귀 테스트로 고정(로드→저장 시 슬롯 개수·좌표 보존) |
| E5 | **취소 동작** | `Cancel()`(`:304-305`)은 FrameSelect로 이동 — 저장 안 함, 정상 | 팝업이 열린 상태에서 편집기 [취소]와 팝업 [취소] 구분: 팝업 [취소]는 팝업만 닫고 편집 유지(§4.4) |
| E6 | **DB 업데이트 후 목록 갱신** | FrameSelect `OnEnterAsync`가 `ReloadFramesAsync` 호출(`:60`) → 편집기에서 돌아오면 재스캔 | 캐시 갱신(SaveLocal ownerName=null)이 `#dbid` 보존하도록 확인 — SaveAsync가 돌려준 `saved.Id`(=원본 GUID)로 SaveLocal 호출 시 `#dbid` 유지(중복 캐시 방지) |

> E1은 리스크 단일화를 위해 **DI 수명 확인 후** 필요 시에만 `ResetForNew` 추가(Step 6에서 검증).

---

## 7. 파일별 역할 (변경/신규)

| 파일 | 유형 | 역할 | 인코딩 |
|------|------|------|--------|
| `src/MCPhoto.Core/Frames/FrameOrigin.cs` | **신규** | 출처 판정 순수 함수(§2) | 프로젝트 관례(UTF-8) |
| `src/MCPhoto.Core/Frames/FrameEditPolicy.cs` | **신규** | 편집 권한 판정 순수 함수(§3) | UTF-8 |
| `src/MCPhoto.Core/Frames/FrameDiff.cs` | **신규** | 이미지·슬롯 diff 순수 함수(§4.2) | UTF-8 |
| `src/MCPhoto.Core/Frames/IFrameRepository.cs` | 수정 | `bool SupportsUpdateById { get; }` capability 추가(§5.2) | **기존 인코딩 보존** |
| `src/MCPhoto.Firebase/FrameRepository.cs` | 수정 | `SupportsUpdateById => true` | 기존 보존 |
| `src/MCPhoto.Http/HttpFrameRepository.cs` | 수정 | `SupportsUpdateById => false` | 기존 보존 |
| `src/MCPhoto.App/ViewModels/FrameSelectViewModel.cs` | 수정 | `CanEdit` → `FrameEditPolicy.CanEdit` 위임(§3.3) | 기존 보존 |
| `src/MCPhoto.App/ViewModels/FrameEditorViewModel.cs` | 수정 | 원본 스냅샷, 팝업 상태·명령, 저장 분기(§4.3·4.4·6) | 기존 보존 |
| `src/MCPhoto.App/Views/FrameEditorView.xaml` | 수정 | DB 업데이트 확인 팝업 오버레이 추가(§4.5) | 기존 보존 |
| `docs/analysis/11-exe-app-features.md` | 수정 | §4.1에 권한 규칙·팝업/diff 반영 | 기존 보존 |
| `tests/MCPhoto.Tests/FrameOriginTests.cs` | **신규** | 출처 판정 테스트 | UTF-8 |
| `tests/MCPhoto.Tests/FrameEditPolicyTests.cs` | **신규** | 권한 규칙 테스트(역할×출처 매트릭스) | UTF-8 |
| `tests/MCPhoto.Tests/FrameDiffTests.cs` | **신규** | diff 판정 테스트 | UTF-8 |
| `tests/MCPhoto.Tests/FrameEditorViewModelTests.cs` | 수정 | 팝업/DB업데이트/로컬만/diff no-op/HTTP 가드 VM 테스트 추가 | 기존 보존 |
| `tests/MCPhoto.Tests/FrameSelectViewModelTests.cs` | 수정 | 편집 게이트(user 본인만/DB=power) 테스트 추가 | 기존 보존 |

> **인코딩 규칙(안전 규칙 6)**: 기존 파일 수정 시 현재 파일 인코딩 보존. **실측 결과 기존 소스는 UTF-8 without BOM**(`SlotLayout.cs`/`IFrameRepository.cs`/`FrameEditorViewModel.cs` 선두 바이트에 `EF BB BF` 없음). 신규 파일도 **UTF-8 without BOM**으로 생성.

`SupportsUpdateById`를 인터페이스에 추가하면 기존 테스트 스텁(`CapturingFrameRepository`, `StubRepo`)도 멤버 구현이 필요하다 → 해당 스텁에 `=> true`(또는 테스트 의도에 맞게) 추가(무회귀).

---

## 8. 스레딩·안전 모델

- 모든 저장/DB 호출은 `RelayCommand`의 `async Task`(기존 `Save`와 동일). UI 스레드 블로킹 없음. `SaveAsync`는 이미 백그라운드 I/O.
- diff의 SHA-256 계산은 편집 이미지 크기(수 MB)에서 저비용이나, 안전하게 `SaveToDb` 명령 내 `await Task.Run`으로 감쌀지는 **[CONFIRM]**: 프레임 이미지는 장변 4000 이하로 축소된 PNG이고 diff는 저장 클릭 1회뿐이라 UI 스레드 직접 계산 허용(수십 ms). 필요 시 developer가 `Task.Run` 적용 가능.
- 이벤트 구독 신규 없음(팝업은 바인딩 기반). 누수 위험 0.
- `FrameEditorView.xaml.cs`의 기존 `Slots.CollectionChanged` 구독은 `OnDataContextChanged`에서 해제 후 재구독(`:36-37`) — 팝업 추가는 code-behind 이벤트 불요(순수 바인딩) → 누수 무영향.

---

## 9. 품질 자체 점검

- [x] 모든 신규 View 요소(팝업)에 대응 VM 상태·명령·연결 방식 명확(§4.4·4.5)
- [x] 바인딩·명령에 누락 VM 멤버 없음(팝업 3버튼 + 안내 2속성 명시)
- [x] 이벤트 구독 신규 없음(팝업 바인딩) — 누수 위험 0
- [x] UI/백그라운드 경계: 저장은 async Task, diff는 저비용 UI 스레드 허용(§8)
- [x] 리소스 키 충돌 없음(기존 컨버터 재사용, 신규 키 없음)
- [x] 전역 예외/오류 표시: 저장 실패 시 화면 유지 + 안내(§6 E3)
- [x] VM/순수 함수가 UI 없이 테스트 가능(FrameOrigin/EditPolicy/Diff는 Core, VM은 스텁)
- [x] 양 모드(레거시/HTTP) 동작: 레거시 완전 지원, HTTP 가드로 무회귀(§5)
- [x] developer가 추가 질문 없이 구현 가능한 상세도

---

## 10. 구현 단계 (WBS 블루프린트)

> 형식: `docs/templates/WBS_BLUEPRINT.md`. 각 단계 self-contained.
> 공통 검증: 저장소 루트 `E:\Study\photobooth`에서 `dotnet build MCPhoto.sln` (또는 `build-verify` 스킬), `dotnet test tests/MCPhoto.Tests/MCPhoto.Tests.csproj`.

### Step 1: 프레임 출처 판정 순수 함수 (FrameOrigin)
- **Context Brief**: MCPhoto 프레임은 Id 접두로 출처를 구분한다(`local:`=user 로컬, `bundle:`=번들, `fallback`=코드생성, 그 외 접두 없음=DB 기본). 편집 권한·저장 팝업 판단의 기반이 될 순수 판정 함수를 만든다. 기존 규약은 `LocalFrameStore.cs`(id 생성)·`FrameCatalogService.cs:172`(bundle:)·`FrameSelectViewModel.IsDeletable`에 흩어져 있다.
- **대상 파일**: `src/MCPhoto.Core/Frames/FrameOrigin.cs`(신규), `tests/MCPhoto.Tests/FrameOriginTests.cs`(신규)
- **선행 조건**: 없음
- **구현 내용**: §2.2의 `FrameOriginKind` enum과 `FrameOrigin.Classify/IsOwnedLocal/IsDbDefault` 순수 함수 구현. 접두 판정은 `StringComparison.Ordinal`.
- **검증 명령**: `dotnet test tests/MCPhoto.Tests/MCPhoto.Tests.csproj --filter FullyQualifiedName~FrameOriginTests`
- **완료 기준**:
  - [관측] `local:u1_x`+UserId=u1 → IsOwnedLocal(true); `local:u1_x`+UserId=u2 → false; 접두없는 GUID+IsDefault → IsDbDefault(true); `bundle:x`→Bundle; `fallback`→Fallback. 테스트 전부 PASS.
  - [non-goal] 기존 `IsDeletable`·`FrameDeleteVisibilityConverter` 동작 불변(이 단계는 신규 파일만 추가).
  - [trigger] 판정은 함수 호출 시에만 — 전역 상태·부작용 없음(순수).
- **롤백**: 신규 파일 2개 삭제(다른 단계와 독립).
- [ ] 완료

### Step 2: 편집 권한 규칙 순수 함수 + FrameSelect 게이트 교체 (FrameEditPolicy)
- **Context Brief**: user는 본인 로컬 프레임만, power는 본인 로컬+DB 기본 프레임을 편집 가능해야 한다(요구 2). 현재 `FrameSelectViewModel.CanEdit`(`:214-221`)은 `local:` 접두면 소유 검증 없이 허용한다. Step 1의 `FrameOrigin`을 써서 규칙을 순수 함수로 만들고 VM이 위임하게 한다.
- **대상 파일**: `src/MCPhoto.Core/Frames/FrameEditPolicy.cs`(신규), `src/MCPhoto.App/ViewModels/FrameSelectViewModel.cs`(수정), `tests/MCPhoto.Tests/FrameEditPolicyTests.cs`(신규), `tests/MCPhoto.Tests/FrameSelectViewModelTests.cs`(수정)
- **선행 조건**: Step 1(`FrameOrigin`)
- **구현 내용**: §3.2의 `FrameEditPolicy.CanEdit/RequiresDbUpdatePrompt` 구현. `FrameSelectViewModel.CanEdit(f)`를 `FrameEditPolicy.CanEdit(f, role, userId)` 호출로 교체(role/userId는 `_shell.Session.CurrentUser`에서). `EditFrame()`·`OnSelectedFrameChanged` 가드도 이 함수 사용. 권한 매트릭스(게스트/user/power × 4출처) 테스트, VM 편집 게이트 테스트(user가 DB 기본 편집 불가, power는 가능) 추가.
- **검증 명령**: `dotnet test ... --filter "FullyQualifiedName~FrameEditPolicyTests|FullyQualifiedName~FrameSelectViewModelTests"`
- **완료 기준**:
  - [관측] user 세션에서 DB 기본 프레임(접두없는 GUID) 선택 시 `CanEditSelected=false`; 본인 `local:u1_x` 선택 시 true. power 세션에서 DB 기본 선택 시 true. 게스트는 항상 false. 테스트 PASS + 빌드 통과.
  - [non-goal] 삭제 관련 로직(`IsDeletable`, ConfirmDelete)·기존 삭제 테스트 불변. "선택 편집" 버튼 XAML(`FrameSelectView.xaml:141-143`) 미변경.
  - [trigger] 편집 게이트는 프레임 선택(`SelectedFrame` 변경) 시에만 재계산; 다른 프레임 선택 없이 상태 변경 없음.
- **롤백**: FrameEditPolicy.cs 삭제 + FrameSelectViewModel.CanEdit 원복.
- [ ] 완료

### Step 3: 이미지·슬롯 diff 순수 함수 + 원본 스냅샷 보관 (FrameDiff)
- **Context Brief**: manager가 DB 기본 프레임을 편집·저장할 때 "변경이 있으면만" DB를 업데이트한다(요구 3). 원본(편집 진입 시점) 대비 이미지·슬롯·이름 변경 여부를 판정하는 순수 함수와, 편집기가 원본을 보관하도록 한다. `FrameEditorViewModel.LoadForEdit`(`:123-160`)가 이미 이미지 바이트(`_imageBytes`)·슬롯(`_baseSlots`)을 로드한다.
- **대상 파일**: `src/MCPhoto.Core/Frames/FrameDiff.cs`(신규), `src/MCPhoto.App/ViewModels/FrameEditorViewModel.cs`(수정: 스냅샷 필드), `tests/MCPhoto.Tests/FrameDiffTests.cs`(신규)
- **선행 조건**: 없음(Step 4가 이 스냅샷을 사용)
- **구현 내용**: §4.2의 `FrameChange`/`FrameDiff.Compare/SlotsEqual/ImageEqual`(SHA-256) 구현. `FrameEditorViewModel`에 §4.3 스냅샷 필드(`_originalImageBytes/_originalSlots/_originalName/_originalSize/_editingFrame`) 추가하고 `LoadForEdit`에서 세팅(깊은 복사), 신규 생성 경로에서 비움. diff 테스트: 무변경→HasAnyChange=false, 슬롯 1px 이동→SlotsChanged, 이미지 바이트 변경→ImageChanged, 원본 null→변경으로 간주(보수).
- **검증 명령**: `dotnet test ... --filter FullyQualifiedName~FrameDiffTests` + `dotnet build`
- **완료 기준**:
  - [관측] 동일 입력(원본=편집본) Compare → HasAnyChange=false; 슬롯/이미지/이름 하나 변경 시 해당 플래그 true. 원본 이미지 null → ImageChanged=true. 테스트 PASS + 빌드 통과.
  - [non-goal] 기존 편집기 저장/드래그/스케일 동작 불변(스냅샷은 필드 추가만, 저장 분기는 Step 4에서).
  - [trigger] 스냅샷은 `LoadForEdit` 호출 시에만 세팅 — 드래그·스케일 중 갱신 없음(원본 고정).
- **롤백**: FrameDiff.cs 삭제 + VM 스냅샷 필드 제거.
- [ ] 완료

### Step 4: 저장소 update capability + HTTP 가드 (SupportsUpdateById)
- **Context Brief**: manager DB 업데이트는 "같은 frameId 덮어쓰기"를 요구한다. 레거시 `FrameRepository.SaveAsync`는 `SetAsync(frame.Id)`로 성립하나, HTTP `HttpFrameRepository`는 백엔드가 항상 새 GUID를 만들어 **불성립**(중복문서 위험). 저장소가 update-by-id 지원 여부를 노출하게 하고, VM이 이를 보고 HTTP 모드에서 안전 차단하도록 한다.
- **대상 파일**: `src/MCPhoto.Core/Frames/IFrameRepository.cs`(수정), `src/MCPhoto.Firebase/FrameRepository.cs`(수정), `src/MCPhoto.Http/HttpFrameRepository.cs`(수정), 테스트 스텁 2곳(`FrameEditorViewModelTests.cs`·`FrameSelectViewModelTests.cs`의 stub repo)
- **선행 조건**: 없음
- **구현 내용**: `IFrameRepository`에 `bool SupportsUpdateById { get; }` 추가. `FrameRepository=>true`, `HttpFrameRepository=>false`. 기존 테스트 스텁(`CapturingFrameRepository`, `StubRepo`)에 해당 멤버 구현 추가(레거시 의미로 `true` 또는 테스트별 필요값).
- **검증 명령**: `dotnet build MCPhoto.sln` + `dotnet test tests/MCPhoto.Tests/MCPhoto.Tests.csproj`(기존 전체 무회귀)
- **완료 기준**:
  - [관측] 빌드 통과(인터페이스 멤버 추가로 인한 미구현 에러 0). 기존 전체 테스트 PASS. `new FrameRepository(...).SupportsUpdateById==true`, `new HttpFrameRepository(...).SupportsUpdateById==false`.
  - [non-goal] SaveAsync/DeleteAsync 등 기존 저장소 동작·시그니처 불변(속성 추가만). 서버(`web/functions`) 미변경.
  - [trigger] 속성은 읽기 전용 상수 — 런타임 상태 의존 없음.
- **롤백**: 인터페이스·구현 3곳·스텁 2곳에서 속성 제거.
- [ ] 완료

### Step 5: manager 저장 팝업 플로우 + diff 연동 (FrameEditorViewModel 저장 분기)
- **Context Brief**: manager가 DB 기본 프레임을 편집·저장하면 "로컬만/DB도 업데이트" 확인 팝업을 띄우고, DB 선택 시 diff로 변경이 있을 때만 같은 frameId로 업데이트한다(요구 3). Step 2(RequiresDbUpdatePrompt)·Step 3(FrameDiff, 스냅샷)·Step 4(SupportsUpdateById)를 조립한다. 기존 `Save()`(`FrameEditorViewModel.cs:244-302`)는 power일 때 팝업 없이 항상 DB 저장한다.
- **대상 파일**: `src/MCPhoto.App/ViewModels/FrameEditorViewModel.cs`(수정), `tests/MCPhoto.Tests/FrameEditorViewModelTests.cs`(수정)
- **선행 조건**: Step 2, Step 3, Step 4
- **구현 내용**: §4.4 — `IsDbUpdatePromptVisible/DbUpdateNotice/DbUpdateNoticeIsError` 속성, `SaveLocalOnly/SaveToDb/CancelDbUpdatePrompt` 명령 추가. `Save()`를 분기: 검증 후 `FrameEditPolicy.RequiresDbUpdatePrompt`이면 팝업 표시(저장 보류), 아니면 기존 저장. `SaveToDb`: diff 무변경→DB 미호출+로컬캐시+"변경없음" 안내; 변경+`!_repository.SupportsUpdateById`→DB 미호출+오류안내+로컬만; 변경+지원→`SaveAsync(Id=_editingFrame.Id)`+캐시+성공안내. 예외 시 화면 유지(§6 E3). VM 테스트: manager DB 기본 편집 저장→팝업 표시; SaveLocalOnly→repo.Saved=null·local 갱신; SaveToDb 무변경→repo.Saved=null; SaveToDb 변경(지원 repo)→repo.Saved.Id==원본; SaveToDb 변경(미지원 repo)→repo.Saved=null·NoticeIsError.
- **검증 명령**: `dotnet test ... --filter FullyQualifiedName~FrameEditorViewModelTests` + `dotnet build`
- **완료 기준**:
  - [관측] manager가 DB 기본 프레임(접두없는 GUID, IsDefault=true) LoadForEdit 후 SaveCommand 실행 → `IsDbUpdatePromptVisible=true`, repo.Saved=null(아직 미저장). SaveToDbCommand + 슬롯 변경 + 지원 repo → repo.Saved.Id==원본 GUID, local.SavedOwner=null. 무변경 → repo.Saved=null. 미지원 repo → repo.Saved=null & DbUpdateNoticeIsError=true. 모든 신규/기존 테스트 PASS.
  - [non-goal] user 저장(로컬 전용)·power **신규 생성**(팝업 없음, EditingServerId=null) 경로 불변 — 기존 `User_Save_Persists_Locally`·`Power_Save_Persists_To_Db_And_Local_Cache` 테스트 PASS 유지. 취소/팝업취소 시 저장·이동 없음.
  - [trigger] 팝업은 SaveCommand 클릭 && RequiresDbUpdatePrompt일 때만; DB 업데이트는 SaveToDbCommand 클릭 && diff 변경 && SupportsUpdateById일 때만. CancelDbUpdatePrompt는 팝업만 닫고 편집 유지(저장·이동 없음).
- **롤백**: FrameEditorViewModel의 팝업 속성·명령 제거, `Save()` 원복.
- [ ] 완료

### Step 6: 편집기 팝업 XAML + 완성도 엣지 마감 + 문서 갱신
- **Context Brief**: Step 5의 팝업 상태·명령을 화면에 노출하고(기존 삭제 팝업과 동일 오버레이 패턴, `FrameSelectView.xaml:106-129` 참조), 편집기 완성도 엣지(§6 E1·E6)를 마감하며 분석 문서 §4.1을 갱신한다.
- **대상 파일**: `src/MCPhoto.App/Views/FrameEditorView.xaml`(수정), `src/MCPhoto.App/ViewModels/FrameEditorViewModel.cs`(수정: ResetForNew if needed), `docs/analysis/11-exe-app-features.md`(수정)
- **선행 조건**: Step 5
- **구현 내용**: §4.5 — FrameEditorView에 DB 업데이트 확인 팝업 오버레이 추가(제목/본문/[로컬에만 적용]/[DB에도 업데이트]/[취소] + 결과 안내 TextBlock). 기존 컨버터 `BoolToVis`/`BoolToNoticeBrush` 재사용(신규 키 없음). §6 E1: FrameEditorViewModel의 DI 수명 확인(ServiceRegistration) — Transient가 아니면 `CreateFrame`(신규) 진입 시 편집 상태·스냅샷 초기화(`ResetForNew`) 추가; Transient면 불필요(주석으로 근거 명시). §6 E6: SaveToDb에서 `SaveAsync`가 돌려준 `saved`(원본 Id 유지)로 SaveLocal 호출해 `#dbid` 보존. 분석 문서 §4.1에 권한 규칙(user 본인 로컬만/power+DB 기본)·팝업/diff·SupportsUpdateById·HTTP 가드 반영.
- **검증 명령**: `dotnet build MCPhoto.sln`(XAML 컴파일 포함) + headless XAML 로드 회귀가 있으면 실행(`dotnet test ... --filter ~Xaml` 존재 시). 없으면 `dotnet build`로 XAML 파싱 검증.
- **완료 기준**:
  - [관측] 빌드 통과(XAML 파싱 에러 0). 팝업 오버레이가 `IsDbUpdatePromptVisible` 바인딩으로 표시/숨김되고 3버튼이 각 명령에 바인딩됨(grep로 `SaveToDbCommand`/`SaveLocalOnlyCommand`/`CancelDbUpdatePromptCommand` 3개 확인). 분석 문서 §4.1에 권한/팝업/diff 서술 존재.
  - [non-goal] FrameEditorView 기존 캔버스·컨트롤 패널·드래그 code-behind 불변. 다른 화면 XAML·리소스 딕셔너리 불변(신규 리소스 키 0).
  - [trigger] 팝업 표시는 `IsDbUpdatePromptVisible=true`일 때만(Step 5 트리거에 종속); 편집기 로드/드래그 중 팝업 자동 표시 없음.
- **롤백**: FrameEditorView 팝업 오버레이 XAML 제거, 문서 원복(다른 단계와 독립).
- [ ] 완료

---

## 11. 완결성 게이트 (자체 검사)

- [x] 검증된 사실 / 미검증 가정 분리(§1.1 / §1.2)
- [x] 모든 가정에 검증 단계 매핑(A1→Step1, A2→Step1·2, A3→Step3, A4→Step5, A5→Step6/§4.5)
- [x] 모든 단계에 7개 필수 필드(Context Brief/대상 파일/선행 조건/구현 내용/검증 명령/완료 기준/롤백)
- [x] 모든 완료 기준 관측 기반 3문 형식(UI 단계 Step5·6은 non-goal·trigger 포함)
- [x] 검증 명령 자동 실행 가능(dotnet build/test --filter)

---

## 12. 보고 요지 (CODE/CONSOLE · CONFIRM · USER-DECISION · 순서)

### 12.1 출처 판정 / 권한 규칙 / 저장 플로우 / 마감 요지
- **출처 판정**(§2): Id 접두+IsDefault+UserId로 UserLocal/DbDefault/Bundle/Fallback 구분. 신규 순수 함수 `FrameOrigin`(Core).
- **권한 규칙**(§3): user=본인 로컬만(UserId 검증), power=본인 로컬+DB 기본, 번들/fallback·게스트 차단. 신규 순수 함수 `FrameEditPolicy`. FrameSelect의 기존 `CanEdit` 위임 교체(버튼·진입 이중 게이트).
- **저장 플로우**(§4·§5): power가 DB 기본 편집·저장 시 확인 팝업(로컬만/DB도/취소). DB 선택 시 `FrameDiff`로 변경 판정 → 변경 있음+지원 모드면 같은 frameId `SaveAsync`(레거시 SetAsync 덮어쓰기)+캐시 갱신, 변경 없음이면 DB 미호출(no-op). HTTP 모드는 계약 미지원이라 `SupportsUpdateById=false`로 안전 차단(로컬만+경고).
- **편집기 마감**(§6): 원본 스냅샷 보관, 저장 실패 화면 유지, `#dbid` 캐시 보존, 재진입 상태 초기화(DI 수명 확인 후), 기존 슬롯 복원 회귀 테스트.

### 12.2 [CODE] / [CONSOLE]
- **[CODE]**(내가 후속 파이프라인에서 구현) — 본 이터레이션 전부가 CODE. 신규 3파일(FrameOrigin/EditPolicy/Diff) + 저장소 capability(인터페이스+구현3) + VM 2개 + XAML 1개 + 테스트 5파일 + 분석 문서 1개.
- **[CODE](서버, 조건부)** — HTTP 모드 완전 지원 시 `web/functions`의 `POST /frames` id 수용 또는 `PUT /frames/{id}` 신설 + `SaveFrameRequest`에 Id 추가 + firebase-contract 갱신. **USER-DECISION #1 승인 시에만**.
- **[CONSOLE]**(USER-ACTIONS) — 없음. 배포/콘솔 설정 변경 불요(레거시 모드에서 즉시 동작). *단, USER-DECISION #1에서 서버 확장을 택하면 Firebase Functions 재배포가 CONSOLE로 추가됨.*

### 12.3 [CONFIRM] (합리적 기본안, 근거 있음)
- **C1**: `IsOwnedLocal`은 `UserId==userId` 엄격 검증(요구 2 "본인이 만든"). 근거: LoadUser가 본인 것만 로드해 정상 흐름 무영향.
- **C2**: 슬롯 diff는 X/Y/W/H 정수 완전일치. 1px 이동도 변경으로 판정(정확). 원본 그대로 저장만 "변경 없음".
- **C3**: 원본 이미지 확보 실패 시 ImageChanged=true(보수적) — 불필요 업데이트는 데이터 무해.
- **C4**: diff SHA-256은 UI 스레드 직접 계산 허용(장변 4000 이하 PNG, 저장 1회, 수십 ms). 필요 시 `Task.Run`.
- **C5**: 팝업은 기존 삭제 확인 팝업과 동일 오버레이 패턴·기존 컨버터 재사용(신규 리소스 키 0).

### 12.4 [USER-DECISION-REQUIRED] (진짜 제품 판단)
- **#1 (§5.2)**: HTTP 백엔드 모드에서 manager의 "기본 프레임 DB 업데이트"를 이번에 **완전 지원**할지(서버 계약 확장 A: POST id 수용 / B: PUT 신설), 아니면 **단기 가드**(HTTP=로컬만+경고)로 두고 서버 확장은 다음 이터레이션으로 미룰지. **architect 권장: 단기 가드**(기본 모드가 레거시 OFF이고, 서버 변경은 배포·contract 동반이라 범위·리스크가 큼). 이 결정에 따라 §5.2 중장기 항목의 CODE/CONSOLE 포함 여부가 갈린다.

### 12.5 권장 구현 순서
Step 1(FrameOrigin) → Step 2(FrameEditPolicy+게이트) → Step 3(FrameDiff+스냅샷) → Step 4(SupportsUpdateById) → Step 5(저장 팝업 플로우) → Step 6(팝업 XAML+마감+문서).
- Step 1·3·4는 상호 독립(병렬 가능). Step 2는 Step 1 후, Step 5는 2·3·4 후, Step 6은 5 후.
- 각 단계 검증(`dotnet test --filter`)으로 독립 PASS/FAIL 확인 후 다음 단계 진행.
