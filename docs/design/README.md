# MC포토 설계 문서 (docs/design)

기능·구조 결정의 **근거와 대안 검토 과정**을 남긴 설계 문서 모음입니다. "현재 무엇이 어떻게 동작하는가"는 [`docs/analysis`](../analysis/README.md)가 진실원이고, 이 폴더는 **"왜 그렇게 결정했는가"** 를 남깁니다.

| 항목 | 값 |
|------|-----|
| 최종 업데이트 | 2026-08-01 (웹 클라이언트 Step 14 프레임 저장소·선택 화면 설계 등재) |
| 갱신 규칙 | 새 설계 문서를 추가하면 이 인덱스의 해당 절에 등재한다. 이터레이션이 완료돼 내용이 `docs/analysis`에 흡수되면 §4로 옮긴다 |

---

## 0. 어느 문서를 봐야 하나

| 하려는 일 | 읽을 문서 |
|-----------|-----------|
| **웹(브라우저) 클라이언트를 만든다** | **[`docs/web-client/`](../web-client/README.md)** — 전용 문서 세트 15개(범위·결정·화면 명세·미디어·저장·인증·서버 선행 작업·WBS·**Web↔Windows 차이 보고서**). ⚠️ 아래 [멀티플랫폼 아키텍처 §4.3](./multiplatform-client-architecture.md)의 "웹 P2 제외" 판정은 **2026-07-30 사용자 결정으로 대체**됐다 |
| **다른 플랫폼(iOS·iPadOS·Android·macOS) 클라이언트를 만든다** | [멀티플랫폼 클라이언트 아키텍처](./multiplatform-client-architecture.md) → 그다음 [`docs/analysis/05`](../analysis/05-cross-platform-client-guide.md) |
| 백엔드 API를 확장한다 | [백엔드 프록시 전환 설계](./wpf-backend-proxy-migration-design.md) + [`docs/analysis/31`](../analysis/31-backend-api-reference.md) |
| 인증·계정 모델을 바꾼다 | [it15 Google 전용 인증](./wpf-it15-google-only-auth-design.md) · [Google SSO 설계](./wpf-google-sso-design.md) · [it14 PIN 게이트](./wpf-it14-settings-pin-gate-design.md) |
| 역할·권한을 바꾼다 | [it16 고급 유저 역할](./wpf-it16-advanced-user-role-design.md) · [it13 임시 유저 역할](./wpf-it13-temp-user-role-design.md) |
| 프레임 기능을 바꾼다 | [it15 프레임 UX](./wpf-it15-frame-ux-design.md) · [프레임 신규 생성·서버 등록 팝업](./wpf-frame-create-from-existing-and-server-register-design.md) · [it20 다운로드 대기 UI](./wpf-it20-frame-download-waiting-design.md) · [프레임 편집 완성](./wpf-frame-edit-completion-design.md) |
| 촬영 컷 수·슬롯 관계를 바꾼다 | [it17 컷 수 자동 모드](./wpf-it17-auto-cutcount-design.md) |
| 웹 다운로드 페이지를 바꾼다 | [it17 자동 저장·공유](./web-it17-download-share-design.md) → [웹 아키텍처](./web-architecture.md) + [Firebase 계약](./firebase-contract.md) |
| 웹 클라이언트(키오스크)의 타임랩스를 바꾼다 | [Step 9 타임랩스 인코더](./web-step9-timelapse-encoder-design.md) → [`web-client/04 §7`](../web-client/04-media-pipeline-web.md) + [`analysis/14 §7`](../analysis/14-media-pipeline-spec.md) |
| 웹 클라이언트(키오스크)의 업로드·QR을 바꾼다 | [Step 11 업로드·QR·Done](./web-step11-upload-qr-done-design.md) → [`web-client/06 §4·§5`](../web-client/06-backend-integration-web.md) + [`analysis/31 §5·§7`](../analysis/31-backend-api-reference.md) |
| 웹 클라이언트(키오스크)의 로그인·JWT를 바꾼다 | [Step 12 Google SSO·JWT](./web-step12-google-sso-auth.md) → [`web-client/07`](../web-client/07-auth-and-permissions-web.md) + [`analysis/61 §3.4·§6`](../analysis/61-auth-platform-integration.md) + [`analysis/31 §4.2`](../analysis/31-backend-api-reference.md) |
| 웹 클라이언트(키오스크)의 PIN 게이트·설정 화면을 바꾼다 | [Step 13 PIN 게이트·설정](./web-step13-settings-pin-gate.md) → [`web-client/07 §6`](../web-client/07-auth-and-permissions-web.md)·[`03 §12`](../web-client/03-screens-spec.md) + [`analysis/41 §2`](../analysis/41-local-data-and-file-formats.md)(설정 진실원) + [`analysis/61 §7`](../analysis/61-auth-platform-integration.md) |
| 웹 클라이언트(키오스크)의 프레임 목록·저장소·대기 UI를 바꾼다 | [Step 14 프레임 저장소·선택](./web-step14-frame-catalog-and-select.md) → [`web-client/03 §4·§4.1`](../web-client/03-screens-spec.md)·[`05 §4`](../web-client/05-storage-and-persistence.md)·[`06 §6`](../web-client/06-backend-integration-web.md) + [`analysis/13 §4.2·§5`](../analysis/13-client-behavior-spec.md) + [it20 대기 UI](./wpf-it20-frame-download-waiting-design.md)(Windows 원본) |
| 웹 클라이언트(키오스크)의 프레임 편집기·불러오기·서버 등록을 바꾼다 | [Step 15 프레임 편집기·피커](./web-step15-frame-editor-and-picker.md) → [`web-client/03 §11·§15.4·§15.7`](../web-client/03-screens-spec.md) + [`analysis/13 §6`](../analysis/13-client-behavior-spec.md)(**2026-07-31 개정판**)·[`analysis/14 §4`](../analysis/14-media-pipeline-spec.md) + [프레임 신규 생성·서버 등록 팝업](./wpf-frame-create-from-existing-and-server-register-design.md)(Windows 원본) |
| Windows 앱 구조를 바꾼다 | [WPF 아키텍처](./wpf-architecture.md) |

---

## 1. 플랫폼 중립 · 멀티플랫폼

| 문서 | 범위 | 내용 |
|------|------|------|
| [multiplatform-client-architecture](./multiplatform-client-architecture.md) | **전 플랫폼** | 공통 코어 / 플랫폼 어댑터 경계, 플랫폼별 권장 스택, 프로파일별 범위, 마일스톤, 검증 전략, 서버 확장 의존성 |

## 2. 플랫폼 무관 계약 · 백엔드

| 문서 | 범위 | 내용 |
|------|------|------|
| [firebase-contract](./firebase-contract.md) | **전 플랫폼** | 생산자↔소비자 인터페이스 계약: Firestore 스키마·Storage 경로·토큰 URL·다운로드 페이지 URL·보안 규칙 요구사항·TTL 의미론. **요금제 전제 포함** |
| [wpf-backend-proxy-migration-design](./wpf-backend-proxy-migration-design.md) | **전 플랫폼**(서버측) | 클라이언트의 Admin SDK 직결을 폐지하고 백엔드 API 경유로 전환한 설계. 엔드포인트·게이트·업로드 3단계의 근거 |
| [wpf-it13-temp-user-role-design](./wpf-it13-temp-user-role-design.md) | **전 플랫폼** | 임시 유저 역할 + 무료 사용 한도(시간·횟수). 과금 안전을 서버가 담보하는 이유, prepare 선검사 + commit 트랜잭션 |
| [wpf-it16-advanced-user-role-design](./wpf-it16-advanced-user-role-design.md) | **전 플랫폼** | 고급 유저 역할 도입. 프레임 저작 권한 축(`CanWriteFrames`)을 관리 권한(`IsPower`)과 분리한 근거, 역할 변경 매트릭스 |
| [wpf-it15-google-only-auth-design](./wpf-it15-google-only-auth-design.md) | **전 플랫폼**(서버측) · 데스크톱(클라측) | 비밀번호 폐지 + Google SSO 단일화. 서버 검증 절차·와이어 형식 동결·PIN 완화 정책의 근거 |
| [wpf-it14-settings-pin-gate-design](./wpf-it14-settings-pin-gate-design.md) | **전 플랫폼** | 진입 PIN 게이트. fail-closed 규약, 서버 잠금을 채택하지 않은 이유(DoS) |

## 3. 특정 클라이언트 설계

### 3.1 웹 (소비자 클라이언트)

| 문서 | 내용 |
|------|------|
| [web-it17-download-share-design](./web-it17-download-share-design.md) | **it17** 원클릭 자동 저장(fetch→Blob→`<a download>`)·전역 degrade 폴백·상단 공유 버튼(링크 복사+토스트)·파일명 규칙·`MCPhoto` 네이밍. **버킷 CORS(GET) 선행 조건 포함** |
| [web-architecture](./web-architecture.md) | 다운로드 페이지 구조·상태머신·보안 규칙·Emulator 검증 |
| [web-wbs](./web-wbs.md) | 웹 작업 분해 |
| [web-step15-frame-editor-and-picker](./web-step15-frame-editor-and-picker.md) | **키오스크 웹 클라이언트 WBS Step 15** 상세 설계(WD20 15a+15b 전량). 슬롯 배치 편집기(**표시·드래그·클램프가 하나의 `EditorTransform`** — `<img>`+DOM 슬롯으로 좌표계 이중화를 없앤 근거)·**저장 전 검증 7단+⑧을 도메인 순수 함수 하나가 소유**(④가 ⑦보다 먼저인 이유, ⑤⑥은 길이를 보지 않는 `isFileNameSafe`)·진입점 2개의 **저장 함수 첫 줄 재검증**·서버 등록 확인 오버레이 상태 머신(기본 on·열 때마다 리셋·**닫기 전 확정**·노출 조건과 등록 분기가 **같은 함수**)·**원자성**(문서 생성/이미지 PUT 실패 시 로컬 미저장 + 서버 문서 best-effort 정리)·**"기존 프레임 불러오기 = 신규 생성"(2026-07-30 재정의 — 사본 폐기)** 반영·피커(단일 비행 합류·호출자별 취소·상한)·삭제는 Step 14 화면 로컬 오버레이 재사용(`ModalId` 축소로 구조 고정). **기존 결함 2건 정정 포함**: `createFrame`이 `{frame, upload}` 봉투를 안 읽어 **이미지 PUT이 영구 생략**되던 문제, `saveLocal` 덮어쓰기가 남기던 **고아 OPFS PNG** |
| [web-step14-frame-catalog-and-select](./web-step14-frame-catalog-and-select.md) | **키오스크 웹 클라이언트 WBS Step 14** 상세 설계. 프레임 저장소(IndexedDB 메타 + OPFS PNG, **`mcphoto`가 아닌 `mcphoto-frames` DB** — 로그 연결이 붙들고 있어 같은 DB 업그레이드가 영구 blocked 된다)·카탈로그 로더(**단일 비행 + 진행 replay**, 호출자별 취소로 공유 작업 존속)·**it20 로딩 4국면**(`finally`가 `finalizeFrameLoad`를 무조건 불러 오버레이 고착이 구조적으로 불가능)·무진행 30초/총 60초 **실경과** 상한·삭제 흐름(로컬 항상 → power 서버 옵션, 결과 4문구)·WM2 CORS-clean 로드와 object URL 수명. **오프라인이 `Degraded`가 아니라 `Ready`인 근거(어댑터가 서버 실패를 삼키는 catch 한 곳)**, 삭제 확인을 Step 15의 공용 모달이 아닌 화면 로컬 오버레이로 둔 판단, JS 고유 단일 비행 함정 2건(`inFlight` 조기 해제·abort 리스너 누적), 기존 결함 2건(`deleteFrame`이 `{deleted}`를 버림 · `logStore`의 낡은 주석) 정정 포함 |
| [web-step13-settings-pin-gate](./web-step13-settings-pin-gate.md) | **키오스크 웹 클라이언트 WBS Step 13** 상세 설계. 진입 PIN 게이트(4자리·5회/1.5초·**기기 5분 잠금** WD16·**fail-closed 5경로**)를 **네비게이션 가드가 아니라 `<PinGate>` 렌더 게이트**로 만든 근거(OAuth 복귀 `returnTo="Settings"` 경로를 구조적으로 덮는다), `pushModal`이 결과를 돌려주지 않는 문제를 **1개짜리 pending 채널 + 멱등 해제 + 마운트 감시 5초**로 푼 구조, `hasPin` 갱신 경로 부재로 인한 **최초 설정 후 401 데드락**과 `markPinSet()` 신설, **`setMyPin`에 `unauthorized:"reject"` 누락(E17의 PUT 판) 버그 수정**, 설정 화면 6섹션 + 게스트 **4중 방어**(렌더·액션·패치·저장소) + 저장 4단(**재반영 필수**) + **[보관된 결과물] 패널(Step 10 이월)** 포함 |
| [web-step12-google-sso-auth](./web-step12-google-sso-auth.md) | **키오스크 웹 클라이언트 WBS Step 12** 상세 설계. Google SSO **전체 페이지 리디렉트**(PKCE S256·`state`·`nonce`·`prompt=select_account`·`clientKind:"web"`) → `/oauth2callback` 동기 1회 소비 → 메모리 JWT(M2). **콜백을 화면 상태가 아니라 URL 경로로 처리한 근거**(`OauthCallback` 상태 미신설), StrictMode 이중 실행을 React 밖 동기 소비로 막은 구조, **401 → `expireSession()`(촬영 데이터 유지 — `logout()`과 다른 이유)** 배선, PIN 401 예외 처리, 개발 포트 5273↔5173 불일치 정정 포함 |
| [web-step11-upload-qr-done-design](./web-step11-upload-qr-done-design.md) | **키오스크 웹 클라이언트 WBS Step 11**(★마일스톤 A) 상세 설계. 업로드 3단계(prepare → **XHR 서명 PUT**(진행률·`requiredHeaders` 전량 순회 — M14) → commit)·ECC **Q** QR·`Done` 자동 홈. **업로드 실행 주체를 `Qr` 화면으로 확정한 근거**(Windows `QrPopupViewModel`·[재시도] 중복 진입점), `qrUsageStore`로 `isTempUserBlocked` 실배선, `qrcode-generator@2.0.4`(MIT) 도입 근거 포함 |
| [web-step10-local-save-design](./web-step10-local-save-design.md) | **키오스크 웹 클라이언트 WBS Step 10** 상세 설계. 합성 결과·타임랩스의 OPFS 보관(Worker 경계 필수 — Safari에 `createWritable` 없음), 폴더명 규칙(Windows `LocalSaveService`와 동일 유도), 보관 한도·회수, 사용자 지정 폴더 핸들을 **로그 DB와 분리된** IndexedDB에 두는 근거 |
| [web-step9-timelapse-encoder-design](./web-step9-timelapse-encoder-design.md) | **키오스크 웹 클라이언트**([`docs/web-client/`](../web-client/README.md)) **WBS Step 9** 상세 설계. 스풀(≤15fps JPEG→OPFS) + 종료 시 실경과 선별 → WebCodecs/mp4-muxer(Worker) → MediaRecorder(메인) → `null` 3경로. **`mp4-muxer@5.2.2`(MIT) 도입 근거·`THIRD-PARTY.md` 신설** 포함 |

### 3.2 Windows 데스크톱 (WPF)

> ⚠️ 아래 문서의 WPF·XAML·OpenCvSharp·ffmpeg·INI 관련 서술은 **Windows 구현 전용**이다. 다른 플랫폼은 [`docs/analysis`](../analysis/README.md)의 플랫폼 중립 규격(05·13·14·31·41·61)을 따른다.

| 문서 | 내용 |
|------|------|
| [wpf-architecture](./wpf-architecture.md) | 전체 아키텍처(계층·MVVM·DI·상태머신·캡처 파이프라인) |
| [wpf-wbs](./wpf-wbs.md) | 초기 작업 분해 |
| [wpf-google-sso-design](./wpf-google-sso-design.md) | 데스크톱 OAuth(loopback + PKCE + 시스템 브라우저) 상세 |
| [wpf-it15-frame-ux-design](./wpf-it15-frame-ux-design.md) | 프레임 편집 로컬 전용 정책·사본 분기·기존 프레임 불러오기 |
| [wpf-frame-create-from-existing-and-server-register-design](./wpf-frame-create-from-existing-and-server-register-design.md) | 기존 프레임 불러오기를 **사본이 아닌 신규 생성**으로 재정의(이름은 사용자가 지정) + 파워 계정 저장 시 **서버 등록 확인 팝업**(체크 시에만 DB insert). 이름 충돌 차단으로 로컬 공용 프레임 덮어쓰기 방지 |
| [wpf-frame-edit-completion-design](./wpf-frame-edit-completion-design.md) | 프레임 편집기 슬롯 배치·좌표 변환 |
| [wpf-auth-ux-and-account-rules-design](./wpf-auth-ux-and-account-rules-design.md) | 로그인 UX·계정 규칙(일부는 it15에서 폐지 — 이력) |
| [wpf-it10-server-connectivity-design](./wpf-it10-server-connectivity-design.md) · [wpf-it10-wbs](./wpf-it10-wbs.md) | 서버 연결 상태 표시 |
| [wpf-it11-deferred-features-design](./wpf-it11-deferred-features-design.md) · [wpf-it11-wbs](./wpf-it11-wbs.md) | 재촬영·진단 화면·카메라 이름·업로드 진행률. **컷별 재촬영은 미구현**(설계만 존재) |
| [wpf-it12-design](./wpf-it12-design.md) · [wpf-it12-wbs](./wpf-it12-wbs.md) | 설정 편집 게이트·레이아웃·버전 표기 조정 |
| [wpf-it20-frame-download-waiting-design](./wpf-it20-frame-download-waiting-design.md) | **it20** 기본 프레임 다운로드 대기 UI. 로딩 4국면(`Loading`/`Ready`/`Degraded`/`Failed`)·무진행 30초/총 60초 2단 상한·`finally`가 국면을 무조건 확정하는 구조·`FrameCatalogService` 단일 비행(single-flight) + 진행 중계 |
| [wpf-ffmpeg-licensing-and-distribution-design](./wpf-ffmpeg-licensing-and-distribution-design.md) | **검토 전용(미착수)** ffmpeg GPLv3 준수 의무와 배포 형태. 동봉 자체는 위반이 아니고 **조건 미이행**이 위반이라는 판정, LGPL 빌드·`h264_mf` 전환 경로 |
| [wpf-it17-auto-cutcount-design](./wpf-it17-auto-cutcount-design.md) | 촬영 컷 수 "자동" 모드(ini sentinel 0 → `max(6, 슬롯+2)`). 설정 도메인과 실효값 도메인을 분리한 근거, 단일 해석 지점(`CaptureSession.Begin`) |

---

## 4. 문서 유효성 주의

| 주의 | 내용 |
|------|------|
| **폐기된 서술** | it15 이전 문서에는 **id/pw 로그인·비밀번호·시드 계정·서비스 계정 키·`MCPhoto.Firebase` 직결**이 남아 있다. 모두 **이력**이며 현행이 아니다 |
| **미구현 설계** | [wpf-it11-deferred-features-design](./wpf-it11-deferred-features-design.md)의 **컷별 재촬영**은 설계만 있고 구현되지 않았다(UI 배치 결정 대기, [`analysis/90 §2`](../analysis/90-roadmap-and-future-work.md)) |
| **결정 대기** | [`analysis/90 §1`](../analysis/90-roadmap-and-future-work.md)이 미해결 항목의 **단일 진실**이다. 설계 문서에 있는 아이디어가 확정을 뜻하지는 않는다 |
| **진실원 우선순위** | 실제 소스 > `docs/analysis` > `docs/design`. 설계 문서와 현행 동작이 다르면 **소스가 사실**이고 analysis를 갱신한다 |
