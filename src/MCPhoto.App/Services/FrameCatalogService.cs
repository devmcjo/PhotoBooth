using System.IO;
using System.Net.Http;
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
    private readonly ILocalFrameStore _localStore;
    private readonly Func<string, CancellationToken, Task<byte[]?>> _downloadImage;
    private readonly ILogger<FrameCatalogService>? _logger;

    /// <summary>번들 프레임 폴더(설치 경로/Frame).</summary>
    public string BundleFolder { get; }

    /// <summary>fallback 프레임 이미지 캐시 경로(%ProgramData%\MCPhoto\).</summary>
    public string FallbackImagePath { get; }

    public FrameCatalogService(
        IFrameRepository repository,
        ILocalFrameStore localStore,
        ILogger<FrameCatalogService>? logger = null,
        Func<string, CancellationToken, Task<byte[]?>>? downloadImage = null)
    {
        _repository = repository;
        _localStore = localStore;
        _logger = logger;
        _downloadImage = downloadImage ?? DefaultDownloadAsync;
        BundleFolder = Path.Combine(AppContext.BaseDirectory, "Frame");
        FallbackImagePath = Path.Combine(App.DataFolder, "cache", "fallback_frame.png");
    }

    /// <summary>
    /// 공용 프레임(게스트 포함). 로컬 공용(번들+파워캐시) 우선 → DB isDefault 중 로컬에 없는 이름만 캐시·병합
    /// (이름 기준 dedup) → 없으면 fallback. 로컬에 이미 있으면 그 이름은 DB 미다운로드. (it8 §3 정정)
    /// </summary>
    public async Task<IReadOnlyList<FrameTemplate>> GetDefaultFramesAsync(CancellationToken ct = default)
    {
        // ① 로컬 공용(접두 없는 파일 = 번들 + 파워 캐시)
        var local = _localStore.LoadPublic();
        var localNames = _localStore.PublicFrameNames();

        // ② DB isDefault 중 로컬에 이름이 없는 것만 다운로드·캐시(이름 기준 dedup, 중복 집계 없음)
        try
        {
            var dbFrames = await _repository.GetDefaultFramesAsync(ct);
            foreach (var f in dbFrames)
            {
                if (localNames.Contains(f.Name)) continue; // 로컬에 이미 있음 → 다운로드 스킵(캐시 히트)
                var cached = await TryCacheAsync(f, ct);
                if (cached is not null) local = Append(local, cached);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "DB 기본 프레임 조회 실패 — 로컬/번들/fallback로 폴백(오프라인 모드)");
        }

        if (local.Count > 0)
        {
            _logger?.LogInformation("공용 프레임 {Count}개(로컬 우선 + DB 캐시 병합)", local.Count);
            return local;
        }

        // ③ 번들 폴더에 .slots 없는 이미지가 있으면 자동 격자 배치로 로드(기존 폴백)
        var bundled = LoadBundleFrames();
        if (bundled.Count > 0)
        {
            _logger?.LogInformation("번들 프레임 {Count}개 사용", bundled.Count);
            return bundled;
        }

        // ④ fallback(코드 생성)
        _logger?.LogInformation("fallback 프레임 생성");
        return new[] { EnsureFallbackFrame() };
    }

    /// <summary>로그인 사용자 커스텀 프레임(로컬 전용, `{계정}_` 접두). DB 미조회. (it8 §3 정정)</summary>
    public Task<IReadOnlyList<FrameTemplate>> GetUserFramesAsync(string userId, CancellationToken ct = default)
    {
        try { return Task.FromResult(_localStore.LoadUser(userId)); }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "로컬 커스텀 프레임 로드 실패: {User}", userId);
            return Task.FromResult((IReadOnlyList<FrameTemplate>)Array.Empty<FrameTemplate>());
        }
    }

    /// <summary>DB 프레임 이미지를 다운로드해 공용 캐시(이름 기반, 접두 없음). 실패 시 null.</summary>
    private async Task<FrameTemplate?> TryCacheAsync(FrameTemplate f, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrEmpty(f.ImageUrl)) return null;
            var bytes = await _downloadImage(f.ImageUrl, ct);
            if (bytes is { Length: > 0 })
                return _localStore.CacheFromDb(f, bytes);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "프레임 캐시 다운로드 실패: {Id}", f.Id);
        }
        return null;
    }

    private static IReadOnlyList<FrameTemplate> Append(IReadOnlyList<FrameTemplate> list, FrameTemplate item)
    {
        var l = new List<FrameTemplate>(list) { item };
        return l;
    }

    private static readonly HttpClient _http = new();
    private static async Task<byte[]?> DefaultDownloadAsync(string url, CancellationToken ct)
    {
        // 로컬 파일 경로(번들/기존 캐시)면 직접 읽기, http면 다운로드.
        if (File.Exists(url)) return await File.ReadAllBytesAsync(url, ct);
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null;
        return await _http.GetByteArrayAsync(url, ct);
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
