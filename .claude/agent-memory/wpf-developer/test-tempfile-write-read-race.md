---
name: test-tempfile-write-read-race
description: MCPhoto 테스트가 %TEMP%에 만든 파일을 곧바로 읽으면 공유 위반(IOException)으로 간헐 실패 — 전체 스위트 병렬 실행에서만 재현
metadata:
  type: feedback
---

테스트 픽스처가 `%TEMP%`에 파일을 쓰고 **곧바로 읽는 구간**은 Windows에서 간헐 실패한다.
새 픽스처 이미지/파일을 만들 때는 반드시 `tests/MCPhoto.Tests/TestImageFile.cs`의
`TestImageFile.Write` / `CreateInTemp`를 경유하라(쓰기 성공 + 읽기 가능 재시도 + 디코드 검증 내장).

**Why:** 갓 만든 파일을 여는 순간이 외부 프로세스(실시간 검사 등)의 스캔 구간과 겹치면
`IOException: The process cannot access the file ... used by another process`가 난다.
전체 스위트를 병렬로 돌릴 때 %TEMP% 쓰기가 몰려 창이 벌어지고, xUnit은 **테스트 메서드마다 클래스를
새로 인스턴스화**하므로 생성자에서 픽스처를 만들면 노출 횟수가 메서드 수만큼 곱해진다.
it15 실측: `FrameEditorViewModelTests`가 전체 실행 시 ~10% 실패(단독·클래스 단위는 항상 통과).
`Cv2.ImWrite`는 성공하고 `File.Exists`도 true였다 → **PNG 쓰기 실패 가설은 반증**됐고,
범인은 `LoadForEdit`의 `File.ReadAllBytes` 공유 위반이었다.

**How to apply:**
- 여러 테스트가 **읽기 전용**으로만 쓰는 픽스처 파일은 `IClassFixture<T>`로 클래스 1회 생성한다
  (예: `FrameImageFixture`). 생성자 생성은 노출 횟수를 곱한다.
- 조용한 실패를 남기지 말 것 — `Cv2.ImWrite` 반환값을 버리면 원인이 **저 아래 다른 단언**에서 터진다.
- 진단 팁: VM이 `catch`로 삼킨 예외는 `ILogger<T>` 캡처 fake를 주입해야 보인다
  (`FrameEditorViewModelTests.CapturingLogger`). 이것 없이는 원인 특정 불가였다.
- **미수정 잠복분**: `CompositionTests.MakeFrameWithImage`도 같은 write→즉시 read 패턴이다(당시 범위 밖).
- 제품 코드 함정: `FrameEditorViewModel.Save()`가 `LoadForEdit`이 남긴 실패 메시지를
  "슬롯이 겹치거나 프레임을 벗어났습니다."로 **덮어써** 최종 메시지로는 사유 구분이 불가능하다.

관련: [[it15-frame-local-only]], [[mcphoto-http-test-infra]]
