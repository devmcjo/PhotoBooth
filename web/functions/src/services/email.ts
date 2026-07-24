/**
 * 이메일 발송 추상화(설계 §10).
 *
 * - `EmailSender` 인터페이스: 공급자 무관 계약(인증 메일 / 재설정 메일).
 * - `LogEmailSender`(dev/Emulator): 실제 발송 없이 `console.info`로 수신자·링크·코드 로그. 외부 의존 0.
 * - `SendGridEmailSender`(prod): `@sendgrid/mail` **지연 import**(dev에 패키지 미설치여도 무방).
 *   자격(API 키·발신자)은 config(env/Secret Manager) — 코드/리포 하드코딩 금지.
 *
 * 발송 실패는 예외로 던지되, **호출측(계정 생성/재설정 request)이 삼켜 로그만**(§5.2·§10.1).
 * request 계열은 발송 실패해도 202(가용성·열거 방지).
 */
import { AppConfig, EmailProvider } from "../config";

/** 인증/재설정 메일에 실리는 값(링크·코드·대상 계정). */
export interface EmailTokenOptions {
  /** 이메일 링크 URL(`{hostingBaseUrl}/verify?token=...` 등). */
  link: string;
  /** 6자리 수기 입력 코드. */
  code: string;
  /** 대상 계정 id(본문 표시용). */
  accountId: string;
}

/** 공급자 무관 이메일 발송 계약. */
export interface EmailSender {
  /** 이메일 인증 메일 발송. */
  sendVerification(to: string, opts: EmailTokenOptions): Promise<void>;
  /** 비밀번호 재설정 메일 발송. */
  sendPasswordReset(to: string, opts: EmailTokenOptions): Promise<void>;
}

/** 메일 본문(텍스트+HTML) 조립 결과. */
interface EmailBody {
  subject: string;
  text: string;
  html: string;
}

/** HTML 이스케이프(본문에 코드·링크·계정 id 삽입 시 XSS/깨짐 방어). */
function escapeHtml(value: string): string {
  return value
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}

/** 인증 메일 본문(한국어, 24시간 유효, 설계 §10.4). */
function buildVerificationBody(opts: EmailTokenOptions): EmailBody {
  const code = escapeHtml(opts.code);
  const link = escapeHtml(opts.link);
  return {
    subject: "MCPhoto 계정 이메일 인증",
    text: [
      "MCPhoto 계정 이메일 인증",
      "",
      "아래 코드를 앱에 입력하거나 링크를 눌러 이메일을 인증하세요.",
      `코드: ${opts.code}`,
      `링크: ${opts.link}`,
      "",
      "이 인증은 24시간 동안 유효합니다.",
      "본인이 요청하지 않았다면 이 메일을 무시하세요.",
    ].join("\n"),
    html: [
      "<h2>MCPhoto 계정 이메일 인증</h2>",
      "<p>아래 코드를 앱에 입력하거나 링크를 눌러 이메일을 인증하세요.</p>",
      `<p>코드: <strong>${code}</strong></p>`,
      `<p><a href="${link}">이메일 인증하기</a></p>`,
      "<p>이 인증은 24시간 동안 유효합니다. 본인이 요청하지 않았다면 이 메일을 무시하세요.</p>",
    ].join(""),
  };
}

/** 재설정 메일 본문(한국어, 1시간 유효, 설계 §10.4). */
function buildPasswordResetBody(opts: EmailTokenOptions): EmailBody {
  const code = escapeHtml(opts.code);
  const link = escapeHtml(opts.link);
  return {
    subject: "MCPhoto 비밀번호 재설정",
    text: [
      "MCPhoto 비밀번호 재설정",
      "",
      "아래 코드를 앱에 입력하거나 링크를 눌러 비밀번호를 재설정하세요.",
      `코드: ${opts.code}`,
      `링크: ${opts.link}`,
      "",
      "이 재설정은 1시간 동안 유효합니다.",
      "본인이 요청하지 않았다면 이 메일을 무시하세요(비밀번호는 변경되지 않습니다).",
    ].join("\n"),
    html: [
      "<h2>MCPhoto 비밀번호 재설정</h2>",
      "<p>아래 코드를 앱에 입력하거나 링크를 눌러 비밀번호를 재설정하세요.</p>",
      `<p>코드: <strong>${code}</strong></p>`,
      `<p><a href="${link}">비밀번호 재설정하기</a></p>`,
      "<p>이 재설정은 1시간 동안 유효합니다. 본인이 요청하지 않았다면 이 메일을 무시하세요(비밀번호는 변경되지 않습니다).</p>",
    ].join(""),
  };
}

/**
 * 개발용 sender — 실제 발송 없이 콘솔에 로그(Emulator/개발 전용, 설계 §10.2).
 * 링크·코드를 그대로 출력하므로 **프로덕션에서 사용 금지**(config가 "log"면 실제 메일 미발송).
 */
export class LogEmailSender implements EmailSender {
  async sendVerification(to: string, opts: EmailTokenOptions): Promise<void> {
    const body = buildVerificationBody(opts);
    console.info(
      `[LogEmailSender] 인증 메일(개발용, 미발송) to=${to} account=${opts.accountId} code=${opts.code} link=${opts.link} subject="${body.subject}"`
    );
  }

  async sendPasswordReset(to: string, opts: EmailTokenOptions): Promise<void> {
    const body = buildPasswordResetBody(opts);
    console.info(
      `[LogEmailSender] 재설정 메일(개발용, 미발송) to=${to} account=${opts.accountId} code=${opts.code} link=${opts.link} subject="${body.subject}"`
    );
  }
}

/** `@sendgrid/mail`의 지연 로드에 필요한 최소 형태(패키지 타입에 의존하지 않기 위한 구조적 타입). */
interface SendGridMailModule {
  setApiKey(key: string): void;
  send(msg: {
    to: string;
    from: string;
    subject: string;
    text: string;
    html: string;
  }): Promise<unknown>;
}

/**
 * 프로덕션 sender — `@sendgrid/mail` 지연 import(설계 §10.3).
 * dev/테스트에서 패키지가 없어도 이 모듈 로드는 실패하지 않는다(send 호출 시점에만 import).
 * 자격은 생성자 주입(config에서). API 키는 로그에 절대 노출하지 않는다.
 */
export class SendGridEmailSender implements EmailSender {
  private readonly apiKey: string;
  private readonly from: string;
  private client: SendGridMailModule | null = null;

  constructor(apiKey: string, from: string) {
    this.apiKey = apiKey;
    this.from = from;
  }

  /** `@sendgrid/mail` 지연 로드(1회) + API 키 설정. */
  private async ensureClient(): Promise<SendGridMailModule> {
    if (this.client) return this.client;
    // 동적 import — 타입/설치 의존을 런타임으로 미룬다(dev 미설치 허용).
    // 모듈명을 변수로 넘겨 tsc의 정적 모듈 해석(설치 강제)을 피한다.
    const moduleName = "@sendgrid/mail";
    const imported = (await import(moduleName)) as {
      default?: SendGridMailModule;
    } & SendGridMailModule;
    const sg: SendGridMailModule = imported.default ?? imported;
    sg.setApiKey(this.apiKey);
    this.client = sg;
    return sg;
  }

  private async send(to: string, body: EmailBody): Promise<void> {
    const sg = await this.ensureClient();
    await sg.send({
      to,
      from: this.from,
      subject: body.subject,
      text: body.text,
      html: body.html,
    });
  }

  async sendVerification(to: string, opts: EmailTokenOptions): Promise<void> {
    await this.send(to, buildVerificationBody(opts));
  }

  async sendPasswordReset(to: string, opts: EmailTokenOptions): Promise<void> {
    await this.send(to, buildPasswordResetBody(opts));
  }
}

let cachedSender: EmailSender | null = null;
let cachedProvider: EmailProvider | null = null;

/**
 * config에 따라 EmailSender를 선택(공급자당 1회 캐시).
 * "log"=LogEmailSender(기본), "sendgrid"=SendGridEmailSender.
 */
export function getEmailSender(cfg: AppConfig): EmailSender {
  if (cachedSender && cachedProvider === cfg.emailProvider) return cachedSender;
  cachedProvider = cfg.emailProvider;
  cachedSender =
    cfg.emailProvider === "sendgrid"
      ? new SendGridEmailSender(cfg.sendgridApiKey, cfg.emailFrom)
      : new LogEmailSender();
  return cachedSender;
}

/** 테스트/재구성용 캐시 리셋. */
export function resetEmailSenderCache(): void {
  cachedSender = null;
  cachedProvider = null;
}
