using System.IO;
using System.Linq;
using MCPhoto.App.ViewModels;
using MCPhoto.Core.Frames;
using MCPhoto.Core.Models;

namespace MCPhoto.Tests;

/// <summary>
/// it27 회귀 잠금 — 앱 경로(<c>{exe}\Frame</c>) 사용의 <b>부재</b>와 <c>bundle:</c> 출처 범주의
/// <b>보존</b>을 함께 단정한다. 두 계약이 서로 반대 방향이라 같은 파일에 모아 둔다.
/// <para>
/// ① <b>부재 단정</b>(T1~T5): 앱 경로를 읽는 표면이 되살아나는 것을 막는다. 리포 관례를 따른 정적 검증
/// (소스·csproj 스캔 + 리플렉션)이며, <c>{exe}\Frame</c>에 실제 파일을 만드는 테스트는 만들지 않는다 —
/// 테스트 실행 폴더를 오염시키고 병렬 실행에서 서로를 깨뜨린다.
/// </para>
/// <para>
/// ② ⭐ <b>폐기 보존 계약</b>(T6·T7, 설계 it27 §4.2): <c>bundle:</c> 프레임을 <b>만드는</b> 코드는
/// 제거했지만 <b>판정하는</b> 코드는 남긴다. 판정을 지우면 그 id가 <c>DbDefault</c>로 오분류되어
/// <c>FrameEditPolicy.CanDelete</c>가 power에게 삭제를 허용하는 <b>fail-closed → fail-open 반전</b>이
/// 일어나고, 같은 반전이 삭제 ✕ 컨버터 · <c>IsDeletable</c> · <c>ConfirmDelete</c> ·
/// <c>FrameCatalogService.DbIdsOf</c>에서 연쇄로 일어난다. <b>"안 쓰니까 지우자"는 정리로 이 판정을
/// 뒤집을 수 없다</b>는 것이 이 두 단정의 존재 이유다.
/// </para>
/// </summary>
public class AppPathFrameRemovalTests
{
    /// <summary>리포 루트 탐색(InstallerScriptTests와 같은 방식 — 상위로 올라가며 마커 파일 탐색).</summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MCPhoto.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("MCPhoto.sln 을 찾지 못함");
    }

    /// <summary>
    /// 소스 파일 원문. 파일이 없으면 <b>스킵이 아니라 실패</b>다 — 파일 이동·개명을 못 보고 지나가면
    /// 이 파일의 부재 단정 전부가 조용히 무동작이 된다.
    /// </summary>
    private static string SourceText(params string[] relativeParts)
    {
        var path = Path.Combine(new[] { FindRepoRoot() }.Concat(relativeParts).ToArray());
        Assert.True(File.Exists(path), $"소스 파일을 찾지 못함(이동·개명 확인 필요): {path}");
        return File.ReadAllText(path);
    }

    private static FrameTemplate BundleFrame() => new() { Id = "bundle:classic", IsDefault = true };

    /// <summary>
    /// 주석·XML 문서 주석을 걷어낸 소스(식별자 부재 판정용). 폐기 주석이 사라진 심볼 이름을 <b>의도적으로</b>
    /// 언급하므로(설계 it27 §4.7 동결 문구), 원문 <c>Contains</c>로 판정하면 그 주석을 오탐한다.
    /// </summary>
    private static string CodeLinesOf(params string[] relativeParts)
    {
        var lines = SourceText(relativeParts)
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l =>
            {
                var t = l.TrimStart();
                return !(t.StartsWith("//", StringComparison.Ordinal)
                         || t.StartsWith("///", StringComparison.Ordinal)
                         || t.StartsWith("*", StringComparison.Ordinal));
            });
        return string.Join('\n', lines);
    }

    // ── ① 부재 단정 (T1~T5) ──

    /// <summary>
    /// T1: <see cref="LocalFrameStore"/>의 루트는 <b>하나</b>다 — public ctor가 1개이고 매개변수도 1개다.
    /// 리플렉션으로 보므로 기본값이 붙은 선택 매개변수(<c>legacyReadRoot = null</c>)의 부활도 잡아낸다
    /// (소스 스캔보다 강한 판정 — 이름을 바꿔 되살려도 걸린다).
    /// </summary>
    [Fact]
    public void LocalFrameStore_Has_Single_Root_Constructor()
    {
        var ctors = typeof(LocalFrameStore).GetConstructors();

        var ctor = Assert.Single(ctors);
        Assert.Single(ctor.GetParameters());
    }

    /// <summary>T4: 합성 루트가 앱 경로를 보조 루트로 다시 꽂지 않았다.</summary>
    [Fact]
    public void ServiceRegistration_Has_No_Legacy_Read_Root()
    {
        var code = CodeLinesOf("src", "MCPhoto.App", "ServiceRegistration.cs");

        Assert.DoesNotContain("legacyReadRoot", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// T5: 배포물에 번들 프레임을 담는 복사 항목이 없다.
    /// <para>
    /// ⚠️ XML 주석(<c>&lt;!-- --&gt;</c>)을 걷어낸 뒤 판정한다 — 폐기 주석이 "Frame"을 언급하므로
    /// 원문 <c>Contains</c>는 오탐한다. ⛔ 라이선스·ffmpeg·branding 복사 항목은 이 단정의 대상이 아니다
    /// (그쪽은 <c>LicenseComplianceTests</c>가 감시한다).
    /// </para>
    /// </summary>
    [Fact]
    public void Csproj_Does_Not_Copy_Bundle_Frames()
    {
        var raw = SourceText("src", "MCPhoto.App", "MCPhoto.App.csproj");
        var directives = System.Text.RegularExpressions.Regex.Replace(
            raw, "<!--.*?-->", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline);

        Assert.DoesNotContain(@"Frame\**", directives, StringComparison.Ordinal);
        Assert.DoesNotContain(@"Link=""Frame\", directives, StringComparison.Ordinal);
    }

    /// <summary>
    /// T2: 번들 폴더 스캔 경로가 되살아나지 않았다 — <c>BundleFolder</c>·<c>LoadBundleFrames</c> 식별자가
    /// 코드에 없다(폐기 주석의 언급은 제외).
    /// </summary>
    [Fact]
    public void FrameCatalogService_Has_No_Bundle_Scan()
    {
        var code = CodeLinesOf("src", "MCPhoto.App", "Services", "FrameCatalogService.cs");

        Assert.DoesNotContain("BundleFolder", code, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadBundleFrames", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⭐ T3: 프레임 카탈로그가 <b>앱 실행 경로를 전혀 접촉하지 않는다</b>. <c>AppContext.BaseDirectory</c>
    /// 부재로 판정하므로 폴더 이름을 바꿔 우회하는 재발까지 함께 막는다(가장 넓게 막는 단정).
    /// </summary>
    [Fact]
    public void FrameCatalogService_Never_Touches_App_Base_Directory()
    {
        var code = CodeLinesOf("src", "MCPhoto.App", "Services", "FrameCatalogService.cs");

        Assert.DoesNotContain("AppContext.BaseDirectory", code, StringComparison.Ordinal);
    }

    // ── ② 폐기 보존 계약 (T6·T7) ──

    /// <summary>
    /// T6: <c>bundle:</c> 접두 상수와 그 판정이 <b>살아 있다</b>. 리터럴 스캔 + 실제 판정 실행 둘 다 본다 —
    /// 상수만 남고 <c>Classify</c>에서 분기가 빠지는 절반의 제거도 잡아낸다.
    /// </summary>
    [Fact]
    public void Bundle_Origin_Category_Is_Preserved()
    {
        var source = SourceText("src", "MCPhoto.Core", "Frames", "FrameOrigin.cs");
        Assert.Contains("\"bundle:\"", source, StringComparison.Ordinal);

        Assert.Equal(FrameOriginKind.Bundle, FrameOrigin.Classify(BundleFrame()));
    }

    /// <summary>
    /// T7: <c>bundle:</c>은 <b>누구도 삭제할 수 없다</b>(최고 권한인 Admin도). 판정이 사라지면 이 단정이
    /// 먼저 깨지므로, 여기서 실패하면 "정리"가 권한을 완화했다는 신호다.
    /// </summary>
    [Fact]
    public void Bundle_Frame_Stays_Undeletable_For_Every_Role()
    {
        Assert.False(FrameEditPolicy.CanDelete(BundleFrame(), UserRole.Admin));
        Assert.False(FrameSelectViewModel.IsDeletable(BundleFrame()));
    }
}
