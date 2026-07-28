namespace MCPhoto.Http;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Models;
using MCPhoto.Core.Upload;
using MCPhoto.Http.Dto;
using MCPhoto.Http.Session;
using Microsoft.Extensions.Logging;

/// <summary>
/// <see cref="IFirebaseClient"/>의 HTTP 구현(설계 §5.1·§5.4-A). 저수준 Storage/Firestore 직결을
/// 백엔드 /uploads·/health 호출로 대체하되, <see cref="UploadService"/>가 무변경으로 동작하도록 계약을 캡슐화한다.
///
/// - <see cref="UploadFileAsync"/> = /uploads/prepare(서명 PUT URL 수신) → 파일 PUT(진행률 유지) → 다운로드 토큰 반환.
///   UploadService가 반환 토큰으로 <see cref="UploadContract.TokenDownloadUrl"/>을 재조립하므로,
///   <see cref="Bucket"/>을 서버 버킷과 일치시켜 동일 URL이 나오게 한다.
/// - <see cref="CreateResultSessionAsync"/> = /uploads/commit(resultSession 생성).
/// - <see cref="IsInitialized"/> = base URL 설정됨(구성 사실). 실시간 도달성은 호출 실패→상위 폴백으로 처리.
/// - Query/Delete 계열(U3/U4/U5)은 앱 미호출 + 서버 엔드포인트 없음 → 미지원(NotSupportedException).
/// </summary>
public sealed class HttpFirebaseClient : HttpBackendClient, IFirebaseClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly bool _configured;

    public HttpFirebaseClient(
        IHttpClientFactory httpClientFactory,
        IBackendSession session,
        string apiKey,
        string bucket,
        bool configured,
        ILogger<HttpFirebaseClient>? logger = null)
        : base(httpClientFactory, session, apiKey, logger)
    {
        _httpClientFactory = httpClientFactory;
        _configured = configured;
        Bucket = bucket ?? string.Empty;
    }

    /// <summary>
    /// 백엔드 사용 가능 여부(설계 §5.1). 현행 Firebase의 "키 로드됨"(구성 사실)에 대응하는 HTTP 아날로그로,
    /// base URL이 설정되어 있으면 true. 실제 도달성(네트워크)은 prepare/PUT/commit 호출에서 확인되고,
    /// 실패는 상위(QR off·로컬 저장)가 예외 경로로 폴백한다 — IsInitialized는 실시간 헬스체크로 흔들지 않는다.
    /// </summary>
    public bool IsInitialized => _configured;

    /// <summary>백엔드 도달 여부를 명시 확인(설정 화면 상태 표시용, 선택). /health 성공 시 true.</summary>
    public async Task<bool> ProbeReachableAsync(CancellationToken ct = default)
    {
        if (!_configured) return false;
        try
        {
            var client = _httpClientFactory.CreateClient(HttpBackendClient.HttpClientName);
            using var response = await client.GetAsync("health", ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "백엔드 헬스 체크 실패");
            return false;
        }
    }

    /// <summary>Storage 버킷(토큰 URL 조립용). 서버 prepare 응답으로 갱신된다.</summary>
    public string Bucket { get; private set; }

    public async Task<string> UploadFileAsync(
        string storagePath, string localFilePath, string contentType,
        IProgress<double>? fileProgress = null, CancellationToken ct = default)
    {
        var (sessionId, kind) = ParsePath(storagePath);
        var ext = ExtFromPath(storagePath);

        // 1) prepare: 이 파일 하나에 대한 서명 PUT URL + 다운로드 토큰 URL 발급.
        var prepareReq = new PrepareUploadRequest
        {
            SessionId = sessionId,
            Files = new List<UploadFileEntry>
            {
                new() { Kind = kind, Ext = ext, ContentType = contentType },
            },
        };

        PrepareUploadResponse prepared;
        try
        {
            // it13 §5.1: TempUser면 서버가 prepare에서 한도 선검사(초과 시 403 — Storage 서명 URL 원천 차단).
            //            선택적 Bearer: 로그인 상태면 JWT 부착(신원화), 게스트는 무토큰 익명 통과(§8.6). 서버 optionalBearer와 대칭.
            prepared = await SendJsonOptionalBearerAsync<PrepareUploadResponse>(
                HttpMethod.Post, "uploads/prepare", prepareReq, ct).ConfigureAwait(false);
        }
        catch (BackendException ex)
        {
            throw MapUploadException(ex);   // it13: TempUser 한도 초과(403 사유 code) → QrLimitExceededException
        }

        // 서버 버킷을 신뢰(토큰 URL 재조립 정합). prepare 응답의 첫 upload가 이 파일.
        if (!string.IsNullOrWhiteSpace(prepared.Bucket))
            Bucket = prepared.Bucket;

        PreparedUploadDto? slot = null;
        foreach (var u in prepared.Uploads)
        {
            if (string.Equals(u.Kind, kind, StringComparison.Ordinal)) { slot = u; break; }
        }
        if (slot is null)
            throw new InvalidOperationException($"업로드 준비 응답에 '{kind}' 항목이 없습니다.");

        // 2) 서명 URL로 파일 직접 PUT(진행률 유지).
        await PutFileAsync(slot, localFilePath, contentType, fileProgress, ct).ConfigureAwait(false);

        // 3) 다운로드 토큰 반환. UploadService가 TokenDownloadUrl(Bucket, storagePath, token)로 재조립하면
        //    서버 downloadUrl과 동일해진다(Bucket == 서버 버킷).
        return ExtractToken(slot.DownloadUrl);
    }

    public async Task CreateResultSessionAsync(ResultSession session, CancellationToken ct = default)
    {
        // commit: retentionHours는 (ExpiresAt-CreatedAt) 정수 시간으로 역산(UploadService가 정수 시간으로 세팅).
        var hours = (int)Math.Round((session.ExpiresAt - session.CreatedAt).TotalHours);
        if (hours < 1) hours = 1;

        var req = new CommitUploadRequest
        {
            SessionId = session.Id,
            FinalImageUrl = session.FinalImageUrl,
            TimelapseUrl = session.TimelapseUrl,
            RetentionHours = hours,
            DownloadPageUrl = session.DownloadPageUrl,
        };

        try
        {
            // it13 §5.1: commit도 TempUser면 서버가 트랜잭션으로 한도 재검사 후 qrUsedCount 증가(초과 시 403).
            //            선택적 Bearer: prepare와 동일 신원(로그인 시 JWT, 게스트 무토큰).
            _ = await SendJsonOptionalBearerAsync<ResultSessionResponse>(
                HttpMethod.Post, "uploads/commit", req, ct).ConfigureAwait(false);
        }
        catch (BackendException ex)
        {
            throw MapUploadException(ex);   // it13: TempUser 한도 초과(403 사유 code) → QrLimitExceededException
        }
    }

    /// <summary>
    /// 업로드 예외 매핑(it13 §5.2). TempUser 한도 초과 403(사유 code)은 <see cref="QrLimitExceededException"/>으로
    /// 변환해 QR 팝업이 사유별 정확 문구를 표시하게 한다. 그 외는 기존 <see cref="MapToDomainException"/> 계약 유지.
    /// </summary>
    private static Exception MapUploadException(BackendException ex) => ex.ServerCode switch
    {
        "TEMP_USER_TIME_EXCEEDED" => new QrLimitExceededException(QrGateReason.Time, ex.Message),
        "TEMP_USER_COUNT_EXCEEDED" => new QrLimitExceededException(QrGateReason.Count, ex.Message),
        _ => MapToDomainException(ex),
    };

    /// <summary>만료 정리(U5)는 앱 미호출 + 서버 엔드포인트 없음(설계 §2 note). HTTP 경로 미지원.</summary>
    public Task DeleteStoragePrefixAsync(string prefix, CancellationToken ct = default)
        => throw new NotSupportedException(
            "HTTP 경로에서 Storage prefix 삭제는 지원하지 않습니다(만료 정리는 인프라 TTL 담당).");

    /// <summary>만료 세션 조회(U3)는 앱 미호출 + 서버 엔드포인트 없음. HTTP 경로 미지원.</summary>
    public Task<IReadOnlyList<ResultSession>> QueryExpiredSessionsAsync(DateTime now, CancellationToken ct = default)
        => throw new NotSupportedException(
            "HTTP 경로에서 만료 세션 조회는 지원하지 않습니다(만료 정리는 인프라 TTL 담당).");

    /// <summary>만료 세션 삭제(U4)는 앱 미호출 + 서버 엔드포인트 없음. HTTP 경로 미지원.</summary>
    public Task DeleteResultSessionAsync(string sessionId, CancellationToken ct = default)
        => throw new NotSupportedException(
            "HTTP 경로에서 결과 세션 삭제는 지원하지 않습니다(만료 정리는 인프라 TTL 담당).");

    // ── 내부 헬퍼 ──

    private async Task PutFileAsync(
        PreparedUploadDto slot, string localFilePath, string contentType,
        IProgress<double>? fileProgress, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(HttpBackendClient.HttpClientName);
        var fileLen = new FileInfo(localFilePath).Length;

        await using var fileStream = File.OpenRead(localFilePath);
        using var progressStream = new ProgressStream(fileStream, fileLen, fileProgress);
        using var content = new StreamContent(progressStream);

        using var request = new HttpRequestMessage(HttpMethod.Put, slot.PutUrl) { Content = content };

        // requiredHeaders: Content-Type은 콘텐츠 헤더, x-goog-meta-* 등은 요청 헤더.
        bool contentTypeSet = false;
        foreach (var (key, value) in slot.RequiredHeaders)
        {
            if (string.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                content.Headers.ContentType = new MediaTypeHeaderValue(value);
                contentTypeSet = true;
            }
            else
            {
                request.Headers.TryAddWithoutValidation(key, value);
            }
        }
        if (!contentTypeSet)
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

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
            Logger?.LogWarning(ex, "파일 PUT 실패(네트워크)");
            throw new InvalidOperationException("파일 업로드에 실패했습니다(네트워크).", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"파일 업로드에 실패했습니다(HTTP {(int)response.StatusCode}).");
        }

        // StreamContent가 전량 전송 후에도 100% 보고 보장(길이 미상 대비).
        if (fileLen > 0) fileProgress?.Report(1.0);
    }

    /// <summary>results/{sessionId}/final.{ext} | results/{sessionId}/timelapse.mp4 → (sessionId, kind).</summary>
    private static (string sessionId, string kind) ParsePath(string storagePath)
    {
        // 형식: results/{sessionId}/{fileName}
        var parts = storagePath.Split('/');
        if (parts.Length < 3 || !string.Equals(parts[0], "results", StringComparison.Ordinal))
            throw new InvalidOperationException($"업로드 경로 형식이 올바르지 않습니다: {storagePath}");

        var sessionId = parts[1];
        var fileName = parts[^1];
        var kind = fileName.StartsWith("timelapse", StringComparison.OrdinalIgnoreCase)
            ? "timelapse"
            : "final";
        return (sessionId, kind);
    }

    private static string ExtFromPath(string storagePath)
    {
        var ext = Path.GetExtension(storagePath);
        return string.IsNullOrEmpty(ext) ? string.Empty : ext.TrimStart('.').ToLowerInvariant();
    }

    /// <summary>다운로드 토큰 URL(…?alt=media&amp;token={token})에서 token 값을 추출.</summary>
    private static string ExtractToken(string downloadUrl)
    {
        const string marker = "token=";
        var idx = downloadUrl.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return string.Empty;
        var tail = downloadUrl.Substring(idx + marker.Length);
        var amp = tail.IndexOf('&');
        return amp >= 0 ? tail.Substring(0, amp) : tail;
    }

    /// <summary>
    /// 읽기 진행률을 IProgress&lt;double&gt;로 보고하는 읽기 전용 스트림 래퍼(업로드 진행률 유지).
    /// StreamContent가 이 스트림을 읽어 PUT하므로 읽힌 바이트 = 전송 바이트로 근사.
    /// </summary>
    private sealed class ProgressStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _total;
        private readonly IProgress<double>? _progress;
        private long _read;

        public ProgressStream(Stream inner, long total, IProgress<double>? progress)
        {
            _inner = inner;
            _total = total;
            _progress = progress;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = _inner.Read(buffer, offset, count);
            Advance(n);
            return n;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var n = await _inner.ReadAsync(buffer, ct).ConfigureAwait(false);
            Advance(n);
            return n;
        }

        private void Advance(int n)
        {
            if (n <= 0 || _total <= 0) return;
            _read += n;
            _progress?.Report(Math.Clamp(_read / (double)_total, 0.0, 1.0));
        }

        public override void Flush() => _inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
