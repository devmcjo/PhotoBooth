---
name: mcphoto-solution
description: MCPhoto WPF 솔루션 구조·인코딩 규약·빌드검증 명령·백엔드 프록시 HTTP 계층(P3) 구현 결과
metadata:
  type: project
---

MCPhoto = WPF(.NET 8) 포토부스 앱. 솔루션 루트 `E:\Study\photobooth\MCPhoto.sln`.

## 프로젝트 구성
- `MCPhoto.Core` (net8.0): 인터페이스·모델·`UploadContract`(순수)·`AppSettings`·`IniSettingsService`. 계약의 진실.
- `MCPhoto.Firebase` (net8.0): Admin SDK 직결 구현(레거시, 롤백용 공존).
- `MCPhoto.Http` (net8.0): 백엔드 HTTPS 프록시 구현(P3). `Http{AccountService,FrameRepository,FirebaseClient}` + `IBackendSession`(JWT 홀더) + `HttpBackendClient`(공통 기반) + `Dto/`. HttpClient는 `IHttpClientFactory` 명명 클라이언트 "backend".
- `MCPhoto.Capture` (net8.0-windows): 카메라·ffmpeg.
- `MCPhoto.App` (net8.0-windows, WPF): 셸·VM·View·`ServiceRegistration`(DI). `AssemblyName=MCPhoto`.
- `tests/MCPhoto.Tests` (net8.0-windows, xUnit): 전 계층 단위 테스트. `[Using Include="Xunit"]` 전역.

## 규약 (반드시 준수)
- **인코딩: 전 `.cs`/`.xaml` UTF-8 no BOM** (선두 바이트 `6e 61 6d`=namespace 또는 `75 73 69`=using), **LF 개행**. Write/Edit이 이 관례 유지.
- `Directory.Build.props`가 전역 `Nullable=enable`·`ImplicitUsings=enable`·`LangVersion=12`. 신규 csproj에서 재선언 금지(중복).
- 예외↔UI 계약: 로그인 실패=`null`(예외 아님), 권한=`UnauthorizedAccessException`, 중복=`InvalidOperationException`.
- 내부 가시성: `MCPhoto.Capture`·`MCPhoto.App`가 `<InternalsVisibleTo Include="MCPhoto.Tests" />`로 내부 멤버 테스트 허용.

## 빌드·검증
- 빌드: `dotnet build MCPhoto.sln -c Debug --nologo -v q` (경고 0·오류 0 필수). Release도 동일 확인.
- 테스트: `dotnet test MCPhoto.sln -c Debug --nologo --no-build`. **P3 완료 기준선 = 402 통과**(이전 366 + P3 36).
- 솔루션에 프로젝트 추가: `dotnet sln MCPhoto.sln add <csproj>` (src 폴더 자동 nesting + 6개 config 자동 추가).

## 백엔드 프록시 HTTP 계층 (P3, 방향 B)
- 설계: `docs/design/wpf-backend-proxy-migration-design.md`. 서버(TS Cloud Functions) 계약의 진실 = `web/functions/src/{routes,services,domain}/*`.
- **DI feature flag**: `AppSettings.UseBackend`(기본 OFF=현행 Firebase 유지·롤백 가능). `ServiceRegistration.RegisterBackendOrFirebase`가 인터페이스 팩토리에서 설정 읽어 분기. `NormalizeBackend()`(Clamp 내)가 **빈 BackendBaseUrl → UseBackend 강제 off**(안전 불변식) + base URL 슬래시 보정(HttpClient.BaseAddress 상대결합).
- **UploadService/QrService/UploadContract 무변경**: `HttpFirebaseClient`가 per-file `UploadFileAsync`를 prepare(1파일)→서명 PUT→토큰추출로 캡슐화. 반환 토큰으로 UploadService가 `TokenDownloadUrl(Bucket, path, token)` 재조립→서버 downloadUrl과 동일(Bucket=서버 prepare 응답 버킷).
- `IsInitialized = base URL 설정 사실`(현행 "키 로드됨" 아날로그). **실시간 헬스체크로 게이팅 금지**(백그라운드 probe가 transient 실패로 업로드를 잘못 차단한 버그 있었음 → 제거). 도달성은 실제 호출 실패→상위 QR off/로컬저장 폴백으로 처리. 명시 확인은 `HttpFirebaseClient.ProbeReachableAsync`(설정화면 표시용).
- U3/U4/U5(만료정리)·EnsureSeed·DeleteAllByUser는 HTTP 경로에서 미지원(NotSupportedException)/no-op — 앱 미호출 + 서버 엔드포인트 없음(만료는 인프라 TTL, cascade는 서버 계정삭제가 수행).
- 테스트: `tests/MCPhoto.Tests/Http/`의 `FakeHttpMessageHandler`+`TestHttpClientFactory`로 실서버 없이 검증(로그인 토큰 재사용·요청형·prepare→PUT→commit 순서·진행률·에러매핑·DI 분기).
