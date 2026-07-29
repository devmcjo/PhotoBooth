/**
 * post-deploy-smoke.mjs — 배포된(실제) 백엔드 도달·기본 흐름 검증.
 *
 * Emulator smoke(smoke.mjs)와 달리 이건 **실제 배포된 함수 URL**을 친다. 읽기 전용 호출만 하므로
 * 데이터에 영향 없음(health / login / frames 조회). 서명 PUT·업로드 실왕복은 앱에서 수동 확인한다.
 *
 * 사용법 (web/functions 또는 아무 곳에서):
 *   BASE_URL="https://asia-northeast3-mcphoto-955fb.cloudfunctions.net/api" \
 *   API_KEY="<CLIENT_API_KEYS 값>" \
 *   LOGIN_ID="devmcjo" LOGIN_PW="<비번>" \
 *   node post-deploy-smoke.mjs
 *
 *   - BASE_URL 필수(배포 후 출력되는 함수 URL, 끝에 /api 포함).
 *   - API_KEY 필수(set-secrets.sh 가 출력한 BackendApiKey 값).
 *   - LOGIN_ID/LOGIN_PW 는 선택 — 주면 로그인 왕복까지 검증(안 주면 health+frames 게이트만).
 *
 * 종료코드 0 = 전 케이스 PASS.
 */
const BASE_URL = (process.env.BASE_URL || "").replace(/\/+$/, "");
const API_KEY = process.env.API_KEY || "";
const LOGIN_ID = process.env.LOGIN_ID || "";
const LOGIN_PW = process.env.LOGIN_PW || "";

if (!BASE_URL) {
  console.error("BASE_URL 미지정 — 배포된 함수 URL(끝에 /api 포함)을 넣으세요.");
  process.exit(2);
}

let passed = 0;
let failed = 0;
function check(label, cond, detail = "") {
  if (cond) {
    passed++;
    console.log(`  PASS  ${label}`);
  } else {
    failed++;
    console.error(`  FAIL  ${label}${detail ? " — " + detail : ""}`);
  }
}

async function call(method, path, { apiKey, bearer, body } = {}) {
  const headers = {};
  if (apiKey) headers["X-MCPhoto-Client"] = apiKey;
  if (bearer) headers["Authorization"] = `Bearer ${bearer}`;
  if (body !== undefined) headers["Content-Type"] = "application/json";
  let res;
  try {
    res = await fetch(`${BASE_URL}${path}`, {
      method,
      headers,
      body: body !== undefined ? JSON.stringify(body) : undefined,
    });
  } catch (e) {
    return { status: 0, json: null, error: String(e) };
  }
  let json = null;
  const text = await res.text();
  if (text) {
    try {
      json = JSON.parse(text);
    } catch {
      json = { _raw: text.slice(0, 200) };
    }
  }
  return { status: res.status, json };
}

async function main() {
  console.log(`== 배포 스모크: ${BASE_URL} ==`);

  // 1) 헬스 — 인증 없이 200.
  {
    const r = await call("GET", "/health");
    check("health 200 + status:ok", r.status === 200 && r.json?.status === "ok", `status=${r.status}`);
  }

  // 2) API 키 게이트 — 키 없으면 401.
  {
    const r = await call("GET", "/frames/default");
    check("frames 키 없음 → 401", r.status === 401, `status=${r.status}`);
  }

  // 3) API 키 유효 → 기본 프레임 200 배열 (키가 맞는지 = 배포된 CLIENT_API_KEYS 일치 확인).
  if (API_KEY) {
    const r = await call("GET", "/frames/default", { apiKey: API_KEY });
    check("frames 유효 키 → 200 배열", r.status === 200 && Array.isArray(r.json), `status=${r.status}`);
  } else {
    console.log("  SKIP  frames 유효 키 (API_KEY 미지정)");
  }

  // 4) 로그인 왕복 (선택) — 자격 주면 200 + token.
  if (API_KEY && LOGIN_ID && LOGIN_PW) {
    const r = await call("POST", "/auth/login", { apiKey: API_KEY, body: { id: LOGIN_ID, password: LOGIN_PW } });
    check("로그인 성공 → 200 + token", r.status === 200 && typeof r.json?.token === "string", `status=${r.status}`);
    check("응답 user에 비밀번호/해시 미포함",
      !!r.json && !("password" in (r.json.user || {})) && !("passwordHash" in (r.json.user || {})));
  } else {
    console.log("  SKIP  로그인 왕복 (LOGIN_ID/LOGIN_PW 미지정)");
  }

  console.log(`\n결과: ${passed} passed, ${failed} failed`);
  process.exit(failed === 0 ? 0 : 1);
}

main().catch((e) => {
  console.error("스모크 실행 오류:", e);
  process.exit(1);
});
