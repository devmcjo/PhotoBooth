using System.IO;
using MCPhoto.Core.Settings;

namespace MCPhoto.Tests;

/// <summary>
/// it26 §3.5 T3 — 설정 파일이 설치 폴더(Program Files) 하위인지 판정(시작 시 Warning의 입력).
/// 경로 <b>정책</b>은 이 이터레이션에서 바뀌지 않는다(<see cref="SettingsPathResolver"/> 참조) — 관측만 추가했다.
/// </summary>
public class SettingsPathDiagnosticsTests
{
    private const string Pf = @"C:\Program Files";
    private const string Pf86 = @"C:\Program Files (x86)";

    [Theory]
    [InlineData(@"C:\Program Files\MCPhoto\MCPhoto.ini", true)]
    [InlineData(@"C:\Program Files (x86)\MCPhoto\MCPhoto.ini", true)]
    [InlineData(@"c:\program files\mcphoto\mcphoto.ini", true)]              // 대소문자 무시
    [InlineData(@"C:\ProgramData\MCPhoto\MCPhoto.ini", false)]
    [InlineData(@"C:\Users\op\AppData\Local\MCPhoto\MCPhoto.ini", false)]
    [InlineData(@"E:\Study\photobooth\src\MCPhoto.App\MCPhoto.ini", false)]  // 개발 실행(리포 경로)
    [InlineData(@"C:\Program Files Extra\MCPhoto\MCPhoto.ini", false)]       // 접두만 같은 다른 폴더
    public void Judges_Program_Files_Subpaths(string path, bool expected)
    {
        Assert.Equal(expected, SettingsPathDiagnostics.IsUnderProgramFiles(path, Pf, Pf86));
    }

    [Fact]
    public void Trailing_Separator_Does_Not_Matter()
    {
        Assert.True(SettingsPathDiagnostics.IsUnderProgramFiles(
            @"C:\Program Files\MCPhoto\MCPhoto.ini", Pf + @"\", Pf86 + @"\"));
        Assert.True(SettingsPathDiagnostics.IsUnderProgramFiles(
            @"C:\Program Files\MCPhoto\", Pf, Pf86));
    }

    [Fact]
    public void Root_Itself_Counts_As_Under()
    {
        // 파일이 Program Files 루트에 직접 있는 병리적 배치도 경고 대상이다.
        Assert.True(SettingsPathDiagnostics.IsUnderProgramFiles(Pf, Pf, Pf86));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_Path_Is_Never_Under(string? path)
    {
        Assert.False(SettingsPathDiagnostics.IsUnderProgramFiles(path, Pf, Pf86));
    }

    [Fact]
    public void Blank_Prefix_Does_Not_Match_Everything()
    {
        // ⚠️ 빈 접두를 Path.GetFullPath에 넣으면 현재 디렉터리로 확장돼 **모든 경로가 참**이 되는 함정.
        Assert.False(SettingsPathDiagnostics.IsUnderProgramFiles(
            @"C:\ProgramData\MCPhoto\MCPhoto.ini", string.Empty, null));
        Assert.False(SettingsPathDiagnostics.IsUnderProgramFiles(
            Path.Combine(Directory.GetCurrentDirectory(), "MCPhoto.ini"), "   ", string.Empty));
    }

    [Fact]
    public void Invalid_Path_Returns_False_Instead_Of_Throwing()
    {
        // 진단 목적이므로 판정 불가는 조용한 false다(시작 경로에서 예외를 던지지 않는다).
        Assert.False(SettingsPathDiagnostics.IsUnderProgramFiles("C:\\Program Files\\<>|\0", Pf, Pf86));
    }
}
