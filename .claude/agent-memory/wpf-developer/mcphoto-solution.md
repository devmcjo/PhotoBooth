---
name: mcphoto-solution
description: MCPhoto WPF 솔루션 구조·인코딩 규약·빌드검증 명령·백엔드 전용 HTTP 계층
metadata:
  type: project
---

MCPhoto = WPF(.NET 10) 포토부스 앱. 솔루션 루트 `C:\STUDY\PROJECT\PhotoBooth\MCPhoto.sln`
(구 경로 `E:\Study\photobooth`는 폐기 — 문서에 남아 있어도 무시할 것).

## 프로젝트 구성
- `MCPhoto.Core` (net10.0): 인터페이스·모델·`UploadContract`(순수)·`AppSettings`·`IniSettingsService`·`UploadService`. 계약의 진실.
- `MCPhoto.Http` (net10.0): **백엔드 HTTPS API 구현(유일한 서버 접근 경로)**. `Http{AccountService,FrameRepository,FirebaseClient}` + `IBackendSession`(JWT 홀더) + `HttpBackendClient`(공통 기반) + `Dto/`. HttpClient는 `IHttpClientFactory` 명명 클라이언트 "backend".
- `MCPhoto.Capture` (net10.0-windows): 카메라·ffmpeg.
- `MCPhoto.App` (net10.0-windows, WPF): 셸·VM·View·`ServiceRegistration`(DI, `internal static class`). `AssemblyName=MCPhoto`.
- `tests/MCPhoto.Tests` (net10.0-windows, xUnit): 전 계층 단위 테스트. `[Using Include="Xunit"]` 전역.

> ⚠️ **`MCPhoto.Firebase`(Admin SDK 직결)는 it15에서 프로젝트째 삭제됐다.** `AppSettings.UseBackend` feature flag와
> `serviceAccountKey.json`도 함께 사라졌다. 앱은 **백엔드 전용**이며 DI에 이중 경로 분기가 없다
> (`ServiceRegistration.RegisterBackendServices`). 옛 문서에 남은 "롤백용 공존"·"기본 OFF" 서술은 폐기된 내용이다.

## 규약 (반드시 준수)
- **인코딩: 전 `.cs`/`.xaml` UTF-8 no BOM** (선두 바이트 `6e 61 6d`=namespace 또는 `75 73 69`=using), **LF 개행**. Write/Edit이 이 관례 유지.
- `Directory.Build.props`가 전역 `Nullable=enable`·`ImplicitUsings=enable`·`LangVersion=12`. 신규 csproj에서 재선언 금지(중복). `GenerateDocumentationFile=false`(XML 주석 오류는 빌드 경고가 아니다 — 사람이 봐야 잡힌다).
- 예외↔UI 계약: 로그인 실패=`null`(예외 아님), 권한=`UnauthorizedAccessException`, 중복=`InvalidOperationException`.
- 내부 가시성: `MCPhoto.Capture`·`MCPhoto.App`가 `<InternalsVisibleTo Include="MCPhoto.Tests" />`로 내부 멤버 테스트 허용.

## 빌드·검증
- 빌드: `dotnet build MCPhoto.sln -c Debug --nologo -v q` (경고 0·오류 0 필수). Release도 동일 확인.
- 테스트: `dotnet test MCPhoto.sln -c Debug --nologo --no-build`.
- **기준선은 이터레이션마다 바뀐다 — 숫자를 여기 박아두지 말고 착수 시 직접 측정하라.** 참고 이력: it15 613 → it16 713 → 로그아웃 토큰 수정 721.
- 간헐 실패 감시: 전체 스위트 **5회 이상 연속** 실행으로 flake 없음을 확인할 것([[test-tempfile-write-read-race]]).
- 솔루션에 프로젝트 추가: `dotnet sln MCPhoto.sln add <csproj>` (src 폴더 자동 nesting + 6개 config 자동 추가).

## 백엔드 HTTP 계층
- 설계: `docs/design/wpf-backend-proxy-migration-design.md`(구조), `wpf-it15-google-only-auth-design.md`(현행 계약).
  서버(TS Cloud Functions) 계약의 진실 = `web/functions/src/{routes,services,domain}/*`.
- **UploadService/QrService/UploadContract 무변경 원칙**: `HttpFirebaseClient`가 per-file `UploadFileAsync`를
  prepare(1파일)→서명 PUT→토큰추출로 캡슐화. 반환 토큰으로 UploadService가 `TokenDownloadUrl(Bucket, path, token)`
  재조립→서버 downloadUrl과 동일(Bucket=서버 prepare 응답 버킷).
- `IsInitialized = base URL 설정 사실`. **실시간 헬스체크로 게이팅 금지** — 백그라운드 probe가 transient 실패로
  업로드를 잘못 차단한 버그가 있었다(제거됨). 도달성은 실제 호출 실패→상위 QR off/로컬저장 폴백으로 처리.
  명시 확인은 `HttpFirebaseClient.ProbeReachableAsync`(설정화면 표시용).
- 만료정리·DeleteAllByUser는 HTTP 경로에서 미지원/no-op — 앱 미호출 + 서버 엔드포인트 없음
  (만료는 인프라 TTL, cascade는 서버 계정삭제가 수행).
- 테스트: `tests/MCPhoto.Tests/Http/`의 `FakeHttpMessageHandler`+`TestHttpClientFactory`로 실서버 없이 검증
  (토큰 재사용·요청형·prepare→PUT→commit 순서·진행률·에러매핑·DI 배선).

관련: [[logout-token-invariant]], [[composition-root-testable]], [[it15-client-auth-contract]]
