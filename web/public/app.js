// MCPhoto 모바일 다운로드 페이지 진입 로직.
//
// 불변식(firebase-contract.md §0, web-architecture.md §1.2):
//   1. 웹은 읽기 전용 소비자다. resultSessions 단건 조회(getDoc)만 한다.
//      컬렉션 쿼리·열거 API 는 절대 쓰지 않는다(list deny).
//   2. User / frameTemplates 는 절대 읽지 않는다.
//   3. 파일은 문서의 finalImageUrl / timelapseUrl(다운로드 토큰 URL)을
//      DOM 속성에 직접 바인딩한다. Storage SDK / Auth 는 import 하지 않는다.
//   4. URL 은 쿼리형 /?s={token} (D-1).
//
// gstatic CDN 모듈러 import, 버전 고정 pin(v12.16.0) — latest/무버전 금지(WR6).
import { initializeApp } from "https://www.gstatic.com/firebasejs/12.16.0/firebase-app.js";
import {
  getFirestore,
  connectFirestoreEmulator,
  doc,
  getDoc
} from "https://www.gstatic.com/firebasejs/12.16.0/firebase-firestore.js";
import { firebaseConfig } from "./firebase-config.js";

// 로컬 개발/검증 편의: 호스트가 localhost/127.0.0.1 일 때만 Firestore Emulator 에 연결한다.
// 실제 배포 도메인(*.web.app 등)에서는 절대 트리거되지 않는다(프로덕션 안전).
const IS_LOCAL =
  ["localhost", "127.0.0.1", "0.0.0.0"].includes(location.hostname);

// ---- 상수(it17) ---------------------------------------------------------
const TOAST_VISIBLE_MS = 2600;
const TOAST_FADE_MS = 200; // .toast transition(0.18s)보다 약간 길게

// 파일명: 토큰 = sessionId = {yyyyMMdd}_{HHmmss}_{UUIDv4} (계약 §3.5). 시각 prefix 만 캡처해 쓴다.
const TOKEN_STAMP_RE = /^(\d{8})_(\d{6})_/;
const ALLOWED_EXT = new Set(["jpg", "jpeg", "png", "mp4"]);

// 자동 저장 방어선: 계약을 벗어난 이상 크기를 메모리에 적재하지 않는다.
// 정상 범위(타임랩스 최대 12.5초·이미지 장변 4000px 상한)에서는 발동하지 않는다.
const MAX_AUTO_DOWNLOAD_BYTES = 150 * 1024 * 1024;
// blob: URL 지연 해제 시간. 즉시 revoke 하면 다운로드 시작 전에 blob 이 사라질 수 있다(특히 iOS).
const OBJECT_URL_TTL_MS = 60_000;

// ---- 토스트(it17 §5.4) --------------------------------------------------
// 재사용 일시 알림. #toast 는 index.html 에 미리 존재하는 live region 이다(동적 생성 금지).
// 메시지는 textContent 로만 넣는다 — HTML 문자열 주입 경로를 두지 않는다(§9.1 A3 게이트).
let toastTimer = 0;

function showToast(message, variant) {
  const el = document.getElementById("toast");
  if (!el) return;
  // 재호출 시 항상 먼저 취소한다. 중첩 단계(가시→페이드)의 id 도 같은 변수에 담기므로
  // 이 한 번의 clearTimeout 이 어느 단계의 타이머든 취소한다(타이머 누적·유령 토스트 없음).
  if (toastTimer) {
    clearTimeout(toastTimer);
    toastTimer = 0;
  }
  el.textContent = message;
  el.classList.toggle("toast--warn", variant === "warn");
  el.hidden = false;
  // hidden 해제와 같은 프레임에 클래스를 붙이면 transition 이 생략된다 → 다음 프레임에 적용.
  requestAnimationFrame(() => el.classList.add("is-visible"));
  toastTimer = window.setTimeout(() => {
    el.classList.remove("is-visible");
    toastTimer = window.setTimeout(() => {
      el.hidden = true;
      toastTimer = 0;
    }, TOAST_FADE_MS);
  }, TOAST_VISIBLE_MS);
}

// ---- 링크 복사·공유(it17 §4.2-4.3) --------------------------------------
// 사용자 요구는 "링크 복사 + 토스트"다. Web Share API(공유 시트)는 쓰지 않는다 —
// 대상 앱을 한 번 더 골라야 하고 복사 자체가 보장되지 않는다(§0.3 이연).

/**
 * 복사할 canonical URL. location.href 를 그대로 쓰지 않고 토큰으로 재조립해
 * 유입 시 붙은 추적 파라미터(utm_*·fbclid 등)를 제거한다. 계약 §3.5 의 {hostingBaseUrl}/?s={token} 과 동형.
 * origin 을 문자열 연결하지 않고 URL 을 쓰는 이유: pathname 보존 + 인코딩 위임.
 */
function canonicalShareUrl(token) {
  const u = new URL(location.href);
  u.search = "";
  u.hash = "";
  u.searchParams.set("s", token);
  return u.toString();
}

/**
 * 2차 폴백(레거시). deprecated API 지만 구형·인앱 브라우저에서 유일한 경로다.
 * <textarea> 는 DOM 에 붙어 있어야 select() 가 동작한다 → display:none 금지, 화면 밖으로 밀어낸다.
 */
function legacyCopy(text) {
  const ta = document.createElement("textarea");
  try {
    ta.value = text;
    ta.setAttribute("readonly", ""); // iOS 에서 키보드가 뜨는 것을 막는다(선택은 가능)
    ta.setAttribute("aria-hidden", "true"); // 접근성 트리에서 제외
    ta.style.position = "fixed";
    ta.style.top = "-1000px";
    ta.style.opacity = "0";
    document.body.appendChild(ta);
    ta.select();
    ta.setSelectionRange(0, text.length); // iOS 호환
    return document.execCommand("copy");
  } catch (err) {
    console.warn("[mcphoto] 레거시 복사 실패:", err);
    return false;
  } finally {
    ta.remove();
  }
}

/** 1차(navigator.clipboard) → 2차(legacyCopy) 순서. 둘 다 실패하면 false. */
async function copyToClipboard(text) {
  try {
    if (navigator.clipboard && typeof navigator.clipboard.writeText === "function") {
      await navigator.clipboard.writeText(text);
      return true;
    }
  } catch (err) {
    // 비 secure context·권한 거부·document.hasFocus() 실패 등 → 조용히 끝내지 않고 2차로 내려간다.
    console.warn("[mcphoto] clipboard.writeText 실패 → 레거시 복사 시도:", err);
  }
  return legacyCopy(text);
}

async function handleShareClick(token) {
  try {
    if (await copyToClipboard(canonicalShareUrl(token))) {
      showToast("링크가 복사되었습니다.");
      return;
    }
  } catch (err) {
    // copyToClipboard 는 내부에서 예외를 흡수하지만, URL 조립 등 예기치 못한 경로까지 여기서 막는다
    // (클릭 핸들러의 미처리 rejection 방지).
    console.warn("[mcphoto] 링크 복사 실패:", err);
  }
  // 3차 폴백: 어떤 경로에서도 "아무 일도 일어나지 않음"은 없다(§4.3).
  // canonical URL 은 정상 진입 시 주소창 URL 과 동일하므로 이 안내가 실행 가능한 지시가 된다.
  showToast("링크 복사를 지원하지 않는 브라우저입니다. 주소창의 URL을 복사해 주세요.", "warn");
}

// ---- 파일명(it17 §6.2) --------------------------------------------------
// 최종 이미지 확장자는 outputFormat 에 따라 .jpg 또는 .png 다(계약). 종전의 하드코딩 파일명은
// PNG 세션에도 jpg 확장자를 붙이는 버그였다(VF-6). 확장자는 아래 3단으로 도출한다.
//
// 파일명에 도달하는 값은 ① 정규식으로 캡처한 숫자 8+6자리와 ② 화이트리스트 확장자뿐이다.
// 경로 구분자·제어문자·".." 가 들어갈 경로가 없다. 토큰 원문(UUID 포함)은 넣지 않는다.

/** 1차: 토큰 URL 경로의 실제 확장자(results/{sid}/final.png → 'png'). */
function extFromTokenUrl(url) {
  try {
    const path = new URL(url).pathname; // /v0/b/{bucket}/o/results%2F…%2Ffinal.png
    const marker = path.lastIndexOf("/o/");
    if (marker < 0) return null;
    const decoded = decodeURIComponent(path.slice(marker + 3));
    const dot = decoded.lastIndexOf(".");
    if (dot < 0) return null;
    const ext = decoded.slice(dot + 1).toLowerCase();
    return ALLOWED_EXT.has(ext) ? ext : null; // 화이트리스트 통과분만
  } catch {
    return null;
  }
}

/** 2차: 응답 Content-Type. */
function extFromMime(mime) {
  switch (String(mime || "").split(";")[0].trim().toLowerCase()) {
    case "image/png":
      return "png";
    case "image/jpeg":
      return "jpg";
    case "video/mp4":
      return "mp4";
    default:
      return null;
  }
}

/** 3차: 미디어 종류 기본값. 토큰이 계약 형식과 다르면 시각 prefix 없이 폴백한다. */
function buildFileName(token, url, mime, kind) {
  const ext = extFromTokenUrl(url) || extFromMime(mime) || (kind === "video" ? "mp4" : "jpg");
  const m = TOKEN_STAMP_RE.exec(String(token || ""));
  const stamp = m ? `_${m[1]}_${m[2]}` : "";
  const suffix = kind === "video" ? "_timelapse" : "";
  return `MCPhoto${stamp}${suffix}.${ext}`;
}

// ---- 자동 저장(it17 §3.3) -----------------------------------------------
// <a download> 는 cross-origin 에서 전 브라우저가 무시한다(same-origin·blob:·data: 전용).
// 그래서 종전에는 원본이 열리기만 하고 사용자가 롱프레스로 저장해야 했다.
// 바이트를 fetch 로 가져와 blob: URL 을 만들면 download 가 유효해진다.
//
// 선행 조건은 버킷/서비스의 CORS(GET)다. 따라서 자동 저장을 "능력(capability)"으로 취급한다 —
// 능력이 없다고 판정되면 페이지 전체를 종전 동작으로 되돌리고 다시 시도하지 않는다(§3.3-D).
// 실패는 결정론적·전역적이므로(CORS 미구성이면 첫 클릭부터 전부 실패) 클릭마다 재시도하면
// 사용자 시간과 데이터를 낭비한다.

/** (A) 기능 감지. iOS Safari < 13 등 download 미지원 환경을 여기서 걸러낸다. */
function supportsAutoDownload() {
  return (
    "download" in HTMLAnchorElement.prototype &&
    typeof URL.createObjectURL === "function" &&
    typeof fetch === "function"
  );
}

let autoDownloadEnabled = supportsAutoDownload();

// 미디어별 진행 중 요청. 재진입(이중 클릭) 차단과 pagehide 일괄 취소를 함께 담당한다.
const inflight = new Map();
// blob: URL 지연 해제 예약 타이머. pagehide 에서 clearTimeout 만 한다(§3.8).
const revokeTimers = new Set();

/** busy 표현. 원래 라벨은 dataset.idleLabel 에 보관해 복원한다. */
function setBusy(anchor, busy) {
  if (!anchor) return;
  if (busy) {
    if (anchor.dataset.idleLabel === undefined) {
      anchor.dataset.idleLabel = anchor.textContent.trim();
    }
    anchor.textContent = "저장 중…";
    anchor.setAttribute("aria-busy", "true");
    anchor.classList.add("is-busy");
  } else {
    if (anchor.dataset.idleLabel !== undefined) {
      anchor.textContent = anchor.dataset.idleLabel;
    }
    anchor.removeAttribute("aria-busy");
    anchor.classList.remove("is-busy");
  }
}

/**
 * 수동 힌트("길게 눌러 저장")를 노출한다. present 인 미디어만 대상이다 —
 * present 여부는 renderSuccess 가 이미 다운로드 <a> 의 hidden 으로 표현해 두었다
 * (URL 있음 → hidden=false, 전송 옵션 꺼짐 → hidden=true). 별도 모듈 상태를 만들지 않는다.
 */
function revealManualHints() {
  for (const [anchorId, hintId] of [
    ["photo-download", "photo-hint"],
    ["video-download", "video-hint"]
  ]) {
    const anchor = document.getElementById(anchorId);
    const hint = document.getElementById(hintId);
    if (anchor && hint && !anchor.hidden) hint.hidden = false;
  }
}

/** (D) 전역 degrade. 두 미디어 모두 종전 동작으로 되돌린다. */
function disableAutoDownload() {
  autoDownloadEnabled = false;
  revealManualHints();
}

function triggerBlobDownload(blob, filename) {
  const objectUrl = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = objectUrl;
  a.download = filename;
  a.rel = "noopener";
  a.style.display = "none";
  document.body.appendChild(a);
  a.click();
  a.remove();
  // 지연 해제가 이 objectUrl 의 유일한 해제 경로다(§3.8).
  const timer = window.setTimeout(() => {
    URL.revokeObjectURL(objectUrl);
    revokeTimers.delete(timer);
  }, OBJECT_URL_TTL_MS);
  revokeTimers.add(timer);
}

async function handleDownloadClick(ev, anchor, kind, token) {
  const url = anchor.getAttribute("href");

  // 프리뷰 로드 실패로 href 가 제거된 상태(VF-3): 개입하지 않는다.
  // .is-disabled 의 pointer-events:none 으로 애초에 클릭이 오지 않지만 방어적으로 확인한다.
  if (!url) return;

  // 능력이 없다고 판정된 뒤에는 개입하지 않는다 → 기본 내비게이션 = 종전 동작.
  if (!autoDownloadEnabled) return;

  // 재진입 차단. busy 중 클릭은 preventDefault 만 하고 반환한다.
  if (inflight.has(kind)) {
    ev.preventDefault();
    return;
  }

  // preventDefault 는 첫 await 앞(동기 구간)에서 호출해야 유효하다.
  ev.preventDefault();

  const ac = new AbortController();
  inflight.set(kind, ac);
  setBusy(anchor, true);

  try {
    // 커스텀 요청 헤더를 절대 추가하지 않는다 — GET + 안전 목록 헤더만 쓰면 simple request 가 되어
    // OPTIONS preflight 가 발생하지 않는다. credentials:"omit" 으로
    // Access-Control-Allow-Credentials 요구도 피한다. cache 옵션은 브라우저 기본에 맡긴다.
    const res = await fetch(url, { mode: "cors", credentials: "omit", signal: ac.signal });
    if (!res.ok) throw new Error(`http-${res.status}`);

    // 용량 가드는 .blob() 앞에서 수행해 메모리 적재 자체를 막는다(헤더가 바디보다 먼저 도착한다).
    // Content-Length 부재 시 NaN → Number.isFinite false → 가드는 무동작(안전).
    const len = Number(res.headers.get("content-length"));
    if (Number.isFinite(len) && len > MAX_AUTO_DOWNLOAD_BYTES) throw new Error("too-large");

    const blob = await res.blob();
    triggerBlobDownload(blob, buildFileName(token, url, res.headers.get("content-type"), kind));

    // "저장되었습니다"라고 단정하지 않는다 — 브라우저가 확인 시트를 띄울 수 있다(§3.9).
    // "다운로드 목록" 안내는 플랫폼 중립 표현이다(iOS=Files 앱>다운로드, Android=Download/,
    // 데스크톱=다운로드 폴더). UA 스니핑으로 분기하지 않는다 — 인앱 브라우저에서 오판정한다.
    showToast("저장을 시작했습니다. 다운로드 목록을 확인해 주세요.");
    // 인앱 브라우저 등에서 조용히 저장되지 않는 경우를 대비해 수동 힌트를 노출한다(R2).
    revealManualHints();
  } catch (err) {
    // 페이지 이탈로 인한 취소: 아무 것도 하지 않는다(토스트·폴백 없음).
    if (err && err.name === "AbortError") return;

    console.warn("[mcphoto] 자동 저장 실패 → 종전 동작으로 폴백:", err);
    disableAutoDownload();
    showToast(
      "자동 저장이 지원되지 않는 환경입니다. 원본을 열었으니 길게 눌러 저장해 주세요.",
      "warn"
    );
    // 새 창/팝업을 여는 방식은 쓰지 않는다 — await 이후에는 사용자 활성화가 만료돼 팝업 차단에 걸린다.
    // location.assign 은 차단 대상이 아니며, <a target> 없는 종전 동작(같은 탭 내비게이션)과 동일하다.
    location.assign(url);
  } finally {
    inflight.delete(kind);
    setBusy(anchor, false);
  }
}

// ---- 정리(해제) ---------------------------------------------------------
// 페이지 이탈 시 남은 부수효과를 정리한다(it17 §3.8).
window.addEventListener("pagehide", () => {
  if (toastTimer) {
    clearTimeout(toastTimer);
    toastTimer = 0;
  }

  // 진행 중 fetch 취소. AbortError 는 폴백 경로로 새지 않는다(핸들러에서 무동작 반환).
  for (const ac of inflight.values()) ac.abort();
  inflight.clear();

  // revoke 예약 타이머만 정리하고 revokeObjectURL 은 호출하지 않는다:
  // 문서가 파괴되면 blob store 도 함께 사라지므로 누수가 아니고, 반대로 진행 중인 다운로드를
  // 일부 엔진에서 중단시킬 수 있다.
  for (const t of revokeTimers) clearTimeout(t);
  revokeTimers.clear();
});

// ---- 상태 전이 ----------------------------------------------------------
// index.html 의 5개 상태 섹션 중 하나만 보이게 토글한다(web-architecture.md §2.1).
const STATES = ["loading", "success", "expired", "error"];

function showState(name) {
  for (const s of STATES) {
    const el = document.getElementById(`state-${s}`);
    if (el) el.hidden = s !== name;
  }
}

// ---- 토큰 파싱 ----------------------------------------------------------
// 쿼리형 /?s={token} 에서 토큰 추출(VF-4). 경량 형식 검증만 수행한다
// (엄격 정규식 불요 — Firestore not-found 가 무효 토큰을 걸러줌, §3.2).
function parseToken() {
  const token = new URLSearchParams(location.search).get("s");
  if (!token) return null; // 없음/빈 문자열
  const trimmed = token.trim();
  if (trimmed.length === 0 || trimmed.length > 200) return null; // 명백한 빈값·과도한 길이 차단
  return trimmed;
}

// ---- 만료 시각 포맷 -----------------------------------------------------
function formatExpiry(date) {
  try {
    return new Intl.DateTimeFormat("ko-KR", {
      year: "numeric",
      month: "long",
      day: "numeric",
      hour: "2-digit",
      minute: "2-digit"
    }).format(date);
  } catch {
    return date.toLocaleString();
  }
}

// ---- 성공 렌더(Step 5, it7 F3) ------------------------------------------
// renderSuccess 는 loadSession 이 만료 판정(문서 부재/expiresAt 경과)을 통과한 뒤에만 호출된다(VF-10).
// 따라서 여기서 URL 이 falsy 하면 "만료/실패"가 아니라 "전송 옵션이 꺼진 것"(의도적 제외)으로
// 안전하게 해석한다(it7 F3, 계약 §5: 미만료 문서의 URL null = 전송 옵션 꺼짐).
// token 은 파일명의 시각 prefix 도출에 쓴다(it17 §6.2). 전역 변수를 만들지 않고 인자로 받는다.
function renderSuccess(data, token) {
  const photoPreview = document.getElementById("photo-preview");
  const photoDownload = document.getElementById("photo-download");
  const photoError = document.getElementById("photo-error");
  const photoHint = document.getElementById("photo-hint");
  const photoOptout = document.getElementById("photo-optout");
  const videoSection = document.getElementById("video-section");
  const videoPreview = document.getElementById("video-preview");
  const videoDownload = document.getElementById("video-download");
  const videoError = document.getElementById("video-error");
  const videoHint = document.getElementById("video-hint");
  const videoOptout = document.getElementById("video-optout");
  const expiryNotice = document.getElementById("expiry-notice");

  // 미디어별 상태:
  //   present=false → URL null = 전송 옵션 꺼짐(의도적 제외). 실패 아님 → 만료 폴백에서 제외.
  //   present=true  → URL 있음. loadOk: null(로드 대기)/true(성공)/false(onerror=로드 실패).
  const mediaState = {
    photo: { present: false, loadOk: null },
    video: { present: false, loadOk: null }
  };

  // 만료 폴백은 "URL 이 있는데 로드에 실패한 경우"만 실패로 센다.
  // 옵션 꺼짐(present=false)은 정상 성공의 부분 부재이므로 폴백 트리거에서 제외한다(it7 §4.2).
  function maybeFallbackToExpired() {
    const photoLoadFailed = mediaState.photo.present && mediaState.photo.loadOk === false;
    const videoLoadFailed = mediaState.video.present && mediaState.video.loadOk === false;
    // present 인 미디어가 하나라도 있고, present 인 것이 모두 로드 실패면 만료로 폴백.
    const anyPresent = mediaState.photo.present || mediaState.video.present;
    const allPresentFailed =
      (!mediaState.photo.present || photoLoadFailed) &&
      (!mediaState.video.present || videoLoadFailed);
    if (anyPresent && allPresentFailed) {
      showState("expired");
    }
  }

  // 사진: URL 있으면 프리뷰/다운로드 표시, 없으면 "전송 옵션 꺼짐" 안내.
  if (data.finalImageUrl) {
    mediaState.photo.present = true;
    if (photoOptout) photoOptout.hidden = true;
    photoPreview.hidden = false;
    // 힌트는 "URL 있음"이 아니라 "자동 저장 능력"에만 종속된다(it17 §3.3):
    // 능력이 있으면 숨기고, 기능 감지 실패/폴백 판정 후에는 노출한다.
    if (photoHint) photoHint.hidden = autoDownloadEnabled;
    photoDownload.hidden = false;
    photoPreview.onload = () => {
      mediaState.photo.loadOk = true;
      if (photoError) photoError.hidden = true;
    };
    photoPreview.onerror = () => {
      mediaState.photo.loadOk = false;
      photoPreview.hidden = true;
      if (photoError) photoError.hidden = false;
      photoDownload.setAttribute("aria-disabled", "true");
      photoDownload.classList.add("is-disabled");
      photoDownload.removeAttribute("href");
      maybeFallbackToExpired();
    };
    photoPreview.src = data.finalImageUrl;
    photoDownload.href = data.finalImageUrl;
    photoDownload.setAttribute(
      "download",
      buildFileName(token, data.finalImageUrl, null, "photo")
    );
  } else {
    // 전송 옵션 꺼짐: 프리뷰·다운로드·힌트·실패문구 숨기고 옵션꺼짐 안내만 노출.
    mediaState.photo.present = false;
    if (photoPreview) photoPreview.hidden = true;
    if (photoError) photoError.hidden = true;
    if (photoHint) photoHint.hidden = true;
    if (photoDownload) photoDownload.hidden = true;
    if (photoOptout) photoOptout.hidden = false;
  }

  // 영상: URL 있으면 프리뷰/다운로드 표시, 없으면 영역을 표시하되 "전송 옵션 꺼짐" 안내(it7: 숨기지 않음).
  if (data.timelapseUrl) {
    mediaState.video.present = true;
    if (videoOptout) videoOptout.hidden = true;
    videoPreview.hidden = false;
    // 사진과 동일 규칙(§3.3) — 자동 저장 능력에만 종속.
    if (videoHint) videoHint.hidden = autoDownloadEnabled;
    videoDownload.hidden = false;
    videoPreview.onloadeddata = () => {
      mediaState.video.loadOk = true;
      if (videoError) videoError.hidden = true;
    };
    videoPreview.onerror = () => {
      mediaState.video.loadOk = false;
      videoPreview.hidden = true;
      if (videoError) videoError.hidden = false;
      videoDownload.setAttribute("aria-disabled", "true");
      videoDownload.classList.add("is-disabled");
      videoDownload.removeAttribute("href");
      maybeFallbackToExpired();
    };
    videoPreview.src = data.timelapseUrl;
    videoDownload.href = data.timelapseUrl;
    videoDownload.setAttribute(
      "download",
      buildFileName(token, data.timelapseUrl, null, "video")
    );
    if (videoSection) videoSection.hidden = false;
  } else {
    // 전송 옵션 꺼짐: 영역은 표시하되 프리뷰·다운로드·힌트·실패문구 숨기고 옵션꺼짐 안내 노출.
    mediaState.video.present = false;
    if (videoPreview) videoPreview.hidden = true;
    if (videoError) videoError.hidden = true;
    if (videoHint) videoHint.hidden = true;
    if (videoDownload) videoDownload.hidden = true;
    if (videoOptout) videoOptout.hidden = false;
    if (videoSection) videoSection.hidden = false;
  }

  // 만료 고지: 사용자 로컬 시간으로 포맷.
  if (expiryNotice && data.expiresAt && typeof data.expiresAt.toDate === "function") {
    expiryNotice.textContent = `이 사진·영상은 ${formatExpiry(data.expiresAt.toDate())}에 만료됩니다.`;
  }

  // 다운로드 폴백 안내(#photo-hint / #video-hint)는 URL 이 있는 미디어 중
  // "자동 저장이 불가/실패로 판정된" 경우에만 노출한다(it17 §3.3 — 종전에는 URL 만 있으면 상시 노출).
  // <a download> 는 cross-origin(firebasestorage.googleapis.com)에서 전 브라우저가 무시하므로
  // (MDN: same-origin + blob:/data: 전용), iOS 한정이 아니라 공통 안내다(리뷰 Minor 1, 2026-07-20).
  // it17 이후로는 blob: 경유 저장이 성공하면 이 안내가 필요 없지만, 인앱 브라우저의 조용한 실패를
  // 대비해 첫 저장 시도 후에는 성공/실패 무관하게 노출한다(R2).

  showState("success");

  // 동기 확정분(옵션 꺼짐/로드 대기) 반영 후 폴백 평가. 옵션 꺼짐은 실패가 아니므로 폴백되지 않는다.
  // present 인 미디어가 하나도 없으면(둘 다 옵션 꺼짐 — 계약상 미발생, 방어적) 성공 화면 유지(안내 2개).
  maybeFallbackToExpired();
}

// ---- 로드 & 판정 코어(Step 3) -------------------------------------------
let db = null;

async function loadSession(token) {
  showState("loading");
  try {
    // 단건 조회(getDoc)만 사용한다. 컬렉션 쿼리·열거 미사용(계약 §5.1 list deny).
    const snap = await getDoc(doc(db, "resultSessions", token));

    // 문서 부재 = 삭제됨/무효 토큰 → 만료 안내(VF-5/VF-6).
    // 무효 토큰과 삭제 문서를 구분하지 않는다(토큰 존재 노출 방지, §3.3).
    if (!snap.exists()) {
      showState("expired");
      return;
    }

    const data = snap.data();

    // 만료 판정: expiresAt < now (Firestore Timestamp → Date).
    // fail-safe: expiresAt 부재/파싱 실패 시 만료를 판정할 수 없으므로 성공 대신 만료로 처리한다
    // (만료 기간 초과 콘텐츠를 잘못 노출하지 않기 위함, 리뷰 Minor 2).
    let expiresAt = null;
    if (data.expiresAt && typeof data.expiresAt.toDate === "function") {
      const d = data.expiresAt.toDate();
      if (d instanceof Date && !Number.isNaN(d.getTime())) {
        expiresAt = d;
      }
    }
    if (!expiresAt) {
      console.warn("[mcphoto] expiresAt 부재/파싱 실패 → 만료 처리(fail-safe)");
      showState("expired");
      return;
    }
    if (expiresAt < new Date()) {
      showState("expired");
      return;
    }

    renderSuccess(data, token);
  } catch (err) {
    // 네트워크/권한 예외 → 오류 화면(재시도 제공).
    console.error("[mcphoto] 세션 로드 실패:", err);
    showState("error");
  }
}

// ---- 진입점 -------------------------------------------------------------
function init() {
  // Firebase 초기화(Step 2). 실패 시 오류 화면.
  try {
    const app = initializeApp(firebaseConfig);
    db = getFirestore(app);
    if (IS_LOCAL) {
      // 로컬 Emulator 검증 시에만 연결. 실도메인에서는 실행되지 않는다.
      connectFirestoreEmulator(db, "127.0.0.1", 8080);
      console.info("[mcphoto] Firestore Emulator 연결(로컬 검증)");
    }
    console.info("[mcphoto] Firebase 초기화 완료");
  } catch (err) {
    console.error("[mcphoto] Firebase 초기화 실패:", err);
    showState("error");
    return;
  }

  const token = parseToken();
  if (!token) {
    // 토큰 없음/형식 오류 → 오류 안내.
    showState("error");
    return;
  }

  // 재시도 버튼: 같은 토큰으로 get 재실행(§3.5, 네트워크/권한 예외만 재시도 대상).
  const retryBtn = document.getElementById("retry-btn");
  if (retryBtn) retryBtn.addEventListener("click", () => loadSession(token));

  // 공유 버튼(it17 §4.5): 유효 토큰이 파싱된 경우에만 노출한다.
  // 만료 상태에서도 노출하는 것은 의도된 선택이다(상태 머신과 결합도를 만들지 않는다, R4).
  // 리스너는 여기서 1회만 배선한다 — renderSuccess 는 #retry-btn 으로 재호출될 수 있다.
  const shareBtn = document.getElementById("share-btn");
  if (shareBtn) {
    shareBtn.hidden = false;
    shareBtn.addEventListener("click", () => {
      void handleShareClick(token);
    });
  }

  // 다운로드 버튼(it17 §8.3): 리스너를 여기서 1회만 배선한다.
  // renderSuccess 안에서 배선하면 #retry-btn 재시도 시 누적되어 fetch 가 중복 발생한다(R6).
  for (const [id, kind] of [
    ["photo-download", "photo"],
    ["video-download", "video"]
  ]) {
    const anchor = document.getElementById(id);
    if (!anchor) continue;
    anchor.addEventListener("click", (ev) => {
      void handleDownloadClick(ev, anchor, kind, token);
    });
  }

  // 기능 감지 실패 환경: 처음부터 수동 힌트를 노출한다.
  // (renderSuccess 가 미디어별 present 여부로 다시 정밀 조정한다)
  if (!autoDownloadEnabled) revealManualHints();

  loadSession(token);
}

// module script 는 defer 되므로 DOM 은 준비된 상태지만, 방어적으로 확인한다.
if (document.readyState === "loading") {
  document.addEventListener("DOMContentLoaded", init);
} else {
  init();
}
