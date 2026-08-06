using System;
using System.IO;
using System.Linq;
using MCPhoto.App.Services;
using Xunit;

namespace MCPhoto.Tests;

/// <summary>
/// GPLv3 준수 이행 회귀 방지. (설계: docs/design/wpf-ffmpeg-licensing-and-distribution-design.md §5.1)
///
/// 이 앱은 GPLv3 바이너리(tools/ffmpeg/ffmpeg.exe)를 재배포한다. GPL은 재배포를 금지하지 않지만
/// ① 라이선스 전문 전달 ② 저작권·적용 사실 고지 ③ 대응 소스 접근 제공을 요구하고,
/// **이 중 하나라도 빠지면 위반**이다. 고지 파일은 코드가 아니라서 리팩터링·파일 정리 중
/// 조용히 사라지기 쉬우므로 여기서 존재와 필수 내용을 고정한다.
/// </summary>
public class LicenseComplianceTests
{
    /// <summary>리포지토리 루트(테스트 실행 디렉터리에서 상위 탐색).</summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "MCPhoto.App"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("리포지토리 루트를 찾지 못함");
    }

    private static string LicensesDir => Path.Combine(FindRepoRoot(), "licenses");

    private static string ReadLicenseFile(string name)
    {
        var path = Path.Combine(LicensesDir, name);
        Assert.True(File.Exists(path), $"라이선스 고지 파일이 없다: {path} — GPLv3 §4/§6 위반 상태로 배포된다");
        return File.ReadAllText(path);
    }

    // ── 고지 산출물 존재·내용 ──

    /// <summary>O1: GPLv3 전문이 동봉되어야 한다(§4). 요약본·링크로 대체할 수 없다.</summary>
    [Fact]
    public void GplV3_Full_Text_Is_Bundled()
    {
        var text = ReadLicenseFile("FFmpeg-COPYING.GPLv3.txt");

        Assert.Contains("GNU GENERAL PUBLIC LICENSE", text);
        Assert.Contains("Version 3, 29 June 2007", text);
        // 전문에만 있는 조항 표제들 — 발췌본으로 바뀌면 실패한다.
        Assert.Contains("TERMS AND CONDITIONS", text);
        Assert.Contains("6. Conveying Non-Source Forms", text);
        Assert.Contains("15. Disclaimer of Warranty", text);
        // 전문은 600줄이 넘는다(요약 파일 교체 방지).
        Assert.True(text.Split('\n').Length > 600, "GPLv3 전문이 아니라 축약본으로 보인다");
    }

    /// <summary>
    /// O2·O3: ffmpeg 고지에 버전·빌드 configuration·소스 위치·3년 서면 오퍼가 모두 있어야 한다.
    /// 특히 **대응 소스 접근 제공**(§6)이 가장 빠뜨리기 쉬운 항목이다.
    /// </summary>
    [Fact]
    public void Ffmpeg_Notice_Has_Version_Config_Source_And_Written_Offer()
    {
        var text = ReadLicenseFile("FFmpeg-README.txt");

        // 어떤 바이너리인지 특정 가능해야 한다.
        Assert.Contains("8.1.2", text);
        Assert.Contains("gyan.dev", text);

        // GPLv3 §4는 저작권 고지 **유지**를 요구한다 — 버전만 적고 저작권자를 빠뜨리면 미이행이다.
        Assert.Contains("Copyright (c) 2000-2026 the FFmpeg developers", text);

        // 재현 가능한 빌드 구성(대응 소스의 범위를 결정한다).
        Assert.Contains("--enable-gpl", text);
        Assert.Contains("--enable-version3", text);
        Assert.Contains("--enable-libx264", text);

        // 소스 접근 경로(§6(d)) — 최소 한 곳 이상의 실제 주소.
        Assert.Contains("https://github.com/GyanD/codexffmpeg", text);
        Assert.Contains("ffmpeg.org", text);

        // 서면 오퍼(§6(b)) — 물리 매체 배포 동선을 대비한다.
        Assert.Contains("3년", text);
        Assert.Contains("devmcjo@gmail.com", text);

        // O5: 추가 제약을 걸지 않았음을 명시.
        Assert.Contains("제한하지 않습니다", text);

        // 전문 파일을 가리켜야 한다(O1과의 연결).
        Assert.Contains("FFmpeg-COPYING.GPLv3.txt", text);
    }

    /// <summary>고지 인덱스가 있어야 사용자가 무엇이 왜 들어 있는지 안다.</summary>
    [Fact]
    public void License_Index_Lists_Ffmpeg_And_Keeps_Mcphoto_Mit()
    {
        var text = ReadLicenseFile("README.txt");

        Assert.Contains("FFmpeg", text);
        Assert.Contains("GPL", text);
        Assert.Contains("MIT", text);          // MCPhoto 본체 라이선스가 유지됨을 밝힌다
        Assert.Contains("FFmpeg-README.txt", text);

        // 안내가 가리키는 파일이 실제로 배포물에 실려야 한다 — 없으면 거짓 안내가 된다.
        Assert.Contains("MCPhoto-LICENSE-MIT.txt", text);
    }

    /// <summary>
    /// MC포토 본체(MIT) 전문도 배포물에 실려야 한다. 인덱스가 "MCPhoto-LICENSE-MIT.txt를 보라"고
    /// 안내하는데 파일이 없으면 안내가 거짓이 된다. 원본은 리포 루트 LICENSE 하나이며
    /// csproj가 링크 복사로 단일 소스를 유지한다(사본 파일을 만들지 않는다).
    /// </summary>
    [Fact]
    public void Mcphoto_Mit_License_Is_Shipped_Into_Licenses_Folder()
    {
        var root = FindRepoRoot();
        var source = Path.Combine(root, "LICENSE");
        Assert.True(File.Exists(source), "리포 루트 LICENSE가 없다");
        Assert.Contains("MIT License", File.ReadAllText(source));

        var csproj = File.ReadAllText(Path.Combine(root, "src", "MCPhoto.App", "MCPhoto.App.csproj"));
        Assert.Contains("McPhotoLicenseFile", csproj);
        Assert.Contains("licenses\\MCPhoto-LICENSE-MIT.txt", csproj);

        // 사본을 만들어 두 곳에서 관리하는 실수 방지 — licenses/ 안에 물리 파일이 있으면 안 된다.
        Assert.False(File.Exists(Path.Combine(LicensesDir, "MCPhoto-LICENSE-MIT.txt")),
            "licenses/ 에 MIT 사본을 두면 루트 LICENSE와 갈라진다 — csproj 링크 복사만 사용할 것");
    }

    /// <summary>
    /// 고지가 배포물에 실제로 실려야 의미가 있다. csproj가 빌드 출력과 **publish 산출물 양쪽**에
    /// licenses/를 복사하는지 고정한다(인스톨러는 publish 폴더 전체를 담는다).
    /// </summary>
    [Fact]
    public void Csproj_Copies_Licenses_To_Output_And_Publish()
    {
        var csproj = File.ReadAllText(
            Path.Combine(FindRepoRoot(), "src", "MCPhoto.App", "MCPhoto.App.csproj"));

        Assert.Contains("LicensesSource", csproj);
        Assert.Contains("Link=\"licenses\\", csproj);
        Assert.Contains("CopyLicensesToPublish", csproj);
        Assert.Contains("AfterTargets=\"Publish\"", csproj);
    }

    /// <summary>
    /// ffmpeg를 번들에서 빼면 GPL 의무도 사라진다(설계 §5.3). 반대로 **번들에 있는 한 고지는 필수**다.
    /// 이 테스트는 그 연결을 고정한다 — ffmpeg 복사 규칙이 살아 있는데 고지가 없으면 실패한다.
    /// </summary>
    [Fact]
    public void If_Ffmpeg_Is_Bundled_Then_Notice_Must_Exist()
    {
        var csproj = File.ReadAllText(
            Path.Combine(FindRepoRoot(), "src", "MCPhoto.App", "MCPhoto.App.csproj"));

        bool bundlesFfmpeg = csproj.Contains("FfmpegSource") && csproj.Contains("CopyFfmpegToPublish");
        if (!bundlesFfmpeg) return;   // 번들을 그만뒀다면 이 검사는 무의미하다

        foreach (var required in new[] { "FFmpeg-COPYING.GPLv3.txt", "FFmpeg-README.txt", "README.txt" })
        {
            var path = Path.Combine(LicensesDir, required);
            Assert.True(File.Exists(path),
                $"ffmpeg.exe를 번들하면서 고지 {required} 가 없다 — GPLv3 위반 상태다");
        }
    }

    // ── LicenseFolderService ──

    [Fact]
    public void Service_Path_Is_Licenses_Under_Base_Directory()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"mcphoto_lic_{Guid.NewGuid():N}");
        var svc = new LicenseFolderService(baseDirectory: baseDir);

        Assert.Equal(Path.Combine(baseDir, "licenses"), svc.LicenseFolderPath);
        Assert.False(svc.Exists);
    }

    [Fact]
    public void Service_Opens_When_Folder_Exists()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"mcphoto_lic_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(baseDir, "licenses"));
        var opened = new System.Collections.Generic.List<string>();

        var svc = new LicenseFolderService(opener: opened.Add, baseDirectory: baseDir);
        Assert.True(svc.Exists);
        svc.OpenLicenseFolder();

        Assert.Single(opened);
        Assert.Equal(svc.LicenseFolderPath, opened[0]);

        Directory.Delete(baseDir, recursive: true);
    }

    /// <summary>
    /// 폴더가 없으면 **만들지 않는다.** 빈 폴더를 만들어 열면 "고지가 누락됐다"는 사실을 감춘다 —
    /// 로그 폴더(없으면 생성)와 의도적으로 다른 동작이다.
    /// </summary>
    [Fact]
    public void Service_Does_Not_Create_Or_Open_When_Missing()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"mcphoto_lic_{Guid.NewGuid():N}");
        var opened = new System.Collections.Generic.List<string>();

        var svc = new LicenseFolderService(opener: opened.Add, baseDirectory: baseDir);
        svc.OpenLicenseFolder();

        Assert.Empty(opened);
        Assert.False(Directory.Exists(svc.LicenseFolderPath), "없는 라이선스 폴더를 생성하면 누락을 은폐한다");
    }
}
