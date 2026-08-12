using MCPhoto.Core.Settings;

namespace MCPhoto.Tests;

/// <summary>
/// it6 #1: 설정 INI 경로 우선순위 순수 로직 검증. 실행경로 → ProgramData → LocalAppData,
/// 쓰기 가능한 첫 경로 선택. 판정 함수 주입으로 headless 테스트(실제 파일시스템 무관).
/// </summary>
public class SettingsPathResolverTests
{
    private const string Exe = @"C:\app\MCPhoto.ini";
    private const string Prog = @"C:\pd\MCPhoto\MCPhoto.ini";
    private const string Local = @"C:\la\MCPhoto\MCPhoto.ini";
    private static readonly string[] Candidates = { Exe, Prog, Local };

    [Fact]
    public void Picks_Exe_Path_First_When_Writable()
    {
        var pick = SettingsPathResolver.ResolveWritable(Candidates, _ => true);
        Assert.Equal(Exe, pick); // 실행경로 1순위
    }

    [Fact]
    public void Falls_Back_To_ProgramData_When_Exe_Not_Writable()
    {
        var pick = SettingsPathResolver.ResolveWritable(Candidates, p => p != Exe);
        Assert.Equal(Prog, pick);
    }

    [Fact]
    public void Falls_Back_To_LocalAppData_When_Exe_And_ProgramData_Not_Writable()
    {
        var pick = SettingsPathResolver.ResolveWritable(Candidates, p => p == Local);
        Assert.Equal(Local, pick);
    }

    [Fact]
    public void Returns_First_Candidate_When_None_Writable()
    {
        // 전부 실패 시 1순위(실행경로) 반환 — Save 폴백 체인이 재시도.
        var pick = SettingsPathResolver.ResolveWritable(Candidates, _ => false);
        Assert.Equal(Exe, pick);
    }

    [Fact]
    public void DefaultCandidates_Order_Is_Exe_Program_Local()
    {
        var c = SettingsPathResolver.DefaultCandidates(@"C:\app", @"C:\pd", @"C:\la");
        Assert.Equal(3, c.Count);
        Assert.Equal(@"C:\app\MCPhoto.ini", c[0]);
        Assert.Equal(@"C:\pd\MCPhoto\MCPhoto.ini", c[1]);
        Assert.Equal(@"C:\la\MCPhoto\MCPhoto.ini", c[2]);
    }

    /// <summary>
    /// it26 §3.5 T23 — <b>ini 경로 정책은 이 이터레이션에서 바뀌지 않는다</b>(result·Frame만 이관됐다).
    /// <para>
    /// 왜 잠그는가: 1순위를 %ProgramData%로 "정리"하면 ① 승격으로 운영해 온 기존 설치가 <c>{app}\MCPhoto.ini</c>를
    /// 읽지 못해 기본값으로 시작하고 첫 종료에 그 기본값을 새 위치에 기록한다(되돌릴 수 없는 설정 유실)
    /// ② 개발 실행이 설치본과 같은 ini를 공유해 <c>[Test]</c>(인증 우회)가 전파된다 — 설정 혼동이 아니라 보안 사고다.
    /// </para>
    /// </summary>
    [Fact]
    public void Ini_Path_Policy_Still_Prefers_Exe_Directory()
    {
        var c = SettingsPathResolver.DefaultCandidates(@"C:\app", @"C:\pd", @"C:\la");

        Assert.Equal(@"C:\app\MCPhoto.ini", c[0]);                    // 실행경로가 1순위
        Assert.DoesNotContain(@"C:\pd", c[0]);                        // ProgramData가 1순위가 아니다
        Assert.Equal(@"C:\app\MCPhoto.ini",
            SettingsPathResolver.ResolveWritable(c, _ => true));      // 전부 쓰기 가능하면 실행경로를 고른다
    }

    [Fact]
    public void Empty_Candidates_Throws()
        => Assert.Throws<ArgumentException>(() => SettingsPathResolver.ResolveWritable(Array.Empty<string>(), _ => true));
}
