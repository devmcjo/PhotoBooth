---
name: functions-secret-wiring
description: Firebase Functions 2nd gen에서 시크릿을 defineSecret+secrets 배열로 연결하지 않으면 배포 런타임에서 process.env가 undefined가 되는 함정
metadata:
  type: project
---

Firebase Functions 2nd gen에서 Secret Manager 시크릿을 런타임 `process.env`에 노출하려면 두 가지가 모두 필요하다: (1) `defineSecret("NAME")` 선언, (2) 함수 정의의 `secrets: [...]` 배열에 포함(`src/index.ts`의 `onRequest({ secrets: [...] }, app)`).

**Why:** `firebase functions:secrets:set NAME`으로 값을 등록해도, 함수에 연결하지 않으면 배포 런타임에서 `process.env.NAME`이 undefined다. `config.ts`가 그 값을 필수로 강제(throw)하면 함수가 부팅 실패한다.

**How to apply:** `config.ts`가 `process.env.X`로 읽는 새 시크릿을 리뷰할 때 항상 `src/index.ts`에서 `defineSecret("X")` + `secrets` 배열 포함 여부를 대조하라. 현재 연결된 것은 JWT_SECRET·CLIENT_API_KEYS 2개뿐. item1a에서 SENDGRID_API_KEY가 config.ts:79에서 읽히지만 index.ts에 미연결 → EMAIL_PROVIDER=sendgrid 전환 시 배포 실패 위험(dev의 EMAIL_PROVIDER=log에서는 미발현). EMAIL_FROM/EMAIL_PROVIDER는 시크릿 아닌 일반 env라 .env/param(CONSOLE)로 주입 가능해 코드 책임 밖.
