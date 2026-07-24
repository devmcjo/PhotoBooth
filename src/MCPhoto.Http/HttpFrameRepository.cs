namespace MCPhoto.Http;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using MCPhoto.Core.Frames;
using MCPhoto.Core.Models;
using MCPhoto.Http.Dto;
using MCPhoto.Http.Session;
using Microsoft.Extensions.Logging;

/// <summary>
/// <see cref="IFrameRepository"/>의 HTTP 구현(설계 §5.3). 백엔드 /frames 엔드포인트 호출.
///
/// - 기본 프레임 조회: /frames/default(API키, 게스트 가능).
/// - 사용자 프레임 조회: /frames?userId=(Bearer, 본인/파워).
/// - 저장: POST /frames(Bearer 파워) → 서명 PUT URL 수신 → 이미지 바이트 직접 PUT(설계 §5.4-A).
/// - 삭제: DELETE /frames/{id}(Bearer 파워). 계정 cascade는 서버가 수행(DeleteAllByUser는 클라 no-op).
///
/// 서버는 공용 기본 프레임만 생성한다(user 커스텀은 it8 A2로 로컬 전용). 온라인 전용.
/// </summary>
public sealed class HttpFrameRepository : HttpBackendClient, IFrameRepository
{
    private readonly IHttpClientFactory _httpClientFactory;

    public HttpFrameRepository(
        IHttpClientFactory httpClientFactory,
        IBackendSession session,
        string apiKey,
        ILogger<HttpFrameRepository>? logger = null)
        : base(httpClientFactory, session, apiKey, logger)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<IReadOnlyList<FrameTemplate>> GetDefaultFramesAsync(CancellationToken ct = default)
    {
        try
        {
            var res = await GetJsonAsync<List<FrameResponse>>("frames/default", bearer: false, ct)
                .ConfigureAwait(false);
            return res.Select(ToTemplate).ToList();
        }
        catch (BackendException ex)
        {
            throw MapToDomainException(ex);
        }
    }

    public async Task<IReadOnlyList<FrameTemplate>> GetUserFramesAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            var res = await GetJsonAsync<List<FrameResponse>>(
                $"frames?userId={Uri.EscapeDataString(userId)}", bearer: true, ct)
                .ConfigureAwait(false);
            return res.Select(ToTemplate).ToList();
        }
        catch (BackendException ex)
        {
            throw MapToDomainException(ex);
        }
    }

    public async Task<FrameTemplate> SaveAsync(FrameTemplate frame, byte[] imageBytes, CancellationToken ct = default)
    {
        try
        {
            // 1) 메타 POST → 서명 PUT URL + 서버가 확정한 프레임(id·imageUrl 포함).
            var req = new SaveFrameRequest
            {
                Name = frame.Name,
                IsDefault = true, // 서버가 강제(공용 기본만). 하위 필드 정합 위해 명시.
                ImageSize = new ImageSizeDto { Width = frame.ImageSize.Width, Height = frame.ImageSize.Height },
                Slots = frame.Slots.Select(s => new SlotDto
                {
                    Index = s.Index, X = s.X, Y = s.Y, Width = s.Width, Height = s.Height
                }).ToList(),
            };
            var res = await SendJsonAsync<SaveFrameResponse>(
                HttpMethod.Post, "frames", req, bearer: true, ct).ConfigureAwait(false);

            // 2) 이미지 바이트를 서명 URL로 직접 PUT(필수 헤더 포함). 함수를 경유하지 않는다.
            await PutImageAsync(res.Upload, imageBytes, ct).ConfigureAwait(false);

            return ToTemplate(res.Frame);
        }
        catch (BackendException ex)
        {
            throw MapToDomainException(ex);
        }
    }

    /// <summary>서버에 PUT /frames/{id} 업데이트 엔드포인트가 있어 같은 id 덮어쓰기를 지원한다. (item2 §5)</summary>
    public bool SupportsUpdateById => true;

    /// <summary>
    /// 기존 공용 기본 프레임 업데이트(PUT /frames/{id}, Bearer 파워). name·slots·imageSize 갱신,
    /// id·userId(null)·isDefault(true)·createdAt은 서버가 보존. replaceImage=true면 응답의 서명 URL로
    /// 이미지 바이트를 별도 PUT(같은 Storage 키 덮어쓰기). (item2 §5.2, functions src/routes/frames.ts PUT /:id)
    /// </summary>
    public async Task<FrameTemplate> UpdateAsync(FrameTemplate frame, byte[] imageBytes, bool replaceImage, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(frame.Id))
            throw new InvalidOperationException("업데이트할 프레임 id가 없습니다.");
        try
        {
            // 1) 메타 PUT → (replaceImage=true면) 서명 PUT URL + 서버가 확정한 프레임(id·imageUrl 보존/갱신).
            var req = new UpdateFrameRequest
            {
                Name = frame.Name,
                ImageSize = new ImageSizeDto { Width = frame.ImageSize.Width, Height = frame.ImageSize.Height },
                Slots = frame.Slots.Select(s => new SlotDto
                {
                    Index = s.Index, X = s.X, Y = s.Y, Width = s.Width, Height = s.Height
                }).ToList(),
                ReplaceImage = replaceImage,
            };
            var res = await SendJsonAsync<UpdateFrameResponse>(
                HttpMethod.Put, $"frames/{Uri.EscapeDataString(frame.Id)}", req, bearer: true, ct)
                .ConfigureAwait(false);

            // 2) 이미지 교체 시에만 서명 URL로 바이트 PUT. 미변경이면 서버가 upload를 주지 않는다.
            if (replaceImage && res.Upload is not null)
                await PutImageAsync(res.Upload, imageBytes, ct).ConfigureAwait(false);

            return ToTemplate(res.Frame);
        }
        catch (BackendException ex)
        {
            throw MapToDomainException(ex);
        }
    }

    public async Task<bool> DeleteAsync(string frameId, CancellationToken ct = default)
    {
        try
        {
            var res = await SendJsonAsync<DeleteFrameResponse>(
                HttpMethod.Delete, $"frames/{Uri.EscapeDataString(frameId)}",
                body: null, bearer: true, ct).ConfigureAwait(false);
            return res.Deleted;
        }
        catch (BackendException ex)
        {
            throw MapToDomainException(ex);
        }
    }

    /// <summary>
    /// 계정 소유 프레임 cascade 삭제는 서버가 계정 삭제(DELETE /accounts/{id})와 함께 수행한다(설계 §5.3·F5).
    /// HTTP 경로에서는 별도 엔드포인트가 없으므로 클라 no-op(서버 cascade에 위임).
    /// </summary>
    public Task DeleteAllByUserAsync(string userId, CancellationToken ct = default)
    {
        Logger?.LogInformation(
            "프레임 cascade 삭제는 서버가 계정 삭제와 함께 처리(클라 no-op): userId 길이={Len}", userId.Length);
        return Task.CompletedTask;
    }

    /// <summary>서명 PUT URL로 이미지 바이트 전송. Content-Type 등 requiredHeaders를 서명대로 부착한다.</summary>
    private async Task PutImageAsync(SignedUploadDto upload, byte[] imageBytes, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(HttpBackendClient.HttpClientName);
        using var content = new ByteArrayContent(imageBytes);

        // requiredHeaders 중 Content-Type은 콘텐츠 헤더, 나머지(x-goog-meta-*)는 요청 헤더로 부착.
        using var request = new HttpRequestMessage(HttpMethod.Put, upload.PutUrl) { Content = content };
        foreach (var (key, value) in upload.RequiredHeaders)
        {
            if (string.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                content.Headers.ContentType = new MediaTypeHeaderValue(value);
            else
                request.Headers.TryAddWithoutValidation(key, value);
        }
        if (content.Headers.ContentType is null)
            content.Headers.ContentType = new MediaTypeHeaderValue("image/png");

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Logger?.LogWarning(ex, "프레임 이미지 PUT 실패(네트워크)");
            throw new InvalidOperationException("프레임 이미지 업로드에 실패했습니다(네트워크).", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"프레임 이미지 업로드에 실패했습니다(HTTP {(int)response.StatusCode}).");
        }
    }

    private static FrameTemplate ToTemplate(FrameResponse d)
    {
        var t = new FrameTemplate
        {
            Id = d.Id,
            UserId = d.UserId,
            IsDefault = d.IsDefault,
            Name = d.Name,
            ImageUrl = d.ImageUrl,
            ImageSize = new MCPhoto.Core.Models.ImageSize { Width = d.ImageSize.Width, Height = d.ImageSize.Height },
            CreatedAt = ParseIso(d.CreatedAt),
        };
        foreach (var s in d.Slots)
            t.Slots.Add(new Slot { Index = s.Index, X = s.X, Y = s.Y, Width = s.Width, Height = s.Height });
        return t;
    }

    private static DateTime ParseIso(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return DateTime.UtcNow;
        return DateTime.TryParse(
            iso, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt)
            ? dt
            : DateTime.UtcNow;
    }
}
