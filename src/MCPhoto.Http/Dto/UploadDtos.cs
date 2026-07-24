namespace MCPhoto.Http.Dto;

using System.Collections.Generic;

/// <summary>POST /uploads/prepare 요청: {sessionId, files:[{kind, ext, contentType}]}. (functions src/routes/uploads.ts)</summary>
internal sealed class PrepareUploadRequest
{
    public string SessionId { get; set; } = string.Empty;
    public List<UploadFileEntry> Files { get; set; } = new();
}

/// <summary>업로드 파일 명세(kind=final/timelapse). (functions src/domain/validation.ts UploadFileSpec)</summary>
internal sealed class UploadFileEntry
{
    public string Kind { get; set; } = string.Empty;
    public string Ext { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
}

/// <summary>prepare 응답 원소: {kind, putUrl, downloadUrl, requiredHeaders}. (functions src/services/uploads.ts PreparedUpload)</summary>
internal sealed class PreparedUploadDto
{
    public string Kind { get; set; } = string.Empty;
    public string PutUrl { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public Dictionary<string, string> RequiredHeaders { get; set; } = new();
}

/// <summary>POST /uploads/prepare 응답: {uploads[], bucket}. (functions src/services/uploads.ts PrepareResult)</summary>
internal sealed class PrepareUploadResponse
{
    public List<PreparedUploadDto> Uploads { get; set; } = new();
    public string Bucket { get; set; } = string.Empty;
}

/// <summary>
/// POST /uploads/commit 요청: {sessionId, finalImageUrl?, timelapseUrl?, retentionHours, downloadPageUrl}.
/// (functions src/routes/uploads.ts) — 명시적 null 의미가 계약이므로 null 필드도 전송한다.
/// </summary>
internal sealed class CommitUploadRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string? FinalImageUrl { get; set; }
    public string? TimelapseUrl { get; set; }
    public int RetentionHours { get; set; }
    public string DownloadPageUrl { get; set; } = string.Empty;
}

/// <summary>commit 응답 / resultSession. (functions src/services/dto.ts ResultSessionResponse)</summary>
internal sealed class ResultSessionResponse
{
    public string Id { get; set; } = string.Empty;
    public string? FinalImageUrl { get; set; }
    public string? TimelapseUrl { get; set; }
    /// <summary>ISO8601.</summary>
    public string? CreatedAt { get; set; }
    /// <summary>ISO8601.</summary>
    public string? ExpiresAt { get; set; }
    public string DownloadPageUrl { get; set; } = string.Empty;
}
