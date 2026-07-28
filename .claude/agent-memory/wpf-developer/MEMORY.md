# wpf-developer 프로젝트 메모리 (MCPhoto)

- [WPF headless Window 테스트 함정](wpf-headless-window-test-pitfall.md) — Window를 테스트에서 new하면 Application 싱글턴/스레드충돌, 정적 StaticResource 키 검증으로 우회
- [인코딩 검증 방법](encoding-verify-method.md) — .cs/.xaml은 no-BOM+LF, CR은 grep 말고 `tr -cd '\r'|wc -c`로 세야 정확(오탐)
- [TempUser 서버 권위](tempuser-server-authority.md) — QR 한도 진실원=서버(계정별), 클라 비영속·비권위·매번 조회·fail-open, 클라 카운트증가/영속 금지
- [it14 PIN 게이트 계약](it14-pin-gate-contract.md) — PIN 4자리 확정, 게이트 분기=OpenSettings(SSO→PIN/그외→비번), IAccountService fake 5곳 무회귀, E1/E2/E3 HTTP 계약
