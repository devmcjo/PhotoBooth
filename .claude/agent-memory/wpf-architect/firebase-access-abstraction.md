---
name: firebase-access-abstraction
description: Firebase 접근이 Core 인터페이스 5종으로 추상화됨 — Admin→HTTP 프록시 교체가 UI 무변경으로 가능한 구조
metadata:
  type: project
---

MCPhoto의 Firebase(Firestore/Storage) 접근은 `MCPhoto.Core`의 인터페이스 5종으로 추상화되어 있고, 구현은 `MCPhoto.Firebase` 어셈블리 한 곳에 격리됨. UI/뷰모델은 인터페이스에만 의존.

- 인터페이스: `IFirebaseClient`(저수준 Storage/Firestore·resultSessions CRUD), `IAccountService`(users), `IFrameRepository`(frameTemplates+frames/), `IUploadService`(업로드 오케스트레이션·순수), `IQrService`(QRCoder 순수, Firebase 무관).
- 현재 구현은 **Admin SDK**(`GoogleCredential.FromFile`) → 보안규칙 우회. 키는 publish 시 exe 폴더 동봉(`publish.ps1`), 앱은 실행폴더 키 최우선 로드.
- `FrameRepository`/`AccountService`는 구상 `FirebaseClient`를 직접 주입받아 `internal FirestoreDb Firestore`를 공유(DI에서 구상+인터페이스 같은 싱글턴). HTTP 전환 시 이 구상 결합을 끊어야 함.
- `UploadService`는 이미 순수 오케스트레이션(파일 업로드+URL 조립+문서생성) → `IFirebaseClient` 구현만 바꾸면 대부분 재사용.
- 만료정리(`PurgeExpiredAsync`/`QueryExpiredSessionsAsync`)는 **앱 런타임 미호출** — 인프라(GCS Lifecycle age 3일 + Firestore 네이티브 TTL)가 담당. 클라에서 제거 대상.

**Why**: 2026-07 방향 B(서버 경유) 보안 재설계 시, 구현체만 HTTP로 교체+DI 3~4줄 변경으로 UI 전부 무변경 가능하다는 것이 핵심 강점으로 확인됨.
**How to apply**: 백엔드 프록시/구현 교체 설계 시 인터페이스 시그니처를 유지하고 구현만 교체하는 전략을 우선 검토. 설계 문서: `docs/design/wpf-backend-proxy-migration-design.md`. 관련 [[camera-singleton-constraint]] [[it10-server-key-distribution]]
