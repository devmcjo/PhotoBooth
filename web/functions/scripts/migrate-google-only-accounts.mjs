/**
 * migrate-google-only-accounts.mjs — it15 Google-only 계정 마이그레이션 (설계 §8, D3·D4).
 *
 * 하는 일:
 *   Step 2. admin 문서 재생성 — Firestore는 문서 ID를 바꿀 수 없으므로
 *           `devmcjo-2`(SSO 가입) → `devmcjo`(목표 ID)로 **재생성 + 참조 갱신 + 삭제** 순서로 옮긴다.
 *           동시에 role="admin"을 부여한다(HTTP API로는 admin 지정 불가 — canSetRole이 막는다).
 *   Step 3. frameTemplates.userId 참조 갱신(구 id → 목표 id).
 *   Step 4. 전 계정에서 `password`·`emailVerified` 삭제 + `authMethod`를 "google"로 통일.
 *   Step 5. 로그인 불가 계정(email 없음) + 소유 프레임 삭제 — `--delete-orphans` 옵트인 시에만.
 *
 * ⚠️ **기본 모드는 dry-run이다.** `--apply` 없이는 어떤 쓰기도 하지 않는다.
 * ⚠️ 실행 중 email 중복 구간(Step 2 SET ~ Step 3 DELETE 사이)이 잠시 생긴다.
 *    이 구간에 SSO 로그인이 들어오면 매핑이 비결정적이므로 **키오스크 미운영 시간에 실행**할 것(§8.4).
 *
 * 실행 위치: `web/functions/`
 *   npm ci && npm run build                                # lib/domain/migration.js 필요
 *   node scripts/migrate-google-only-accounts.mjs --project mcphoto-955fb            # dry-run
 *   node scripts/migrate-google-only-accounts.mjs --project mcphoto-955fb --apply    # 비파괴 반영
 *   node scripts/migrate-google-only-accounts.mjs --project mcphoto-955fb --apply --delete-orphans
 *   node scripts/migrate-google-only-accounts.mjs --project mcphoto-955fb --clear-pin devmcjo --apply
 *
 * 인증: ADC — `gcloud auth application-default login` 또는 GOOGLE_APPLICATION_CREDENTIALS.
 * 종료 코드: 0=성공, 1=대상 미발견/인자 오류, 2=실행 중 실패(부분 적용 — 재실행 필요).
 */
import { initializeApp } from "firebase-admin/app";
import { FieldValue, getFirestore } from "firebase-admin/firestore";
import { getStorage } from "firebase-admin/storage";

// 순수 계획 로직은 TypeScript로 작성해 jest로 검증한다(src/domain/migration.ts).
// 여기서는 컴파일 산출물을 쓴다 — 실행 전 `npm run build` 필요.
let plan;
try {
  plan = await import("../lib/domain/migration.js");
} catch (err) {
  console.error(
    "lib/domain/migration.js 를 찾을 수 없습니다. web/functions 에서 `npm run build` 를 먼저 실행하세요."
  );
  console.error(String(err?.message ?? err));
  process.exit(1);
}

const {
  BATCH_SIZE,
  adminDocMatches,
  buildAdminDoc,
  chunk,
  frameStoragePrefix,
  isOrphanAccount,
  normalizeEmail,
  parseArgs,
  planFieldCleanup,
} = plan;

const USERS = "users";
const FRAMES = "frameTemplates";
const LINE = "═".repeat(64);

// ── 인자 파싱 ────────────────────────────────────────────────────────────────
const parsed = parseArgs(process.argv.slice(2));
if (!parsed.ok) {
  console.error(`인자 오류: ${parsed.error}`);
  console.error(
    "사용법: node scripts/migrate-google-only-accounts.mjs --project <id> " +
      "[--apply] [--admin-email <email>] [--admin-id <id>] [--delete-orphans] " +
      "[--clear-pin <id>] [--bucket <name>] [--verbose]"
  );
  process.exit(1);
}
const args = parsed.value;
const bucketName = args.bucket || (process.env.STORAGE_BUCKET ?? "").trim();

initializeApp({ projectId: args.project, ...(bucketName ? { storageBucket: bucketName } : {}) });
const db = getFirestore();

/** 계획 항목. kind별로 executePlan이 실제 write를 발행한다. */
const planned = [];
const skipped = [];
const notes = [];

function addPlan(item) {
  planned.push(item);
}

function label(item) {
  switch (item.kind) {
    case "set-user":
      return `+ SET    ${USERS}/${item.id}`;
    case "update-user":
      return `~ UPDATE ${USERS}/${item.id}`;
    case "delete-user":
      return `- DELETE ${USERS}/${item.id}`;
    case "update-frame":
      return `~ UPDATE ${FRAMES}/${item.id}`;
    case "delete-frame":
      return `- DELETE ${FRAMES}/${item.id}`;
    case "delete-storage":
      return `- DELETE storage:${item.prefix}`;
    default:
      return `? ${item.kind}`;
  }
}

function printPlanSection(title, items) {
  console.log(`\n ── ${title} ${"─".repeat(Math.max(0, 50 - title.length))}`);
  if (items.length === 0) {
    console.log("  (변경 없음)");
    return;
  }
  const shown = args.verbose ? items : items.slice(0, 20);
  for (const it of shown) {
    console.log(`  ${label(it)}${it.detail ? `   ${it.detail}` : ""}`);
  }
  if (shown.length < items.length) {
    console.log(`  … 외 ${items.length - shown.length}건 (--verbose 로 전체 표시)`);
  }
  console.log(`  총 ${items.length}건`);
}

// ── --clear-pin: 독립 경로(다른 단계 미실행, §8.3) ───────────────────────────
async function runClearPin(targetId) {
  printHeader();
  const ref = db.collection(USERS).doc(targetId);
  const snap = await ref.get();
  if (!snap.exists) {
    console.error(`\n 대상 계정을 찾을 수 없습니다: ${USERS}/${targetId}`);
    return 1;
  }
  const data = snap.data();
  if (typeof data.pinHash !== "string") {
    console.log(`\n ⓘ ${USERS}/${targetId} 에는 pinHash가 없습니다 — 할 일 없음(멱등).`);
    return 0;
  }

  console.log(`\n ── PIN 초기화 ${"─".repeat(38)}`);
  console.log(`  ~ UPDATE ${USERS}/${targetId}   -pinHash`);
  if (!args.apply) {
    console.log(`\n ⓘ DRY-RUN 이므로 아무것도 변경되지 않았습니다.`);
    console.log(
      `    실제 반영: node scripts/migrate-google-only-accounts.mjs --project ${args.project} --clear-pin ${targetId} --apply`
    );
    return 0;
  }
  await ref.update({ pinHash: FieldValue.delete() });
  console.log(`\n ✔ pinHash 삭제 완료 — 앱에서 재설정하세요(설정/계정 관리 진입 시 최초 설정 강제).`);
  return 0;
}

function printHeader() {
  console.log(LINE);
  console.log(" MCPhoto — Google-only 계정 마이그레이션 (it15)");
  console.log(` project       : ${args.project}`);
  console.log(` mode          : ${args.apply ? "APPLY" : "DRY-RUN"}`);
  if (args.clearPin) {
    console.log(` clear-pin     : ${args.clearPin}`);
  } else {
    console.log(` admin-email   : ${args.adminEmail}`);
    console.log(` admin-id      : ${args.adminId}`);
    console.log(` delete-orphans: ${args.deleteOrphans ? "YES" : "NO"}`);
    console.log(` bucket        : ${bucketName || "(미지정)"}`);
  }
  console.log(LINE);
}

// ── 메인 경로 ────────────────────────────────────────────────────────────────
async function run() {
  if (args.clearPin) return runClearPin(args.clearPin);

  printHeader();

  // A5/A7: 전 컬렉션 스캔(수백 건 규모 가정). 스캔 결과를 리포트에 남긴다.
  const usersSnap = await db.collection(USERS).get();
  const framesSnap = await db.collection(FRAMES).get();
  const users = usersSnap.docs.map((d) => ({ id: d.id, data: d.data() }));
  const frames = framesSnap.docs.map((d) => ({ id: d.id, data: d.data() }));
  console.log(`\n [SCAN] ${USERS} 컬렉션 문서 수: ${users.length}`);
  console.log(` [SCAN] ${FRAMES} 컬렉션 문서 수: ${frames.length}`);
  console.log(` [SCAN] 소유자 참조 필드: ${FRAMES}.userId (다른 컬렉션에 소유자 참조 없음 — A7)`);

  // ── Step 1: 대상 식별(읽기 전용) ──
  const adminSource = users.find((u) => normalizeEmail(u.data.email) === args.adminEmail);
  if (!adminSource) {
    console.error(
      `\n ✖ admin-email(${args.adminEmail}) 계정을 찾을 수 없습니다 — 중단합니다(A6).`
    );
    console.error(
      "   해당 Google 계정으로 앱에서 1회 로그인해 계정을 만든 뒤 다시 실행하거나, --admin-email 을 확인하세요."
    );
    return 1;
  }

  const adminTarget = users.find((u) => u.id === args.adminId) ?? null;
  const sameId = adminSource.id === args.adminId;
  if (sameId) {
    notes.push("admin 원본이 이미 목표 ID로 존재 — 구 문서 삭제·참조 갱신 불요(재실행 안전).");
  }

  // ── Step 2: admin 문서 재생성 계획 ──
  // 설계 §8.3은 "이미 목표 ID면 Step 2·3 생략"이라 하나, role이 admin이 아닌 경우
  // 부트스트랩(P1)이 깨진다. 대신 **목표 문서와 현재 문서가 동일할 때만** 생략한다
  // — 멱등성은 그대로 지키면서 admin 부트스트랩을 항상 보장한다.
  const adminDoc = buildAdminDoc(adminSource.data, args.adminId, adminSource.data.createdAt);
  const sameCreatedAt = (a, b) => {
    if (a === b) return true;
    if (a && b && typeof a.isEqual === "function") return a.isEqual(b);
    return false;
  };
  const step2 = [];
  if (!adminDocMatches(adminTarget ? adminTarget.data : null, adminDoc, sameCreatedAt)) {
    step2.push({
      kind: "set-user",
      id: args.adminId,
      doc: adminDoc,
      detail: `role=admin authMethod=google pinHash=${adminDoc.pinHash ? "승계" : "없음"}${
        adminTarget && !sameId ? " (기존 문서를 덮어씀)" : ""
      }`,
    });
  }
  if (!sameId) {
    step2.push({
      kind: "delete-user",
      id: adminSource.id,
      detail: "원본 SSO 계정 — 재생성 후 제거",
    });
  }

  // ── Step 3: 소유자 참조 갱신(frameTemplates.userId) ──
  const step3 = sameId
    ? []
    : frames
        .filter((f) => f.data.userId === adminSource.id)
        .map((f) => ({
          kind: "update-frame",
          id: f.id,
          patch: { userId: args.adminId },
          detail: `userId: ${adminSource.id} → ${args.adminId}`,
        }));

  // ── Step 5(판정 먼저): 로그인 불가 계정 ──
  // Step 4가 삭제 예정 문서를 건드리지 않도록, orphan 판정을 Step 4보다 먼저 한다.
  const orphans = users.filter(
    (u) => isOrphanAccount(u.data) && u.id !== args.adminId && u.id !== adminSource.id
  );
  const orphanIds = new Set(orphans.map((o) => o.id));

  // ── Step 4: 전 계정 필드 정리 ──
  // 제외 대상: Step 2에서 통째로 SET되는 목표 admin 문서, Step 3에서 삭제되는 원본 문서,
  //            Step 5에서 삭제될 orphan(삭제 예정 문서에 write를 낭비하지 않는다).
  const step4 = [];
  for (const u of users) {
    if (u.id === args.adminId) continue;
    if (!sameId && u.id === adminSource.id) continue;
    if (args.deleteOrphans && orphanIds.has(u.id)) continue;

    const cleanup = planFieldCleanup(u.data);
    if (!cleanup) continue; // 이미 정리됨 — write 발행 안 함(멱등)

    const patch = {};
    for (const f of cleanup.deleteFields) patch[f] = FieldValue.delete();
    if (cleanup.setAuthMethod !== null) patch.authMethod = cleanup.setAuthMethod;

    const bits = [
      ...cleanup.deleteFields.map((f) => `-${f}`),
      ...(cleanup.setAuthMethod !== null
        ? [`authMethod: ${u.data.authMethod ?? "(없음)"} → ${cleanup.setAuthMethod}`]
        : []),
    ];
    step4.push({ kind: "update-user", id: u.id, patch, detail: bits.join("  ") });
  }

  // ── Step 5: orphan 삭제 계획(옵트인) ──
  const step5 = [];
  if (args.deleteOrphans) {
    for (const o of orphans) {
      const owned = frames.filter((f) => f.data.userId === o.id);
      if (owned.length > 0) {
        // Storage 먼저 → Firestore 나중(고아 파일보다 고아 문서가 낫다, §8.4 · deleteAllFramesByUser 규약).
        step5.push({
          kind: "delete-storage",
          prefix: frameStoragePrefix(o.id),
          detail: `프레임 이미지 ${owned.length}건`,
        });
        for (const f of owned) {
          step5.push({ kind: "delete-frame", id: f.id, detail: `owner=${o.id}` });
        }
      }
      step5.push({ kind: "delete-user", id: o.id, detail: `email 없음 (프레임 ${owned.length}건 cascade)` });
    }
  } else {
    for (const o of orphans) {
      const owned = frames.filter((f) => f.data.userId === o.id);
      skipped.push(`${o.id} — email 없음 → 삭제 대상(프레임 ${owned.length}건), --delete-orphans 미지정`);
    }
  }

  // ── 계획 출력 ──
  printPlanSection("Step 2: admin 문서 재생성", step2);
  printPlanSection("Step 3: 소유자 참조 갱신", step3);
  printPlanSection("Step 4: 필드 정리", step4);
  if (args.deleteOrphans) {
    printPlanSection("Step 5: 로그인 불가 계정 삭제(파괴적)", step5);
  } else {
    console.log(`\n ── Step 5: 로그인 불가 계정 ${"─".repeat(27)}`);
    if (skipped.length === 0) {
      console.log("  (대상 없음)");
    } else {
      for (const s of skipped) console.log(`  ! ${s}`);
      console.log("  ⓘ --delete-orphans 미지정 → 건너뜀");
    }
  }

  addPlan(...step2, ...step3, ...step4, ...step5);
  for (const n of notes) console.log(`\n ⓘ ${n}`);

  // orphan 프레임 이미지를 지우려면 버킷명이 필요하다. 모르면 고아 파일이 남으므로 조기 중단.
  const needsBucket = step5.some((p) => p.kind === "delete-storage");
  if (needsBucket && !bucketName) {
    console.error(
      "\n ✖ Storage 버킷명을 알 수 없습니다 — --bucket <name> 또는 env STORAGE_BUCKET 을 지정하세요."
    );
    console.error("   (프레임 이미지를 남긴 채 문서만 지우면 고아 파일이 발생합니다.)");
    return 1;
  }

  const counts = planned.reduce((acc, p) => {
    acc[p.kind] = (acc[p.kind] ?? 0) + 1;
    return acc;
  }, {});
  const summary = Object.entries(counts)
    .map(([k, v]) => `${k} ${v}`)
    .join(" / ");

  console.log(`\n${LINE}`);
  console.log(
    ` 요약: 계획 ${planned.length}건${summary ? ` (${summary})` : ""} · 건너뜀 ${skipped.length}`
  );

  if (!args.apply) {
    console.log(" ⓘ DRY-RUN 이므로 아무것도 변경되지 않았습니다.");
    console.log(
      `    실제 반영: node scripts/migrate-google-only-accounts.mjs --project ${args.project} --apply`
    );
    console.log(LINE);
    return 0;
  }

  if (planned.length === 0) {
    console.log(" ✔ 변경할 것이 없습니다(이미 마이그레이션 완료 — 멱등).");
    console.log(LINE);
    return 0;
  }

  console.log(LINE);
  return executePlan(step2, step3, step4, step5);
}

/**
 * §8.4의 순서를 그대로 강제한다. **이 순서를 어기면 데이터가 사라진다.**
 *   1) admin 문서 생성 → 2) 프레임 참조 갱신 → 3) 구 admin 문서 삭제
 *   → 4) 필드 정리 → 5) orphan 삭제
 * 중간 실패 시 이미 커밋된 배치는 유지되고 non-zero exit — 재실행으로 이어간다.
 */
async function executePlan(step2, step3, step4, step5) {
  const setOps = step2.filter((p) => p.kind === "set-user");
  const deleteAdminOps = step2.filter((p) => p.kind === "delete-user");

  try {
    // 1) admin 문서 생성/갱신 — set(merge:false)이라 재실행해도 동일 결과(멱등).
    for (const op of setOps) {
      await db.collection(USERS).doc(op.id).set(op.doc);
      console.log(` ✔ [1/5] SET ${USERS}/${op.id}`);
    }

    // 2) 프레임 소유자 참조 갱신.
    await commitBatches(step3, (batch, op) =>
      batch.update(db.collection(FRAMES).doc(op.id), op.patch)
    );
    if (step3.length > 0) console.log(` ✔ [2/5] ${FRAMES}.userId 갱신 ${step3.length}건`);

    // 3) 구 admin 문서 삭제(신규 문서가 확실히 생긴 뒤에만).
    for (const op of deleteAdminOps) {
      await db.collection(USERS).doc(op.id).delete();
      console.log(` ✔ [3/5] DELETE ${USERS}/${op.id}`);
    }

    // 4) 전 계정 필드 정리.
    await commitBatches(step4, (batch, op) =>
      batch.update(db.collection(USERS).doc(op.id), op.patch)
    );
    if (step4.length > 0) console.log(` ✔ [4/5] 필드 정리 ${step4.length}건`);

    // 5) orphan 삭제 — Storage 먼저(순서대로 순차 실행), 그다음 Firestore 문서.
    for (const op of step5) {
      if (op.kind === "delete-storage") {
        await getStorage().bucket(bucketName).deleteFiles({ prefix: op.prefix, force: true });
        console.log(` ✔ [5/5] storage:${op.prefix} 삭제`);
      } else if (op.kind === "delete-frame") {
        await db.collection(FRAMES).doc(op.id).delete();
      } else if (op.kind === "delete-user") {
        await db.collection(USERS).doc(op.id).delete();
        console.log(` ✔ [5/5] DELETE ${USERS}/${op.id}`);
      }
    }
  } catch (err) {
    console.error("\n ✖ 실행 중 실패 — 부분 적용 상태입니다. 같은 명령을 다시 실행하세요(멱등).");
    console.error(String(err?.stack ?? err));
    return 2;
  }

  console.log(`\n${LINE}`);
  console.log(" ✔ APPLIED — 재실행 dry-run으로 계획 0건을 확인하세요(멱등 검증).");
  console.log(LINE);
  return 0;
}

/** 계획 항목을 BATCH_SIZE 단위 WriteBatch로 나눠 순차 커밋한다(Firestore 상한 500). */
async function commitBatches(ops, apply) {
  const groups = chunk(ops, BATCH_SIZE);
  for (let i = 0; i < groups.length; i++) {
    const batch = db.batch();
    for (const op of groups[i]) apply(batch, op);
    await batch.commit();
    if (groups.length > 1) {
      console.log(`    · 배치 ${i + 1}/${groups.length} 커밋(${groups[i].length}건)`);
    }
  }
}

process.exit(await run());
