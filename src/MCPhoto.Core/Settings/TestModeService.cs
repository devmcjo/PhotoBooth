using MCPhoto.Core.Models;
using Microsoft.Extensions.Logging;

namespace MCPhoto.Core.Settings;

/// <summary>
/// <c>[Test]</c> 섹션을 <b>최초 접근 시 1회</b> 읽어 캐시한다. 런타임에 ini를 바꿔도 판정이 바뀌지 않는다 —
/// 실행 중 권한이 바뀌는 앱을 만들지 않는다는 방침이며, 재시작이 정직하다.
/// <para>
/// 경로는 <see cref="ISettingsService.IniPath"/>를 그대로 쓴다. 자체적으로 <c>SettingsPathResolver</c>를 다시
/// 돌리면 <b>두 개의 독립적인 경로 판정</b>이 생기고("쓰기 가능한 첫 후보"는 환경에 따라 갈릴 수 있다),
/// 그 순간 "테스트 모드가 안 켜진다"의 원인이 미궁이 된다.
/// </para>
/// </summary>
public sealed class TestModeService : ITestModeService
{
    private readonly ISettingsService _settings;
    private readonly ILogger<TestModeService>? _logger;
    private readonly object _gate = new();

    private bool _loaded;
    private TestModeOptions _options = TestModeOptions.Disabled;
    private string _sourcePath = string.Empty;
    private User? _testUser;

    public TestModeService(ISettingsService settings, ILogger<TestModeService>? logger = null)
    {
        _settings = settings;
        _logger = logger;
    }

    public bool IsEnabled { get { EnsureLoaded(); return _options.Enabled; } }

    public TestModeOptions Options { get { EnsureLoaded(); return _options; } }

    public string SourcePath { get { EnsureLoaded(); return _sourcePath; } }

    public User? TestUser { get { EnsureLoaded(); return _testUser; } }

    public bool IsTestUser(User? user)
    {
        EnsureLoaded();
        // ⚠️ 값 비교가 아니라 참조 동일성이다(§B8.3 S2). 이메일·Id·역할이 전부 같은 실계정도 false다.
        return user is not null && ReferenceEquals(_testUser, user);
    }

    private void EnsureLoaded()
    {
        lock (_gate)
        {
            if (_loaded) return;
            _loaded = true;   // 실패해도 재시도하지 않는다(꺼짐 상태로 확정 — 판정이 흔들리지 않게).
            Load();
        }
    }

    private void Load()
    {
        _sourcePath = _settings.IniPath;

        IniFile ini;
        try
        {
            ini = File.Exists(_sourcePath)
                ? IniFile.Parse(File.ReadAllText(_sourcePath))
                : new IniFile();
        }
        catch (Exception ex)
        {
            // 손상·잠김이어도 앱은 정상 부팅해야 한다(크래시 금지) → 테스트 모드 꺼짐으로 진행.
            _logger?.LogWarning(ex, "[Test] 섹션 읽기 실패 — 테스트 모드 꺼짐으로 진행: {Path}", _sourcePath);
            ini = new IniFile();
        }

        _options = TestModeOptions.FromIni(ini);

        if (_options.Enabled)
        {
            _testUser = _options.CreateUser();
            // 검증 실패로 폴백한 항목을 흘린다. 순수 팩토리가 로그를 찍지 않는 대가로 여기서 한 번에 낸다.
            foreach (var warning in _options.Warnings)
                _logger?.LogWarning("테스트 모드 설정 경고: {Warning}", warning);
            return;
        }

        // "분명히 썼는데 안 된다"에 즉답하기 위한 로그. 섹션은 있는데 스위치가 꺼져 있거나 값이 인식되지 않은 경우다.
        if (ini.HasSection(TestModeOptions.SectionName))
        {
            _logger?.LogInformation("테스트 모드 OFF([Test] 섹션 발견, TestMode={Value}) ini={Path}",
                ini.GetString(TestModeOptions.SectionName, "TestMode", "(없음)"), _sourcePath);
        }
    }
}
