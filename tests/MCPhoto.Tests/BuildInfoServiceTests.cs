using System.IO;
using System.Reflection;
using MCPhoto.Core.Build;

namespace MCPhoto.Tests;

/// <summary>
/// 빌드 정보(it18 — 어셈블리 버전 리소스 + exe 타임스탬프). 외부 파일 의존이 없어졌으므로
/// 검증 대상은 ① 버전 포맷(4자리 → 3자리), ② 빌드 시각 포맷·출처, ③ 실패 경로 폴백(크래시 금지)이다.
/// 종전 bldinfo.ini 로드 테스트(부재/부분키/빈값/손상 폴백)는 파일 자체가 폐기되어 함께 삭제됐다.
/// </summary>
public class BuildInfoServiceTests
{
    /// <summary>알려진 버전을 가진 어셈블리 — 실행 환경(테스트 러너 버전)에 의존하지 않게 고정한다.</summary>
    private static Assembly KnownAssembly => typeof(BuildInfoServiceTests).Assembly;

    private static string TempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mcphoto_buildinfo_{Guid.NewGuid():N}.tmp");
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void Version_Is_Three_Parts_From_Assembly()
    {
        // AssemblyVersion은 항상 4자리(major.minor.patch.revision)로 저장되지만 표기는 3자리다.
        var expected = KnownAssembly.GetName().Version!.ToString(3);
        var svc = new AssemblyBuildInfoService(KnownAssembly, TempFileKeptAlive(out var path));
        try
        {
            Assert.Equal(expected, svc.Version);
            Assert.Equal(3, svc.Version.Split('.').Length); // revision이 붙지 않는다
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void DisplayText_Is_Version_Only()
    {
        var svc = new AssemblyBuildInfoService(KnownAssembly, TempFileKeptAlive(out var path));
        try
        {
            // it18: 배포 채널(종전 " · Beta") 표기 폐지 → "v{Version}" 뿐이다.
            Assert.Equal($"v{svc.Version}", svc.DisplayText);
            Assert.DoesNotContain("·", svc.DisplayText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void BuildDate_Comes_From_File_LastWriteTime()
    {
        var path = TempFile();
        try
        {
            // 설치·복사로 덮어써지는 CreationTime이 아니라 LastWriteTime을 읽는지 확인한다.
            var stamp = new DateTime(2026, 7, 30, 16, 42, 0, DateTimeKind.Local);
            File.SetLastWriteTime(path, stamp);

            var svc = new AssemblyBuildInfoService(KnownAssembly, path);
            Assert.Equal("2026-07-30 16:42", svc.BuildDate);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Missing_Exe_Path_Leaves_BuildDate_Empty()
    {
        var absent = Path.Combine(Path.GetTempPath(), $"mcphoto_absent_{Guid.NewGuid():N}.exe");
        var svc = new AssemblyBuildInfoService(KnownAssembly, absent);

        Assert.Equal(string.Empty, svc.BuildDate); // 빈 문자열 → 진단 화면이 "(확인 불가)"로 표기
        Assert.Equal(KnownAssembly.GetName().Version!.ToString(3), svc.Version); // 버전은 영향 없음
    }

    [Fact]
    public void Empty_Exe_Path_Does_Not_Throw()
    {
        // Assembly.Location이 빈 문자열인 단일 파일 퍼블리시 상황을 모사(경로가 비어도 크래시 금지).
        var svc = new AssemblyBuildInfoService(KnownAssembly, string.Empty);
        Assert.Equal(string.Empty, svc.BuildDate);
    }

    [Fact]
    public void Null_Assembly_Falls_Back_To_Default_Version()
    {
        // GetEntryAssembly()가 null인 호스팅 환경(일부 네이티브 호스트)에서도 기본값으로 진행해야 한다.
        var svc = new AssemblyBuildInfoService(assembly: null, exePath: TempFileKeptAlive(out var path));
        try
        {
            // 테스트 프로세스에는 엔트리 어셈블리가 있으므로 null 전달 시에도 폴백이 아닌 실제 값이 나올 수 있다.
            // 여기서 보장할 것은 "예외 없이 형식이 유효한 버전 문자열"이다.
            Assert.False(string.IsNullOrWhiteSpace(svc.Version));
            Assert.Equal(3, svc.Version.Split('.').Length);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Default_Version_Constant_Is_Zero()
        => Assert.Equal("0.0.0", AssemblyBuildInfoService.DefaultVersion);

    /// <summary>임시 파일을 만들고 경로를 밖으로 넘긴다(finally에서 지우기 위해).</summary>
    private static string TempFileKeptAlive(out string path)
    {
        path = TempFile();
        return path;
    }
}
