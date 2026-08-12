using System.IO;
using System.Linq;
using MCPhoto.Core.Frames;
using MCPhoto.Core.Models;

namespace MCPhoto.Tests;

/// <summary>
/// it26 §3.4.3 T18·T19 — 프레임 캐시 루트 이관 후 <b>구 루트를 읽기 전용 보조 루트</b>로 계속 읽는다.
/// <para>
/// ★ 핵심 불변식: ① 구 루트의 프레임이 목록에서 사라지지 않는다 ② 이름 충돌은 새 루트가 이긴다
/// ③ <b>쓰기는 구 루트에 절대 닿지 않는다</b>(승격 필요·부분 실패 위험) ④ 보조 루트가 없으면 현행과 동일.
/// </para>
/// </summary>
public class LocalFrameStoreLegacyRootTests : IDisposable
{
    private const string Alice = "alice@test.com";

    private readonly string _newRoot;
    private readonly string _oldRoot;
    private readonly LocalFrameStore _store;      // 새 루트 + 구 루트(읽기)
    private readonly LocalFrameStore _oldWriter;  // 구 루트에 "이관 전 캐시"를 만드는 용도

    public LocalFrameStoreLegacyRootTests()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"mcphoto_it26_{Guid.NewGuid():N}");
        _newRoot = Path.Combine(baseDir, "new", "Frame");
        _oldRoot = Path.Combine(baseDir, "old", "Frame");
        _store = new LocalFrameStore(_newRoot, legacyReadRoot: _oldRoot);
        _oldWriter = new LocalFrameStore(_oldRoot);
    }

    public void Dispose()
    {
        var baseDir = Directory.GetParent(Directory.GetParent(_newRoot)!.FullName)!.FullName;
        try { if (Directory.Exists(baseDir)) Directory.Delete(baseDir, recursive: true); } catch { /* 무시 */ }
    }

    private static byte[] Png => new byte[] { 1, 2, 3, 4 };

    private static FrameTemplate MakeFrame(string name) => new()
    {
        Name = name,
        ImageSize = new ImageSize { Width = 1200, Height = 1600 },
        Slots = { new Slot { Index = 0, X = 10, Y = 20, Width = 300, Height = 400 } }
    };

    // ── ① 구 루트만 있는 프레임도 읽힌다 ──

    [Fact]
    public void Public_Frame_Only_In_Legacy_Root_Is_Loaded()
    {
        _oldWriter.SaveDefaultFrame(MakeFrame("구버전공용"), Png, dbId: "srv-old");

        var pub = _store.LoadPublic();

        Assert.Single(pub);
        Assert.Equal("구버전공용", pub[0].Name);
        Assert.StartsWith(_oldRoot, pub[0].ImageUrl);       // 이동하지 않았다 — 구 위치를 그대로 가리킨다
        Assert.Contains("구버전공용", _store.PublicFrameNames());
    }

    [Fact]
    public void Both_Roots_Are_Merged()
    {
        _oldWriter.SaveDefaultFrame(MakeFrame("구것"), Png, dbId: null);
        _store.SaveDefaultFrame(MakeFrame("새것"), Png, dbId: null);

        var names = _store.LoadPublic().Select(f => f.Name).OrderBy(n => n).ToArray();

        Assert.Equal(new[] { "구것", "새것" }, names);
    }

    // ── ② 이름 충돌 = 새 루트 승 ──

    [Fact]
    public void Name_Collision_Prefers_New_Root()
    {
        _oldWriter.SaveDefaultFrame(MakeFrame("같은이름"), Png, dbId: "srv-old");
        _store.SaveDefaultFrame(MakeFrame("같은이름"), Png, dbId: "srv-new");

        var pub = _store.LoadPublic();

        Assert.Single(pub);                                  // 두 벌로 보이지 않는다
        Assert.Equal("srv-new", pub[0].Id);
        Assert.StartsWith(_newRoot, pub[0].ImageUrl);
    }

    // ── ③ 쓰기는 구 루트에 닿지 않는다 ──

    [Fact]
    public void Writes_Never_Touch_Legacy_Root()
    {
        _store.SaveDefaultFrame(MakeFrame("공용"), Png, dbId: null);
        _store.SaveUserFrame(MakeFrame("내것"), Png, Alice, dbId: null);

        Assert.True(File.Exists(Path.Combine(_newRoot, "공용.png")));
        Assert.False(Directory.Exists(_oldRoot));             // 폴더 생성조차 하지 않는다
    }

    // ── ④ 보조 루트 부재·null이면 현행과 동일 ──

    [Fact]
    public void Missing_Legacy_Root_Is_Skipped()
    {
        // 구 폴더가 없는 신규 설치·개발 환경: 열거를 건너뛰므로 비용 0이고 결과는 새 루트만이다.
        _store.SaveDefaultFrame(MakeFrame("새것"), Png, dbId: null);

        Assert.False(Directory.Exists(_oldRoot));
        Assert.Single(_store.LoadPublic());
    }

    [Fact]
    public void Null_Legacy_Root_Behaves_Like_Before()
    {
        var solo = new LocalFrameStore(_newRoot);   // legacyReadRoot 미지정(기존 호출부 형태)
        _oldWriter.SaveDefaultFrame(MakeFrame("구것"), Png, dbId: null);
        solo.SaveDefaultFrame(MakeFrame("새것"), Png, dbId: null);

        var pub = solo.LoadPublic();

        Assert.Single(pub);
        Assert.Equal("새것", pub[0].Name);          // 구 루트를 보지 않는다
    }

    [Fact]
    public void Same_Path_For_Both_Roots_Does_Not_Duplicate()
    {
        // 개발 환경에서 두 경로가 같아질 수 있다 — 같은 폴더를 두 번 열거하면 목록이 두 배가 된다.
        var same = new LocalFrameStore(_newRoot, legacyReadRoot: _newRoot + Path.DirectorySeparatorChar);
        same.SaveDefaultFrame(MakeFrame("하나"), Png, dbId: null);

        Assert.Single(same.LoadPublic());
    }

    // ── ⑤ 개인 프레임(T19) ──

    [Fact]
    public void User_Frames_From_Legacy_Root_Are_Merged()
    {
        _oldWriter.SaveUserFrame(MakeFrame("구내것"), Png, Alice, dbId: null);
        _store.SaveUserFrame(MakeFrame("새내것"), Png, Alice, dbId: null);

        var mine = _store.LoadUser(Alice);

        Assert.Equal(2, mine.Count);
        Assert.Contains("구내것", _store.UserFrameNames(Alice));
        Assert.All(mine, f => Assert.Equal(Alice, f.UserId));
    }

    [Fact]
    public void User_Frame_Name_Collision_Prefers_New_Root()
    {
        _oldWriter.SaveUserFrame(MakeFrame("겹침"), Png, Alice, dbId: null);
        _store.SaveUserFrame(MakeFrame("겹침"), Png, Alice, dbId: null);

        var mine = _store.LoadUser(Alice);

        Assert.Single(mine);
        Assert.StartsWith(_newRoot, mine[0].ImageUrl);
    }

    // ── 삭제·진단 ──

    [Fact]
    public void DeleteLocal_Removes_Legacy_Cache_File()
    {
        // 서버에서 삭제된 프레임의 캐시 정리가 구 루트에도 미쳐야 한다(안 그러면 계속 목록에 오른다).
        _oldWriter.SaveDefaultFrame(MakeFrame("구것"), Png, dbId: "srv-old");
        var legacy = _store.LoadPublic().Single();

        Assert.True(_store.DeleteLocal(legacy));

        Assert.Empty(_store.LoadPublic());
        Assert.False(File.Exists(Path.Combine(_oldRoot, "구것.png")));
        Assert.False(File.Exists(Path.Combine(_oldRoot, "구것.slots")));
    }

    [Fact]
    public void Inspect_Covers_Both_Roots()
    {
        _oldWriter.SaveDefaultFrame(MakeFrame("구공용"), Png, dbId: null);
        _oldWriter.SaveUserFrame(MakeFrame("구개인"), Png, Alice, dbId: null);
        _store.SaveDefaultFrame(MakeFrame("새공용"), Png, dbId: null);

        var names = _store.Inspect(Alice).Select(e => e.DisplayName).OrderBy(n => n).ToArray();

        Assert.Equal(new[] { "구개인", "구공용", "새공용" }, names);
    }
}
