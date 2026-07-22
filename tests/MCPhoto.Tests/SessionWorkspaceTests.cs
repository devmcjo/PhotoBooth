using System.IO;
using MCPhoto.Core.Capture;

namespace MCPhoto.Tests;

/// <summary>it6 #3: sessions 임시폴더 시작 정리 검증. sessions 루트 하위만 비우고 result·logs는 보존.</summary>
public class SessionWorkspaceTests : IDisposable
{
    private readonly string _dataFolder;

    public SessionWorkspaceTests()
    {
        _dataFolder = Path.Combine(Path.GetTempPath(), $"mcphoto_ws_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dataFolder);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dataFolder)) Directory.Delete(_dataFolder, recursive: true); }
        catch { /* 무시 */ }
    }

    [Fact]
    public void SessionsRoot_Is_DataFolder_Sessions()
        => Assert.Equal(Path.Combine(_dataFolder, "sessions"), SessionWorkspace.SessionsRoot(_dataFolder));

    [Fact]
    public void CleanupOnStartup_No_Sessions_Folder_Returns_Zero()
        => Assert.Equal(0, SessionWorkspace.CleanupOnStartup(_dataFolder));

    [Fact]
    public void CleanupOnStartup_Removes_Session_Subdirectories()
    {
        var root = SessionWorkspace.SessionsRoot(_dataFolder);
        Directory.CreateDirectory(Path.Combine(root, "aaa"));
        Directory.CreateDirectory(Path.Combine(root, "bbb"));
        File.WriteAllText(Path.Combine(root, "aaa", "session.mp4"), "x");

        var removed = SessionWorkspace.CleanupOnStartup(_dataFolder);

        Assert.Equal(2, removed);
        Assert.True(Directory.Exists(root));                       // 루트 자체는 유지
        Assert.Empty(Directory.EnumerateFileSystemEntries(root));  // 하위는 비워짐
    }

    [Fact]
    public void CleanupOnStartup_Does_Not_Touch_Result_Or_Logs()
    {
        // result·logs는 sessions 루트 밖 → 정리 대상 아님.
        var result = Path.Combine(_dataFolder, "result");
        var logs = Path.Combine(_dataFolder, "logs");
        Directory.CreateDirectory(result);
        Directory.CreateDirectory(logs);
        File.WriteAllText(Path.Combine(result, "final.jpg"), "img");
        File.WriteAllText(Path.Combine(logs, "app.log"), "log");
        Directory.CreateDirectory(Path.Combine(SessionWorkspace.SessionsRoot(_dataFolder), "old"));

        SessionWorkspace.CleanupOnStartup(_dataFolder);

        Assert.True(File.Exists(Path.Combine(result, "final.jpg")));
        Assert.True(File.Exists(Path.Combine(logs, "app.log")));
    }
}
