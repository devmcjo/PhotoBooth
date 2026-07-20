using MCPhoto.Core.Models;

namespace MCPhoto.Core.Capture;

/// <summary>
/// 슬롯에 컷을 배치할 때의 소스 크롭 사각형 계산(cover 방식, 왜곡 없음). (architecture §3.4, PRD §F4)
/// 캡처가 이미 슬롯 종횡비면 크롭 없이 전체 사용, 슬롯 종횡비가 다르면 슬롯별 중앙 크롭 보정.
/// </summary>
public static class SlotPlacement
{
    /// <summary>
    /// 소스 이미지(srcW×srcH)를 슬롯 종횡비(slotW/slotH)에 맞춰 cover(중앙 크롭)할 소스 Rect.
    /// 이 Rect를 슬롯 픽셀 영역에 uniform 스케일로 채우면 왜곡 없이 슬롯을 덮는다.
    /// </summary>
    public static CropRect SourceCropForSlot(int srcW, int srcH, int slotW, int slotH)
    {
        if (srcW <= 0 || srcH <= 0 || slotW <= 0 || slotH <= 0)
            return new CropRect(0, 0, Math.Max(0, srcW), Math.Max(0, srcH));

        double slotAspect = (double)slotW / slotH;
        // 슬롯 종횡비로 소스를 중앙 크롭(캡처가 이미 슬롯 비율이면 결과는 전체와 동일)
        return CropCalculator.CenterCrop(srcW, srcH, slotAspect);
    }

    /// <summary>
    /// 프레임 슬롯이 프레임 이미지 경계 내에 있도록 클램프한 목적지 Rect.
    /// (합성 시 슬롯이 프레임 밖으로 나가는 이상 데이터 방어)
    /// </summary>
    public static CropRect ClampSlotToFrame(Slot slot, int frameW, int frameH)
    {
        int x = Math.Clamp(slot.X, 0, Math.Max(0, frameW - 1));
        int y = Math.Clamp(slot.Y, 0, Math.Max(0, frameH - 1));
        int w = Math.Clamp(slot.Width, 1, frameW - x);
        int h = Math.Clamp(slot.Height, 1, frameH - y);
        return new CropRect(x, y, w, h);
    }
}
