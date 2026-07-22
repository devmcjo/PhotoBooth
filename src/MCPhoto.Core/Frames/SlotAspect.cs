namespace MCPhoto.Core.Frames;

/// <summary>슬롯 종횡비 선택(가로:세로). 캡처 중앙 크롭이 이 비율을 따른다. (it4 §3, PRD §F1/§F36)</summary>
public enum SlotAspect
{
    /// <summary>4:3 (가로 넓음).</summary>
    Ratio4x3,

    /// <summary>3:4 (세로 넓음). 기본.</summary>
    Ratio3x4,

    /// <summary>1:1 (정사각).</summary>
    Ratio1x1
}

/// <summary>SlotAspect 확장.</summary>
public static class SlotAspectExtensions
{
    /// <summary>가로/세로 비율값(width/height).</summary>
    public static double ToRatio(this SlotAspect aspect) => aspect switch
    {
        SlotAspect.Ratio4x3 => 4.0 / 3.0,
        SlotAspect.Ratio3x4 => 3.0 / 4.0,
        SlotAspect.Ratio1x1 => 1.0,
        _ => 3.0 / 4.0
    };

    /// <summary>표시 라벨.</summary>
    public static string ToLabel(this SlotAspect aspect) => aspect switch
    {
        SlotAspect.Ratio4x3 => "4:3",
        SlotAspect.Ratio3x4 => "3:4",
        SlotAspect.Ratio1x1 => "1:1",
        _ => "3:4"
    };
}
