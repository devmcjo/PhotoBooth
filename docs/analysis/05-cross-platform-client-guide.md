# 05 · 멀티플랫폼 클라이언트 개발 가이드 (진입 문서)

| 항목 | 내용 |
|------|------|
| 문서 | iOS · iPadOS · Android · macOS · 웹 프론트엔드 클라이언트를 새로 만드는 개발자의 **첫 문서** |
| 범위 | 플랫폼 중립 용어 사전, 클라이언트 프로파일 정의, 기능×플랫폼 지원 매트릭스, **Windows 전용** 항목 식별, 기술 대체 표, 반드시 지켜야 하는 불변식, 착수 전 해결해야 하는 서버 변경 |
| 최종 업데이트 | 2026-07-30 (신규 — 멀티플랫폼 확장 대비 문서 계층 추가) |
| 관련 소스 | 저장소 전역. 근거가 특정 파일에 있을 때만 `파일:라인`으로 표기 |
| 갱신 규칙 | 새 클라이언트 플랫폼을 추가하거나, 서버가 플랫폼별 분기를 갖게 되면 §5·§7·§9를 갱신한다. 프로파일 정의(§4)가 바뀌면 [13](./13-client-behavior-spec.md)의 화면 목록과 동시 갱신 |

> **이 문서를 먼저 읽어야 하는 이유**: `docs/analysis`의 10·11·12·70·80번은 **현재 유일한 구현(Windows 데스크톱)** 을 기술한 문서다. 그 문서들은 WPF 클래스명·`%ProgramData%` 경로·`ffmpeg.exe` 같은 Windows 고유 어휘를 그대로 쓴다. 새 플랫폼 클라이언트는 **그 문서를 "구현 예시"로만 참조**하고, 실제 규격은 아래 §10의 플랫폼 중립 4문서를 진실원으로 삼는다.

---

## 1. 시스템에서 "클라이언트"란 무엇인가

MC포토는 **서버(백엔드 API) 중심 아키텍처**다. 클라이언트는 다음 4가지만 한다.

1. 촬영·합성 같은 **로컬 미디어 작업** (기기 카메라·인코더 사용)
2. 백엔드 HTTPS API 호출 (`/auth` `/accounts` `/config` `/frames` `/uploads` `/health`)
3. 서버가 발급한 **서명 URL로 Storage에 파일 바이트 직접 PUT**
4. 로컬 영속(설정·프레임·결과물 사본)

클라이언트는 **DB·Storage에 직접 접근하지 않고, 관리자 권한을 갖지 않는다**. 새 플랫폼을 만들 때 "Firebase SDK로 Firestore를 직접 읽자"는 선택지는 **없다** — 유일한 예외는 다운로드 웹 클라이언트의 `resultSessions` 단건 get이다([40 §5](./40-database-firestore-and-storage-schema.md)).

**서버가 진실원인 것 (클라이언트가 판정해도 표시용에 불과함)**

| 항목 | 서버 판정 지점 | 클라이언트가 하면 안 되는 것 |
|------|----------------|------------------------------|
| 역할·권한 | JWT의 `role` 클레임으로 매 요청 재검증 | 클라가 계산한 역할을 요청에 실어 보내기(서버는 무시) |
| 무료 사용 한도(TempUser QR) | `prepare` 선검사 + `commit` 트랜잭션 재검사 | 로컬 카운터로 한도 관리 |
| 결과물 만료 시각 | `commit` 시점의 서버 시계로 `createdAt`·`expiresAt` 기록 | 클라 시계로 만료 판정해 문서에 기록 |
| 계정 생성·역할 승격 | Google SSO 최초 로그인 시 `temp_user` 자동 생성, 승격은 관리 API | 클라에서 계정 생성 |
| Storage 경로·Content-Type | `prepare`가 서명에 고정 | 임의 경로로 업로드 |

---

## 2. 용어 사전 — Windows/WPF 어휘 → 플랫폼 중립 어휘

기존 문서를 읽을 때 아래 표로 치환하면 플랫폼 종속 어휘에 걸리지 않는다.

| 기존 문서의 표현 | 중립 표현 | 비고 |
|------------------|-----------|------|
| Exe 앱 / WPF 앱 / 키오스크 본체 | **촬영 클라이언트** | 현재 구현이 Windows EXE라서 생긴 이름 |
| 웹 다운로드 페이지 | **소비자 클라이언트** | 결과물 열람·다운로드 전용 |
| `AppState` / 상태머신 | **화면 상태(Screen State)** | [13 §2](./13-client-behavior-spec.md) |
| ViewModel / `*ViewModel` | **화면 로직(Presenter)** | MVVM은 WPF의 선택이지 규격이 아님 |
| `AppSettings` / `MCPhoto.ini` | **클라이언트 설정(Client Settings)** | 키 이름은 계약, 저장 형식은 플랫폼 자유 ([41](./41-local-data-and-file-formats.md)) |
| `%ProgramData%\MCPhoto` | **앱 데이터 디렉터리** | 플랫폼별 위치 다름 ([41 §5](./41-local-data-and-file-formats.md)) |
| `ILocalFrameStore` / `Frame\` 폴더 | **로컬 프레임 저장소** | 파일 포맷은 계약 ([41 §3](./41-local-data-and-file-formats.md)) |
| `ICameraService` / OpenCvSharp | **카메라 소스(Camera Source)** | [14 §2](./14-media-pipeline-spec.md) |
| `ffmpeg.exe` / `FfmpegRunner` | **비디오 인코더(Video Encoder)** | [14 §6](./14-media-pipeline-spec.md) |
| `CompositionService` | **합성기(Compositor)** | [14 §5](./14-media-pipeline-spec.md) |
| 표시 모드(전체화면/창모드) | **키오스크 표시 모드** | 데스크톱 전용 개념 |
| `IBackendSession` | **세션 토큰 홀더** | JWT 메모리 보관 |
| `explorer.exe`로 로그 폴더 열기 | **로그 위치 노출/공유** | Windows 전용 구현 |
| `X-MCPhoto-Client` | **배포 게이트 키** | 헤더 이름 자체는 계약(변경 불가) |
| 파워(power) 계정 | manager 또는 admin | 코드 전반의 관용어 |

---

## 3. 클라이언트가 백엔드와 맺는 계약(요약)

세부는 [31 백엔드 API 참조](./31-backend-api-reference.md)가 진실원이다. 착수 전 알아야 할 최소치만 적는다.

| 항목 | 값 |
|------|-----|
| Base URL | `https://asia-northeast3-mcphoto-955fb.cloudfunctions.net/api` (운영 기본값. 오버라이드 가능) |
| 필수 헤더 ① | `X-MCPhoto-Client: {배포 게이트 키}` — 게스트도 통과해야 하는 엔드포인트에 필요 |
| 필수 헤더 ② | `Authorization: Bearer {JWT}` — 로그인 필요 엔드포인트 |
| JWT | HS256, 클레임 `sub`(계정 id)·`role`, 기본 만료 **8시간**. **디스크에 저장하지 않는다**(메모리 전용) |
| 에러 봉투 | `{"error":{"code":"...","message":"..."}}` — 전 엔드포인트 공통 |
| 본문 상한 | JSON `256KB`. 파일 바이트는 API를 경유하지 않는다 |
| 파일 업로드 | 3단계: `POST /uploads/prepare` → **서명 URL로 직접 PUT** → `POST /uploads/commit` |

---

## 4. 클라이언트 프로파일 (P1~P4)

새 플랫폼이 "MC포토 앱을 만든다"고 할 때 실제로는 4개의 서로 다른 제품 중 하나 이상을 만드는 것이다. 프로파일을 먼저 고르면 범위가 확정된다.

| 프로파일 | 이름 | 하는 일 | 필요 권한 | 로컬 미디어 처리 |
|----------|------|---------|-----------|------------------|
| **P1** | 소비자(Consumer) | QR/링크로 진입 → 결과물 미리보기·다운로드·만료 안내 | 없음(비인증) | 없음 |
| **P2** | 촬영(Capture) | 프레임 선택 → N컷 촬영 → 컷 선택 → 합성·필터 → 타임랩스 → 업로드·QR → 로컬 저장 | 게스트 가능 | **필수**(카메라·합성·인코딩) |
| **P3** | 저작(Authoring) | 프레임 이미지 업로드 → 슬롯 배치 → 저장(로컬 / power는 공용 DB 등록) | `advanced_user` 이상 | 이미지 처리만 |
| **P4** | 운영(Admin) | 계정 목록·삭제·역할 변경·타 계정 PIN 재설정·전역 한도·진단 | `manager`/`admin` | 없음 |

**플랫폼별 권장 조합**

| 플랫폼 | 권장 프로파일 | 근거 |
|--------|---------------|------|
| **iPadOS** | P2 + P3 (+P4) | 거치형 키오스크로 쓸 수 있는 유일한 모바일 폼팩터. 후면 카메라·큰 화면·전용 모드 지원 |
| **macOS** | P2 + P3 + P4 | Windows 데스크톱과 동등한 능력. 현재 Windows 클라이언트의 1:1 대응 대상 |
| **Android** | P2(태블릿) / P1+P4(폰) | 폰에서 6~10컷 키오스크 촬영은 UX 부적합. 태블릿은 iPadOS와 동급 |
| **iOS(폰)** | P1 (+P4) | 손님이 QR로 여는 소비자 흐름이 주. 촬영은 비권장(§5 참고) |
| **웹 프론트엔드** | P1(현행) + **P4 관리 콘솔** | P4를 웹으로 빼면 현장 기기에서 운영 기능을 제거할 수 있다. P2는 브라우저 제약이 큼(§7) |

> 프로파일은 **역할과 다르다.** P4를 구현했다고 아무나 쓸 수 있는 게 아니라, 서버가 `requirePower()`/`requireAdmin()`으로 거부한다. UI 미노출 + 커맨드 가드 + 서버 강제의 3중 방어가 현재 규약이며 새 클라이언트도 같은 규약을 따른다([60 §2](./60-auth-accounts-and-roles.md)).

---

## 5. 기능 × 플랫폼 지원 매트릭스

`○`=그대로 이식 가능, `△`=플랫폼 대체 기술 필요, `✕`=해당 플랫폼에서 의미 없음/불가, **WIN**=현재 Windows 전용 구현.

| 기능 | 프로파일 | Windows(현행) | macOS | iPadOS | iOS(폰) | Android | 웹 |
|------|:--------:|:-------------:|:-----:|:------:|:-------:|:-------:|:--:|
| 결과물 열람·다운로드(만료 판정) | P1 | — | ○ | ○ | ○ | ○ | ○(현행) |
| Google SSO 로그인 | 전부 | ○ (loopback) | △ | △ | △ | △ | △ |
| 진입 PIN 게이트(4자리) | P3·P4 | ○ | ○ | ○ | ○ | ○ | ○ |
| 프레임 목록 조회(공용+본인) | P2·P3 | ○ | ○ | ○ | ○ | ○ | ○ |
| 카메라 프리뷰(거울·중앙크롭) | P2 | ○ WIN(DirectShow) | △ | △ | △ | △ | △(getUserMedia) |
| 카메라 장치 선택·열거·이름 | P2 | ○ WIN(WMI) | △ | △(제한적) | ✕(전/후면만) | △ | △ |
| 카메라 테스트 모달 | P2 | ○ | ○ | ○ | ○ | ○ | ○ |
| N컷 연속 촬영 + 카운트다운 | P2 | ○ | ○ | ○ | △(폼팩터) | ○ | △(탭 스로틀·화면 꺼짐) |
| 화면 플래시(하양 오버레이 120ms) | P2 | ○ | ○ | ○ | ○ | ○ | ○ |
| 셔터음 | P2 | ○ | ○ | △(무음 스위치) | △ | ○ | △(자동재생 정책) |
| 세션 전체 녹화(무음 H.264 **mp4**) | P2 | ○ WIN(ffmpeg) | △ | △ | △ | △ | **✕(mp4/H.264 보장 불가 → 계약 위반)** |
| 타임랩스 배속 변환 | P2 | ○ WIN(ffmpeg) | △ | △ | △ | △ | ✕→서버/미제공 |
| 결과 합성(프레임+컷+필터) | P2 | ○ WIN(OpenCV) | △ | △ | △ | △ | △(Canvas) |
| 필터(흑백/밝게/뷰티) | P2 | ○ WIN(OpenCV) | △ | △ | △ | △ | △ |
| 업로드 3단계 + QR 표시 | P2 | ○ | ○ | ○ | ○ | ○ | △(버킷 CORS 필요, §9) |
| 로컬 결과물 영구 저장 | P2 | ○ | ○ | △(사진 앱/공유) | △ | △(MediaStore) | **✕(M6 불변식 충족 불가)** |
| 프레임 생성·편집(슬롯 배치) | P3 | ○ | ○ | ○ | △(화면 협소) | △ | ○ |
| 프레임 로컬 저장(`.png`+`.slots`) | P3 | ○ | ○ | ○ | ○ | ○ | **✕(영속 보장 불가 + 소비자 부재)** |
| 공용 기본 프레임 DB 등록 | P3(power) | ○ | ○ | ○ | ○ | ○ | ○ |
| 계정·역할·PIN 관리 | P4 | ○ | ○ | ○ | ○ | ○ | ○ |
| 전역 TempUser 한도 편집 | P4(admin) | ○ | ○ | ○ | ○ | ○ | ○ |
| 유휴 감시(2분→10초 카운트다운) | P2 | ○ | ○ | ○ | ○ | ○ | ○ |
| 키오스크 표시 모드(전체화면/창) | P2 | ○ WIN | △(창 개념 있음) | △(가이드 접근/전용 모드) | △ | △(고정 모드) | △(Fullscreen API) |
| 창 위치·크기 기억 | P2 | ○ WIN | ○ | ✕ | ✕ | ✕ | ✕ |
| 설정 파일 경로 폴백 3단 | 전부 | ○ WIN | ✕(개념 불필요) | ✕ | ✕ | ✕ | ✕ |
| 로그 파일 + 폴더 열기 | 전부 | ○ WIN(`explorer.exe`) | △ | △(공유 시트) | △ | △ | ✕(콘솔) |
| 진단·상태 화면 | 전부 | ○ | ○ | ○ | ○ | ○ | ○ |
| 앱 종료(관리자) | P4 | ○ WIN | △ | ✕(OS가 금지) | ✕ | △ | ✕ |
| 버전 표기(`bldinfo` 외부 파일) | 전부 | ○ WIN | △ | ✕(번들 버전 사용) | ✕ | ✕ | ✕ |
| 브랜딩(앱 이름·소제목 외부 파일) | 전부 | ○ WIN | △ | △(원격 구성 권장) | △ | △ | △ |
| 단일 실행파일 배포·인스톨러 | — | ○ WIN | ✕(`.app`/notarize) | ✕(스토어) | ✕ | ✕ | ✕ |

> **웹 열의 `△`·`✕` 판정 근거와 대안은 [§7.4 웹 클라이언트 제약 상세](#74-웹-클라이언트-제약-상세-브라우저에서-어려운-것)에 기능별로 정리돼 있다.** 웹은 다른 플랫폼과 달리 "구현 난도" 문제가 아니라 **계약·불변식을 만족할 수 없는 항목이 존재**하므로 범위를 별도로 판정해야 한다.

### 5.1 Windows 전용으로 못 박아야 하는 것 (이식 대상 아님)

아래는 **Windows 구현 세부**이며 규격이 아니다. 새 클라이언트는 같은 *목적*만 달성하면 되고, 방식은 자유다.

| Windows 전용 항목 | 목적(이식해야 하는 것) | 근거 문서 |
|-------------------|------------------------|-----------|
| `MCPhoto.ini` INI 파일 + 실행경로→ProgramData→LocalAppData 3단 폴백 | 설정 영속 + **쓰기 실패를 사용자에게 정직히 알리기** | [12 §2](./12-exe-app-settings-and-config.md), [41 §2](./41-local-data-and-file-formats.md) |
| `%ProgramData%\MCPhoto\logs\mcphoto-YYYYMMDD.log` 일 롤링 14일 | 현장 진단용 로그 영속 + 위치 노출 | [70 §1](./70-logging-and-troubleshooting.md) |
| `explorer.exe`로 로그 폴더 열기 | 운영자가 로그를 꺼낼 수 있는 경로 제공 | [11 §17](./11-exe-app-features.md) |
| `tools/ffmpeg/ffmpeg.exe` 번들 + stdin rawvideo 파이프 | 세션 녹화·타임랩스 생성 | [14 §6](./14-media-pipeline-spec.md) |
| OpenCV DirectShow `VideoCapture(index)` + WMI FriendlyName | 카메라 프리뷰·스틸·장치 선택 | [14 §2](./14-media-pipeline-spec.md) |
| `WindowStyle`/`WindowState`/`WindowBounds` 표시 모드 정책 | 키오스크 몰입 모드 진입·이탈 | [11 §16](./11-exe-app-features.md) |
| `HttpListener` loopback + 시스템 기본 브라우저 | Google 인가 코드 수신 | [61 §3](./61-auth-platform-integration.md) |
| `branding.ini` / `bldinfo.ini` 외부 파일 | 고객사별 앱 이름·버전 표기 | [12 §3·§6](./12-exe-app-settings-and-config.md) |
| `publish.ps1` 단일 EXE + Inno Setup | 배포 | [80](./80-build-and-deployment.md) |
| 실행 폴더 `Frame\` 단일 디렉터리 + 접두 규칙 | 로컬 프레임 저장·공용/개인 구분 | [41 §3](./41-local-data-and-file-formats.md) |

---

## 6. 반드시 지켜야 하는 불변식 (클라이언트 적합성 MUST)

새 클라이언트가 이 중 하나라도 깨면 **과금 사고·데이터 오귀속·보안 결함**이 된다. 각 항목은 현재 Windows 구현에서 실제 결함으로 발견돼 고쳐진 이력이 있다.

| # | 불변식 | 깨질 때 생기는 일 | 근거 |
|---|--------|-------------------|------|
| **M1** | **로그아웃 시 JWT를 즉시 폐기**한다. 세션 사용자 해제와 토큰 폐기는 한 지점(통지 구독)에서 함께 일어나야 한다 | 업로드는 *선택적 Bearer*라서 남은 토큰이 조용히 붙는다 → 로그아웃 직후 게스트 촬영물이 **직전 계정 소유로 기록**되고 TempUser면 무료 횟수까지 차감 | [60 §3.5](./60-auth-accounts-and-roles.md), [30 §3.1](./30-backend-firebase-integration.md) |
| **M2** | JWT는 **메모리에만** 둔다(디스크·keychain 영속 금지) | 공용 키오스크에서 토큰 재사용 위험. 현재 설계는 프로세스 수명 = 세션 수명 | [60 §3.1](./60-auth-accounts-and-roles.md) |
| **M3** | **유휴 타임아웃은 로그아웃하지 않는다.** 홈 복귀 + 촬영 데이터 폐기만 | 로그인 사용자가 프레임 편집 중 잠깐 손 뗐다고 로그아웃되면 작업 유실 | [60 §3.5](./60-auth-accounts-and-roles.md) |
| **M4** | **성공 오인 금지.** 저장·삭제·업로드 실패를 조용히 넘기지 않고 반드시 사용자에게 표시 | 설정이 저장 안 됐는데 저장됐다고 표시 / 서버에 프레임이 남았는데 삭제됐다고 표시 | [12 §2.4](./12-exe-app-settings-and-config.md), [11 §4.2](./11-exe-app-features.md) |
| **M5** | **QR은 업로드 성공 후에만** 표시한다. 실패 시 QR 숨기고 로컬 보존 안내 + 재시도 제공 | 손님이 죽은 링크를 스캔 | [11 §9.1](./11-exe-app-features.md) |
| **M6** | 결과물 **로컬 저장은 업로드 분기 이전**에 수행한다 | 업로드 실패 = 결과물 유실 | [11 §8](./11-exe-app-features.md) |
| **M7** | `finalImageUrl`·`timelapseUrl` 중 **최소 1개는 non-null**. 둘 다 끄면 업로드 자체를 하지 않는다 | 빈 다운로드 페이지 문서 생성 | [40 §7](./40-database-firestore-and-storage-schema.md) |
| **M8** | 미만료 문서에서 URL이 null이면 **"전송 옵션 꺼짐"**이며 만료·실패와 구분해 안내한다 | 손님이 "만료됐다"고 오해 | [20 §6](./20-frontend-web-download-page.md) |
| **M9** | PIN 게이트는 **fail-closed**(확인 불가 시 진입 거부), 무료 한도 조회는 **fail-open**(허용하고 서버가 최종 거부) | 반대로 하면 각각 인증 우회 / 장애 시 전면 차단 | [60 §4.5](./60-auth-accounts-and-roles.md) |
| **M10** | 권한 판정은 **UI 미노출 + 커맨드 가드 + 서버 강제** 3중으로 한다 | 클라 UI만 숨기면 우회 가능 | [60 §2](./60-auth-accounts-and-roles.md) |
| **M11** | 프레임은 **촬영 시작 전에 확정**되고 이후 변경 불가. 필터만 결과 화면에서 변경 가능 | 슬롯 수가 바뀌면 컷 선택 규칙이 깨진다 | [13 §4](./13-client-behavior-spec.md) |
| **M12** | 컷 선택은 **정확히 슬롯 수만큼**, **선택 순서 = 슬롯 순서** | 합성 시 컷/슬롯 개수 불일치 예외 | [14 §5](./14-media-pipeline-spec.md) |
| **M13** | 세션 ID 형식 `{yyyyMMdd_HHmmss}_{UUIDv4}`를 정확히 지킨다(서버가 정규식 검증) | `prepare` 400. 순차 ID·다른 형식 금지 | [31 §7](./31-backend-api-reference.md) |
| **M14** | 서명 PUT 시 `prepare`가 준 `requiredHeaders`를 **그대로 전부** 부착한다 | 서명 불일치 403, 또는 다운로드 토큰 메타 누락으로 파일 GET 불가 | [31 §7.2](./31-backend-api-reference.md) |
| **M15** | 프레임 이름에 `_`를 쓰지 않는다(서버 400) — 로컬 저장소의 공용/개인 구분자 | 저장 실패 또는 공용 목록에서 사라짐 | [41 §3.2](./41-local-data-and-file-formats.md) |
| **M16** | 크래시 대신 복구: 미처리 예외는 로그 + 홈 복귀 | 무인 키오스크가 죽은 채 방치 | [10 §5.1](./10-exe-app-architecture.md) |

---

## 7. 플랫폼별 기술 대체 표

각 행의 "요구 사항"은 규격이고, 오른쪽은 그 요구를 만족하는 플랫폼 API 후보다. **성능·품질 요구는 [14](./14-media-pipeline-spec.md)에 수치로 있다.**

### 7.1 카메라·미디어

| 요구 사항 | Windows(현행) | macOS | iOS/iPadOS | Android | 웹 |
|-----------|---------------|-------|------------|---------|-----|
| 프리뷰 프레임 스트림(BGR/RGB 버퍼 접근) | OpenCV `VideoCapture`(DSHOW) | AVFoundation `AVCaptureVideoDataOutput` | 동일 | CameraX `ImageAnalysis` / Camera2 | `getUserMedia` + `<video>`→Canvas |
| 스틸 캡처(프리뷰와 동일 가공) | 프리뷰 버퍼 복제 | `AVCapturePhotoOutput` 또는 프리뷰 프레임 복제 | 동일 | `ImageCapture` 또는 프레임 복제 | Canvas `drawImage` |
| 좌우반전(거울) | `Cv2.Flip(FlipMode.Y)` | `CIImage` 변환 / Metal | 동일 | Matrix / GL | CSS `transform: scaleX(-1)` + Canvas 반영 |
| 중앙 크롭(목표 종횡비) | `CropCalculator` + ROI | `CIImage.cropped` | 동일 | `Bitmap` crop | Canvas 소스 사각형 |
| 세션 동영상 녹화(H.264 무음 mp4) | ffmpeg stdin rawvideo | `AVAssetWriter` | 동일 | `MediaRecorder` / `MediaCodec` | `MediaRecorder`(webm→변환 필요) |
| 타임랩스 배속(setpts 등가) | ffmpeg `setpts` | `AVMutableComposition.scaleTimeRange` | 동일 | `MediaCodec` 재타임스탬프 / mp4parser | ✕ — 미제공 또는 서버 처리 검토 |
| 이미지 합성·필터 | OpenCV | Core Image / Accelerate | 동일 | RenderScript 대체(Canvas/GL/`ColorMatrix`) | Canvas 2D / WebGL |
| QR 코드 생성 | QRCoder | CoreImage `CIQRCodeGenerator` | 동일 | ZXing | `qrcode` 계열 라이브러리 |

> **핵심 원칙(WYSIWYG)**: 프리뷰·스틸·녹화가 **같은 가공(거울 → 중앙 크롭)을 프레임당 1회** 거쳐야 한다. 프리뷰만 반전하고 저장 이미지는 원본이면 손님이 본 것과 결과물이 달라진다. 이는 현재 구현의 명시적 설계 원칙이다([10 §4](./10-exe-app-architecture.md)).

### 7.2 저장·구성

| 요구 사항 | Windows(현행) | macOS | iOS/iPadOS | Android | 웹 |
|-----------|---------------|-------|------------|---------|-----|
| 클라이언트 설정 영속 | `MCPhoto.ini` 3단 폴백 | `~/Library/Application Support/…` (plist/JSON) | `UserDefaults` 또는 앱 지원 디렉터리 JSON | `DataStore`/SharedPreferences | `localStorage` |
| 로컬 프레임 저장소 | 실행폴더 `Frame\{이름}.png` + `.slots` | 앱 지원 디렉터리 | 앱 Documents/Library | `filesDir` | IndexedDB(Blob) |
| 세션 임시 작업 폴더 | `%ProgramData%\MCPhoto\sessions\{guid}` | `NSTemporaryDirectory` | `tmp`/캐시 | `cacheDir` | 메모리/OPFS |
| 결과물 영구 보관 | `{실행경로}\result\mcphoto_YYMMDD_HHMM\` | `~/Pictures` 또는 지정 경로 | Photos 라이브러리 저장(권한 필요) | `MediaStore.Images/Video` | 브라우저 다운로드 |
| 로그 영속 | Serilog 파일 일 롤링 | `os_log` + 파일 | 파일 + 공유 시트 | 파일 + `logcat` | 콘솔(영속 불가) |
| 브랜딩·버전 | 외부 INI | 외부 파일 또는 번들 | 번들 `Info.plist` + 원격 구성 | `BuildConfig` + 원격 구성 | 빌드 상수 |

### 7.3 인증

전용 문서 [61 · 플랫폼별 인증 통합](./61-auth-platform-integration.md)에 상세가 있다. 요약:

| 플랫폼 | 인가 코드 수신 방식 | Google OAuth 클라이언트 유형 | 서버 변경 필요 |
|--------|---------------------|------------------------------|:--------------:|
| Windows/macOS 데스크톱 | loopback `http://127.0.0.1:{port}/` | Desktop app | 없음(현행) |
| iOS/iPadOS/macOS(앱) | `ASWebAuthenticationSession` + 역방향 클라이언트 ID 커스텀 스킴 | iOS | **필요** |
| Android | Custom Tabs + 커스텀 스킴/App Link | Android | **필요** |
| 웹 | 브라우저 리디렉트 `https://{도메인}/oauth/callback` | Web application | **필요** |

> ⚠️ **현재 서버는 데스크톱 loopback만 허용한다.** `POST /auth/google`의 `redirectUri`는 `http://127.0.0.1` 또는 `http://localhost`만 통과하고(그 외 400), audience는 단일 `GOOGLE_OAUTH_CLIENT_ID`로 고정돼 있다. **모바일·웹 클라이언트를 붙이려면 서버 변경이 선행돼야 한다** — §9 참조.

### 7.4 웹 클라이언트 제약 상세 (브라우저에서 어려운 것)

웹은 다른 플랫폼과 성격이 다르다. macOS·iOS·Android는 "다른 API로 같은 것을 한다"(대체 가능)지만, 웹에는 **어떤 API를 써도 계약·불변식을 만족할 수 없는 항목**이 있다. 그 구분을 먼저 명확히 한다.

| 등급 | 의미 |
|------|------|
| **W-BLOCK** | 계약 또는 불변식을 **만족할 수 없다.** 우회 불가 → 해당 기능을 웹 범위에서 제외해야 한다 |
| **W-RISK** | 구현은 되지만 **브라우저·OS 정책에 따라 깨진다.** 운영 조건(설치형 PWA·키오스크 모드 등)을 강제해야 한다 |
| **W-COST** | 되지만 비용·품질 손실이 크다. 축소 결정으로 회피할 수 있다 |
| **W-OK** | 문제 없다 |

#### 7.4.1 W-BLOCK — 계약·불변식 위반 (웹 범위에서 제외해야 함)

| # | 항목 | 무엇이 막히나 | 근거 |
|---|------|---------------|------|
| **WB1** | **세션 녹화 → 타임랩스** | 서버 `validateUploadFile`이 타임랩스에 대해 **`ext: "mp4"` + `contentType: "video/mp4"` 만** 허용한다. 브라우저 `MediaRecorder`가 무엇을 만드는지는 브라우저가 결정하며 **H.264/mp4 산출을 보장할 수 없다**(webm/VP8·VP9가 흔하다). webm을 올릴 경로가 계약에 **없으므로 400이다.** 배속 변환 수단도 순수 웹에 없다 | [31 §5.1·§8](./31-backend-api-reference.md), [14 §7](./14-media-pipeline-spec.md) |
| **WB2** | **결과물 로컬 영구 보관** | 불변식 **M6**은 "로컬 저장을 업로드 **이전**에 완료"를 요구한다. 브라우저에서 파일을 사용자 저장소로 내보내는 유일한 수단은 다운로드/파일 저장 대화상자이며 **사용자 제스처가 필요**하다. 결과 화면의 [다음] 클릭을 제스처로 쓴다 해도 사진+영상 다중 파일 저장은 팝업/다중 다운로드 차단에 걸린다. 즉 **"업로드 실패해도 결과물이 기기에 남는다"를 보장할 수 없다** | [05 §6 M6](#6-반드시-지켜야-하는-불변식-클라이언트-적합성-must), [41 §5](./41-local-data-and-file-formats.md) |
| **WB3** | **개인(로컬) 프레임 저장** | ① **영속 보장 불가**: IndexedDB·localStorage는 언제든 회수 대상이다. `navigator.storage.persist()`는 Chromium이 휴리스틱으로 판단하고 **WebKit은 사실상 미지원**이며, WebKit은 설치되지 않은 사이트의 script-writable 저장소를 **약 7일 무상호작용 시 삭제**한다(iOS는 모든 브라우저가 WebKit). 행사 단위(주·월) 운영 주기와 정면으로 안 맞는다. ② 더 근본적으로 **소비자가 없다**: 개인 프레임은 로컬 전용 정책이라 동기화가 없고, 촬영은 키오스크에서 일어난다 → 웹에서 만든 개인 프레임은 **지워지기 전에도 쓸 곳이 없다** | [41 §3.1](./41-local-data-and-file-formats.md) |
| **WB4** | **앱 종료(관리자)** | 스크립트로 탭을 닫는 것은 스크립트가 열은 창에만 허용된다. 브라우저 탭을 종료시킬 수 없다 | — |
| **WB5** | **로그 영속** | 파일 로그가 없다. 새로고침·탭 종료로 콘솔이 비워진다 → **현장 진단 능력을 잃는다**. 원격 수집을 붙이지 않으면 대체 수단이 없다 | [41 §8](./41-local-data-and-file-formats.md) |

> **WB1·WB2가 함께 의미하는 것**: 웹에서 P2 촬영을 만들면 **타임랩스 없는 + 결과물이 기기에 남지 않는** 반쪽 촬영이 된다. 업로드가 실패하면 손님의 사진이 그대로 사라진다. 키오스크 제품으로서 받아들일 수 있는 축소가 아니다.

#### 7.4.2 W-RISK — 정책 의존 (운영 조건을 강제해야 함)

| # | 항목 | 위험 | 완화 |
|---|------|------|------|
| **WR1** | **클라이언트 설정 영속** | `localStorage`도 WB3과 같은 회수 대상이다 → 컷 수·카운트다운·보관 시간 같은 **운영자 설정이 사라진다** | 서버 저장으로 옮기거나(엔드포인트 없음 — 신설 필요), 설치형 PWA를 강제하거나, 웹에서는 설정 편집을 제공하지 않는다 |
| **WR2** | **화면 꺼짐 방지** | 6~10컷 × 카운트다운 동안 화면이 꺼지면 촬영이 끊긴다. `Screen Wake Lock`은 지원이 고르지 않고 iOS Safari 이력이 좋지 않다 | 키오스크는 OS 전원 설정으로 해결. 일반 방문자 기기에서는 보장 불가 |
| **WR3** | **탭 백그라운드 스로틀링** | 탭이 비활성이면 타이머가 늦춰지고 애니메이션 콜백이 멈춘다 → **카운트다운·녹화·유휴 타이머가 깨진다** | 전체화면 키오스크 모드 강제 + `visibilitychange`에서 촬영 시퀀스를 안전하게 취소 |
| **WR4** | **키오스크 몰입 모드** | Fullscreen API는 사용자 제스처가 필요하고 **ESC로 항상 빠져나갈 수 있으며 막을 수 없다** | 진짜 락다운은 브라우저 kiosk mode(OS·정책 설정)로만 가능 → **웹 단독으로는 무인 키오스크가 성립하지 않는다** |
| **WR5** | **JWT 메모리 전용(M2) vs 새로고침** | 웹은 새로고침·탭 복구가 흔하다. M2를 지키면 그때마다 로그아웃되고, UX를 위해 저장하면 **M2 위반**이다 | M2를 지키고 **재로그인을 정상 흐름으로 설계**한다. 세션 영속이 꼭 필요하면 HttpOnly 쿠키 세션을 **별도 설계**로 다룬다(현재 JWT 모델 변경) |
| **WR6** | **게이트 키 노출** | `X-MCPhoto-Client`를 브라우저에 넣으면 **완전히 공개**된다 | §9 B4. **관리 API는 전부 Bearer 게이트라 게이트 키가 불필요**하므로, 웹은 게이트 키가 필요한 경로(로그인·업로드·공용 프레임 조회)를 최소화하는 설계가 가능하다 |
| **WR7** | **버킷 CORS** | 서명 URL PUT에 필요한 헤더가 CORS로 막힌다 | §9 B5. 버킷 CORS 구성 선행 |
| **WR8** | **모바일 브라우저 메모리** | 컷 10장(1080p) + 합성 버퍼면 수백 MB가 된다. iOS Safari는 탭 메모리 한계가 낮아 탭이 죽는다 | 컷을 즉시 JPEG로 압축해 보관, 합성은 순차 처리. 그래도 보장은 없다 |

#### 7.4.3 W-COST — 비용·품질 손실 (축소로 회피 가능)

| # | 항목 | 비용 |
|---|------|------|
| **WC1** | **프레임 버퍼 접근** | 프레임마다 Canvas `drawImage` + `getImageData`는 GPU→CPU 전송이라 비싸다. 효율적인 대안(`MediaStreamTrackProcessor`·WebCodecs)은 **Chromium 전용**이고 Safari에 없다 → [14 §2.2](./14-media-pipeline-spec.md)의 "단일 스트림 → 프레임당 1회 가공 → 3분기"를 Safari에서 규격대로 만들기 어렵다 |
| **WC2** | **해상도·fps 보장 없음** | `getUserMedia` constraints는 요청일 뿐이다. 1080p/30fps가 규격이지만 기기·브라우저가 다른 값을 줄 수 있다 |
| **WC3** | **카메라 장치 식별자 불안정** | 권한 부여 전에는 장치 `label`이 빈 문자열이고, `deviceId`는 origin·저장소 상태에 따라 **재생성될 수 있다** → 설정 `CameraDevice` 값을 안정적으로 영속할 수 없다([41 §2.2](./41-local-data-and-file-formats.md)) |
| **WC4** | **거울 반전** | 프리뷰만 CSS로 반전하면 **저장 픽셀이 반전되지 않아 WYSIWYG가 깨진다**([14 §2.4](./14-media-pipeline-spec.md) 규격 위반). Canvas 경유가 필수이며 WC1 비용을 그대로 받는다 |
| **WC5** | **셔터음** | 자동 재생 정책상 사용자 제스처로 오디오 컨텍스트를 먼저 unlock해야 한다(촬영 시작 버튼을 쓰면 해결 가능) |
| **WC6** | **필터 품질** | 뷰티 필터의 bilateral 등가를 Canvas/WebGL로 구현해야 한다. [14 §6](./14-media-pipeline-spec.md)의 파라미터 의도는 유지할 수 있으나 픽셀 동일성은 어렵다 |

#### 7.4.4 W-OK — 웹에서 문제없는 것

결과물 열람·다운로드(P1) · 프레임 목록 조회 · **공용 기본 프레임 생성·수정·삭제**(power) · 슬롯 편집기 UI · 계정·역할·PIN 관리(P4) · 전역 한도 편집 · 진입 PIN 게이트 · QR 생성 · 진단 표시 · 업로드 3단계 자체(CORS 해결 시) · 브랜딩·버전 표기(빌드 상수).

> **공용 프레임 저작이 W-OK인 이유**: 진실원이 **서버**다. `POST /frames`는 `userId=null, isDefault=true`를 강제하고 계정당 10개 제한·Storage 경로·cascade 삭제가 모두 서버에 있어, 브라우저 저장소를 전혀 쓰지 않는다. 저장 수명 문제(WB3)가 성립하지 않고, 만든 프레임이 **즉시 모든 키오스크에 반영**되므로 "소비자 부재" 문제도 없다. 오히려 공용 프레임을 바꾸려고 현장 키오스크까지 갈 필요가 없어지는 **웹의 고유 이점**이다.

#### 7.4.5 그래서 웹 범위는

| 프로파일 | 판정 | 근거 |
|----------|------|------|
| **P1 소비자** | **○ 지원**(현행 구현 존재) | W-OK |
| **P4 운영** | **○ 지원 — 웹에 가장 잘 맞는다** | 표·폼 중심 UI + 스토어 심사 없는 즉시 배포 + 현장 기기에서 관리 기능을 제거하는 보안 이득 |
| **P3 저작 — 공용 프레임(power)** | **○ 지원** | W-OK. 서버가 진실원 |
| **P3 저작 — 개인 프레임** | **✕ 제외** | WB3(영속 불가 + 소비자 부재). 필요해지면 브라우저 저장소 우회가 아니라 **개인 프레임을 서버로 올리는 정책 결정**으로 다뤄야 한다 |
| **P2 촬영** | **✕ 제외 권장** | WB1(타임랩스 계약 위반) + WB2(M6 위반) + WR4(무인 키오스크 불성립). 축소 조합이 제품으로 성립하지 않는다 |

판단 근거와 대안 결정지는 [`docs/design/multiplatform-client-architecture.md` §4.3](../design/multiplatform-client-architecture.md).

---

## 8. 권장 구현 순서(마일스톤)

프로파일 순으로 쌓으면 각 단계가 독립적으로 출시 가능한 상태가 된다.

| 단계 | 산출물 | 선행 조건 | 검증 |
|------|--------|-----------|------|
| **0. 계약 검증** | `GET /health`(게이트 키 포함) 200 + `deployedAt` 수신 | 배포 게이트 키 발급 | 키 없이 호출 시 200이지만 `deployedAt` 미포함, 잘못된 키는 여전히 200(헬스는 무인증) — 다른 엔드포인트로 401을 확인해야 키 유효성이 확정된다 |
| **1. P1 소비자** | 링크 진입 → 결과물 표시·다운로드·만료/옵션꺼짐 안내 | 없음 | [13 §12](./13-client-behavior-spec.md) 상태 4종 전부 재현 |
| **2. 인증** | Google SSO 로그인 → JWT 보관 → 로그아웃 시 폐기(M1) | **서버 OAuth 확장(§9)** | 로그아웃 후 게스트 업로드에 Bearer 미부착 확인 |
| **3. P4 운영** | 계정 목록·역할 변경·PIN·전역 한도 + PIN 게이트 | 2단계 | 권한 없는 역할로 403 우아 처리 확인 |
| **4. P2 촬영 (미디어 없이)** | 프레임 선택 → (더미 이미지) → 업로드 3단계 → QR | 2단계 | `prepare`/PUT/`commit` 성공 + 웹에서 다운로드 확인 |
| **5. P2 촬영 (미디어 완성)** | 카메라·카운트다운·컷선택·합성·필터·로컬 저장 | 4단계 | [14](./14-media-pipeline-spec.md)의 픽셀 규격 대조 |
| **6. 세션 녹화·타임랩스** | 무음 H.264 mp4 + 배속 변환 | 5단계 | 목표 길이 10~15초, 재생 가능성 |
| **7. P3 저작** | 슬롯 편집기(자동배치·드래그·스케일·검증) + 로컬 저장 + (power) 공용 등록 | 2·5단계 | 저장한 프레임으로 촬영이 정상 합성되는지 |
| **8. 키오스크 마감** | 유휴 감시·표시 모드·진단·전역 예외 복구·브랜딩 | 5단계 | M3·M16 재현 |

---

## 9. 착수 전 해결해야 하는 서버·인프라 변경 (블로커)

아래는 **클라이언트 코드만으로는 우회할 수 없는** 항목이다. 새 플랫폼 착수 결정 시 함께 계획해야 한다.

| # | 항목 | 현재 상태 | 필요 변경 | 영향 플랫폼 |
|---|------|-----------|-----------|-------------|
| **B1** | OAuth redirect URI 화이트리스트 | `validateLoopbackRedirectUri`가 `http://127.0.0.1`·`http://localhost`만 허용, 경로는 `/`만, 쿼리·프래그먼트 금지 | 플랫폼별 리디렉트 형태 허용(커스텀 스킴 / https 등록 URI)로 확장. SSRF 방어를 잃지 않도록 **허용 목록 기반**으로 | iOS·iPadOS·Android·웹 |
| **B2** | OAuth audience 단일 고정 | `GOOGLE_OAUTH_CLIENT_ID` 1개, `verifyIdToken`이 그 하나만 audience로 검증 | client_id **목록**으로 일반화하고 요청이 어느 클라이언트인지 식별 | 동상 |
| **B3** | client_secret 전제 | `getToken`이 `GOOGLE_OAUTH_CLIENT_SECRET`으로 code를 교환 | iOS/Android 유형 클라이언트는 secret이 없다(PKCE만). 클라이언트 유형별 교환 분기 필요 | iOS·iPadOS·Android |
| **B4** | 배포 게이트 키의 브라우저 노출 | `X-MCPhoto-Client`는 exe에 내장된 정적 키 | 브라우저 클라이언트에 넣으면 **공개된다**. 웹용 별도 키 발급 + 서버측 사용처 제한(Referer/Origin·rate limit) 또는 웹은 게이트 키 불요 경로만 사용 | 웹 |
| **B5** | Storage 버킷 CORS | 브라우저에서 서명 URL로 PUT하려면 버킷 CORS에 `PUT`·`Content-Type`·`x-goog-meta-firebaseStorageDownloadTokens`가 허용돼야 한다 | 버킷 CORS 구성 추가 | 웹(P2), WebView 기반 클라이언트 |
| **B6** | 타임랩스 생성 주체 | 클라이언트(ffmpeg)가 만든다 | 웹처럼 배속 변환이 불가한 플랫폼은 "타임랩스 미제공"으로 축소하거나 서버 변환 도입 결정 필요 | 웹, 저사양 모바일 |
| **B7** | `deployedAt` 외 서버 버전 노출 없음 | `/health`가 `status`·`time`(+`deployedAt`) | 클라이언트별 최소 지원 버전 강제(강제 업데이트)가 필요해지면 서버 응답 확장 필요 | 스토어 배포 전부 |
| **B8** | 계정 생성 경로 부재 | Google SSO 최초 로그인 시 자동 생성만 | 다른 IdP(Apple/Kakao)를 붙이면 `authMethod` 확장 + 서버 provider 검증 필요. 현재 클라는 `"google"` 외를 "알 수 없음"으로 표시 | iOS(Apple 로그인 요구 가능성) |

> **B8 참고 — Apple 심사**: iOS에서 서드파티 SSO(Google)를 유일한 로그인 수단으로 제공하면 App Store 심사 가이드라인상 **Sign in with Apple 병행 제공을 요구받을 수 있다.** 계정 모델이 `authMethod` 필드를 이미 갖고 있어 확장 자체는 작지만, 계정 매핑(같은 사람의 Google/Apple 계정 통합) 정책 결정이 필요하다.

---

## 10. 문서 지도 — 어떤 문서를 읽어야 하나

### 플랫폼 중립 규격 (새 클라이언트의 진실원)

| # | 문서 | 내용 |
|---|------|------|
| **05** | (이 문서) | 진입·용어·프로파일·지원 매트릭스·불변식·서버 블로커 |
| **13** | [client-behavior-spec](./13-client-behavior-spec.md) | 화면·상태 전이·플로우·타이밍·검증 규칙·사용자 문구 |
| **14** | [media-pipeline-spec](./14-media-pipeline-spec.md) | 카메라·크롭·합성·필터·녹화·타임랩스 알고리즘 규격 |
| **31** | [backend-api-reference](./31-backend-api-reference.md) | 전 엔드포인트 요청/응답 JSON·헤더·상태코드·에러 코드 |
| **41** | [local-data-and-file-formats](./41-local-data-and-file-formats.md) | 설정 키·프레임 파일 포맷·세션 작업 공간·플랫폼별 경로 |
| **61** | [auth-platform-integration](./61-auth-platform-integration.md) | 플랫폼별 OAuth·JWT 수명·PIN 게이트 |

### 플랫폼 무관 공통 규격 (그대로 유효)

| # | 문서 | 내용 |
|---|------|------|
| 00 | [overview-and-architecture](./00-overview-and-architecture.md) | 시스템 조감·데이터 흐름 |
| 40 | [database-firestore-and-storage-schema](./40-database-firestore-and-storage-schema.md) | 저장 스키마·경로·보안 규칙·계약 불변식 |
| 50 | [infra-gcp-lifecycle-and-ttl](./50-infra-gcp-lifecycle-and-ttl.md) | 보관·만료·물리 삭제 |
| 60 | [auth-accounts-and-roles](./60-auth-accounts-and-roles.md) | 역할 위계·권한 매트릭스 |
| 90 | [roadmap-and-future-work](./90-roadmap-and-future-work.md) | 미해결·비범위 |

### Windows 구현 참조 (예시로만 참조)

| # | 문서 | 무엇을 얻을 수 있나 |
|---|------|---------------------|
| 10 | [exe-app-architecture](./10-exe-app-architecture.md) | 계층 분리·DI·스레딩·리소스 해제의 **검증된 구조** |
| 11 | [exe-app-features](./11-exe-app-features.md) | 기능별 실제 동작·엣지 케이스·과거 결함 이력 |
| 12 | [exe-app-settings-and-config](./12-exe-app-settings-and-config.md) | 설정 항목 전수·기본값·Clamp 규칙 |
| 20 | [frontend-web-download-page](./20-frontend-web-download-page.md) | P1 소비자 클라이언트의 완성된 구현 |
| 30 | [backend-firebase-integration](./30-backend-firebase-integration.md) | 클라이언트↔서버 연동의 설계 의도·실패 정책 |
| 70 | [logging-and-troubleshooting](./70-logging-and-troubleshooting.md) | 증상→원인 매핑(로그 문자열은 Windows 전용) |
| 80 | [build-and-deployment](./80-build-and-deployment.md) | 게이트 키 주입 방식 |

설계 문서는 `docs/design/README.md`가 범위별로 분류해 둔다.

---

## 11. 적합성 체크리스트 (새 클라이언트 출시 전)

프로파일에 해당하는 항목만 확인한다.

**공통**
- [ ] 배포 게이트 키를 소스·리포지토리에 평문으로 커밋하지 않았다(빌드 시 주입)
- [ ] JWT를 디스크에 쓰지 않는다 (M2)
- [ ] 로그아웃 시 JWT가 폐기되고, 직후 익명 업로드에 Bearer가 붙지 않는다 (M1)
- [ ] 모든 API 실패가 사용자에게 보이거나 로그에 남는다(조용한 실패 0) (M4)
- [ ] 401/403/404/409/501·네트워크 실패가 각각 다른 안내로 구분된다 ([31 §3](./31-backend-api-reference.md))
- [ ] 시크릿·토큰·인가 코드·PKCE verifier·PIN이 로그에 남지 않는다
- [ ] 미처리 예외가 앱을 죽이지 않고 홈 복귀 + 로그로 처리된다 (M16)

**P1 소비자**
- [ ] 문서 부재 / `expiresAt` 경과 / 파싱 실패 모두 **만료**로 처리(fail-safe) ([13 §12](./13-client-behavior-spec.md))
- [ ] URL null = "전송 옵션 꺼짐"을 만료·로드실패와 구분해 안내 (M8)
- [ ] `resultSessions`를 **단건 조회만** 한다(목록·쿼리 금지)

**P2 촬영**
- [ ] 프리뷰·스틸·녹화가 동일 가공(거울→중앙 크롭)을 거친다 ([14 §3](./14-media-pipeline-spec.md))
- [ ] 카메라 Ready 게이트(연속 프레임 + 최소 경과 + 타임아웃)를 통과한 뒤에만 촬영 시퀀스를 시작한다
- [ ] 세션 ID 형식이 정규식 `^\d{8}_\d{6}_{UUIDv4}$`를 만족한다 (M13)
- [ ] 서명 PUT에 `requiredHeaders` 전부를 부착한다 (M14)
- [ ] 로컬 저장이 업로드 시도 **이전**에 끝난다 (M6)
- [ ] 업로드 성공 후에만 QR을 노출한다 (M5)
- [ ] TempUser 한도 초과(403 `TEMP_USER_TIME_EXCEEDED` / `TEMP_USER_COUNT_EXCEEDED`)를 사유별 문구로 안내한다
- [ ] 유휴 타임아웃이 로그아웃하지 않는다 (M3)

**P3 저작**
- [ ] 프레임 이름에 `_`를 허용하지 않는다 (M15)
- [ ] 슬롯 저장 검증: 개수 1~6, 경계 내, 겹침 없음 ([14 §4.4](./14-media-pipeline-spec.md))
- [ ] 편집 진입·버튼 노출·저장 3곳 모두에 권한 가드가 있다 (M10)
- [ ] 카탈로그 유래(공용 DB·번들·fallback) 프레임 편집은 **사본으로 분기 저장**하고 원본을 건드리지 않는다

**P4 운영**
- [ ] 역할 변경 옵션이 서버 `canSetRole` 매트릭스와 1:1로 일치한다 ([60 §1.4](./60-auth-accounts-and-roles.md))
- [ ] 자기 계정 삭제·자기 대상 타계정 PIN 재설정을 UI에서 막고 서버 거부도 우아 처리한다
- [ ] PIN 재설정 대상은 **엄격히 낮은 위계**만 노출한다(동급 차단 — 매니저 PIN은 `admin`만, [60 §1.3.1](./60-auth-accounts-and-roles.md#131-canresetpin--pin-재설정-전용-판정-엄격히-낮은-위계)). 삭제는 동급 허용이라 두 액션의 게이트가 **다르다**
- [ ] PIN 게이트가 fail-closed이며 네트워크 오류를 실패 횟수로 세지 않는다 (M9)
