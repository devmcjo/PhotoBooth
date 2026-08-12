using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace MCPhoto.Tests;

/// <summary>
/// it26 §3.8 T31·T32 — 인스톨러 스크립트 정적 검증(Inno Setup 컴파일 없이).
/// <para>
/// ⛔ 가장 중요한 단정은 <b>부재</b>다: 제거가 <c>result\</c>(손님 사진)를 지우는 행이 <b>없어야</b> 한다.
/// 로컬 사본은 QR 전송과 독립이라 서버에 없을 수도 있어 유일 사본일 수 있고, 제거로 지우면 복구가 불가능하다.
/// </para>
/// </summary>
public class InstallerScriptTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "installer", "MCPhoto.iss"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("installer/MCPhoto.iss 를 찾지 못함");
    }

    private static string ScriptText()
        => File.ReadAllText(Path.Combine(FindRepoRoot(), "installer", "MCPhoto.iss"));

    /// <summary>
    /// 지정 섹션의 <b>지시 줄만</b> 돌려준다(주석 `;` 줄 제외). 주석에는 경로 문자열이 설명 목적으로
    /// 들어 있으므로, 문자열 포함 검사로 부재를 판정하면 오탐한다.
    /// </summary>
    private static string[] SectionDirectiveLines(string section)
    {
        var lines = ScriptText().Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        var start = Array.FindIndex(lines, l => l.Trim().Equals($"[{section}]", StringComparison.OrdinalIgnoreCase));
        Assert.True(start >= 0, $"[{section}] 섹션을 찾지 못함");

        var body = new List<string>();
        for (int i = start + 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith('[') && line.EndsWith(']')) break;      // 다음 섹션
            if (line.Length == 0 || line.StartsWith(';')) continue;     // 빈 줄·주석
            body.Add(line);
        }
        return body.ToArray();
    }

    [Fact]
    public void Dirs_Creates_Writable_Result_And_Frame_Folders()
    {
        // 상속에 의존하지 않고 명시 생성한다 — 비승격 첫 실행이 폴더 생성부터 실패하면 손님 사진이
        // **조용히** 저장되지 않는다(예외 대신 null 반환).
        var dirs = SectionDirectiveLines("Dirs");

        foreach (var name in new[] { @"{commonappdata}\MCPhoto\result", @"{commonappdata}\MCPhoto\Frame" })
        {
            var row = dirs.SingleOrDefault(l => l.Contains($"Name: \"{name}\"", StringComparison.Ordinal));
            Assert.False(row is null, $"[Dirs] 에 {name} 행이 없다");
            Assert.Contains("users-modify", row!);
        }
    }

    [Fact]
    public void UninstallDelete_Never_Removes_Guest_Photos()
    {
        // ⛔ 이 테스트가 지키는 것: 제거가 손님 사진을 지우지 않는다(구 위치·신 위치 모두).
        var rows = SectionDirectiveLines("UninstallDelete");

        foreach (var forbidden in new[] { @"{app}\result", @"{commonappdata}\MCPhoto\result" })
        {
            var offending = rows.Where(l => Regex.IsMatch(l,
                @"Name:\s*""" + Regex.Escape(forbidden) + @"(\\\*)?""", RegexOptions.IgnoreCase)).ToArray();
            Assert.True(offending.Length == 0,
                $"[UninstallDelete] 에 {forbidden} 삭제 행이 있다(손님 사진 유실): {string.Join(" | ", offending)}");
        }

        // 보존 장치: 두 루트는 dirifempty 로만 정리된다(비어 있지 않으면 남는다).
        Assert.Contains(rows, l => l.StartsWith("Type: dirifempty", StringComparison.Ordinal) && l.Contains(@"{app}"));
        Assert.Contains(rows, l => l.StartsWith("Type: dirifempty", StringComparison.Ordinal)
                                   && l.Contains(@"{commonappdata}\MCPhoto"));
    }

    [Fact]
    public void UninstallDelete_Removes_Frame_Caches()
    {
        // 캐시는 지운다(서버에서 재취득 가능) — 구 위치·신 위치 둘 다.
        var rows = SectionDirectiveLines("UninstallDelete");

        Assert.Contains(rows, l => l.Contains(@"Name: ""{app}\Frame""", StringComparison.Ordinal));
        Assert.Contains(rows, l => l.Contains(@"Name: ""{commonappdata}\MCPhoto\Frame""", StringComparison.Ordinal));
    }

    [Fact]
    public void Files_Whitelist_Still_Excludes_Runtime_Artifacts()
    {
        // [Files] 는 화이트리스트다 — Frame\·result\·MCPhoto.ini 를 담지 않는다(실행 흔적 유출 방지).
        var files = SectionDirectiveLines("Files");

        Assert.Equal(3, files.Count(l => l.StartsWith("Source:", StringComparison.Ordinal)));
        Assert.DoesNotContain(files, l => l.Contains(@"DestDir: ""{app}\Frame""", StringComparison.Ordinal));
        Assert.DoesNotContain(files, l => l.Contains("MCPhoto.ini", StringComparison.Ordinal));
        Assert.DoesNotContain(files, l => l.Contains(@"result", StringComparison.Ordinal));
    }
}
