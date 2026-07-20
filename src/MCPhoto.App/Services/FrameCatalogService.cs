using System.IO;
using MCPhoto.Capture;
using MCPhoto.Core.Frames;
using MCPhoto.Core.Models;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.Services;

/// <summary>
/// 사용 가능한 프레임 목록 제공. 우선순위: ①DB isDefault → ②설치 Frame/ 번들 → ③fallback. (§9 #11)
/// 오프라인/DB 미초기화 시 ②/③로 폴백(게스트+번들 모드). 로그인 시 커스텀 프레임 추가.
/// </summary>
public sealed class FrameCatalogService
{
    private readonly IFrameRepository _repository;
    private readonly ILogger<FrameCatalogService>? _logger;

    /// <summary>번들 프레임 폴더(설치 경로/Frame).</summary>
    public string BundleFolder { get; }

    /// <summary>fallback 프레임 이미지 캐시 경로(%ProgramData%\MCPhoto\).</summary>
    public string FallbackImagePath { get; }

    public FrameCatalogService(IFrameRepository repository, ILogger<FrameCatalogService>? logger = null)
    {
        _repository = repository;
        _logger = logger;
        BundleFolder = Path.Combine(AppContext.BaseDirectory, "Frame");
        FallbackImagePath = Path.Combine(App.DataFolder, "cache", "fallback_frame.png");
    }

    /// <summary>기본 프레임(게스트 포함) 목록. DB → 번들 → fallback 우선순위.</summary>
    public async Task<IReadOnlyList<FrameTemplate>> GetDefaultFramesAsync(CancellationToken ct = default)
    {
        // ① DB 기본 프레임
        try
        {
            var dbFrames = await _repository.GetDefaultFramesAsync(ct);
            if (dbFrames.Count > 0)
            {
                _logger?.LogInformation("DB 기본 프레임 {Count}개 사용", dbFrames.Count);
                return dbFrames;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "DB 기본 프레임 조회 실패 — 번들/fallback로 폴백(오프라인 모드)");
        }

        // ② 번들 Frame/ 폴더
        var bundled = LoadBundleFrames();
        if (bundled.Count > 0)
        {
            _logger?.LogInformation("번들 프레임 {Count}개 사용", bundled.Count);
            return bundled;
        }

        // ③ fallback(코드 생성)
        _logger?.LogInformation("fallback 프레임 생성");
        return new[] { EnsureFallbackFrame() };
    }

    /// <summary>로그인 사용자 커스텀 프레임(있으면). 실패 시 빈 목록.</summary>
    public async Task<IReadOnlyList<FrameTemplate>> GetUserFramesAsync(string userId, CancellationToken ct = default)
    {
        try { return await _repository.GetUserFramesAsync(userId, ct); }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "커스텀 프레임 조회 실패: {User}", userId);
            return Array.Empty<FrameTemplate>();
        }
    }

    private List<FrameTemplate> LoadBundleFrames()
    {
        var list = new List<FrameTemplate>();
        if (!Directory.Exists(BundleFolder)) return list;

        // 번들 규약: Frame/{name}.png + Frame/{name}.slots(선택). slots 없으면 fallback 격자 배치.
        foreach (var img in Directory.EnumerateFiles(BundleFolder)
                     .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                              || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                              || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var (w, h) = ReadImageSize(img);
                var name = Path.GetFileNameWithoutExtension(img);
                var template = new FrameTemplate
                {
                    Id = $"bundle:{name}",
                    Name = name,
                    IsDefault = true,
                    ImageUrl = img,
                    ImageSize = new ImageSize { Width = w, Height = h }
                };
                LoadOrGenerateSlots(template, img);
                list.Add(template);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "번들 프레임 로드 실패: {Path}", img);
            }
        }
        return list;
    }

    private void LoadOrGenerateSlots(FrameTemplate template, string imagePath)
    {
        var slotFile = Path.ChangeExtension(imagePath, ".slots");
        if (File.Exists(slotFile))
        {
            // 규약: 한 줄에 "index,x,y,w,h"
            foreach (var line in File.ReadAllLines(slotFile))
            {
                var parts = line.Split(',');
                if (parts.Length == 5
                    && int.TryParse(parts[0], out var idx) && int.TryParse(parts[1], out var x)
                    && int.TryParse(parts[2], out var y) && int.TryParse(parts[3], out var w)
                    && int.TryParse(parts[4], out var h))
                {
                    template.Slots.Add(new Slot { Index = idx, X = x, Y = y, Width = w, Height = h });
                }
            }
        }

        if (template.Slots.Count == 0)
        {
            // slots 파일 없으면 이미지 크기에 맞춰 2×2 격자 자동 배치
            GenerateGridSlots(template);
        }
    }

    private static void GenerateGridSlots(FrameTemplate template)
    {
        int fw = template.ImageSize.Width, fh = template.ImageSize.Height;
        int margin = fw / 15, gap = fw / 20;
        int cellW = (fw - margin * 2 - gap) / 2;
        int cellH = (int)(cellW * 4.0 / 3.0);
        int totalH = cellH * 2 + gap;
        int top = Math.Max(margin, (fh - totalH) / 2);
        int[,] o = { { margin, top }, { margin + cellW + gap, top },
                     { margin, top + cellH + gap }, { margin + cellW + gap, top + cellH + gap } };
        for (int i = 0; i < 4; i++)
            template.Slots.Add(new Slot { Index = i, X = o[i, 0], Y = o[i, 1], Width = cellW, Height = cellH });
    }

    private FrameTemplate EnsureFallbackFrame()
    {
        if (!File.Exists(FallbackImagePath))
            return FallbackFrameRenderer.Create(FallbackImagePath);

        // 이미 생성돼 있으면 템플릿만 재구성
        return DefaultFrameProvider.CreateFallbackTemplate(FallbackImagePath);
    }

    /// <summary>이미지 헤더에서 크기 읽기(OpenCvSharp 디코드).</summary>
    private static (int w, int h) ReadImageSize(string path)
    {
        using var mat = OpenCvSharp.Cv2.ImRead(path, OpenCvSharp.ImreadModes.Color);
        return (mat.Width, mat.Height);
    }
}
