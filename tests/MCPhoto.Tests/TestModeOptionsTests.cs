using System;
using System.IO;
using System.Linq;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Models;
using MCPhoto.Core.Settings;
using Xunit;

namespace MCPhoto.Tests;

/// <summary>
/// it23 §B3: <c>[Test]</c> 섹션 키 해석·검증·폴백(순수 함수). 잘못된 값이 **조용히** 다른 역할을 세우거나
/// 게이트를 서버 경로로 새게 만드는 것을 막는 지점이다.
/// <para>
/// ⚠️ 역할 문자열은 snake_case(<c>temp_user</c>·<c>advanced_user</c>)다.
/// <c>IniFile.GetEnum&lt;UserRole&gt;()</c>을 쓰면 C# 이름(<c>TempUser</c>)만 파싱하고 <c>temp_user</c>를 놓친다.
/// </para>
/// </summary>
public class TestModeOptionsTests
{
    private static TestModeOptions Parse(string ini) => TestModeOptions.FromIni(IniFile.Parse(ini));

    /// <summary>B-T10: [Test] 섹션이 없으면 꺼짐 + 전 기본값(앱 동작에 어떤 변화도 없다).</summary>
    [Fact]
    public void T10_No_Section_Is_Disabled_With_Defaults()
    {
        var o = Parse("[MCPhoto]\nCutCount=8\n");

        Assert.False(o.Enabled);
        Assert.Equal("testuser", o.Id);
        Assert.Equal("test@email.com", o.Email);
        Assert.Equal(UserRole.AdvancedUser, o.Role);
        Assert.Null(o.Pin);
        Assert.False(o.QrBlocked);
        Assert.Equal(QrGateReason.Count, o.QrBlockReason);
        Assert.False(o.ExternalCamera);                 // it25 §9.2
        Assert.Equal(-1, o.ExternalCameraType);
        Assert.Empty(o.Warnings);
    }

    /// <summary>
    /// TestMode=0이면 나머지 키를 해석하지 않는다 — 그 결과가 어디에도 쓰이지 않으므로 경고만 늘어난다.
    /// "분명히 썼는데 안 된다"는 <c>TestModeService</c>의 Information 로그가 답한다.
    /// </summary>
    [Fact]
    public void Switch_Off_Ignores_Other_Keys()
    {
        var o = Parse("[Test]\nTestMode=0\nRole=admin\nPin=oops\n");

        Assert.False(o.Enabled);
        Assert.Equal(UserRole.AdvancedUser, o.Role);
        Assert.Empty(o.Warnings);
    }

    /// <summary>B-T11: 5개 역할 문자열이 각각 해당 UserRole로 매핑된다.</summary>
    [Theory]
    [InlineData("temp_user", UserRole.TempUser)]
    [InlineData("user", UserRole.User)]
    [InlineData("advanced_user", UserRole.AdvancedUser)]
    [InlineData("manager", UserRole.Manager)]
    [InlineData("admin", UserRole.Admin)]
    public void T11_All_Five_Roles_Parse(string value, UserRole expected)
    {
        var o = Parse($"[Test]\nTestMode=1\nRole={value}\n");

        Assert.True(o.Enabled);
        Assert.Equal(expected, o.Role);
        Assert.Empty(o.Warnings);
    }

    /// <summary>
    /// §B3.3: 값의 <b>대소문자·공백을 무시</b>한다(설계 결정). <c>ParseRole</c>은 대소문자를 구분하므로
    /// 그대로 넘기면 <c>Role=Admin</c>이 조용히 <c>user</c>로 떨어진다 — 손으로 쓰는 설정 파일에서 그것은 함정이다.
    /// 명시 열거 규약은 유지된다(비교 대상이 여전히 5개 리터럴이고 서수 부등식을 쓰지 않는다).
    /// </summary>
    [Theory]
    [InlineData("Admin", UserRole.Admin)]
    [InlineData("  MANAGER  ", UserRole.Manager)]
    [InlineData("Advanced_User", UserRole.AdvancedUser)]
    public void Role_Value_Is_Case_And_Whitespace_Insensitive(string value, UserRole expected)
    {
        var o = Parse($"[Test]\nTestMode=1\nRole={value}\n");

        Assert.Equal(expected, o.Role);
        Assert.Empty(o.Warnings);
    }

    /// <summary>키 이름 대소문자도 무시된다(IniFile이 OrdinalIgnoreCase) — 요구 원문의 소문자 키가 그대로 동작한다.</summary>
    [Fact]
    public void Key_Names_Are_Case_Insensitive()
    {
        var o = Parse("[test]\ntestmode=1\nemail=QA@Example.COM\nrole=manager\n");

        Assert.True(o.Enabled);
        Assert.Equal("qa@example.com", o.Email);
        Assert.Equal(UserRole.Manager, o.Role);
    }

    /// <summary>
    /// B-T12: 잘못된 값은 문서화된 기본값으로 폴백하고 <b>경고를 남긴다</b>.
    /// 최소권한 폴백(<c>User</c>)을 쓰지 않는 이유: 그 규약은 **서버가 준 값**의 권한 상승을 막는 것이고
    /// 여기에는 서버가 없다. 대신 의도와 다른 역할이 조용히 서는 것을 막아야 하며, 안전망은 배너 표시다.
    /// </summary>
    [Theory]
    [InlineData("Role=admn")]
    [InlineData("Email=abc")]
    [InlineData("Pin=12a4")]
    [InlineData("Pin=12345")]
    [InlineData("QrBlockReason=xxx")]
    public void T12_Invalid_Value_Falls_Back_With_One_Warning(string line)
    {
        var o = Parse($"[Test]\nTestMode=1\n{line}\n");

        Assert.True(o.Enabled);
        Assert.Single(o.Warnings);
    }

    /// <summary>B-T12 상세: 각 폴백값이 문서와 일치한다(경고만 남고 값이 방치되지 않는다).</summary>
    [Fact]
    public void T12_Fallback_Values_Match_Documented_Defaults()
    {
        var o = Parse("[Test]\nTestMode=1\nId=\nRole=admn\nEmail=abc\nPin=12a4\nQrBlocked=1\nQrBlockReason=xxx\n");

        Assert.Equal("testuser", o.Id);
        Assert.Equal(UserRole.AdvancedUser, o.Role);
        Assert.Equal("test@email.com", o.Email);
        Assert.Null(o.Pin);                          // 형식 미달 PIN은 "없음" 취급 → 게이트 생략
        Assert.True(o.QrBlocked);
        Assert.Equal(QrGateReason.Count, o.QrBlockReason);
        Assert.Equal(4, o.Warnings.Count);           // Role · Email · Pin · QrBlockReason
        Assert.All(o.Warnings, w => Assert.Contains("[Test]", w));
    }

    /// <summary>인식 불가 bool은 안전측(false)으로 떨어진다.</summary>
    [Fact]
    public void Unrecognized_Bool_Is_Safe_Side()
    {
        Assert.False(Parse("[Test]\nTestMode=maybe\n").Enabled);
        Assert.False(Parse("[Test]\nTestMode=1\nQrBlocked=maybe\n").QrBlocked);
    }

    /// <summary>QrBlockReason=time은 설정 화면의 시간 사유 문구를 재현하는 값이다.</summary>
    [Fact]
    public void QrBlockReason_Time_Parses()
    {
        var o = Parse("[Test]\nTestMode=1\nRole=temp_user\nQrBlocked=1\nQrBlockReason=TIME\n");

        Assert.True(o.QrBlocked);
        Assert.Equal(QrGateReason.Time, o.QrBlockReason);
        Assert.Empty(o.Warnings);
    }

    /// <summary>
    /// B-T13: <c>CreateUser()</c> 파생값. <c>HasPin</c>은 <see cref="TestModeOptions.Pin"/> 존재에서만 나온다 —
    /// 별 키로 두면 두 값이 모순될 수 있고(<c>HasPin=1</c> + Pin 없음), 그 조합은 게이트를 서버 설정 분기로
    /// 보내 §B8의 블로커를 되살린다.
    /// </summary>
    [Fact]
    public void T13_CreateUser_Derives_HasPin_From_Pin()
    {
        var withPin = Parse("[Test]\nTestMode=1\nId=qa\nRole=manager\nEmail=QA@Example.com\nPin=1234\n").CreateUser();
        Assert.True(withPin.HasPin);
        Assert.Equal("qa", withPin.Id);
        Assert.Equal(UserRole.Manager, withPin.Role);
        Assert.Equal("qa@example.com", withPin.Email);   // 소문자 정규화(개인 프레임 소유 키)
        Assert.Equal(AuthMethod.Google, withPin.AuthMethod);

        var withoutPin = Parse("[Test]\nTestMode=1\n").CreateUser();
        Assert.False(withoutPin.HasPin);
    }

    /// <summary>
    /// 도메인 모델에 테스트 전용 플래그를 넣지 않았음을 고정한다 — 넣으면 그 값이 서버 DTO 매핑·직렬화·
    /// 표시 코드로 번지고, 실계정 <see cref="User"/>에도 실릴 수 있어 참조 동일성 봉인이 무의미해진다.
    /// </summary>
    [Fact]
    public void User_Model_Has_No_Test_Flag()
    {
        var names = typeof(User).GetProperties().Select(p => p.Name).ToArray();

        Assert.DoesNotContain("IsTest", names);
        Assert.DoesNotContain("IsTestMode", names);
    }

    // ══════════ it25 §5.1: 외부 카메라 시뮬레이션 2키 ══════════

    /// <summary>T-B1: 2키 결측 시 기본값, 정상 조합 파싱, bool 표기 변형.</summary>
    [Fact]
    public void B1_External_Camera_Keys_Default_And_Parse()
    {
        // 결측 → (false, -1). TestMode만 켠 상태가 종전 동작과 완전히 같아야 한다.
        var missing = Parse("[Test]\nTestMode=1\n");
        Assert.False(missing.ExternalCamera);
        Assert.Equal(-1, missing.ExternalCameraType);
        Assert.Empty(missing.Warnings);

        // 정상 조합 → (true, 0) = D5300 인식 시뮬레이션(S6).
        var on = Parse("[Test]\nTestMode=1\nExternalCamera=1\nExternalCameraType=0\n");
        Assert.True(on.ExternalCamera);
        Assert.Equal(0, on.ExternalCameraType);
        Assert.Empty(on.Warnings);

        // 인식 0 상태(S4)의 명시 조합 — 모순이 아니라 정의된 조합이다.
        var none = Parse("[Test]\nTestMode=1\nExternalCamera=on\nExternalCameraType=-1\n");
        Assert.True(none.ExternalCamera);
        Assert.Equal(-1, none.ExternalCameraType);
        Assert.Empty(none.Warnings);
    }

    /// <summary>bool 표기 변형(<c>true</c>/<c>on</c>/<c>yes</c>)이 기존 규약대로 통한다.</summary>
    [Theory]
    [InlineData("true")]
    [InlineData("on")]
    [InlineData("yes")]
    [InlineData("1")]
    public void B1_External_Camera_Bool_Spellings(string value)
        => Assert.True(Parse($"[Test]\nTestMode=1\nExternalCamera={value}\n").ExternalCamera);

    /// <summary>
    /// T-B2: 목록 밖·파싱 실패 Type은 <c>-1</c>로 폴백하고 <b>경고 정확 1건</b>을 남긴다(E21).
    /// 시뮬레이션은 그대로 S4 시나리오로 동작하므로 화면이 멈추지 않는다.
    /// </summary>
    [Theory]
    [InlineData("-2")]
    [InlineData("99")]
    [InlineData("abc")]
    public void B2_Unknown_External_Camera_Type_Falls_Back_With_One_Warning(string value)
    {
        var o = Parse($"[Test]\nTestMode=1\nExternalCamera=1\nExternalCameraType={value}\n");

        Assert.True(o.ExternalCamera);
        Assert.Equal(-1, o.ExternalCameraType);
        Assert.Single(o.Warnings);
        Assert.Contains("[Test] ExternalCameraType", o.Warnings[0]);
        Assert.Contains(value, o.Warnings[0]);   // 운영자가 쓴 원문을 그대로 인용한다
    }

    /// <summary>인식 불가 bool은 안전측(false = 시뮬레이션 꺼짐)이며 경고를 남기지 않는다(bool 규약).</summary>
    [Fact]
    public void B2_Unrecognized_External_Camera_Bool_Is_Off_Without_Warning()
    {
        var o = Parse("[Test]\nTestMode=1\nExternalCamera=maybe\n");

        Assert.False(o.ExternalCamera);
        Assert.Empty(o.Warnings);
    }

    /// <summary>
    /// ★ Type은 마스터 스위치가 켜졌을 때만 의미다 — 꺼진 상태에서는 <b>경고 없이 무시</b>한다(§5.1).
    /// 꺼진 상태의 값에 경고를 내면 "Type이 틀렸다"고 말하는 셈인데 실제 문제는 "ExternalCamera=1을
    /// 안 썼다"이므로 안내가 QA를 헤매게 한다.
    /// </summary>
    [Theory]
    [InlineData("0")]
    [InlineData("99")]
    public void B2_Type_Is_Ignored_Without_Warning_When_Switch_Is_Off(string value)
    {
        var o = Parse($"[Test]\nTestMode=1\nExternalCamera=0\nExternalCameraType={value}\n");

        Assert.False(o.ExternalCamera);
        Assert.Equal(-1, o.ExternalCameraType);
        Assert.Empty(o.Warnings);
    }

    /// <summary>TestMode=0이면 시뮬레이션 2키도 해석되지 않는다(기존 마스터 스위치 규약 그대로).</summary>
    [Fact]
    public void B2_Test_Mode_Off_Ignores_Simulation_Keys()
    {
        var o = Parse("[Test]\nTestMode=0\nExternalCamera=1\nExternalCameraType=0\n");

        Assert.False(o.Enabled);
        Assert.False(o.ExternalCamera);
        Assert.Equal(-1, o.ExternalCameraType);
        Assert.Empty(o.Warnings);
    }

    /// <summary>
    /// 서비스는 <c>ISettingsService.IniPath</c>가 가리키는 파일을 읽는다 — 자체 경로 해석을 만들면
    /// "쓰기 가능한 첫 후보" 판정이 둘로 갈려 원인 추적이 미궁이 된다.
    /// </summary>
    [Fact]
    public void Service_Reads_Section_From_Settings_Ini_Path()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mcphoto_tm_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "MCPhoto.ini");
        File.WriteAllText(path, "[MCPhoto]\nCutCount=8\n[Test]\nTestMode=1\nId=qa\nRole=admin\nPin=1234\n");
        try
        {
            var settings = new IniSettingsService(iniPath: path, fallbackCandidates: new[] { path });
            var svc = new TestModeService(settings);

            Assert.True(svc.IsEnabled);
            Assert.Equal(path, svc.SourcePath);
            Assert.NotNull(svc.TestUser);
            Assert.Equal(UserRole.Admin, svc.TestUser!.Role);
            Assert.Equal("1234", svc.Options.Pin);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    /// <summary>
    /// <c>TestUser</c>는 **같은 인스턴스**를 돌려주고, <c>IsTestUser</c>는 참조 동일성으로 판정한다.
    /// 값이 전부 같은 별 인스턴스는 false여야 한다 — 그것이 위조 불가의 근거다(§B8.3 S2).
    /// </summary>
    [Fact]
    public void IsTestUser_Is_Reference_Identity_Not_Value_Equality()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mcphoto_tm_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "MCPhoto.ini");
        File.WriteAllText(path, "[Test]\nTestMode=1\nId=qa\nEmail=qa@example.com\nRole=admin\n");
        try
        {
            var svc = new TestModeService(new IniSettingsService(iniPath: path, fallbackCandidates: new[] { path }));
            var user = svc.TestUser!;

            Assert.Same(user, svc.TestUser);          // 1회 생성 후 같은 인스턴스
            Assert.True(svc.IsTestUser(user));

            var twin = new User { Id = "qa", Email = "qa@example.com", Role = UserRole.Admin };
            Assert.False(svc.IsTestUser(twin));       // 값이 전부 같아도 false
            Assert.False(svc.IsTestUser(null));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    /// <summary>파일이 없어도 크래시하지 않고 꺼짐으로 진행한다(경로는 그대로 돌려준다).</summary>
    [Fact]
    public void Service_Without_Ini_File_Is_Disabled()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mcphoto_missing_{Guid.NewGuid():N}.ini");
        var svc = new TestModeService(new IniSettingsService(iniPath: path, fallbackCandidates: new[] { path }));

        Assert.False(svc.IsEnabled);
        Assert.Null(svc.TestUser);
        Assert.Equal(path, svc.SourcePath);
        Assert.NotNull(svc.Options);   // IsEnabled=false에서도 non-null(호출부에 null 분기를 늘리지 않는다)
    }
}
