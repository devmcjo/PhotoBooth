using MCPhoto.Core.Devices;

namespace MCPhoto.Tests;

/// <summary>
/// it23 Step 1: 외부 카메라 Core 계약(모델 레지스트리·capability 정책·노출 도메인) 검증.
/// SDK·UI와 무관한 순수 타입이므로 실물 장비 없이 전부 결론이 난다(설계 §14.1 T-R1·T-P1).
/// </summary>
public class ExternalCameraContractTests
{
    // ── T-R1: 모델 레지스트리(§3.3) ──

    [Fact]
    public void Registry_Default_Is_D5300_With_Type0011_Module()
    {
        var d = ExternalCameraModels.Default;

        Assert.Equal("NikonD5300", d.Id);
        Assert.Equal("Nikon D5300", d.DisplayName);
        Assert.Equal("Type0011.md3", d.Md3FileName);
        // 현재 활성 항목은 D5300 하나(모델 추가는 표 한 줄).
        Assert.Single(ExternalCameraModels.All);
        Assert.Same(ExternalCameraModels.All[0], d);
    }

    [Theory]
    [InlineData("NikonD5300")]
    [InlineData("nikond5300")]   // 대소문자 무시
    [InlineData("  NikonD5300 ")] // 앞뒤 공백 무시(ini 손입력 대비)
    public void Registry_Find_Matches_Case_And_Whitespace_Insensitively(string id)
        => Assert.Equal("NikonD5300", ExternalCameraModels.Find(id)!.Id);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NikonD5500")]
    public void Registry_Find_Unknown_Id_Returns_Null(string? id)
        => Assert.Null(ExternalCameraModels.Find(id));

    [Fact]
    public void Registry_Resolve_Falls_Back_To_Default()
    {
        // Find는 보정하지 않고(유효성 판정 가능), Resolve는 보정한다(런타임 소비 지점).
        Assert.Same(ExternalCameraModels.Default, ExternalCameraModels.Resolve("NikonD5500"));
        Assert.Same(ExternalCameraModels.Default, ExternalCameraModels.Resolve(null));
        Assert.Equal("NikonD5300", ExternalCameraModels.Resolve("nikond5300").Id);
    }

    [Fact]
    public void Registry_Ids_Are_Unique()
    {
        // 중복 Id는 Find가 앞의 항목만 돌려주는 조용한 버그가 된다(모델 추가 시 회귀 잠금).
        var ids = ExternalCameraModels.All.Select(m => m.Id.ToLowerInvariant()).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    // ── T-P1: capability 게이트(§4.1) ──

    [Fact]
    public void IsOpen_Only_Supported_Opens()
    {
        Assert.True(ExternalCapturePolicy.IsOpen(CapabilityState.Supported));
        Assert.False(ExternalCapturePolicy.IsOpen(CapabilityState.Unsupported));
        // Unknown("확인 못 함")도 닫힌다 — 미검증 경로가 손님 세션에서 처음 실행되지 않게.
        Assert.False(ExternalCapturePolicy.IsOpen(CapabilityState.Unknown));
    }

    [Fact]
    public void DescribeClosed_Distinguishes_Unsupported_From_Unknown()
    {
        // 게이트 판정은 같아도 사유 문구는 달라야 한다(운영자가 점검할 지점이 다르다).
        Assert.Null(ExternalCapturePolicy.DescribeClosed(CapabilityState.Supported));
        Assert.Equal("이 카메라가 지원하지 않는 기능입니다",
            ExternalCapturePolicy.DescribeClosed(CapabilityState.Unsupported));
        Assert.Equal("기능 지원 여부를 확인하지 못했습니다",
            ExternalCapturePolicy.DescribeClosed(CapabilityState.Unknown));
    }

    [Fact]
    public void Policy_Constants_Are_The_Single_Source()
    {
        // 실기 측정 후 조정될 값들(설계 A7) — 흩어지지 않았음을 고정한다.
        Assert.Equal(TimeSpan.FromSeconds(5), ExternalCapturePolicy.ConnectTimeout);
        Assert.Equal(TimeSpan.FromSeconds(10), ExternalCapturePolicy.CaptureTimeout);
        Assert.Equal(2400, ExternalCapturePolicy.MaxIngestLongEdge);
        Assert.Equal(1, ExternalCapturePolicy.CaptureRetryCount);
    }

    [Fact]
    public void Capabilities_AllUnknown_Is_Every_Item_Unknown()
    {
        var c = ExternalCameraCapabilities.AllUnknown;

        Assert.Equal(CapabilityState.Unknown, c.StillCapture);
        Assert.Equal(CapabilityState.Unknown, c.ExposureControl);
        Assert.Equal(CapabilityState.Unknown, c.PhysicalFlash);
        Assert.Equal(CapabilityState.Unknown, c.LiveView);
        Assert.Equal(CapabilityState.Unknown, c.VideoRecord);
        Assert.Null(c.BatteryLevelPercent);
    }

    // ── 노출 도메인 매칭(§10.2 — 근사 매칭 금지) ──

    [Fact]
    public void ExposureDomainEntry_IndexOf_Is_Exact_Match_Only()
    {
        var e = new ExposureDomainEntry(new[] { "1/60", "1/125", "1/250" }, 1);

        Assert.Equal(1, e.IndexOf("1/125"));
        Assert.Equal(1, e.IndexOf(" 1/125 "));   // 공백만 무시
        Assert.Equal(-1, e.IndexOf("1/100"));     // 근사 매칭 금지(몰래 값 바꾸기 방지)
        Assert.Equal(-1, e.IndexOf(""));
        Assert.Equal(-1, e.IndexOf(null));
    }

    [Fact]
    public void ExposureDomainEntry_IndexOf_Ignores_Case()
    {
        var e = new ExposureDomainEntry(new[] { "f/5.6", "F/8" }, 0);

        Assert.Equal(1, e.IndexOf("f/8"));
        Assert.Equal(0, e.IndexOf("F/5.6"));
    }

    [Fact]
    public void ExposureDomainEntry_CurrentValue_Null_When_Index_Unknown()
    {
        Assert.Null(new ExposureDomainEntry(new[] { "100", "200" }, -1).CurrentValue);
        Assert.Null(new ExposureDomainEntry(new[] { "100", "200" }, 5).CurrentValue);
        Assert.Equal("200", new ExposureDomainEntry(new[] { "100", "200" }, 1).CurrentValue);
    }

    [Fact]
    public void ExposureDomain_Indexer_Returns_Per_Parameter_Entry()
    {
        var shutter = new ExposureDomainEntry(new[] { "1/125" }, 0);
        var iso = new ExposureDomainEntry(new[] { "100", "400" }, 1);
        var domain = new ExposureDomain(shutter, Aperture: null, Iso: iso);

        Assert.Same(shutter, domain[ExposureParameter.ShutterSpeed]);
        Assert.Null(domain[ExposureParameter.Aperture]);   // 파라미터별 미지원 표현(모드에 따라 잠김)
        Assert.Same(iso, domain[ExposureParameter.Iso]);
    }

    // ── Null 구현: 추가 멤버도 예외 없이 미지원을 반환한다(회귀 기준) ──

    [Fact]
    public async Task NullExternalCamera_New_Members_Are_Unsupported_Without_Throwing()
    {
        IExternalCamera cam = new NullExternalCamera();

        Assert.Null(cam.ModelName);
        Assert.Equal("외부 카메라 미구성", cam.UnavailableReason);
        Assert.Null(await cam.GetCapabilitiesAsync());
        Assert.Null(await cam.GetExposureDomainAsync());
        Assert.False(await cam.SetExposureAsync(ExposureParameter.Iso, "400"));
        Assert.False(await cam.TrySetPhysicalFlashAsync(true));

        // 구독·해제가 예외 없이 통과한다(발행은 없음).
        void OnChanged(object? s, ExternalCameraConnectionChange e) { }
        cam.ConnectionChanged += OnChanged;
        cam.ConnectionChanged -= OnChanged;
    }
}
