/**
 * 배포 스탬프 로더 — scripts/gen-build-stamp.mjs가 빌드 마지막에 쓴 lib/build-stamp.json을 읽는다.
 *
 * 왜 소스가 아니라 JSON 파일인가: 생성 파일을 src/*.ts로 두면 tsc/jest가 그 파일의 존재에 의존하게 되고
 * (스탬프 없이 타입체크·테스트가 깨진다), 리포에 커밋하면 빌드마다 diff가 생긴다. JSON을 런타임에
 * best-effort로 읽으면 스탬프가 없어도(로컬 개발·Emulator·테스트) 조용히 null이 되어 아무것도 깨지지 않는다.
 *
 * 경로는 __dirname 기준이다 — 배포 산출물에서는 lib/deployStamp.js와 lib/build-stamp.json이 같은 폴더다.
 * 테스트(ts-jest)는 src/에서 실행되어 파일이 없으므로 null을 얻는다(의도된 동작).
 */
import fs from "fs";
import path from "path";

/** undefined = 아직 안 읽음, null = 스탬프 없음(정상 폴백). */
let cached: string | null | undefined;

/** 이 배포의 시각(UTC ISO8601). 스탬프 파일이 없거나 손상되면 null. */
export function readDeployedAt(): string | null {
  if (cached !== undefined) return cached;
  cached = loadDeployedAt();
  return cached;
}

/** 테스트용 캐시 리셋(resetConfigCache와 동일 규약). */
export function resetDeployStampCache(): void {
  cached = undefined;
}

function loadDeployedAt(): string | null {
  try {
    const raw = fs.readFileSync(path.join(__dirname, "build-stamp.json"), "utf8");
    const value: unknown = JSON.parse(raw)?.deployedAt;
    return typeof value === "string" && value.length > 0 ? value : null;
  } catch {
    // 파일 부재(로컬·테스트)·JSON 손상 모두 "표기 없음"으로 폴백한다. 헬스 체크를 실패시키지 않는다.
    return null;
  }
}
