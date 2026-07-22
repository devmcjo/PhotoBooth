using System.IO;
using MCPhoto.Core.Frames;
using MCPhoto.Core.Models;

namespace MCPhoto.Tests;

/// <summary>
/// it8 A2(정정): 로컬 프레임 저장/로딩/삭제 — 공용(접두 없음)/user(`{계정}_`) 구분, 이름 원문(sanitize 없음),
/// 금지문자 저장 거부, slots 라운드트립(+imagesize·dbid 메타).
/// </summary>
public class LocalFrameStoreTests : IDisposable
{
    private readonly string _root;
    private readonly LocalFrameStore _store;

    public LocalFrameStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"mcphoto_lfs_{Guid.NewGuid():N}");
        _store = new LocalFrameStore(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { /* 무시 */ }
    }

    private static FrameTemplate MakeFrame(string name, string? id = null) => new()
    {
        Id = id ?? string.Empty,
        Name = name,
        ImageSize = new ImageSize { Width = 1200, Height = 1600 },
        Slots =
        {
            new Slot { Index = 0, X = 10, Y = 20, Width = 300, Height = 400 },
            new Slot { Index = 1, X = 400, Y = 20, Width = 300, Height = 400 }
        }
    };

    private static byte[] Png => new byte[] { 1, 2, 3, 4 };

    [Fact]
    public void User_Save_Uses_Owner_Prefix()
    {
        _store.SaveLocal(MakeFrame("내프레임"), Png, ownerName: "alice");
        // {계정}_{이름} 접두, 루트 직속(하위 폴더 없음)
        Assert.True(File.Exists(Path.Combine(_root, "alice_내프레임.png")));
        Assert.True(File.Exists(Path.Combine(_root, "alice_내프레임.slots")));
    }

    [Fact]
    public void Public_Save_Has_No_Prefix()
    {
        _store.SaveLocal(MakeFrame("공용프레임"), Png, ownerName: null);
        Assert.True(File.Exists(Path.Combine(_root, "공용프레임.png"))); // 접두 없음
    }

    [Fact]
    public void Name_Kept_Verbatim_No_Sanitize()
    {
        // '_' 치환·특수문자 변환 없이 원문 그대로(sanitize 제거).
        _store.SaveLocal(MakeFrame("my frame 2024"), Png, ownerName: null);
        Assert.True(File.Exists(Path.Combine(_root, "my frame 2024.png")));
    }

    [Fact]
    public void Invalid_Filename_Chars_Rejected()
    {
        // 파일시스템 금지문자는 저장 거부(sanitize가 아니라 유효성 검사).
        Assert.Throws<IOException>(() => _store.SaveLocal(MakeFrame("a/b:c"), Png, ownerName: null));
    }

    [Fact]
    public void Slots_RoundTrip_With_ImageSize()
    {
        _store.SaveLocal(MakeFrame("f1"), Png, ownerName: null);
        var loaded = _store.LoadPublic();

        Assert.Single(loaded);
        var f = loaded[0];
        Assert.Equal(1200, f.ImageSize.Width);
        Assert.Equal(1600, f.ImageSize.Height);
        Assert.Equal(2, f.Slots.Count);
        Assert.Equal(10, f.Slots[0].X);
        Assert.Equal(400, f.Slots[0].Height);
    }

    [Fact]
    public void LoadPublic_Excludes_User_Prefixed()
    {
        _store.SaveLocal(MakeFrame("공용"), Png, ownerName: null);   // 접두 없음
        _store.SaveLocal(MakeFrame("내것"), Png, ownerName: "alice"); // alice_ 접두

        var pub = _store.LoadPublic();
        Assert.Single(pub);          // 공용만(접두 없는 것)
        Assert.Equal("공용", pub[0].Name);
        Assert.True(pub[0].IsDefault);
    }

    [Fact]
    public void LoadUser_Only_Own_Prefix()
    {
        _store.SaveLocal(MakeFrame("a1"), Png, ownerName: "alice");
        _store.SaveLocal(MakeFrame("b1"), Png, ownerName: "bob");

        var alice = _store.LoadUser("alice");
        Assert.Single(alice);
        Assert.Equal("a1", alice[0].Name);      // 접두 제거된 표시 이름
        Assert.Equal("alice", alice[0].UserId);
        Assert.False(alice[0].IsDefault);
        Assert.Empty(_store.LoadUser("carol"));  // 없는 계정
    }

    [Fact]
    public void CacheFromDb_Preserves_DbId_For_Server_Delete()
    {
        var db = MakeFrame("서버프레임", id: "firestore-doc-42");
        var cached = _store.CacheFromDb(db, Png);
        Assert.False(cached.Id.StartsWith("local:")); // DB id 보존

        // 재로딩 시에도 dbid 메타로 실 DB id 복원(local: 아님).
        var reloaded = _store.LoadPublic();
        Assert.Single(reloaded);
        Assert.Equal("firestore-doc-42", reloaded[0].Id);
    }

    [Fact]
    public void User_Frame_Has_No_DbId_Local_Id()
    {
        _store.SaveLocal(MakeFrame("mine"), Png, ownerName: "alice");
        var loaded = _store.LoadUser("alice");
        Assert.StartsWith("local:", loaded[0].Id); // user는 서버 문서 없음 → 로컬 식별자
    }

    [Fact]
    public void PublicFrameNames_For_Dedup()
    {
        _store.SaveLocal(MakeFrame("공용1"), Png, ownerName: null);
        _store.SaveLocal(MakeFrame("mine"), Png, ownerName: "alice");
        var names = _store.PublicFrameNames();
        Assert.Contains("공용1", names);
        Assert.DoesNotContain("alice_mine", names); // user 파일은 공용 이름집합에서 제외
    }

    [Fact]
    public void DeleteLocal_Removes_Png_And_Slots()
    {
        var saved = _store.SaveLocal(MakeFrame("del"), Png, ownerName: "alice");
        Assert.True(_store.DeleteLocal(saved));
        Assert.Empty(_store.LoadUser("alice"));
    }
}
