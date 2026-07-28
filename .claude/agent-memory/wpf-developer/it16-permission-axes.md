---
name: it16-permission-axes
description: it16 권한 축 3개(IsPower·CanWriteFrames·CanManage)의 역할 분리 — 혼용 금지 규칙과 CanDelete가 소유자를 안 보는 이유
metadata:
  type: project
---

it16부터 MCPhoto 클라이언트에는 **서로 대체 불가능한 권한 축 3개**가 있다. 하나로 합치려는 시도를 하지 말 것.

| 축 | 정의 | 통과 역할 | 용도 |
|---|---|---|---|
| `IsPower()` | 계정 관리 + 공용 DB 프레임 관리 | Manager, Admin | 사용자 관리 화면, DB 프레임 쓰기, "서버에서도 제거" |
| `CanWriteFrames()` | 프레임 **저작**(생성·편집·삭제) | AdvancedUser, Manager, Admin | 프레임 만들기/선택 편집/삭제 ✕ 노출, `Save()` 가드 |
| `CanManage(target)` | 대상이 자신과 **같거나 낮은** 위계 | 전 역할(위계 비교) | 관리 액션의 **보조** 조건 |

**Why:**
- `AdvancedUser`는 `CanWriteFrames=true`인데 `IsPower=false`다. 둘을 섞으면 고급 유저가 계정 관리·공용 DB
  프레임 권한까지 얻는다. `CanWriteFrames`를 `ManageRank >= 2` 부등식으로 쓰지 않는 이유도 같다 —
  훗날 관리 위계에 역할이 끼어들 때 저작 권한이 조용히 따라 움직인다(명시 열거 유지).
- `CanManage` **단독으로 관리 게이트를 만들면 구멍이 난다.** it15까지 `CanResetPin = !isSelf && CanManage(...)`
  였고, 같은 위계를 허용하므로 `user`가 다른 `user`의 PIN을, `temp_user`가 다른 `temp_user`의 PIN을
  재설정할 수 있었다. it16에서 `IsPower()` 항을 추가해 막았다. 관리 액션 게이트는 항상
  **`IsPower() && CanManage(target)`** 2항이다(서버 `requirePower + canManage`와 대칭).
- `canManage`의 의미를 "엄격히 높은 위계"로 좁히는 안은 채택하지 않았다 — 계정 삭제와 공유되므로
  admin↔admin·manager↔manager 삭제가 회귀한다.

**How to apply:**
- `FrameEditPolicy.CanDelete(frame, role)`는 **소유자(userId)를 보지 않는다.** power가 fork·저장한 공용 로컬
  프레임은 디스크에서 다시 읽으면 `Id="local:{이름}"`, `UserId=null`이라(`LocalFrameStore`) `IsOwnedLocal`로
  판정하면 기존 삭제 능력이 회귀한다. 타인 개인 프레임은 `LoadUser`의 `{계정}_` 접두 필터로 목록에 애초에 안 오른다.
  반면 `CanEdit`의 `UserLocal` 분기는 `IsOwnedLocal`을 **유지**한다(둘의 비대칭은 의도된 것).
- 역할 위계 표기: `TempUser=0, User=1, AdvancedUser=2, Manager=3, Admin=4`(enum 배치값 = `ManageRank` =
  서버 `MANAGE_RANK`). 서수는 여전히 비교·직렬화에 쓰지 않는다 — 와이어는 `advanced_user` snake_case 문자열.
- `User`·`TempUser`는 프레임을 **사용만** 한다(E4): 목록 노출·촬영은 유지하고 생성·편집·삭제 UI만 사라진다.
  프레임 목록 로딩 코드를 권한으로 필터하지 말 것.

관련: [[it15-frame-local-only]], [[it14-pin-gate-contract]], [[it16-window-geometry-contract]]
