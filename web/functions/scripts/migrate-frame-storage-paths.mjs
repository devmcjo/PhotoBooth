/**
 * migrate-frame-storage-paths.mjs — 프레임 Storage 경로·문서 점검 및 이관
 * (설계 `docs/design/wpf-frame-ownership-binding-design.md` D-14).
 *
 * ## 왜 필요한가
 *
 * 개인 프레임 Storage 경로가 `frames/{userId}/` → **`frames/users/{userId}/`** 로 바뀌었다.
 * 계정 id는 형식 검증만 거치므로 `default@…` 로 가입하면 id가 `default`가 되어 개인 프레임이
 * **공용 경로에 섞이고**, 최악의 경우 계정 삭제 cascade가 공용 프레임을 지운다. 경로 분리가 그 차단책이다.
 *
 * 공용(기본) 프레임 경로 `frames/default/{id}.png` 는 **바뀌지 않았다** — 이 스크립트는 공용 문서에 대해
 * "이관"을 하지 않고 **점검만** 한다(이미지 존재 여부·이름 규약).
 *
 * ## 하는 일
 *
 *   1) `frameTemplates` 전 문서를 훑어 기대 경로와 실제 파일 위치를 대조한다.
 *   2) 개인 문서(`userId != null`)가 구 경로에 있으면 **복사 → imageUrl 갱신 → 구 파일 삭제** 순으로 옮긴다.
 *      (복사가 먼저다. 삭제 먼저 하면 실패 시 이미지가 사라진다.)
 *   3) 이미지가 어디에도 없는 **고아 문서**와 이름 규약 위반(`_` 포함)을 보고한다(삭제하지 않는다).
 *
 * ⚠️ **기본 모드는 dry-run이다.** `--apply` 없이는 어떤 쓰기도 하지 않는다.
 * ⚠️ imageUrl의 다운로드 토큰은 파일을 복사해도 **새로 발급되지 않는다** — 복사본에 같은 토큰 메타를
 *    실어 옮기므로 기존 URL이 그대로 동작한다. 토큰 메타가 없는 파일은 URL이 깨지므로 경고를 남긴다.
 *
 * 실행 위치: `web/functions/`
 *   node scripts/migrate-frame-storage-paths.mjs --project mcphoto-955fb            # 점검(dry-run)
 *   node scripts/migrate-frame-storage-paths.mjs --project mcphoto-955fb --apply    # 이관 실행
 *
 * 인증: ADC — `gcloud auth application-default login` 또는 GOOGLE_APPLICATION_CREDENTIALS.
 * 종료 코드: 0=성공(또는 점검 완료), 1=인자 오류, 2=실행 중 실패(부분 적용 — 재실행하면 남은 것만 처리).
 */
import { initializeApp } from "firebase-admin/app";
import { getFirestore } from "firebase-admin/firestore";
import { getStorage } from "firebase-admin/storage";

const COLLECTION = "frameTemplates";
const DOWNLOAD_TOKEN_META = "firebaseStorageDownloadTokens";

// ── 인자 ──
const args = process.argv.slice(2);
const apply = args.includes("--apply");
const projectId = readOption("--project") ?? process.env.GCLOUD_PROJECT ?? process.env.GOOGLE_CLOUD_PROJECT;
const bucketName = readOption("--bucket") ?? (projectId ? `${projectId}.firebasestorage.app` : undefined);

function readOption(name) {
  const i = args.indexOf(name);
  return i >= 0 && i + 1 < args.length ? args[i + 1] : undefined;
}

if (!projectId) {
  console.error("--project <projectId> 가 필요합니다(또는 GCLOUD_PROJECT 환경변수).");
  process.exit(1);
}

// 경로 규칙은 서버와 **같은 모듈**을 쓴다(복제 금지 — 어긋나면 멀쩡한 파일을 옮기거나 고아로 오판한다).
// 컴파일 산출물을 참조하므로 실행 전 `npm run build` 가 필요하다.
let paths;
try {
  paths = await import("../lib/domain/framePaths.js");
} catch (err) {
  console.error(
    "lib/domain/framePaths.js 를 찾을 수 없습니다. web/functions 에서 `npm run build` 를 먼저 실행하세요."
  );
  console.error(String(err?.message ?? err));
  process.exit(1);
}
const { framePath: expectedPath, legacyFramePath: legacyPath } = paths;

initializeApp({ projectId, storageBucket: bucketName });
const db = getFirestore();
const bucket = getStorage().bucket(bucketName);

console.log(`project=${projectId} bucket=${bucketName} mode=${apply ? "APPLY" : "DRY-RUN"}`);
console.log("─".repeat(72));

const snap = await db.collection(COLLECTION).get();
if (snap.empty) {
  console.log("frameTemplates 문서가 없습니다.");
  process.exit(0);
}

const report = {
  total: snap.size,
  publicOk: 0,
  personalOk: 0,
  moved: 0,
  needsMove: 0,
  orphan: [],
  underscoreName: [],
  missingToken: [],
  failed: [],
};

for (const doc of snap.docs) {
  const data = doc.data();
  const frameId = data.id ?? doc.id;
  const userId = data.userId ?? null;
  const isPersonal = userId !== null;

  if (typeof data.name === "string" && data.name.includes("_")) {
    report.underscoreName.push(`${frameId} (${data.name})`);
  }

  const want = expectedPath(userId, frameId);
  const old = legacyPath(userId, frameId);

  const [wantExists] = await bucket.file(want).exists();
  if (wantExists) {
    if (isPersonal) report.personalOk++;
    else report.publicOk++;
    continue;
  }

  // 기대 위치에 없다 → 구 경로 확인(공용은 구·신이 같으므로 여기 오면 고아다).
  const [oldExists] = old === want ? [false] : await bucket.file(old).exists();
  if (!oldExists) {
    report.orphan.push(`${frameId} (${data.name ?? "?"}) — 기대 경로: ${want}`);
    continue;
  }

  report.needsMove++;
  console.log(`이관 필요: ${frameId} (${data.name ?? "?"})`);
  console.log(`   ${old}`);
  console.log(` → ${want}`);

  if (!apply) continue;

  try {
    // 다운로드 토큰을 보존해야 기존 imageUrl이 계속 동작한다.
    const [meta] = await bucket.file(old).getMetadata();
    const token = meta?.metadata?.[DOWNLOAD_TOKEN_META];
    if (!token) report.missingToken.push(frameId);

    // ① 복사(먼저) → ② 메타 이식 → ③ 원본 삭제. 순서를 바꾸면 실패 시 이미지가 사라진다.
    await bucket.file(old).copy(bucket.file(want));
    if (token) {
      await bucket.file(want).setMetadata({
        contentType: meta.contentType ?? "image/png",
        metadata: { [DOWNLOAD_TOKEN_META]: token },
      });
    }

    // imageUrl은 경로를 포함하므로 함께 갱신한다(토큰은 그대로).
    if (typeof data.imageUrl === "string" && data.imageUrl.includes(encodeURIComponent(old))) {
      const newUrl = data.imageUrl.replace(encodeURIComponent(old), encodeURIComponent(want));
      await doc.ref.update({ imageUrl: newUrl });
    } else {
      console.warn(`   ⚠️ imageUrl에서 구 경로를 찾지 못해 URL을 갱신하지 못했습니다: ${frameId}`);
    }

    await bucket.file(old).delete();
    report.moved++;
    console.log("   ✔ 이관 완료");
  } catch (err) {
    report.failed.push(`${frameId}: ${String(err?.message ?? err)}`);
    console.error(`   ✖ 실패: ${String(err?.message ?? err)}`);
  }
}

// ── 요약 ──
console.log("─".repeat(72));
console.log(`문서 ${report.total}건 — 공용 정상 ${report.publicOk} · 개인 정상 ${report.personalOk}`);
console.log(`이관 ${apply ? `완료 ${report.moved}` : `필요 ${report.needsMove}`}건`);

if (report.orphan.length) {
  console.log(`\n⚠️ 이미지 없는 문서 ${report.orphan.length}건(삭제하지 않았습니다 — 수동 확인 필요):`);
  report.orphan.forEach((x) => console.log(`   - ${x}`));
}
if (report.underscoreName.length) {
  console.log(`\n⚠️ 이름에 '_'가 있는 문서 ${report.underscoreName.length}건:`);
  console.log("   서버 validateFrameName이 '_'를 거부하므로 재저장·수정이 불가합니다(조회·사용은 정상).");
  report.underscoreName.forEach((x) => console.log(`   - ${x}`));
}
if (report.missingToken.length) {
  console.log(`\n⚠️ 다운로드 토큰 메타가 없던 파일 ${report.missingToken.length}건 — imageUrl이 동작하지 않을 수 있습니다:`);
  report.missingToken.forEach((x) => console.log(`   - ${x}`));
}
if (report.failed.length) {
  console.log(`\n✖ 실패 ${report.failed.length}건:`);
  report.failed.forEach((x) => console.log(`   - ${x}`));
  process.exit(2);
}

if (!apply && report.needsMove > 0) {
  console.log("\n반영하려면 --apply 를 붙여 다시 실행하세요.");
}
process.exit(0);
