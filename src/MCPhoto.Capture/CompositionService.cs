using System.IO;
using MCPhoto.Core.Capture;
using MCPhoto.Core.Models;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace MCPhoto.Capture;

/// <summary>
/// 배경형 합성. 프레임 이미지=배경 레이어, 필터 적용 컷=슬롯 픽셀 영역 위. (architecture §3.4, PRD §F4)
/// 출력 해상도 = 프레임 원본. 캡처가 이미 슬롯 종횡비라 왜곡 없이 배치(슬롯별 중앙 크롭 보정).
/// </summary>
public sealed class CompositionService : ICompositionService
{
    private readonly ILogger<CompositionService>? _logger;

    public CompositionService(ILogger<CompositionService>? logger = null)
    {
        _logger = logger;
    }

    public Task<string> ComposeAsync(
        FrameTemplate frame,
        IReadOnlyList<CapturedStill> cuts,
        FilterKind filter,
        string outputPath,
        CancellationToken ct = default)
    {
        return Task.Run(() => Compose(frame, cuts, filter, outputPath), ct);
    }

    private string Compose(FrameTemplate frame, IReadOnlyList<CapturedStill> cuts, FilterKind filter, string outputPath)
    {
        if (cuts.Count != frame.Slots.Count)
            throw new ArgumentException(
                $"컷 수({cuts.Count})가 슬롯 수({frame.Slots.Count})와 다릅니다(정확히 슬롯 수만큼 필요).");

        // 배경 = 프레임 이미지(원본 해상도). 로컬 경로 또는 URL(다운로드된 로컬 캐시 전제).
        using Mat background = LoadFrameImage(frame);
        int frameW = background.Width;
        int frameH = background.Height;

        // 슬롯 인덱스 순서대로 배치(선택 순서 = 슬롯 순서). Slot.Index로 정렬.
        var orderedSlots = frame.Slots.OrderBy(s => s.Index).ToList();

        for (int i = 0; i < orderedSlots.Count; i++)
        {
            var slot = orderedSlots[i];
            var cut = cuts[i];

            var slotRect = SlotPlacement.ClampSlotToFrame(slot, frameW, frameH);

            // 컷(BGR24 byte[]) → Mat
            using Mat cutMat = StillToMat(cut);
            // 필터 적용(전체 컷 일괄)
            using Mat filtered = Filters.Apply(cutMat, filter);

            // 슬롯 종횡비로 소스 중앙 크롭(cover) → uniform 스케일로 슬롯 채움
            var srcCrop = SlotPlacement.SourceCropForSlot(filtered.Width, filtered.Height, slotRect.Width, slotRect.Height);
            using Mat srcRoi = new Mat(filtered, new Rect(srcCrop.X, srcCrop.Y, srcCrop.Width, srcCrop.Height));

            using Mat scaled = new Mat();
            Cv2.Resize(srcRoi, scaled, new Size(slotRect.Width, slotRect.Height), 0, 0, InterpolationFlags.Area);

            // 배경의 슬롯 영역에 덮어쓰기
            using Mat destRoi = new Mat(background, new Rect(slotRect.X, slotRect.Y, slotRect.Width, slotRect.Height));
            scaled.CopyTo(destRoi);
        }

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        // 출력 포맷은 outputPath 확장자로 결정(호출부가 outputFormat 반영)
        Cv2.ImWrite(outputPath, background);
        _logger?.LogInformation("합성 완료: {Path} ({W}x{H}, {Slots}슬롯)", outputPath, frameW, frameH, orderedSlots.Count);
        return outputPath;
    }

    /// <summary>프레임 이미지 로드. 로컬 파일 경로 우선(URL은 상위에서 로컬 캐시로 다운로드 전제).</summary>
    private static Mat LoadFrameImage(FrameTemplate frame)
    {
        var path = frame.ImageUrl;
        if (File.Exists(path))
        {
            var mat = Cv2.ImRead(path, ImreadModes.Color);
            if (!mat.Empty()) return mat;
            mat.Dispose();
        }
        throw new FileNotFoundException($"프레임 이미지를 찾을 수 없습니다: {path}");
    }

    /// <summary>CapturedStill(BGR24) → Mat(CV_8UC3).</summary>
    private static Mat StillToMat(CapturedStill still)
    {
        int stride = still.Width * 3;
        // 픽셀 배열을 복사해 소유권 있는 Mat 생성
        var mat = Mat.FromPixelData(still.Height, still.Width, MatType.CV_8UC3, still.Pixels, stride);
        return mat.Clone(); // FromPixelData는 외부 버퍼 참조 → Clone으로 독립
    }
}
