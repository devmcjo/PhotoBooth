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
    /// GPLv3 전문은 **1바이트도 수정하지 않는다**(원문이어야 효력이 있다). it24의 서식 통일 대상에서
    /// 제외했음을 내용 자체로 잠근다 — 줄바꿈 정리·머리말 추가·78열 재접기가 들어가면 전부 실패한다.
    /// <para>
    /// 왜 별도 테스트인가: <see cref="GplV3_Full_Text_Is_Bundled"/>는 "요약본으로 대체"만 막는다.
    /// 여기서는 <b>원문의 물리적 형태</b>(줄 수·CRLF 부재·선두 공백 정렬·자체 SPDX 줄 부재)를 고정한다.
    /// </para>
    /// </summary>
    [Fact]
    public void GplV3_Full_Text_Is_Verbatim_And_Untouched_By_Formatting_Rules()
    {
        var path = Path.Combine(LicensesDir, "FFmpeg-COPYING.GPLv3.txt");
        Assert.True(File.Exists(path));
        var bytes = File.ReadAllBytes(path);

        // BOM 부착 금지(원문에 없다).
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
            "GPLv3 전문에 BOM이 붙었다 — 원문을 수정한 것이다");

        var text = File.ReadAllText(path);

        // gnu.org 원문은 674줄(= 개행 674개, 파일 끝 개행 포함)이다.
        // 서식 규약(78열 재접기 등)을 적용하면 이 값이 달라진다.
        Assert.Equal(674, text.Count(c => c == '\n'));
        Assert.EndsWith("\n", text, StringComparison.Ordinal);

        // 원문은 LF이며 CRLF가 아니다 — it24의 CRLF 규약을 이 파일에 적용하지 않았음을 고정한다.
        Assert.DoesNotContain("\r\n", text);

        // 원문의 중앙 정렬 선두 공백(제목 들여쓰기)이 살아 있어야 한다.
        Assert.StartsWith("                    GNU GENERAL PUBLIC LICENSE", text, StringComparison.Ordinal);

        // 고지 txt에는 SPDX 줄을 넣지만 **전문에는 넣지 않는다**(원문에 없는 줄을 추가하면 수정이다).
        Assert.DoesNotContain("SPDX-License-Identifier:", text);
    }

    /// <summary>
    /// O2·O3: ffmpeg 고지에 버전·빌드 configuration·소스 위치·3년 서면 오퍼가 모두 있어야 한다.
    /// 특히 **대응 소스 접근 제공**(§6)이 가장 빠뜨리기 쉬운 항목이다.
    /// it24에서 파일명이 <c>FFmpeg-README.txt</c> → <c>FFmpeg-NOTICE.txt</c>로 개명됐다(내용 항목은 보존).
    /// </summary>
    [Fact]
    public void Ffmpeg_Notice_Has_Version_Config_Source_And_Written_Offer()
    {
        var text = ReadLicenseFile("FFmpeg-NOTICE.txt");

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

        // it24 T-C1: 업계 관례(SPDX 짧은 식별자)로 라이선스를 지목한다 — 전문을 중복 재현하지 않는 표준 수단.
        Assert.Contains("SPDX-License-Identifier: GPL-3.0-or-later", text);

        // it24 X7 정정: 종전 문안은 "설치 폴더의 LICENSE 파일 참조"라고 안내했는데 배포물의 실제
        // 파일명은 MCPhoto-LICENSE-MIT.txt였다 — 안내가 거짓이었다. 두 방향으로 잠근다.
        Assert.Contains("MCPhoto-LICENSE-MIT.txt", text);
        Assert.DoesNotContain("설치 폴더의 LICENSE", text);

        // it24 X1: 80열 ASCII 벽을 없앤 서식 규약. 되돌아오면 실패한다.
        Assert.DoesNotContain(new string('=', 80), text);
        Assert.DoesNotContain(new string('-', 80), text);
    }

    /// <summary>
    /// 고지 인덱스가 있어야 폴더를 직접 연 사람이 무엇이 왜 들어 있는지 안다.
    /// it24에서 <c>README.txt</c> → <c>NOTICE.txt</c>로 개명했다(<c>NOTICE</c>가 배포물 고지의 통용 이름).
    /// </summary>
    [Fact]
    public void License_Index_Lists_Ffmpeg_And_Keeps_Mcphoto_Mit()
    {
        var text = ReadLicenseFile("NOTICE.txt");

        Assert.Contains("FFmpeg", text);
        Assert.Contains("GPL", text);
        Assert.Contains("MIT", text);          // MCPhoto 본체 라이선스가 유지됨을 밝힌다
        Assert.Contains("FFmpeg-NOTICE.txt", text);

        // 안내가 가리키는 파일이 실제로 배포물에 실려야 한다 — 없으면 거짓 안내가 된다.
        Assert.Contains("MCPhoto-LICENSE-MIT.txt", text);

        // it24 X3: 두 컴포넌트를 SPDX 식별자로 지목한다.
        Assert.Contains("SPDX-License-Identifier: MIT", text);
        Assert.Contains("SPDX-License-Identifier: GPL-3.0-or-later", text);

        // it24 X9: 상용 고지에서 추정 표현은 신뢰를 떨어뜨린다(종전 "대부분 MIT/Apache-2.0 등").
        Assert.DoesNotContain("대부분", text);

        // it24 X1: 80열 ASCII 벽 폐지.
        Assert.DoesNotContain(new string('=', 80), text);
        Assert.DoesNotContain(new string('-', 80), text);
    }

    /// <summary>
    /// it24 X8: 상용 고지 문서에 통상 있는 항목 — 고지 기준일과 문의 창구의 역할 구분.
    /// 기준일은 요약 메타데이터의 <c>updatedOn</c>과 같아야 한다(T-M4가 그 정합을 잠근다).
    /// </summary>
    [Fact]
    public void Notice_Documents_Have_As_Of_Date_And_Contact_Split()
    {
        var index = ReadLicenseFile("NOTICE.txt");
        var ffmpeg = ReadLicenseFile("FFmpeg-NOTICE.txt");

        Assert.Matches(@"이 고지의 기준일: \d{4}-\d{2}-\d{2}", index);
        Assert.Matches(@"이 고지의 기준일: \d{4}-\d{2}-\d{2}", ffmpeg);

        // 소스 코드 요청과 일반 문의를 구분해 안내한다(메일 제목 예시 포함).
        Assert.Contains("MCPhoto FFmpeg source request", index);
        Assert.Contains("MCPhoto license notice inquiry", index);
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

        // it24: 개명 + 요약 메타데이터 추가. 개명 누락으로 고지가 배포물에서 사라지는 것을 막는 첫 번째 그물이며,
        //       두 번째 그물은 출력 폴더를 검사하는 Manifest_Declares_Files_That_Actually_Ship 다.
        foreach (var required in new[]
                 {
                     "FFmpeg-COPYING.GPLv3.txt", "FFmpeg-NOTICE.txt", "NOTICE.txt", "notice-manifest.json",
                 })
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
    /// C-T3: 열거 정렬 규약 — 색인(NOTICE.txt) 최상단, 나머지 이름 오름차순, 하위 폴더 포함, 비-txt 제외.
    /// 하드코딩 목록을 쓰지 않는 것이 요점이다(폴더에 파일을 넣으면 배포되고 목록에도 나와야 한다).
    /// ⚠️ <c>notice-manifest.json</c>이 목록에 섞이지 않는 것도 함께 고정한다 — 기계용 파일이 전문 목록에
    /// 나타나면 사용자가 그것을 고지 문서로 오해한다.
    /// </summary>
    [Fact]
    public void Service_Enumerates_Txt_Recursively_With_Index_First()
    {
        var baseDir = MakeTempLicenses(
            ("ZZZ.txt", "z"),
            ("NOTICE.txt", "index"),
            ("AAA.txt", "a"),
            ("sub/Nested.txt", "n"),
            ("notice-manifest.json", "{}"),
            ("binary.png", "not text"));
        try
        {
            var docs = new LicenseNoticeService(baseDirectory: baseDir).ListDocuments();

            Assert.Equal(new[] { "NOTICE.txt", "AAA.txt", "sub/Nested.txt", "ZZZ.txt" },
                docs.Select(d => d.DisplayName).ToArray());
            Assert.DoesNotContain("binary.png", docs.Select(d => d.DisplayName));
            Assert.DoesNotContain("notice-manifest.json", docs.Select(d => d.DisplayName));
            Assert.All(docs, d => Assert.True(d.SizeBytes > 0));
        }
        finally { Cleanup(baseDir); }
    }

    /// <summary>C-T4: UTF-8 no BOM 한글 파일이 온전히 읽히고 CRLF가 보존된다("그대로 노출" 요구).</summary>
    [Fact]
    public void Service_Reads_Utf8_Korean_And_Preserves_Crlf()
    {
        var baseDir = MakeTempLicenses(("NOTICE.txt", "오픈소스 라이선스\r\n둘째 줄\ttab"));
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
        File.WriteAllText(Path.Combine(dir, "NOTICE.txt"), "GNU 전문",
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

        Assert.Equal("NOTICE.txt", docs[0].DisplayName);   // 색인 최상단
        foreach (var required in new[] { "NOTICE.txt", "FFmpeg-NOTICE.txt", "FFmpeg-COPYING.GPLv3.txt" })
            Assert.Contains(required, docs.Select(d => d.DisplayName));

        foreach (var doc in docs)
        {
            var result = svc.ReadText(doc);
            Assert.True(result.IsSuccess, $"{doc.DisplayName} 을 읽지 못했다: {result.ErrorMessage}");
            // 인코딩 오판(CP949로 읽힘) 시 한글이 깨지므로 대체 문자(U+FFFD)가 없어야 한다.
            Assert.False(result.Text!.Contains('\uFFFD'), $"{doc.DisplayName} 의 인코딩이 깨졌다");
        }

        var index = svc.ReadText(docs.First(d => d.DisplayName == "NOTICE.txt")).Text!;
        Assert.Contains("라이선스 고지", index);           // 한글이 온전하다

        var gpl = svc.ReadText(docs.First(d => d.DisplayName == "FFmpeg-COPYING.GPLv3.txt")).Text!;
        Assert.Contains("GNU GENERAL PUBLIC LICENSE", gpl);
        Assert.True(gpl.Split('\n').Length > 600, "GPLv3 전문이 아니라 축약본으로 보인다");
    }

    /// <summary>고지 파일 인코딩 규약(UTF-8 **no BOM**) — BOM이 붙으면 한글이 깨지는 경로가 생긴다.</summary>
    [Theory]
    [InlineData("NOTICE.txt")]
    [InlineData("FFmpeg-NOTICE.txt")]
    [InlineData("notice-manifest.json")]
    public void Notice_Files_Are_Utf8_Without_Bom(string name)
    {
        var bytes = File.ReadAllBytes(Path.Combine(LicensesDir, name));
        Assert.True(bytes.Length > 0, $"{name} 이 비어 있다");
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
            $"{name} 에 BOM이 붙었다 — 리포 관례는 UTF-8 no BOM 이다");
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // it24 — 요약 메타데이터(매니페스트) 정합 (설계 §6.1·§6.2)
    //
    // 이 묶음의 존재 이유: 요약 카드가 **거짓말을 하지 않는지**를 자동으로 검증한다.
    //   ① 매니페스트 자체가 스키마·필수 필드를 지키는가          (T-M1)
    //   ② 매니페스트가 선언한 파일이 실제로 배포물에 실리는가     (T-M2, 출력 폴더 기준)
    //   ③ 배포물의 고지 문서가 모두 선언되어 있는가              (T-M3, 양방향 diff의 반대편)
    //   ④ 매니페스트의 버전·저작권·기준일이 txt와 일치하는가      (T-M4)
    // ②·③을 출력 폴더 기준으로 쓰는 이유: 리포 소스에는 MIT 전문이 없다(빌드 시 루트 LICENSE에서
    // 링크 복사). 소스 폴더만 보면 매니페스트가 없는 파일을 가리키는 것처럼 보인다.
    // ══════════════════════════════════════════════════════════════════════════════

    /// <summary>테스트 실행 폴더의 고지 폴더 = 실제 배포되는 집합(csproj가 출력에 복사한다).</summary>
    private static ILicenseNoticeService OutputFolderService() => new LicenseNoticeService();

    /// <summary>T-M1: 리포 매니페스트가 파싱되고 스키마·필수 필드·순서·SPDX 집합·본체 버전 규칙을 지킨다.</summary>
    [Fact]
    public void Manifest_Is_Valid_And_Self_Component_Comes_First()
    {
        var summary = new LicenseNoticeService(baseDirectory: FindRepoRoot()).ReadSummary();

        Assert.Null(summary.DegradedMessage);   // 리포 매니페스트가 깨져 있으면 여기서 잡힌다
        Assert.True(summary.Components.Count >= 2,
            "매니페스트에 본체 + 동봉 구성 요소가 모두 선언되어야 한다");

        // 배열 순서 = 표시 순서이며 본체가 첫 번째다("본체 → 동봉 구성요소" 읽기 순서).
        Assert.True(summary.Components[0].IsSelf, "kind:\"self\" 항목이 첫 번째여야 한다");
        Assert.Single(summary.Components, c => c.IsSelf);

        foreach (var c in summary.Components)
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Name));
            Assert.False(string.IsNullOrWhiteSpace(c.LicenseName));
            Assert.False(string.IsNullOrWhiteSpace(c.SpdxId));
            Assert.False(string.IsNullOrWhiteSpace(c.FullTextFile));
            // 알려진 SPDX 식별자만 — 오타·비표준 값은 라이선스를 잘못 지목한다.
            Assert.Contains(c.SpdxId, new[] { "MIT", "GPL-3.0-or-later" });
        }

        // M4: 본체 버전은 어셈블리 버전 리소스가 단일 소스다. 매니페스트에 적으면 릴리스마다 어긋난다.
        Assert.Null(summary.Components[0].Version);
        Assert.False(summary.Components[0].HasVersion);

        // 기준일은 화면 푸터에 노출되므로 형식을 고정한다.
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", summary.UpdatedOn ?? string.Empty);
    }

    /// <summary>
    /// T-M2: 매니페스트가 선언한 파일이 **출력 폴더에 실제로 있고 읽힌다** — 매니페스트가 거짓말을 하지 않는다.
    /// 개명·삭제로 고지가 배포물에서 사라지면 여기서 실패한다.
    /// </summary>
    [Fact]
    public void Manifest_Declares_Files_That_Actually_Ship()
    {
        var svc = OutputFolderService();
        var summary = svc.ReadSummary();

        Assert.Null(summary.DegradedMessage);
        Assert.NotEmpty(summary.Components);

        foreach (var c in summary.Components)
        {
            Assert.False(c.IsFullTextMissing,
                $"{c.Name} 의 라이선스 전문이 배포 산출물에 없다 — GPLv3 §4 위반 상태로 배포된다");
            Assert.False(c.IsNoticeMissing, $"{c.Name} 의 상세 고지가 배포 산출물에 없다");

            var full = svc.ReadText(c.FullTextFile);
            Assert.True(full.IsSuccess, $"{c.Name} 전문을 읽지 못했다: {full.ErrorMessage}");
            Assert.False(full.Text!.Contains('\uFFFD'), $"{c.Name} 전문의 인코딩이 깨졌다");

            if (c.NoticeFile is not null)
            {
                var notice = svc.ReadText(c.NoticeFile);
                Assert.True(notice.IsSuccess, $"{c.Name} 상세 고지를 읽지 못했다: {notice.ErrorMessage}");
                Assert.False(notice.Text!.Contains('\uFFFD'));
            }
        }
    }

    /// <summary>
    /// T-M3: 배포물의 모든 <c>.txt</c>가 어떤 항목에서 참조된다(미참조 0건).
    /// 새 고지를 추가하고 매니페스트를 잊으면 실패한다 — 열거만 쓰던 종전 방식에서는 탐지 자체가 불가능했다.
    /// </summary>
    [Fact]
    public void Every_Shipped_Notice_Document_Is_Declared()
    {
        var summary = OutputFolderService().ReadSummary();

        Assert.Empty(summary.UnlistedDocuments);
    }

    /// <summary>
    /// T-M4: 매니페스트와 txt의 <b>내용 정합</b>. 요약 카드와 고지 문서가 다른 말을 하면
    /// 어느 쪽이 참인지 아무도 모른다 — 중복을 없앨 수 없으니 어긋남을 여기서 잡는다.
    /// </summary>
    [Fact]
    public void Manifest_Version_Copyright_And_As_Of_Date_Match_The_Notice_Text()
    {
        var summary = new LicenseNoticeService(baseDirectory: FindRepoRoot()).ReadSummary();
        Assert.Null(summary.DegradedMessage);

        foreach (var c in summary.Components)
        {
            // 상세 고지가 없는 항목(본체)은 색인이 그 역할을 한다.
            var text = ReadLicenseFile(c.NoticeFile ?? "NOTICE.txt");

            if (c.Version is not null) Assert.Contains(c.Version, text);
            if (c.Copyright is not null) Assert.Contains(c.Copyright, text);
            Assert.Contains(c.SpdxId, text);
        }

        // 고지 기준일은 색인 txt와 매니페스트가 같은 값을 말해야 한다.
        Assert.Contains($"이 고지의 기준일: {summary.UpdatedOn}", ReadLicenseFile("NOTICE.txt"));
    }

    // ── ReadSummary 동작(정상·강등·경로 탈출) ──

    private const string ValidManifest = """
        {
          "schemaVersion": 1,
          "updatedOn": "2026-08-11",
          "components": [
            { "kind": "self", "name": "본체", "version": null, "licenseName": "MIT License",
              "spdxId": "MIT", "copyright": "(c) 2025", "purpose": "용도", "distribution": "본체",
              "sourceOffer": null, "fullTextFile": "Mit.txt", "noticeFile": null },
            { "kind": "redistributed", "name": "FFmpeg", "version": "8.1.2",
              "licenseName": "GNU General Public License v3.0 or later", "spdxId": "GPL-3.0-or-later",
              "copyright": "(c) 2000-2026", "purpose": "인코딩", "distribution": "무수정 재배포",
              "sourceOffer": "제6조", "fullTextFile": "Gpl.txt", "noticeFile": "Ffmpeg.txt" }
          ]
        }
        """;

    /// <summary>T-S1: 정상 매니페스트 — 순서 보존 · 부재 없음 · 강등 없음 · 미참조 0건.</summary>
    [Fact]
    public void Summary_Normal_Case_Preserves_Order_And_Reports_No_Problem()
    {
        var baseDir = MakeTempLicenses(
            ("notice-manifest.json", ValidManifest),
            ("NOTICE.txt", "색인"),
            ("Mit.txt", "MIT"),
            ("Gpl.txt", "GPL"),
            ("Ffmpeg.txt", "고지"));
        try
        {
            var summary = new LicenseNoticeService(baseDirectory: baseDir).ReadSummary();

            Assert.Null(summary.DegradedMessage);
            Assert.Equal(new[] { "본체", "FFmpeg" }, summary.Components.Select(c => c.Name).ToArray());
            Assert.True(summary.Components[0].IsSelf);
            Assert.False(summary.Components[1].IsSelf);
            Assert.All(summary.Components, c => Assert.False(c.IsAnyFileMissing));
            Assert.Empty(summary.UnlistedDocuments);   // 색인은 폴더의 목차이므로 미참조로 세지 않는다
            Assert.Equal("2026-08-11", summary.UpdatedOn);

            // Has* 계산 속성(화면의 행 표시 여부)
            Assert.False(summary.Components[0].HasVersion);
            Assert.False(summary.Components[0].HasNoticeFile);
            Assert.True(summary.Components[1].HasVersion);
            Assert.True(summary.Components[1].HasNoticeFile);
            Assert.True(summary.Components[1].HasSourceOffer);
        }
        finally { Cleanup(baseDir); }
    }

    /// <summary>T-S2: 매니페스트 파일 없음(D1) — 강등 문구 + 폴더의 문서 전부를 폴백으로 돌려준다.</summary>
    [Fact]
    public void Summary_Missing_Manifest_Degrades_But_Keeps_Documents()
    {
        var baseDir = MakeTempLicenses(("NOTICE.txt", "색인"), ("Gpl.txt", "GPL"));
        try
        {
            var summary = new LicenseNoticeService(baseDirectory: baseDir).ReadSummary();

            Assert.Equal(
                "라이선스 요약 정보를 찾을 수 없어 동봉된 고지 문서를 그대로 표시합니다. "
                + "배포 산출물이 불완전할 수 있으므로 개발자에게 알려주세요.",
                summary.DegradedMessage);
            Assert.Empty(summary.Components);
            // 전문 도달 경로가 유지되어야 한다 — 요약이 깨졌다고 전문을 못 보게 되면 법적 후퇴다.
            Assert.Equal(2, summary.UnlistedDocuments.Count);
        }
        finally { Cleanup(baseDir); }
    }

    /// <summary>T-S3: 손상·스키마 불일치·필수 필드 누락·항목 0개 — 전부 D2로 강등하고 예외를 던지지 않는다.</summary>
    [Theory]
    [InlineData("{ not json at all")]
    [InlineData("""{ "schemaVersion": 2, "components": [ { "kind": "self", "name": "x", "licenseName": "MIT", "spdxId": "MIT", "fullTextFile": "a.txt" } ] }""")]
    [InlineData("""{ "schemaVersion": 1, "components": [] }""")]
    [InlineData("""{ "schemaVersion": 1, "components": [ { "kind": "self", "name": "", "licenseName": "MIT", "spdxId": "MIT", "fullTextFile": "a.txt" } ] }""")]
    [InlineData("""{ "schemaVersion": 1, "components": [ { "kind": "unknown", "name": "x", "licenseName": "MIT", "spdxId": "MIT", "fullTextFile": "a.txt" } ] }""")]
    public void Summary_Broken_Manifest_Degrades_Without_Throwing(string manifest)
    {
        var baseDir = MakeTempLicenses(("notice-manifest.json", manifest), ("NOTICE.txt", "색인"));
        try
        {
            var summary = new LicenseNoticeService(baseDirectory: baseDir).ReadSummary();

            Assert.Equal(
                "라이선스 요약 정보를 읽을 수 없어 동봉된 고지 문서를 그대로 표시합니다. "
                + "배포 산출물이 불완전할 수 있으므로 개발자에게 알려주세요.",
                summary.DegradedMessage);
            Assert.Empty(summary.Components);
            Assert.Single(summary.UnlistedDocuments);
        }
        finally { Cleanup(baseDir); }
    }

    /// <summary>T-S4: 선언된 파일이 없으면 카드는 유지하고 부재만 표시한다(카드를 숨기면 누락을 감춘다).</summary>
    [Fact]
    public void Summary_Missing_Declared_File_Keeps_Card_And_Flags_It()
    {
        var baseDir = MakeTempLicenses(
            ("notice-manifest.json", ValidManifest),
            ("NOTICE.txt", "색인"),
            ("Mit.txt", "MIT"));
        try
        {
            var summary = new LicenseNoticeService(baseDirectory: baseDir).ReadSummary();

            Assert.Null(summary.DegradedMessage);            // 강등이 아니다 — 요약 자체는 읽혔다
            Assert.Equal(2, summary.Components.Count);       // 카드는 그대로 그려진다
            var ffmpeg = summary.Components[1];
            Assert.True(ffmpeg.IsFullTextMissing);
            Assert.True(ffmpeg.IsNoticeMissing);
            Assert.True(ffmpeg.IsAnyFileMissing);
            Assert.True(ffmpeg.HasNoticeFile);               // 버튼은 숨기지 않는다(누르면 사유가 나온다)
        }
        finally { Cleanup(baseDir); }
    }

    /// <summary>T-S5: 선언되지 않은 문서가 폴더에 있으면 그 사실이 드러난다(파일 → 매니페스트 방향).</summary>
    [Fact]
    public void Summary_Reports_Documents_That_Nobody_Declared()
    {
        var baseDir = MakeTempLicenses(
            ("notice-manifest.json", ValidManifest),
            ("NOTICE.txt", "색인"),
            ("Mit.txt", "MIT"), ("Gpl.txt", "GPL"), ("Ffmpeg.txt", "고지"),
            ("Stray-LICENSE.txt", "선언되지 않은 고지"));
        try
        {
            var summary = new LicenseNoticeService(baseDirectory: baseDir).ReadSummary();

            Assert.Null(summary.DegradedMessage);
            Assert.Equal(2, summary.Components.Count);
            Assert.Equal(new[] { "Stray-LICENSE.txt" },
                summary.UnlistedDocuments.Select(d => d.DisplayName).ToArray());
        }
        finally { Cleanup(baseDir); }
    }

    /// <summary>
    /// T-S6: M5 경로 탈출 차단. 매니페스트는 배포물의 데이터이므로 경로 탈출을 허용하면 이 화면이
    /// **임의 파일 리더**가 된다. 참조는 무효로 강등되고 폴더 밖 파일은 읽히지 않는다.
    /// </summary>
    [Theory]
    [InlineData(@"..\\..\\secret.txt")]
    [InlineData("sub/Nested.txt")]
    [InlineData(@"sub\\Nested.txt")]
    [InlineData(@"C:\\Windows\\win.ini")]
    public void Summary_Rejects_Path_Escape_In_Declared_File(string declared)
    {
        var manifest = $$"""
            {
              "schemaVersion": 1,
              "components": [
                { "kind": "self", "name": "본체", "licenseName": "MIT License", "spdxId": "MIT",
                  "fullTextFile": "{{declared}}" }
              ]
            }
            """;
        var baseDir = MakeTempLicenses(
            ("notice-manifest.json", manifest),
            ("NOTICE.txt", "색인"),
            ("sub/Nested.txt", "하위 폴더도 금지다(파일명만 허용)"));
        // 탈출 대상(고지 폴더의 부모)에 실제 파일을 둬서 "읽히면 실패"를 관측 가능하게 만든다.
        File.WriteAllText(Path.Combine(baseDir, "secret.txt"), "TOP SECRET");
        try
        {
            var svc = new LicenseNoticeService(baseDirectory: baseDir);
            var summary = svc.ReadSummary();

            Assert.Null(summary.DegradedMessage);
            Assert.True(summary.Components[0].IsFullTextMissing, $"'{declared}' 참조가 유효로 취급됐다");

            var read = svc.ReadText(declared);
            Assert.False(read.IsSuccess);
            Assert.DoesNotContain("TOP SECRET", read.Text ?? string.Empty);
        }
        finally { Cleanup(baseDir); }
    }

    /// <summary>T-S7: 이름으로 읽기 — 정상은 본문, 부재는 예외가 아니라 F3 문구.</summary>
    [Fact]
    public void ReadText_By_FileName_Returns_Body_Or_Message()
    {
        var baseDir = MakeTempLicenses(("NOTICE.txt", "색인 본문\r\n둘째 줄"));
        try
        {
            var svc = new LicenseNoticeService(baseDirectory: baseDir);

            var ok = svc.ReadText("NOTICE.txt");
            Assert.True(ok.IsSuccess);
            Assert.Equal("색인 본문\r\n둘째 줄", ok.Text);

            var missing = svc.ReadText("NoSuchFile.txt");
            Assert.False(missing.IsSuccess);
            Assert.Equal("이 파일을 읽을 수 없습니다. 파일이 사용 중이거나 접근 권한이 없습니다.",
                missing.ErrorMessage);
        }
        finally { Cleanup(baseDir); }
    }

    /// <summary>
    /// T-S8·T-S9: 사람이 손으로 편집하는 파일이므로 주석·후행 콤마를 허용하고, 빈 문자열은 null로 정규화한다.
    /// </summary>
    [Fact]
    public void Summary_Allows_Comments_Trailing_Commas_And_Normalizes_Blank_Fields()
    {
        const string manifest = """
            // 사람이 읽는 주석
            {
              "schemaVersion": 1,
              "updatedOn": "   ",
              "components": [
                {
                  /* 항목 설명 */
                  "kind": "self", "name": "본체", "licenseName": "MIT License", "spdxId": "MIT",
                  "copyright": "", "purpose": "  ", "distribution": "", "sourceOffer": "",
                  "fullTextFile": "Mit.txt", "noticeFile": "",
                },
              ],
            }
            """;
        var baseDir = MakeTempLicenses(("notice-manifest.json", manifest), ("Mit.txt", "MIT"));
        try
        {
            var summary = new LicenseNoticeService(baseDirectory: baseDir).ReadSummary();

            Assert.Null(summary.DegradedMessage);
            var c = Assert.Single(summary.Components);
            Assert.Null(c.Copyright);
            Assert.Null(c.Purpose);
            Assert.Null(c.Distribution);
            Assert.Null(c.SourceOffer);
            Assert.Null(c.NoticeFile);
            Assert.Null(summary.UpdatedOn);
            Assert.False(c.HasCopyright);
            Assert.False(c.HasNoticeFile);
            Assert.False(c.IsNoticeMissing);   // 선언되지 않은 파일은 "부재"가 아니다
        }
        finally { Cleanup(baseDir); }
    }

    /// <summary>고지 폴더가 아예 없으면 요약도 강등되고 폴더를 만들지 않는다(누락 은폐 금지 원칙 승계).</summary>
    [Fact]
    public void Summary_Does_Not_Create_Folder_When_Missing()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"mcphoto_lic_{Guid.NewGuid():N}");
        var svc = new LicenseNoticeService(baseDirectory: baseDir);

        var summary = svc.ReadSummary();

        Assert.NotNull(summary.DegradedMessage);
        Assert.Empty(summary.Components);
        Assert.Empty(summary.UnlistedDocuments);
        Assert.False(Directory.Exists(svc.FolderPath), "없는 라이선스 폴더를 생성하면 누락을 은폐한다");
    }
}
