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

// ---- 성공 렌더(Step 5) --------------------------------------------------
function renderSuccess(data) {
  const photoSection = document.getElementById("photo-section");
  const photoPreview = document.getElementById("photo-preview");
  const photoDownload = document.getElementById("photo-download");
  const photoError = document.getElementById("photo-error");
  const videoSection = document.getElementById("video-section");
  const videoPreview = document.getElementById("video-preview");
  const videoDownload = document.getElementById("video-download");
  const videoError = document.getElementById("video-error");
  const expiryNotice = document.getElementById("expiry-notice");

  // 개별 미디어 로드 성공/실패 추적 — 둘 다 실패하면 만료 화면으로 폴백(§3.4, VF-10).
  const mediaState = { photoOk: null, videoPresent: false, videoOk: null };

  function maybeFallbackToExpired() {
    const photoFailed = mediaState.photoOk === false;
    const videoFailed = !mediaState.videoPresent || mediaState.videoOk === false;
    if (photoFailed && videoFailed) {
      showState("expired");
    }
  }

  // 사진: 문서 URL 을 img/a 에 직접 바인딩(파일명은 표시용 힌트, 실제는 서버 헤더 따름).
  if (data.finalImageUrl) {
    photoPreview.onload = () => {
      mediaState.photoOk = true;
      if (photoError) photoError.hidden = true;
    };
    photoPreview.onerror = () => {
      mediaState.photoOk = false;
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
    // finalImageUrl 이 없으면(계약상 필수지만 방어적으로) 사진 영역을 실패로 표시.
    // 폴백 평가는 영상 블록까지 처리한 뒤 아래에서 한 번 수행한다(순서 안전).
    mediaState.photoOk = false;
    if (photoPreview) photoPreview.hidden = true;
    if (photoError) photoError.hidden = false;
    photoDownload.setAttribute("aria-disabled", "true");
    photoDownload.classList.add("is-disabled");
    photoDownload.removeAttribute("href");
  }

  // 영상: timelapseUrl 이 truthy 일 때만 표시, null 이면 영역 전체 숨김(계약 §2.3).
  if (data.timelapseUrl) {
    mediaState.videoPresent = true;
    videoPreview.onloadeddata = () => {
      mediaState.videoOk = true;
      if (videoError) videoError.hidden = true;
    };
    videoPreview.onerror = () => {
      mediaState.videoOk = false;
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
    mediaState.videoPresent = false;
    if (videoSection) videoSection.hidden = true; // 빈 영상 플레이어를 노출하지 않는다.
  }

  // 만료 고지: 사용자 로컬 시간으로 포맷.
  if (expiryNotice && data.expiresAt && typeof data.expiresAt.toDate === "function") {
    expiryNotice.textContent = `이 사진·영상은 ${formatExpiry(data.expiresAt.toDate())}에 만료됩니다.`;
  }

  // 다운로드 폴백 안내(#photo-hint / #video-hint)는 상시 노출한다.
  // <a download> 는 cross-origin(firebasestorage.googleapis.com)에서 전 브라우저가 무시하므로
  // (MDN: same-origin + blob:/data: 전용), iOS 한정이 아니라 공통 안내다(리뷰 Minor 1, 2026-07-20).

  showState("success");

  // 동기 확정분(URL 부재) 반영 후 폴백 평가: 사진 URL 부재 + 영상 없음이면 만료로 폴백(리뷰 Minor 3).
  // 비동기 로드 실패는 각 onerror 콜백에서 별도로 평가된다.
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
