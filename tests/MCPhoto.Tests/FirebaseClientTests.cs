using System.IO;
using MCPhoto.Firebase;

namespace MCPhoto.Tests;

/// <summary>it10 S4-1: 서비스 계정 키 탐색 후보 진단. 실행폴더 우선 → ProgramData 폴백.</summary>
public class FirebaseClientTests
{
    [Fact]
    public void KeyCandidatePaths_Returns_Two_Paths_ExeFirst()
    {
        var candidates = FirebaseClient.KeyCandidatePaths();

        Assert.Equal(2, candidates.Length);

        // ① 실행폴더(AppContext.BaseDirectory) 우선
        var expectedExe = Path.Combine(AppContext.BaseDirectory, "serviceAccountKey.json");
        Assert.Equal(expectedExe, candidates[0]);

        // ② %ProgramData%\MCPhoto\ 폴백
        var expectedProgramData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MCPhoto", "serviceAccountKey.json");
        Assert.Equal(expectedProgramData, candidates[1]);
    }

    [Fact]
    public void DefaultKeyPath_Falls_Back_To_Last_Candidate_When_None_Exist()
    {
        // 두 후보 모두 존재하지 않는 게 일반적(테스트 실행 폴더에 키 없음) → 마지막 후보(ProgramData) 반환.
        // 단, 실행폴더에 키가 있으면 그게 반환됨(개발 PC 환경 방어).
        var candidates = FirebaseClient.KeyCandidatePaths();
        var expected = File.Exists(candidates[0]) ? candidates[0]
                     : File.Exists(candidates[1]) ? candidates[1]
                     : candidates[^1];

        Assert.Equal(expected, FirebaseClient.DefaultKeyPath());
    }
}
