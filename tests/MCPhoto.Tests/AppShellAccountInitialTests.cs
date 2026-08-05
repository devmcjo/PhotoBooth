using System.IO;
using MCPhoto.App;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;

namespace MCPhoto.Tests;

/// <summary>
/// it21 §6.2: 상단 바 계정 버튼이 텍스트 pill에서 아이콘/아바타로 바뀌면서, "누가 로그인했는지"를
/// 이니셜 1글자로 전달한다. 종전 AccountLabel(계정 ID)은 툴팁으로 이전됐다.
/// 아바타가 비거나 통지가 빠지면 로그인 상태가 화면에서 사라지므로 여기서 고정한다.
/// </summary>
public class AppShellAccountInitialTests
{
    /// <summary>아무 서비스도 해석하지 않는 최소 provider(셸이 null을 방어한다).</summary>
    private sealed class EmptyProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private static AppShellViewModel MakeShell(SessionContext session)
    {
        var settings = new IniSettingsService(
            iniPath: Path.Combine(Path.GetTempPath(), $"shell_{Guid.NewGuid():N}.ini"));
        settings.Load();
        return new AppShellViewModel(new IdleWatchdog(), settings, new EmptyProvider(), session);
    }

    [Fact]
    public void Guest_Has_Empty_Initial_And_Login_Label()
    {
        var session = new SessionContext();
        var shell = MakeShell(session);

        Assert.Equal(string.Empty, shell.AccountInitial);
        Assert.Equal("로그인", shell.AccountLabel);
        Assert.False(shell.IsLoggedIn);
    }

    [Theory]
    [InlineData("devmcjo", "D")]
    [InlineData("Alice", "A")]
    [InlineData("07user", "0")]
    public void LoggedIn_Initial_Is_First_Char_Uppercased(string id, string expected)
    {
        var session = new SessionContext();
        var shell = MakeShell(session);

        session.Login(new User { Id = id, Role = UserRole.User });

        Assert.Equal(expected, shell.AccountInitial);
        Assert.Equal(id, shell.AccountLabel);   // 툴팁에는 계정 ID 원문이 그대로 나간다
    }

    [Fact]
    public void Logout_Clears_Initial()
    {
        var session = new SessionContext();
        var shell = MakeShell(session);

        session.Login(new User { Id = "devmcjo", Role = UserRole.User });
        Assert.Equal("D", shell.AccountInitial);

        session.Logout();
        Assert.Equal(string.Empty, shell.AccountInitial);
    }

    /// <summary>
    /// 통지가 없으면 아바타가 갱신되지 않는다(로그인했는데 사람 아이콘이 그대로 남는다).
    /// OnCurrentUserChanged 에서 AccountInitial 통지가 빠지는 회귀를 잡는다.
    /// </summary>
    [Fact]
    public void Login_Raises_PropertyChanged_For_Initial()
    {
        var session = new SessionContext();
        var shell = MakeShell(session);

        var raised = new List<string?>();
        shell.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        session.Login(new User { Id = "devmcjo", Role = UserRole.User });

        Assert.Contains(nameof(AppShellViewModel.AccountInitial), raised);
        Assert.Contains(nameof(AppShellViewModel.IsLoggedIn), raised);
    }
}
