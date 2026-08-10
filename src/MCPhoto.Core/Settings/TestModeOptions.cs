using MCPhoto.Core.Accounts;
using MCPhoto.Core.Models;

namespace MCPhoto.Core.Settings;

/// <summary>
/// <c>MCPhoto.ini</c> <c>[Test]</c> 섹션 해석 결과 — 로그인 없이 특정 역할로 앱을 실행하는 QA 기능의 입력.
/// <para>
/// <b>순수 함수 정책 클래스</b>다(리포의 <c>QrEffectivePolicy</c>·<c>FrameLoadPolicy</c>·<c>CutCountPolicy</c>와 동형).
/// 로그를 찍지 않고 경고를 <see cref="Warnings"/>에 담아 돌려주는 이유: 그래야 "잘못된 값 → 어떤 경고가 몇 개"까지
/// 단위 테스트로 고정할 수 있다. 그 목록을 <c>TestModeService</c>가 <c>LogWarning</c>으로 흘린다.
/// </para>
/// <para>
/// ⚠️ 이 섹션은 앱이 <b>읽기만</b> 한다(불변식 TM5). 설정 저장이 <c>[Test]</c>를 갱신하면 값이 사용자 손을 떠난다.
/// </para>
/// (설계: docs/design/wpf-it23-session-testmode-license-design.md §B3·§B5)
/// </summary>
/// <param name="Enabled"><c>TestMode</c> 키가 참인가(마스터 스위치).</param>
/// <param name="Id">계정 Id. 상단바 툴팁·아바타 이니셜·진단 계정 요약에 그대로 보인다.</param>
/// <param name="Email">
/// 계정 이메일(소문자 정규화). ⚠️ <b>표시용이 아니다</b> — 개인 프레임 로컬 저장 경로(<c>users/{sha(email)}/</c>)와
/// <c>.slots</c> <c>#owner</c> 서명 값이며, 편집기는 이메일이 없으면 저장을 거부한다.
/// </param>
/// <param name="Role">역할. 앱의 모든 역할 게이트가 <c>CurrentUser.Role</c>만 읽으므로 이 값 하나로 전부 따라온다.</param>
/// <param name="Pin">
/// 진입 PIN(4자리). <c>null</c>이면 PIN 게이트를 <b>생략</b>하고, 값이 있으면 게이트를 띄워 <b>로컬 대조</b>한다.
/// <c>HasPin</c>을 별 키로 두지 않고 이 값의 존재로 파생시켜 "게이트가 서버 설정 분기로 새는" 조합을
/// 표현 불가능하게 만든다(§B3.2).
/// </param>
/// <param name="QrBlocked">TempUser QR 한도 초과 상태 주입 여부(서버 조회가 불가능하므로 유일한 재현 수단).</param>
/// <param name="QrBlockReason">초과 사유(설정 화면 문구가 사유별로 다르다). <paramref name="QrBlocked"/>가 참일 때만 의미.</param>
/// <param name="Warnings">검증 실패로 기본값 폴백한 항목의 사람 말 경고(호출자가 로깅한다).</param>
public sealed record TestModeOptions(
    bool Enabled,
    string Id,
    string Email,
    UserRole Role,
    string? Pin,
    bool QrBlocked,
    QrGateReason QrBlockReason,
    IReadOnlyList<string> Warnings)
{
    /// <summary>INI 섹션 이름. 대소문자는 파서가 무시한다(<c>[test]</c>도 유효).</summary>
    public const string SectionName = "Test";

    // ── 문서화된 기본값(§B3.1). 값이 없거나 검증에 실패하면 이 값으로 폴백한다. ──
    public const string DefaultId = "testuser";
    public const string DefaultEmail = "test@email.com";
    public const UserRole DefaultRole = UserRole.AdvancedUser;
    public const QrGateReason DefaultQrBlockReason = QrGateReason.Count;

    /// <summary>
    /// 역할 허용 문자열(snake_case). <b>명시 열거</b>이며 서수 부등식을 쓰지 않는다 — 리포 규약(it13이 서수를 버린 이유).
    /// ⚠️ <c>IniFile.GetEnum&lt;UserRole&gt;()</c>을 쓰면 C# 이름(<c>TempUser</c>)만 파싱하고 <c>temp_user</c>를 놓친다.
    /// </summary>
    private static readonly string[] AllowedRoles =
        { "temp_user", "user", "advanced_user", "manager", "admin" };

    /// <summary>테스트 모드 꺼짐 + 전 기본값. <c>IsEnabled=false</c>에서도 호출부에 null 분기를 늘리지 않기 위한 객체.</summary>
    public static TestModeOptions Disabled { get; } = new(
        Enabled: false, Id: DefaultId, Email: DefaultEmail, Role: DefaultRole,
        Pin: null, QrBlocked: false, QrBlockReason: DefaultQrBlockReason,
        Warnings: Array.Empty<string>());

    /// <summary>
    /// <c>[Test]</c> 섹션 해석(순수 — 파일·로그·시간에 의존하지 않는다).
    /// <para>
    /// <c>TestMode</c>가 참이 아니면 나머지 키를 해석하지 않고 <see cref="Disabled"/>를 돌려준다 —
    /// 그 결과가 어디에도 쓰이지 않으므로 경고만 늘어난다. "분명히 썼는데 안 된다"는
    /// <c>TestModeService</c>의 Information 로그가 답한다.
    /// </para>
    /// </summary>
    public static TestModeOptions FromIni(IniFile ini)
    {
        if (ini is null || !ini.GetBool(SectionName, "TestMode", false)) return Disabled;

        var warnings = new List<string>();

        // Id: 화면에 보이는 값. 공백이면 기본값(경고 불요 — 의도를 오해할 여지가 없다).
        var id = ini.GetString(SectionName, "Id", DefaultId).Trim();
        if (id.Length == 0) id = DefaultId;

        // Email: 기능값이므로 형식을 확인한다(@ 없으면 프레임 소유 판정이 조용히 어긋난다).
        var rawEmail = ini.GetString(SectionName, "Email", DefaultEmail).Trim();
        var email = rawEmail.ToLowerInvariant();
        if (email.Length == 0 || !email.Contains('@'))
        {
            warnings.Add($"[Test] Email 값이 이메일 형식이 아닙니다(\"{rawEmail}\") — 기본값 {DefaultEmail} 를 사용합니다.");
            email = DefaultEmail;
        }

        // Role: trim + 소문자 정규화 후 5개 리터럴과 대조.
        // ⚠️ ParseRole의 미지원값 폴백(User)에 의존하지 않는다 — 그 규약은 **서버가 준 값**의 권한 상승을 막는
        //    것이고 여기에는 서버가 없다. 대신 의도와 다른 역할이 조용히 서는 것을 막아야 하며,
        //    안전망은 "배너에 실제 역할을 표시"하는 것이다(§B9).
        var rawRole = ini.GetString(SectionName, "Role", string.Empty).Trim();
        UserRole role;
        if (rawRole.Length == 0)
        {
            role = DefaultRole;
        }
        else
        {
            var normalized = rawRole.ToLowerInvariant();
            if (Array.IndexOf(AllowedRoles, normalized) >= 0)
            {
                role = UserRoleExtensions.ParseRole(normalized);
            }
            else
            {
                warnings.Add($"[Test] Role 값을 알 수 없습니다(\"{rawRole}\") — 기본값 {DefaultRole.ToFirestoreValue()} 로 실행합니다.");
                role = DefaultRole;
            }
        }

        // Pin: 없으면 게이트 생략(정상 상태 — 경고 없음). 있으나 4자리 숫자가 아니면 없음 취급 + 경고.
        var rawPin = ini.GetString(SectionName, "Pin", string.Empty).Trim();
        string? pin = null;
        if (rawPin.Length > 0)
        {
            if (IsFourDigits(rawPin)) pin = rawPin;
            else warnings.Add($"[Test] Pin 은 4자리 숫자여야 합니다(\"{rawPin}\") — PIN 게이트를 생략합니다.");
        }

        var qrBlocked = ini.GetBool(SectionName, "QrBlocked", false);

        var rawReason = ini.GetString(SectionName, "QrBlockReason", string.Empty).Trim();
        var reason = DefaultQrBlockReason;
        if (rawReason.Length > 0)
        {
            switch (rawReason.ToLowerInvariant())
            {
                case "time": reason = QrGateReason.Time; break;
                case "count": reason = QrGateReason.Count; break;
                default:
                    warnings.Add($"[Test] QrBlockReason 값을 알 수 없습니다(\"{rawReason}\") — 기본값 count 를 사용합니다.");
                    break;
            }
        }

        return new TestModeOptions(true, id, email, role, pin, qrBlocked, reason, warnings);
    }

    /// <summary>
    /// 테스트 계정 <see cref="User"/> 생성. <c>HasPin</c>은 <see cref="Pin"/> 존재에서 <b>파생</b>하며
    /// (§B3.2 단일 소스), <c>AuthMethod</c>는 Google 고정이다(영향 범위가 라벨 2종뿐이라 키로 둘 값이 없다).
    /// <para>
    /// ⚠️ 이 <see cref="User"/>에는 "테스트 계정"을 표시하는 필드가 없다 — 도메인 모델에 테스트 전용 플래그를
    /// 넣으면 그 값이 서버 DTO 매핑·직렬화·표시 코드로 번진다. 판정은 <c>ITestModeService.IsTestUser</c>의
    /// <b>참조 동일성</b>이 담당한다(위조 불가).
    /// </para>
    /// </summary>
    public User CreateUser() => new()
    {
        Id = Id,
        Role = Role,
        Email = Email,
        AuthMethod = AuthMethod.Google,
        HasPin = Pin is not null,
        CreatedAt = DateTime.UtcNow,
    };

    private static bool IsFourDigits(string value)
    {
        if (value.Length != 4) return false;
        foreach (var c in value)
            if (c is < '0' or > '9') return false;
        return true;
    }
}
