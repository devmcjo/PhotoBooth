using MCPhoto.Capture;
using MCPhoto.Core.Capture;
using MCPhoto.Core.Devices;
using OpenCvSharp;

namespace MCPhoto.Tests;

/// <summary>
/// it23 Step 4: DSLR 수신 스틸 정규화(WYSIWYG의 핵). 설계 §14.1 T-N1~T-N3 + 디코더 왕복.
/// <para>
/// 이 파일이 지키는 불변식: <b>DSLR 컷의 기하는 웹캠 컷과 같은 함수로 결정된다</b>.
/// 규칙이 복제되면 혼합 소스 세션(강등)에서 컷들의 종횡비·거울 방향이 어긋난다.
/// </para>
/// </summary>
public class ExternalStillNormalizeTests
{
    private const double Slot3x4 = 3.0 / 4.0;   // 세로형 슬롯(현행 기본 폴백)
    private const double Slot4x3 = 4.0 / 3.0;   // 가로형 슬롯

    // ── T-N1: 크롭 사각형이 웹캠과 같은 CropCalculator 결과와 동치 ──

    [Theory]
    [InlineData(6000, 4000, Slot3x4)]   // D5300 24MP 3:2 → 세로 슬롯
    [InlineData(6000, 4000, Slot4x3)]
    [InlineData(1920, 1080, Slot3x4)]   // 웹캠 1080p 대조
    [InlineData(4000, 6000, Slot3x4)]   // 세로 파지(EXIF 회전 적용 후 가정)
    public void Compute_Crop_Matches_CropCalculator(int w, int h, double aspect)
    {
        var plan = ExternalStillNormalizePlan.Compute(w, h, aspect, mirror: false);
        Assert.Equal(CropCalculator.CenterCrop(w, h, aspect), plan.Crop);
    }

    [Fact]
    public void Compute_Crop_Is_Independent_Of_Mirror()
    {
        // 중앙 크롭은 좌우 대칭이므로 거울모드가 사각형을 바꾸지 않는다(집행 순서만 웹캠과 맞춘다).
        var a = ExternalStillNormalizePlan.Compute(6000, 4000, Slot3x4, mirror: false);
        var b = ExternalStillNormalizePlan.Compute(6000, 4000, Slot3x4, mirror: true);
        Assert.Equal(a.Crop, b.Crop);
    }

    [Fact]
    public void Compute_Zero_Aspect_Means_No_Crop()
    {
        var plan = ExternalStillNormalizePlan.Compute(1000, 800, 0, mirror: false, maxLongEdge: 0);
        Assert.Equal(new CropRect(0, 0, 1000, 800), plan.Crop);
    }

    // ── T-N2: 긴 변 상한 축소 ──

    [Fact]
    public void Compute_Downscales_When_Long_Edge_Exceeds_Limit()
    {
        // 6000×4000 + 3:4 → 크롭 3000×4000(긴 변 4000) → 상한 2400 → 1800×2400.
        var plan = ExternalStillNormalizePlan.Compute(6000, 4000, Slot3x4, mirror: false);

        Assert.Equal(3000, plan.Crop.Width);
        Assert.Equal(4000, plan.Crop.Height);
        Assert.True(plan.NeedsDownscale);
        Assert.Equal(ExternalCapturePolicy.MaxIngestLongEdge, Math.Max(plan.TargetWidth, plan.TargetHeight));
        Assert.Equal(1800, plan.TargetWidth);
        Assert.Equal(2400, plan.TargetHeight);
    }

    [Fact]
    public void Compute_Preserves_Aspect_Ratio_When_Downscaling()
    {
        var plan = ExternalStillNormalizePlan.Compute(6000, 4000, Slot4x3, mirror: false);

        double cropAspect = plan.Crop.Width / (double)plan.Crop.Height;
        double targetAspect = plan.TargetWidth / (double)plan.TargetHeight;
        Assert.Equal(cropAspect, targetAspect, precision: 2);
    }

    [Fact]
    public void Compute_Keeps_Original_Size_When_Within_Limit()
    {
        // 1920×1080 + 3:4 → 크롭 810×1080(긴 변 1080 ≤ 2400) → 축소 없음.
        var plan = ExternalStillNormalizePlan.Compute(1920, 1080, Slot3x4, mirror: false);

        Assert.False(plan.NeedsDownscale);
        Assert.Equal(plan.Crop.Width, plan.TargetWidth);
        Assert.Equal(plan.Crop.Height, plan.TargetHeight);
    }

    [Fact]
    public void Compute_Target_Never_Collapses_To_Zero()
    {
        // 극단적으로 납작한 크롭이 축소되며 0px가 되면 OpenCV resize가 예외를 던지고
        // 그 예외가 컷 실패로 오인된다 — 최소 1px 보장.
        var plan = ExternalStillNormalizePlan.Compute(8000, 4, 2000.0, mirror: false, maxLongEdge: 100);

        Assert.True(plan.TargetWidth >= 1);
        Assert.True(plan.TargetHeight >= 1);
    }

    [Fact]
    public void Compute_MaxLongEdge_Zero_Disables_Downscale()
    {
        var plan = ExternalStillNormalizePlan.Compute(6000, 4000, Slot3x4, mirror: false, maxLongEdge: 0);
        Assert.False(plan.NeedsDownscale);
        Assert.Equal(4000, plan.TargetHeight);
    }

    // ── T-N3: mirror 플래그가 plan에 반영 ──

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Compute_Carries_Mirror_Flag(bool mirror)
        => Assert.Equal(mirror, ExternalStillNormalizePlan.Compute(1000, 800, Slot3x4, mirror).Mirror);

    // ── 디코더 왕복(합성 JPEG 바이트 — 실물 카메라 없이 검증) ──

    /// <summary>좌우가 확실히 다른 테스트 이미지(좌=파랑, 우=빨강)를 JPEG로 인코딩.</summary>
    private static byte[] MakeJpeg(int width, int height)
    {
        using var mat = new Mat(height, width, MatType.CV_8UC3, Scalar.Black);
        // 좌측 절반 파랑(BGR), 우측 절반 빨강 → 거울반전 검증에 사용.
        mat.Rectangle(new Rect(0, 0, width / 2, height), new Scalar(255, 0, 0), thickness: -1);
        mat.Rectangle(new Rect(width / 2, 0, width - width / 2, height), new Scalar(0, 0, 255), thickness: -1);
        Cv2.ImEncode(".jpg", mat, out var buf, new[] { (int)ImwriteFlags.JpegQuality, 95 });
        return buf;
    }

    private static (byte b, byte g, byte r) PixelAt(CapturedStill still, int x, int y)
    {
        int i = (y * still.Width + x) * 3;
        return (still.Pixels[i], still.Pixels[i + 1], still.Pixels[i + 2]);
    }

    [Fact]
    public void Decoder_Produces_Still_With_Plan_Dimensions()
    {
        var jpeg = MakeJpeg(1200, 800);
        var plan = ExternalStillNormalizePlan.Compute(1200, 800, Slot3x4, mirror: false);

        var still = new ExternalStillDecoder().Decode(jpeg, Slot3x4, mirror: false);

        Assert.NotNull(still);
        Assert.Equal(plan.TargetWidth, still!.Width);
        Assert.Equal(plan.TargetHeight, still.Height);
        // BGR24 연속 버퍼(웹캠 스틸과 동일 표현) — 크기가 어긋나면 합성이 조용히 깨진다.
        Assert.Equal(still.Width * still.Height * 3, still.Pixels.Length);
    }

    [Fact]
    public void Decoder_Applies_Mirror_Like_Webcam()
    {
        // 좌=파랑/우=빨강 이미지가 mirror=true면 좌우가 뒤바뀌어야 한다(웹캠 FlipMode.Y와 동일).
        // 슬롯 종횡비를 원본과 같게 줘서 크롭이 좌우를 잘라내지 않게 한다(반전만 관측).
        var jpeg = MakeJpeg(1200, 900);
        var decoder = new ExternalStillDecoder();

        var normal = decoder.Decode(jpeg, Slot4x3, mirror: false);
        var mirrored = decoder.Decode(jpeg, Slot4x3, mirror: true);

        Assert.NotNull(normal);
        Assert.NotNull(mirrored);

        var leftNormal = PixelAt(normal!, 4, normal!.Height / 2);
        var leftMirrored = PixelAt(mirrored!, 4, mirrored!.Height / 2);

        Assert.True(leftNormal.b > leftNormal.r);      // 정상: 좌측은 파랑
        Assert.True(leftMirrored.r > leftMirrored.b);  // 반전: 좌측이 빨강으로 바뀜
    }

    [Fact]
    public void Decoder_Downscales_24mp_Class_Input()
    {
        // 실제 24MP JPEG 합성은 느리므로 축소가 필요한 최소 크기로 검증(경로는 동일).
        var jpeg = MakeJpeg(3600, 2400);

        var still = new ExternalStillDecoder().Decode(jpeg, Slot3x4, mirror: false);

        Assert.NotNull(still);
        Assert.Equal(ExternalCapturePolicy.MaxIngestLongEdge, Math.Max(still!.Width, still.Height));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(new byte[0])]
    public void Decoder_Returns_Null_For_Empty_Input(byte[]? bytes)
        => Assert.Null(new ExternalStillDecoder().Decode(bytes, Slot3x4, mirror: false));

    [Fact]
    public void Decoder_Returns_Null_For_Corrupt_Bytes_Without_Throwing()
    {
        // 손상 수신(§11 E11)은 예외가 아니라 null → 컷 실패로 편입되어 재시도 대상이 된다.
        var garbage = new byte[512];
        new Random(1234).NextBytes(garbage);

        var still = new ExternalStillDecoder().Decode(garbage, Slot3x4, mirror: false);

        Assert.Null(still);
    }

    [Fact]
    public void Decoder_Crop_Matches_Webcam_Rule_For_Same_Source_Size()
    {
        // 같은 원본 크기·같은 슬롯 종횡비라면 DSLR 컷과 웹캠 컷의 결과 크기가 동일해야 한다
        // (혼합 소스 세션에서 합성이 컷마다 다르게 늘어나지 않는 근거).
        var jpeg = MakeJpeg(1920, 1080);
        var still = new ExternalStillDecoder().Decode(jpeg, Slot3x4, mirror: true);
        var webcamCrop = CropCalculator.CenterCrop(1920, 1080, Slot3x4);

        Assert.NotNull(still);
        Assert.Equal(webcamCrop.Width, still!.Width);
        Assert.Equal(webcamCrop.Height, still.Height);
    }
}
