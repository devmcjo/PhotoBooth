using System;
using System.IO;
using MCPhoto.Core.Settings;
using Xunit;

namespace MCPhoto.Tests;

/// <summary>
/// it23 §B4: <c>IniSettingsService.Save()</c>가 <b>외래 섹션</b>(<c>[Test]</c> 등)을 보존하는지 고정한다.
///
/// 원래 결함: <c>Save()</c>는 빈 <see cref="IniFile"/>에 <c>[MCPhoto]</c>만 채워 파일을 통째로 덮어썼고,
/// <c>MainWindow.OnClosing</c>이 <b>앱 종료마다 무조건</b> 그것을 호출한다 → 사람이 손으로 넣은 <c>[Test]</c>
/// 섹션이 **첫 종료에 사라졌다.** 사용자 관점에서는 "테스트 모드로 잘 됐는데 다시 켜니 게스트다"이고
/// 원인 추적이 매우 어렵다. 이 결함을 고치지 않으면 B부 기능은 "한 번만 동작하는 기능"이다.
///
/// ⚠️ 소유 경계는 **섹션 단위**다: <c>[MCPhoto]</c>의 미매핑 키는 여전히 사라져야 한다(B-T4).
///    키 단위로 되살리면 오탈자 키(<c>Cutcount=8</c>)와 폐기된 키가 영구히 남는다.
/// </summary>
public class IniForeignSectionTests : IDisposable
{
    private readonly string _dir;

    public IniForeignSectionTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"mcphoto_ini_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* 임시 폴더 정리 실패는 테스트 결과와 무관 */ }
    }

    private string PathFor(string name) => Path.Combine(_dir, name);

    /// <summary>후보 목록을 명시 주입한다 — 폴백이 실제 머신 경로(ProgramData·LocalAppData)로 새지 않게.</summary>
    private static IniSettingsService Service(string iniPath, params string[] fallbacks)
        => new(iniPath: iniPath, fallbackCandidates: fallbacks.Length > 0 ? fallbacks : new[] { iniPath });

    private const string TestSection = """
        [Test]
        TestMode=1
        Id=testadmin
        Email=test@email.com
        Role=admin
        Pin=1234
        """;

    /// <summary>B-T1: [Test]가 있는 ini → Load() → Save() → 다시 읽기. 모든 키·값이 그대로 존재한다.</summary>
    [Fact]
    public void T1_Save_Preserves_Test_Section()
    {
        var path = PathFor("MCPhoto.ini");
        File.WriteAllText(path, "[MCPhoto]\nCutCount=8\n" + TestSection + "\n");

        var svc = Service(path);
        svc.Load();
        Assert.True(svc.Save());

        var reread = IniFile.Parse(File.ReadAllText(path));
        Assert.Equal("1", reread.Get("Test", "TestMode"));
        Assert.Equal("testadmin", reread.Get("Test", "Id"));
        Assert.Equal("test@email.com", reread.Get("Test", "Email"));
        Assert.Equal("admin", reread.Get("Test", "Role"));
        Assert.Equal("1234", reread.Get("Test", "Pin"));
        Assert.Equal(8, reread.GetInt("MCPhoto", "CutCount", -1));   // 소유 섹션도 정상 기록
    }

    /// <summary>B-T2: 저장 2회 연속(멱등). 두 번째 저장이 자기가 방금 쓴 [Test]를 다시 실어야 한다.</summary>
    [Fact]
    public void T2_Save_Twice_Is_Idempotent()
    {
        var path = PathFor("MCPhoto.ini");
        File.WriteAllText(path, TestSection + "\n");

        var svc = Service(path);
        svc.Load();
        Assert.True(svc.Save());
        Assert.True(svc.Save());

        var reread = IniFile.Parse(File.ReadAllText(path));
        Assert.Equal("1", reread.Get("Test", "TestMode"));
        Assert.Equal("admin", reread.Get("Test", "Role"));
    }

    /// <summary>
    /// B-T3: [Test] · [Unknown] · 선두 무명 섹션 줄 3종 전부 보존.
    /// <c>[Test]</c>만 특별 취급하지 않는 이유: 다음 섹션(<c>[Branding]</c> 등)이 추가될 때 같은 버그가 반복된다.
    /// </summary>
    [Fact]
    public void T3_Preserves_Unknown_And_Default_Sections()
    {
        var path = PathFor("MCPhoto.ini");
        File.WriteAllText(path,
            "Preamble=keep\n[MCPhoto]\nCutCount=6\n[Test]\nTestMode=1\n[Unknown]\nFoo=bar\n");

        var svc = Service(path);
        svc.Load();
        Assert.True(svc.Save());

        var reread = IniFile.Parse(File.ReadAllText(path));
        Assert.Equal("keep", reread.Get("", "Preamble"));
        Assert.Equal("1", reread.Get("Test", "TestMode"));
        Assert.Equal("bar", reread.Get("Unknown", "Foo"));
    }

    /// <summary>
    /// B-T4: <c>[MCPhoto]</c>의 미매핑 키는 **여전히 사라진다**(소유 경계가 의도대로 동작).
    /// 이것이 깨지면 오탈자 키·폐기된 키가 파일에 영구히 남는다.
    /// </summary>
    [Fact]
    public void T4_Unmapped_Keys_In_Owned_Section_Still_Disappear()
    {
        var path = PathFor("MCPhoto.ini");
        File.WriteAllText(path, "[MCPhoto]\nCutCount=8\nBogus=1\n[Test]\nTestMode=1\n");

        var svc = Service(path);
        svc.Load();
        Assert.True(svc.Save());

        var reread = IniFile.Parse(File.ReadAllText(path));
        Assert.Null(reread.Get("MCPhoto", "Bogus"));
        Assert.Equal("1", reread.Get("Test", "TestMode"));   // 외래 섹션은 살아 있다
    }

    /// <summary>B-T5: 외래 섹션에 소유 섹션과 **같은 이름의 키**가 있어도 섞이지 않는다.</summary>
    [Fact]
    public void T5_Same_Key_Name_In_Foreign_Section_Does_Not_Mix()
    {
        var path = PathFor("MCPhoto.ini");
        File.WriteAllText(path, "[MCPhoto]\nCutCount=8\n[Test]\nTestMode=1\nCutCount=99\n");

        var svc = Service(path);
        svc.Load();
        Assert.True(svc.Save());

        var reread = IniFile.Parse(File.ReadAllText(path));
        Assert.Equal(8, reread.GetInt("MCPhoto", "CutCount", -1));   // 설정값
        Assert.Equal("99", reread.Get("Test", "CutCount"));          // 원문 보존
    }

    /// <summary>B-T6: 대상 경로에 파일이 없을 때도 예외 없이 [MCPhoto]만 기록된다.</summary>
    [Fact]
    public void T6_Save_Without_Existing_File_Writes_Owned_Section_Only()
    {
        var path = PathFor("MCPhoto.ini");
        Assert.False(File.Exists(path));

        var svc = Service(path);
        svc.Load();
        Assert.True(svc.Save());

        var reread = IniFile.Parse(File.ReadAllText(path));
        Assert.True(reread.HasSection("MCPhoto"));
        Assert.False(reread.HasSection("Test"));
    }

    /// <summary>
    /// B-T7: 대상 파일을 **읽을 수 없을 때**(잠김) 저장은 성공하고 외래 섹션만 유실된다. 크래시 없음.
    /// <para>
    /// 잠금 방식: <c>FileAccess.Read</c> + <c>FileShare.Write</c> — 들어오는 <b>읽기</b>는 거부되고
    /// <b>쓰기</b>는 허용된다. 그래야 "읽기만 실패"를 격리 주입할 수 있다(쓰기까지 막으면 폴백으로 새어 나간다).
    /// </para>
    /// </summary>
    [Fact]
    public void T7_Read_Failure_Loses_Foreign_Sections_But_Save_Succeeds()
    {
        var path = PathFor("MCPhoto.ini");
        File.WriteAllText(path, "[MCPhoto]\nCutCount=8\n[Test]\nTestMode=1\n");

        var svc = Service(path);
        svc.Load();   // 잠그기 전에 로드(설정값 확보)

        using (var _ = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Write))
        {
            Assert.True(svc.Save());   // 예외 없이 성공한다
        }

        var reread = IniFile.Parse(File.ReadAllText(path));
        Assert.True(reread.HasSection("MCPhoto"));
        Assert.False(reread.HasSection("Test"));   // 보존은 부가 기능 — 실패해도 저장을 막지 않는다
    }

    /// <summary>
    /// B-T8: 폴백 승격 시 <b>2순위 파일의</b> 외래 섹션이 보존되고, <b>1순위 파일의</b> 외래 섹션이
    /// 2순위로 <b>복제되지 않는다</b>. 경로마다 다시 조립하지 않으면 이 테스트가 깨진다
    /// (엉뚱한 위치에 [Test]가 복제되는 형태의 결함).
    /// </summary>
    [Fact]
    public void T8_Fallback_Does_Not_Transplant_Foreign_Sections()
    {
        // 1순위: 디렉터리가 아니라 **파일**을 부모로 갖는 경로 → Directory.CreateDirectory가 실패해 쓰기 불가.
        var blocker = PathFor("blocker");
        File.WriteAllText(blocker, "not a directory");
        var first = Path.Combine(blocker, "MCPhoto.ini");

        var second = PathFor("second.ini");
        File.WriteAllText(second, "[MCPhoto]\nCutCount=6\n[Second]\nKeep=yes\n");

        var svc = new IniSettingsService(iniPath: second,
            fallbackCandidates: new[] { first, second });
        svc.Load();
        Assert.True(svc.Save());

        Assert.False(File.Exists(first), "쓰기 불가 경로에 파일이 생겼다 — 주입이 유효하지 않다");

        var reread = IniFile.Parse(File.ReadAllText(second));
        Assert.Equal("yes", reread.Get("Second", "Keep"));   // 2순위 자신의 외래 섹션은 보존
        Assert.False(reread.HasSection("Test"));             // 1순위에서 이식된 것이 없다
    }

    /// <summary>
    /// B-T9: 창 종료 경로(<c>MainWindow.OnClosing</c>)의 회귀 게이트 — VF-6 결함을 직접 겨냥한다.
    /// <para>
    /// ⚠️ <c>MainWindow</c>를 인스턴스화하지 않는다(headless Window 함정 — Application 싱글턴·스레드 충돌).
    /// 대신 <c>OnClosing</c>이 하는 것과 <b>같은 시퀀스</b>(창 기하 반영 + <c>Save()</c>)를 직접 호출한다.
    /// </para>
    /// </summary>
    [Fact]
    public void T9_Window_Closing_Sequence_Preserves_Test_Section()
    {
        var path = PathFor("MCPhoto.ini");
        File.WriteAllText(path, "[MCPhoto]\nCutCount=8\n" + TestSection + "\n");

        var svc = Service(path);
        var settings = svc.Load();

        // OnClosing 상당: 창 기하를 설정 객체에 반영한 뒤 저장한다.
        settings.WindowBounds.Left = 100;
        settings.WindowBounds.Top = 50;
        settings.WindowBounds.Width = 1280;
        settings.WindowBounds.Height = 720;
        Assert.True(svc.Save());

        var reread = IniFile.Parse(File.ReadAllText(path));
        Assert.Equal("1", reread.Get("Test", "TestMode"));
        Assert.Equal("admin", reread.Get("Test", "Role"));
        Assert.Equal(1280, (int)reread.GetDouble("MCPhoto", "WindowWidth", -1));   // 기하도 기록됐다
    }

    /// <summary>
    /// <c>AdoptMissingSections</c>는 **이미 있는 섹션을 건드리지 않는다** — 소유자가 방금 채운 값이 정본이다.
    /// (Save() 경로와 별개로 API 자체의 계약을 고정한다)
    /// </summary>
    [Fact]
    public void AdoptMissingSections_Never_Overwrites_Existing_Section()
    {
        var target = new IniFile();
        target.Set("MCPhoto", "CutCount", "8");

        var source = IniFile.Parse("[MCPhoto]\nCutCount=99\n[Test]\nTestMode=1\n");
        target.AdoptMissingSections(source);

        Assert.Equal("8", target.Get("MCPhoto", "CutCount"));   // 덮이지 않는다
        Assert.Equal("1", target.Get("Test", "TestMode"));      // 없던 섹션만 가져온다
    }
}
