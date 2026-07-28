namespace MCPhoto.Http.Dto;

using System.Collections.Generic;

/// <summary>이미지 픽셀 크기(서버 imageSize 필드와 정합).</summary>
internal sealed class ImageSizeDto
{
    public int Width { get; set; }
    public int Height { get; set; }
}

/// <summary>슬롯 하나(서버 slots 원소와 정합).</summary>
internal sealed class SlotDto
{
    public int Index { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

/// <summary>클라 응답용 Frame. (functions src/services/dto.ts FrameResponse)</summary>
internal sealed class FrameResponse
{
    public string Id { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public bool IsDefault { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public ImageSizeDto ImageSize { get; set; } = new();
    public List<SlotDto> Slots { get; set; } = new();
    /// <summary>ISO8601 문자열.</summary>
    public string? CreatedAt { get; set; }
}

/// <summary>
/// POST /frames 요청 본문(메타). 서버가 공용 기본 프레임만 생성하므로 isDefault=true·userId=null 고정.
/// (설계 §5.3 · functions src/routes/frames.ts) — 이미지 바이트는 응답의 서명 URL로 별도 PUT.
/// </summary>
internal sealed class SaveFrameRequest
{
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; } = true;
    public ImageSizeDto ImageSize { get; set; } = new();
    public List<SlotDto> Slots { get; set; } = new();
}

/// <summary>클라가 이미지를 직접 PUT할 서명 URL + 필수 헤더(서버 SignedUpload와 정합). (functions src/services/signing.ts)</summary>
internal sealed class SignedUploadDto
{
    public string PutUrl { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public Dictionary<string, string> RequiredHeaders { get; set; } = new();
}

/// <summary>POST /frames 응답: {frame, upload}. (functions src/services/frames.ts SaveFrameResult)</summary>
internal sealed class SaveFrameResponse
{
    public FrameResponse Frame { get; set; } = new();
    public SignedUploadDto Upload { get; set; } = new();
}

// it15 F1-D2: PUT /frames/{id} 요청·응답 DTO는 앱이 이 라우트를 호출하지 않게 되어 제거했다
// (프레임 편집은 로컬 전용 — docs/design/wpf-it15-frame-ux-design.md §3.2).

/// <summary>DELETE /frames/{id} 응답: {deleted:bool}. (functions src/routes/frames.ts)</summary>
internal sealed class DeleteFrameResponse
{
    public bool Deleted { get; set; }
}
