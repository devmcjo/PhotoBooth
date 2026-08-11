using MCPhoto.Core.Capture;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace MCPhoto.Capture;

/// <summary>
/// 외부 카메라(DSLR)가 보내온 인코딩 이미지를 웹캠 스틸과 <b>같은 규칙</b>으로 정규화해
/// <see cref="CapturedStill"/>로 변환한다. (it23 §5.2)
/// <para>
/// 이 클래스가 WYSIWYG 계약의 집행 지점이다: 거울반전 → 대표 슬롯 종횡비 중앙 크롭 → 축소 상한을
/// <see cref="OpenCvCameraService"/>와 동일한 연산·동일한 순서로 적용한다. 입구에서 규칙을 통일하므로
/// 하류(컷선택·필터·합성·재촬영)는 컷의 출처를 알 필요가 없다 — 손댈 필요도 없다.
/// </para>
/// <para>
/// ⚠️ 24MP 디코드+크롭은 수백 ms 급이다 — 호출측이 <c>Task.Run</c>으로 UI 스레드 밖에서 호출한다(§12.1).
/// </para>
/// 실패(손상 바이트·빈 배열·예외)는 예외가 아니라 null이다(§11 E11: 캡처 실패로 편입 → 재시도 1회 대상).
/// </summary>
public sealed class ExternalStillDecoder
{
    private readonly ILogger? _logger;

    public ExternalStillDecoder(ILogger? logger = null) => _logger = logger;

    /// <summary>
    /// 수신 바이트를 정규화된 스틸로 변환. 디코드 불가·빈 입력이면 null(예외 금지).
    /// </summary>
    /// <param name="encoded">DSLR이 보낸 인코딩 이미지(JPEG 가정 — Imdecode가 포맷을 판별한다).</param>
    /// <param name="slotAspect">대표 슬롯 종횡비(가로/세로). 웹캠 <c>StartAsync</c>에 넘기는 값과 같아야 한다.</param>
    /// <param name="mirror">거울모드(웹캠과 동일 조건).</param>
    public CapturedStill? Decode(byte[]? encoded, double slotAspect, bool mirror)
    {
        if (encoded is null || encoded.Length == 0)
        {
            _logger?.LogWarning("외부 카메라 스틸 디코드 실패: 빈 바이트");
            return null;
        }

        Mat? decoded = null;
        try
        {
            // ① 디코드. 손상 바이트면 빈 Mat을 돌려준다(예외가 아니다) — 그래서 Empty 검사가 필수다.
            decoded = Cv2.ImDecode(encoded, ImreadModes.Color);
            if (decoded.Empty())
            {
                _logger?.LogWarning("외부 카메라 스틸 디코드 실패: 이미지로 해석되지 않음({Bytes}바이트)", encoded.Length);
                return null;
            }

            var plan = ExternalStillNormalizePlan.Compute(decoded.Width, decoded.Height, slotAspect, mirror);

            // ② 거울반전 — 웹캠(OpenCvCameraService.ProcessAndDispatch)과 동일 연산.
            if (plan.Mirror)
                Cv2.Flip(decoded, decoded, FlipMode.Y);

            // ③ 중앙 크롭 — 웹캠과 같은 CropCalculator 결과를 그대로 ROI로 사용.
            var rect = new Rect(plan.Crop.X, plan.Crop.Y, plan.Crop.Width, plan.Crop.Height);
            using var cropped = new Mat(decoded, rect);

            // ④ 축소 상한(24MP 원시 버퍼가 세션 메모리를 삼키는 것을 막는다).
            //    축소가 없으면 크롭 뷰를 그대로 쓴다(불필요한 복사 회피 — ExtractBgr24가 행별 복사를 처리).
            Mat? resized = null;
            try
            {
                Mat source = cropped;
                if (plan.NeedsDownscale)
                {
                    resized = new Mat();
                    // INTER_AREA는 축소 전용 최적 보간(모아레·에일리어싱 억제).
                    Cv2.Resize(cropped, resized, new Size(plan.TargetWidth, plan.TargetHeight),
                        interpolation: InterpolationFlags.Area);
                    source = resized;
                }

                // ⑤ 연속 BGR24 버퍼 추출(웹캠 스틸과 동일한 픽셀 표현).
                var (buffer, _) = ExtractBgr24(source);
                return new CapturedStill
                {
                    Width = source.Width,
                    Height = source.Height,
                    Pixels = buffer
                };
            }
            finally
            {
                resized?.Dispose();
            }
        }
        catch (Exception ex)
        {
            // 크래시 금지 관례: 어떤 실패도 컷 실패(null)로 강등된다.
            _logger?.LogWarning(ex, "외부 카메라 스틸 정규화 실패(컷 실패로 강등)");
            return null;
        }
        finally
        {
            decoded?.Dispose();
        }
    }

    /// <summary>
    /// Mat(BGR, 3채널)에서 연속 BGR24 바이트 배열 추출(행 패딩 제거).
    /// <see cref="OpenCvCameraService"/>의 동명 헬퍼와 동일 규칙 — ROI Mat은 연속이 아니므로 행별 복사가 필수다.
    /// </summary>
    private static (byte[] buffer, int stride) ExtractBgr24(Mat mat)
    {
        int width = mat.Width;
        int height = mat.Height;
        int stride = width * 3;
        var buffer = new byte[stride * height];

        if (mat.IsContinuous())
        {
            System.Runtime.InteropServices.Marshal.Copy(mat.Data, buffer, 0, buffer.Length);
        }
        else
        {
            for (int row = 0; row < height; row++)
            {
                nint rowPtr = mat.Ptr(row);
                System.Runtime.InteropServices.Marshal.Copy(rowPtr, buffer, row * stride, stride);
            }
        }

        return (buffer, stride);
    }
}
