# wpf-developer 프로젝트 메모리 (MCPhoto)

- [WPF headless Window 테스트 함정](wpf-headless-window-test-pitfall.md) — Window를 테스트에서 new하면 Application 싱글턴/스레드충돌, 정적 StaticResource 키 검증으로 우회
- [인코딩 검증 방법](encoding-verify-method.md) — .cs/.xaml은 no-BOM+LF, CR은 grep 말고 `tr -cd '\r'|wc -c`로 세야 정확(오탐)
