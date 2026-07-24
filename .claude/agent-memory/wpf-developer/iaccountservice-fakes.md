---
name: iaccountservice-fakes
description: MCPhoto IAccountService에 멤버 추가 시 함께 갱신해야 하는 테스트 fake 3곳 위치
metadata:
  type: project
---

MCPhoto(E:\Study\photobooth)에서 `MCPhoto.Core.Accounts.IAccountService`에 메서드를 추가하면
**테스트 fake 3곳**과 **레거시 구현 1곳**을 반드시 함께 갱신해야 빌드가 통과한다(CS0535).

- 실 구현: `src/MCPhoto.Firebase/AccountService.cs` — HTTP 전용 기능은 `NotSupportedException` 최소 대응 관례.
- 테스트 fake:
  - `tests/MCPhoto.Tests/LoginGuestViewModelTests.cs` (StubAccountService)
  - `tests/MCPhoto.Tests/PasswordResetViewModelTests.cs` (RecordingAccountService)
  - `tests/MCPhoto.Tests/AccountViewModelEmailTests.cs` (RecordingAccountService)

**Why:** HttpAccountService(백엔드) + AccountService(Firebase) 이중 구현 + VM 테스트 fake들이 모두
같은 인터페이스를 구현하므로, 인터페이스 확장이 여러 파일로 파급된다.
**How to apply:** IAccountService 시그니처를 바꾸면 이 4곳을 grep(`: IAccountService`, `: *IAccountService`)으로
찾아 동시에 수정하고 전체 솔루션 빌드로 확인한다.

관련: [[mcphoto-http-test-infra]]
