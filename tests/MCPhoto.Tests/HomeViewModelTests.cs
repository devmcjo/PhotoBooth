using System.IO;
using MCPhoto.App;
using MCPhoto.App.ViewModels;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;

namespace MCPhoto.Tests;

/// <summary>
/// it21 §7.4(개정): Home 흐름 안내 1번 칸의 보조 문구는 **현재 권한에서 참인 문장만** 내려야 한다.
///
/// 프레임 만들기는 CanWriteFrames(AdvancedUser·Manager·Admin)만 가능하다. 게스트는 물론
/// 일반 user·temp_user도 못 만든다 — "직접 만들 수도 있어요"를 고정 문구로 두면 대다수에게 거짓이 되고,
/// 프레임 선택 화면에서 [프레임 만들기]를 찾지 못한 손님에게 실망만 남긴다.
/// 이 테스트가 그 거짓을 구조적으로 막는다.
/// </summary>
public class HomeViewModelTests
{
    private sealed class EmptyProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private static HomeViewModel MakeVm(User? user)
    {
        var settings = new IniSettingsService(
            iniPath: Path.Combine(Path.GetTempPath(), $"home_{Guid.NewGuid():N}.ini"));
        settings.Load();
        var session = new SessionContext();
        if (user is not null) session.Login(user);

        var shell = new AppShellViewModel(new IdleWatchdog(), settings, new EmptyProvider(), session);
        return new HomeViewModel(shell);
    }

    private static User Account(UserRole role) => new() { Id = "tester", Role = role };

    [Fact]
    public void Guest_Is_Told_Login_Enables_Own_Frames()
    {
        var vm = MakeVm(null);

        Assert.True(vm.IsGuest);
        Assert.True(vm.HasFrameStepHint);
        Assert.Equal("로그인하면 내 프레임을 쓸 수 있어요", vm.FrameStepHint);
    }

    /// <summary>프레임 저작 권한이 있는 역할에만 "만들 수 있다"고 말한다.</summary>
    [Theory]
    [InlineData(UserRole.AdvancedUser)]
    [InlineData(UserRole.Manager)]
    [InlineData(UserRole.Admin)]
    public void Frame_Authors_Are_Told_They_Can_Create(UserRole role)
    {
        var vm = MakeVm(Account(role));

        Assert.False(vm.IsGuest);
        Assert.True(vm.HasFrameStepHint);
        Assert.Equal("직접 만들 수도 있어요", vm.FrameStepHint);
    }

    /// <summary>
    /// 로그인했지만 만들 수 없는 역할에는 **아무 말도 하지 않는다.**
    /// 여기서 "직접 만들 수도 있어요"가 나오면 거짓 안내가 된다(이 테스트의 존재 이유).
    /// </summary>
    [Theory]
    [InlineData(UserRole.User)]
    [InlineData(UserRole.TempUser)]
    public void Non_Author_Logins_Get_No_Hint(UserRole role)
    {
        var vm = MakeVm(Account(role));

        Assert.False(vm.IsGuest);
        Assert.False(vm.HasFrameStepHint);
        Assert.Equal(string.Empty, vm.FrameStepHint);
    }

    /// <summary>
    /// 문구와 실제 권한 판정이 갈라지지 않도록 고정: "만들 수 있다"고 말하는 경우는
    /// 정확히 CanWriteFrames가 true인 경우여야 한다.
    /// </summary>
    [Theory]
    [InlineData(UserRole.TempUser)]
    [InlineData(UserRole.User)]
    [InlineData(UserRole.AdvancedUser)]
    [InlineData(UserRole.Manager)]
    [InlineData(UserRole.Admin)]
    public void Create_Hint_Matches_CanWriteFrames_Exactly(UserRole role)
    {
        var vm = MakeVm(Account(role));

        bool saysCanCreate = vm.FrameStepHint == "직접 만들 수도 있어요";
        Assert.Equal(role.CanWriteFrames(), saysCanCreate);
    }
}
