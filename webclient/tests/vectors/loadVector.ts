import { readFileSync, readdirSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

/**
 * 공유 테스트 벡터 로더 — `docs/spec-vectors/*.json` (10 §3)
 *
 * **Windows 테스트(`tests/MCPhoto.Tests/SpecVectorTests.cs`)가 같은 파일을 읽는다.**
 * 벡터 값을 하나 바꾸면 양쪽이 동시에 실패해야 한다 — 그것이 이 장치의 목적이다.
 */

export const VECTOR_DIR = join(
  dirname(fileURLToPath(import.meta.url)),
  "..",
  "..",
  "..",
  "docs",
  "spec-vectors",
);

export interface VectorFile<TCase = unknown> {
  readonly name: string;
  readonly spec: string;
  readonly note?: string;
  readonly cases: readonly TCase[];
}

export function loadVector<TCase = unknown>(name: string): VectorFile<TCase> {
  const raw = readFileSync(join(VECTOR_DIR, `${name}.json`), "utf8");
  return JSON.parse(raw) as VectorFile<TCase>;
}

export function loadCases<TCase>(name: string): readonly TCase[] {
  return loadVector<TCase>(name).cases;
}

export function vectorFileNames(): string[] {
  return readdirSync(VECTOR_DIR)
    .filter((f) => f.endsWith(".json"))
    .map((f) => f.replace(/\.json$/, ""))
    .sort();
}

export const EXPECTED_VECTOR_NAMES = [
  "auto-arrange",
  "center-crop",
  "clamp-slot",
  "copy-name",
  "cut-count",
  "editor-transform",
  "overlap",
  "qr-normalize",
  "role-matrix",
  "scale-slots",
  "session-id",
  "settings-clamp",
  "slots-file",
  "timelapse-speed",
] as const;
