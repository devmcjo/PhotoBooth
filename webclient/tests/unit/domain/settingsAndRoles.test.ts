import { describe, expect, it } from "vitest";
import {
  applyConnectionFallbacks,
  autoCutCountValue,
  closestFrom,
  clampSettings,
  DEFAULT_SETTINGS,
  DEFAULT_WEB_EXTRAS,
  GUEST_LOCKED_KEYS,
  MAX_RETENTION_HOURS,
  MIN_RETENTION_HOURS,
} from "@domain/settings/appSettings";
import { AUTO_CUT_COUNT } from "@domain/settings/cutCountPolicy";
import { isQrEffectivelyEnabled } from "@domain/settings/qrEffectivePolicy";
import { assignableRoles } from "@domain/roles/roleChangePolicy";
import {
  canCreate,
  canManage,
  canResetPin,
  canWriteFrames,
  creatableRoles,
  DEFAULT_ROLE,
  isPower,
  parseRole,
  roleLabel,
  USER_ROLES,
} from "@domain/roles/userRole";

describe("appSettings — 기본값", () => {
  it("규격 기본값을 갖는다(analysis/41 §2.1)", () => {
    expect(DEFAULT_SETTINGS.CutCount).toBe(6);
    expect(DEFAULT_SETTINGS.CountdownSec).toBe(6);
    expect(DEFAULT_SETTINGS.MirrorMode).toBe(true);
    expect(DEFAULT_SETTINGS.FlashMode).toBe(false);
    expect(DEFAULT_SETTINGS.ShutterSound).toBe(false);
    expect(DEFAULT_SETTINGS.RetakeEnabled).toBe(false);
    expect(DEFAULT_SETTINGS.RetentionHours).toBe(24);
    expect(DEFAULT_SETTINGS.EnableQrDelivery).toBe(true);
    expect(DEFAULT_SETTINGS.SaveLocalCopy).toBe(true);
    expect(DEFAULT_SETTINGS.OutputFormat).toBe("Jpg");
  });

  it("게이트 키 필드가 모델에 없다(analysis/41 §2.5)", () => {
    expect("BackendApiKey" in DEFAULT_SETTINGS).toBe(false);
  });

  it("웹 보조값 기본은 전면 카메라다", () => {
    expect(DEFAULT_WEB_EXTRAS.CameraFacing).toBe("user");
  });

  it("게스트 잠금 키가 규격 11개다(analysis/41 §2.3)", () => {
    expect(GUEST_LOCKED_KEYS).toHaveLength(11);
    expect(GUEST_LOCKED_KEYS).toContain("MirrorMode");
    expect(GUEST_LOCKED_KEYS).toContain("EnableQrDelivery");
    expect(GUEST_LOCKED_KEYS).toContain("HostingBaseUrl");
    // 컷 수·카운트다운은 게스트도 편집할 수 있다
    expect(GUEST_LOCKED_KEYS).not.toContain("CutCount");
    expect(GUEST_LOCKED_KEYS).not.toContain("CountdownSec");
  });
});

describe("closestFrom — 동률 규칙", () => {
  it("거리가 같으면 배열의 앞선 값이 이긴다(Windows와 동일)", () => {
    expect(closestFrom(7, [6, 8, 10], 6)).toBe(6); // 6·8 동률 → 앞선 6
    expect(closestFrom(9, [6, 8, 10], 6)).toBe(8); // 8·10 동률 → 앞선 8
    expect(closestFrom(5, [3, 6, 8, 10], 6)).toBe(6); // 동률 아님(3까지 2, 6까지 1)
    expect(closestFrom(4.5, [3, 6], 6)).toBe(3); // 동률 → 앞선 3
  });

  it("허용 목록이 비면 폴백을 돌려준다", () => {
    expect(closestFrom(5, [], 42)).toBe(42);
  });
});

describe("clampSettings — 두 URL 정규화 방향이 반대다", () => {
  it("Hosting은 트레일링 슬래시를 제거하고 Backend는 부여한다", () => {
    const clamped = clampSettings({
      ...DEFAULT_SETTINGS,
      HostingBaseUrl: "https://a.web.app/",
      BackendBaseUrl: "https://api/x",
    });
    expect(clamped.HostingBaseUrl).toBe("https://a.web.app");
    expect(clamped.BackendBaseUrl).toBe("https://api/x/");
  });

  it("빈 BackendBaseUrl은 슬래시를 붙이지 않는다(미구성 상태 보존)", () => {
    expect(clampSettings({ ...DEFAULT_SETTINGS, BackendBaseUrl: "   " }).BackendBaseUrl).toBe("");
  });

  it("보관 시간을 1~72로 제한한다", () => {
    expect(clampSettings({ ...DEFAULT_SETTINGS, RetentionHours: 0 }).RetentionHours).toBe(
      MIN_RETENTION_HOURS,
    );
    expect(clampSettings({ ...DEFAULT_SETTINGS, RetentionHours: 999 }).RetentionHours).toBe(
      MAX_RETENTION_HOURS,
    );
  });

  it("멱등이다 — 두 번 clamp해도 같은 값이다", () => {
    const once = clampSettings({ ...DEFAULT_SETTINGS, CutCount: 7, RetentionHours: 200 });
    expect(clampSettings(once)).toEqual(once);
  });

  it("자동 sentinel은 0이고 헬퍼가 그 값을 돌려준다", () => {
    expect(autoCutCountValue()).toBe(AUTO_CUT_COUNT);
    expect(AUTO_CUT_COUNT).toBe(0);
  });
});

describe("applyConnectionFallbacks — 빈 값 영속 방지(05 §2.2)", () => {
  const defaults = {
    backendBaseUrl: "https://build/api/",
    hostingBaseUrl: "https://build.web.app",
    storageBucket: "build.bucket",
    googleClientId: "build-client-id",
  };

  it("빈 문자열은 빌드 주입값으로 대체된다", () => {
    const result = applyConnectionFallbacks(
      {
        ...DEFAULT_SETTINGS,
        BackendBaseUrl: "",
        HostingBaseUrl: "  ",
        StorageBucket: "",
        GoogleClientId: "",
      },
      defaults,
    );
    expect(result.BackendBaseUrl).toBe(defaults.backendBaseUrl);
    expect(result.HostingBaseUrl).toBe(defaults.hostingBaseUrl);
    expect(result.StorageBucket).toBe(defaults.storageBucket);
    expect(result.GoogleClientId).toBe(defaults.googleClientId);
  });

  it("저장값이 있으면 그 값이 우선한다", () => {
    const result = applyConnectionFallbacks(
      { ...DEFAULT_SETTINGS, GoogleClientId: "stored-id" },
      defaults,
    );
    expect(result.GoogleClientId).toBe("stored-id");
  });

  it("GoogleClientId 빈 값이 영속되면 로그인 버튼이 영구히 사라진다 — 그 회귀를 막는다", () => {
    const stored = { ...DEFAULT_SETTINGS, GoogleClientId: "" };
    expect(applyConnectionFallbacks(stored, defaults).GoogleClientId).not.toBe("");
  });
});

describe("qrEffectivePolicy — 게스트에게는 QR이 없다(VF-11)", () => {
  it("미로그인이면 raw 값과 무관하게 off다", () => {
    expect(isQrEffectivelyEnabled(true, false, false)).toBe(false);
    expect(isQrEffectivelyEnabled(false, false, false)).toBe(false);
  });

  it("TempUser 한도 초과면 off다", () => {
    expect(isQrEffectivelyEnabled(true, true, true)).toBe(false);
  });

  it("그 외에는 raw 값 그대로다(한도 해제 시 즉시 원복)", () => {
    expect(isQrEffectivelyEnabled(true, true, false)).toBe(true);
    expect(isQrEffectivelyEnabled(false, true, false)).toBe(false);
  });
});

describe("userRole", () => {
  it("5역할이 snake_case 문자열이다(서버·Firestore와 동일)", () => {
    expect(USER_ROLES).toEqual(["temp_user", "user", "advanced_user", "manager", "admin"]);
  });

  it("알 수 없는 값은 최소 권한으로 폴백한다", () => {
    expect(parseRole("admin")).toBe("admin");
    expect(parseRole("Admin")).toBe(DEFAULT_ROLE);
    expect(parseRole("superuser")).toBe(DEFAULT_ROLE);
    expect(parseRole(null)).toBe(DEFAULT_ROLE);
    expect(parseRole(undefined)).toBe(DEFAULT_ROLE);
    expect(DEFAULT_ROLE).toBe("user");
  });

  it("한글 라벨을 제공한다", () => {
    expect(USER_ROLES.map(roleLabel)).toEqual([
      "임시 유저",
      "사용자",
      "고급 유저",
      "매니저",
      "관리자",
    ]);
  });

  it("isPower와 canWriteFrames는 별개 축이다 — advanced_user가 그 증거다", () => {
    expect(canWriteFrames("advanced_user")).toBe(true);
    expect(isPower("advanced_user")).toBe(false);
    expect(isPower("manager")).toBe(true);
    expect(canWriteFrames("user")).toBe(false);
  });

  it("canManage는 같거나 낮은 위계, canResetPin은 엄격히 낮은 위계다", () => {
    expect(canManage("manager", "manager")).toBe(true);
    expect(canResetPin("manager", "manager")).toBe(false); // 동급 PIN 재설정 금지
    expect(canResetPin("admin", "manager")).toBe(true);
    expect(canResetPin("admin", "admin")).toBe(false);
    expect(canManage("manager", "admin")).toBe(false);
    expect(canResetPin("advanced_user", "user")).toBe(false); // 비power
  });

  it("생성 가능 역할 규칙을 보존한다", () => {
    expect(creatableRoles("admin")).toEqual(["temp_user", "user", "advanced_user", "manager"]);
    expect(creatableRoles("manager")).toEqual(["temp_user", "user", "advanced_user"]);
    expect(creatableRoles("advanced_user")).toEqual([]);
    expect(canCreate("admin", "manager")).toBe(true);
    expect(canCreate("admin", "admin")).toBe(false); // 최종 1인 규칙
    expect(canCreate("manager", "manager")).toBe(false);
  });
});

describe("roleChangePolicy — 서버 setRole 매트릭스와 1:1", () => {
  it("admin 대상은 누구도 변경할 수 없다", () => {
    for (const actor of USER_ROLES) {
      expect(assignableRoles(actor, "admin"), actor).toEqual([]);
    }
  });

  it("admin은 admin을 제외한 전부를 지정한다", () => {
    expect(assignableRoles("admin", "user")).toEqual([
      "temp_user",
      "user",
      "advanced_user",
      "manager",
    ]);
  });

  it("manager는 하위 대역 안에서만 자유 지정한다", () => {
    expect(assignableRoles("manager", "user")).toEqual(["temp_user", "user", "advanced_user"]);
    expect(assignableRoles("manager", "advanced_user")).toEqual([
      "temp_user",
      "user",
      "advanced_user",
    ]);
    expect(assignableRoles("manager", "manager")).toEqual([]); // 동급 승격·강등 금지
  });

  it("비power는 빈 목록이다(UI 미노출)", () => {
    expect(assignableRoles("advanced_user", "user")).toEqual([]);
    expect(assignableRoles("user", "user")).toEqual([]);
    expect(assignableRoles("temp_user", "user")).toEqual([]);
  });

  it("반환 순서가 위계 오름차순으로 고정이다", () => {
    const roles = assignableRoles("admin", "temp_user");
    expect(roles).toEqual([...roles].sort((a, b) => USER_ROLES.indexOf(a) - USER_ROLES.indexOf(b)));
  });
});
