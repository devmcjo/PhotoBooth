using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.Services;

/// <summary>
/// 고지 폴더 = {설치 폴더}\licenses. 로그 폴더(App.DataFolder)와 달리 **실행 파일 옆**이다 —
/// 배포물에 동봉되는 정적 문서이고 사용자가 수정할 대상이 아니기 때문이다.
/// <para>
/// ⚠️ <b>폴더·파일을 생성하지 않는다.</b> 없다는 것은 배포 산출물에서 고지가 누락됐다는 뜻이고,
/// 빈 폴더/빈 파일을 만들면 라이선스 위반 상태를 은폐한다(<c>LicenseFolderService</c>가 확립한 원칙 승계).
/// </para>
/// <para>
/// 동기 API인 이유: 파일 I/O가 수 ms이고, 느린 디스크·네트워크 드라이브 대비는 **호출자(VM)가
/// <c>Task.Run</c>으로 감싼다**. 서비스에 async를 넣으면 순수 파일 조작 테스트가 async로 오염된다
/// (<c>LogFolderService</c> 선례와 동형).
/// </para>
/// <paramref name="baseDirectory"/>는 테스트가 임시 폴더로 검증하는 이음새다 — 반드시 보존한다.
/// </summary>
public sealed class LicenseNoticeService : ILicenseNoticeService
{
    /// <summary>표시 상한. 현행 최대 35 KB이므로 정상 파일을 절대 막지 않는 오배치 방어선이다.</summary>
    public const long MaxDisplayBytes = 2 * 1024 * 1024;

    /// <summary>
    /// 색인 파일 — 다른 파일을 상호 참조하므로 목록 최상단에 고정한다.
    /// it24에서 <c>README.txt</c> → <c>NOTICE.txt</c>로 개명했다(<c>NOTICE</c>가 배포물 고지의 통용 이름이고
    /// <c>README</c>는 개발자 문서로 읽힌다).
    /// </summary>
    internal const string IndexFileName = "NOTICE.txt";

    /// <summary>
    /// 요약 메타데이터 파일. ⚠️ <c>private</c>인 이유: 이 이름이 화면·실패 문구로 새면 안 된다(파일명 미노출 요구).
    /// 확장자를 <c>.json</c>으로 둔 덕에 <see cref="ListDocuments"/>의 <c>*.txt</c> 패턴에 걸리지 않는다 —
    /// 기계용 파일이 전문 목록에 섞이지 않는다.
    /// </summary>
    private const string ManifestFileName = "notice-manifest.json";

    /// <summary>앱이 해석할 수 있는 매니페스트 스키마 버전. 다르면 억지로 읽지 않고 강등한다.</summary>
    private const int SupportedSchemaVersion = 1;

    // ── 실패 문구(§C6 동결). 경로를 넣지 않는다 — 경로는 Warning 로그에만 남긴다(요구). ──
    internal const string ReadFailedMessage = "이 파일을 읽을 수 없습니다. 파일이 사용 중이거나 접근 권한이 없습니다.";
    internal const string EmptyFileMessage = "이 파일은 비어 있습니다. 배포 산출물이 불완전할 수 있습니다.";

    // ── 강등 문구(설계 §2.7 D1·D2 동결). 파일명·경로·슬래시를 넣지 않는다 ──
    //    (공개 문구의 경로 부재는 No_Folder_Path_In_Ui 테스트가 잠근다).

    /// <summary>D1 — 요약 메타데이터 파일이 없다.</summary>
    internal const string SummaryMissingMessage =
        "라이선스 요약 정보를 찾을 수 없어 동봉된 고지 문서를 그대로 표시합니다. "
        + "배포 산출물이 불완전할 수 있으므로 개발자에게 알려주세요.";

    /// <summary>D2 — 손상·스키마 불일치·필수 필드 누락·항목 0개.</summary>
    internal const string SummaryUnreadableMessage =
        "라이선스 요약 정보를 읽을 수 없어 동봉된 고지 문서를 그대로 표시합니다. "
        + "배포 산출물이 불완전할 수 있으므로 개발자에게 알려주세요.";

    /// <summary>
    /// 매니페스트 역직렬화 옵션. 앞 둘은 리포 선례(<c>BackendJson</c>)이고, 뒤 둘은 이 파일이
    /// <b>사람이 손으로 편집하는 배포물 데이터</b>라서 필요하다 — 설명 주석을 달아 둘 수 있어야 유지보수가 산다.
    /// </summary>
    private static readonly JsonSerializerOptions ManifestOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>매니페스트가 선언할 수 있는 구성 요소 종류(그 외 값은 오타로 보고 강등한다).</summary>
    private const string KindSelf = "self";
    private const string KindRedistributed = "redistributed";

    private readonly ILogger<LicenseNoticeService>? _logger;
    private readonly string _baseDirectory;

    public LicenseNoticeService(
        ILogger<LicenseNoticeService>? logger = null,
        string? baseDirectory = null)
    {
        _logger = logger;
        _baseDirectory = baseDirectory ?? AppContext.BaseDirectory;
    }

    public string FolderPath => Path.Combine(_baseDirectory, "licenses");

    public bool Exists => Directory.Exists(FolderPath);

    /// <summary>파일당 크기 상한 초과 안내(§C6 F4).</summary>
    internal static string TooLargeMessage(long bytes) =>
        string.Format(CultureInfo.InvariantCulture,
            "파일이 너무 커서 화면에 표시할 수 없습니다({0}). 배포 폴더의 파일을 직접 확인해 주세요.",
            LicenseDocument.FormatSize(bytes));

    /// <summary>
    /// <c>licenses/**/*.txt</c> 재귀 열거. 하드코딩 목록을 쓰지 않는 이유: csproj가 <c>**\*.*</c>를
    /// <c>%(RecursiveDir)</c> 보존으로 복사하므로 **소스 폴더에 파일을 넣으면 그대로 배포된다** —
    /// UI가 하드코딩이면 새로 추가된 고지가 조용히 안 보인다(= 법적 고지 누락).
    /// <para>
    /// ⚠️ 확장자는 <c>.txt</c>만. 폴더에 실수로 들어간 바이너리를 텍스트로 읽어 깨진 문자를 보여주는 것을 막는다.
    /// 비-txt 고지 파일이 생기면 이 패턴을 넓혀야 한다(배포 체크리스트 항목).
    /// </para>
    /// </summary>
    public IReadOnlyList<LicenseDocument> ListDocuments()
    {
        var folder = FolderPath;
        if (!Directory.Exists(folder))
        {
            _logger?.LogWarning("라이선스 고지 폴더 없음(배포 누락 가능): {Path}", folder);
            return Array.Empty<LicenseDocument>();
        }

        try
        {
            var docs = new List<LicenseDocument>();
            foreach (var path in Directory.EnumerateFiles(folder, "*.txt", SearchOption.AllDirectories))
            {
                long size;
                try { size = new FileInfo(path).Length; }
                catch (Exception ex)
                {
                    // 크기를 못 읽어도 목록에서 빼지 않는다 — 파일이 있다는 사실이 더 중요하고,
                    // 본문 읽기 단계에서 실패 문구가 나온다(누락을 감추지 않는다).
                    _logger?.LogWarning(ex, "라이선스 고지 파일 크기 확인 실패: {Path}", path);
                    size = 0;
                }
                docs.Add(new LicenseDocument(RelativeName(folder, path), path, size));
            }

            // ① NOTICE.txt(색인) 최상단 고정 — 색인을 먼저 읽게 하는 것이 그 파일을 만든 의도다.
            // ② 나머지는 이름 오름차순(OrdinalIgnoreCase) → FFmpeg-* 2개가 인접한다.
            return docs
                .OrderByDescending(d => string.Equals(d.DisplayName, IndexFileName, StringComparison.OrdinalIgnoreCase))
                .ThenBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            // 열거 자체 실패(경로 권한 등) — 빈 목록으로 축퇴하고 화면은 F6 문구를 보여준다.
            _logger?.LogWarning(ex, "라이선스 고지 열거 실패: {Path}", folder);
            return Array.Empty<LicenseDocument>();
        }
    }

    /// <summary>
    /// 본문 읽기. 개행(CRLF)·탭·정렬 공백을 **변환하지 않는다** — 요구가 “안에 있는 내용을 그대로 노출”이다.
    /// <para>
    /// 인코딩은 UTF-8 + BOM 감지. 동봉 파일은 BOM 없는 UTF-8이며 향후 BOM이 붙어도 정상 처리된다.
    /// ⚠️ CP949(ANSI) 파일은 깨진다 — 자동 추측은 오판을 만들고 동봉 파일은 우리가 관리하므로 방어하지 않고
    /// 테스트로 고정한다(배포 4파일이 UTF-8로 읽혀 한글이 온전한지).
    /// </para>
    /// </summary>
    public LicenseTextResult ReadText(LicenseDocument document)
    {
        if (document is null) return LicenseTextResult.Fail(ReadFailedMessage);

        try
        {
            var info = new FileInfo(document.FullPath);
            if (!info.Exists)
            {
                _logger?.LogWarning("라이선스 고지 파일 없음: {Path}", document.FullPath);
                return LicenseTextResult.Fail(ReadFailedMessage);
            }
            if (info.Length == 0)
                return LicenseTextResult.Fail(EmptyFileMessage);
            if (info.Length > MaxDisplayBytes)
            {
                _logger?.LogWarning("라이선스 고지 파일이 표시 상한 초과: {Path} ({Bytes} bytes)",
                    document.FullPath, info.Length);
                return LicenseTextResult.Fail(TooLargeMessage(info.Length));
            }

            using var reader = new StreamReader(document.FullPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var text = reader.ReadToEnd();
            // BOM 감지가 실패하는 경계(BOM을 ANSI로 오판)에서 첫 글자로 보이는 U+FEFF를 1회 제거.
            // ⚠️ 이스케이프로 쓴다 — 소스에 BOM 문자를 그대로 넣으면 편집기·diff에서 보이지 않는다.
            if (text.Length > 0 && text[0] == '\uFEFF') text = text[1..];
            return LicenseTextResult.Ok(text);
        }
        catch (Exception ex)
        {
            // 잠김·권한·I/O — 크래시 금지. 경로는 로그에만 남긴다(UI 문구에는 경로가 없다).
            _logger?.LogWarning(ex, "라이선스 고지 파일 읽기 실패: {Path}", document.FullPath);
            return LicenseTextResult.Fail(ReadFailedMessage);
        }
    }

    /// <summary>고지 폴더 기준 상대 경로(하위 폴더는 <c>하위폴더/파일명.txt</c>). 구분자는 항상 <c>/</c>.</summary>
    private static string RelativeName(string folder, string fullPath)
        => Path.GetRelativePath(folder, fullPath).Replace(Path.DirectorySeparatorChar, '/');

    // ══════════════════════════════════════════════════════════════════════════════
    // it24 — 요약 메타데이터(매니페스트) 해석 (설계 §2.5~§2.8)
    //
    // 왜 코드 상수가 아니라 배포물 안 파일인가: 법적 고지의 단일 소스는 **배포물에 동봉된 파일**이어야
    // 한다. 요약을 exe 안 상수로 옮기면, 고지 폴더를 교체한 배포물에서 화면과 파일이 서로 다른 말을 한다.
    // 왜 txt 산문을 파싱하지 않는가: 이번 작업 자체가 "txt 문구를 상용 수준으로 다시 쓰기"다.
    // 문구를 파서의 입력으로 만들면 문구를 손질할 때마다 화면이 깨진다.
    // ══════════════════════════════════════════════════════════════════════════════

    public LicenseSummary ReadSummary()
    {
        // 열거는 강등 폴백 목록·미참조 문서 산출에 모두 필요하므로 먼저 확보한다(폴더 부재 시 빈 목록).
        var documents = ListDocuments();
        var manifestPath = Path.Combine(FolderPath, ManifestFileName);

        ManifestRoot? root;
        try
        {
            var info = new FileInfo(manifestPath);
            if (!info.Exists)
            {
                _logger?.LogWarning("라이선스 요약 메타데이터 없음(배포 누락 가능): {Path}", manifestPath);
                return Degraded(documents, SummaryMissingMessage);
            }
            if (info.Length == 0 || info.Length > MaxDisplayBytes)
            {
                _logger?.LogWarning("라이선스 요약 메타데이터 크기 이상: {Path} ({Bytes} bytes)",
                    manifestPath, info.Length);
                return Degraded(documents, SummaryUnreadableMessage);
            }

            using var stream = File.OpenRead(manifestPath);
            root = JsonSerializer.Deserialize<ManifestRoot>(stream, ManifestOptions);
        }
        catch (Exception ex)
        {
            // JSON 손상·잠김·권한 — 크래시 금지. 예외는 로그에만, 화면에는 사람 말로.
            _logger?.LogWarning(ex, "라이선스 요약 메타데이터 해석 실패: {Path}", manifestPath);
            return Degraded(documents, SummaryUnreadableMessage);
        }

        if (root is null || root.SchemaVersion != SupportedSchemaVersion
            || root.Components is null || root.Components.Count == 0)
        {
            _logger?.LogWarning(
                "라이선스 요약 메타데이터가 유효하지 않음(schemaVersion={Version}, 항목={Count}): {Path}",
                root?.SchemaVersion, root?.Components?.Count ?? 0, manifestPath);
            return Degraded(documents, SummaryUnreadableMessage);
        }

        var components = new List<LicenseComponent>(root.Components.Count);
        // 미참조 판정용. 색인은 항목의 첨부물이 아니라 폴더 자체의 목차이므로 처음부터 참조된 것으로 본다
        // (요약 카드가 색인의 역할을 대신하므로 색인 txt를 "선언되지 않은 파일"로 볼 이유가 없다).
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { IndexFileName };

        foreach (var entry in root.Components)
        {
            var kind = Normalize(entry?.Kind);
            var name = Normalize(entry?.Name);
            var licenseName = Normalize(entry?.LicenseName);
            var spdxId = Normalize(entry?.SpdxId);
            var fullTextFile = Normalize(entry?.FullTextFile);

            // M6: 필수 필드가 하나라도 비면 **매니페스트 전체를 손상으로 판정**한다.
            // 부분적으로 맞는 법적 고지 목록은 명시적 강등보다 위험하다(이 경로는 깨진 빌드에서만 발생한다).
            bool isSelf = string.Equals(kind, KindSelf, StringComparison.OrdinalIgnoreCase);
            bool isThirdParty = string.Equals(kind, KindRedistributed, StringComparison.OrdinalIgnoreCase);
            if (!isSelf && !isThirdParty)
            {
                _logger?.LogWarning("라이선스 요약 항목의 kind 값이 유효하지 않음: {Kind}", entry?.Kind);
                return Degraded(documents, SummaryUnreadableMessage);
            }
            if (name is null || licenseName is null || spdxId is null || fullTextFile is null)
            {
                _logger?.LogWarning("라이선스 요약 항목에 필수 필드가 비어 있음: {Name}", entry?.Name);
                return Degraded(documents, SummaryUnreadableMessage);
            }

            var noticeFile = Normalize(entry?.NoticeFile);

            // 매니페스트 → 파일 방향의 교차 검사. 부재·참조 무효는 카드를 숨기지 않고 사유로 알린다.
            bool fullTextMissing = !ResolveDeclaredFile(fullTextFile, out var fullTextPath);
            if (!fullTextMissing) referenced.Add(fullTextFile);

            bool noticeMissing = false;
            if (noticeFile is not null)
            {
                noticeMissing = !ResolveDeclaredFile(noticeFile, out _);
                if (!noticeMissing) referenced.Add(noticeFile);
            }
            _ = fullTextPath;   // 존재 여부만 쓴다(본문은 ReadText(string)가 그때 읽는다)

            components.Add(new LicenseComponent(
                IsSelf: isSelf,
                Name: name,
                Version: isSelf ? null : Normalize(entry?.Version),   // M4: 본체 버전은 어셈블리 리소스가 단일 소스
                LicenseName: licenseName,
                SpdxId: spdxId,
                Copyright: Normalize(entry?.Copyright),
                Purpose: Normalize(entry?.Purpose),
                Distribution: Normalize(entry?.Distribution),
                SourceOffer: Normalize(entry?.SourceOffer),
                FullTextFile: fullTextFile,
                NoticeFile: noticeFile,
                IsFullTextMissing: fullTextMissing,
                IsNoticeMissing: noticeMissing));
        }

        // 파일 → 매니페스트 방향. 정상 배포물에서는 0건이며, 0건이 아니면 화면이 파일명을 그대로 나열한다.
        var unlisted = documents.Where(d => !referenced.Contains(d.DisplayName)).ToList();
        if (unlisted.Count > 0)
        {
            _logger?.LogWarning("요약 메타데이터가 선언하지 않은 고지 문서 {Count}건: {Names}",
                unlisted.Count, string.Join(", ", unlisted.Select(d => d.DisplayName)));
        }

        return new LicenseSummary(components, unlisted, Normalize(root.UpdatedOn), null);
    }

    /// <summary>
    /// 강등 결과. ⚠️ 폴백 목록을 <b>반드시</b> 함께 돌려준다 — 요약이 깨졌다고 전문을 못 보게 되면
    /// 이 재설계가 GPLv3 §4 이행의 후퇴가 된다(전문 도달 경로는 마지막 그물이다).
    /// </summary>
    private static LicenseSummary Degraded(IReadOnlyList<LicenseDocument> documents, string message)
        => new(Array.Empty<LicenseComponent>(), documents, null, message);

    /// <summary>M8: 빈 문자열·공백은 <c>null</c>로 정규화한다(화면의 행 표시 여부가 null 하나로 결정된다).</summary>
    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// 매니페스트가 선언한 파일이 고지 폴더에 실제로 있는지. M5 위반(경로 구분자·상위 경로·드라이브 문자)은
    /// <b>참조 무효</b>로 간주해 부재와 같게 처리한다 — 폴더 밖 파일을 열어 주지 않는다.
    /// </summary>
    private bool ResolveDeclaredFile(string fileName, out string fullPath)
    {
        fullPath = string.Empty;
        if (!TryResolveInsideFolder(fileName, out var candidate))
        {
            _logger?.LogWarning("요약 메타데이터의 파일 참조가 유효하지 않음(폴더 밖 참조 시도): {Name}", fileName);
            return false;
        }

        if (!File.Exists(candidate))
        {
            _logger?.LogWarning("요약 메타데이터가 선언한 고지 파일이 없음: {Path}", candidate);
            return false;
        }

        fullPath = candidate;
        return true;
    }

    /// <summary>
    /// 파일명 → 고지 폴더 하위 절대 경로. 두 단계로 막는다:
    /// ① 파일명 형태 검사(구분자·<c>..</c>·루트 금지) ② 결합 결과가 고지 폴더 하위인지 재확인.
    /// ①만으로 충분해 보이지만, 결합 후 재확인이 없으면 형태 검사에 구멍이 생기는 순간 조용히 뚫린다.
    /// </summary>
    private bool TryResolveInsideFolder(string fileName, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        if (fileName.IndexOfAny(new[] { '/', '\\' }) >= 0) return false;
        if (fileName.Contains("..", StringComparison.Ordinal)) return false;
        if (Path.IsPathRooted(fileName)) return false;
        if (!string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal)) return false;
        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;

        var folder = Path.GetFullPath(FolderPath);
        string combined;
        try { combined = Path.GetFullPath(Path.Combine(folder, fileName)); }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "고지 파일 경로 결합 실패: {Name}", fileName);
            return false;
        }

        var prefix = folder.EndsWith(Path.DirectorySeparatorChar) ? folder : folder + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

        fullPath = combined;
        return true;
    }

    public LicenseTextResult ReadText(string fileName)
    {
        if (!TryResolveInsideFolder(fileName, out var fullPath))
        {
            _logger?.LogWarning("허용되지 않는 고지 파일 참조: {Name}", fileName);
            return LicenseTextResult.Fail(ReadFailedMessage);
        }

        long size = 0;
        try { var info = new FileInfo(fullPath); if (info.Exists) size = info.Length; }
        catch (Exception ex) { _logger?.LogWarning(ex, "고지 파일 크기 확인 실패: {Path}", fullPath); }

        // 상한·빈 파일·읽기 실패 판정과 문구를 한 곳에 유지하기 위해 기존 경로로 위임한다.
        return ReadText(new LicenseDocument(fileName, fullPath, size));
    }

    // ── 매니페스트 DTO. 화면·서비스 계약과 분리한다(파일 스키마가 바뀌어도 계약이 흔들리지 않게). ──

    private sealed class ManifestRoot
    {
        public int SchemaVersion { get; set; }
        public string? UpdatedOn { get; set; }
        public List<ManifestComponent>? Components { get; set; }
    }

    private sealed class ManifestComponent
    {
        public string? Kind { get; set; }
        public string? Name { get; set; }
        public string? Version { get; set; }
        public string? LicenseName { get; set; }
        public string? SpdxId { get; set; }
        public string? Copyright { get; set; }
        public string? Purpose { get; set; }
        public string? Distribution { get; set; }
        public string? SourceOffer { get; set; }
        public string? FullTextFile { get; set; }
        public string? NoticeFile { get; set; }
    }
}
