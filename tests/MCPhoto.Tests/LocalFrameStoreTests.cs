using System.IO;
using System.Linq;
using MCPhoto.Core.Frames;
using MCPhoto.Core.Models;
using Xunit;

namespace MCPhoto.Tests;

/// <summary>
/// 로컬 프레임 저장소 — `.slots` v2(서명) + 계정별 해시 폴더 레이아웃.
/// <para>
/// 종전 접두 규약(<c>{계정}_{이름}.png</c>) 테스트는 **폐지**했다. 소유 판정이 파일명이 아니라
/// 서명된 <c>#owner</c>로 옮겨갔고(설계 D-2), 그에 따라 "파일명만 바꿔 남의 프레임 보기"와
/// "계정 id 접두 겹침 유출"(billing/04 §2.1 결함 A)이 원인 채로 사라졌기 때문이다.
/// </para>
/// 설계 §13 T5~T8에 해당한다.
/// </summary>
public class LocalFrameStoreTests : IDisposable
{
    private readonly string _root;
    private readonly LocalFrameStore _store;

    private const string Alice = "alice@test.com";
    private const string Bob = "bob@test.com";

    public LocalFrameStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"mcphoto_store_{Guid.NewGuid():N}");
        _store = new LocalFrameStore(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { /* 무시 */ }
    }

    private static byte[] Png => new byte[] { 1, 2, 3, 4 };

    private static FrameTemplate MakeFrame(string name) => new()
    {
        Name = name,
        ImageSize = new ImageSize { Width = 1200, Height = 1600 },
        Slots = { new Slot { Index = 0, X = 10, Y = 20, Width = 300, Height = 400 } }
    };

    // ── 저장·로딩 기본 ──

    [Fact]
    public void SaveDefault_Is_Visible_To_Everyone_Including_Guest()
    {
        _store.SaveDefaultFrame(MakeFrame("공용"), Png, dbId: "srv-1");

        var pub = _store.LoadPublic();
        Assert.Single(pub);
        Assert.Equal("공용", pub[0].Name);
        Assert.True(pub[0].IsDefault);
        Assert.Null(pub[0].UserId);
        Assert.Equal("srv-1", pub[0].Id);          // #dbid가 있으면 서버 문서 id가 프레임 id
    }

    [Fact]
    public void SaveUser_Is_Loaded_For_Owner()
    {
        _store.SaveUserFrame(MakeFrame("내것"), Png, Alice, dbId: null);

        var mine = _store.LoadUser(Alice);
        Assert.Single(mine);
        Assert.Equal("내것", mine[0].Name);
        Assert.False(mine[0].IsDefault);
        Assert.Equal(Alice, mine[0].UserId);        // 로컬 규약: UserId = 소유자 이메일
        Assert.StartsWith("local:", mine[0].Id);    // 서버 미동기(#dbid 없음)
    }

    [Fact]
    public void User_Frame_Is_Not_In_Public_List()
    {
        _store.SaveUserFrame(MakeFrame("내것"), Png, Alice, dbId: null);
        Assert.Empty(_store.LoadPublic());
    }

    /// <summary>T5: 계정 A의 프레임이 계정 B에게 보이지 않는다(요구의 핵심).</summary>
    [Fact]
    public void Other_Account_Cannot_See_My_Frames()
    {
        _store.SaveUserFrame(MakeFrame("alice것"), Png, Alice, dbId: null);

        Assert.Single(_store.LoadUser(Alice));
        Assert.Empty(_store.LoadUser(Bob));
    }

    /// <summary>T6: 두 계정이 같은 이름을 써도 서로 덮어쓰지 않는다(계정별 폴더 분리의 이득).</summary>
    [Fact]
    public void Same_Name_In_Two_Accounts_Does_Not_Collide()
    {
        _store.SaveUserFrame(MakeFrame("같은이름"), Png, Alice, dbId: null);
        _store.SaveUserFrame(MakeFrame("같은이름"), Png, Bob, dbId: null);

        Assert.Single(_store.LoadUser(Alice));
        Assert.Single(_store.LoadUser(Bob));
        Assert.NotEqual(_store.LoadUser(Alice)[0].ImageUrl, _store.LoadUser(Bob)[0].ImageUrl);
    }

    /// <summary>
    /// T7: 구 포맷(v1 평문) 파일은 조용히 제외된다 — base64 디코딩 실패로 걸러지므로
    /// 마이그레이션 코드가 필요 없다(설계 §7).
    /// </summary>
    [Fact]
    public void Legacy_Plaintext_Slots_Is_Ignored()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllBytes(Path.Combine(_root, "구프레임.png"), Png);
        File.WriteAllText(Path.Combine(_root, "구프레임.slots"),
            "#imagesize=1200,1600\n0,10,20,300,400\n");   // v1 평문

        Assert.Empty(_store.LoadPublic());
    }

    /// <summary>
    /// 서명 위조 차단: `#owner`를 남의 것으로 바꿔 다시 인코딩해도 서명이 깨져 목록에서 빠진다.
    /// (키 없이 유효 서명을 만들 수 없다 — 이 설계의 존재 이유)
    /// </summary>
    [Fact]
    public void Tampered_Owner_Is_Rejected()
    {
        _store.SaveUserFrame(MakeFrame("내것"), Png, Alice, dbId: null);

        var slots = Directory.EnumerateFiles(_root, "*.slots", SearchOption.AllDirectories).Single();
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(File.ReadAllText(slots)));
        var tampered = decoded.Replace(Alice, Bob);          // 소유자만 바꾸고 서명은 그대로
        File.WriteAllText(slots, Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(tampered)));

        Assert.Empty(_store.LoadUser(Bob));                  // 위조 대상 계정에도 안 보이고
        Assert.Empty(_store.LoadUser(Alice));                // 원 소유자에게도 안 보인다(서명 불일치)
    }

    [Fact]
    public void Slots_File_Missing_Is_Skipped()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllBytes(Path.Combine(_root, "이미지만.png"), Png);

        Assert.Empty(_store.LoadPublic());
    }

    // ── 이름 규칙·삭제 ──

    [Fact]
    public void Name_With_Invalid_Chars_Is_Rejected()
        => Assert.Throws<IOException>(() => _store.SaveDefaultFrame(MakeFrame("a/b:c"), Png, dbId: null));

    [Fact]
    public void Empty_Name_Is_Rejected()
        => Assert.Throws<IOException>(() => _store.SaveDefaultFrame(MakeFrame("  "), Png, dbId: null));

    /// <summary>접두 규약 폐지의 이득: 이름에 `_`가 있어도 공용 목록에서 사라지지 않는다.</summary>
    [Fact]
    public void Underscore_In_Name_No_Longer_Hides_Public_Frame()
    {
        _store.SaveDefaultFrame(MakeFrame("my_frame"), Png, dbId: null);

        var pub = _store.LoadPublic();
        Assert.Single(pub);
        Assert.Equal("my_frame", pub[0].Name);
    }

    [Fact]
    public void SaveUser_Without_Owner_Throws()
        => Assert.Throws<IOException>(() => _store.SaveUserFrame(MakeFrame("x"), Png, "  ", dbId: null));

    [Fact]
    public void Delete_Removes_Both_Files()
    {
        var saved = _store.SaveUserFrame(MakeFrame("지울것"), Png, Alice, dbId: null);
        var slotsPath = Path.ChangeExtension(saved.ImageUrl, ".slots");

        Assert.True(_store.DeleteLocal(saved));
        Assert.False(File.Exists(saved.ImageUrl));
        Assert.False(File.Exists(slotsPath));
        Assert.Empty(_store.LoadUser(Alice));
    }

    [Fact]
    public void Delete_Returns_False_When_Missing()
        => Assert.False(_store.DeleteLocal(new FrameTemplate { ImageUrl = Path.Combine(_root, "없음.png") }));

    // ── 이름 집합(충돌 검사용) ──

    [Fact]
    public void Name_Sets_Are_Scoped()
    {
        _store.SaveDefaultFrame(MakeFrame("공용"), Png, dbId: null);
        _store.SaveUserFrame(MakeFrame("내것"), Png, Alice, dbId: null);
        _store.SaveUserFrame(MakeFrame("남의것"), Png, Bob, dbId: null);

        Assert.Equal(new[] { "공용" }, _store.PublicFrameNames().ToArray());
        Assert.Equal(new[] { "내것" }, _store.UserFrameNames(Alice).ToArray());
    }

    // ── 진단 ──

    /// <summary>T8 계열: 진단은 "왜 안 보이는지"를 보여야 하므로 검증 실패 파일도 상태와 함께 돌려준다.</summary>
    [Fact]
    public void Inspect_Reports_Status_Including_Broken_Files()
    {
        _store.SaveDefaultFrame(MakeFrame("정상"), Png, dbId: "srv-1");
        File.WriteAllBytes(Path.Combine(_root, "구프레임.png"), Png);
        File.WriteAllText(Path.Combine(_root, "구프레임.slots"), "#imagesize=1,1\n0,0,0,1,1\n"); // v1

        var entries = _store.Inspect(ownerEmail: null);

        var ok = entries.Single(e => e.DisplayName == "정상");
        Assert.Equal(SlotsDecodeStatus.Ok, ok.Status);
        Assert.Equal(FrameOwnership.DefaultOwner, ok.Owner);
        Assert.Equal("srv-1", ok.DbId);
        Assert.Equal(1, ok.SlotCount);

        var broken = entries.Single(e => e.DisplayName == "구프레임");
        Assert.Equal(SlotsDecodeStatus.NotEncoded, broken.Status);
    }

    [Fact]
    public void Inspect_Includes_Own_Frames_Only()
    {
        _store.SaveUserFrame(MakeFrame("내것"), Png, Alice, dbId: null);
        _store.SaveUserFrame(MakeFrame("남의것"), Png, Bob, dbId: null);

        var entries = _store.Inspect(Alice);

        Assert.Contains(entries, e => e.DisplayName == "내것");
        Assert.DoesNotContain(entries, e => e.DisplayName == "남의것");
    }
}
