using System.IO;
using MCPhoto.Core.LocalSave;

namespace MCPhoto.Tests;

/// <summary>WBS Step 5: 로컬 저장 폴더 생성·경로 변경·중복·빈경로·타임랩스 유무 검증.</summary>
public class LocalSaveTests : IDisposable
{
    private readonly string _root;
    private readonly string _finalImage;
    private readonly string _timelapse;

    public LocalSaveTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"mcphoto_lstest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        _finalImage = Path.Combine(_root, "_src_final.jpg");
        _timelapse = Path.Combine(_root, "_src_timelapse.mp4");
        File.WriteAllBytes(_finalImage, new byte[] { 1, 2, 3 });
        File.WriteAllBytes(_timelapse, new byte[] { 4, 5, 6 });
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { /* 정리 실패 무시 */ }
    }

    [Fact]
    public async Task SessionFolder_Name_Follows_Convention()
    {
        var name = LocalSaveService.SessionFolderName(new DateTime(2026, 7, 20, 14, 45, 0));
        Assert.Equal("mcphoto_260720_1445", name);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Save_Creates_Session_Folder_With_Final_And_Timelapse()
    {
        var svc = new LocalSaveService();
        var dest = Path.Combine(_root, "out");

        var folder = await svc.SaveAsync(dest, _finalImage, _timelapse, new DateTime(2026, 7, 20, 14, 45, 0));

        Assert.NotNull(folder);
        Assert.True(File.Exists(Path.Combine(folder!, "final.jpg")));
        Assert.True(File.Exists(Path.Combine(folder!, "timelapse.mp4")));
        Assert.EndsWith("mcphoto_260720_1445", folder);
    }

    [Fact]
    public async Task Save_Png_Keeps_Extension()
    {
        var pngSrc = Path.Combine(_root, "_src_final.png");
        File.WriteAllBytes(pngSrc, new byte[] { 9 });
        var svc = new LocalSaveService();

        var folder = await svc.SaveAsync(Path.Combine(_root, "out2"), pngSrc, null, DateTime.Now);

        Assert.NotNull(folder);
        Assert.True(File.Exists(Path.Combine(folder!, "final.png")));
        Assert.False(File.Exists(Path.Combine(folder!, "timelapse.mp4"))); // 타임랩스 null
    }

    [Fact]
    public async Task Save_Without_Timelapse_Only_Final()
    {
        var svc = new LocalSaveService();
        var folder = await svc.SaveAsync(Path.Combine(_root, "out3"), _finalImage, null, DateTime.Now);

        Assert.NotNull(folder);
        Assert.True(File.Exists(Path.Combine(folder!, "final.jpg")));
        Assert.False(File.Exists(Path.Combine(folder!, "timelapse.mp4")));
    }

    [Fact]
    public async Task Empty_Path_Returns_Null_No_Save()
    {
        var svc = new LocalSaveService();
        var folder = await svc.SaveAsync("", _finalImage, _timelapse, DateTime.Now);
        Assert.Null(folder);
    }

    [Fact]
    public async Task Custom_Path_Respected()
    {
        var svc = new LocalSaveService();
        var custom = Path.Combine(_root, "custom", "deep");

        var folder = await svc.SaveAsync(custom, _finalImage, null, DateTime.Now);

        Assert.NotNull(folder);
        Assert.StartsWith(custom, folder);
    }

    [Fact]
    public async Task Duplicate_SessionFolder_Gets_Unique_Suffix()
    {
        var svc = new LocalSaveService();
        var dest = Path.Combine(_root, "dup");
        var time = new DateTime(2026, 7, 20, 14, 45, 0);

        var f1 = await svc.SaveAsync(dest, _finalImage, null, time);
        var f2 = await svc.SaveAsync(dest, _finalImage, null, time);

        Assert.NotNull(f1);
        Assert.NotNull(f2);
        Assert.NotEqual(f1, f2); // 같은 분(minute)이어도 폴더 충돌 회피
    }
}
