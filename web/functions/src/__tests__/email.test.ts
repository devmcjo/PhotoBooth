import { AppConfig } from "../config";
import {
  getEmailSender,
  LogEmailSender,
  resetEmailSenderCache,
  SendGridEmailSender,
} from "../services/email";

/** 테스트용 최소 config(공급자만 바꿔가며). */
function makeConfig(overrides: Partial<AppConfig>): AppConfig {
  return {
    jwtSecret: "s",
    jwtExpiresInSeconds: 3600,
    clientApiKeys: ["k"],
    storageBucket: "b",
    hostingBaseUrl: "https://example.test",
    emailProvider: "log",
    emailFrom: "",
    sendgridApiKey: "",
    ...overrides,
  };
}

describe("email — 발송 추상화(순수 로직·팩토리)", () => {
  beforeEach(() => {
    resetEmailSenderCache();
  });

  test("LogEmailSender: 발송이 예외 없이 완료되고 콘솔에 코드/링크 로그", async () => {
    const spy = jest.spyOn(console, "info").mockImplementation(() => undefined);
    const sender = new LogEmailSender();

    await expect(
      sender.sendVerification("user@example.com", {
        link: "https://example.test/verify?token=abc.def",
        code: "012345",
        accountId: "devmcjo",
      })
    ).resolves.toBeUndefined();

    await expect(
      sender.sendPasswordReset("user@example.com", {
        link: "https://example.test/reset?token=abc.def",
        code: "654321",
        accountId: "devmcjo",
      })
    ).resolves.toBeUndefined();

    // 로그에 코드·수신자가 실려야 개발자가 Emulator에서 확인 가능.
    const logged = spy.mock.calls.map((c) => c.join(" ")).join("\n");
    expect(logged).toContain("012345");
    expect(logged).toContain("654321");
    expect(logged).toContain("user@example.com");
    spy.mockRestore();
  });

  test("getEmailSender: provider=log → LogEmailSender", () => {
    const sender = getEmailSender(makeConfig({ emailProvider: "log" }));
    expect(sender).toBeInstanceOf(LogEmailSender);
  });

  test("getEmailSender: provider=sendgrid → SendGridEmailSender", () => {
    const sender = getEmailSender(
      makeConfig({
        emailProvider: "sendgrid",
        emailFrom: "no-reply@example.test",
        sendgridApiKey: "SG.dummy",
      })
    );
    expect(sender).toBeInstanceOf(SendGridEmailSender);
  });

  test("getEmailSender: 동일 provider는 캐시(같은 인스턴스), provider 변경 시 재생성", () => {
    const a = getEmailSender(makeConfig({ emailProvider: "log" }));
    const b = getEmailSender(makeConfig({ emailProvider: "log" }));
    expect(a).toBe(b);

    const c = getEmailSender(
      makeConfig({ emailProvider: "sendgrid", emailFrom: "x@y.z", sendgridApiKey: "SG.x" })
    );
    expect(c).not.toBe(a);
  });
});
