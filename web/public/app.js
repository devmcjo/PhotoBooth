// MC포토 모바일 다운로드 페이지 진입 로직.
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
function renderSuccess(data) {
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
    if (photoHint) photoHint.hidden = false;
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
    photoDownload.setAttribute("download", "mcphoto.jpg");
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
    if (videoHint) videoHint.hidden = false;
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
    videoDownload.setAttribute("download", "mcphoto.mp4");
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

  // 다운로드 폴백 안내(#photo-hint / #video-hint)는 URL 이 있는 미디어에서만 노출한다.
  // <a download> 는 cross-origin(firebasestorage.googleapis.com)에서 전 브라우저가 무시하므로
  // (MDN: same-origin + blob:/data: 전용), iOS 한정이 아니라 공통 안내다(리뷰 Minor 1, 2026-07-20).

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

    renderSuccess(data);
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

  loadSession(token);
}

// module script 는 defer 되므로 DOM 은 준비된 상태지만, 방어적으로 확인한다.
if (document.readyState === "loading") {
  document.addEventListener("DOMContentLoaded", init);
} else {
  init();
}
