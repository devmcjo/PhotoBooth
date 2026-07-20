using Google.Cloud.Firestore;

namespace MCPhoto.Firebase.Dto;

/// <summary>Firestore frameTemplates 문서. (firebase-contract §2.2)</summary>
[FirestoreData]
public sealed class FrameTemplateDoc
{
    [FirestoreProperty("id")]
    public string Id { get; set; } = string.Empty;

    [FirestoreProperty("userId")]
    public string? UserId { get; set; }

    [FirestoreProperty("isDefault")]
    public bool IsDefault { get; set; }

    [FirestoreProperty("name")]
    public string Name { get; set; } = string.Empty;

    [FirestoreProperty("imageUrl")]
    public string ImageUrl { get; set; } = string.Empty;

    [FirestoreProperty("imageSize")]
    public Dictionary<string, object> ImageSize { get; set; } = new();

    [FirestoreProperty("slots")]
    public List<Dictionary<string, object>> Slots { get; set; } = new();

    [FirestoreProperty("createdAt")]
    public Timestamp CreatedAt { get; set; }
}
