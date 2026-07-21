using MCPhoto.App;
using MCPhoto.Core.Models;
using Xunit;

namespace MCPhoto.Tests;

/// <summary>
/// it3 Step 1(B1): 계정 단일 소스 — Login/Logout/CurrentUserChanged, Reset(clearUser) 정책 검증.
/// 로그인은 촬영 세션보다 상위 수명(Reset(false)에도 보존), 명시 로그아웃·Reset(true)만 해제.
/// </summary>
public class SessionServiceTests
{
    private static User MakeUser(string id = "u1") => new() { Id = id, Password = "pw", Role = UserRole.User };

    [Fact]
    public void Login_Sets_User_And_Raises_Event()
    {
        var s = new SessionContext();
        int events = 0;
        s.CurrentUserChanged += (_, _) => events++;

        var u = MakeUser();
        s.Login(u);

        Assert.Same(u, s.CurrentUser);
        Assert.Equal(1, events);
    }

    [Fact]
    public void Reset_Preserves_User_By_Default()
    {
        var s = new SessionContext();
        s.Login(MakeUser());
        int events = 0;
        s.CurrentUserChanged += (_, _) => events++;

        // 촬영 데이터 설정 후 Reset(clearUser 기본 false)
        s.SelectedFrame = new FrameTemplate { Id = "f" };
        s.Filter = MCPhoto.Core.Capture.FilterKind.Grayscale;
        s.Reset();

        Assert.NotNull(s.CurrentUser);           // 로그인 보존
        Assert.Null(s.SelectedFrame);            // 촬영 데이터 폐기
        Assert.Equal(MCPhoto.Core.Capture.FilterKind.None, s.Filter);
        Assert.Equal(0, events);                 // 로그인 유지 → 통지 없음
    }

    [Fact]
    public void Reset_ClearUser_True_Logs_Out()
    {
        var s = new SessionContext();
        s.Login(MakeUser());
        int events = 0;
        s.CurrentUserChanged += (_, _) => events++;

        s.Reset(clearUser: true);

        Assert.Null(s.CurrentUser);              // 다음 손님: 로그아웃
        Assert.Equal(1, events);                 // 로그아웃 통지
    }

    [Fact]
    public void Logout_Clears_User_And_Raises_Event()
    {
        var s = new SessionContext();
        s.Login(MakeUser());
        int events = 0;
        s.CurrentUserChanged += (_, _) => events++;

        s.Logout();

        Assert.Null(s.CurrentUser);
        Assert.Equal(1, events);
    }

    [Fact]
    public void Logout_When_Already_Null_Does_Not_Raise()
    {
        var s = new SessionContext();
        int events = 0;
        s.CurrentUserChanged += (_, _) => events++;

        s.Logout(); // 이미 null

        Assert.Null(s.CurrentUser);
        Assert.Equal(0, events); // 불필요한 통지 없음
    }

    [Fact]
    public void Reset_ClearUser_True_When_Guest_No_Event()
    {
        var s = new SessionContext(); // 게스트(로그인 안 함)
        int events = 0;
        s.CurrentUserChanged += (_, _) => events++;

        s.Reset(clearUser: true);

        Assert.Null(s.CurrentUser);
        Assert.Equal(0, events); // 원래 null이라 통지 없음
    }
}
