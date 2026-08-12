using System.Text;
using MCPhoto.Core.Models;

namespace MCPhoto.Core.Frames;

/// <summary>
/// 로컬 프레임 파일 저장소(png + `.slots` v2). 루트 = 쓰기 가능한 프레임 캐시 폴더
/// (앱: <c>%ProgramData%\MCPhoto\Frame</c> — it26 §3.4).
/// <para>
/// 레이아웃: 공용 = <c>{루트}\{이름}.png</c>, 개인 = <c>{루트}\users\{이메일 해시}\{이름}.png</c>.
/// 계정별 폴더 분리로 <b>다른 계정과 같은 이름을 써도 파일이 충돌하지 않는다</b>
/// (같은 계정 안에서는 <see cref="FrameNaming.IsNameAvailable"/>가 중복을 막는다).
/// </para>
/// <para>
/// 소유 판정의 권위는 <b>서명된 <c>#owner</c></b>다. 종전 파일명 접두(<c>{계정}_{이름}</c>) 규약은
/// 파일 이름만 바꾸면 뚫렸고, 계정 id가 다른 계정 id의 접두일 때 유출되는 결함도 있었다(billing/04 §2.1).
/// </para>
/// 이름은 원문 그대로 저장한다(sanitize 없음). 파일시스템 금지문자만 저장 거부.
/// <para>
/// it27 §3.1 — 루트는 **하나**다. 종전의 읽기 전용 보조 루트({exe}\Frame)는 제거했다:
/// ① 그 폴백은 "이관 전 설치본이 남긴 캐시"를 위한 것이었는데 이 제품은 그 시점까지 배포된 적이
///    없어 보호할 대상이 존재하지 않았다 ② 로컬 프레임은 전부 서버에서 재취득 가능한 캐시다
///    (FrameCatalogService의 SyncPublicCache·SyncUserCache가 서버를 정본으로 다룬다)
/// ③ 앱 경로를 읽는 표면을 남기면 "앱 경로 완전 제거"가 성립하지 않는다.
/// 구 위치의 잔재는 인스톨러가 제거 시 정리한다(MCPhoto.iss [UninstallDelete] — 개발·검증 머신용).
/// </para>
/// </summary>
public sealed class LocalFrameStore : ILocalFrameStore
{
    /// <summary>개인 프레임 루트 하위 폴더명.</summary>
    public const string UsersFolderName = "users";

    private readonly string _root;

    /// <param name="rootFolder">쓰기 가능한 캐시 루트(앱: %ProgramData%\MCPhoto\Frame). 읽기·쓰기 모두 이 하나뿐이다.</param>
    public LocalFrameStore(string rootFolder)
    {
        _root = rootFolder;
    }

    // ── 저장 ──

    public FrameTemplate SaveDefaultFrame(FrameTemplate frame, byte[] png, string? dbId)
        => Write(frame, png, FrameOwnership.DefaultOwner, _root, dbId);

    public FrameTemplate SaveUserFrame(FrameTemplate frame, byte[] png, string ownerEmail, string? dbId)
    {
        var owner = FrameOwnership.NormalizeEmail(ownerEmail);
        if (owner.Length == 0)
            throw new IOException("개인 프레임을 저장하려면 로그인 계정이 필요합니다.");

        return Write(frame, png, owner, UserFolder(owner), dbId);
    }

    private FrameTemplate Write(FrameTemplate frame, byte[] png, string owner, string folder, string? dbId)
    {
        EnsureFileNameSafe(frame.Name);
        Directory.CreateDirectory(folder);

        var pngPath = Path.Combine(folder, frame.Name + ".png");
        var slotsPath = Path.Combine(folder, frame.Name + ".slots");

        File.WriteAllBytes(pngPath, png);

        var content = new SlotsFileContent(owner, frame.ImageSize, frame.Slots, dbId);
        File.WriteAllText(slotsPath, SlotsFileCodec.Encode(content), Encoding.UTF8);

        bool isDefault = FrameOwnership.IsDefault(owner);
        frame.ImageUrl = pngPath;                       // 로컬 경로 반영(로딩·표시용)
        frame.IsDefault = isDefault;
        frame.UserId = isDefault ? null : owner;        // ⚠️ 로컬 규약: UserId = 소유자 "이메일"
        return frame;
    }

    // ── 로딩 ──

    public IReadOnlyList<FrameTemplate> LoadPublic()
        => Enumerate(_root, viewerEmail: null)
            .Where(x => FrameOwnership.IsDefault(x.owner))
            .Select(x => x.frame)
            .ToList();

    public IReadOnlyList<FrameTemplate> LoadUser(string ownerEmail)
    {
        var owner = FrameOwnership.NormalizeEmail(ownerEmail);
        if (owner.Length == 0) return Array.Empty<FrameTemplate>();

        return Enumerate(UserFolder(owner), viewerEmail: owner)
            .Where(x => !FrameOwnership.IsDefault(x.owner))   // 개인 폴더의 default 표기는 이상 데이터 → 제외
            .Select(x => x.frame)
            .ToList();
    }

    public IReadOnlySet<string> PublicFrameNames()
        => new HashSet<string>(LoadPublic().Select(f => f.Name), StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> UserFrameNames(string ownerEmail)
        => new HashSet<string>(LoadUser(ownerEmail).Select(f => f.Name), StringComparer.OrdinalIgnoreCase);

    // ── 삭제 ──

    /// <remarks>
    /// ⚠️ <b>경로 기반</b>이다 — <see cref="FrameTemplate.ImageUrl"/>이 가리키는 파일을 지우며 루트에 갇히지
    /// 않는다. 루트 개념과 무관하므로 단일 루트화(it27 §3.1)에도 한 줄도 바뀌지 않았다.
    /// </remarks>
    public bool DeleteLocal(FrameTemplate frame)
    {
        var pngPath = !string.IsNullOrEmpty(frame.ImageUrl) && File.Exists(frame.ImageUrl)
            ? frame.ImageUrl
            : null;
        if (pngPath is null) return false;

        var slotsPath = Path.ChangeExtension(pngPath, ".slots");
        try { if (File.Exists(slotsPath)) File.Delete(slotsPath); } catch { /* 무시 */ }
        try { File.Delete(pngPath); } catch { /* 잠김 등 — 아래에서 존재 여부로 판정 */ }
        // png가 실제로 사라졌는지로 성공을 판정(잠금·부분 삭제를 정직하게 반환).
        return !File.Exists(pngPath);
    }

    // ── 진단 ──

    public IReadOnlyList<LocalFrameEntry> Inspect(string? ownerEmail)
    {
        var list = new List<LocalFrameEntry>();
        var owner = FrameOwnership.NormalizeEmail(ownerEmail);

        AppendEntries(list, _root);
        if (owner.Length > 0) AppendEntries(list, UserFolder(_root, owner));

        return list;
    }

    private static void AppendEntries(List<LocalFrameEntry> sink, string folder)
    {
        if (!Directory.Exists(folder)) return;

        foreach (var png in Directory.EnumerateFiles(folder, "*.png"))
        {
            var name = Path.GetFileNameWithoutExtension(png);
            var slotsPath = Path.ChangeExtension(png, ".slots");
            if (!File.Exists(slotsPath))
            {
                sink.Add(new LocalFrameEntry(png, name, SlotsDecodeStatus.NotEncoded, null, null, 0));
                continue;
            }

            var status = ReadSlots(slotsPath, out var content);
            sink.Add(new LocalFrameEntry(
                png, name, status, content?.Owner, content?.DbId, content?.Slots.Count ?? 0));
        }
    }

    // ── 내부 ──

    /// <summary>개인 프레임 폴더(읽기·쓰기 모두 <c>_root</c> 하위 — it27 §3.1로 루트가 하나다).</summary>
    private string UserFolder(string normalizedEmail) => UserFolder(_root, normalizedEmail);

    private static string UserFolder(string root, string normalizedEmail)
        => Path.Combine(root, UsersFolderName, FrameOwnership.FolderNameFor(normalizedEmail));

    /// <summary>
    /// 폴더의 프레임을 열거하며 서명 검증 + 노출 판정을 통과한 것만 돌려준다.
    /// 검증 실패·손상 파일은 <b>조용히 건너뛴다</b>(목록 로딩이 멈추면 안 된다 — 진단은 <see cref="Inspect"/>).
    /// </summary>
    private IEnumerable<(FrameTemplate frame, string owner)> Enumerate(string folder, string? viewerEmail)
    {
        if (!Directory.Exists(folder)) yield break;

        foreach (var png in Directory.EnumerateFiles(folder, "*.png"))
        {
            var slotsPath = Path.ChangeExtension(png, ".slots");
            if (!File.Exists(slotsPath)) continue;      // slots 없으면 프레임이 아니다

            if (ReadSlots(slotsPath, out var content) != SlotsDecodeStatus.Ok || content is null)
                continue;                               // v1 평문·변조·손상 → 제외

            if (!FrameOwnership.CanShow(content.Owner, viewerEmail)) continue;

            bool isDefault = FrameOwnership.IsDefault(content.Owner);
            var name = Path.GetFileNameWithoutExtension(png);

            yield return (new FrameTemplate
            {
                // 서버 동기분은 문서 id, 미동기분은 로컬 식별자.
                Id = !string.IsNullOrEmpty(content.DbId) ? content.DbId! : $"local:{name}",
                Name = name,
                UserId = isDefault ? null : content.Owner,   // ⚠️ 이메일(로컬 규약)
                IsDefault = isDefault,
                ImageUrl = png,
                ImageSize = content.ImageSize,
                Slots = content.Slots.ToList()
            }, content.Owner);
        }
    }

    /// <summary>파일 읽기 + 디코딩. I/O 예외도 상태로 환원한다(호출부가 예외를 신경 쓰지 않게).</summary>
    private static SlotsDecodeStatus ReadSlots(string slotsPath, out SlotsFileContent? content)
    {
        content = null;
        string text;
        try { text = File.ReadAllText(slotsPath, Encoding.UTF8); }
        catch { return SlotsDecodeStatus.NotEncoded; }

        return SlotsFileCodec.Decode(text, out content);
    }

    /// <summary>
    /// 파일시스템 금지문자(\ / : * ? " &lt; &gt; |) 포함 시 저장 거부. sanitize 아님(원문 유지).
    /// 판정은 <see cref="FrameNaming.IsFileNameSafe"/>(순수 함수)에 위임 — 저장 전 선검증(VM)과 동일 기준.
    /// </summary>
    private static void EnsureFileNameSafe(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new IOException("이름이 비어 있습니다.");
        if (!FrameNaming.IsFileNameSafe(value))
            throw new IOException($"이름에 사용할 수 없는 문자가 있습니다: {value}");
    }
}
