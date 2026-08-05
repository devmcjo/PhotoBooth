/**
 * OAuth 클라이언트 **구성 상태** 판정(순수) — 진단 화면 신호용(2026-08-01 사고 후속).
 *
 * 왜 필요한가: `GOOGLE_OAUTH_CLIENT_ID_WEB`에 치환되지 않은 플레이스홀더가 배포돼
 * 웹 로그인이 100% `invalid_client`로 실패했는데, **운영자가 그 사실을 알아챌 화면 신호가
 * 하나도 없었다.** 게이트 키는 이미 "설정됨/미설정"을 보여 주므로, OAuth 구성도 같은 수준의
 * 신호를 준다.
 *
 * ⚠️ **client_id 값이나 그 일부(앞 n자·해시·길이)를 절대 반환하지 않는다.** 반환은 열거값과
 *    허용목록 "개수"뿐이다. 값이 필요한 진단은 서버 로그가 담당한다.
 * ⚠️ 파일 I/O·`process.env` 접근을 여기에 두지 않는다 — 판정만 순수 함수로 두고 라우트가 주입한다
 *    (`domain/envPlaceholder.ts`와 같은 패턴).
 */

/** 한 종류(desktop/web) OAuth 클라이언트의 구성 상태. */
export type OAuthClientConfigState =
  /** 값이 있고 Google client_id 형식이다. */
  | "ok"
  /** 값은 있으나 형식이 아니다 — **치환되지 않은 플레이스홀더가 여기 걸린다.** */
  | "malformed"
  /** 구성되지 않았다(해당 종류의 로그인은 501). */
  | "unset";

export interface OAuthConfigStatus {
  readonly web: OAuthClientConfigState;
  readonly desktop: OAuthClientConfigState;
  /**
   * web·desktop이 **같은 값**이다. OAuth 클라이언트는 유형이 다르면 공유할 수 없으므로
   * (웹은 `https://` 리디렉트, desktop은 loopback) 이 값이 true면 오구성이다.
   */
  readonly sharedClientId: boolean;
  /** `OAUTH_REDIRECT_ALLOWLIST` 항목 수. **주소 자체는 싣지 않는다.** */
  readonly redirectAllowlistCount: number;
}

/** Google이 발급하는 client_id의 고정 접미사. 비밀이 아니라 형식이다. */
const CLIENT_ID_SUFFIX = ".apps.googleusercontent.com";

/**
 * client_id 한 개의 형식 판정.
 *
 * 접미사만으로 판정하는 이유: 플레이스홀더(`<A1의 웹 client_id>`)·복사 실수·빈 문자열이 전부
 * 여기 걸리고, 그 이상으로 조이면(숫자 자릿수 등) Google이 형식을 바꿨을 때 멀쩡한 배포를
 * "오구성"으로 오판한다.
 */
export function classifyClientId(clientId: string | undefined | null): OAuthClientConfigState {
  const value = (clientId ?? "").trim();
  if (value.length === 0) return "unset";
  if (!value.endsWith(CLIENT_ID_SUFFIX)) return "malformed";
  // 접미사뿐이고 앞의 식별자가 없는 값도 형식 오류다.
  if (value.length <= CLIENT_ID_SUFFIX.length) return "malformed";
  return "ok";
}

/** `describeOAuthConfig`가 필요로 하는 구성의 최소 표면(AppConfig의 부분집합). */
export interface OAuthConfigSource {
  readonly googleOAuthClients?: {
    readonly desktop?: { readonly clientId: string } | undefined;
    readonly web?: { readonly clientId: string } | undefined;
  };
  readonly oauthRedirectAllowlist?: readonly string[];
}

/**
 * 구성 → 진단 신호. `AppConfig`를 통째로 받지 않고 최소 표면만 받는다(테스트에서 조립하기 쉽다).
 *
 * ⚠️ 여기서 `loadConfig()`를 부르지 않는다 — 순수하게 유지해야 라우트 밖에서 단위 검증된다.
 */
export function describeOAuthConfig(cfg: OAuthConfigSource): OAuthConfigStatus {
  const desktopId = cfg.googleOAuthClients?.desktop?.clientId ?? "";
  const webId = cfg.googleOAuthClients?.web?.clientId ?? "";
  const desktop = classifyClientId(desktopId);
  const web = classifyClientId(webId);
  return {
    web,
    desktop,
    // 둘 다 값이 있을 때만 "공유"가 의미를 갖는다(둘 다 미설정이면 false).
    sharedClientId: desktopId.length > 0 && desktopId === webId,
    redirectAllowlistCount: cfg.oauthRedirectAllowlist?.length ?? 0,
  };
}
