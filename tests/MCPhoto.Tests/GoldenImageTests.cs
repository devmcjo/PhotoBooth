using System.IO;
using MCPhoto.Capture;
using MCPhoto.Core.Capture;
using MCPhoto.Core.Models;
using OpenCvSharp;

namespace MCPhoto.Tests;

/// <summary>
/// 골든 이미지 생성·검증 — `docs/spec-vectors/golden/` (docs/web-client/10 §4)
///
/// <para>
/// 웹 합성이 Windows와 **픽셀 수준으로** 같은지 검증하려면 기준 이미지가 필요하다.
/// 이 테스트가 그 기준을 만들고(최초 1회) 이후에는 **회귀 게이트**로 동작한다 —
/// Windows 합성 코드가 바뀌면 커밋된 기준과 달라져 여기서 먼저 실패한다.
/// 웹은 `webclient/tests/golden/golden.test.ts`가 **같은 파일**을 읽어 대조한다.
/// </para>
///
/// <para>
/// 입력을 코드로 생성하는 이유: 바이너리 입력을 커밋하면 "어떻게 만든 것인지"가 사라진다.
/// 결정적 패턴(체커보드·그라데이션·피부톤·고주파)이라 어느 기계에서도 같은 값이 나온다.
/// </para>
/// </summary>
public class GoldenImageTests
{
    private const int FrameWidth = 1200;
    private const int FrameHeight = 1600;
    private const int CutWidth = 1080;
    private const int CutHeight = 1440;

    private static readonly string GoldenDir = FindGoldenDir();
    private static string InputDir => Path.Combine(GoldenDir, "input");

    private static string FindGoldenDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "spec-vectors");
            if (Directory.Exists(candidate)) return Path.Combine(candidate, "golden");
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("docs/spec-vectors 를 찾을 수 없습니다.");
    }

    // ── 결정적 테스트 패턴(10 §4.1) ──

    /// <summary>체커보드 — 슬롯 경계가 1px만 밀려도 눈에 띄는 고대비 패턴.</summary>
    private static Mat MakeCheckerboard(int width, int height, int cell)
    {
        var mat = new Mat(height, width, MatType.CV_8UC3);
        var indexer = mat.GetGenericIndexer<Vec3b>();
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                byte v = (byte)(((x / cell + y / cell) % 2 == 0) ? 235 : 20);
                indexer[y, x] = new Vec3b(v, v, v);
            }
        return mat;
    }

    /// <summary>그라데이션 — 보간·양자화 오차를 드러낸다.</summary>
    private static Mat MakeGradient(int width, int height)
    {
        var mat = new Mat(height, width, MatType.CV_8UC3);
        var indexer = mat.GetGenericIndexer<Vec3b>();
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                // BGR 순서(OpenCV)
                indexer[y, x] = new Vec3b(
                    (byte)(255 - x * 255 / Math.Max(1, width - 1)),
                    (byte)(y * 255 / Math.Max(1, height - 1)),
                    (byte)(x * 255 / Math.Max(1, width - 1)));
            }
        return mat;
    }

    /// <summary>피부톤 패치 — 뷰티 필터가 실제로 다루는 색 영역.</summary>
    private static Mat MakeSkinTone(int width, int height)
    {
        var mat = new Mat(height, width, MatType.CV_8UC3);
        var indexer = mat.GetGenericIndexer<Vec3b>();
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                // 완만한 음영이 있는 살구색 + 약한 노이즈(경계 보존 여부를 본다)
                int shade = (x + y) % 24;
                indexer[y, x] = new Vec3b(
                    (byte)Math.Clamp(170 + shade - 12, 0, 255),
                    (byte)Math.Clamp(196 + shade - 12, 0, 255),
                    (byte)Math.Clamp(226 + shade - 12, 0, 255));
            }
        return mat;
    }

    /// <summary>고주파 패턴 — 축소 보간 차이가 가장 크게 드러난다.</summary>
    private static Mat MakeHighFrequency(int width, int height)
    {
        var mat = new Mat(height, width, MatType.CV_8UC3);
        var indexer = mat.GetGenericIndexer<Vec3b>();
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                byte v = (byte)((x % 2 == 0) ^ (y % 3 == 0) ? 250 : 5);
                indexer[y, x] = new Vec3b(v, (byte)(255 - v), v);
            }
        return mat;
    }

    /// <summary>프레임 배경: 하양 + 슬롯 자리를 알아볼 수 있는 옅은 테두리.</summary>
    private static Mat MakeFrameImage(IReadOnlyList<Slot> slots)
    {
        var mat = new Mat(FrameHeight, FrameWidth, MatType.CV_8UC3, Scalar.All(255));
        foreach (var slot in slots)
        {
            Cv2.Rectangle(
                mat,
                new Rect(slot.X - 4, slot.Y - 4, slot.Width + 8, slot.Height + 8),
                new Scalar(200, 200, 200),
                thickness: 4);
        }
        return mat;
    }

    /// <summary>fallback 프레임과 같은 2×2 배치(analysis/14 §4.7) — 웹과 좌표를 공유한다.</summary>
    private static List<Slot> GoldenSlots()
    {
        const int margin = 80;
        const int gap = 60;
        int cellW = (FrameWidth - margin * 2 - gap) / 2;
        int cellH = (int)(cellW * 4.0 / 3.0);
        int top = (FrameHeight - (cellH * 2 + gap)) / 2;
        int right = margin + cellW + gap;
        int bottom = top + cellH + gap;

        return new List<Slot>
        {
            new() { Index = 0, X = margin, Y = top, Width = cellW, Height = cellH },
            new() { Index = 1, X = right, Y = top, Width = cellW, Height = cellH },
            new() { Index = 2, X = margin, Y = bottom, Width = cellW, Height = cellH },
            new() { Index = 3, X = right, Y = bottom, Width = cellW, Height = cellH },
        };
    }

    private static CapturedStill ToStill(Mat mat)
    {
        var pixels = new byte[mat.Width * mat.Height * 3];
        System.Runtime.InteropServices.Marshal.Copy(mat.Data, pixels, 0, pixels.Length);
        return new CapturedStill { Width = mat.Width, Height = mat.Height, Pixels = pixels };
    }

    /// <summary>파일이 없으면 쓰고(최초 생성), 있으면 바이트가 같은지 확인한다(회귀 게이트).</summary>
    private static void WriteOrVerify(string path, Mat image)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path))
        {
            Cv2.ImWrite(path, image);
            return;
        }

        using var committed = Cv2.ImRead(path, ImreadModes.Color);
        Assert.False(committed.Empty(), $"골든 파일을 읽을 수 없습니다: {path}");
        Assert.Equal(committed.Width, image.Width);
        Assert.Equal(committed.Height, image.Height);

        using var diff = new Mat();
        Cv2.Absdiff(committed, image, diff);
        Cv2.MinMaxLoc(diff.Reshape(1), out _, out double maxDiff);
        // PNG는 무손실이라 완전 동일해야 한다. 다르면 합성 코드가 바뀐 것이다.
        Assert.True(
            maxDiff == 0,
            $"골든 이미지가 현재 합성 결과와 다릅니다(최대 차이 {maxDiff}): {path}\n" +
            "합성 규격을 의도적으로 바꿨다면 이 파일을 지우고 테스트를 다시 실행해 재생성하세요.");
    }

    [Fact]
    public void Golden_Inputs_And_Expected_Outputs_Are_Reproducible()
    {
        var slots = GoldenSlots();

        using var frameImage = MakeFrameImage(slots);
        using var cut0 = MakeCheckerboard(CutWidth, CutHeight, 64);
        using var cut1 = MakeGradient(CutWidth, CutHeight);
        using var cut2 = MakeSkinTone(CutWidth, CutHeight);
        using var cut3 = MakeHighFrequency(CutWidth, CutHeight);

        // 입력도 커밋한다 — 웹이 같은 픽셀에서 출발해야 비교가 성립한다.
        WriteOrVerify(Path.Combine(InputDir, "frame.png"), frameImage);
        WriteOrVerify(Path.Combine(InputDir, "cut0-checkerboard.png"), cut0);
        WriteOrVerify(Path.Combine(InputDir, "cut1-gradient.png"), cut1);
        WriteOrVerify(Path.Combine(InputDir, "cut2-skintone.png"), cut2);
        WriteOrVerify(Path.Combine(InputDir, "cut3-highfreq.png"), cut3);

        // 슬롯 좌표도 함께 커밋한다(웹이 같은 배치를 쓰도록).
        var slotsJson = "[\n" + string.Join(",\n", slots.ConvertAll(s =>
            $"  {{ \"index\": {s.Index}, \"x\": {s.X}, \"y\": {s.Y}, \"width\": {s.Width}, \"height\": {s.Height} }}"))
            + "\n]\n";
        var slotsPath = Path.Combine(InputDir, "slots.json");
        if (!File.Exists(slotsPath)) File.WriteAllText(slotsPath, slotsJson);
        else Assert.Equal(File.ReadAllText(slotsPath).ReplaceLineEndings("\n"), slotsJson);

        var frame = new FrameTemplate
        {
            Id = "golden",
            Name = "골든 프레임",
            IsDefault = true,
            ImageUrl = Path.Combine(InputDir, "frame.png"),
            ImageSize = new ImageSize { Width = FrameWidth, Height = FrameHeight },
            Slots = slots,
        };
        var cuts = new List<CapturedStill> { ToStill(cut0), ToStill(cut1), ToStill(cut2), ToStill(cut3) };

        var service = new CompositionService();
        foreach (var filter in new[]
                 {
                     FilterKind.None, FilterKind.Grayscale, FilterKind.Brightness, FilterKind.Beauty,
                 })
        {
            var name = filter.ToString().ToLowerInvariant();
            var outputPath = Path.Combine(Path.GetTempPath(), $"mcphoto_golden_{name}_{Guid.NewGuid():N}.png");
            try
            {
                service.ComposeAsync(frame, cuts, filter, outputPath).GetAwaiter().GetResult();
                using var produced = Cv2.ImRead(outputPath, ImreadModes.Color);
                Assert.False(produced.Empty());
                WriteOrVerify(Path.Combine(GoldenDir, $"expected-{name}.png"), produced);
            }
            finally
            {
                try { File.Delete(outputPath); } catch { /* 무시 */ }
            }
        }

        foreach (var mat in cuts) { /* CapturedStill은 관리 배열이라 해제 불요 */ _ = mat; }
    }
}
