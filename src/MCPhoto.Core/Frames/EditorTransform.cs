namespace MCPhoto.Core.Frames;

/// <summary>
/// 프레임 편집기의 화면(캔버스) ↔ 프레임 원본 좌표 변환(순수 로직, 테스트 대상). (it4 §2)
/// Uniform 스케일 + 중앙 레터박스 정렬. 표시·드래그·클램프가 모두 이 변환을 공유해 WYSIWYG를 보장한다.
/// - 프레임 좌표(F): 슬롯 X/Y/Width/Height·저장·클램프·캡처 크롭 기준(진실의 좌표).
/// - 캔버스 좌표(C): SlotCanvas 내부 Canvas.Left/Top. 슬롯 사각형을 그리는 좌표.
/// scale=Min(canvasW/frameW, canvasH/frameH), origin=중앙 레터박스 여백.
/// </summary>
public readonly struct EditorTransform
{
    /// <summary>프레임→캔버스 배율(Uniform). 크기 0/음수면 0.</summary>
    public double Scale { get; }

    /// <summary>이미지 표시 영역 좌상단 X(캔버스 좌표, 중앙 레터박스 여백).</summary>
    public double OriginX { get; }

    /// <summary>이미지 표시 영역 좌상단 Y.</summary>
    public double OriginY { get; }

    /// <summary>화면에 그려지는 이미지 폭(레터박스 여백 제외).</summary>
    public double DisplayWidth { get; }

    /// <summary>화면에 그려지는 이미지 높이.</summary>
    public double DisplayHeight { get; }

    private EditorTransform(double scale, double originX, double originY, double dispW, double dispH)
    {
        Scale = scale;
        OriginX = originX;
        OriginY = originY;
        DisplayWidth = dispW;
        DisplayHeight = dispH;
    }

    /// <summary>변환 계산. 캔버스/프레임 크기가 0 이하이면 Scale=0인 무효 변환 반환.</summary>
    public static EditorTransform Compute(double canvasW, double canvasH, int frameW, int frameH)
    {
        if (canvasW <= 0 || canvasH <= 0 || frameW <= 0 || frameH <= 0)
            return new EditorTransform(0, 0, 0, 0, 0);

        double scale = Math.Min(canvasW / frameW, canvasH / frameH);
        double dispW = frameW * scale;
        double dispH = frameH * scale;
        double originX = (canvasW - dispW) / 2;
        double originY = (canvasH - dispH) / 2;
        return new EditorTransform(scale, originX, originY, dispW, dispH);
    }

    /// <summary>변환이 유효한지(그리기/이동 가능).</summary>
    public bool IsValid => Scale > 0;

    /// <summary>프레임 좌표 → 캔버스 좌표.</summary>
    public (double x, double y) FrameToCanvas(double fx, double fy)
        => (OriginX + fx * Scale, OriginY + fy * Scale);

    /// <summary>캔버스 좌표 → 프레임 좌표. Scale 0이면 (0,0).</summary>
    public (double x, double y) CanvasToFrame(double cx, double cy)
        => Scale <= 0 ? (0, 0) : ((cx - OriginX) / Scale, (cy - OriginY) / Scale);
}
