using System.IO;
using MCPhoto.Core.LocalSave;

namespace MCPhoto.Tests;

/// <summary>
/// it26 §3.7 T1 — 로컬 저장 루트 해석 단일 지점.
/// ★ 핵심 불변식: <b>운영자가 지정한 경로는 항상 우선이며 변형되지 않는다</b>(이관이 설정을 덮어쓰면 사고다).
/// </summary>
public class LocalSavePathResolverTests
{
    private const string DataFolder = @"C:\ProgramData\MCPhoto";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Blank_Configured_Path_Falls_Back_To_DataFolder_Result(string? configured)
    {
        // 빈 값의 해석만 바뀌었다: 종전 {exe}\result → 이제 {데이터 폴더}\result.
        Assert.Equal(Path.Combine(DataFolder, "result"),
            LocalSavePathResolver.Resolve(configured, DataFolder));
    }

    [Fact]
    public void Configured_Path_Wins_Over_Default()
    {
        Assert.Equal(@"D:\photos", LocalSavePathResolver.Resolve(@"D:\photos", DataFolder));
    }

    [Fact]
    public void Configured_Path_Is_Trimmed_But_Otherwise_Untouched()
    {
        // Trim 외의 정규화(대소문자·후행 슬래시·상대경로 확장)를 하지 않는다 — 운영자가 적은 그대로 쓴다.
        Assert.Equal(@"D:\photos", LocalSavePathResolver.Resolve("  D:\\photos  ", DataFolder));
        Assert.Equal(@"D:\Photos\", LocalSavePathResolver.Resolve(@"D:\Photos\", DataFolder));
        Assert.Equal(@"\\nas\share\사진", LocalSavePathResolver.Resolve(@"\\nas\share\사진", DataFolder));
        Assert.Equal("relative\\out", LocalSavePathResolver.Resolve("relative\\out", DataFolder));
    }

    [Fact]
    public void Default_Folder_Name_Is_Result()
    {
        // 인스톨러 [Dirs]·문서·제거 규약이 이 이름에 묶여 있다(installer/MCPhoto.iss).
        Assert.Equal("result", LocalSavePathResolver.DefaultFolderName);
    }
}
