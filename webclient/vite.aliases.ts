import { fileURLToPath } from "node:url";

/**
 * `tsconfig.json`의 `paths`와 **같은 값을 유지해야 한다**.
 * vite.config.ts와 vitest.config.ts가 공유해 드리프트를 막는다(01 §3).
 */
export const aliases: Record<string, string> = {
  "@domain": fileURLToPath(new URL("./src/domain", import.meta.url)),
  "@adapters": fileURLToPath(new URL("./src/adapters", import.meta.url)),
  "@shell": fileURLToPath(new URL("./src/shell", import.meta.url)),
  "@screens": fileURLToPath(new URL("./src/screens", import.meta.url)),
  "@ui": fileURLToPath(new URL("./src/ui", import.meta.url)),
};
