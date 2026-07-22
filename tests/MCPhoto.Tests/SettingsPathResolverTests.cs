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

    [Fact]
    public void Empty_Candidates_Throws()
        => Assert.Throws<ArgumentException>(() => SettingsPathResolver.ResolveWritable(Array.Empty<string>(), _ => true));
}
