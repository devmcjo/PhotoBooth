using MCPhoto.Core.Settings;

namespace MCPhoto.Tests;

/// <summary>it7 Step 2 (F2): QR 세분화 연동 규칙 — 하위 둘 다 off면 QR 자체 off로 정규화.</summary>
public class QrDeliveryPolicyTests
{
    [Fact]
    public void Both_Off_Disables_Qr()
    {
        var (enableQr, _, _) = QrDeliveryPolicy.Normalize(enableQr: true, sendPhoto: false, sendTimelapse: false);
        Assert.False(enableQr);
    }

    [Theory]
    [InlineData(true, false)]  // 사진만
    [InlineData(false, true)]  // 타임랩스만
    [InlineData(true, true)]   // 둘 다
    public void At_Least_One_On_Keeps_Qr(bool photo, bool timelapse)
    {
        var (enableQr, sp, st) = QrDeliveryPolicy.Normalize(enableQr: true, photo, timelapse);
        Assert.True(enableQr);
        Assert.Equal(photo, sp);      // 하위 값 보존
        Assert.Equal(timelapse, st);
    }

    [Fact]
    public void Qr_Already_Off_Stays_Off_Preserving_Subtoggles()
    {
        // QR off 상태에서는 하위 값 보존(재활성 시 복원).
        var (enableQr, sp, st) = QrDeliveryPolicy.Normalize(enableQr: false, sendPhoto: true, sendTimelapse: false);
        Assert.False(enableQr);
        Assert.True(sp);
        Assert.False(st);
    }

    [Fact]
    public void AppSettings_NormalizeQr_Applies_Rule()
    {
        var s = new AppSettings { EnableQrDelivery = true, SendPhoto = false, SendTimelapse = false };
        s.NormalizeQr();
        Assert.False(s.EnableQrDelivery); // 둘 다 off → QR off
    }

    // ── it8 A5: QR off→on 재활성 시 하위 둘 다 on 강제 ──

    [Fact]
    public void OnReEnabled_Forces_Both_Sub_Toggles_On()
    {
        var (sp, st) = QrDeliveryPolicy.OnReEnabled();
        Assert.True(sp);
        Assert.True(st);
    }
}
