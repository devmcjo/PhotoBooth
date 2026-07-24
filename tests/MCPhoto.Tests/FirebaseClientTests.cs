using System.IO;
using MCPhoto.Firebase;

namespace MCPhoto.Tests;

/// <summary>it10 S4-1: 서비스 계정 키 탐색 후보 진단. 실행폴더 전용(ProgramData 후보 제거 — 사용자 결정).</summary>
public class FirebaseClientTests
{
    [Fact]
    public void KeyCandidatePaths_Returns_Only_ExePath()
    {
        var candidates = FirebaseClient.KeyCandidatePaths();

        // 실행폴더(AppContext.BaseDirectory) 전용. ProgramData 후보는 제거됨.
        Assert.Single(candidates);

        var expectedExe = Path.Combine(AppContext.BaseDirectory, "serviceAccountKey.json");
        Assert.Equal(expectedExe, candidates[0]);
    }

    [Fact]
    public void DefaultKeyPath_Returns_ExePath_Candidate()
    {
        // 후보는 실행폴더 1개 → 존재하면 그 경로, 없으면 마지막(=유일) 후보 반환.
        var candidates = FirebaseClient.KeyCandidatePaths();
        var expected = File.Exists(candidates[0]) ? candidates[0] : candidates[^1];

        Assert.Equal(expected, FirebaseClient.DefaultKeyPath());
    }
}
