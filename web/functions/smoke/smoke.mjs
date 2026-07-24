/**
 * Firebase Emulator 엔드포인트 스모크 테스트.
 *
 * `firebase emulators:exec --only functions,firestore,storage "node functions/smoke/smoke.mjs"`
 * 로 실행한다(web/ 디렉토리 기준). Emulator가 뜬 상태에서 실제 HTTP 호출로 12개 엔드포인트의
 * 인증·역할검증·업로드 흐름을 검증한다. Admin(규칙 우회)으로 시드 계정을 심고 로그인부터 진행.
 *
 * 종료코드 0 = 전 케이스 PASS.
 */
import admin from "firebase-admin";

const PROJECT = process.env.GCLOUD_PROJECT || "mcphoto-955fb";
const REGION = "asia-northeast3";
const FN_HOST = process.env.FUNCTIONS_EMULATOR_HOST || "127.0.0.1:5001";
const BASE = `http://${FN_HOST}/${PROJECT}/${REGION}/api`;
const API_KEY = "dev-client-key";

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
  const res = await fetch(`${BASE}${path}`, {
    method,
    headers,
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });
  let json = null;
  const text = await res.text();
  if (text) {
    try {
      json = JSON.parse(text);
    } catch {
      json = { _raw: text };
    }
  }
  return { status: res.status, json };
}

async function main() {
  // Admin SDK(Emulator, 규칙 우회)로 시드 데이터 준비.
  admin.initializeApp({ projectId: PROJECT });
  const db = admin.firestore();

  // 시드: 레거시 평문 admin 계정(지연 마이그레이션 검증용) + 평문 manager + user.
  await db.collection("users").doc("devmcjo").set({
    id: "devmcjo",
    password: "1111", // 평문(레거시) — 로그인 성공 시 해시로 마이그레이션되어야 함
    role: "admin",
    createdAt: admin.firestore.Timestamp.now(),
  });
  await db.collection("users").doc("mgr1").set({
    id: "mgr1",
    password: "mgrpw",
    role: "manager",
    createdAt: admin.firestore.Timestamp.now(),
  });

  console.log(`\n엔드포인트 스모크 (BASE=${BASE}):`);

  // --- /health (인증 없음) ---
  {
    const r = await call("GET", "/health");
    check("(health) GET /health → 200", r.status === 200, `status=${r.status}`);
  }

  // --- API 키 게이트 ---
  {
    const r = await call("GET", "/frames/default"); // 키 없음
    check("(apikey) /frames/default 키 없음 → 401", r.status === 401, `status=${r.status}`);
  }
  {
    const r = await call("GET", "/frames/default", { apiKey: "wrong-key" });
    check("(apikey) /frames/default 잘못된 키 → 401", r.status === 401, `status=${r.status}`);
  }
  {
    const r = await call("GET", "/frames/default", { apiKey: API_KEY });
    check(
      "(F1) /frames/default 유효 키 → 200 배열",
      r.status === 200 && Array.isArray(r.json),
      `status=${r.status}`
    );
  }

  // --- 로그인(A1) + 지연 해시 마이그레이션 ---
  let adminToken = null;
  {
    const r = await call("POST", "/auth/login", { apiKey: API_KEY, body: { id: "devmcjo", password: "wrong" } });
    check("(A1) 로그인 오답 → 401", r.status === 401, `status=${r.status}`);
  }
  {
    const r = await call("POST", "/auth/login", { apiKey: API_KEY, body: { id: "devmcjo", password: "1111" } });
    const okToken = r.status === 200 && typeof r.json?.token === "string";
    const noPw = r.json?.user && r.json.user.password === undefined;
    check("(A1) 로그인 성공 → 200 token", okToken, `status=${r.status}`);
    check("(A1) 응답 user에 비밀번호/해시 미포함", !!noPw);
    if (okToken) adminToken = r.json.token;
  }
  {
    // 마이그레이션 확인: 저장된 password가 이제 bcrypt 해시여야 한다.
    const snap = await db.collection("users").doc("devmcjo").get();
    const stored = snap.data()?.password || "";
    check(
      "(A1) 로그인 후 평문→bcrypt 해시로 지연 마이그레이션",
      /^\$2[aby]\$/.test(stored),
      `stored prefix=${stored.slice(0, 4)}`
    );
  }
  {
    // 마이그레이션 후에도 동일 비번으로 재로그인 성공.
    const r = await call("POST", "/auth/login", { apiKey: API_KEY, body: { id: "devmcjo", password: "1111" } });
    check("(A1) 마이그레이션 후 재로그인 성공", r.status === 200, `status=${r.status}`);
  }

  // manager 로그인(역할 위계 검증용).
  let mgrToken = null;
  {
    const r = await call("POST", "/auth/login", { apiKey: API_KEY, body: { id: "mgr1", password: "mgrpw" } });
    if (r.status === 200) mgrToken = r.json.token;
    check("(A1) manager 로그인 성공", r.status === 200, `status=${r.status}`);
  }

  // --- 계정 CRUD + 역할 위계 (A2~A6) ---
  {
    const r = await call("GET", "/accounts"); // Bearer 없음
    check("(auth) GET /accounts Bearer 없음 → 401", r.status === 401, `status=${r.status}`);
  }
  {
    const r = await call("GET", "/accounts", { bearer: adminToken });
    check(
      "(A4) admin GET /accounts → 200 배열",
      r.status === 200 && Array.isArray(r.json),
      `status=${r.status}`
    );
  }
  {
    // admin → user 생성(A2)
    const r = await call("POST", "/accounts", {
      bearer: adminToken,
      body: { id: "newuser1", password: "pw", role: "user" },
    });
    check("(A2) admin이 user 생성 → 201", r.status === 201, `status=${r.status}`);
  }
  {
    // 중복 생성 → 409
    const r = await call("POST", "/accounts", {
      bearer: adminToken,
      body: { id: "newuser1", password: "pw", role: "user" },
    });
    check("(A2) 중복 id 생성 → 409", r.status === 409, `status=${r.status}`);
  }
  {
    // admin → admin 생성 금지(최종 1인) → 403
    const r = await call("POST", "/accounts", {
      bearer: adminToken,
      body: { id: "wannabeadmin", password: "pw", role: "admin" },
    });
    check("(A2) admin이 admin 생성 → 403", r.status === 403, `status=${r.status}`);
  }
  {
    // manager → manager 생성 금지 → 403 (역할 위계 서버 재검증 핵심)
    const r = await call("POST", "/accounts", {
      bearer: mgrToken,
      body: { id: "mgr2", password: "pw", role: "manager" },
    });
    check("(A2/보안) manager가 manager 생성 → 403", r.status === 403, `status=${r.status}`);
  }
  {
    // manager → user 생성 허용 → 201
    const r = await call("POST", "/accounts", {
      bearer: mgrToken,
      body: { id: "mgruser", password: "pw", role: "user" },
    });
    check("(A2) manager가 user 생성 → 201", r.status === 201, `status=${r.status}`);
  }
  {
    // manager가 admin(devmcjo) 삭제 시도 → 403 (위계: 자신보다 높은 역할 관리 불가)
    const r = await call("DELETE", "/accounts/devmcjo", { bearer: mgrToken });
    check("(A5/보안) manager가 admin 삭제 → 403", r.status === 403, `status=${r.status}`);
  }
  {
    // 비번 변경: 본인(newuser1 토큰 필요) 대신 admin이 대상 변경(파워) → 204
    const r = await call("PATCH", "/accounts/newuser1/password", {
      bearer: adminToken,
      body: { newPassword: "newpw123" },
    });
    check("(A3) admin이 하위 계정 비번 변경 → 204", r.status === 204, `status=${r.status}`);
  }
  {
    // 역할 지정(A6): admin이 newuser1(user)을 manager로 승격 → 204
    const r = await call("PATCH", "/accounts/newuser1/role", {
      bearer: adminToken,
      body: { role: "manager" },
    });
    check("(A6) admin이 user→manager 승격 → 204", r.status === 204, `status=${r.status}`);
  }
  {
    // 역할 지정 admin 금지 → 403
    const r = await call("PATCH", "/accounts/newuser1/role", {
      bearer: adminToken,
      body: { role: "admin" },
    });
    check("(A6/보안) admin 역할 지정 → 403", r.status === 403, `status=${r.status}`);
  }
  {
    // manager가 역할 지정 시도 → 403 (admin 전용)
    const r = await call("PATCH", "/accounts/mgruser/role", {
      bearer: mgrToken,
      body: { role: "manager" },
    });
    check("(A6/보안) manager가 역할 지정 → 403", r.status === 403, `status=${r.status}`);
  }
  {
    // admin이 하위 계정 삭제(A5) → 204 (+cascade)
    const r = await call("DELETE", "/accounts/mgruser", { bearer: adminToken });
    check("(A5) admin이 user 삭제 → 204", r.status === 204, `status=${r.status}`);
  }
  {
    // 입력 검증: 잘못된 id 형식 → 400
    const r = await call("POST", "/accounts", {
      bearer: adminToken,
      body: { id: "has space", password: "pw", role: "user" },
    });
    check("(검증) 잘못된 id → 400", r.status === 400, `status=${r.status}`);
  }

  // --- 프레임 저장/삭제 (F3/F4) ---
  let frameId = null;
  {
    // user 권한으론 프레임 저장 불가(파워 전용) → 403
    const userTok = (await call("POST", "/auth/login", { apiKey: API_KEY, body: { id: "newuser1", password: "newpw123" } })).json?.token;
    const r = await call("POST", "/frames", {
      bearer: userTok,
      body: { name: "여름", imageSize: { width: 1200, height: 1800 }, slots: [{ index: 0, x: 10, y: 10, width: 100, height: 100 }] },
    });
    // newuser1은 위에서 manager로 승격됐으므로 파워 → 201 예상. (권한 승격 반영 확인)
    check("(F3) 승격된 계정이 프레임 저장 → 201", r.status === 201 && typeof r.json?.upload?.putUrl === "string", `status=${r.status}`);
    if (r.status === 201) frameId = r.json.frame.id;
  }
  {
    const r = await call("POST", "/frames", {
      bearer: adminToken,
      body: { name: "밑줄_금지", imageSize: { width: 10, height: 10 }, slots: [{ index: 0, x: 0, y: 0, width: 5, height: 5 }] },
    });
    check("(검증) 프레임 이름 '_' 금지 → 400", r.status === 400, `status=${r.status}`);
  }
  {
    const r = await call("GET", "/frames/default", { apiKey: API_KEY });
    const found = Array.isArray(r.json) && r.json.some((f) => f.id === frameId);
    check("(F1) 저장한 공용 기본 프레임이 default 목록에 노출", found, `count=${Array.isArray(r.json) ? r.json.length : "?"}`);
  }
  {
    const r = await call("DELETE", `/frames/${frameId}`, { bearer: adminToken });
    check("(F4) 프레임 삭제 → 200 {deleted:true}", r.status === 200 && r.json?.deleted === true, `status=${r.status}`);
  }
  {
    const r = await call("DELETE", `/frames/nonexistent-id`, { bearer: adminToken });
    check("(F4) 없는 프레임 삭제 → 200 {deleted:false}", r.status === 200 && r.json?.deleted === false, `status=${r.status}`);
  }

  // --- 업로드 prepare/commit (U1/U2) ---
  {
    // sessionId는 서버 규칙에 맞는 형식으로 생성(prepare가 검증).
    const sid = `20260724_120000_11111111-1111-4111-8111-1111111111aa`;
    const prep = await call("POST", "/uploads/prepare", {
      apiKey: API_KEY,
      body: { sessionId: sid, files: [{ kind: "final", ext: "jpg", contentType: "image/jpeg" }] },
    });
    const okPrep =
      prep.status === 200 &&
      Array.isArray(prep.json?.uploads) &&
      prep.json.uploads[0]?.kind === "final" &&
      typeof prep.json.uploads[0]?.putUrl === "string" &&
      typeof prep.json.uploads[0]?.downloadUrl === "string";
    check("(U1) prepare → 200 putUrl+downloadUrl", okPrep, `status=${prep.status}`);

    if (okPrep) {
      const finalUrl = prep.json.uploads[0].downloadUrl;
      const commit = await call("POST", "/uploads/commit", {
        apiKey: API_KEY,
        body: {
          sessionId: sid,
          finalImageUrl: finalUrl,
          timelapseUrl: null,
          retentionHours: 24,
          downloadPageUrl: `https://mcphoto-955fb.web.app/?s=${sid}`,
        },
      });
      check("(U2) commit → 201 resultSession", commit.status === 201 && commit.json?.id === sid, `status=${commit.status}`);

      // 문서가 실제로 생성됐는지 Admin으로 확인.
      const doc = await db.collection("resultSessions").doc(sid).get();
      check("(U2) resultSessions 문서 생성 확인", doc.exists && doc.data()?.finalImageUrl === finalUrl);

      // 최소 1개 불변식: 둘 다 null commit → 400
      const bad = await call("POST", "/uploads/commit", {
        apiKey: API_KEY,
        body: { sessionId: `20260724_120001_22222222-2222-4222-8222-2222222222bb`, finalImageUrl: null, timelapseUrl: null, retentionHours: 24, downloadPageUrl: "x" },
      });
      check("(U2/불변식) 미디어 0개 commit → 400", bad.status === 400, `status=${bad.status}`);

      // 위조 URL(다른 세션 경로) commit → 400
      const forged = await call("POST", "/uploads/commit", {
        apiKey: API_KEY,
        body: {
          sessionId: `20260724_120002_33333333-3333-4333-8333-3333333333cc`,
          finalImageUrl: finalUrl, // 다른 세션의 URL
          timelapseUrl: null,
          retentionHours: 24,
          downloadPageUrl: "x",
        },
      });
      check("(U2/보안) 세션 불일치 URL commit → 400", forged.status === 400, `status=${forged.status}`);
    }

    // 잘못된 sessionId 형식 prepare → 400
    const badSid = await call("POST", "/uploads/prepare", {
      apiKey: API_KEY,
      body: { sessionId: "not-valid", files: [{ kind: "final", ext: "jpg", contentType: "image/jpeg" }] },
    });
    check("(U1/검증) 잘못된 sessionId → 400", badSid.status === 400, `status=${badSid.status}`);
  }

  // --- 404 ---
  {
    const r = await call("GET", "/no-such-endpoint", { apiKey: API_KEY });
    check("(라우팅) 없는 경로 → 404", r.status === 404, `status=${r.status}`);
  }

  console.log(`\n결과: ${passed} passed, ${failed} failed`);
  await admin.app().delete();
  if (failed > 0) process.exit(1);
  process.exit(0);
}

main().catch((err) => {
  console.error("스모크 실행 오류:", err);
  process.exit(1);
});
