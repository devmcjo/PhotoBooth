# js-developer 프로젝트 메모리 (photobooth)

- [Functions 백엔드 프록시](functions-backend-proxy.md) — web/functions Cloud Functions 2nd gen(TS), 빌드·검증·Emulator 스모크 방법, 서명 URL Emulator 제약
- [Functions 백엔드 관례·함정](functions-backend-conventions.md) — 검증 게이트, jest 순수 테스트 경계, AppConfig 확장 시 동반 수정
- [검증 커맨드·구현 패턴](functions-verify-and-patterns.md) — 커맨드 요약, 지연 import, 스모크 해시 토큰 검증 전략
- [운영 스크립트·줄바꿈](ops-scripts-and-encoding.md) — .mjs 스크립트를 jest로 검증하는 domain 분리 패턴, autocrlf 경고 해석, lib/ 잔재 청소
- [web/public 검증](web-public-verification.md) — 린터·테스트 없는 정적 ESM을 스텁 DOM으로 검증하는 패턴, rg 게이트가 주석과 충돌하는 함정, hidden vs display 캐스케이드
- [webclient 검증 게이트](webclient-verification-gate.md) — ESLint 미설정(tsc+vitest+coverage+build가 전부), build는 **2단**(앱→SW)이고 산출물은 ../web/kiosk. tsc가 못 잡는 NUL 함정 포함
- [Storage 다운로드 CORS](firebase-storage-cors.md) — firebasestorage 호스트는 버킷 CORS 없이 ACAO:* 를 준다(GCS 호스트는 아님). 버킷 CORS는 업로드 PUT에만 필요
