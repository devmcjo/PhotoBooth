using MCPhoto.Core.Devices;

namespace MCPhoto.Core.Capture;

/// <summary>
/// 외부 카메라(DSLR) 수신 스틸의 정규화 계획(순수 기하 결정). (it23 §5.2)
/// <para>
/// OpenCV 연산은 <c>MCPhoto.Capture</c>의 디코더가 집행하고, "무엇을 어떻게 자르고 줄일지"의 결정만
/// 여기서 한다 — 그래야 WYSIWYG의 핵심 규칙을 실물 장비·OpenCV 없이 headless로 검증할 수 있다.
/// </para>
/// </summary>
/// <param name="Mirror">거울반전 필요 여부(웹캠과 동일한 <c>settings.MirrorMode</c> 조건).</param>
/// <param name="Crop">거울반전 <b>후</b> 적용할 중앙 크롭 사각형(웹캠과 같은 <see cref="CropCalculator.CenterCrop"/> 결과).</param>
/// <param name="TargetWidth">최종 폭(축소 상한 적용 후). 크롭 결과와 같으면 축소 없음.</param>
/// <param name="TargetHeight">최종 높이(축소 상한 적용 후).</param>
public readonly record struct ExternalStillNormalizePlan(
    bool Mirror,
    CropRect Crop,
    int TargetWidth,
    int TargetHeight)
{
    /// <summary>축소가 필요한지(크롭 크기 ≠ 목표 크기).</summary>
    public bool NeedsDownscale => TargetWidth != Crop.Width || TargetHeight != Crop.Height;

    /// <summary>
    /// 정규화 계획 산출. 순서는 웹캠 캡처 스레드와 <b>동일</b>하다:
    /// ① 거울반전 → ② 대표 슬롯 종횡비 중앙 크롭 → ③ 긴 변 상한 축소.
    /// <para>
    /// ①②가 웹캠과 같은 규칙·같은 함수(<see cref="CropCalculator.CenterCrop"/>)라는 것이 이 설계의 요점이다.
    /// 규칙을 복제하지 않고 재사용하므로 웹캠 컷과 DSLR 컷의 기하가 어긋날 수가 없다 —
    /// 혼합 소스 세션(§6.4 강등)에서도 컷들이 같은 종횡비·같은 거울 방향으로 남는다.
    /// </para>
    /// <para>
    /// ⚠️ 거울반전은 중앙 크롭 사각형에 영향을 주지 않는다(좌우 대칭이므로 중앙 기준 크롭은 동일).
    /// 그래서 <see cref="Crop"/>은 반전 전/후 어느 쪽으로 계산해도 같지만, 집행 순서는 웹캠과 맞춘다.
    /// </para>
    /// </summary>
    /// <param name="srcWidth">수신 이미지 원본 폭.</param>
    /// <param name="srcHeight">수신 이미지 원본 높이.</param>
    /// <param name="slotAspect">대표 슬롯 종횡비(가로/세로). 0 이하면 크롭 없음.</param>
    /// <param name="mirror">거울모드 설정값.</param>
    /// <param name="maxLongEdge">긴 변 상한(px). 0 이하면 축소 없음.</param>
    public static ExternalStillNormalizePlan Compute(
        int srcWidth,
        int srcHeight,
        double slotAspect,
        bool mirror,
        int maxLongEdge = ExternalCapturePolicy.MaxIngestLongEdge)
    {
        var crop = CropCalculator.CenterCrop(srcWidth, srcHeight, slotAspect);

        int w = crop.Width;
        int h = crop.Height;

        int longEdge = Math.Max(w, h);
        if (maxLongEdge > 0 && longEdge > maxLongEdge)
        {
            // 균등 축소(종횡비 보존). 반올림으로 0이 되는 것을 막아 최소 1px 보장 —
            // 0폭 Mat은 OpenCV resize에서 예외가 되고, 그 예외는 컷 실패로 오인된다.
            double scale = maxLongEdge / (double)longEdge;
            w = Math.Max(1, (int)Math.Round(crop.Width * scale));
            h = Math.Max(1, (int)Math.Round(crop.Height * scale));
        }

        return new ExternalStillNormalizePlan(mirror, crop, w, h);
    }
}
