using MCPhoto.Core.Frames;

namespace MCPhoto.Tests;

/// <summary>
/// it4 Step 1 (B3): 편집기 화면(캔버스)↔프레임 좌표 변환 순수 함수 검증.
/// scale·origin·왕복·좌우상하 경계 수치를 고정해 WYSIWYG 회귀를 막는다(설계 §2.4).
/// </summary>
public class EditorTransformTests
{
    private const double Eps = 1e-6;

    [Fact]
    public void Compute_Letterboxes_And_Centers()
    {
        // 캔버스 800×600, 프레임 1200×1600(세로) → 세로 제약: scale=600/1600=0.375
        var tf = EditorTransform.Compute(800, 600, 1200, 1600);

        Assert.Equal(0.375, tf.Scale, 6);
        Assert.Equal(450, tf.DisplayWidth, 6);   // 1200*0.375
        Assert.Equal(600, tf.DisplayHeight, 6);  // 1600*0.375
        Assert.Equal(175, tf.OriginX, 6);        // (800-450)/2
        Assert.Equal(0, tf.OriginY, 6);          // (600-600)/2
    }

    [Fact]
    public void FrameToCanvas_Corners_Map_To_Image_Rect()
    {
        var tf = EditorTransform.Compute(800, 600, 1200, 1600);

        var (x0, y0) = tf.FrameToCanvas(0, 0);
        Assert.Equal(175, x0, 6);
        Assert.Equal(0, y0, 6);

        var (x1, y1) = tf.FrameToCanvas(1200, 1600); // 우하단 = 이미지 우하단
        Assert.Equal(625, x1, 6);  // 175+450
        Assert.Equal(600, y1, 6);  // 0+600
    }

    [Fact]
    public void CanvasToFrame_Roundtrips()
    {
        var tf = EditorTransform.Compute(800, 600, 1200, 1600);

        foreach (var (fx, fy) in new[] { (0.0, 0.0), (600.0, 800.0), (1200.0, 1600.0), (300.0, 1100.0) })
        {
            var (cx, cy) = tf.FrameToCanvas(fx, fy);
            var (bx, by) = tf.CanvasToFrame(cx, cy);
            Assert.True(Math.Abs(bx - fx) < 1, $"왕복 X 오차 {bx}!={fx}");
            Assert.True(Math.Abs(by - fy) < 1, $"왕복 Y 오차 {by}!={fy}");
        }
    }

    [Fact]
    public void Left_Boundary_Slot_Touches_Image_Left_Edge()
    {
        // F.x=0 슬롯의 좌변이 이미지 좌변(originX)에 정확히 붙어야(레터박스 여백으로 안 새어나감).
        var tf = EditorTransform.Compute(800, 600, 1200, 1600);
        var (cx, _) = tf.FrameToCanvas(0, 0);
        Assert.Equal(tf.OriginX, cx, 6);
    }

    [Fact]
    public void Right_Boundary_Slot_Reaches_Image_Right_Edge()
    {
        // F.x = frameW - slotW 인 슬롯의 우변이 이미지 우변(originX+dispW)에 정확히 도달.
        var tf = EditorTransform.Compute(800, 600, 1200, 1600);
        int slotW = 300;
        var (cx, _) = tf.FrameToCanvas(1200 - slotW, 0);
        double rightEdge = cx + slotW * tf.Scale;
        Assert.Equal(tf.OriginX + tf.DisplayWidth, rightEdge, 6);
    }

    [Fact]
    public void Landscape_Frame_Letterboxes_Vertically()
    {
        // 가로 제약 케이스: 캔버스 800×600, 프레임 1600×900 → scale=800/1600=0.5, 상하 여백.
        var tf = EditorTransform.Compute(800, 600, 1600, 900);
        Assert.Equal(0.5, tf.Scale, 6);
        Assert.Equal(800, tf.DisplayWidth, 6);
        Assert.Equal(450, tf.DisplayHeight, 6);
        Assert.Equal(0, tf.OriginX, 6);
        Assert.Equal(75, tf.OriginY, 6); // (600-450)/2
    }

    [Theory]
    [InlineData(0, 600, 1200, 1600)]   // 캔버스 폭 0
    [InlineData(800, 0, 1200, 1600)]   // 캔버스 높이 0
    [InlineData(800, 600, 0, 1600)]    // 프레임 폭 0
    [InlineData(800, 600, 1200, 0)]    // 프레임 높이 0
    public void Zero_Or_Negative_Sizes_Produce_Invalid_Transform(double cw, double ch, int fw, int fh)
    {
        var tf = EditorTransform.Compute(cw, ch, fw, fh);
        Assert.False(tf.IsValid);
        Assert.Equal(0, tf.Scale, 6);
        // 무효 변환의 CanvasToFrame은 (0,0) 안전 반환(0 나눗셈 방지).
        var (fx, fy) = tf.CanvasToFrame(100, 100);
        Assert.Equal(0, fx, 6);
        Assert.Equal(0, fy, 6);
    }
}
