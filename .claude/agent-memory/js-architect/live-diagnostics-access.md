---
name: live-diagnostics-access
description: 배포된 백엔드(mcphoto-955fb)를 진단할 때 실제로 쓸 수 있는 CLI와 차단되는 CLI — 추측 대신 관측으로 원인을 특정하는 최단 경로
metadata:
  type: reference
---

배포 백엔드 이슈를 진단할 때 **추측하지 말고 이것부터 돌려라.** 2026-08-01에 이 명령 하나가
"로그인이 왜 안 되는가"를 30초 만에 확정했다(플레이스홀더 client_id → `invalid_client`).

## 되는 것

| 명령 | 얻는 것 |
|------|---------|
| `cd web && npx --no-install firebase functions:log --project mcphoto-955fb` | **서버 `console.warn` 원문 + 배포 audit log**. audit log에는 `secretEnvironmentVariables`(시크릿 **이름·버전**), `update_mask`, 리비전 번호, 배포 시각이 그대로 들어 있다 — 어떤 시크릿이 등록됐는지 값 없이 확인할 수 있다 |
| `npx --no-install firebase login:list` | 로그인 계정 확인(현재 `devmcjo@gmail.com` — 이미 인증돼 있다) |
| `web/functions/lib/build-stamp.json` | 마지막 배포 시각(`deployedAt`, UTC). 로컬 env 파일 mtime과 비교하면 **그 env가 실제로 배포에 실렸는지** 판정된다 |
| `.env` / `.env.<projectId>` / `.env.production.local` 직접 읽기 | gitignore라 git 이력이 없다. **파일을 열어야만** 실제 배포값을 안다 |

## 막히는 것 (도구 정책)

- `firebase functions:secrets:access …` — 시크릿 **값** 조회. 마스킹 파이프를 붙여도 차단된다
- `gcloud …` — 설치·인증 여부와 무관하게 차단

→ 시크릿 **값**의 정합성은 코드로 닫을 수 없다. 설계에 **미검증 가정**으로 올리고 사용자 액션 단계에서 확인시켜라.

## 라우트 순서를 진단 도구로 쓰는 법

`web/functions/src/routes/auth.ts`는 검사 순서가 곧 구간 판정이다. **어디까지 갔는지가 상태코드/로그로 드러난다** —
400이면 redirectUri 이전, 501이면 클라이언트 구성, code 교환 로그가 있으면 그 뒤 단계는 전부 무죄다.
후보를 나열하기 전에 **먼저 이 순서를 그려라.** 도달조차 하지 않은 코드를 의심하는 데 시간을 쓰지 않게 된다.

관련: [[verify-completed-user-actions]] · [[truth-source-judgment]]
