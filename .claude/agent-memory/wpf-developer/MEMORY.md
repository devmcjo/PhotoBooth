# wpf-developer 프로젝트 메모리 (MCPhoto)

- [WPF headless Window 테스트 함정](wpf-headless-window-test-pitfall.md) — Window를 테스트에서 new하면 Application 싱글턴/스레드충돌, 정적 StaticResource 키 검증으로 우회
- [인코딩 검증 방법](encoding-verify-method.md) — no-BOM이 진짜 게이트. autocrlf=true라 워킹카피 CRLF는 정상, 개행은 diff 오염으로 판정
- [it15 프레임 로컬 전용](it15-frame-local-only.md) — 편집=로컬 전용(fork 저장·Id=""로 #dbid 미기록·PUT 미호출), FrameCatalogService는 빈 목록을 절대 안 준다
- [TempUser 서버 권위](tempuser-server-authority.md) — QR 한도 진실원=서버(계정별), 클라 비영속·비권위·매번 조회·fail-open, 클라 카운트증가/영속 금지
- [it14 PIN 게이트 계약](it14-pin-gate-contract.md) — PIN 4자리 확정, E1/E2/E3 HTTP 계약. 게이트 분기 서술은 it15에서 폐기(아래 참조)
- [it15 클라 인증 계약](it15-client-auth-contract.md) — IAccountService 7메서드, AuthMethod=Google/Unknown, EnsurePinGateAsync 단일 게이트, 테스트 HasPin 함정
- [임시파일 write→read 경합](test-tempfile-write-read-race.md) — %TEMP% 쓰기 직후 읽기는 공유 위반으로 flaky. TestImageFile 경유 + IClassFixture로 봉쇄
- [it16 권한 축 3개](it16-permission-axes.md) — IsPower·CanWriteFrames·CanManage 혼용 금지. 관리 게이트는 IsPower+CanManage 2항, CanDelete는 소유자 미판정
- [it16 창 기하 계약](it16-window-geometry-contract.md) — _appliedMode가 모드변경 판정 유일 기준, 저장 시 캡처는 s.DisplayMode 갱신보다 먼저
- [dotnet format은 게이트 아님](dotnet-format-baseline-fails.md) — HEAD부터 실패(기존 한 줄 초기화자 관례). 재포맷 금지, WARN으로만 보고
- [로그아웃 JWT 폐기 불변식](logout-token-invariant.md) — IBackendSession은 동기화기가 소유. AddSingleton 한 줄로 되돌리면 런타임에서만 조용히 깨진다
- [합성 루트 테스트법](composition-root-testable.md) — 배선 결함은 ServiceRegistration을 조립해야 재현. IHttpClientFactory는 마지막 등록으로 덮어쓴다
