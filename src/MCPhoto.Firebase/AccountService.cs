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
            {
                // it10 S2-2: 오프라인 시드 로그인은 백도어가 아니라 현장 오프라인 대응 수단임을 로그로 명시.
                _logger?.LogWarning("오프라인 시드 로그인 — DB 미연결(서비스 계정 키 없음)");
                return new User { Id = SeedId, Password = SeedPassword, Role = UserRole.Admin };
            }
            return null;
        }

        var snap = await Db.Collection(Collection).Document(id).GetSnapshotAsync(ct);
        if (!snap.Exists) return null;
        var doc = snap.ConvertTo<UserDoc>();
        if (doc.Password != password) return null; // 평문 비교(MVP)
        return ToUser(doc);
    }

    public async Task<User> CreateAsync(string id, string password, UserRole role, string? email, UserRole actingRole, CancellationToken ct = default)
    {
        // 권한 게이트를 먼저 검사(호출자 신뢰 금지, it2 §7). 위반이 미초기화보다 우선.
        if (!actingRole.CanCreate(role))
            throw new UnauthorizedAccessException(
                $"{actingRole} 권한으로 {role} 계정을 생성할 수 없습니다.");

        EnsureDb();
        var docRef = Db!.Collection(Collection).Document(id);
        var existing = await docRef.GetSnapshotAsync(ct);
        if (existing.Exists) throw new InvalidOperationException($"이미 존재하는 아이디입니다: {id}");

        // item1a §9.1: email은 레거시 경로에서 무시한다(현행 동작 유지). 이메일 인증 인프라는 백엔드 전용이며,
        // 계정 생성 UI의 email 필드도 백엔드 모드에서만 노출되므로 레거시로 email이 흘러들지 않는다.
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

    // ── item1b: Google SSO (HTTP 전용 기능) ──
    // 레거시 Firebase 경로엔 SSO 인프라(code 교환·id_token 검증 백엔드)가 없다. SSO 버튼이 백엔드 모드에서만
    // 노출되므로 아래는 도달하지 않는다(도달하면 설정 오류 → 명확히 실패시킨다). (item1b §7.6)
    public Task<User?> LoginWithGoogleAsync(string code, string codeVerifier, string redirectUri,
        string? nonce = null, CancellationToken ct = default)
        => throw new NotSupportedException("Google 로그인은 백엔드 모드에서만 지원됩니다.");

    // ── item1a: 이메일 인증 + 비밀번호 재설정 (HTTP 전용 기능) ──
    // 레거시 Firebase 경로엔 이메일 인프라(토큰 서브컬렉션·메일 발송)가 없다. UI가 백엔드 모드에서만
    // 이 기능을 노출하므로 아래는 도달하지 않는다(도달하면 설정 오류 → 명확히 실패시킨다). (item1a §9.1)
    private const string NotSupportedMsg = "이메일 인증·비밀번호 재설정은 백엔드 모드에서만 지원됩니다.";

    public Task SetEmailAsync(string id, string email, CancellationToken ct = default)
        => throw new NotSupportedException(NotSupportedMsg);

    public Task RequestPasswordResetAsync(string idOrEmail, CancellationToken ct = default)
        => throw new NotSupportedException(NotSupportedMsg);

    public Task ConfirmPasswordResetAsync(string id, string token, string newPassword, CancellationToken ct = default)
        => throw new NotSupportedException(NotSupportedMsg);

    public Task ConfirmPasswordResetByCodeAsync(string idOrEmail, string code, string newPassword, CancellationToken ct = default)
        => throw new NotSupportedException(NotSupportedMsg);

    public Task RequestEmailVerificationAsync(string idOrEmail, CancellationToken ct = default)
        => throw new NotSupportedException(NotSupportedMsg);

    public Task<bool> ConfirmEmailVerificationAsync(string id, string code, CancellationToken ct = default)
        => throw new NotSupportedException(NotSupportedMsg);

    public Task<bool> ConfirmEmailVerificationByTokenAsync(string id, string token, CancellationToken ct = default)
        => throw new NotSupportedException(NotSupportedMsg);

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
