import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { env, versionCaption } from "./env";
import "./main.css";

/**
 * Step 1 부트스트랩(최소판).
 *
 * 규격 부트스트랩 11단계(01 §4.2)는 Step 3·Step 4에서 이 파일에 순서대로 채운다:
 *   1 env 검증(완료) → 2 로그 스토어 → 3 branding fetch(800ms) → 4 설정 로드+clamp
 *   → 5 storage.persist() → 6 OPFS sessions/ 잔재 삭제 → 7 SW 등록 → 8 전역 예외
 *   → 9 OAuth 콜백 → 10 React 마운트 → 11 첫 제스처(전체화면·오디오·WakeLock)
 *
 * 현재는 배포 경로·CSP·HTTPS 확정을 위한 버전 캡션 화면만 렌더한다(11-wbs Step 1).
 * `envWarnings`는 Step 3에서 로그 스토어 초기화 직후 flush한다.
 */
function BootScreen() {
  return (
    <>
      <main className="boot">
        <h1 className="boot__title">MCPhoto</h1>
        <p className="boot__subtitle">self custom photobooth</p>
      </main>
      <p className="version-caption">{versionCaption(env.appVersion)}</p>
    </>
  );
}

const container = document.getElementById("root");
if (container) {
  createRoot(container).render(
    <StrictMode>
      <BootScreen />
    </StrictMode>,
  );
}
