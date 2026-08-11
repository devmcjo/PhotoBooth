using MCPhoto.Core.Models;
using MCPhoto.Core.Settings;

namespace MCPhoto.Tests.Fakes;

/// <summary>
/// <see cref="ITestModeService"/> 페이크(it25 §12.2). <c>[Test]</c> ini 문자열을 그대로 받아
/// 실서비스와 동일한 <see cref="TestModeOptions.FromIni"/> 경로로 해석한다 — 옵션 객체를 손으로
/// 조립하면 파싱 규약(폴백·경고)이 테스트와 프로덕션에서 갈린다.
/// <para>
/// ★ 이 페이크의 핵심은 <b>참조 동일성</b>이다: <see cref="TestUser"/>는 1회 생성 후 같은 인스턴스를
/// 돌려주고 <see cref="IsTestUser"/>는 <c>ReferenceEquals</c>로만 판정한다. 값이 전부 같은 별
/// 인스턴스(실계정 모사)가 false여야 봉인 TS2("실계정 세션에는 시뮬레이션이 적용되지 않는다")를
/// 테스트가 실제로 잠근다 — 값 비교로 바꾸면 그 테스트가 통과하면서도 의미를 잃는다.
/// </para>
/// </summary>
public sealed class FakeTestModeService : ITestModeService
{
    private readonly User? _testUser;

    /// <param name="testIni">
    /// <c>[Test]</c> 섹션을 포함한 ini 본문. 예: <c>"[Test]\nTestMode=1\nExternalCamera=1\n"</c>.
    /// </param>
    public FakeTestModeService(string testIni)
    {
        Options = TestModeOptions.FromIni(IniFile.Parse(testIni));
        _testUser = Options.Enabled ? Options.CreateUser() : null;
    }

    public bool IsEnabled => Options.Enabled;

    public TestModeOptions Options { get; }

    public string SourcePath => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MCPhoto.ini");

    public User? TestUser => _testUser;

    public bool IsTestUser(User? user) => user is not null && ReferenceEquals(_testUser, user);
}
