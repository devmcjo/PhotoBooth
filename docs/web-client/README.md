# MC포토 웹 클라이언트 (Web Kiosk) 개발 문서

| 항목 | 값 |
|------|-----|
| 문서 세트 | **Windows 앱과 동일하게 동작하는 웹 버전**을 처음부터 만들기 위한 전체 개발 문서 |
| 대상 독자 | 이 폴더의 문서만 보고 개발에 착수하는 프론트엔드 개발자(또는 에이전트) |
| 전제 지식 | 없음. 필요한 Windows 규격·소스 위치는 각 문서 안에 경로로 명시했다 |
| 작성일 | 2026-07-30 |
| 상태 | **설계 확정 v1.1 — 구현 착수 가능**. 단, [08 서버·인프라 선행 작업](./08-server-and-infra-prerequisites.md)의 P0 항목은 코드 작성 전에 처리해야 한다 |
| v1.1 갱신(2026-07-30) | 원격 it17~it19 반영 — **자동 컷 수**(sentinel 0, WD19)·**오버레이 복귀 집합**(it19)·**버전 표기 채널 폐기**(it18)·설정 그룹 이동·진단 개발자 문의 카드·**CORS 실측 반영**(`web/OPS-cors.md` — 다운로드 GET 불필요/PUT 필수) + **개인 프레임 이연 컷라인**(WD20) |
| v1.3 갱신(2026-07-30) | **Opus 적대 리뷰 반영(설계 리뷰 완료)** — 치명 3(M1 Zustand 배선·**은행가 반올림 `roundHalfToEven`**·삭제 소유자 검사 회귀)·중요 11(WBS Step 7 프레임 공급·`currentPin` 계약·`SlotPlacement` 이식·opfsWriter 명시·**JWT 만료 C10 등재**·빌드값 빈 문자열 폴백·**타임랩스 OPFS 스풀 재설계**·`prompt=select_account`·Ready "누적" 통일·E24 범위·stale 주석)·사소 9 |
| v1.2 갱신(2026-07-30) | 소스 대조 검증 반영 — ① **게스트에게는 QR이 제공되지 않는다**(`QrEffectivePolicy`: 미로그인 → `Result→Done`. 도메인 모듈·화면·테스트·WBS 마일스톤 정정) ② 타임랩스 **경로 A는 메인 스레드 전용**(`MediaRecorder`/`captureStream`이 Worker에 없음) + 브라우저 지원 현실표 ③ **OPFS 쓰기는 Worker `createSyncAccessHandle`이 유일 경로**(Safari에 `createWritable` 없음) ④ iOS Safari API 하한표(`OffscreenCanvas` WebGL2 = 17+) ⑤ QR **ECC Q**(Windows와 일치) ⑥ WBS 버전 캡션 `Site` 제거·컷 수 해석 식 정정 |
| 범위 | Windows 앱의 **모든 화면**(촬영·프레임 저작·설정·계정·사용자 관리·진단)을 웹으로 구현 + 기존 다운로드 페이지(P1) 유지 |

---

## 0. 30초 요약

- 만드는 것: **브라우저 전체화면에서 도는 셀프 포토부스 앱**. Windows 앱(`MCPhoto.App`)의 13개 화면 + 6개 모달 전부.
- 동작하는 곳: Windows·macOS·Android·iOS·iPadOS의 최신 브라우저. **내장 카메라(전/후면) 사용**.
- 서버: **기존 백엔드를 그대로 쓴다**(새 API 없음). 다만 착수 전 서버 4건·인프라 1건을 손봐야 한다 → [08](./08-server-and-infra-prerequisites.md).
- 안 만드는 것: 외장 DSLR 카메라 연동, 사진 프린터 출력, 앱 종료(관리자) — 브라우저에서 불가하므로 **UI 미노출**.
- Windows와 다르게 동작하는 것: **[12 · Web ↔ Windows 차이 보고서](./12-web-vs-windows-differences.md)에 전부 모아 두었다.** 기능 추가 시 이 표를 먼저 본다.

> ⚠️ **중요**: 기존 저장소 문서(`docs/analysis/05 §7.4.5`, `docs/design/multiplatform-client-architecture.md §4.3`)는 **"웹에서 촬영(P2)은 제외 권장"** 으로 판정해 두었다. 이 문서 세트는 **사용자 결정으로 그 판정을 대체**하며, 막힌다고 판정했던 항목을 어떻게 해결하는지 [00 §3](./00-scope-and-decisions.md#3-기존-판정-재검토--무엇이-실제로-막히고-무엇이-풀리는가)에서 항목별로 다시 판정했다. 두 문서가 충돌하면 **이 폴더가 웹 클라이언트에 대한 최신 결정**이다.

---

## 1. 읽는 순서

### 처음 착수한다면 (필수, 순서대로)

| # | 문서 | 무엇을 얻나 | 분량 |
|---|------|-------------|------|
| 1 | [00 · 범위와 결정](./00-scope-and-decisions.md) | 무엇을 만들고 무엇을 빼는가, 결정 20건(WD1~WD20), 불변식의 웹 변형 | 필독 |
| 2 | [13 · 소스·문서 참조 지도](./13-source-reference-map.md) | Windows 규격·소스의 **어디를** 봐야 하는가(화면·로직별 파일 경로) | 필독 |
| 3 | [01 · 기술 스택과 프로젝트 구조](./01-tech-stack-and-structure.md) | 스택·폴더 구조·빌드·배포 형태 | 필독 |
| 4 | [08 · 서버·인프라 선행 작업](./08-server-and-infra-prerequisites.md) | 코드 짜기 전에 처리할 서버·GCP·Google Console 작업 | 필독 |
| 5 | [02 · 앱 셸과 내비게이션](./02-app-shell-and-navigation.md) | 화면 상태머신·상단바·유휴 감시·전체화면·전역 예외 | 필독 |
| 6 | [03 · 화면별 상세 명세](./03-screens-spec.md) | 13화면 + 6모달의 레이아웃·상태·명령·문구·검증 | 구현 시 상시 참조 |
| 7 | [04 · 미디어 파이프라인(웹)](./04-media-pipeline-web.md) | 카메라·크롭·합성·필터·타임랩스 인코딩 | 구현 시 상시 참조 |
| 8 | [05 · 저장·영속](./05-storage-and-persistence.md) | 설정·프레임·세션·결과물·로그의 웹 저장 설계 | 구현 시 상시 참조 |
| 9 | [06 · 백엔드 연동](./06-backend-integration-web.md) | API 클라이언트·업로드 3단계·에러 매핑 | 구현 시 상시 참조 |
| 10 | [07 · 인증·권한](./07-auth-and-permissions-web.md) | OAuth 리디렉트·JWT·PIN 게이트·역할 게이트 | 구현 시 상시 참조 |
| 11 | [09 · 키오스크 운영](./09-kiosk-operations.md) | 브라우저 키오스크 모드·권한 사전승인·전원 설정 | 배포 시 |
| 12 | [10 · 테스트와 수락 기준](./10-testing-and-acceptance.md) | 테스트 전략·골든 이미지·실기기 매트릭스·수락 체크리스트 | 각 단계 종료 시 |
| 13 | [11 · WBS(작업 분해)](./11-wbs.md) | Step 0~17 실행 계획(self-contained) | 실행 순서 |
| 14 | [12 · Web ↔ Windows 차이 보고서](./12-web-vs-windows-differences.md) | 다르게 동작하는 전 항목 + 기능 추가 시 규칙 | 보고·유지보수 |

### 특정 작업만 한다면

| 하려는 일 | 읽을 문서 |
|-----------|-----------|
| 카메라가 안 잡힌다 / 프리뷰 품질 문제 | [04 §2·§3](./04-media-pipeline-web.md) + [09 §3](./09-kiosk-operations.md) |
| 업로드가 403/CORS로 실패한다 | [06 §4](./06-backend-integration-web.md) + [08 §5](./08-server-and-infra-prerequisites.md) |
| 로그인이 400으로 거부된다 | [07 §2](./07-auth-and-permissions-web.md) + [08 §3](./08-server-and-infra-prerequisites.md) |
| 합성 결과가 Windows와 다르다 | [04 §5·§6](./04-media-pipeline-web.md) + [10 §4 골든 이미지](./10-testing-and-acceptance.md) |
| 새 기능을 추가한다 | [12 §7 기능 추가 규칙](./12-web-vs-windows-differences.md) |

---

## 2. 이 문서 세트가 진실원인 범위

```
docs/analysis/13·14·31·41·60·61   ← 플랫폼 중립 규격 (동작·픽셀·API·설정 키의 진실원)
        │  "어떤 클라이언트든 이렇게 동작해야 한다"
        ▼
docs/web-client/*  (이 폴더)      ← 웹에서 그 규격을 어떤 기술로 어떻게 만족시키는가
        │  + 규격을 만족할 수 없는 항목의 대체 정의(웹 변형)
        ▼
실제 웹 코드
```

| 충돌 시 우선순위 | 규칙 |
|------------------|------|
| 동작·타이밍·픽셀·API 계약 | `docs/analysis`가 우선. 이 폴더가 다르게 적었다면 이 폴더의 버그다 |
| **웹 변형이 명시된 항목** | 이 폴더가 우선([12](./12-web-vs-windows-differences.md)에 전량 등재된 것만) |
| 기술 선택·폴더 구조·구현 방법 | 이 폴더가 유일한 근거 |
| Windows 구현 세부(WPF·INI·ffmpeg) | **이식 대상이 아니다.** 예시로만 참조 |

---

## 3. 문서 갱신 규칙

1. **규격을 바꿀 때는 `docs/analysis`를 먼저 고친다.** 그다음 이 폴더, 그다음 코드.
2. 웹에서만 다르게 동작하게 만들었다면 **반드시 [12](./12-web-vs-windows-differences.md)에 행을 추가**한다. 등재되지 않은 차이는 버그로 취급한다.
3. 결정을 바꿨다면 [00 §5 결정 목록](./00-scope-and-decisions.md#5-결정-목록-wd1wd20)의 해당 WD 항목을 갱신하고 사유를 남긴다.
4. 새 화면·모달을 추가하면 [03](./03-screens-spec.md)과 [02 §2 상태 전이표](./02-app-shell-and-navigation.md)를 함께 갱신한다.

---

## 4. 관련 저장소 위치 요약

| 무엇 | 경로 |
|------|------|
| Windows 앱 소스(참조 구현) | `src/MCPhoto.App/` · `src/MCPhoto.Core/` · `src/MCPhoto.Capture/` · `src/MCPhoto.Http/` |
| Windows 앱 테스트(테스트 벡터 추출원) | `tests/MCPhoto.Tests/` |
| 백엔드 API 소스 | `web/functions/src/` |
| 기존 다운로드 페이지(P1, 유지) | `web/public/` |
| Hosting·보안 규칙 구성 | `web/firebase.json` · `web/firestore.rules` · `web/storage.rules` |
| 플랫폼 중립 규격 문서 | `docs/analysis/05·13·14·31·41·60·61` |
| **새로 만들 웹 앱 소스(이 문서의 산출물)** | `webclient/` (신규) → 빌드 산출물은 `web/kiosk/` |

상세 매핑은 [13 · 소스·문서 참조 지도](./13-source-reference-map.md).
