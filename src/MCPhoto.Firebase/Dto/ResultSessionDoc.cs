using Google.Cloud.Firestore;

namespace MCPhoto.Firebase.Dto;

/// <summary>
/// Firestore resultSessions 문서 매핑. 필드명은 firebase-contract §2.3 규약과 일치해야 웹이 읽는다.
/// </summary>
[FirestoreData]
public sealed class ResultSessionDoc
{
    [FirestoreProperty("id")]
    public string Id { get; set; } = string.Empty;

    [FirestoreProperty("finalImageUrl")]
    public string FinalImageUrl { get; set; } = string.Empty;

    [FirestoreProperty("timelapseUrl")]
    public string? TimelapseUrl { get; set; }

    [FirestoreProperty("createdAt")]
    public Timestamp CreatedAt { get; set; }

    [FirestoreProperty("expiresAt")]
    public Timestamp ExpiresAt { get; set; }

    [FirestoreProperty("downloadPageUrl")]
    public string DownloadPageUrl { get; set; } = string.Empty;
}
