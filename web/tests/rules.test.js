// MCPhoto 보안 규칙 Emulator 테스트 (계약 §5.3, web-architecture.md §6.3).
//
// 웹(비인증) 관점에서 allow/deny 를 단정한다. 7 케이스(a~g):
//   (a) users/{u} get            → deny
//   (b) frameTemplates/{f} get    → deny
//   (c) resultSessions list(쿼리) → deny   ← 토큰 열거 방어, 회귀 감시 핵심(WR3)
//   (d) resultSessions/{token} get → allow  ← 웹 기능 자체(deny 로 떨어지면 회귀)
//   (e) resultSessions/{sid} write → deny
//   (f) Storage results/… SDK read → deny  ← 정상(웹은 토큰 URL 사용)
//   (g) Storage frames/… SDK read  → deny
//
// firebase emulators:exec --only firestore,storage "node tests/rules.test.js" 로 실행.
// 종료 코드 0 = 전 케이스 PASS.
import fs from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";
import {
  initializeTestEnvironment,
  assertFails,
  assertSucceeds
} from "@firebase/rules-unit-testing";
import {
  doc,
  getDoc,
  getDocs,
  setDoc,
  collection
} from "firebase/firestore";
import { ref, getBytes, uploadBytes } from "firebase/storage";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(__dirname, "..");

const PROJECT_ID = "demo-mcphoto-rules";
const VALID_TOKEN = "11111111-1111-4111-8111-111111111111";

let passed = 0;
let failed = 0;

async function expect(label, promise) {
  try {
    await promise;
    passed++;
    console.log(`  PASS  ${label}`);
  } catch (err) {
    failed++;
    console.error(`  FAIL  ${label}: ${err && err.message ? err.message : err}`);
  }
}

async function main() {
  const testEnv = await initializeTestEnvironment({
    projectId: PROJECT_ID,
    firestore: {
      rules: fs.readFileSync(path.join(webRoot, "firestore.rules"), "utf8")
    },
    storage: {
      rules: fs.readFileSync(path.join(webRoot, "storage.rules"), "utf8")
    }
  });

  // 시드: Admin(규칙 우회) 컨텍스트로 문서·파일 생성.
  await testEnv.withSecurityRulesDisabled(async (ctx) => {
    const fdb = ctx.firestore();
    await setDoc(doc(fdb, "resultSessions", VALID_TOKEN), {
      finalImageUrl: "https://example.com/final.jpg",
      timelapseUrl: null,
      expiresAt: new Date(Date.now() + 24 * 3600 * 1000),
      createdAt: new Date()
    });
    await setDoc(doc(fdb, "users", "u1"), { id: "u1", password: "x", role: "user" });
    await setDoc(doc(fdb, "frameTemplates", "f1"), { id: "f1", name: "default" });

    const storage = ctx.storage();
    await uploadBytes(
      ref(storage, "results/" + VALID_TOKEN + "/final.jpg"),
      new Uint8Array([1, 2, 3]),
      { contentType: "image/jpeg" }
    );
    await uploadBytes(
      ref(storage, "frames/u1/frame.png"),
      new Uint8Array([4, 5, 6]),
      { contentType: "image/png" }
    );
  });

  // 비인증(웹) 컨텍스트.
  const guest = testEnv.unauthenticatedContext();
  const gdb = guest.firestore();
  const gstorage = guest.storage();

  console.log("보안 규칙 테스트 (비인증/웹 관점):");
  // (a) users get → deny
  await expect("(a) users/{u} get → deny", assertFails(getDoc(doc(gdb, "users", "u1"))));
  // (b) frameTemplates get → deny
  await expect(
    "(b) frameTemplates/{f} get → deny",
    assertFails(getDoc(doc(gdb, "frameTemplates", "f1")))
  );
  // (c) resultSessions list(쿼리) → deny
  await expect(
    "(c) resultSessions list → deny",
    assertFails(getDocs(collection(gdb, "resultSessions")))
  );
  // (d) resultSessions 단건 get → allow
  await expect(
    "(d) resultSessions/{token} get → allow",
    assertSucceeds(getDoc(doc(gdb, "resultSessions", VALID_TOKEN)))
  );
  // (e) resultSessions write → deny
  await expect(
    "(e) resultSessions/{sid} write → deny",
    assertFails(setDoc(doc(gdb, "resultSessions", "attacker"), { finalImageUrl: "x" }))
  );
  // (f) Storage results/ SDK read → deny
  await expect(
    "(f) Storage results/… SDK read → deny",
    assertFails(getBytes(ref(gstorage, "results/" + VALID_TOKEN + "/final.jpg")))
  );
  // (g) Storage frames/ SDK read → deny
  await expect(
    "(g) Storage frames/… SDK read → deny",
    assertFails(getBytes(ref(gstorage, "frames/u1/frame.png")))
  );

  await testEnv.cleanup();

  console.log(`\n결과: ${passed} passed, ${failed} failed`);
  if (failed > 0) process.exit(1);
  process.exit(0);
}

main().catch((err) => {
  console.error("테스트 실행 오류:", err);
  process.exit(1);
});
