---
name: imwrite-atomic-replace-extension
description: Cv2.ImWrite는 확장자로 인코더를 고르므로 원자 교체용 임시 파일도 .png를 유지해야 한다 — ".tmp"로 끝나면 예외
metadata:
  type: project
---

이미지 파일을 `.tmp` → `File.Move(overwrite)` 원자 교체로 쓸 때, 임시 경로는 **반드시 원본 확장자를 유지**해야 한다.
`Path.ChangeExtension(path, ".tmp.png")` 형태를 쓴다. `path + ".tmp"`는 안 된다.

**Why:** OpenCV는 파일 확장자로 인코더를 선택한다. `fallback_frame.png.tmp`에는 writer가 없어
`OpenCvSharp.OpenCVException: could not find a writer for the specified extension`을 던진다(실측 확인).
`FallbackFrameRenderer.Create` → `Cv2.ImWrite` 경로에서 발생하며, 이 예외는 프레임 해석 체인 전체를
fault시켜 **최초 실행이 항상 "프레임 준비 실패"로 떨어지는** 조용한 회귀가 된다 —
fallback 생성이 단 한 번도 성공하지 못하므로 로그만 보면 "프레임이 없다"로 보인다.

**How to apply:** `Cv2.ImWrite`/`ImRead`를 타는 경로에 임시 파일·백업 파일·`.bak`·`.part` 등을 도입할 때.
설계 문서가 `경로 + ".tmp"`를 명시해도 그대로 쓰지 않고 확장자 보존형으로 바꾸고 그 이유를 주석에 남긴다.
[[it20-frame-load-phase-invariant]] 참조 — 같은 이터레이션에서 나온 설계 코드 조각 결함 2건 중 하나다.
