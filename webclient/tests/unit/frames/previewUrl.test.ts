import { describe, expect, it } from "vitest";
import { createPreviewUrlHolder } from "@screens/frameEditor/previewUrl";

/**
 * 미리보기 object URL 누수 0 — 설계 §9.4
 *
 * `frameImageCache`(저장된 프레임 URL의 소유자)와 **다른 소유권**이다. 편집기 미리보기는 세션
 * 자원이라 편집기가 직접 해제해야 한다. 여기서 고정하는 것은 **"만든 수 == 해제한 수"** 다.
 */

function spyHolder(): {
  holder: ReturnType<typeof createPreviewUrlHolder>;
  created: string[];
  revoked: string[];
} {
  const created: string[] = [];
  const revoked: string[] = [];
  let seq = 0;
  const holder = createPreviewUrlHolder({
    createObjectURL: () => {
      const url = `blob:preview-${++seq}`;
      created.push(url);
      return url;
    },
    revokeObjectURL: (url) => revoked.push(url),
  });
  return { holder, created, revoked };
}

describe("createPreviewUrlHolder", () => {
  it("set 3회 + dispose → 만든 3개가 모두 해제된다", () => {
    const { holder, created, revoked } = spyHolder();
    holder.set(new Blob(["a"]));
    holder.set(new Blob(["b"]));
    holder.set(new Blob(["c"]));
    holder.dispose();

    expect(created).toHaveLength(3);
    expect(revoked).toEqual(created);
  });

  it("새 URL을 만들기 **전에** 이전 URL을 해제한다", () => {
    const { holder, created, revoked } = spyHolder();
    const first = holder.set(new Blob(["a"]));
    expect(revoked).toEqual([]);
    const second = holder.set(new Blob(["b"]));
    expect(revoked).toEqual([first]);
    expect(second).not.toBe(first);
    expect(holder.current()).toBe(second);
    expect(created).toEqual([first, second]);
  });

  it("set(null)은 이전 URL을 해제하고 빈 문자열을 돌려준다", () => {
    const { holder, revoked } = spyHolder();
    const first = holder.set(new Blob(["a"]));
    expect(holder.set(null)).toBe("");
    expect(holder.current()).toBe("");
    expect(revoked).toEqual([first]);
  });

  it("dispose는 멱등이고, 해제 후 다시 set할 수 있다", () => {
    const { holder, created, revoked } = spyHolder();
    holder.set(new Blob(["a"]));
    holder.dispose();
    holder.dispose();
    holder.dispose();
    expect(revoked).toHaveLength(1);

    // <StrictMode> 이중 effect: 2회차 마운트가 새 URL을 만들어도 누수가 없다.
    holder.set(new Blob(["b"]));
    holder.dispose();
    expect(created).toHaveLength(2);
    expect(revoked).toEqual(created);
  });

  it("아무것도 만들지 않은 홀더의 dispose는 아무 일도 하지 않는다", () => {
    const { holder, created, revoked } = spyHolder();
    holder.dispose();
    expect(created).toEqual([]);
    expect(revoked).toEqual([]);
    expect(holder.current()).toBe("");
  });
});
