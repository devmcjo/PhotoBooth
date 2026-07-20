namespace MCPhoto.Core.Models;

/// <summary>
/// 프레임 템플릿. 배경형 합성의 배경 레이어이자 슬롯 배치 정의. (architecture §3.1, firebase-contract §2.2)
/// </summary>
public sealed class FrameTemplate
{
    public string Id { get; set; } = string.Empty;

    /// <summary>소유 계정 id. 공용 기본 프레임은 null.</summary>
    public string? UserId { get; set; }

    /// <summary>공용 기본 프레임 여부(게스트에게도 노출).</summary>
    public bool IsDefault { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>프레임 이미지 URL(Storage) 또는 로컬 번들 경로.</summary>
    public string ImageUrl { get; set; } = string.Empty;

    /// <summary>등록 원본 픽셀 크기.</summary>
    public ImageSize ImageSize { get; set; } = new();

    /// <summary>슬롯 1~6개. 프레임 픽셀 좌표계.</summary>
    public List<Slot> Slots { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>이미지 픽셀 크기.</summary>
public sealed class ImageSize
{
    public int Width { get; set; }
    public int Height { get; set; }
}
