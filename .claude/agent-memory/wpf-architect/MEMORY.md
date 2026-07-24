# wpf-architect 프로젝트 메모리 (MCPhoto)

- [Camera Singleton 제약](camera-singleton-constraint.md) — 카메라 서비스 Singleton 공유·StartAsync 재시작 불가·CameraFramePresenter 재사용·Preview 데드코드
- [설정 INI 인프라](mcphoto-settings-ini-infra.md) — IniFile/SettingsPathResolver 재사용·실행경로 우선·설정 오버레이 네비게이션
- [it10 서버 키 배포](it10-server-key-distribution.md) — 키 번들은 publish.ps1 레벨(csproj 금지)·시드 폴백 D1~D3 미확정·프레임 이름 '_' 규약 함정
- [설정 게스트 편집 게이트](settings-guest-edit-gate.md) — 편집 권한 게이트 3지점(Load강제off/Save미기록/XAML IsEnabled)·VM계층만·런타임 불변·Toggle.Gated 툴팁
- [Firebase 접근 추상화](firebase-access-abstraction.md) — Core 인터페이스 5종으로 격리·구현만 HTTP 교체 시 UI 무변경·만료정리 앱 미호출
- [소스 파일 인코딩](source-file-encoding.md) — .cs는 UTF-8 no BOM(한글 주석)·수정/신규도 no BOM 유지
