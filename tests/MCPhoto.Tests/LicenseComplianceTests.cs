using System;
using System.IO;
using System.Linq;
using System.Text;
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

    // ── LicenseNoticeService (it23 §C7: 폴더 열기·경로 표시 → 열거·본문 읽기로 대체) ──
    // ⚠️ Service_Opens_When_Folder_Exists 는 삭제했다 — 탐색기 열기 기능 자체가 폐지됐다(요구).

    /// <summary>임시 고지 폴더를 만들고 파일을 채운다. 반환값은 baseDirectory(= licenses의 부모).</summary>
    private static string MakeTempLicenses(params (string relativePath, string content)[] files)
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"mcphoto_lic_{Guid.NewGuid():N}");
        var dir = Path.Combine(baseDir, "licenses");
        Directory.CreateDirectory(dir);
        foreach (var (rel, content) in files)
        {
            var path = Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // 동봉 파일과 같은 조건: UTF-8 no BOM.
            File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        return baseDir;
    }

    private static void Cleanup(string baseDir)
    {
        try { if (Directory.Exists(baseDir)) Directory.Delete(baseDir, recursive: true); }
        catch { /* 임시 폴더 정리 실패는 테스트 결과와 무관 */ }
    }

    /// <summary>C-T1: 고지 폴더 경로 산출 + 폴더가 없으면 Exists=false.</summary>
    [Fact]
    public void Service_Path_Is_Licenses_Under_Base_Directory()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"mcphoto_lic_{Guid.NewGuid():N}");
        var svc = new LicenseNoticeService(baseDirectory: baseDir);

        Assert.Equal(Path.Combine(baseDir, "licenses"), svc.FolderPath);
        Assert.False(svc.Exists);
    }

    /// <summary>
    /// C-T2: 폴더가 없으면 **만들지 않는다.** 빈 폴더를 만들면 "고지가 누락됐다"는 사실을 감춘다 —
    /// 로그 폴더(없으면 생성)와 의도적으로 다른 동작이다.
    /// </summary>
    [Fact]
    public void Service_Does_Not_Create_When_Missing()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"mcphoto_lic_{Guid.NewGuid():N}");
        var svc = new LicenseNoticeService(baseDirectory: baseDir);

        Assert.Empty(svc.ListDocuments());
        Assert.False(Directory.Exists(svc.FolderPath), "없는 라이선스 폴더를 생성하면 누락을 은폐한다");
    }

    /// <summary>
    /// C-T3: 열거 정렬 규약 — README.txt 최상단, 나머지 이름 오름차순, 하위 폴더 포함, 비-txt 제외.
    /// 하드코딩 목록을 쓰지 않는 것이 요점이다(폴더에 파일을 넣으면 배포되고 목록에도 나와야 한다).
    /// </summary>
    [Fact]
    public void Service_Enumerates_Txt_Recursively_With_Index_First()
    {
        var baseDir = MakeTempLicenses(
            ("ZZZ.txt", "z"),
            ("README.txt", "index"),
            ("AAA.txt", "a"),
            ("sub/Nested.txt", "n"),
            ("binary.png", "not text"));
        try
        {
            var docs = new LicenseNoticeService(baseDirectory: baseDir).ListDocuments();

            Assert.Equal(new[] { "README.txt", "AAA.txt", "sub/Nested.txt", "ZZZ.txt" },
                docs.Select(d => d.DisplayName).ToArray());
            Assert.DoesNotContain("binary.png", docs.Select(d => d.DisplayName));
            Assert.All(docs, d => Assert.True(d.SizeBytes > 0));
        }
        finally { Cleanup(baseDir); }
    }

    /// <summary>C-T4: UTF-8 no BOM 한글 파일이 온전히 읽히고 CRLF가 보존된다("그대로 노출" 요구).</summary>
    [Fact]
    public void Service_Reads_Utf8_Korean_And_Preserves_Crlf()
    {
        var baseDir = MakeTempLicenses(("README.txt", "오픈소스 라이선스\r\n둘째 줄\ttab"));
        try
        {
            var svc = new LicenseNoticeService(baseDirectory: baseDir);
            var result = svc.ReadText(svc.ListDocuments()[0]);

            Assert.True(result.IsSuccess);
            Assert.Equal("오픈소스 라이선스\r\n둘째 줄\ttab", result.Text);
            Assert.Null(result.ErrorMessage);
        }
        finally { Cleanup(baseDir); }
    }

    /// <summary>C-T5: BOM 있는 UTF-8 파일도 선두에 BOM 문자가 남지 않는다(첫 글자로 보이면 안 된다).</summary>
    [Fact]
    public void Service_Strips_Bom_From_Text()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"mcphoto_lic_{Guid.NewGuid():N}");
        var dir = Path.Combine(baseDir, "licenses");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "README.txt"), "GNU 전문",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        try
        {
            var svc = new LicenseNoticeService(baseDirectory: baseDir);
            var result = svc.ReadText(svc.ListDocuments()[0]);

            Assert.True(result.IsSuccess);
            Assert.Equal("GNU 전문", result.Text);
            // ⚠️ string.Contains(char)는 서수 비교다. Assert.DoesNotContain(string,string)은 문화권 비교라
            //    U+FEFF가 '무게 없는 문자'로 취급되어 **어떤 문자열에서도 발견**되고 테스트가 항상 실패한다.
            Assert.False(result.Text!.Contains('\uFEFF'), "본문 선두에 BOM 문자가 남았다");
        }
        finally { Cleanup(baseDir); }
    }

    /// <summary>C-T6: 표시 상한(2 MB) 초과는 예외가 아니라 F4 문구. 경로는 문구에 없다(요구).</summary>
    [Fact]
    public void Service_Rejects_Oversized_File_With_Message()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"mcphoto_lic_{Guid.NewGuid():N}");
        var dir = Path.Combine(baseDir, "licenses");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "Huge.txt");
        File.WriteAllBytes(path, new byte[LicenseNoticeService.MaxDisplayBytes + 1]);
        try
        {
            var svc = new LicenseNoticeService(baseDirectory: baseDir);
            var result = svc.ReadText(svc.ListDocuments()[0]);

            Assert.False(result.IsSuccess);
            Assert.Contains("파일이 너무 커서 화면에 표시할 수 없습니다", result.ErrorMessage);
            Assert.DoesNotContain(dir, result.ErrorMessage);
        }
        finally { Cleanup(baseDir); }
    }

    /// <summary>C-T7: 0바이트 파일은 F5 문구(정상처럼 보여 주면 배포 불완전을 은폐한다).</summary>
    [Fact]
    public void Service_Reports_Empty_File()
    {
        var baseDir = MakeTempLicenses(("Empty.txt", string.Empty));
        try
        {
            var svc = new LicenseNoticeService(baseDirectory: baseDir);
            var result = svc.ReadText(svc.ListDocuments()[0]);

            Assert.False(result.IsSuccess);
            Assert.Equal("이 파일은 비어 있습니다. 배포 산출물이 불완전할 수 있습니다.", result.ErrorMessage);
        }
        finally { Cleanup(baseDir); }
    }

    /// <summary>C-T8: 존재하지 않는 경로도 **예외가 아니라** F3 문구(설정 화면이 통째로 닫히면 안 된다).</summary>
    [Fact]
    public void Service_Read_Failure_Returns_Message_Not_Exception()
    {
        var svc = new LicenseNoticeService(baseDirectory: Path.GetTempPath());
        var ghost = new LicenseDocument("Ghost.txt",
            Path.Combine(Path.GetTempPath(), $"no_such_{Guid.NewGuid():N}.txt"), 10);

        var result = svc.ReadText(ghost);

        Assert.False(result.IsSuccess);
        Assert.Equal("이 파일을 읽을 수 없습니다. 파일이 사용 중이거나 접근 권한이 없습니다.", result.ErrorMessage);
    }

    /// <summary>
    /// C-T9: 리포의 **실제 배포 파일**이 전부 열거·읽히고 한글이 깨지지 않으며 GPLv3가 전문(600줄 초과)이다.
    /// MIT 전문은 빌드 시 루트 LICENSE에서 링크 복사되므로 소스 폴더에는 없다(그래서 3건).
    /// </summary>
    [Fact]
    public void Service_Reads_Real_Repo_License_Files()
    {
        var svc = new LicenseNoticeService(baseDirectory: FindRepoRoot());
        var docs = svc.ListDocuments();

        Assert.Equal("README.txt", docs[0].DisplayName);   // 색인 최상단
        foreach (var required in new[] { "README.txt", "FFmpeg-README.txt", "FFmpeg-COPYING.GPLv3.txt" })
            Assert.Contains(required, docs.Select(d => d.DisplayName));

        foreach (var doc in docs)
        {
            var result = svc.ReadText(doc);
            Assert.True(result.IsSuccess, $"{doc.DisplayName} 을 읽지 못했다: {result.ErrorMessage}");
            // 인코딩 오판(CP949로 읽힘) 시 한글이 깨지므로 대체 문자(U+FFFD)가 없어야 한다.
            Assert.False(result.Text!.Contains('\uFFFD'), $"{doc.DisplayName} 의 인코딩이 깨졌다");
        }

        var index = svc.ReadText(docs.First(d => d.DisplayName == "README.txt")).Text!;
        Assert.Contains("오픈소스 라이선스", index);       // 한글이 온전하다

        var gpl = svc.ReadText(docs.First(d => d.DisplayName == "FFmpeg-COPYING.GPLv3.txt")).Text!;
        Assert.Contains("GNU GENERAL PUBLIC LICENSE", gpl);
        Assert.True(gpl.Split('\n').Length > 600, "GPLv3 전문이 아니라 축약본으로 보인다");
    }
}
