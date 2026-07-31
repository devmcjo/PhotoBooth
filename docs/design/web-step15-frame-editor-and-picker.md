# 웹 클라이언트 Step 15 — 프레임 편집기 · 기존 프레임 불러오기 피커 · 삭제 통합

| 항목 | 값 |
|------|-----|
| 대상 | `webclient/` (TypeScript + React + Vite) · WBS [Step 15](../web-client/11-wbs.md#step-15-프레임-편집기--피커--삭제) |
| 규격 | [`03 §11`](../web-client/03-screens-spec.md)(§11.1~§11.7) · [`03 §15.4·§15.5·§15.7`](../web-client/03-screens-spec.md) · [`analysis/13 §6`](../analysis/13-client-behavior-spec.md)(**2026-07-31 개정판이 진실원**) · [`analysis/14 §4`](../analysis/14-media-pipeline-spec.md) |
| Windows 원본 | `src/MCPhoto.App/ViewModels/FrameEditorViewModel.cs` · `FramePickerViewModel.cs` · `src/MCPhoto.Core/Frames/{FrameNaming,SlotLayout,EditorTransform,FrameImageValidator,FrameEditPolicy}.cs` |
| 설계 근거 | [`wpf-frame-create-from-existing-and-server-register-design.md`](./wpf-frame-create-from-existing-and-server-register-design.md)(D1~D6·R1·R2) |
| 선행 | Step 2(도메인) · Step 12(인증) · **Step 14**(`frameStore`·`frameCatalog`·`frameImageCache`·`frameThumbnails`·삭제 오버레이) |
| 작성일 | 2026-08-01 |
| 범위 | **WD20 15a + 15b 전량**(이연 없음) |

> ⚠️ **이 문서를 읽기 전에 반드시 알아야 할 것**
> **"기존 프레임 불러오기 = 사본(fork)"은 2026-07-30 사용자 결정으로 폐기됐다.** 피커로 불러온 세션의 정체성은
> **신규 생성(`New`)** 이고, 따라서 power가 불러온 세션도 **서버 등록 대상**이며 이름 자동 제안(`{원본} 사본`)도 **없다**.
> fork는 `FrameSelect`의 **[선택 편집] 경로에만** 남는다. 저장소의 오래된 주석·`it15` 문서를 보고 옛 동작을 구현하면
> §4 ⑦ 가드가 자기 자신과 충돌해 저장이 영구히 막히는 등 규격 전체가 어긋난다.

---

## 0. 검증된 사실 / 미검증 가정

### 0.1 검증된 사실 (2026-08-01, 코드·문서를 직접 읽어 확인)

- **F-1** 도메인은 **이미 이식돼 있다**: `editorTransform.ts`(§4.5 변환) · `slotLayout.ts`(autoArrange·scaleSlots·clampToFrame·overlaps·isValidLayout) · `slotAspect.ts` · `frameNaming.ts`(**`isFileNameSafe` 분리 완료** — 길이를 보지 않는다) · `frameEditPolicy.ts` · `frameOrigin.ts` · `frameStorePolicy.ts`. 벡터 `auto-arrange.json`·`scale-slots.json`·`clamp-slot.json`·`overlap.json`·`editor-transform.json`·`copy-name.json`이 Windows와 교차 고정한다. **다시 만들지 않는다.**
- **F-2** `frameStore.saveLocal(input)`·`countPersonal(userId)`·`exceedsLocalFrameLimit(count)`(`LOCAL_FRAME_LIMIT=10`)가 **구현돼 있고 호출자가 0명**이다(`adapters/storage/frameStore.ts:167-194`).
- **F-3** 삭제 흐름은 **Step 14가 완성했다**: `screens/frameSelect/frameSelectActions.ts`의 `runFrameDelete`(6단계 순서 + 결과 4문구) + `ui/views/FrameSelectView.tsx`의 **화면 로컬 오버레이** `FrameDeleteOverlay`. 정적 검사 **FR-5**가 `FrameSelectView.tsx`에 `pushModal(`·`"confirmDelete"`·`"framePicker"`가 0건임과 `screens/modals/{confirmDelete,framePicker}` **디렉터리 부재**를 고정한다(`tests/unit/frames/frameInvariants.test.ts:120-134`).
- **F-4** `frameRepository.createFrame`은 **응답 봉투를 잘못 읽는다.** 서버는 `201 {frame, upload:{putUrl,downloadUrl,requiredHeaders}}`를 준다(`web/functions/src/routes/frames.ts:81` → `services/frames.ts:122` · [`analysis/31 §4.12`](../analysis/31-backend-api-reference.md)). 그러나 클라이언트는 **최상위** `record.putUrl`·`record.requiredHeaders`를 읽는다(`adapters/http/frameRepository.ts:132-137`) → **항상 `putUrl=null`·`requiredHeaders={}`**. 호출자가 없어 지금까지 드러나지 않았다. → **§11에서 고친다.**
- **F-5** `frameStore.saveLocal`의 `persist()`는 **같은 키를 덮어쓸 때 이전 OPFS 이미지를 지우지 않는다**(`frameStore.ts:258-287` — 새 토큰으로 파일을 쓰고 `meta.put`으로 레코드를 교체한다). Step 14에는 덮어쓰기 경로가 없어 문제가 없었지만, Step 15의 **`EditOwnLocal` 덮어쓰기 저장**은 매번 고아 PNG를 남긴다. → **§10.2에서 고친다.**
- **F-6** `FrameImageValidator`(10MB·장변 4000·확장자)는 **웹에 이식되지 않았다.** [`01 §2.2`](../web-client/01-tech-stack-and-structure.md) 매핑표에 행 자체가 없다. Windows 테스트는 `tests/MCPhoto.Tests/SlotLayoutTests.cs:256-287`에 있다. → **§9.1에서 이식한다.**
- **F-7** `canEditFrame(frame, role, userId)`는 `UserLocal`에 대해 `isOwnedLocal`(= `frame.userId === userId`)을 요구한다. **power가 로컬 공용으로 저장한 프레임은 `userId=null`이라 편집 대상이 아니다**(삭제는 `canDeleteFrame` 2인자라 가능). Windows `FrameEditPolicy.cs`와 **동일 동작**이며 FR-2가 이 축을 고정한다. → **바꾸지 않는다**(§17 표에 명시).
- **F-8** 상태 머신이 `FrameSelect → FrameEditor`와 `FrameEditor → {FrameSelect, Settings, Login}`을 허용하고, `isSessionActive`가 `FrameEditor`를 **제외**한다(유휴 감시 비대상 — `03 §11.2`와 일치). 화면 라우팅은 `App.tsx:193` `default:` → `DummyScreen`이다.
- **F-9** `frameCatalog`는 **모듈 싱글턴 단일 비행**이고 취소는 **호출자별**이다. `loadPublic({signal,onProgress})`·`loadLocalOnly()`·`loadPersonal(userId)` 세 계약이 있고 **절대 reject하지 않는다**(취소 예외만 예외).
- **F-10** `frameCatalog`가 돌려주는 `frames[]`의 `imageUrl`은 **항상 same-origin**이다: 로컬 캐시·서버 캐시분 = `blob:`(OPFS), 번들 = 상대 경로, fallback = 코드 생성. 이미지를 못 받은 서버 프레임은 `frames`가 아니라 `unavailable[]`로 빠진다.
- **F-11** `frameImageCache`가 object URL의 **유일한 소유자**이고 해제 시점은 **프레임 삭제뿐**이다. 편집기 미리보기 URL은 이 캐시의 소유가 아니다 → **편집기가 자기 URL을 직접 해제해야 한다**(§8.4).
- **F-12** `STRINGS.frames`의 `nameEmpty`·`nameTooLong`·`nameInvalidChars`·`sameNameRejected`·`limitReached`·`localOnlyBanner`·`underscoreWarning`·`nameUnderscoreRejected`는 **`src`·`tests` 어디에서도 참조되지 않는다**(Step 2에서 카탈로그만 옮겨 둔 상태) → 문구 정정이 안전하다.
- **F-13** 서버 `POST /frames`는 `name`에 `_`가 있으면 **400**이다(`web/functions/src/domain/validation.ts`). `imageSize`·`slots`도 재검증하며 `isDefault`·`userId`는 서버가 강제한다.

### 0.2 미검증 가정 (전부 검증 단계가 매핑돼 있다)

| # | 가정 | 검증 |
|---|------|------|
| **A15-1** | `OffscreenCanvas.convertToBlob({type:"image/png"})`가 대상 브라우저 전부에서 동작한다 | 폴백(`HTMLCanvasElement.toBlob`)을 함께 구현 → **V24-1**(실측) |
| **A15-2** | `createImageBitmap(blob, {imageOrientation:"from-image"})`가 EXIF 회전 JPG를 바로 세운다 | **V24-2**(실측) |
| **A15-3** | 편집기에서 본 슬롯 위치와 그 프레임으로 촬영한 합성 결과가 **0px 일치**한다 | **V24-3**(실측 — 규격의 핵심 수락 조건) |
| **A15-4** | 서명 PUT(프레임 이미지)이 `POST /frames`의 `requiredHeaders`로 성공한다(OA-1과 같은 축, 다른 경로) | **V24-4**(실계정 power 필요) |
| **A15-5** | Pointer Events 드래그가 태블릿 터치에서 스크롤과 충돌하지 않는다(`touch-action: none`) | **V24-5**(실기기) |

---

## 1. 이 Step이 푸는 문제 5개

1. **WYSIWYG 편집기** — 표시·드래그·클램프가 **하나의 `EditorTransform`** 을 쓰지 않으면 저장한 슬롯과 합성 결과가 어긋난다(Windows B3 버그의 재발).
2. **저장 전 검증 7단의 순서** — 진입점이 `[저장]`과 서버 등록 확인 오버레이 **2개**라, 한쪽만 검사하면 모달 경로로 우회된다. 순서 자체가 규격이다(④가 ⑦보다 먼저).
3. **서버 등록의 원자성** — 서버 등록이 실패했는데 로컬만 저장되면, 재시도 시 ⑦ 가드가 **자기 자신과 충돌**해 저장이 영구히 막힌다.
4. **불러오기 = 신규 생성** — 세션 정체성 축(`FrameSessionSource`) 하나가 배너·이름 제안·서버 등록·fork 저장을 전부 결정한다. 파생값(`isCreateMode` 류)을 쓰면 조용한 불일치가 생긴다.
5. **삭제 중복 구현 방지** — Step 14가 이미 만든 화면 로컬 오버레이를 공용 모달로 재작성하지 않는다.

---

## 2. 계층 배치 (한눈에)

```
ui/views/FrameEditorView.tsx ─────────── 렌더만. 판정 0(jsdom 없음 — 15 §3.1)
  │  ├─ <FrameEditorStage/>            프레임 <img> + 슬롯 <button> (한 변환)
  │  ├─ <FramePickerOverlay/>          화면 로컬 오버레이 ①
  │  └─ <ServerRegisterOverlay/>       화면 로컬 오버레이 ②   (① ②는 상호배타 — 03 §790)
  ▼
screens/frameEditor/useFrameEditor.ts ── 얇은 훅(상태 보관 + 세대 카운터 + 아래 모듈 호출)
  ├─ frameEditorState.ts   순수 reducer(기하·세션·문구 상태) ......... node 테스트
  ├─ frameEditorSave.ts    runFrameSave(검증 재실행 → 서버 → 로컬) ... node 테스트
  ├─ framePickerRunner.ts  후보 목록 로딩·취소·상한 ................. node 테스트
  └─ previewUrl.ts         object URL 단일 소유자(누수 0) ........... node 테스트
  ▼
adapters/frames/frameImageLoader.ts ──── File/URL → PNG Blob + 크기 (브라우저 전용, 예외 미전파)
adapters/storage/frameStore.ts ───────── +scopeFrameNames, saveLocal 고아 정리 (수정)
adapters/http/frameRepository.ts ─────── createFrame `upload` 봉투 정정 (수정)
adapters/http/uploadGateway.ts ───────── put() 재사용 (무변경)
  ▼
domain/frames/frameSavePolicy.ts ─────── 세션 축·스코프·7단+⑧ 검증  (신규, 순수)
domain/frames/frameImagePolicy.ts ────── 10MB·장변 4000·확장자      (신규, 순수 — F-6 이식)
domain/frames/slotLayout.ts ──────────── +rescaleSlots               (수정, 순수)
domain/frames/{editorTransform,frameNaming,frameEditPolicy,frameOrigin,slotAspect}.ts  (무변경)

shell/frameEditorIntent.ts ───────────── FrameSelect → FrameEditor 인계 채널 (신규)
```

**규칙**: `src/domain`은 아무것도 import하지 않는다(순수성 테스트가 자동 포함) · 어댑터는 예외를 전파하지 않는다 ·
`console.*` 금지(`logger.*`만) · OPFS 쓰기는 `opfsWriter` Worker 경계 뒤(VF-14 — 이 Step은 `frameStore`를 지나므로 자동 충족).

---

## 3. 세션 정체성 축 — `FrameSessionSource`

### 3.1 값 3종과 진입 경로

```
빈 편집기 [프레임 만들기]                      → "New"
[기존 프레임 불러오기] 피커 적용                → "New"   ★ 사본 아님(2026-07-30 재정의)
[선택 편집], 출처 = 본인 로컬 생성분             → "EditOwnLocal"
[선택 편집], 출처 = 서버 공용 기본(power만 도달) → "ForkFromCatalog"
```

`domain/frames/frameSavePolicy.ts`

```ts
export const FRAME_SESSION_SOURCES = ["New", "EditOwnLocal", "ForkFromCatalog"] as const;
export type FrameSessionSource = (typeof FRAME_SESSION_SOURCES)[number];

/** [선택 편집] 진입 시의 세션 축. `requiresFork(frame)`(도메인 기존 함수)가 유일한 판정 근거다. */
export function editSessionSource(frame: FrameTemplate): FrameSessionSource {
  return requiresFork(frame) ? "ForkFromCatalog" : "EditOwnLocal";
}
```

- **피커는 세션 축을 바꾸지 않는다.** `New`로 진입해 피커를 써도 `New`, `EditOwnLocal`로 진입해서는 피커 버튼 자체가 노출되지 않는다(§7.1).
- `sourceName`은 **fork·피커 양쪽에서 기록**한다(fork = ④ 가드 근거, 피커 = 안내 캡션 근거). `New` 세션에서는 ④가 발동하지 않는다 — 그 자리는 ⑦이 막는다.

### 3.2 이 축이 결정하는 것 4가지 (전부 같은 함수를 부른다)

| 결정 | 함수 | 값 |
|------|------|-----|
| 정책 배너 노출 | `showsLocalOnlyBanner(source)` | `source !== "New"` |
| 서버 등록 확인 오버레이 노출 | **`requiresServerRegisterPrompt(role, source)`** | `isPower(role) && source === "New"` |
| 서버 등록 실행 분기 | **같은 함수** | 동상 |
| 저장 결과 캡션 | `saveScopeNoticeKind(role, source)` | 4종 |

```ts
/**
 * 서버 등록 확인 오버레이 노출 조건 = **서버 등록 분기와 완전히 같은 축**(03 §11.4).
 * ⚠️ `isCreateMode` 같은 파생값을 쓰지 마라 — 두 축이 갈라지면 "오버레이는 떴는데 등록은 안 되는"
 *    조용한 불일치가 생긴다. 권한 축은 `isPower`만 쓴다(`canWriteFrames`로 넓히면 DB 권한이 없는
 *    advanced_user에게 서버 등록 체크박스를 노출한다).
 * ⚠️ 이 함수의 호출부는 **정확히 2곳**이다(오버레이 판정 · 등록 분기). 정적 검사 FR-11이 고정한다.
 */
export function requiresServerRegisterPrompt(
  role: UserRole | null,
  source: FrameSessionSource,
): boolean {
  return role !== null && isPower(role) && source === "New";
}

export type SaveScopeNoticeKind = "public-new" | "public-fork" | "overwrite" | "personal";

/** 저장 버튼 위 캡션의 **종류**만 고른다. 문구 조립은 UI(`ui/strings.ts`)가 한다. */
export function saveScopeNoticeKind(
  role: UserRole | null,
  source: FrameSessionSource,
): SaveScopeNoticeKind {
  if (role !== null && isPower(role)) {
    if (source === "New") return "public-new";
    if (source === "ForkFromCatalog") return "public-fork";
    return "overwrite";
  }
  return source === "EditOwnLocal" ? "overwrite" : "personal";
}

/** 저장 스코프. power = 공용, 그 외(advanced_user) = 개인. */
export function frameSaveScope(role: UserRole | null): FrameSaveScope {
  return role !== null && isPower(role) ? "public" : "personal";
}

/** 정책 배너(analysis/13 §6.4)는 **편집 세션 전용**이다 — 신규 생성 세션에는 문장이 거짓이 된다. */
export function showsLocalOnlyBanner(source: FrameSessionSource): boolean {
  return source !== "New";
}
```

> ⚠️ `frameSaveScope`가 돌려주는 `FrameSaveScope`(`"public" | "personal"`)는 `frameNaming.ts`의 기존 타입이다.
> `frameStorePolicy.ts`의 `FrameScope`(`"public" | "user"`)와 **다른 타입**이다 — 저장 호출부에서
> `scope === "public" ? "public" : "user"`로 한 번 변환한다(§5.3). 두 타입을 합치지 마라(전자는 이름 규약 축,
> 후자는 저장소 레코드 축이고 기존 벡터·테스트가 각각 걸려 있다).

---

## 4. 저장 전 검증 — 순수 함수 하나가 순서를 소유한다

### 4.1 왜 도메인 순수 함수인가

Windows는 `TryValidateForSave(out error)` **한 메서드**가 순서를 소유하고, 진입점 2개가 모두 그것을 부른다.
웹은 ⑦(스코프 이름 열거)·⑧(개인 프레임 개수)이 비동기라 그대로 옮기면 검증 자체가 async가 되어
"순서가 규격"이라는 성질이 배선 코드에 흩어진다. → **비동기 조회를 호출자가 먼저 끝내고, 판정은 순수 함수 하나**가 한다.

```
screens/frameEditorSave.ts  ── ①  scopeFrameNames() / countPersonal()  비동기 수집(실패 = 빈 값)
                              ↓
domain/frameSavePolicy.ts   ── ②  validateFrameSave(input)  ← 순서를 소유하는 유일한 지점
```

### 4.2 계약

```ts
export interface FrameSaveValidationInput {
  /** null = 게스트(비로그인). */
  readonly role: UserRole | null;
  readonly sessionSource: FrameSessionSource;
  /** PNG 바이트를 확보했는가(이미지 미로드 상태 차단). */
  readonly hasImage: boolean;
  readonly slots: readonly Slot[];
  readonly frameWidth: number;
  readonly frameHeight: number;
  /** **원문 그대로**(trim하지 않는다 — 실제 저장되는 문자열이다). */
  readonly name: string;
  /** fork 원본 이름. 없으면 "". */
  readonly sourceName: string;
  /**
   * 저장 스코프의 기존 이름들. ⚠️ **열거 실패는 빈 배열**이고 그때 ⑦은 조용히 꺼진다(비차단).
   * 그것이 ④를 ⑦보다 먼저 두는 이유다(2중 방어).
   */
  readonly existingNames: readonly string[];
  /** 저장 **전** 개인 프레임 개수. 공용 스코프에서는 무시된다. */
  readonly personalCount: number;
}

export type FrameSaveRejection =
  | "not-logged-in"        // ①
  | "no-write-permission"  // ②
  | "invalid-slots"        // ③
  | "same-as-source"       // ④
  | "name-empty"           // ⑤
  | "name-invalid-chars"   // ⑥
  | "name-conflict"        // ⑦
  | "limit-reached";       // ⑧ (웹 추가 게이트 — 7단 **뒤** 고정)

export interface FrameSaveValidation {
  readonly ok: boolean;
  readonly reason?: FrameSaveRejection;
}

export function validateFrameSave(input: FrameSaveValidationInput): FrameSaveValidation;
```

### 4.3 구현 — 순서가 규격이다

```ts
export function validateFrameSave(input: FrameSaveValidationInput): FrameSaveValidation {
  // ① 로그인
  if (input.role === null) return { ok: false, reason: "not-logged-in" };
  // ② 쓰기 권한 — 화면 게이트로 도달할 수 없는 역할이지만 저장 경로에도 둔다(3차 게이트, fail-closed).
  if (!canWriteFrames(input.role)) return { ok: false, reason: "no-write-permission" };
  // ③ 슬롯 유효성(개수 1~6 · 경계 내 · 겹침 없음) + 이미지 확보
  if (!input.hasImage) return { ok: false, reason: "invalid-slots" };
  if (!isValidLayout(input.slots, input.frameWidth, input.frameHeight)) {
    return { ok: false, reason: "invalid-slots" };
  }

  const isPublic = frameSaveScope(input.role) === "public";
  const isFork = input.sessionSource === "ForkFromCatalog";

  // ④ 원본 덮어쓰기 가드 — ⑦보다 **먼저**다.
  //    ⑦은 열거 실패 시 조용히 꺼지지만 ④는 "원본 이름"이라는 확정 사실만 본다.
  //    개인 스코프는 저장 키에 소유자가 들어가 공용 원본과 물리적으로 겹치지 않으므로 공용에서만 본다.
  if (isFork && isPublic && input.name === input.sourceName) {
    return { ok: false, reason: "same-as-source" };
  }

  // ⑤⑥ 이름 안전성 — **`isFileNameSafe` 하나**로 판정한다(길이를 보지 않는다).
  //     ⚠️ `validateFrameName`(100자 포함)을 쓰면 축이 어긋난다(03 §11.3 웹 주의).
  if (input.name.trim().length === 0) return { ok: false, reason: "name-empty" };
  if (!isFileNameSafe(input.name)) return { ok: false, reason: "name-invalid-chars" };

  // ⑦ 스코프 이름 충돌 — 예외는 **[선택 편집]으로 연 본인 로컬 생성분 세션 하나뿐**이다.
  //    로컬 저장은 같은 키를 경고 없이 덮어쓴다 → 가드가 없으면 다른 프레임이 조용히 파괴된다.
  //    비교는 정확 일치(Ordinal) — JS의 `===`가 그 축이다.
  const collides = input.existingNames.includes(input.name);
  if (input.sessionSource !== "EditOwnLocal" && collides) {
    return { ok: false, reason: "name-conflict" };
  }

  // ⑧ 개인 프레임 10개 상한(05 §4.8 — `scope:"user"`·`ownerId` 기준).
  //    ⚠️ **새 키를 만드는 저장에만** 적용한다. 덮어쓰기(이미 존재하는 이름)는 개수를 늘리지 않으므로
  //       상한에 걸리면 안 된다 — 안 그러면 10개를 채운 계정이 자기 프레임을 수정조차 못 한다.
  if (!isPublic && !collides && exceedsLocalFrameLimit(input.personalCount)) {
    return { ok: false, reason: "limit-reached" };
  }

  return { ok: true };
}
```

**왜 ⑧을 ⑦ 뒤에 두는가**: ①~⑦의 순서는 규격이고 문구까지 고정돼 있다. ⑧을 앞에 끼우면 "이름이 비었는데
`프레임은 최대 10개까지…`가 뜨는" 오안내가 생긴다. Windows도 상한을 `SaveLocal` 내부에서 마지막에 던진다.

**문구 매핑**(도메인은 문자열을 갖지 않는다 — `ui/strings.ts`의 `frameSaveRejectionMessage(reason)`):

| reason | 문구(03 §11.3 문자열 일치) | STRINGS 키 |
|--------|---------------------------|------------|
| `not-logged-in` | 로그인이 필요합니다. | `frameEditor.rejectNotLoggedIn`(신규) |
| `no-write-permission` | 프레임을 만들 권한이 없습니다. | `frameEditor.rejectNoPermission`(신규) |
| `invalid-slots` | 슬롯이 겹치거나 프레임을 벗어났습니다. | `frameEditor.rejectInvalidSlots`(신규) |
| `same-as-source` | 원본과 같은 이름은 사용할 수 없습니다. 이름을 변경해 주세요. | `frames.sameNameRejected`(기존) |
| `name-empty` | 프레임 이름을 입력해 주세요. | `frames.nameEmpty` — **"이름을 입력해 주세요."에서 정정**(F-12로 안전) |
| `name-invalid-chars` | 이름에 사용할 수 없는 문자가 있습니다. | `frames.nameInvalidChars`(기존) |
| `name-conflict` | 이미 같은 이름의 프레임이 있습니다. 다른 이름을 입력해 주세요. | `frameEditor.rejectNameConflict`(신규) |
| `limit-reached` | 프레임은 최대 10개까지 저장할 수 있습니다. | `frames.limitReached`(기존) |

---

## 5. 저장 파이프라인 — `screens/frameEditor/frameEditorSave.ts`

### 5.1 진입점 2개와 재실행

```
[저장] 버튼  ──► requestSave()
   │  ① validateFrameSave 로 선판정(비동기 수집 포함) ── 실패 → status = 문구, 저장·오버레이 없음
   │  ② requiresServerRegisterPrompt(role, source) ── true ─► 오버레이 열기(registerToServer = true 리셋)
   │                                                          **이 시점에 아무것도 저장하지 않는다**
   └────────────────────────────────────── false ─► runFrameSave({ registerToServer: false })

[오버레이 저장] ──► const alsoServer = state.registerToServer;   // ★ 닫기 **전에** 지역 값으로 확정
                    closeOverlay(); resetRegisterToServer();      // 리셋이 먼저면 선택이 조용히 무시된다
                    runFrameSave({ registerToServer: alsoServer })

[오버레이 취소] ──► closeOverlay(); resetRegisterToServer();      // 저장·전환·저장소 모두 무변경
```

### 5.2 `runFrameSave` — 실제 저장 함수 (첫 줄에서 검증 재실행)

```ts
export interface FrameSaveDeps {
  /** 저장 스코프의 기존 이름(메타 전용 조회). 실패는 **빈 배열**(⑦ 비차단). */
  scopeNames(): Promise<readonly string[]>;
  /** 개인 프레임 개수. 실패는 0. */
  personalCount(): Promise<number>;
  /** `POST /frames`. 예외를 그대로 던진다(HTTP 계층 관례). */
  createServerFrame(request: CreateFrameRequest): Promise<CreateFrameResponse>;
  /** 서명 PUT. **던지지 않는다** — `SignedPutOutcome` 판별 유니온. */
  putImage(request: SignedPutRequest): Promise<SignedPutOutcome>;
  /** 고아 문서 정리(best-effort). 실패해도 사용자 문구를 바꾸지 않는다. */
  deleteServerFrame(id: string): Promise<boolean>;
  /** 로컬 저장. 실패는 `null`. */
  saveLocal(input: SaveFrameInput): Promise<FrameTemplate | null>;
  setStatus(message: string): void;
  /** 저장 성공 후 전이. 실패해도 저장 결과에 영향 없음(로그만). */
  goToFrameSelect(): void;
}

export interface FrameSaveRequest {
  readonly role: UserRole | null;
  readonly userId: string | null;
  readonly sessionSource: FrameSessionSource;
  readonly name: string;
  readonly sourceName: string;
  readonly slots: readonly Slot[];
  readonly imageSize: ImageSize;
  /** PNG 바이트. `null`이면 ③에서 막힌다. */
  readonly png: Blob | null;
  readonly registerToServer: boolean;
}

export type FrameSaveOutcome =
  | { readonly status: "saved"; readonly registeredToServer: boolean }
  | { readonly status: "rejected"; readonly reason: FrameSaveRejection }
  | { readonly status: "server-failed"; readonly detail: string }
  | { readonly status: "local-failed" };

export async function runFrameSave(
  deps: FrameSaveDeps,
  request: FrameSaveRequest,
): Promise<FrameSaveOutcome>;
```

**본체 순서(규격)**

```
① 검증 재실행 ── 진입점이 2개이므로 **여기서 다시 한다**(fail-closed).
     existingNames = await deps.scopeNames()      // 실패 → []
     personalCount = await deps.personalCount()   // 실패 → 0
     v = validateFrameSave({...})
     if (!v.ok) → setStatus(문구); return {status:"rejected", reason}

② 지역 확정: scope = frameSaveScope(role) · png(비null 확정) · isNew = source === "New"
   ⚠️ `requiresServerRegisterPrompt(role, source)`를 **다시 부른다** — 등록 분기의 조건은
      오버레이 노출 조건과 같은 함수여야 한다(FR-11).
   register = requiresServerRegisterPrompt(role, source) && request.registerToServer

③ setStatus(STRINGS.frameEditor.saving)

④ if (register):
     a. `_` 하드 거부:  validateFrameNameForServer(name)  ── 실패 → server-failed(문구는
        `frames.nameUnderscoreRejected`). 서버가 400을 줄 값을 보내지 않는다(왕복 낭비·성공 오인 방지).
     b. created = await deps.createServerFrame({ name, imageSize, slots, ext:"png", contentType:"image/png" })
        - 예외/`created.frame === null` → **원자성**: 로컬 저장하지 않고 편집 세션 유지.
          setStatus(registerFailed(사유)); return {status:"server-failed"}
     c. if (created.putUrl === null) → 문서만 생기고 이미지가 없다 = 실패로 본다.
        deps.deleteServerFrame(created.frame.id) (best-effort) → server-failed
     d. put = await deps.putImage({ url: created.putUrl, body: png,
                                    headers: created.requiredHeaders })   // M14: 순회해 전부 부착
        - `put.ok === false` → **원자성**: deps.deleteServerFrame(created.frame.id) (best-effort)
          → setStatus(registerFailed(...)); return {status:"server-failed"}
     e. dbId = created.frame.id

⑤ saved = await deps.saveLocal({
       scope: scope === "public" ? "public" : "user",
       ownerId: scope === "public" ? null : userId,
       name, dbId: register ? dbId : null,
       imageSize, slots, bytes: png })
   - null → setStatus(saveLocalFailed); return {status:"local-failed"}
     ⚠️ 이때 서버 문서는 이미 만들어졌다(register 경로). **정리하지 않는다** —
        §4 ⑤⑥⑧ 선검증이 로컬 실패 사유를 사전에 제거했으므로 여기 도달은 저장소 장애이고,
        그 상황에서 네트워크 정리까지 시도하면 사용자에게 두 개의 실패를 겹쳐 보여준다.
        로그(`logger.error`)로 남기고 문구는 하나만 낸다.

⑥ setStatus(""); deps.goToFrameSelect(); return {status:"saved", registeredToServer: register}
```

**왜 ④c·④d에서 서버 문서를 지우는가**: 이미지 없는 문서를 남기면 `GET /frames/default`가 그 프레임을 계속
내려주고, **모든 키오스크에서 영구히 "불러올 수 없음" 카드**가 된다(Step 14의 `unavailable` 경로).
`analysis/31 §4.12`가 "2단계 실패 시 `DELETE /frames/{id}`로 정리한다"고 규정한 그 정리다.
정리 실패는 **로그만** 남기고 사용자 문구를 바꾸지 않는다(사용자가 할 조치가 없다).

**역할별 최종 결과**(03 §11.6과 1:1)

| 역할 | 세션 | 체크 | 서버 | 로컬 저장 | 결과 id |
|------|------|------|------|-----------|---------|
| manager/admin | New | on | `POST /frames` + 이미지 PUT | `scope:"public"`, `dbId=서버 id` | 서버 문서 id → 출처 `DbDefault` |
| manager/admin | New | off | 없음 | `scope:"public"`, `dbId=null` | `local:public:{이름}` → 출처 `UserLocal` |
| manager/admin | ForkFromCatalog / EditOwnLocal | (오버레이 없음) | 없음 | `scope:"public"`, `dbId=null` | 동상 |
| advanced_user | 전부 | (오버레이 없음) | 없음 | `scope:"user"`, `ownerId=본인` | `local:user:{uid}:{이름}` |
| user·temp_user·게스트 | — | — | — | **도달 불가**(①②에서 거부) | — |

---

## 6. 서버 등록 확인 오버레이 — 상태 머신

### 6.1 형태

**화면 로컬 오버레이**다(셸 모달 아님 — 03 §790). `FrameEditor`의 오버레이는 **상호배타 단일 필드**로 관리한다.

```ts
type EditorOverlay = "none" | "picker" | "serverRegister";
```

boolean 2개를 두지 않는 이유: 두 오버레이가 동시에 뜨는 상태가 타입 수준에서 표현 불가능해진다(03 §790 요구).

### 6.2 상태 머신

```
       ┌──────────────────────────────────────────────┐
       │                  none                        │
       └──┬───────────────────────────────┬───────────┘
          │ [저장] & requiresServerRegisterPrompt      │ [기존 프레임 불러오기]
          │ (검증 통과 후에만)                          │ (New 세션 & 이미지 유무 무관)
          ▼                                           ▼
   ┌──────────────────┐                        ┌──────────────┐
   │ serverRegister   │                        │   picker     │
   │ registerToServer │                        │ (§7)         │
   │  = true (리셋)    │                        └──────────────┘
   └──┬───────┬───────┘
      │       │
      │       └─ [취소] ─► none, registerToServer = true 로 리셋. **저장·전환·저장소 무변경**
      │
      └─ [저장] ─► ① const alsoServer = registerToServer        ★ 닫기 전에 확정
                   ② overlay = "none"; registerToServer = true  (리셋)
                   ③ runFrameSave({ registerToServer: alsoServer })
```

### 6.3 규격 준수 체크리스트

| 규격(03 §11.4 / §15.7) | 구현 |
|------|------|
| 노출 조건 = 등록 분기와 **완전히 같은 축** | `requiresServerRegisterPrompt(role, source)` **한 함수**를 두 곳에서 부른다(FR-11이 호출 2건을 고정) |
| 체크박스 **기본 on** + **열 때마다 리셋** | 오버레이를 여는 액션이 `registerToServer = DEFAULT_REGISTER_TO_SERVER(=true)`를 **먼저** 대입한다 |
| 체크 상태를 **닫기 전에 확정** | ①이 ②보다 먼저(위 도식). 순서를 뒤집으면 선택이 조용히 무시된다 |
| 캡션 **고정 문구** | 체크 상태와 무관한 상수 문자열(컨버터·분기 없음) |
| [취소]는 아무것도 바꾸지 않는다 | 상태 2개 대입만. `runFrameSave` 미호출 |
| **원자성** | §5.2 ④b·④c·④d — 서버 실패 시 `saveLocal`에 **도달하지 않는다** |
| `_` 이름 | 오버레이 안에 경고 문구 노출(체크 on + 이름에 `_`) + `runFrameSave` ④a에서 하드 거부 |

`DEFAULT_REGISTER_TO_SERVER = true`는 `domain/frames/frameSavePolicy.ts`의 상수로 둔다(뒤집을 때 함께
움직이는 기대값이 테스트에 명시되도록 — Windows D4의 교훈).

> **삭제 오버레이는 기본 off, 등록 오버레이는 기본 on.** 관례 위반이 아니라 **축이 다르다**
> (삭제 = 파괴적 opt-in, 생성 = 배포가 통상 목적). 두 값을 "일관성"을 이유로 맞추지 마라.

---

## 7. 기존 프레임 불러오기 피커 (03 §11.5 · §15.4)

### 7.1 노출·후보

| 항목 | 규격 | 구현 |
|------|------|------|
| 노출 | **생성 모드 전용** | `sessionSource === "New"` — 세션 축을 그대로 쓴다 |
| 형태 | 앱 내부 **썸네일 그리드**(파일 탐색기 아님) | 화면 로컬 오버레이 + `createFrameThumbnail`(Step 14 재사용) |
| 후보 | 공용 전체 + 본인 개인. 번들·fallback **복사 허용**. **역할 필터 없음** | `frameCatalog.loadPublic()` + `loadPersonal(userId)` — `FrameSelect`와 **같은 소스** |
| 적용 | 이미지 **읽기만**, 슬롯은 `현재 폭 / 원본 폭` 배율 보정 | §7.3 |
| 임시 파일 | **만들지 않는다** | OPFS 쓰기는 저장 1회뿐(§5.2 ⑤가 유일한 쓰기) |
| 세션 정체성 | **신규 생성**(사본 아님) | 세션 축을 **건드리지 않는다** |
| 이름 | **자동 제안 없음** | `name` 상태를 건드리지 않는다 |
| 원본 캡션 | 이름 입력 위 | `pickedSourceNotice` |
| [취소] | 모달만 닫고 무변경 + **목록 로딩 취소** | `abort()` 후 `overlay="none"` |

### 7.2 목록 로더 — `screens/frameEditor/framePickerRunner.ts`

Step 14의 `runFrameLoad`를 **재사용하지 않는다**(그 함수의 patch 형태·국면 문구는 `FrameSelect` 전용이다).
대신 **같은 구조**를 축소해 쓴다: 단일 비행 합류 + 호출자별 취소 + 상한 + `finally` 무조건 확정.

```ts
export type FramePickerPhase = "loading" | "ready" | "failed";

export interface FramePickerPatch {
  readonly phase?: FramePickerPhase;
  readonly frames?: readonly FrameTemplate[];
  readonly notice?: string;
  readonly selectedId?: string | null;
}

export interface FramePickerDeps {
  loadPublic(options: FrameCatalogLoadOptions): Promise<FrameCatalogResult>;
  loadLocalOnly(): Promise<FrameCatalogResult>;
  loadPersonal(userId: string): Promise<readonly FrameTemplate[]>;
  currentUserId(): string | null;
  isStale(): boolean;
  apply(patch: FramePickerPatch): void;
  /** `defaultLoadDeadline`(Step 14 모듈)을 훅이 주입한다 — 러너는 브라우저 타이머를 모른다. */
  createDeadline(abort: () => void): LoadDeadline;
  registerAbort(abort: () => void): void;
}

export async function runFramePickerLoad(deps: FramePickerDeps): Promise<void>;
```

동작:

```
apply({phase:"loading", frames:[], notice:"", selectedId:null})
controller = new AbortController(); registerAbort(() => controller.abort())
deadline = createDeadline(() => controller.abort()); deadline.arm()
try {
  result = await loadPublic({signal})            // 실패·취소 → loadLocalOnly() 폴백(빈 결과까지 축퇴)
  merged  = [...result.frames]
  if (userId) merged.push(...await loadPersonal(userId))   // 개별 try — 개인 실패가 공용을 무너뜨리지 않는다
  frames  = merged.filter(hasUsableImage)
  apply({frames, selectedId: null})               // ⚠️ 자동 선택하지 않는다(§7.4)
} finally {
  deadline.dispose()
  if (!isStale()) apply({ phase: frames.length > 0 ? "ready" : "failed",
                          notice: frames.length > 0 ? "" : pickerEmpty|pickerFailed })
}
```

- **`finally`가 무조건 국면을 확정**한다 → `loading` 고착이 구조적으로 불가능하다(Step 14와 같은 형태).
- **취소는 호출자별**이다. 오버레이를 닫아도 공유 작업은 계속 진행해 캐시를 완성한다.
- 상한을 붙이는 이유: `[취소]`가 항상 있긴 하지만, 서버 무응답에서 100초 스피너를 보여주는 대신
  30초/60초에 로컬 목록으로 마감하는 편이 `FrameSelect`와 일관된다(§18 이탈 ④).
- 진행 문구(`(n/m)`)는 **표시하지 않는다** — 피커는 보조 경로이고 스피너 하나면 충분하다.

### 7.3 적용 — `ApplyPickedFrame`의 웹판

```
① 이미지 재인코딩: loadFrameImageFromUrl(src.imageUrl)
     - 번들 프레임은 .jpg일 수 있고 크기도 4000을 넘을 수 있다 → **반드시 로더를 경유**한다.
     - 실패 → status = pickedImageMissing, 오버레이만 닫고 **편집기 상태는 무변경**
② 슬롯 보정:
     factor = src.imageSize.width > 0 ? loaded.width / src.imageSize.width : 0
     if (src.slots.length > 0 && factor > 0)
        slotCount = clamp(src.slots.length, 1, 6)
        baseSlots = rescaleSlots(src.slots, factor, loaded.width, loaded.height)
        scalePercent = 100 → slots = scaleSlots(baseSlots, 1, w, h)
     else
        baseSlots = autoArrange(slotCount, w, h, slotAspectToRatio(aspect))   // 메타 없음 → 자동 배치
③ 세션 축 **불변**(New 유지) · sourceName = src.name
④ pickedSourceNotice = "'{src.name}'의 이미지·슬롯을 불러왔습니다. 새 프레임 이름을 입력해 주세요."
⑤ name **건드리지 않음** · status = ""
```

`rescaleSlots`(신규, `slotLayout.ts`):

```ts
/**
 * 원본 이미지 크기 → 현재 프레임 크기 배율로 슬롯 값을 복사 보정한다.
 * Windows `FrameEditorViewModel.ApplyPickedFrame`(:396-415)의 인라인 계산을 순수 함수로 옮긴 것이다.
 *
 * ⚠️ `scaleSlots`와 **다르다**: 저쪽은 중심 유지 일괄 스케일(사용자 배율)이고 이쪽은 좌표계 환산이다.
 * ⚠️ 반올림은 `roundHalfToEven`이다 — C# `(int)Math.Round(x)`의 기본이 MidpointRounding.ToEven이라
 *    Windows와 픽셀이 갈라지지 않게 맞춘다(04 §9).
 */
export function rescaleSlots(
  slots: readonly Slot[],
  factor: number,
  frameW: number,
  frameH: number,
): Slot[] {
  return slots.map((s) =>
    clampToFrame(
      {
        index: s.index,
        x: roundHalfToEven(s.x * factor),
        y: roundHalfToEven(s.y * factor),
        width: Math.max(1, roundHalfToEven(s.width * factor)),
        height: Math.max(1, roundHalfToEven(s.height * factor)),
      },
      frameW,
      frameH,
    ),
  );
}
```

> Windows에는 대응 순수 함수가 없어(VM 인라인) **벡터 파일을 만들지 않는다.** 웹 테스트가 같은 입력·기대값을
> `// ↔ FrameEditorViewModel.cs:396-415` 주석과 함께 고정한다(`resultNaming` 선례).

### 7.4 자동 선택을 하지 않는 이유

`FrameSelect`는 첫 항목을 자동 선택한다(바로 [다음]을 눌러야 하므로). 피커는 **적용이 파괴적**이다
(현재 편집 중인 이미지·슬롯을 덮어쓴다) → 기본 선택을 두면 [불러오기]를 실수로 눌렀을 때 작업이 날아간다.
[불러오기] 버튼은 `selectedId !== null`일 때만 활성한다(Windows `HasSelection`과 동형).

---

## 8. 편집기 기하 — 하나의 변환 (WYSIWYG)

### 8.1 표시 구조: `<img>` + DOM 슬롯 (canvas 아님)

```
<div class="stage" ref={stageRef}>          ← ResizeObserver로 실제 렌더 크기를 잰다
  <img class="frameImage" src={previewUrl}  ← left/top/width/height = 변환 결과
       style={{ left: t.originX, top: t.originY,
                width: t.displayWidth, height: t.displayHeight }} alt="" />
  {slots.map(s => (
    <button class="slot" style={slotBoxStyle(t, s)} … />   ← 같은 변환
  ))}
</div>
```

```ts
const rect = stage.getBoundingClientRect();          // ★ 선언 크기 금지(03 §11.7)
const t = computeEditorTransform(rect.width, rect.height, frameWidth, frameHeight);

function slotBoxStyle(t: EditorTransform, s: Slot): CSSProperties {
  const p = frameToCanvas(t, s.x, s.y);
  return { left: p.x, top: p.y, width: s.width * t.scale, height: s.height * t.scale };
}
```

**하나의 변환이라는 성질이 구조적으로 보장된다**: 이미지 위치·슬롯 박스·포인터 역변환·클램프가 전부
같은 `t` 객체를 쓰고, `t`를 만드는 곳은 `useFrameEditor`의 `useState<EditorTransform>` **한 곳**이다.

### 8.2 왜 canvas가 아닌가 (설계 이탈 ②)

03 §11.7은 "캔버스 크기는 실제 렌더 크기(`getBoundingClientRect()` × `devicePixelRatio`)"라고 canvas 구현을
전제로 쓰여 있다. `<img>` + DOM 슬롯을 택한 이유:

1. **좌표계가 하나다.** canvas는 백킹 스토어(device px)와 DOM 오버레이(CSS px)가 갈라져, 슬롯을 DOM으로
   그리는 순간 dpr 환산이 두 번째 변환이 된다 — 이 Step이 막아야 하는 바로 그 실패 모드다.
2. **접근성**: 슬롯이 실제 `<button>`이라 포커스·`aria-label`·키보드 이동이 공짜다. canvas는 전부 수동이다.
3. **자원 수명이 줄어든다**: `ImageBitmap`을 들고 있을 필요가 없다(`close()` 누락 위험 0 — WR8).
4. `<img>`는 브라우저가 기기 해상도로 직접 스케일하므로 dpr 처리가 애초에 불필요하다.

규격의 실질 요구("선언 크기 사용 금지, 실제 렌더 크기 측정")는 `getBoundingClientRect()`로 그대로 충족한다.

### 8.3 드래그 — 그랩 오프셋 기반 절대 위치 (델타 누적 금지)

```ts
// pointerdown (슬롯 <button> 위)
event.currentTarget.setPointerCapture(event.pointerId);
const rect = stageRef.current.getBoundingClientRect();
const p = canvasToFrame(t, event.clientX - rect.left, event.clientY - rect.top);
grabRef.current = { index, dx: p.x - slot.x, dy: p.y - slot.y, pointerId: event.pointerId };

// pointermove
const p = canvasToFrame(t, event.clientX - rect.left, event.clientY - rect.top);
dispatch({ type: "dragSlot", index,
           x: Math.round(p.x - grab.dx), y: Math.round(p.y - grab.dy) });

// pointerup / pointercancel / lostpointercapture
grabRef.current = null;   // ⚠️ 세 이벤트 **전부** 구독한다 — 하나라도 빠지면 드래그가 고착된다
```

- **매 이동마다 절대 위치를 새로 계산**한다. 포인터 델타를 누적하면 오차가 쌓인다(analysis/14 §4.5).
- 클램프는 reducer가 `clampToFrame(…, frameWidth, frameHeight)`으로 한다 — **표시와 같은 좌표계**다.
- CSS: 슬롯에 `touch-action: none`(드래그 중 스크롤 방지) + `user-select: none`.
- **키보드**: 슬롯 포커스 상태에서 방향키 = 1px, `Shift`+방향키 = 10px 이동(같은 `dragSlot` 액션 재사용).
  터치·마우스가 없는 접근 경로를 남긴다.

### 8.4 드래그 후 `baseSlots` 동기화 (Windows `UpdateSlot`과 동일)

```ts
// reducer: dragSlot
const clamped = clampToFrame({ index, x, y, width: prev.width, height: prev.height }, fw, fh);
slots[index] = clamped;

// 스케일 기준 슬롯도 **중심을 맞춰** 갱신한다(원본 크기 유지).
// 하지 않으면 드래그 뒤 배율 슬라이더를 건드리는 순간 슬롯이 원래 자리로 튄다.
const b = baseSlots[index];
const cx = clamped.x + clamped.width / 2;
const cy = clamped.y + clamped.height / 2;
baseSlots[index] = clampToFrame(
  { index: b.index, x: Math.round(cx - b.width / 2), y: Math.round(cy - b.height / 2),
    width: b.width, height: b.height }, fw, fh);
```

### 8.5 슬롯 개수·종횡비·배율

| 조작 | 동작 |
|------|------|
| 슬롯 개수 1~6 | `baseSlots = autoArrange(n, fw, fh, slotAspectToRatio(aspect))` → `applyScale()` |
| 종횡비 4:3 / 3:4 / 1:1 | 동상(재배치) |
| 배율 슬라이더 | `slots = scaleSlots(baseSlots, pct/100, fw, fh)` — **항상 `baseSlots`에서** 계산(누적 오차 방지) |
| 이미지 교체(파일) | 크기 갱신 후 `autoArrange` |
| 피커 적용 | `autoArrange`를 **하지 않고** `rescaleSlots` 결과로 `baseSlots`를 교체(§7.3) |
| [선택 편집] 진입 | `autoArrange`를 **하지 않고** `frame.slots`를 그대로 `baseSlots`로(§9.3) |

> Windows는 `_suppressArrange` 플래그로 "값 대입이 자동 배치를 유발하는" 문제를 막는다. 웹 reducer는
> 자동 배치를 **액션 안에서 명시적으로** 하므로 그런 플래그가 필요 없다 — 억제 플래그를 만들지 마라.

**배율 범위**: `MIN_SCALE_PERCENT = 10`, `MAX_SCALE_PERCENT = 300`(Windows `FrameEditorViewModel.MinScale/MaxScale`와 동일 — 03 §11.2 · analysis/13 §6.2 · 14 §4.2).
규격 문서에 한동안 남아 있던 70~130은 커밋 `0a93b59`("슬롯 스케일 10~300%·직접입력")가 넓히기 전의 **폐기된 초기 설계값**이었다. 진실원 우선순위(**소스 > docs/analysis > docs/design**, `docs/design/README.md §4`)에 따라 2026-08-01에 문서 6곳을 소스에 맞췄다 — **되돌리지 마라**(V24-6 해소·§22).

---

## 9. 이미지 로드 · 재인코딩

### 9.1 도메인 — `domain/frames/frameImagePolicy.ts` (신규, `FrameImageValidator.cs` 이식)

```ts
export const MAX_FRAME_IMAGE_BYTES = 10 * 1024 * 1024;      // 10MB
export const MAX_FRAME_IMAGE_LONG_SIDE = 4000;
export const SUPPORTED_FRAME_IMAGE_EXTENSIONS = [".png", ".jpg", ".jpeg"] as const;
export const SUPPORTED_FRAME_IMAGE_MIME_TYPES = ["image/png", "image/jpeg"] as const;

export function isFrameImageSizeWithinLimit(byteLength: number): boolean;
/** 장변 4000 초과 시 축소 배율(1 = 축소 불필요). */
export function frameImageResizeFactor(width: number, height: number): number;
/** 축소 후 크기. `roundHalfToEven`(C# `Math.Round` 대응). */
export function scaledFrameImageSize(width: number, height: number): ImageSize;
/**
 * 지원 형식 판정. **MIME이 있으면 MIME이 우선**이고, 비어 있으면(일부 안드로이드 파일 선택기)
 * 파일명 확장자로 판정한다. Windows는 확장자만 보지만 웹은 `File.type`이 더 신뢰할 수 있다.
 */
export function isSupportedFrameImage(mimeType: string, fileName: string): boolean;
```

Windows 기대값(`SlotLayoutTests.cs:256-287`)을 그대로 옮긴 테스트를 쓴다 — 벡터 파일은 만들지 않고
`// ↔ SlotLayoutTests.cs:256` 주석으로 짝을 명시한다(`resultNaming` 선례).

| 케이스 | 기대 |
|--------|------|
| `isFrameImageSizeWithinLimit(5_000_000)` | `true` |
| `isFrameImageSizeWithinLimit(11_000_000)` | `false` |
| `scaledFrameImageSize(8000, 4000)` | `{4000, 2000}` |
| `scaledFrameImageSize(3000, 2000)` | `{3000, 2000}`(무변경) |
| `isSupportedFrameImage("", "a.PNG"/"a.JPG"/"a.jpeg")` | `true` |
| `isSupportedFrameImage("", "a.gif"/"a.bmp")` | `false` |
| `isSupportedFrameImage("image/gif", "a.png")` | `false`(MIME 우선) |

### 9.2 어댑터 — `adapters/frames/frameImageLoader.ts` (신규)

```ts
export interface LoadedFrameImage {
  readonly blob: Blob;      // **항상 PNG**
  readonly width: number;
  readonly height: number;
}
export type FrameImageFailure =
  | "unsupported-type" | "too-large" | "decode-failed" | "encode-failed" | "fetch-failed";
export type FrameImageOutcome =
  | { readonly ok: true; readonly image: LoadedFrameImage }
  | { readonly ok: false; readonly failure: FrameImageFailure };

/** `<input type="file">` 경로. */
export function loadFrameImageFromFile(file: File): Promise<FrameImageOutcome>;
/** 피커 경로(앱 내부 URL). 원본을 **읽기만** 한다. */
export function loadFrameImageFromUrl(url: string): Promise<FrameImageOutcome>;
```

절차(둘 다 같은 코어를 지난다):

```
① 형식·용량 검사(파일 경로만) → unsupported-type / too-large
② fetch (URL 경로) — /^https?:/i.test(url) ? {mode:"cors", cache:"force-cache"} : {}
   ⚠️ `compositor.loadFrameImage`와 **같은 분기**를 쓴다(WM2·FR-6). 현재 피커 후보에 원격 URL이
      들어올 경로는 없지만(F-10), 규약을 복제해 두면 나중에 생겨도 canvas가 오염되지 않는다.
③ bitmap = await createImageBitmap(blob, { imageOrientation: "from-image" })
   ⚠️ EXIF 회전 JPG가 옆으로 눕는 것을 막는다. 실패 → decode-failed
④ target = scaledFrameImageSize(bitmap.width, bitmap.height)
⑤ canvas.drawImage(bitmap, 0, 0, target.width, target.height)
⑥ png = await toPngBlob(canvas)      // 축소가 없어도 **항상 재인코딩**한다(규격: 저장 포맷은 PNG)
⑦ finally { bitmap.close() }         // WR8 — GC 대상이 아니다
```

`toPngBlob`: `OffscreenCanvas.convertToBlob({type:"image/png"})`를 먼저 시도하고, 없거나 던지면
`document.createElement("canvas").toBlob(cb, "image/png")`로 폴백한다(A15-1). 둘 다 실패 → `encode-failed`.

**어댑터 규약**: 예외를 전파하지 않는다. 모든 실패가 위 유니온이다.

### 9.3 [선택 편집] 진입은 **재인코딩하지 않는다** (중요)

```
편집 진입(kind:"edit"):
  bytes  = await fetch(frame.imageUrl).blob()      ← 바이트 **그대로** 보존
  width  = frame.imageSize.width  || (디코드해서 얻는다)
  height = frame.imageSize.height || (디코드해서 얻는다)
  baseSlots = frame.slots (값 복사)
  slotCount = clamp(frame.slots.length, 1, 6)
  scalePercent = 100
  실패 → status = editImageMissing, 저장 비활성(hasImage=false)
```

⚠️ **여기서 `loadFrameImageFromUrl`을 쓰면 안 된다.** 재인코딩 경로는 장변 4000 축소를 적용하므로
`frame.slots`의 좌표계와 이미지 크기가 어긋나 **기존 슬롯이 전부 밀린다.** Windows `LoadForEdit`도
`LoadImage`를 경유하지 않고 파일을 그대로 읽는다(`FrameEditorViewModel.cs:244`) — 같은 이유다.

### 9.4 미리보기 URL의 단일 소유자 — `screens/frameEditor/previewUrl.ts`

```ts
export interface PreviewUrlHolder {
  /** 이전 URL을 **먼저 해제**하고 새 URL을 만든다. 빈 문자열이면 해제만 한다. */
  set(blob: Blob | null): string;
  current(): string;
  /** 언마운트에서 반드시 부른다(멱등). */
  dispose(): void;
}
export function createPreviewUrlHolder(deps?: {
  createObjectURL?: (blob: Blob) => string;
  revokeObjectURL?: (url: string) => void;
}): PreviewUrlHolder;
```

- **`frameImageCache`가 아니다.** 저쪽은 저장된 프레임의 URL 소유자이고 해제 시점이 "프레임 삭제"뿐이다.
  편집기 미리보기는 세션 자원이라 **편집기가 직접 해제**해야 한다(F-11).
- 주입 가능한 이유는 node에서 **"만든 수 == 해제한 수"** 를 단위 테스트로 고정하기 위함이다(누수 0 증명).
- 훅의 언마운트 cleanup에서 `dispose()`. ⚠️ `<StrictMode>` 이중 effect에서도 안전하다: 1회차 언마운트가
  1회차 URL만 해제하고 2회차 마운트는 새 홀더를 만들어 이미지 로드를 다시 수행한다.

---

## 10. 저장소 계층 변경 (`adapters/storage/frameStore.ts`)

### 10.1 `scopeFrameNames` 추가 — ⑦·fork 이름 제안용

```ts
export interface FrameStore {
  /* … 기존 7개 … */
  /**
   * 저장 스코프의 기존 이름들. **메타만 읽는다**(OPFS 존재 확인·object URL 생성 없음).
   * ⚠️ `listPublic()`로 대신하지 마라: ① 목록 조회는 이미지가 없는 레코드를 **건너뛰지만**,
   *    저장 키는 그 레코드가 여전히 점유하고 있어 덮어쓰기가 일어난다 — 가드가 뚫린다.
   *    ② 이름 하나 보려고 프레임 전체의 object URL을 만들 이유가 없다.
   * 실패는 **빈 배열**이다(⑦이 조용히 꺼진다 — 03 §11.3 규격).
   */
  scopeFrameNames(scope: FrameScope, ownerId: string | null): Promise<readonly string[]>;
}
```

구현: `meta.all()` → `scope` 일치 + (`scope==="user"`면 `ownerId` 일치) → `record.name` 수집.

### 10.2 `saveLocal` — 같은 키를 덮어쓸 때 이전 이미지를 지운다 (F-5 수정)

```
현재: 새 토큰으로 PNG 쓰기 → meta.put(같은 key) → **이전 imageFile이 참조를 잃는다(고아)**
수정: ① 저장 전 previous = records.find(r => r.key === key)
      ② 새 PNG 쓰기 → meta.put
      ③ previous !== null && previous.imageFile !== 새 imageFile 이면
           await opfs.remove(previous.imageFile);  release(previous.imageFile);
         실패는 `logger.warn`만(고아 1개 < 저장 실패). ★ 순서가 규격: **새 레코드를 기록한 뒤** 지운다.
           반대로 하면 쓰기 실패 시 이미지 없는 프레임이 된다.
```

`release`는 기존 `frameImageCache.revokeFrameImage`다 — 옛 경로의 object URL을 놓아준다.
`cacheServerFrame`도 같은 `persist()`를 지나므로 서버 프레임 재캐시의 고아도 함께 사라진다(부수 이득).

### 10.3 변경하지 않는 것

- `deleteLocal`의 "이미 없으면 성공" 판정(설계 이탈 ④) — 그대로.
- `LOCAL_FRAME_LIMIT`·`exceedsLocalFrameLimit` — 그대로. 상한은 **개인 스코프에만** 적용한다
  (05 §4.8: `scope:"user"`·`ownerId` 기준. 서버도 `userId`가 있을 때만 검사한다 — analysis/31 §4.12).

---

## 11. HTTP 계층 변경 (`adapters/http/frameRepository.ts`)

### 11.1 `createFrame` 응답 봉투 정정 (F-4 — 기존 결함)

```ts
/** analysis/31 §4.12 — 201 { frame, upload: { putUrl, downloadUrl, requiredHeaders } } */
async createFrame(request) {
  const raw = await client.request<unknown>({
    method: "POST", path: "frames", body: request, auth: "required",
  });
  const record = asRecord(raw);
  // ★ 봉투는 `upload`다. 최상위에서 읽으면 **항상 null**이라 이미지 PUT이 조용히 생략되고
  //   서버에는 이미지 없는 문서만 남는다(모든 키오스크에서 영구 "불러올 수 없음" 카드).
  const upload = asRecord(record.upload);
  const headers = upload.requiredHeaders;
  return {
    frame: parseFrame(record.frame),
    putUrl: typeof upload.putUrl === "string" ? upload.putUrl : null,
    // 응답 객체를 **그대로 보존**한다 — 키를 골라 담으면 M14가 깨진다.
    requiredHeaders:
      typeof headers === "object" && headers !== null ? (headers as Record<string, string>) : {},
  };
}
```

- `parseFrame(record.frame ?? raw)`의 `?? raw` 폴백도 제거한다(계약이 `{frame, upload}`로 확정됐다).
- `CreateFrameRequest.ext`·`contentType`은 서버가 무시하지만(항상 PNG 강제) **그대로 둔다** — 필드를 지우면
  `PUT /frames/{id}`와 DTO를 공유하는 서버 쪽 형태와 어긋나고, 얻는 것이 없다.

### 11.2 `PUT /frames/{id}`를 만들지 않는다 (불변)

기존 파일 상단 경고를 유지하고, **정적 검사 FR-9**를 새로 건다:
`adapters/http/frameRepository.ts` 소스에 `method: "PUT"`·`replaceImage`·`updateFrame`이 **0건**.

### 11.3 이미지 PUT은 `uploadGateway.put`을 재사용한다

`uploadGateway`는 "업로드 3단계"의 소유자지만 `put()` 자체는 **범용 서명 PUT**이다(XHR·진행률·
`requiredHeaders` 전량 순회 M14·**던지지 않음**). 프레임 이미지 PUT을 위해 두 번째 구현을 만들지 않는다.

```ts
// ⚠️ 싱글턴 getter가 없다 — Step 11의 `uploadRunner.ts:346`처럼 `createUploadGateway()`로 만든다.
//    상태가 없는 팩토리라 호출마다 새로 만들어도 무해하고, 훅이 deps로 주입해 node 테스트가 가능해진다.
const outcome = await deps.putImage({
  url: created.putUrl,
  body: png,
  headers: created.requiredHeaders,   // 순회해 전부 붙는다(M14)
  // kind는 붙이지 않는다 — 로그 라벨이 "final"/"timelapse" 전용이다.
});
```

> ⚠️ `SIGNED_PUT_TIMEOUT_MS`(100초)를 그대로 쓴다. 프레임 PNG는 최대 10MB이고 결과 이미지보다 크지 않다.

---

## 12. 삭제 통합 — **중복 구현 금지**

### 12.1 결론: 새로 만들지 않는다

Step 14가 규격(03 §15.5 · analysis/13 §6.6)을 **전부** 구현했다: 순서 6단, 결과 4문구, `{deleted:false}`는
성공이 아님, 이름 매칭 재시도, 실제 부재 확인, 조용한 재스캔, 체크박스 기본 off + 열 때마다 리셋,
체크 상태를 닫기 전에 확정, `canDeleteFrame` **2인자**.

**Step 15가 하는 일은 셋뿐이다:**

1. `screens/modals/confirmDelete/*`를 **만들지 않는다**(FR-5의 디렉터리 부재 단언을 그대로 통과시킨다).
2. `shell/shellStore.ts`의 `ModalId`에서 **`"framePicker"`·`"confirmDelete"`를 제거**한다 —
   두 오버레이 모두 화면 로컬로 확정됐으므로 셸 모달 식별자로 남으면 "나중에 누군가 배선하는" 경로가 된다.
3. `FrameEditor`의 두 오버레이가 삭제 오버레이와 **같은 마크업 규약**을 쓰도록 `OverlayDialog`를 신설한다(§14.2).

### 12.2 `FrameSelectView`의 삭제 오버레이는 손대지 않는다

`OverlayDialog`로 이관하고 싶어지지만 **비목표**다. 이유: FR-5가 그 파일을 문자열로 감시하고 있고,
Step 14가 방금 검증을 끝낸 코드다. 마크업 통일은 Step 17 이후의 정리 대상으로 남긴다.

### 12.3 편집기에서의 삭제

**없다.** 삭제 진입점은 `FrameSelect` 카드의 ✕ 하나다(03 §4). 편집기에 [이 프레임 삭제]를 추가하지 마라.

---

## 13. 인계 채널 — `shell/frameEditorIntent.ts` (신규)

`useFrameSelect.ts`의 `TODO(Step 15)` 두 곳이 이 자리다.

```ts
export type FrameEditorIntent =
  | { readonly kind: "new" }
  | { readonly kind: "edit"; readonly frame: FrameTemplate };

const NEW_INTENT: FrameEditorIntent = { kind: "new" };
let pending: FrameEditorIntent = NEW_INTENT;

/** `go("FrameEditor")` **직전에** 부른다. */
export function setFrameEditorIntent(intent: FrameEditorIntent): void { pending = intent; }

/**
 * ⚠️ **비파괴 읽기**다. 소비형(consume)으로 만들면 `<StrictMode>`의 이중 마운트에서 2회차가
 *    `new`로 떨어져 편집 세션이 조용히 신규 생성으로 바뀐다(Step 12·13에서 같은 함정을 밟았다).
 */
export function readFrameEditorIntent(): FrameEditorIntent { return pending; }

/** 편집기를 떠날 때(저장 성공·취소·홈 복귀) 부른다. 다음 진입의 기본값은 `new`다. */
export function clearFrameEditorIntent(): void { pending = NEW_INTENT; }
```

배선:

| 호출자 | 시점 |
|--------|------|
| `useFrameSelect.createFrame()` | `setFrameEditorIntent({kind:"new"})` → `go("FrameEditor")` |
| `useFrameSelect.editSelected()` | `setFrameEditorIntent({kind:"edit", frame: selected})` → `go("FrameEditor")` |
| `useFrameEditor` 진입 | `readFrameEditorIntent()`(비파괴) |
| `useFrameEditor` 나가기(저장 성공·[취소]) | `clearFrameEditorIntent()` → `go("FrameSelect")` |
| `main.tsx`의 `configureShell` | `returnHome` 훅에 넣지 **않는다** — `ShellHooks`는 촬영 자원 정리용이고 편집 의도는 다음 진입에서 어차피 덮어쓰인다. 대신 `App.tsx`가 화면이 `FrameEditor`가 아닐 때 렌더하지 않으므로 잔존 값이 소비되지 않는다 |

**진입 가드**(3차 게이트의 1차·2차):

```
role = currentUser?.role ?? null
if (!canWriteFrames(role))            → 편집기 본문을 렌더하지 않는다(안내 카드 + [프레임 선택으로])
if (intent.kind === "edit"
    && !canEditFrame(intent.frame, role, userId))
                                       → logger.warn + 세션을 "New"로 강등 + status 안내
                                          (타인/권한 밖 프레임의 이미지를 애초에 읽지 않는다)
```

---

## 14. 컴포넌트 트리 · props · 접근성

### 14.1 트리

```
<FrameEditorView>                                  // useFrameEditor() 1회 호출
  ├ <h1>{titleNew|titleEdit}</h1>
  ├ {showsLocalOnlyBanner && <p role="note">{frames.localOnlyBanner}</p>}   // 편집 세션 전용
  ├ <div class="layout">
  │   ├ <FrameEditorStage                          // §8.1
  │   │     transform slots previewUrl frameSize
  │   │     onSlotPointerDown onSlotPointerMove onSlotPointerUp onSlotKeyDown />
  │   └ <aside class="panel">
  │       ├ <input type="file" accept="image/png,image/jpeg" hidden/> + [이미지 불러오기]
  │       ├ {sessionSource === "New" && [기존 프레임에서 불러오기]}
  │       ├ <ChoiceGroup label="슬롯 개수" options=[1..6] />       // 값 기반(it7 B9와 같은 취지)
  │       ├ <ChoiceGroup label="슬롯 종횡비" options=[4:3,3:4,1:1] />
  │       ├ <input type="range" min=70 max=130 />  + 현재 % 표시
  │       ├ {pickedSourceNotice && <p class="muted">{pickedSourceNotice}</p>}   // 이름 **위**
  │       ├ <TextField label="프레임 이름" value={name} maxLength={100} />
  │       ├ <p class="scopeNotice">{saveScopeNotice}{underscoreWarning ? " ⚠ …" : ""}</p>
  │       └ <div class="actions"> [취소] [저장] </div>
  │     </aside>
  ├ {status && <p role="alert">{status}</p>}
  ├ {overlay === "picker"         && <FramePickerOverlay …/>}
  └ {overlay === "serverRegister" && <ServerRegisterOverlay …/>}
```

### 14.2 `ui/components/OverlayDialog.tsx` (신규)

```tsx
export interface OverlayDialogProps {
  readonly title: string;
  /** `Esc`·[취소] 공통 처리. **셸 `popModal`을 부르지 않는다**(화면 로컬 오버레이다). */
  readonly onCancel: () => void;
  /** 진입 포커스 대상의 DOM id. 파괴적 액션에 기본 포커스를 주지 않는다. */
  readonly initialFocusId: string;
  readonly children?: ReactNode;
  readonly actions?: ReactNode;
}
```

- `role="dialog" aria-modal="true" aria-label={title}` + scrim. **배경 클릭으로 닫지 않는다**(오조작 방지).
- `Esc` → `onCancel`(자체 `keydown`; 셸 `Modal`의 내장 Esc를 쓰지 않는다).
- 진입 시 `document.getElementById(initialFocusId)?.focus()`.
- 언마운트에서 `removeEventListener` — cleanup 누락 0.
- `FrameSelectView`의 삭제 오버레이와 **같은 마크업 규약**이지만 그 파일은 이관하지 않는다(§12.2).

### 14.3 접근성·반응형

| 항목 | 규격 |
|------|------|
| 슬롯 | `<button aria-label="슬롯 {n} (x, y)">` · 방향키 이동 · `touch-action: none` |
| 스테이지 | `aspect-ratio`로 높이 확보 + `ResizeObserver`. 좁은 화면(<900px)은 패널이 아래로 |
| 배율 슬라이더 | `<input type="range" aria-valuetext="{n}%">` + 숫자 표시 |
| 상태 문구 | 차단·실패는 `role="alert"`, 진행("저장 중...")은 `aria-live="polite"` |
| 버튼 | 48px 터치 타깃(기존 `Button` 규약) |
| 이미지 | `<img alt="">`(장식 — 정보는 슬롯 버튼이 갖는다) |

---

## 15. 파일별 역할과 시그니처

### 15.1 도메인 (순수 · node 테스트)

| 파일 | 상태 | 내용 |
|------|------|------|
| `domain/frames/frameSavePolicy.ts` | **신규** | `FRAME_SESSION_SOURCES`·`FrameSessionSource`·`editSessionSource`·`frameSaveScope`·`requiresServerRegisterPrompt`·`DEFAULT_REGISTER_TO_SERVER`·`saveScopeNoticeKind`·`showsLocalOnlyBanner`·`MIN_SCALE_PERCENT`·`MAX_SCALE_PERCENT`·`validateFrameSave`·`FrameSaveRejection` |
| `domain/frames/frameImagePolicy.ts` | **신규** | `MAX_FRAME_IMAGE_BYTES`·`MAX_FRAME_IMAGE_LONG_SIDE`·`isFrameImageSizeWithinLimit`·`frameImageResizeFactor`·`scaledFrameImageSize`·`isSupportedFrameImage` |
| `domain/frames/slotLayout.ts` | 수정 | `rescaleSlots` **추가만**(기존 export 무변경 — 벡터 4종이 걸려 있다) |
| `domain/index.ts` | 수정 | 신규 2모듈 재수출(평면 배럴이라 **이름 충돌 주의** — 전부 한정형 이름을 썼다) |

### 15.2 어댑터 (브라우저 격리 · 예외 미전파)

| 파일 | 상태 | 내용 |
|------|------|------|
| `adapters/frames/frameImageLoader.ts` | **신규** | `loadFrameImageFromFile`·`loadFrameImageFromUrl` → `FrameImageOutcome` |
| `adapters/storage/frameStore.ts` | 수정 | `scopeFrameNames` 추가 · `persist()`에 **이전 이미지 정리**(F-5) |
| `adapters/http/frameRepository.ts` | 수정 | `createFrame`이 `upload` 봉투를 읽는다(F-4) |
| `adapters/http/uploadGateway.ts` | 무변경 | `put()` 재사용 |
| `adapters/frames/{frameCatalog,frameImageCache,frameThumbnails}.ts` | 무변경 | 피커·썸네일이 그대로 쓴다 |

### 15.3 화면 로직 (React 무관 · node 테스트)

| 파일 | 상태 | 내용 |
|------|------|------|
| `screens/frameEditor/frameEditorState.ts` | **신규** | `FrameEditorState`·`frameEditorReducer`·`initialFrameEditorState(intentKind)`·액션 12종 |
| `screens/frameEditor/frameEditorSave.ts` | **신규** | `runFrameSave(deps, request)` — §5.2 |
| `screens/frameEditor/framePickerRunner.ts` | **신규** | `runFramePickerLoad(deps)` — §7.2 |
| `screens/frameEditor/previewUrl.ts` | **신규** | `createPreviewUrlHolder` — §9.4 |
| `screens/frameEditor/frameEditorEntry.ts` | **신규** | `runEditorEntry(deps, intent)` — 편집 진입 시 이미지·슬롯·fork 이름 제안 준비(§9.3 + §16.2) |
| `screens/frameEditor/useFrameEditor.ts` | **신규** | 얇은 훅. 판정 0, 상태 보관 + 세대 카운터 + 위 모듈 호출 |
| `screens/frameSelect/useFrameSelect.ts` | 수정 | `TODO(Step 15)` 2곳 → `setFrameEditorIntent(...)` |

### 15.4 UI · 셸 · 배선

| 파일 | 상태 | 내용 |
|------|------|------|
| `ui/views/FrameEditorView.tsx` | **신규** | 렌더 전용 |
| `ui/views/frameEditor.module.css` | **신규** | 스테이지·슬롯·패널·오버레이 |
| `ui/components/OverlayDialog.tsx` | **신규** | 화면 로컬 오버레이 공통 껍데기 |
| `ui/strings.ts` | 수정 | `frameEditor` 섹션 신설 + `frames.nameEmpty` 정정 + `frameSaveRejectionMessage(reason)` |
| `shell/frameEditorIntent.ts` | **신규** | §13 |
| `shell/shellStore.ts` | 수정 | `ModalId`에서 `"framePicker"`·`"confirmDelete"` 제거 |
| `App.tsx` | 수정 | `case "FrameEditor": return <FrameEditorView/>` + 더미 목록 주석 갱신 |
| `ui/views/SettingsView.tsx` | 수정(주석만) | `confirmDelete` 모달 언급 정정 |

---

## 16. 데이터 흐름 시나리오

### 16.1 power · 빈 편집기 → 서버 등록 (핵심 경로)

```
FrameSelect [프레임 만들기]
  → setFrameEditorIntent({kind:"new"}) → go("FrameEditor")
FrameEditor 진입: intent=new → sessionSource="New" → 배너 **없음**
[이미지 불러오기] → loadFrameImageFromFile → PNG 1200×1600 → autoArrange(4, 3:4)
드래그·배율 조정 → slots 확정
이름 "여름 6컷" 입력 → 캡션: "저장 시 '여름 6컷'을(를) 이 기기의 공용 목록에 만듭니다. 서버 등록 여부는 저장할 때 선택합니다."
[저장]
  → scopeNames() = ["봄 4컷"] · personalCount 무시(공용)
  → validateFrameSave → ok
  → requiresServerRegisterPrompt(manager,"New") = true → overlay="serverRegister", registerToServer=true
[오버레이 저장]
  → alsoServer=true 확정 → overlay="none" → registerToServer=true 리셋
  → runFrameSave: 검증 **재실행** → ok
     validateFrameNameForServer("여름 6컷") ok
     POST /frames → 201 {frame:{id:"abc"}, upload:{putUrl, requiredHeaders}}
     uploadGateway.put(putUrl, png, requiredHeaders) → 200
     saveLocal({scope:"public", ownerId:null, dbId:"abc", …}) → id="abc"(출처 DbDefault)
  → go("FrameSelect") → 목록 재로드 → 새 프레임 노출(로컬 캐시 히트 — 재다운로드 없음)
```

### 16.2 power · [선택 편집](서버 공용 프레임) → fork

```
FrameSelect에서 DbDefault 프레임 선택 → [선택 편집](canEditFrame = isPower)
  → setFrameEditorIntent({kind:"edit", frame}) → go("FrameEditor")
진입 effect(runEditorEntry, busy=true — 폼 비활성):
  sessionSource = editSessionSource(frame) = "ForkFromCatalog" · sourceName = frame.name
  names = await scopeFrameNames("public", null)
  name  = nextCopyName(frame.name, names, uniqueSuffix)      // "여름 6컷 사본"
  bytes = fetch(frame.imageUrl)  ← **재인코딩하지 않는다**
  baseSlots = frame.slots · slotCount = clamp(len,1,6) · scale=100
  busy=false
배너 **표시**(편집 세션) · 캡션: "원본은 그대로 두고 '여름 6컷 사본'(으)로 이 기기의 공용 목록에 저장됩니다."
[저장] → requiresServerRegisterPrompt(manager,"ForkFromCatalog") = **false** → 오버레이 없음
       → runFrameSave({registerToServer:false}) → saveLocal(scope:"public", dbId:null)
       → id = "local:public:여름 6컷 사본" (출처 UserLocal)
※ 이름을 원본 그대로 되돌리면 ④가 막는다("원본과 같은 이름은…"). 다른 기존 이름이면 ⑦이 막는다.
```

### 16.3 power · 피커로 불러오기 → **서버 등록 대상** (재정의의 핵심)

```
[프레임 만들기] → New 세션 → [기존 프레임에서 불러오기]
  overlay="picker" → runFramePickerLoad → 공용 + 개인 후보 그리드
프레임 선택 → [불러오기]
  loadFrameImageFromUrl(src.imageUrl) → PNG 재인코딩(+4000 축소)
  factor = 새폭 / src.imageSize.width → rescaleSlots
  sessionSource **여전히 "New"** · sourceName = src.name
  pickedSourceNotice = "'봄 4컷'의 이미지·슬롯을 불러왔습니다. 새 프레임 이름을 입력해 주세요."
  name **건드리지 않음**(사용자가 이미 타이핑한 값 보존)
[저장] → requiresServerRegisterPrompt(manager,"New") = **true** → 오버레이 표시 ★
        (종전 규격이었다면 fork 세션이라 오버레이가 뜨지 않았다 — 이것이 재정의의 실질 차이다)
※ 이름을 "봄 4컷" 그대로 두면 ④가 아니라 **⑦**이 막는다(New 세션이라 ④는 발동하지 않는다).
※ 이후 [이미지 불러오기]로 파일을 직접 넣으면 pickedSourceNotice를 **비운다**(사실과 어긋나므로).
```

### 16.4 서버 등록 실패 — 원자성

```
[오버레이 저장](체크 on) → POST /frames 500
  → deps.saveLocal **미호출** · 화면 전환 **없음** · 편집 세션(이미지·슬롯·이름·배율) 그대로
  → status = "서버 등록 실패: {사유} 이 기기에만 저장하려면 '서버에도 등록'을 해제하고 다시 저장해 주세요."
사용자가 [저장] → 오버레이 → 체크 해제 → [저장]
  → runFrameSave({registerToServer:false}) → 로컬만 저장 → 성공
  ⚠️ 만약 실패 시 로컬을 저장해 뒀다면 이 재시도가 **⑦ 자기 자신과의 충돌**로 영구히 막혔다.
```

```
POST 201 성공 → 이미지 PUT 403(서명 불일치)
  → deleteServerFrame("abc") best-effort → saveLocal **미호출** → server-failed
  (정리 실패해도 문구는 하나만. 로그에 orphanFrameId 기록)
```

### 16.5 advanced_user · 개인 저장 · 10개 상한

```
role=advanced_user → frameSaveScope = "personal" → 오버레이 절대 없음(isPower=false)
scopeNames("user", uid) = 본인 10개 · personalCount = 10
새 이름 → ⑦ 통과 → ⑧ `!collides && exceedsLocalFrameLimit(10)` → **거부**("프레임은 최대 10개까지…")
기존 이름(자기 프레임 덮어쓰기, EditOwnLocal) → ⑦ 예외 · ⑧ `collides`라 통과 → 저장 성공 ★
  (이 예외가 없으면 10개를 채운 계정은 자기 프레임 수정조차 못 한다)
```

---

## 17. Windows 구현과의 대응 관계

| Windows | 웹 | 비고 |
|---------|-----|------|
| `FrameSessionSource` enum(private) | `domain/frames/frameSavePolicy.ts`의 공개 유니온 | 순수 판정을 도메인에 두는 웹 관례 |
| `TryValidateForSave(out error)` | `validateFrameSave(input)` + 호출자의 비동기 수집 | 순서 소유자는 여전히 **한 함수** |
| `ExistingNamesForCurrentScope()` | `frameStore.scopeFrameNames(scope, ownerId)` | 실패 = 빈 집합(비차단) 동일 |
| `RequiresServerRegisterPrompt` | `requiresServerRegisterPrompt(role, source)` | 호출 2곳 고정(FR-11) |
| `IsServerRegisterConfirmVisible` + `RegisterToServer` | `overlay:"serverRegister"` + `registerToServer` | boolean 2개 → 상호배타 단일 필드(03 §790) |
| `PersistAsync(registerToServer)` | `runFrameSave(deps, request)` | 첫 줄 재검증 동일 |
| `_repository.SaveAsync(frame, png)` | `POST /frames` + **서명 PUT 2단계** | 웹은 이미지가 별 요청이다(§11.3) |
| `LoadImage(path)` (OpenCV → PNG) | `loadFrameImageFromFile` (createImageBitmap → canvas PNG) | 10MB·4000·PNG 재인코딩 동일 |
| `LoadForEdit(frame)` (파일 그대로) | `runEditorEntry` (`fetch` 그대로) | **재인코딩 금지** 동일(§9.3) |
| `ApplyPickedFrame(src)` | 피커 적용 + `rescaleSlots` | 세션 축 불변 동일 |
| `UpdateSlot(...)` + `_baseSlots` 중심 갱신 | reducer `dragSlot` | §8.4 |
| `_suppressArrange` | **없음** | 자동 배치를 액션 안에서 명시 호출(§8.5) |
| `FramePickerViewModel` | `framePickerRunner` + 오버레이 컴포넌트 | 이벤트 0개·확인/취소는 소유자가 갖는 구조 동일 |
| `canEditFrame`이 power의 공용 로컬 프레임을 편집 불가로 판정 | **동일**(F-7) | 고치지 마라 — FR-2가 삭제 축을 고정하고 있고 Windows와 같은 동작이다 |
| `MinScale=10 / MaxScale=300` | `10 / 300`(동일) | 해소됨(2026-08-01) — 규격 문서에 남아 있던 70~130은 폐기값이었다. 소스가 진실원이라 문서 6곳을 갱신, 웹도 Windows와 동일 |

---

## 18. 설계 이탈 (규격·지시문과 다른 6가지)

### 이탈 ① `screens/modals/{confirmDelete,framePicker}`를 만들지 않고 `ModalId`에서 두 값을 제거한다
WBS Step 15의 대상 파일 목록은 두 공용 모달을 전제하지만, `03 §790`이 **삭제 확인·불러오기·서버 등록 확인을
전부 화면 로컬 오버레이로 규정**했고 Step 14가 삭제를 그 형태로 이미 구현했다(FR-5). 공용 모달을 만들면
같은 UI가 둘이 되거나 Step 14 코드를 재작성해야 한다. → **오버레이로 통일**하고 셸 식별자를 제거해
"나중에 배선되는" 경로를 구조적으로 없앤다(FR-8). 문서 동기화: `02 §10`·`00 §2`·`03 §15`의 "모달 7종" 표기를
**셸 모달 4종(카메라 테스트·진단·PIN·유휴) + 화면 로컬 오버레이 5종**으로 정정한다.

### 이탈 ② 편집기 스테이지를 canvas가 아니라 `<img>` + DOM 슬롯으로 만든다
§8.2. 규격의 실질 요구(실제 렌더 크기 측정·하나의 변환)는 그대로 충족하고, 좌표계 이중화·`ImageBitmap`
수명·접근성 수동 구현을 전부 없앤다.

### 이탈 ③ 저장 전 검증에 **⑧ 개인 프레임 10개 상한**을 7단 뒤에 붙인다
03 §11.3의 표는 7단이고 상한은 §11.6에 따로 있다. Windows는 상한을 `SaveLocal` 내부 예외로 처리하는데,
웹 `saveLocal`은 **예외를 던지지 않는 어댑터**라 그 자리에 둘 수 없다. → 같은 순수 판정 함수의 **⑧**로
편입하되 **순서는 7단 뒤로 고정**해 기존 문구 순서를 보존한다. 덮어쓰기 저장은 상한에서 제외한다(§4.3).

### 이탈 ④ 피커 목록에도 **무진행 30초 / 총 60초 상한**을 건다
Windows 피커에는 상한이 없다(취소 토큰만). 웹은 `FrameSelect`와 같은 상한을 붙여 서버 무응답에서
100초 스피너 대신 로컬 목록으로 마감한다. 실패 모드가 `loadLocalOnly`라 데이터 손실이 없다.

### 이탈 ⑤ 서버 등록 2단계 중 **이미지 PUT이 실패하면 서버 문서를 지운다**
03 §11.4의 원자성은 "로컬 저장도 하지 않는다"까지만 규정한다. 그대로 두면 이미지 없는 문서가 남아
**모든 키오스크에서 영구 "불러올 수 없음" 카드**가 된다. `analysis/31 §4.12`가 허용한 정리 경로를
**best-effort**로 수행한다(실패해도 사용자 문구 불변).

### 이탈 ⑥ `frameStore.saveLocal`이 덮어쓰기에서 **이전 OPFS 이미지를 지운다**
Step 14 코드의 잠재 결함(F-5) 수정이다. 규격 변경이 아니라 누수 제거이며, 05 §4의 "프레임 1개 = 메타 1 + PNG 1"
불변식을 실제로 성립시킨다.

---

## 19. 정적 불변식 (신설 — `tests/unit/frames/frameInvariants.test.ts`에 추가)

| # | 불변식 | 왜 |
|---|--------|-----|
| **FR-5**(확장) | `FrameSelectView.tsx` **+ `FrameEditorView.tsx`** 에 `pushModal(` 0건. `screens/modals/{confirmDelete,framePicker}` 디렉터리 부재(기존 단언 유지, 주석을 "영구 원칙"으로 갱신) | 화면 로컬 오버레이 원칙 |
| **FR-8** | `src/` 전체에 `"framePicker"`·`"confirmDelete"` **리터럴** 0건 | 셸 모달로 되살아나는 경로 차단(식별자 `confirmDelete()`는 따옴표가 없어 걸리지 않는다) |
| **FR-9** | `adapters/http/frameRepository.ts`에 `method: "PUT"`·`replaceImage`·`updateFrame` 0건 | `PUT /frames/{id}` 미호출 정책(03 §11.2) |
| **FR-10** | `screens/frameEditor/frameEditorSave.ts` 소스에서 `validateFrameSave(`의 첫 등장 인덱스가 `createServerFrame(`·`saveLocal(`의 첫 등장보다 **작다** | "실제 저장 함수 첫 줄에서 재실행"의 기계 검증 |
| **FR-11** | `src/` 전체에서 `requiresServerRegisterPrompt(` 호출이 **정확히 2건**(`useFrameEditor.ts`·`frameEditorSave.ts`) | 오버레이 노출 축과 등록 분기 축이 갈라지는 것을 막는다 |
| **FR-12** | `domain/frames/frameSavePolicy.ts`·`screens/frameEditor/*`에 `validateFrameName(` 0건, `isFileNameSafe(`는 `frameSavePolicy.ts`에 존재 | ⑤⑥의 판정 축(길이 무관) 고정 |
| **FR-13** | `frameSavePolicy.ts` 소스에서 reason 리터럴 8개의 등장 순서가 `not-logged-in → no-write-permission → invalid-slots → same-as-source → name-empty → name-invalid-chars → name-conflict → limit-reached` | 7단 순서(특히 **④ < ⑦**)의 정적 고정 |
| **FR-14** | `screens/frameEditor/*`·`ui/views/FrameEditorView.tsx`에 `console.` 0건 | 로깅 규약 |
| **FR-15** | `adapters/frames/frameImageLoader.ts`에 `mode: "cors"` 존재 | WM2 규약 복제(§9.2 ②) |

기존 **FR-1**(OPFS 직접 접근 0)의 검사 대상 목록에 `adapters/frames/frameImageLoader.ts`를 추가한다.

---

## 20. 테스트 계획 (js-developer가 작성할 것)

### 20.1 `tests/unit/frames/frameSavePolicy.test.ts` (신규 · 도메인)

- **7단 순서**: 여러 검사를 동시에 위반하는 입력에서 **앞선 reason**이 나온다.
  - fork + 공용 + `이름 == 원본` + 금지문자 + 스코프 충돌 → `same-as-source` (④가 ⑤⑥⑦보다 먼저)
  - 게스트 + 모든 위반 → `not-logged-in`
  - `user` 역할 + 모든 위반 → `no-write-permission`
  - 겹치는 슬롯 + 빈 이름 → `invalid-slots`
  - 빈 이름 + 충돌 → `name-empty`
  - `"a<b"` + 충돌 → `name-invalid-chars`
- **⑤⑥ 축**: `"a".repeat(150)`가 **통과한다**(길이를 보지 않는다 — `validateFrameName`을 쓰면 실패한다).
  `"이름\n"`은 `isFileNameSafe`가 **거부**한다(원문 판정).
- **⑦ 예외**: `EditOwnLocal` + 충돌 → `ok`. `New`·`ForkFromCatalog` + 충돌 → `name-conflict`.
- **⑦ 비차단**: `existingNames: []`(열거 실패)에서 ⑦이 꺼지고 ④는 여전히 동작한다.
- **④ 스코프**: 개인 스코프(advanced_user) fork + 같은 이름 → ④ 발동 **안 함**(공용에서만).
- **⑧**: 공용 스코프는 상한 무시. 개인 + 신규 이름 + count 10 → `limit-reached`. 개인 + 기존 이름 + count 10 → `ok`.
- `requiresServerRegisterPrompt`: 5역할 × 3세션 **15조합 전수**. true는 `(manager|admin) × New` 2개뿐.
- `saveScopeNoticeKind`·`showsLocalOnlyBanner`·`frameSaveScope`·`editSessionSource` 전수.
- `DEFAULT_REGISTER_TO_SERVER === true`(뒤집을 때 함께 움직이는 기대값 명시).

### 20.2 `tests/unit/frames/frameImagePolicy.test.ts` (신규 · 도메인)

§9.1 표의 7케이스 + `// ↔ SlotLayoutTests.cs:256` 주석.

### 20.3 `tests/unit/domain/frames.test.ts` (증분) — `rescaleSlots`

- `factor=0.5`에서 좌표·크기가 절반이고 `roundHalfToEven`이 적용된다(`x=2.5 → 2`, `x=3.5 → 4`).
- 결과가 항상 프레임 경계 안이다(`clampToFrame` 적용).
- `width/height`는 최소 1이다.
- `// ↔ FrameEditorViewModel.cs:396-415` 주석.

### 20.4 `tests/unit/frames/frameEditorState.test.ts` (신규 · reducer)

- `setSlotCount(6)` → `baseSlots.length === 6`이고 자동 배치 좌표가 `autoArrange`와 일치.
- `setAspect("Ratio1x1")` → 재배치.
- `setScale(130)` → **항상 `baseSlots`에서** 계산(연속 70→130→100이 원래 값으로 정확히 복귀한다 — 누적 오차 0).
- `dragSlot`: 경계 밖 좌표가 클램프되고 `baseSlots`의 중심이 함께 갱신된다.
- `dragSlot` 뒤 `setScale`이 **드래그한 위치를 유지**한다(§8.4 회귀).
- `pickedApplied`가 `name`을 **바꾸지 않고** `pickedSourceNotice`를 채우며 `sessionSource`를 유지한다.
- `imageLoaded`(직접 파일)가 `pickedSourceNotice`를 **비운다**.
- `editSessionReady`가 자동 배치를 **하지 않고** 원본 슬롯을 그대로 쓴다.

### 20.5 `tests/unit/frames/frameEditorSave.test.ts` (신규 · 순서·원자성)

- **재실행**: `runFrameSave`가 검증에 실패하면 `createServerFrame`·`saveLocal`이 **0회** 호출된다.
- **모달 우회 차단**: 오버레이 경로로 들어온 요청도 같은 판정에 막힌다(권한을 낮춰 확인).
- **등록 축**: `advanced_user` + `registerToServer:true` → 서버 호출 **0회**(권한 축이 막는다).
- **`_` 하드 거부**: 체크 on + `"a_b"` → `createServerFrame` 0회, `server-failed`.
- **원자성 3케이스**: `createServerFrame` 예외 / `frame===null` / `putUrl===null` / `putImage.ok===false`
  → **각각 `saveLocal` 0회**이고 `deleteServerFrame`이 (뒤 2케이스에서) 1회 호출된다.
- `deleteServerFrame`이 던져도 결과 문구가 바뀌지 않는다.
- **성공 순서**: `createServerFrame → putImage → saveLocal → goToFrameSelect` (호출 순서 배열로 단언).
- `registerToServer:false` → `saveLocal(dbId:null)`.
- power 공용 저장은 `scope:"public"`·`ownerId:null`, advanced_user는 `scope:"user"`·`ownerId:uid`.
- `saveLocal`이 `null`이면 `local-failed`이고 화면 전환이 **없다**.

### 20.6 `tests/unit/frames/framePickerRunner.test.ts` (신규)

- 정상: 공용 + 개인이 합쳐지고 `hasUsableImage`로 걸러진다. `selectedId`는 **null**(자동 선택 없음).
- `loadPublic` 실패 → `loadLocalOnly` 폴백. 둘 다 실패 → `phase:"failed"` + `pickerFailed`.
- 결과 0개 → `phase:"failed"` + `pickerEmpty`.
- 개인 로드 실패가 공용 목록을 무너뜨리지 않는다.
- `isStale()`이 true면 `finally`가 아무것도 apply하지 않는다.
- 상한 abort에서도 `finally`가 국면을 확정한다(`loading` 고착 0).

### 20.7 `tests/unit/frames/previewUrl.test.ts` (신규 · 누수 0)

- `set` 3회 + `dispose` → `createObjectURL` 3회, `revokeObjectURL` 3회.
- `set(null)`이 이전 URL을 해제하고 빈 문자열을 돌려준다.
- `dispose` 멱등.

### 20.8 `tests/unit/frames/frameStore.test.ts` (증분)

- `scopeFrameNames("public", null)`가 공용 이름만, `("user", uid)`가 본인 것만 돌려준다.
- **이미지가 없는 레코드의 이름도 포함**한다(`listPublic`과의 차이 — 가드가 뚫리지 않는다).
- 메타 조회 실패 → `[]`.
- **덮어쓰기**: 같은 `scope/owner/name`으로 `saveLocal`을 두 번 하면 OPFS에 PNG가 **1개만** 남는다(F-5).
- 이전 이미지 삭제 실패가 저장 자체를 실패시키지 않는다.

### 20.9 `tests/unit/http/frameRepository.test.ts` (증분)

- `analysis/31 §4.12`의 **응답 예시 그대로**를 목으로 주면 `putUrl`·`requiredHeaders`가 채워진다(F-4 회귀).
- `upload`가 없으면 `putUrl === null`.
- `requiredHeaders` 객체가 **원형 보존**된다(키를 골라 담지 않는다 — M14).

### 20.10 `tests/unit/frames/frameInvariants.test.ts` (증분)

§19의 FR-5 확장 + FR-8 ~ FR-15.

### 20.11 E2E (Step 17로 이월 — 시나리오만 확정)

| id | 시나리오 |
|----|----------|
| `E-15a` | power 신규 → 오버레이 체크 on → `POST /frames` 201 + `PUT` 200 → 목록에 등장 |
| `E-15b` | 기존 공용 이름을 그대로 타이핑해 저장 → **차단**(⑦) · 다른 프레임이 파괴되지 않는다 |
| `E-15c` | 피커로 불러온 세션에서 [저장] → **오버레이가 뜬다**(재정의 확인) |
| `E-15d` | 서버 500 목 → 로컬에도 저장되지 않고 편집 세션이 유지된다 |
| `E-15e` | 저장 취소 후 OPFS `frames/`에 임시 파일이 생기지 않는다 |
| `E-15f` | `PUT /frames/{id}` 요청이 **0건**(Network 감시) |

---

## 21. 구현 단계 (WBS 블루프린트)

> 각 단계는 **self-contained**다. 컨텍스트가 없는 에이전트가 그 단계만 읽고 실행할 수 있다.
> 공통 검증: `cd webclient && npx tsc --noEmit && npx vitest run` (Step 14 종료 시점 **1469 통과 / 62파일**).
> `docs/spec-vectors/`·`tests/MCPhoto.Tests/`·`web/functions/`를 **변경하지 않는다** → `dotnet test`·서버 테스트 불요.

### Step 15-1: 도메인 3종 (순수)

- **Context Brief**: 저장 판정·이미지 제한·슬롯 환산을 순수 함수로 만든다. 이 단계가 끝나면 Step 15의
  모든 "규격"이 node에서 검증 가능해진다. 규격은 `03 §11.3`·`analysis/13 §6.3·§6.4`·`analysis/14 §4.6`.
- **대상 파일**: `src/domain/frames/frameSavePolicy.ts`(신규) · `src/domain/frames/frameImagePolicy.ts`(신규) ·
  `src/domain/frames/slotLayout.ts`(수정 — `rescaleSlots` 추가만) · `src/domain/index.ts`(재수출) ·
  `tests/unit/frames/frameSavePolicy.test.ts`(신규) · `tests/unit/frames/frameImagePolicy.test.ts`(신규) ·
  `tests/unit/domain/frames.test.ts`(증분)
- **선행 조건**: 없음
- **구현 내용**: §3·§4·§7.3·§9.1의 시그니처와 본문 그대로. `frameSavePolicy.ts`는
  `roles/userRole`·`frames/{frameNaming,frameEditPolicy,frameOrigin,slotLayout,frameStorePolicy,types}`만
  import한다(도메인 내부 상대 경로). 문자열을 **갖지 않는다**(reason 유니온만).
- **검증 명령**: `npx tsc --noEmit` · `npx vitest run tests/unit/frames tests/unit/domain` ·
  `npx vitest run tests/unit/domain/purity.test.ts`(신규 2파일이 자동 포함된다)
- **완료 기준**
  - [관측] §20.1·§20.2·§20.3의 케이스가 전부 통과한다. 특히 **"fork+공용+원본이름+금지문자+충돌 → `same-as-source`"**
    와 **"150자 이름이 ⑤⑥을 통과"** 두 개가 녹색이다.
  - [non-goal] 화면·어댑터 코드 **없음**. `slotLayout.ts`의 **기존 export가 하나도 바뀌지 않는다**
    (`auto-arrange.json`·`scale-slots.json`·`clamp-slot.json`·`overlap.json` 4벡터가 계속 통과한다).
  - [trigger] 순수성 테스트가 새 2파일을 자동으로 검사한다(브라우저 API·`Date.now`·`console` 0건).
- **롤백**: 신규 2파일 삭제 + `slotLayout.ts`·`domain/index.ts`의 추가분 revert.

### Step 15-2: 어댑터 3종 (이미지 로더 · 저장소 보강 · HTTP 정정)

- **Context Brief**: 브라우저 경계 3곳을 정리한다. **`createFrame`의 응답 봉투 결함(F-4)** 과
  **`saveLocal` 덮어쓰기 고아(F-5)** 는 기존 코드의 잠재 버그 수정이다 — 이 단계에서 반드시 함께 고친다.
- **대상 파일**: `src/adapters/frames/frameImageLoader.ts`(신규) ·
  `src/adapters/storage/frameStore.ts`(수정 — `scopeFrameNames` 추가 + `persist` 이전 이미지 정리) ·
  `src/adapters/http/frameRepository.ts`(수정 — `upload` 봉투) ·
  `tests/unit/frames/frameStore.test.ts`(증분) · `tests/unit/http/frameRepository.test.ts`(증분) ·
  `tests/unit/frames/frameImages.test.ts`(증분 — 로더)
- **선행 조건**: Step 15-1(`frameImagePolicy`)
- **구현 내용**: §9.2·§10·§11. 로더는 **예외를 전파하지 않고** 판별 유니온을 돌려준다.
  `createImageBitmap`에 `{imageOrientation:"from-image"}`를 **반드시** 준다. `bitmap.close()`는 `finally`.
  `toPngBlob`은 `OffscreenCanvas.convertToBlob` → `HTMLCanvasElement.toBlob` 폴백.
  `persist()`의 정리 순서는 **새 레코드 기록 뒤**다.
- **검증 명령**: `npx tsc --noEmit` · `npx vitest run tests/unit/frames tests/unit/http`
- **완료 기준**
  - [관측] `analysis/31 §4.12`의 응답 예시를 목으로 주면 `putUrl`이 채워진다(그전에는 `null`이었다).
    같은 이름으로 두 번 `saveLocal`하면 OPFS PNG가 **1개**다. `scopeFrameNames`가 이미지 없는 레코드의
    이름도 포함한다.
  - [non-goal] 화면 코드 **없음**. `deleteLocal`·`listPublic`·`listPersonal`·`cacheServerFrame`의 **기존 동작이
    바뀌지 않는다**(Step 14 테스트 전부 녹색). `getUserFrames` 호출 0건 유지.
  - [trigger] 이전 이미지 삭제는 **키가 이미 있고 파일 경로가 다를 때만** 일어난다.
- **롤백**: 로더 삭제 + 나머지 두 파일 revert(Step 14 상태로 복귀 — 단 F-4·F-5는 다시 잠복한다).

### Step 15-3: 인계 채널 + 화면 로직 (React 무관)

- **Context Brief**: `FrameSelect → FrameEditor` 인계와 편집기의 순서·판정 전부를 React 밖에 만든다.
  이 저장소에는 jsdom이 없어 훅·컴포넌트가 테스트되지 않으므로(15 §3.1) **여기 있는 것만 검증된다**.
- **대상 파일**: `src/shell/frameEditorIntent.ts`(신규) · `src/screens/frameEditor/frameEditorState.ts`(신규) ·
  `frameEditorSave.ts`(신규) · `framePickerRunner.ts`(신규) · `previewUrl.ts`(신규) · `frameEditorEntry.ts`(신규) ·
  `src/ui/strings.ts`(수정 — `frameEditor` 섹션 + `frames.nameEmpty` 정정 + `frameSaveRejectionMessage`) ·
  `tests/unit/frames/{frameEditorState,frameEditorSave,framePickerRunner,previewUrl}.test.ts`(신규)
- **선행 조건**: Step 15-1, Step 15-2
- **구현 내용**: §5·§7.2·§9.4·§13·§16. `runFrameSave`의 **첫 실행문이 검증**이다(FR-10).
  `requiresServerRegisterPrompt`를 `frameEditorSave.ts`에서 **다시 부른다**(FR-11).
  `framePickerRunner`는 `createDeadline`을 **주입**받는다(브라우저 타이머를 모른다).
  `previewUrl`은 `createObjectURL`/`revokeObjectURL`을 주입받는다.
- **검증 명령**: `npx tsc --noEmit` · `npx vitest run tests/unit/frames`
- **완료 기준**
  - [관측] §20.4~§20.8이 전부 통과한다. 특히 **원자성 4케이스에서 `saveLocal` 0회**와
    **`dragSlot` 뒤 `setScale`이 위치를 유지**가 녹색이다.
  - [non-goal] React import **0건**(네 파일 전부). 브라우저 전역(`window`·`document`·`URL`) 직접 참조 0건
    — 전부 주입이다. UI 컴포넌트 **없음**.
  - [trigger] `readFrameEditorIntent`는 **비파괴**다 — 두 번 읽어도 같은 값이 나온다(StrictMode 대비).
- **롤백**: 신규 6파일 삭제 + `strings.ts` revert.

### Step 15-4: 편집기 UI + 스테이지 기하

- **Context Brief**: 화면을 붙인다. **표시·드래그·클램프가 하나의 `EditorTransform`** 을 쓰는 것이 이 단계의
  전부다. 규격은 `03 §11.2·§11.7`·`analysis/14 §4.5`.
- **대상 파일**: `src/ui/views/FrameEditorView.tsx`(신규) · `src/ui/views/frameEditor.module.css`(신규) ·
  `src/ui/components/OverlayDialog.tsx`(신규) · `src/screens/frameEditor/useFrameEditor.ts`(신규) ·
  `src/App.tsx`(수정 — `FrameEditor` 케이스) · `src/screens/frameSelect/useFrameSelect.ts`(수정 — TODO 2곳)
- **선행 조건**: Step 15-3
- **구현 내용**: §8·§14. `useFrameEditor`는 **얇다** — 상태 보관 + 세대 카운터 + 위 모듈 호출만 한다.
  진입 effect의 cleanup 순서는 **① 세대 증가 → ② abort**(Step 14와 같은 규격). `previewUrl.dispose()`를
  언마운트 cleanup에 반드시 넣는다. 드래그는 `pointerup`·`pointercancel`·`lostpointercapture` **셋 다** 구독.
  `ResizeObserver`는 언마운트에서 `disconnect()`.
- **검증 명령**: `npx tsc --noEmit` · `npx vitest run` · `npx vite build` ·
  브라우저: 이미지 로드 → 슬롯 4개 자동 배치 → 드래그 → 저장 → `FrameSelect`에 등장
- **완료 기준**
  - [관측] 슬롯을 프레임 모서리로 끌면 **경계에서 멈추고**(클램프) 손을 떼도 그 자리에 있다. 창 크기를
    바꿔도 슬롯이 이미지 위 같은 상대 위치에 남는다(하나의 변환). `user` 역할로는 편집기 본문이
    **렌더되지 않는다**.
  - [non-goal] `scaleX(-1)`·`rotateY(180deg)` 0건(WM1 정적 검사 유지). `pushModal(` 0건.
    `FrameSelectView.tsx`의 삭제 오버레이 **무변경**.
  - [trigger] 스테이지 측정은 `getBoundingClientRect()`에서만(선언 크기 금지). 자동 배치는 슬롯 개수·종횡비·
    파일 이미지 교체에서만 — **[선택 편집] 진입과 피커 적용에서는 일어나지 않는다**.
- **롤백**: 신규 4파일 삭제 + `App.tsx`를 `DummyScreen`으로 되돌리고 `useFrameSelect.ts`의 TODO 복원.

### Step 15-5: 피커 오버레이 + 서버 등록 확인 오버레이

- **Context Brief**: 화면 로컬 오버레이 2개를 붙인다. **상호배타**(03 §790)이며 체크박스 확정 순서가 규격이다.
- **대상 파일**: `src/ui/views/FrameEditorView.tsx`(수정 — 오버레이 2개) ·
  `src/ui/views/frameEditor.module.css`(수정) · `src/screens/frameEditor/useFrameEditor.ts`(수정 — 오버레이 상태)
- **선행 조건**: Step 15-4
- **구현 내용**: §6·§7. 피커는 `createFrameThumbnail`(Step 14)로 썸네일을 그리고 **`ImageBitmap`을
  언마운트에서 `close()`** 한다(WR8 — `FrameSelectView.FrameThumb`가 선례다). 오버레이를 닫을 때
  목록 로딩을 `abort()`한다. 등록 오버레이는 열 때마다 `registerToServer = DEFAULT_REGISTER_TO_SERVER`.
- **검증 명령**: `npx tsc --noEmit` · `npx vitest run` ·
  브라우저: 피커로 불러오기 → 캡션 표시 → [저장] → **등록 오버레이가 뜬다**
- **완료 기준**
  - [관측] 체크를 해제하고 [저장]하면 서버 요청이 **0건**이다(Network). 체크 on에서 취소하고 다시 열면
    체크가 **다시 on**이다. 피커와 등록 오버레이가 **동시에 뜨지 않는다**.
  - [non-goal] `pushModal(` 0건. 배경 클릭으로 닫히지 않는다. 피커가 자동 선택하지 않는다.
  - [trigger] 등록 오버레이는 `isPower && sessionSource==="New"`에만. 피커 버튼은 `sessionSource==="New"`에만.
- **롤백**: 오버레이 2개 렌더 제거(편집기 본체는 남는다).

### Step 15-6: 셸 정리 · 정적 불변식 · 문서 동기화 · 전량 검증

- **Context Brief**: 화면 로컬 오버레이 원칙을 **구조적으로** 고정하고 문서를 맞춘다.
- **대상 파일**: `src/shell/shellStore.ts`(수정 — `ModalId` 2개 제거) · `src/App.tsx`(수정 — 주석) ·
  `src/ui/views/SettingsView.tsx`(수정 — 주석) · `tests/unit/frames/frameInvariants.test.ts`(증분) ·
  `docs/web-client/11-wbs.md`(Step 15 체크 + 산출물) · `docs/web-client/15-implementation-conventions.md`
  (§3 불변식 표 · §6 Step 15 절 · §7 상태 요약) · `docs/web-client/01-tech-stack-and-structure.md`(§2.2에
  `frameImagePolicy`·`frameSavePolicy` 행 추가) · `docs/web-client/03-screens-spec.md`(§15 "모달 7종" → 셸 4 + 오버레이 5) ·
  `docs/web-client/02-app-shell-and-navigation.md` §10 · `docs/web-client/00-scope-and-decisions.md` §2 ·
  `docs/web-client/14-handoff-and-user-actions.md`(V24 등재) · `docs/design/README.md` §3.1
- **선행 조건**: Step 15-1 ~ 15-5
- **구현 내용**: §12.1·§19. `15 §6`의 "이월: [프레임 내보내기]/[가져오기] → Step 15" 행을 **Step 16으로 정정**한다
  (WBS Step 16의 `exportImport.ts`가 그것을 소유한다 — 15b 컷라인 설명과도 일치).
- **검증 명령**: `cd webclient && npx tsc --noEmit && npx vitest run && npm run coverage && npx vite build` ·
  `grep -rn '"framePicker"\|"confirmDelete"' webclient/src` → **0건**
- **완료 기준**
  - [관측] 전 스위트 녹색이고 `src/domain` 커버리지 임계(lines 95 / branches 90 / functions 95)를 넘는다.
    FR-8 ~ FR-15가 전부 통과한다.
  - [non-goal] `web/functions`·`tests/MCPhoto.Tests`·`docs/spec-vectors` **무변경**. Windows·서버 테스트
    재실행 의무 없음. Step 16 항목(진단·PWA·zip 내보내기) **미구현**.
  - [trigger] 문서 갱신은 이 단계에서만.
- **롤백**: `ModalId` 2값 복원 + 불변식 테스트 revert(문서는 그대로 둬도 무해).

### 완결성 게이트 (developer 전달 전 자체 검사)

- [x] 모든 미검증 가정(A15-1~A15-5)에 검증 단계가 매핑돼 있다(§0.2 · §22).
- [x] 6단계(권장 3~12) · 각 단계가 "실패 시 원인이 하나로 특정된다".
- [x] 각 단계에 Context Brief / 대상 파일 / 선행 조건 / 구현 내용 / 검증 명령 / 완료 기준(관측·non-goal·trigger) / 롤백.
- [x] 검증 명령이 전부 자동 실행 가능한 CLI다.
- [x] UI 단계(15-4·15-5)에 non-goal·trigger가 있다.

---

## 22. 남는 사용자 액션 — 실측 **V24**(브라우저·실계정 필요, 자동화 불가)

`docs/web-client/14-handoff-and-user-actions.md` §10.10에 등재한다.

| # | 확인 | 방법 | 실패 시 |
|---|------|------|---------|
| **V24-1** | 대상 브라우저에서 PNG 재인코딩이 성공한다(A15-1) | JPG 5MB 로드 → 저장 → OPFS PNG 확인 | 폴백 경로가 동작하는지 확인(로그 `convertToBlob`) |
| **V24-2** | EXIF 회전 JPG가 바로 선다(A15-2) | 아이폰 세로 사진 로드 | `imageOrientation` 옵션 재확인 |
| **V24-3** | **편집기 슬롯 위치 == 합성 결과 위치(0px)**(A15-3) | 저장 → 그 프레임으로 촬영 → 결과 이미지와 편집기 캡처 비교 | `EditorTransform` 공유가 깨진 지점 추적 |
| **V24-4** | power 서버 등록 2단계가 실제로 성공한다(A15-4) | manager 계정 → 체크 on 저장 → Storage에 PNG · 다른 기기에서 내려받힘 | `requiredHeaders`·버킷 CORS 확인 |
| **V24-5** | 태블릿 터치 드래그가 스크롤과 충돌하지 않는다(A15-5) | Android/iPad에서 슬롯 드래그 | `touch-action: none` 적용 범위 확인 |
| ~~V24-6~~ | **해소됨(2026-08-01)** — 배율 10~300 확정, 상수·문서 반영 완료 | — | — |
| **V24-7** | 저장 취소 후 OPFS `frames/`에 임시 파일이 없다 | DevTools → OPFS | 로더가 디스크에 쓰고 있지 않은지 확인 |
| **V24-8** | 서버 등록 실패 후 체크 해제 재저장이 성공한다 | 오프라인에서 체크 on 저장 → 온라인 복귀 없이 체크 해제 저장 | ⑦ 자기 충돌 여부 확인 |

**추정으로 통과 처리하지 않는다.**

---

## 23. 리스크와 명시적 비목표

### 23.1 리스크

| # | 리스크 | 완화 |
|---|--------|------|
| R1 | `<img>` 스테이지가 규격 문구("캔버스")와 달라 리뷰에서 되돌려질 수 있다 | §8.2에 근거를 남기고 규격의 실질 요구(측정·단일 변환)를 그대로 충족 |
| R2 | ⑧ 상한을 7단에 편입한 것이 "순서가 규격"과 충돌해 보인다 | 7단 **뒤**로 고정 + FR-13이 순서를 기계 검증 |
| R3 | `ModalId` 축소가 Step 16(진단 모달)과 충돌 | `diagnostics`는 남긴다 — 제거 대상은 화면 로컬로 확정된 2개뿐 |
| R4 | 서버 문서 정리(DELETE)가 권한·네트워크로 실패해 고아가 남는다 | best-effort + 로그. 삭제 흐름의 **이름 매칭 폴백**으로 나중에 정리 가능 |
| R5 | power가 저장한 공용 로컬 프레임을 자기가 편집할 수 없다(F-7) | Windows와 같은 동작. 피커로 불러와 새 이름 저장이 우회로다. 바꾸면 FR-2 회귀 |
| R6 | 피커의 `loadPublic`이 네트워크를 타 대기가 길어진다 | 단일 비행이라 대개 캐시 히트 + 30/60초 상한 + [취소] 상시 노출 |

### 23.2 명시적 비목표

- **`PUT /frames/{id}` 호출** — 어떤 경로로도 하지 않는다(FR-9).
- **프레임 zip 내보내기/가져오기** — **Step 16**(WBS Step 16의 `exportImport.ts`). `15 §6`의 반대 서술은 오기다(§21 Step 15-6에서 정정).
- **삭제 UI 재작성** — Step 14의 화면 로컬 오버레이를 그대로 쓴다(§12).
- **`FrameSelectView` 삭제 오버레이의 `OverlayDialog` 이관** — Step 17 이후 정리 대상.
- **텍스트·스티커·배경 편집** — 범위 밖(analysis/13 §6.2 · 90 §2.1).
- **서버 측 동명 문서 검사** — 하지 않는다(네트워크 실패가 오프라인 저장을 막는다 — analysis/13 §6.4).
- **계정·사용자 관리·진단·PWA** — Step 16.
- **Playwright E2E** — Step 17(시나리오만 §20.11에 확정).

---

## 24. 요약

1. **세션 정체성 축 하나**(`FrameSessionSource`)가 배너·이름 제안·서버 등록·fork를 전부 결정한다.
   피커는 그 축을 **바꾸지 않는다** — 불러온 세션도 신규 생성이고 power면 서버 등록 대상이다.
2. **저장 전 검증은 도메인 순수 함수 하나**가 소유하고 순서(④ < ⑦, ⑤⑥은 `isFileNameSafe`)를 FR-13이 기계 검증한다.
   진입점 2개는 모두 `runFrameSave`를 지나고, 그 **첫 실행문이 재검증**이다(FR-10).
3. **서버 등록은 원자적이다**: 문서 생성·이미지 PUT 중 하나라도 실패하면 로컬에 저장하지 않고 서버 문서를
   best-effort로 정리한다. 부분 성공이 ⑦ 자기 충돌로 저장을 영구히 막는 것을 방지한다.
4. **오버레이 노출과 등록 분기는 같은 함수**(`requiresServerRegisterPrompt`)를 쓰고 FR-11이 호출 2건을 고정한다.
5. **WYSIWYG는 구조로 보장한다**: `<img>`와 슬롯 박스와 포인터 역변환과 클램프가 전부 하나의
   `EditorTransform` 객체를 쓴다.
6. **기존 결함 2건을 함께 고친다**: `createFrame`의 `upload` 봉투(F-4 — 안 고치면 이미지가 영원히 안 올라간다),
   `saveLocal` 덮어쓰기 고아 PNG(F-5).
7. **삭제는 다시 만들지 않는다.** 화면 로컬 오버레이 원칙을 `ModalId` 축소 + FR-8로 구조적으로 고정한다.
