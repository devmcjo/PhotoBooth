---
name: role-enum-ordinal-verify-recipe
description: UserRole enum 서수 재배치가 안전한지 독립 검증하는 5-grep 레시피와 "역할은 프로세스 밖으로 숫자로 나가지 않는다"는 근거 체계
metadata:
  type: project
---

`UserRole` enum의 **배치값(서수)을 재배치**하는 변경(it13 TempUser, it16 AdvancedUser)을 리뷰할 때는
developer의 "서수 의존 0건" 주장을 그대로 받지 말고 아래 5축을 직접 grep해 무매치를 확인한다.
하나라도 매치되면 저장된 역할이 다른 역할로 읽혀 **권한 상승**이 되므로 🔴 Critical이다.

```bash
# 1) 캐스팅·대소비교·CompareTo (src + tests 모두)
grep -rnE "\(int\)[[:space:]]*[A-Za-z_]*[Rr]ole|Role\.CompareTo|\bRole[[:space:]]*[<>]=?|[Rr]ole[[:space:]]*[<>]=?[[:space:]]*UserRole|Convert\.ToInt32\(.*[Rr]ole" src tests --include=*.cs --include=*.xaml
# 2) 역할 전용 JSON 컨버터
grep -rn "JsonConverter\|JsonSerializer" src --include=*.cs | grep -i role
# 3) XAML의 enum 인덱스 바인딩·SelectedIndex
grep -rn "UserRole" src --include=*.xaml          # 무매치여야 한다(XAML은 라벨 컨버터만 경유)
# 4) ini 저장 (역할은 애초에 ini 대상이 아니다)
grep -rn "Role" src/MCPhoto.Core/Settings/IniSettingsService.cs
# 5) 와이어 DTO의 타입 — string이어야 한다
grep -rn "Role" src/MCPhoto.Http/Dto/AccountDtos.cs
```

**구조적 근거(왜 안전한가)**: 위계 비교는 `UserRoleExtensions.ManageRank` switch가 담당하고,
직렬화는 `ToFirestoreValue()`/`ParseRole()` snake_case 문자열 왕복이며, DTO의 `Role`은 `string`이다.
`default(UserRole)`은 재배치 후에도 `TempUser=0`으로 불변. XAML은 `RoleLabelConverter`(값→라벨)만 쓰고
콤보 항목은 `AssignableRoles` 목록 바인딩이라 인덱스 의존이 없다.

**Why:** it16(2026-07-29) 리뷰에서 Manager·Admin이 2·3 → 3·4로 이동했다. 설계 §3.1이 "안전 게이트 grep"을
요구했지만 설계 시점 grep은 스냅샷이라 구현 후 재실행이 필수였다. 실측 결과 5축 전부 무매치였고
서수 재배치는 무해했다 — 이 근거 체계를 매번 재구성하지 않도록 남긴다.

**How to apply:** 역할·권한 enum(`UserRole`, 향후 유사 위계 enum)의 배치값이 diff에 보이면 리뷰 착수 직후
위 5축을 돌려 결과를 보고서에 붙인다. 권한 축 자체의 오염 검사는 [[it16-permission-axes-crosscheck]]와
짝으로 수행한다(`IsPower`에 새 역할이 들어갔는지 / `CanWriteFrames`를 `IsPower` 자리에 대입했는지).
관련: [[photobooth-local-id-scope-collision]]
