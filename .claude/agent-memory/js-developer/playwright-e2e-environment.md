---
name: playwright-e2e-environment
description: webclient E2E(Playwright 1.49.1)의 브라우저 빌드 한계 3건 — headless shell에 getUserMedia 없음, WebKit에 OPFS/OffscreenCanvas 없음, CDP 쿼터 override가 OPFS 쓰기를 막지 못함
metadata:
  type: project
---

Step 17에서 Playwright E2E를 세우며 **실측으로 확인한 브라우저 빌드 한계 3건.**
전부 "설계가 가능하다고 가정했으나 실제로는 아니었던" 것들이라 재발견 비용이 크다.

## 1. Playwright 1.49 기본 headless에는 `getUserMedia`가 없다

기본값은 구 `chromium_headless_shell` 빌드이고, 여기서 `navigator.mediaDevices.getUserMedia`가
`NotSupportedError: Not supported`로 즉시 실패한다 → **카메라 시나리오 전량 실패**.
`--use-fake-device-for-media-stream`을 줘도 소용없다(스위치 이전의 문제).

**해법**: chromium 프로젝트 `use`에 `channel: "chromium"` 한 줄. 정식 Chromium 빌드를
새 headless 모드로 띄운다. `headless: false`까지 갈 필요 없다.

## 2. Playwright WebKit 18.2(Windows)에는 OPFS도 `OffscreenCanvas`도 없다

런타임 probe 결과: `navigator.storage.getDirectory` **부재**, `OffscreenCanvas` **부재**
(`Worker`는 있다). 그래서 `getOpfsClient()`가 `UNSUPPORTED_OPFS_CLIENT`로 떨어져
**모든 저장이 조용히 실패**한다("프레임 로컬 저장 실패" 로그로 드러난다).

⚠️ **이것은 Safari의 동작이 아니다** — 실제 Safari 17+에는 OPFS가 있다.
따라서 WebKit 프로젝트가 통과해도 Safari가 검증된 것이 아니며, 저장 경로는 iPad 실측이 소유한다.
저장 쓰기가 필요한 spec에는 태그를 달아 `grepInvert`로 뺀다(현재 `@camera|@opfs-write`).

## 3. CDP `Storage.overrideQuotaForOrigin(origin, 0)`은 OPFS 쓰기를 막지 못한다

`navigator.storage.estimate().quota`는 **정말 0이 되지만**, 그 직후 2 MiB OPFS 쓰기가
**그대로 성공**한다(Chromium 131). 저장 실패를 유발하는 레버로 쓸 수 없다.

`navigator.storage.getDirectory`를 지우는 우회도 **틀렸다** — 컷조차 못 읽어 합성이 실패하고
`finalBlob === null` → `skipped`(토스트 없음)가 되어 **보려던 것과 다른 경로**를 관측하게 된다.

→ 저장 실패 표시(M4/E6)는 **자동화 불가**로 판정하고 실측(V19-6)에 남겼다.

## 부수 관측 (제품 결함 아님 — 오해 방지)

| 관측 | 원인 |
|------|------|
| dev에서 `uploads/prepare`만 2건 | `<StrictMode>` 개발 빌드 이중 effect. `useUploadRun`이 첫 실행을 abort하므로 PUT·commit은 1건씩 |
| `logger.error`가 브라우저 콘솔에 보임 | `createLogStore({ mirrorToConsole })`가 개발 빌드에서만 켜진다 |
| `/favicon.ico` 404 | 저장소에 favicon이 없다. **브라우저 내부 요청이라 `page.route`로 못 막는다** → 콘솔 메시지의 `location().url`로 걸러야 한다 |
| `A VideoFrame was garbage collected…` | `camera.stop()`의 Worker `terminate()`가 소유 프레임을 버린다. 자원은 Worker와 함께 회수되므로 누수 아님. **GC 타이밍 의존이라 비결정적** |

관련: [[webclient-verification-gate]]
