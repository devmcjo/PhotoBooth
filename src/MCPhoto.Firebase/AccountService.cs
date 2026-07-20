using Google.Cloud.Firestore;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Frames;
using MCPhoto.Core.Models;
using MCPhoto.Firebase.Dto;
using Microsoft.Extensions.Logging;

namespace MCPhoto.Firebase;

/// <summary>
/// 계정 로그인/CRUD/역할. Firestore users. ⚠️ MVP 평문 비교. (PRD §F8, firebase-contract §2.1)
/// Firebase 미초기화(오프라인/키 없음) 시 시드 계정만 인메모리로 제공.
/// </summary>
public sealed class AccountService : IAccountService
{
    private const string Collection = "users";
    private const string SeedId = "devmcjo";
    private const string SeedPassword = "1111";

    private readonly FirebaseClient _client;
    private readonly IFrameRepository _frames;
    private readonly ILogger<AccountService>? _logger;

    public AccountService(FirebaseClient client, IFrameRepository frames, ILogger<AccountService>? logger = null)
    {
        _client = client;
        _frames = frames;
        _logger = logger;
    }

    private FirestoreDb? Db => _client.Firestore;

    public async Task<User?> LoginAsync(string id, string password, CancellationToken ct = default)
    {
        // 오프라인/미초기화: 시드 계정만 허용(관리자 로컬 접근)
        if (Db is null)
        {
            if (id == SeedId && password == SeedPassword)
                return new User { Id = SeedId, Password = SeedPassword, Role = UserRole.Admin };
            return null;
        }

        var snap = await Db.Collection(Collection).Document(id).GetSnapshotAsync(ct);
        if (!snap.Exists) return null;
        var doc = snap.ConvertTo<UserDoc>();
        if (doc.Password != password) return null; // 평문 비교(MVP)
        return ToUser(doc);
    }

    public async Task<User> CreateAsync(string id, string password, UserRole role = UserRole.User, CancellationToken ct = default)
    {
        EnsureDb();
        var docRef = Db!.Collection(Collection).Document(id);
        var existing = await docRef.GetSnapshotAsync(ct);
        if (existing.Exists) throw new InvalidOperationException($"이미 존재하는 아이디입니다: {id}");

        var now = DateTime.UtcNow;
        var doc = new UserDoc { Id = id, Password = password, Role = role.ToFirestoreValue(), CreatedAt = Timestamp.FromDateTime(now) };
        await docRef.SetAsync(doc, cancellationToken: ct);
        return new User { Id = id, Password = password, Role = role, CreatedAt = now };
    }

    public async Task ChangePasswordAsync(string id, string newPassword, CancellationToken ct = default)
    {
        EnsureDb();
        await Db!.Collection(Collection).Document(id)
            .UpdateAsync("password", newPassword, cancellationToken: ct);
    }

    public async Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default)
    {
        if (Db is null) return Array.Empty<User>();
        var snap = await Db.Collection(Collection).GetSnapshotAsync(ct);
        return snap.Documents.Select(d => ToUser(d.ConvertTo<UserDoc>())).ToList();
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        EnsureDb();
        // cascade: 소유 프레임(Firestore 문서 + Storage frames/{id}/) 함께 삭제(§F8)
        try { await _frames.DeleteAllByUserAsync(id, ct); }
        catch (Exception ex) { _logger?.LogWarning(ex, "cascade 프레임 삭제 실패: {Id}", id); }

        await Db!.Collection(Collection).Document(id).DeleteAsync(cancellationToken: ct);
    }

    public async Task SetRoleAsync(string id, UserRole role, CancellationToken ct = default)
    {
        EnsureDb();
        await Db!.Collection(Collection).Document(id)
            .UpdateAsync("role", role.ToFirestoreValue(), cancellationToken: ct);
    }

    public async Task EnsureSeedAccountAsync(CancellationToken ct = default)
    {
        if (Db is null) return; // 오프라인: 시드는 LoginAsync에서 인메모리 처리
        var docRef = Db.Collection(Collection).Document(SeedId);
        var snap = await docRef.GetSnapshotAsync(ct);
        if (!snap.Exists)
        {
            var doc = new UserDoc
            {
                Id = SeedId,
                Password = SeedPassword,
                Role = UserRole.Admin.ToFirestoreValue(),
                CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
            };
            await docRef.SetAsync(doc, cancellationToken: ct);
            _logger?.LogInformation("시드 계정 생성: {Id}", SeedId);
        }
    }

    private static User ToUser(UserDoc d) => new()
    {
        Id = d.Id,
        Password = d.Password,
        Role = UserRoleExtensions.ParseRole(d.Role),
        CreatedAt = d.CreatedAt.ToDateTime()
    };

    private void EnsureDb()
    {
        if (Db is null)
            throw new InvalidOperationException("Firebase 미초기화 — 계정 쓰기 불가(서비스 계정 키 필요).");
    }
}
