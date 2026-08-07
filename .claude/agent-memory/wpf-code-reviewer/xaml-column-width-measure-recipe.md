---
name: xaml-column-width-measure-recipe
description: 고정폭 GridViewColumn 잘림 검증은 앱 실행 대신 FormattedText 실측으로 direct 근거화한다(이 리포는 power+PIN 게이트로 UI 육안 확인 불가)
metadata:
  type: reference
---

고정 `Width`를 쓰는 `GridViewColumn` 헤더/셀이 잘리는지 판정할 때, 앱 실행 육안 확인 대신
PowerShell + `System.Windows.Media.FormattedText`로 실측한다.

**Why:** 이 리포의 관리 화면(UserMgmt 등)은 power 로그인 + PIN 게이트 뒤에 있어 에이전트가 실행 화면을
띄울 수 없다. "폭이 넉넉해 보인다"는 indirect 근거라 PASS 판정에 쓸 수 없는데, FormattedText 실측은
direct 근거가 된다.

**How to apply:**
- 폰트는 `Themes/Typography.xaml`의 `Font.Primary` = `"Segoe UI, Malgun Gothic"`(한글은 Malgun Gothic 폴백).
- 가용폭 = `GridViewColumn.Width` − 헤더 `Border Padding` 좌우 합(UserMgmtView는 `12,0` → 24).
  셀은 `Width` − 셀 컨텐츠 `Margin` 좌우.
- `pixelsPerDip`을 1.0/1.25/1.5/2.0으로 바꿔도 DIP 폭은 불변이므로 DPI 스케일은 변수가 아니다(측정 확인).
- 실측 기준값(13px SemiBold): 한글 1글자 ≈ 13.0, `"개인 프레임"` 68.57, `"고급 유저"`·`"역할 변경"` 55.57,
  `"가입 일시 ▲"` 70.34. 14px Normal: `"PIN 재설정"` 67.87, `"삭제"`·`"적용"` 28.0.
- `UserMgmtView.xaml`은 `ScrollViewer.HorizontalScrollBarVisibility="Disabled"`라 폭 합계 초과는 스크롤이
  아니라 **마지막 컬럼 잘림**으로 나타난다 — 컬럼 추가 시 합계(현재 1146px) 유지를 함께 확인한다.
- 함정: 수평 `StackPanel` 안의 `TextTrimming`은 무효다(측정폭 무한) — 긴 텍스트는 줄임표가 아니라
  뒤 요소를 셀 밖으로 밀어내 잘린다.
