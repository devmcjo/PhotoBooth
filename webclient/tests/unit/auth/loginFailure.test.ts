import { describe, expect, it } from "vitest";
import {
  abortReasonToLoginFailure,
  describeLoginFailure,
  loginFailureMessageKey,
  type LoginFailureReason,
} from "@domain/auth/loginFailure";
import type { OauthAbortReason } from "@domain/auth/oauthCallbackPolicy";
import { STRINGS } from "@ui/strings";

/**
 * 실패 사유 → 문구 키 (07 §2.6 · 03 §3.1)
 *
 * 진단용 사유(6종)와 손님에게 보이는 문구(5종)의 축이 다르다 — 그 접힘을 여기서 고정한다.
 */

const ALL_REASONS: readonly LoginFailureReason[] = [
  "cancelled",
  "rejected",
  "notConfigured",
  "redirectRejected",
  "network",
  "clientNotConfigured",
];

const ALL_ABORT_REASONS: readonly OauthAbortReason[] = [
  "no-pending",
  "state-mismatch",
  "provider-error",
  "timeout",
  "no-code",
];

describe("loginFailureMessageKey", () => {
  it("redirectRejected는 network 문구로 접힌다(손님에게 400을 설명하지 않는다)", () => {
    expect(loginFailureMessageKey("redirectRejected")).toBe("network");
  });

  it("그 외는 같은 이름이다", () => {
    for (const reason of ALL_REASONS) {
      if (reason === "redirectRejected") continue;
      expect(loginFailureMessageKey(reason), reason).toBe(reason);
    }
  });

  it("모든 사유가 실제 문구 카탈로그에 매핑된다(빈 문구가 나오지 않는다)", () => {
    for (const reason of ALL_REASONS) {
      const message = STRINGS.login.errors[loginFailureMessageKey(reason)];
      expect(typeof message, reason).toBe("string");
      expect(message.length, reason).toBeGreaterThan(0);
    }
  });
});

describe("abortReasonToLoginFailure", () => {
  it("abort 5종이 전부 cancelled다(07 §2.6)", () => {
    for (const reason of ALL_ABORT_REASONS) {
      expect(abortReasonToLoginFailure(reason), reason).toBe("cancelled");
    }
  });

  it("cancelled의 문구가 규격 문구다", () => {
    expect(STRINGS.login.errors[loginFailureMessageKey(abortReasonToLoginFailure("timeout"))]).toBe(
      "Google 로그인이 취소되었습니다.",
    );
  });
});

describe("describeLoginFailure — 진단 표시 문구(07 §2.5)", () => {
  it("사유 6종 전부가 비어 있지 않은 한국어 문구를 갖는다", () => {
    for (const reason of ALL_REASONS) {
      const label = describeLoginFailure(reason);
      expect(typeof label, reason).toBe("string");
      expect(label.length, reason).toBeGreaterThan(0);
    }
  });

  it("손님 문구와 달리 redirectRejected와 network가 갈라진다 — 운영자가 원인을 찾아야 한다", () => {
    expect(describeLoginFailure("redirectRejected")).not.toBe(describeLoginFailure("network"));
    // 손님 문구는 여전히 같은 것으로 접힌다(축이 둘이라는 사실 자체를 고정한다).
    expect(loginFailureMessageKey("redirectRejected")).toBe(loginFailureMessageKey("network"));
  });

  it("서버 구성 오류(notConfigured)가 계정 거부(rejected)와 다르게 표시된다", () => {
    // 2026-08-01: 서버 구성 오류가 401 계정 문구로 표시돼 운영자가 원인을 찾지 못했다.
    expect(describeLoginFailure("notConfigured")).not.toBe(describeLoginFailure("rejected"));
  });
});
