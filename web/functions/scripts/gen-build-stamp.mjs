/**
 * 배포 스탬프 생성 — lib/build-stamp.json 에 이 빌드의 시각(UTC ISO8601)을 기록한다.
 *
 * `npm run build`의 마지막 단계로 실행되고, firebase.json의 functions predeploy 훅이 그 build를
 * 호출하므로 결과적으로 "배포 직전 시각" = 최종 웹 배포 시각이 기록된다.
 * GET /health가 이 값을 (유효 클라이언트 키를 제시한 호출자에게만) deployedAt으로 응답하고,
 * WPF 진단·상태 화면의 "Web Deploy Date"가 그것을 표시한다.
 *
 * 산출물은 lib/ 아래(= functions/.gitignore로 무시)이므로 리포를 더럽히지 않는다.
 * 이 스크립트가 실패해도 배포는 계속되어야 하므로(진단 표기 하나 때문에 배포를 막지 않는다)
 * 쓰기 실패는 경고만 남기고 종료 코드 0으로 끝낸다 — 스탬프가 없으면 서버가 deployedAt을 생략한다.
 */
import { mkdirSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const scriptDir = dirname(fileURLToPath(import.meta.url));
const outPath = join(scriptDir, "..", "lib", "build-stamp.json");
const deployedAt = new Date().toISOString();

try {
  // tsc가 이미 만들어 두는 디렉터리지만, 스크립트를 단독 실행하는 경우를 위해 보장한다.
  mkdirSync(dirname(outPath), { recursive: true });
  writeFileSync(outPath, `${JSON.stringify({ deployedAt }, null, 2)}\n`, "utf8");
  console.log(`build-stamp: deployedAt=${deployedAt}`);
} catch (err) {
  console.warn(`build-stamp 생성 실패(배포는 계속됨): ${err instanceof Error ? err.message : err}`);
}
