using Google.Cloud.Firestore;

namespace MCPhoto.Firebase.Dto;

/// <summary>Firestore users 문서. ⚠️ MVP 평문 비밀번호. (firebase-contract §2.1)</summary>
[FirestoreData]
public sealed class UserDoc
{
    [FirestoreProperty("id")]
    public string Id { get; set; } = string.Empty;

    [FirestoreProperty("password")]
    public string Password { get; set; } = string.Empty;

    [FirestoreProperty("role")]
    public string Role { get; set; } = "user";

    [FirestoreProperty("createdAt")]
    public Timestamp CreatedAt { get; set; }
}
