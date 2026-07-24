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
import { createHash } from "node:crypto";

/** sha256 소문자 hex — 서버 domain/tokens.hashToken과 동일(토큰 문서 심기용). */
const sha256 = (v) => createHash("sha256").update(v, "utf8").digest("hex");

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

  // --- 프레임 업데이트 PUT /frames/{id} (같은 id 덮어쓰기, item2 서버 파트) ---
  {
    // Bearer 없음 → 401
    const r = await call("PUT", `/frames/${frameId}`, {
      body: { name: "겨울", imageSize: { width: 1200, height: 1800 }, slots: [{ index: 0, x: 20, y: 20, width: 200, height: 200 }] },
    });
    check("(F5) PUT Bearer 없음 → 401", r.status === 401, `status=${r.status}`);
  }
  {
    // user(비파워) 권한 → 403. 이 시점 비파워 계정을 admin이 새로 만들어 로그인(요구 3 권한 게이트 검증).
    await call("POST", "/accounts", {
      bearer: adminToken,
      body: { id: "frameuser", password: "fupw", role: "user" },
    });
    const userTok = (await call("POST", "/auth/login", { apiKey: API_KEY, body: { id: "frameuser", password: "fupw" } })).json?.token;
    const r = await call("PUT", `/frames/${frameId}`, {
      bearer: userTok,
      body: { name: "겨울", imageSize: { width: 1200, height: 1800 }, slots: [{ index: 0, x: 20, y: 20, width: 200, height: 200 }] },
    });
    check("(F5/권한) user가 PUT → 403", r.status === 403, `status=${r.status}`);
  }
  {
    // 없는 id → 404
    const r = await call("PUT", `/frames/nonexistent-id`, {
      bearer: adminToken,
      body: { name: "겨울", imageSize: { width: 1200, height: 1800 }, slots: [{ index: 0, x: 20, y: 20, width: 200, height: 200 }] },
    });
    check("(F5) 없는 프레임 PUT → 404", r.status === 404, `status=${r.status}`);
  }
  {
    // 이름 '_' 금지 → 400
    const r = await call("PUT", `/frames/${frameId}`, {
      bearer: adminToken,
      body: { name: "겨울_x", imageSize: { width: 1200, height: 1800 }, slots: [{ index: 0, x: 20, y: 20, width: 200, height: 200 }] },
    });
    check("(F5/검증) PUT 이름 '_' 금지 → 400", r.status === 400, `status=${r.status}`);
  }
  {
    // 이미지 미변경(replaceImage 없음) 메타 갱신 → 200, upload 없음, name/slots 반영, isDefault·userId 보존
    const r = await call("PUT", `/frames/${frameId}`, {
      bearer: adminToken,
      body: { name: "겨울", imageSize: { width: 1200, height: 1800 }, slots: [{ index: 0, x: 20, y: 20, width: 200, height: 200 }] },
    });
    const okMeta =
      r.status === 200 &&
      r.json?.frame?.id === frameId &&
      r.json.frame.name === "겨울" &&
      r.json.frame.isDefault === true &&
      r.json.frame.userId === null &&
      Array.isArray(r.json.frame.slots) &&
      r.json.frame.slots[0]?.x === 20 &&
      r.json.upload === undefined;
    check("(F5) 이미지 미변경 PUT → 200 메타 갱신(upload 없음, 보존필드 유지)", okMeta, `status=${r.status}`);
  }
  {
    // Firestore에 갱신 반영·보존 필드 확인(같은 문서 덮어쓰기, 중복 문서 없음).
    const snap = await db.collection("frameTemplates").where("isDefault", "==", true).get();
    const docs = snap.docs.filter((d) => d.id === frameId);
    const doc = docs[0]?.data();
    check(
      "(F5) 같은 frameId 문서 1건 유지 + name/isDefault/userId 보존",
      docs.length === 1 && doc?.name === "겨울" && doc?.isDefault === true && (doc?.userId ?? null) === null,
      `count=${docs.length} name=${doc?.name}`
    );
  }
  {
    // 이미지 교체(replaceImage=true) → 200 + 서명 PUT URL 발급
    const r = await call("PUT", `/frames/${frameId}`, {
      bearer: adminToken,
      body: { name: "겨울", imageSize: { width: 1200, height: 1800 }, slots: [{ index: 0, x: 20, y: 20, width: 200, height: 200 }], replaceImage: true },
    });
    const okReplace =
      r.status === 200 &&
      typeof r.json?.upload?.putUrl === "string" &&
      typeof r.json?.upload?.downloadUrl === "string" &&
      r.json.frame?.imageUrl === r.json.upload.downloadUrl;
    check("(F5) 이미지 교체 PUT(replaceImage) → 200 서명 PUT URL 발급", okReplace, `status=${r.status}`);
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

  // ─────────────────────────────────────────────────────────────────────────
  // item1a: 이메일 인증 + 비밀번호 재설정 (설계 §5·§6·§8·§12)
  // ─────────────────────────────────────────────────────────────────────────

  // --- 계정 생성 시 email 수집 + unverified 저장 + verify 토큰 발급 ---
  {
    const r = await call("POST", "/accounts", {
      bearer: adminToken,
      body: { id: "emailuser", password: "pw", role: "user", email: "Owner@Example.COM" },
    });
    const okCreate = r.status === 201;
    check("(item1a) email 포함 계정 생성 → 201", okCreate, `status=${r.status}`);
    // 응답에 email(소문자 정규화)·emailVerified=false 포함, 비밀번호/해시 미포함.
    check(
      "(item1a) 응답에 email 정규화·emailVerified=false, 비번 미포함",
      r.json?.email === "owner@example.com" &&
        r.json?.emailVerified === false &&
        r.json?.password === undefined,
      `email=${r.json?.email} verified=${r.json?.emailVerified}`
    );
  }
  {
    // Firestore에 email·emailVerified 저장 + verify 토큰 서브컬렉션 1건 생성 확인.
    const u = await db.collection("users").doc("emailuser").get();
    const okDoc = u.data()?.email === "owner@example.com" && u.data()?.emailVerified === false;
    const toks = await db.collection("users").doc("emailuser").collection("tokens").get();
    const verifyToks = toks.docs.filter((d) => d.data().purpose === "verify_email");
    check("(item1a) users 문서에 email·emailVerified 저장", okDoc);
    check(
      "(item1a) verify 토큰 서브컬렉션 1건 발급(해시 저장)",
      verifyToks.length === 1 && typeof verifyToks[0].data().secretHash === "string",
      `count=${verifyToks.length}`
    );
    // 저장 문서에 평문 secret/code가 없어야 한다(해시만).
    const td = verifyToks[0]?.data() ?? {};
    check(
      "(item1a/보안) 토큰 문서에 평문 secret/code 미저장",
      td.secret === undefined && td.code === undefined && td.secretHash?.length === 64
    );
  }

  // --- email 중복(유일성 강제) → 409 ---
  {
    const r = await call("POST", "/accounts", {
      bearer: adminToken,
      body: { id: "dupemail", password: "pw", role: "user", email: "owner@example.com" },
    });
    check("(item1a/§4.5) 중복 email 생성 → 409", r.status === 409, `status=${r.status}`);
  }
  {
    // 잘못된 email 형식 → 400
    const r = await call("POST", "/accounts", {
      bearer: adminToken,
      body: { id: "bademail", password: "pw", role: "user", email: "not-an-email" },
    });
    check("(item1a/검증) 잘못된 email 형식 → 400", r.status === 400, `status=${r.status}`);
  }
  {
    // email 없이도 계정 생성은 허용(서버 null 허용, §5.1) → 201, emailVerified=false
    const r = await call("POST", "/accounts", {
      bearer: adminToken,
      body: { id: "noemail", password: "pw", role: "user" },
    });
    check(
      "(item1a) email 미포함 계정 생성 허용 → 201 (email=null)",
      r.status === 201 && r.json?.email === null && r.json?.emailVerified === false,
      `status=${r.status} email=${r.json?.email}`
    );
  }

  // --- 이메일 인증 코드 경로(consumeByCode) — 알려진 코드를 Admin으로 심어 검증 ---
  {
    // 서버가 발급한 verify 토큰을 알려진 code로 덮어써서(해시 심기) 코드 경로를 검증한다.
    const KNOWN_CODE = "123456";
    const col = db.collection("users").doc("emailuser").collection("tokens");
    const toks = await col.where("purpose", "==", "verify_email").get();
    await Promise.all(toks.docs.map((d) => d.ref.delete()));
    await col.doc("known-verify-token").set({
      id: "known-verify-token",
      purpose: "verify_email",
      secretHash: sha256("dummy-secret"),
      codeHash: sha256(KNOWN_CODE),
      email: "owner@example.com",
      createdAt: admin.firestore.Timestamp.now(),
      expiresAt: admin.firestore.Timestamp.fromMillis(Date.now() + 3600_000),
      consumedAt: null,
      attempts: 0,
    });

    // 잘못된 코드 → 401(verified 안 됨)
    const wrong = await call("POST", "/auth/verify-email/confirm", {
      apiKey: API_KEY,
      body: { id: "emailuser", code: "000000" },
    });
    check("(item1a) verify 코드 오답 → 401", wrong.status === 401, `status=${wrong.status}`);

    // 올바른 코드 → 200 {verified:true} + emailVerified=true + 토큰 소비(삭제)
    const okv = await call("POST", "/auth/verify-email/confirm", {
      apiKey: API_KEY,
      body: { id: "emailuser", code: KNOWN_CODE },
    });
    check(
      "(item1a) verify 코드 정답 → 200 {verified:true}",
      okv.status === 200 && okv.json?.verified === true,
      `status=${okv.status}`
    );
    const u = await db.collection("users").doc("emailuser").get();
    check("(item1a) verify 후 emailVerified=true 반영", u.data()?.emailVerified === true);
    const remain = await col.doc("known-verify-token").get();
    check("(item1a/1회성) verify 성공 후 토큰 소비(삭제)", !remain.exists);

    // 같은 코드 재사용 → 401(이미 소비됨)
    const reuse = await call("POST", "/auth/verify-email/confirm", {
      apiKey: API_KEY,
      body: { id: "emailuser", code: KNOWN_CODE },
    });
    check("(item1a/1회성) 소비된 코드 재사용 → 401", reuse.status === 401, `status=${reuse.status}`);
  }

  // --- 토큰 만료 검증(expiresAt 과거) ---
  {
    const col = db.collection("users").doc("emailuser").collection("tokens");
    await col.doc("expired-token").set({
      id: "expired-token",
      purpose: "verify_email",
      secretHash: sha256("x"),
      codeHash: sha256("999999"),
      email: "owner@example.com",
      createdAt: admin.firestore.Timestamp.fromMillis(Date.now() - 7200_000),
      expiresAt: admin.firestore.Timestamp.fromMillis(Date.now() - 3600_000), // 1시간 전 만료
      consumedAt: null,
      attempts: 0,
    });
    const r = await call("POST", "/auth/verify-email/confirm", {
      apiKey: API_KEY,
      body: { id: "emailuser", code: "999999" },
    });
    check("(item1a/만료) 만료 토큰 코드 → 401", r.status === 401, `status=${r.status}`);
    const remain = await col.doc("expired-token").get();
    check("(item1a/만료) 만료 토큰은 정리(삭제)", !remain.exists);
  }

  // --- 비밀번호 재설정: request 열거 방지(존재/상태 무관 202) ---
  {
    const r1 = await call("POST", "/auth/password-reset/request", {
      apiKey: API_KEY,
      body: { idOrEmail: "no-such-account-xyz" },
    });
    check("(item1a/열거방지) 없는 계정 reset 요청 → 202", r1.status === 202, `status=${r1.status}`);

    const r2 = await call("POST", "/auth/password-reset/request", {
      apiKey: API_KEY,
      body: { idOrEmail: "noemail" }, // email 없는 계정
    });
    check("(item1a/열거방지) email 없는 계정 reset 요청 → 202", r2.status === 202, `status=${r2.status}`);

    const r3 = await call("POST", "/auth/password-reset/request", {
      apiKey: API_KEY,
      body: { idOrEmail: "owner@example.com" }, // verified 계정(emailuser) — 실제 발송(LogEmailSender)
    });
    check("(item1a/열거방지) verified 계정 reset 요청 → 202", r3.status === 202, `status=${r3.status}`);
  }
  {
    // verified 계정 요청 시 실제 reset 토큰이 발급됐는지(no-op이 아닌지) 확인.
    const toks = await db
      .collection("users")
      .doc("emailuser")
      .collection("tokens")
      .where("purpose", "==", "password_reset")
      .get();
    check("(item1a) verified 계정 reset 요청 시 토큰 발급됨", toks.size === 1, `count=${toks.size}`);
  }

  // --- 비밀번호 재설정 코드 경로 confirm(알려진 코드 심기) ---
  {
    const KNOWN = "222333";
    const col = db.collection("users").doc("emailuser").collection("tokens");
    const rt = await col.where("purpose", "==", "password_reset").get();
    await Promise.all(rt.docs.map((d) => d.ref.delete()));
    await col.doc("known-reset-token").set({
      id: "known-reset-token",
      purpose: "password_reset",
      secretHash: sha256("dummy"),
      codeHash: sha256(KNOWN),
      email: "owner@example.com",
      createdAt: admin.firestore.Timestamp.now(),
      expiresAt: admin.firestore.Timestamp.fromMillis(Date.now() + 3600_000),
      consumedAt: null,
      attempts: 0,
    });

    // idOrEmail(email로도 조회) + 코드 + 새 비번 → 200 {reset:true}
    const r = await call("POST", "/auth/password-reset/confirm", {
      apiKey: API_KEY,
      body: { idOrEmail: "owner@example.com", code: KNOWN, newPassword: "brandnewpw" },
    });
    check(
      "(item1a) reset 코드 정답(email 조회) → 200 {reset:true}",
      r.status === 200 && r.json?.reset === true,
      `status=${r.status}`
    );
    const remain = await col.doc("known-reset-token").get();
    check("(item1a/1회성) reset 성공 후 토큰 소비(삭제)", !remain.exists);

    // 새 비번으로 로그인되고, 기존 비번은 실패해야 한다.
    const login1 = await call("POST", "/auth/login", {
      apiKey: API_KEY,
      body: { id: "emailuser", password: "brandnewpw" },
    });
    check("(item1a) 재설정된 새 비번으로 로그인 성공", login1.status === 200, `status=${login1.status}`);
    const login2 = await call("POST", "/auth/login", {
      apiKey: API_KEY,
      body: { id: "emailuser", password: "pw" },
    });
    check("(item1a) 기존 비번은 로그인 실패 → 401", login2.status === 401, `status=${login2.status}`);
  }

  // --- 코드 시도 횟수 제한(5회 초과 시 무효화, §12) ---
  {
    const col = db.collection("users").doc("newuser1").collection("tokens");
    const existing = await col.get();
    await Promise.all(existing.docs.map((d) => d.ref.delete()));
    await col.doc("attempt-token").set({
      id: "attempt-token",
      purpose: "verify_email",
      secretHash: sha256("x"),
      codeHash: sha256("111222"),
      email: "attempt@example.com",
      createdAt: admin.firestore.Timestamp.now(),
      expiresAt: admin.firestore.Timestamp.fromMillis(Date.now() + 3600_000),
      consumedAt: null,
      attempts: 0,
    });
    // newuser1이 위에서 email을 갖도록, email 필드도 맞춰 심는다.
    await db.collection("users").doc("newuser1").set(
      { email: "attempt@example.com", emailVerified: false },
      { merge: true }
    );

    // 5회 오답 → 5회째에 토큰 무효화(삭제).
    for (let i = 0; i < 5; i++) {
      await call("POST", "/auth/verify-email/confirm", {
        apiKey: API_KEY,
        body: { id: "newuser1", code: "000000" },
      });
    }
    const gone = await col.doc("attempt-token").get();
    check("(item1a/§12) 코드 5회 오답 후 토큰 무효화(삭제)", !gone.exists);
  }

  // --- PATCH /accounts/{id}/email — 등록/변경 시 emailVerified=false 리셋 + verify 재발송 ---
  {
    // admin이 noemail 계정에 email 지정(파워 경로).
    const r = await call("PATCH", "/accounts/noemail/email", {
      bearer: adminToken,
      body: { email: "later@example.com" },
    });
    check("(item1a/§8.3) PATCH email(파워) → 204", r.status === 204, `status=${r.status}`);
    const u = await db.collection("users").doc("noemail").get();
    check(
      "(item1a/§8.3) email 지정 시 emailVerified=false + verify 토큰 발급",
      u.data()?.email === "later@example.com" && u.data()?.emailVerified === false
    );
    const toks = await db
      .collection("users")
      .doc("noemail")
      .collection("tokens")
      .where("purpose", "==", "verify_email")
      .get();
    check("(item1a/§8.3) PATCH email 후 verify 토큰 1건", toks.size === 1, `count=${toks.size}`);
  }

  // --- verify-email/request 재발송 + 열거 방지 ---
  {
    const r = await call("POST", "/auth/verify-email/request", {
      apiKey: API_KEY,
      body: { idOrEmail: "no-such-xyz" },
    });
    check("(item1a/열거방지) 없는 계정 verify 재발송 요청 → 202", r.status === 202, `status=${r.status}`);
  }

  // ─────────────────────────────────────────────────────────────────────────
  // item1b: Google SSO /auth/google (설계 §5·§6)
  //  실제 Google 왕복은 Emulator에서 불가하므로, API키 게이트 + 미구성(501) + 형식검증(400) 경로만 검증.
  //  code 교환·id_token 검증·매핑(loginWithGoogleEmail)은 googleAuth 단위 테스트(OAuth2Client mock)로 커버.
  // ─────────────────────────────────────────────────────────────────────────
  {
    // API 키 없음 → 401(게이트).
    const r = await call("POST", "/auth/google", {
      body: { code: "x", codeVerifier: "A".repeat(43), redirectUri: "http://127.0.0.1:52001/" },
    });
    check("(item1b) /auth/google 키 없음 → 401", r.status === 401, `status=${r.status}`);
  }
  {
    // 유효 키 + 요청. GOOGLE_OAUTH_CLIENT_ID/SECRET 구성 여부에 따라 분기:
    //  - 미구성(기본 .env): 501(구성 오류) — 형식검증 이전에 비활성 반환.
    //  - 구성됨(GOOGLE_OAUTH_CLIENT_ID/SECRET env): 형식검증이 먼저 → 잘못된 형식은 400.
    const badForm = await call("POST", "/auth/google", {
      apiKey: API_KEY,
      body: { code: "x", codeVerifier: "too-short", redirectUri: "http://evil.com/" },
    });
    if (badForm.status === 501) {
      check("(item1b/미구성) Google 미설정 → 501", true, `status=${badForm.status}`);
    } else {
      check(
        "(item1b/구성됨) 잘못된 codeVerifier/redirectUri 형식 → 400",
        badForm.status === 400,
        `status=${badForm.status}`
      );
      // 구성된 경우 loopback redirectUri SSRF 차단도 확인(외부 host 거부).
      const ssrf = await call("POST", "/auth/google", {
        apiKey: API_KEY,
        body: { code: "x", codeVerifier: "A".repeat(43), redirectUri: "https://attacker.example/" },
      });
      check(
        "(item1b/보안) 비-loopback redirectUri → 400",
        ssrf.status === 400,
        `status=${ssrf.status}`
      );
    }
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
