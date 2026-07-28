using MCPhoto.Core.Settings;

namespace MCPhoto.Tests;

/// <summary>
/// it13 §7.1b: 런타임 effective QR 진리표. raw ini 값 + 로그인 + TempUser 한도상태 → effective on/off.
/// 핵심: raw=true여도 미로그인/TempUser초과면 effective=false지만 입력 raw는 불변(오버라이드일 뿐, ini 미변경).
/// </summary>
public class QrEffectivePolicyTests
{
    [Theory]
    // 미로그인(게스트): raw 무관 항상 false(기존 IsLoggedIn 조건 흡수).
    [InlineData(true, false, false, false)]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, true, false)]
    // 로그인 + TempUser 초과: raw 무관 false(신규).
    [InlineData(true, true, true, false)]
    [InlineData(false, true, true, false)]
    // 로그인 + 미초과(User/Manager/Admin·정상 TempUser): raw 그대로.
    [InlineData(true, true, false, true)]
    [InlineData(false, true, false, false)]
    public void IsQrEnabled_TruthTable(bool rawEnableQr, bool isLoggedIn, bool isTempUserBlocked, bool expected)
        => Assert.Equal(expected, QrEffectivePolicy.IsQrEnabled(rawEnableQr, isLoggedIn, isTempUserBlocked));

    [Fact]
    public void IsQrEnabled_Does_Not_Mutate_Raw_Input()
    {
        // raw=true·TempUser초과 → effective=false지만 raw 값은 그대로(함수는 입력을 변경하지 않는 순수 로직).
        bool raw = true;
        var effective = QrEffectivePolicy.IsQrEnabled(rawEnableQr: raw, isLoggedIn: true, isTempUserBlocked: true);
        Assert.False(effective);
        Assert.True(raw); // 오버라이드는 계산값일 뿐 — raw는 불변
    }
}
