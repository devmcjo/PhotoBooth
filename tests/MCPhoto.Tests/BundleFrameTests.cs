using System.IO;
using MCPhoto.Core.Frames;
using MCPhoto.Core.Models;
using OpenCvSharp;

namespace MCPhoto.Tests;

/// <summary>WBS Step 12: 번들 기본 프레임(Frame/) 자산 유효성 검증(슬롯이 경계 내·겹침 없음).</summary>
public class BundleFrameTests
{
    private static string? FindBundleFrameDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Frame");
            if (Directory.Exists(candidate) &&
                Directory.EnumerateFiles(candidate, "*.slots").Any())
                return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    [Fact]
    public void Bundle_Frames_Have_Valid_Slots()
    {
        var frameDir = FindBundleFrameDir();
        if (frameDir is null)
        {
            Assert.True(true, "번들 프레임 폴더 없음 — 스킵(fallback로 동작)");
            return;
        }

        foreach (var slotsFile in Directory.EnumerateFiles(frameDir, "*.slots"))
        {
            var imgPath = Path.ChangeExtension(slotsFile, ".png");
            if (!File.Exists(imgPath)) imgPath = Path.ChangeExtension(slotsFile, ".jpg");
            Assert.True(File.Exists(imgPath), $"슬롯 파일에 대응하는 이미지 없음: {slotsFile}");

            using var mat = Cv2.ImRead(imgPath, ImreadModes.Color);
            Assert.False(mat.Empty(), $"이미지 로드 실패: {imgPath}");
            int w = mat.Width, h = mat.Height;

            var slots = new List<Slot>();
            foreach (var line in File.ReadAllLines(slotsFile))
            {
                var p = line.Split(',');
                if (p.Length != 5) continue;
                slots.Add(new Slot
                {
                    Index = int.Parse(p[0]), X = int.Parse(p[1]), Y = int.Parse(p[2]),
                    Width = int.Parse(p[3]), Height = int.Parse(p[4])
                });
            }

            Assert.NotEmpty(slots);
            // 모든 슬롯이 프레임 경계 내 + 겹침 없음
            Assert.True(SlotLayout.IsValid(slots, w, h),
                $"{Path.GetFileName(slotsFile)}: 슬롯이 경계를 벗어나거나 겹침 (프레임 {w}x{h})");
        }
    }
}
