---
name: license-notice-constraints
description: 라이선스 고지 화면·문서를 설계할 때의 불변 경계 — GPLv3 전문/루트 LICENSE 무수정, 요약은 매니페스트 단일 소스, 공개 실패 문구에 슬래시 금지
metadata:
  type: project
---

MCPhoto의 오픈소스 라이선스 고지(설정 → 고급)를 설계할 때 다음 4개는 협상 대상이 아니다.

1. **`licenses/FFmpeg-COPYING.GPLv3.txt`와 리포 루트 `LICENSE`는 1바이트도 수정하지 않는다.** 전문은 원문 그대로여야 효력이 있고, 루트 `LICENSE`는 csproj가 `licenses\MCPhoto-LICENSE-MIT.txt`로 링크 복사하는 단일 소스다. 서식 통일·한국어 안내 추가 대상에서 제외한다.
2. **"전문을 다 보여주지 말라"는 요구를 전문 경로 삭제로 구현하면 안 된다.** GPLv3 §4 때문에 요약 카드 + `[전문 보기]` 2단이 정답이고, 매니페스트가 깨진 강등 경로에서도 전문 도달을 유지해야 한다.
3. **요약 메타데이터는 코드 하드코딩·산문 파싱이 아니라 배포물 안 구조화 파일**(`licenses/notice-manifest.json`)이다. 결정적 이유는 *열거로는 "있어야 할 파일이 없다"를 탐지할 수 없다*는 것. csproj가 `licenses\**\*.*` 와일드카드라 파일 추가·개명에 csproj 변경이 필요 없다.
4. ⚠️ **공개 실패 문구에 `/`와 `:\`를 쓸 수 없다** — `SettingsViewModelLicenseTests.No_Folder_Path_In_Ui`가 금지한다. `licenses/notice-manifest.json`처럼 파일 경로를 문구에 넣으면 테스트가 깨진다. 경로·파일명은 Warning 로그에만.

**Why:** ffmpeg(GPLv3) 바이너리를 재배포하므로 고지 누락·약화는 곧 라이선스 위반이고, 과거 "문서가 거짓말을 하는" 결함 2건(MIT 전문 미동봉·FFmpeg 저작권 표시 누락)이 테스트를 다 통과한 상태에서 발견됐다.

**How to apply:** 라이선스 화면·고지 파일 관련 요구가 오면 [[spec-deprecation-convention]]처럼 먼저 `docs/design/wpf-ffmpeg-licensing-and-distribution-design.md §2.4`(의무 O1~O5)와 `docs/design/wpf-it24-license-notice-redesign-design.md`를 읽고, 요구를 의무 대조표로 옮긴 뒤 설계한다. 고지 접근은 로그인·역할 무관이며 버튼에 `IsEnabled`를 붙이지 않는다.
