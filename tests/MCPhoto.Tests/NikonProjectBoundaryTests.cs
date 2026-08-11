using System.IO;
using System.Linq;
using System.Reflection;
using MCPhoto.Devices.Nikon;

namespace MCPhoto.Tests;

/// <summary>
/// it23 Step 5 경계 규약 고정(설계 §3.1·§16 Step 5 완료 기준).
/// <para>
/// 이 파일이 지키는 것: <b>SDK 수정 지점이 1파일로 수렴한다</b>는 설계의 핵. 어댑터 프로젝트에
/// OpenCvSharp·WPF·SDK 참조가 스며들면 (a) 어셈블리 전체가 SDK 유무에 인질로 잡히고,
/// (b) 라이선스 문제 시 프로젝트째 제외하는 탈출구가 막힌다.
/// 규약 위반은 컴파일되므로 사람 리뷰로만 막히는데, 그건 시간이 지나면 실패한다.
/// </para>
/// </summary>
public class NikonProjectBoundaryTests
{
    /// <summary>레포 루트를 테스트 실행 위치에서 거슬러 찾는다(다른 XAML 테스트와 동일 방식).</summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MCPhoto.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string NikonCsprojText()
        => File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "MCPhoto.Devices.Nikon", "MCPhoto.Devices.Nikon.csproj"));

    /// <summary>
    /// XML 주석을 제거한 csproj 본문. 이 프로젝트의 csproj 주석은 "왜 OpenCvSharp을 참조하지 않는가"를
    /// 설명하므로, 주석을 지우지 않으면 규약 검사가 자기 자신의 설명문에 걸린다.
    /// </summary>
    private static string NikonCsprojBody()
        => System.Text.RegularExpressions.Regex.Replace(
            NikonCsprojText(), @"<!--.*?-->", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline);

    [Fact]
    public void Nikon_Project_Does_Not_Reference_OpenCv_Wpf_Or_Sdk()
    {
        var body = NikonCsprojBody();

        // 이미지 처리는 MCPhoto.Capture, UI는 App의 몫이다 — 어댑터는 bytes만 다룬다.
        Assert.DoesNotContain("OpenCvSharp", body);
        Assert.DoesNotContain("<UseWPF>", body);
        Assert.DoesNotContain("UseWindowsForms", body);

        // SDK/래퍼 참조(PackageReference·Reference·COMReference)는 shim 실구현(Step S-A)에서만 추가된다.
        // 자기 프로젝트 이름은 제외하고 판정한다.
        var withoutSelfName = body.Replace("MCPhoto.Devices.Nikon", string.Empty);
        Assert.DoesNotContain("Nikon", withoutSelfName);
    }

    [Fact]
    public void Nikon_Project_References_Only_Core()
    {
        var text = NikonCsprojText();
        var projectRefs = System.Text.RegularExpressions.Regex
            .Matches(text, @"ProjectReference\s+Include=""([^""]+)""")
            .Select(m => Path.GetFileName(m.Groups[1].Value))
            .ToArray();

        Assert.Equal(new[] { "MCPhoto.Core.csproj" }, projectRefs);
    }

    [Fact]
    public void Nikon_Assembly_Does_Not_Reference_OpenCv_Or_Presentation()
    {
        // csproj 텍스트만으로는 전이 참조를 못 잡는다 — 실제 어셈블리 참조 목록도 확인한다.
        var referenced = typeof(NikonExternalCamera).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain("OpenCvSharp", referenced);
        Assert.DoesNotContain("PresentationFramework", referenced);
        Assert.DoesNotContain("PresentationCore", referenced);
        Assert.DoesNotContain("WindowsBase", referenced);
    }

    /// <summary>
    /// <c>NikonSdkShim.cs</c>는 <b>지금 존재하지 않아야 한다</b>(설계 §3.4).
    /// 부재가 "SDK 미착수" 신호이기 때문이다 — 빈 껍데기가 있으면 §15 체크리스트의
    /// "파일 생성부터 시작"이라는 명확한 출발점이 사라지고, 미구현 파일이 구현된 것처럼 보인다.
    /// SDK가 도착해 이 파일을 만들면 이 테스트를 함께 지운다(Step S-A).
    /// </summary>
    [Fact]
    public void Real_Sdk_Shim_File_Is_Absent_Until_Sdk_Arrives()
    {
        var path = Path.Combine(FindRepoRoot(), "src", "MCPhoto.Devices.Nikon", "NikonSdkShim.cs");
        Assert.False(File.Exists(path),
            "NikonSdkShim.cs가 존재한다. SDK 실구현을 시작했다면 이 테스트를 삭제하고 "
            + "ServiceRegistration의 shim 등록도 함께 교체했는지 확인하라(설계 §15-C4).");
    }

    /// <summary>
    /// MAID API 이름이 shim 실구현 밖으로 새지 않았는지 정적 검사(설계 §0.2·§3.4).
    /// 지금은 shim 실구현이 없으므로 <b>어디에도</b> 나타나면 안 된다.
    /// </summary>
    [Fact]
    public void Maid_Api_Names_Do_Not_Leak_Into_Contracts()
    {
        var dir = Path.Combine(FindRepoRoot(), "src", "MCPhoto.Devices.Nikon");
        var forbidden = new[] { "NkMAID", "kNkMAIDCapability", "NikonManager", "NikonDevice", "eNkMAID" };

        foreach (var file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            foreach (var name in forbidden)
            {
                Assert.False(text.Contains(name, StringComparison.Ordinal),
                    $"{Path.GetFileName(file)} 에 SDK 이름 '{name}'이 등장한다. "
                    + "SDK 이름은 NikonSdkShim.cs(미작성) 안에서만 허용된다.");
            }
        }
    }

    /// <summary>
    /// 어댑터가 노출하는 공개 표면이 계약 + shim 배선뿐임을 고정한다.
    /// 공개 타입이 늘어나면 App이 어댑터 내부에 의존할 길이 생기고, 그러면 프로젝트 교체 탈출구가 막힌다.
    /// </summary>
    [Fact]
    public void Nikon_Assembly_Public_Surface_Is_Minimal()
    {
        var publicTypes = typeof(NikonExternalCamera).Assembly
            .GetExportedTypes()
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                nameof(INikonSdkShim),
                nameof(MissingNikonSdkShim),
                nameof(NikonCameraReasons),
                nameof(NikonExternalCamera),
                nameof(SdkRuntimeProbe),
            },
            publicTypes);
    }
}
