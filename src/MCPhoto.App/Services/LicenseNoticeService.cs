using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
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

    /// <summary>색인 파일 — 다른 파일을 상호 참조하므로 목록 최상단에 고정한다.</summary>
    private const string IndexFileName = "README.txt";

    // ── 실패 문구(§C6 동결). 경로를 넣지 않는다 — 경로는 Warning 로그에만 남긴다(요구). ──
    internal const string ReadFailedMessage = "이 파일을 읽을 수 없습니다. 파일이 사용 중이거나 접근 권한이 없습니다.";
    internal const string EmptyFileMessage = "이 파일은 비어 있습니다. 배포 산출물이 불완전할 수 있습니다.";

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

            // ① README.txt(색인) 최상단 고정 — 색인을 먼저 읽게 하는 것이 그 파일을 만든 의도다.
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
}
