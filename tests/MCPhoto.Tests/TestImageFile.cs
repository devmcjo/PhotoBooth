using System.IO;
using OpenCvSharp;

namespace MCPhoto.Tests;

/// <summary>
/// 테스트용 이미지 파일 생성 유틸. 만든 직후 <b>쓰기 성공 + 읽기 가능 + 디코드 가능</b>을 확인한다.
/// </summary>
/// <remarks>
/// 확인이 필요한 이유: Windows에서는 갓 만든 파일을 곧바로 여는 순간이 외부 프로세스(실시간 검사 등)가
/// 그 파일을 잡고 있는 구간과 겹쳐 <see cref="IOException"/>(공유 위반)이 난다. 전체 스위트를 병렬로 돌리면
/// %TEMP% 쓰기가 몰려 이 창이 넓어지고, 픽스처 이미지를 읽는 테스트가 간헐 실패한다
/// (실측 사례: <c>FrameEditorViewModel.LoadForEdit</c>의 <c>File.ReadAllBytes</c>가 IOException →
/// 편집 세션이 미완성으로 남아 저장이 엉뚱한 사유로 차단됨).
/// 그러므로 조용한 실패를 남기지 않고, 끝까지 실패하면 원인이 드러나는 예외를 던진다.
/// </remarks>
internal static class TestImageFile
{
    /// <summary>%TEMP%에 새 이미지 파일을 만들고 경로를 반환한다(호출측 삭제 책임).</summary>
    public static string CreateInTemp(int width, int height, string extension, byte gray = 200)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mcphoto_test_{Guid.NewGuid():N}{extension}");
        Write(path, width, height, gray);
        return path;
    }

    /// <summary>지정 경로에 단색 이미지를 쓰고, 읽기·디코드가 가능해진 것을 확인한 뒤 반환한다.</summary>
    public static void Write(string path, int width, int height, byte gray = 200)
    {
        using (var mat = new Mat(height, width, MatType.CV_8UC3, Scalar.All(gray)))
        {
            if (!Cv2.ImWrite(path, mat))
                throw new InvalidOperationException($"테스트 이미지 쓰기 실패(Cv2.ImWrite=false): {path}");
        }

        WaitUntilReadable(path);

        using var decoded = Cv2.ImRead(path, ImreadModes.Color);
        if (decoded.Empty() || decoded.Width != width || decoded.Height != height)
            throw new InvalidOperationException(
                $"테스트 이미지가 유효하지 않습니다: {path} (empty={decoded.Empty()}, " +
                $"실제={decoded.Width}×{decoded.Height}, 기대={width}×{height})");
    }

    /// <summary>
    /// 공유 위반이 풀릴 때까지 짧게 재시도한다(최대 약 1초). 끝까지 실패하면 그 <see cref="IOException"/>을
    /// 그대로 던져 "누가 파일을 잡고 있었다"는 사실이 실패 메시지에 남게 한다.
    /// 접근 모드는 제품 코드의 읽기 경로(<c>File.ReadAllBytes</c>)와 동일하게 맞춘다.
    /// </summary>
    private static void WaitUntilReadable(string path)
    {
        const int maxRetries = 50;
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (fs.Length <= 0) throw new IOException($"테스트 이미지가 비어 있습니다: {path}");
                return;
            }
            catch (IOException) when (attempt < maxRetries)
            {
                Thread.Sleep(20);
            }
        }
    }
}

/// <summary>
/// <see cref="FrameEditorViewModelTests"/>가 공유하는 픽스처 PNG(1200×1600)를 <b>클래스 단위 1회</b>만 만든다.
/// xUnit은 테스트 메서드마다 클래스를 새로 인스턴스화하므로 생성자에서 만들면 같은 파일의 쓰기→즉시 읽기가
/// 메서드 수만큼 반복되어 공유 위반 창에 그만큼 더 노출된다(<see cref="TestImageFile"/> 주석 참고).
/// 이 파일은 모든 테스트가 읽기 전용으로만 쓰므로 공유해도 간섭이 없다.
/// </summary>
public sealed class FrameImageFixture : IDisposable
{
    /// <summary>1200×1600 PNG 경로. OpenCV 디코드 경로와 <c>File.ReadAllBytes</c> 경로 양쪽에서 읽힌다.</summary>
    public string PngPath { get; } = TestImageFile.CreateInTemp(1200, 1600, ".png");

    public void Dispose()
    {
        try { if (File.Exists(PngPath)) File.Delete(PngPath); } catch { /* 무시 */ }
    }
}
