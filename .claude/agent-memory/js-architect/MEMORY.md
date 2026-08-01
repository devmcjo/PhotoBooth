# js-architect 메모리 인덱스 (PhotoBooth)

- [웹 Storage CORS는 미검증](web-cors-never-verified.md) — 문서의 "CORS 문제 없음"은 no-cors 서브리소스만 근거다. 브라우저 JS가 바이트를 읽는 설계엔 폴백 필수
- [설계 문서 관례](design-doc-convention.md) — `{platform}-it{N}-{topic}-design.md`, it13부터 WBS는 별 파일 없이 문서 내 §절로 embed
- [진실원 판정 예외](truth-source-judgment.md) — "소스 > analysis > design"은 **실행된 적 있는 코드**에만 적용. 호출자 0·미구현 요구사항은 규격이 이긴다
- [배포 진단 CLI 접근성](live-diagnostics-access.md) — `firebase functions:log`는 되고 시크릿 값 조회·gcloud는 차단. 라우트 검사 순서를 구간 판정 도구로 쓰는 법
- ["완료" 표기를 믿지 마라](verify-completed-user-actions.md) — 14의 A1~A5 ✅는 문서 갱신일 뿐. 값이 **있는데 틀린** 경우가 "미설정"보다 훨씬 안 잡힌다
