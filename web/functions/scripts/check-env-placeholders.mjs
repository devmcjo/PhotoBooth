/**
 * check-env-placeholders.mjs — 배포 전 `.env` 플레이스홀더 차단 가드 (Step F3 · 2026-08-01).
 *
 * 왜 있는가:
 *   인수인계 문서의 예시 명령을 값 치환 없이 실행해 `.env.mcphoto-955fb`에
 *   `GOOGLE_OAUTH_CLIENT_ID_WEB=<A1의 웹 client_id>` 가 그대로 저장됐고, 그대로 배포됐다.
 *   **배포는 성공했다.** 웹 로그인만 조용히 100% `invalid_client`로 실패했고 아무도 눈치채지 못했다.
 *   같은 실수를 사람이 아니라 기계가 잡게 한다.
 *
 * 실행 위치: `web/functions/`
 *   npm run build && npm run check:env      # lib/domain/envPlaceholder.js 필요
 *   node scripts/check-env-placeholders.mjs
 *
 * `web/deploy-web.bat`의 `[2/3] Building functions` 직후에 호출된다(functions 경로에서만).
 *
 * 종료 코드: 0 = 문제 없음(검사 대상 파일이 없어도 0), 1 = 플레이스홀더/빈 필수값 발견.
 *
 * ⚠️ **값은 절대 출력하지 않는다** — 키 이름만 찍는다(시크릿이 CI 로그로 새면 안 된다).
 */
import { readFileSync, existsSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

// 순수 판정은 TypeScript로 작성해 jest로 검증한다(src/domain/envPlaceholder.ts).
// 여기서는 컴파일 산출물을 쓴다 — 실행 전 `npm run build` 필요.
let mod;
try {
  mod = await import("../lib/domain/envPlaceholder.js");
} catch (err) {
  console.error(
    "lib/domain/envPlaceholder.js 를 찾을 수 없습니다. web/functions 에서 `npm run build` 를 먼저 실행하세요."
  );
  console.error(String(err?.message ?? err));
  process.exit(1);
}
const { findPlaceholderKeys } = mod;

const functionsDir = resolve(dirname(fileURLToPath(import.meta.url)), "..");

/** `.firebaserc`의 기본 프로젝트 id(없으면 null). 배포가 읽는 파일과 같은 것을 검사하기 위함. */
function defaultProjectId() {
  const rcPath = join(functionsDir, "..", ".firebaserc");
  if (!existsSync(rcPath)) return null;
  try {
    const rc = JSON.parse(readFileSync(rcPath, "utf8"));
    const id = rc?.projects?.default;
    return typeof id === "string" && id.length > 0 ? id : null;
  } catch {
    // .firebaserc가 깨져 있어도 이 가드가 배포를 막을 이유는 없다 — `.env`만 검사한다.
    return null;
  }
}

const targets = [join(functionsDir, ".env")];
const projectId = defaultProjectId();
if (projectId) targets.push(join(functionsDir, `.env.${projectId}`));

let failed = false;
let checked = 0;

for (const file of targets) {
  if (!existsSync(file)) continue; // 파일이 없는 환경(CI 등)은 정상이다.
  checked += 1;
  const keys = findPlaceholderKeys(readFileSync(file, "utf8"));
  if (keys.length === 0) {
    console.log(`  [ OK ] ${file}`);
    continue;
  }
  failed = true;
  console.error(`  [FAIL] ${file}`);
  for (const key of keys) {
    console.error(`         ${key} — 치환되지 않은 <플레이스홀더> 또는 빈 필수값`);
  }
}

if (checked === 0) {
  console.log("  검사할 .env 파일이 없습니다(건너뜀).");
}

if (failed) {
  console.error("");
  console.error("*** 배포 중단: env 값을 실제 값으로 치환한 뒤 다시 실행하세요. ***");
  console.error("    치환하지 않고 배포하면 배포는 성공하지만 웹 로그인이 100% 실패합니다.");
  process.exit(1);
}

process.exit(0);
