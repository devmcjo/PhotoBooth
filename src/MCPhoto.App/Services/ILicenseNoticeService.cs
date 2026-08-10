using System.Collections.Generic;
using System.Globalization;

namespace MCPhoto.App.Services;

/// <summary>
/// 고지 문서 1건(표시 이름 · 절대 경로 · 크기). <see cref="DisplayName"/>은 파일명 그대로이며
/// 하위 폴더에 있으면 <c>하위폴더/파일명.txt</c> 형태다 — <c>README.txt</c>가 다른 파일을 **파일명으로
/// 상호 참조**하므로(“전문은 FFmpeg-COPYING.GPLv3.txt에 있습니다”) 친절한 별칭을 붙이면 그 안내가
/// 목록과 어긋난다.
/// </summary>
/// <param name="DisplayName">목록 표시 이름(= 고지 폴더 기준 상대 경로).</param>
/// <param name="FullPath">절대 경로. ⚠️ UI에 표시하지 않는다(요구: “경로를 적어주지 말고”) — 읽기·로그용.</param>
/// <param name="SizeBytes">열거 시점의 파일 크기.</param>
public sealed record LicenseDocument(string DisplayName, string FullPath, long SizeBytes)
{
    /// <summary>사람이 읽는 크기 표기(예: <c>34.3 KB</c>). 하단 요약·크기 상한 문구가 공유한다.</summary>
    public string SizeText => FormatSize(SizeBytes);

    /// <summary>바이트 수 → 표기 문자열. 숫자 포맷이라 invariant로 고정(로케일에 따라 소수점이 바뀌지 않게).</summary>
    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return string.Format(CultureInfo.InvariantCulture, "{0} B", bytes);
        if (bytes < 1024 * 1024) return string.Format(CultureInfo.InvariantCulture, "{0:0.0} KB", bytes / 1024.0);
        return string.Format(CultureInfo.InvariantCulture, "{0:0.0} MB", bytes / (1024.0 * 1024.0));
    }
}

/// <summary>
/// 본문 읽기 결과. 실패를 **예외가 아니라 판별 결과값**으로 돌려준다 — 호출자(VM)가 6종 catch를
/// 갖는 대신 문구를 그대로 바인딩하면 되고, 라이선스 화면이 예외로 닫히는 일이 원리적으로 없다.
/// </summary>
/// <param name="Text">본문(성공). 실패면 null.</param>
/// <param name="ErrorMessage">실패 안내(사람 말, 경로 없음). 성공이면 null.</param>
public sealed record LicenseTextResult(string? Text, string? ErrorMessage)
{
    public bool IsSuccess => Text is not null;

    /// <summary>성공 결과(빈 문자열도 성공일 수 있으므로 null 여부로만 판정한다).</summary>
    public static LicenseTextResult Ok(string text) => new(text, null);

    /// <summary>실패 결과.</summary>
    public static LicenseTextResult Fail(string message) => new(null, message);
}

/// <summary>
/// 오픈소스 라이선스 고지(설치 폴더의 <c>licenses/</c>) 열거·읽기.
/// 이 앱은 GPLv3 바이너리(ffmpeg.exe)를 재배포하므로 고지 전문을 수령자가 실제로 볼 수 있어야 한다(GPLv3 §4·§6).
/// <para>
/// it23 C부에서 <c>ILicenseFolderService</c>(경로 표시 + 탐색기 열기)를 대체했다 — 사용자 요구가
/// “폴더를 열거나 경로를 적어주지 말고 내용을 그대로 노출”이라 그 두 기능이 UI에서 사라졌고,
/// 대신 설정 화면이 <b>파일 내용을 직접 렌더링</b>한다.
/// </para>
/// <para>
/// ⚠️ 법적 산출물은 여전히 **배포물에 동봉된 파일**이다(csproj의 <c>CopyLicensesToPublish</c>).
/// 앱 내 표시는 발견성 보조이므로 폴더 동봉 배선을 이 서비스가 대체하지 않는다.
/// </para>
/// (설계: docs/design/wpf-it23-session-testmode-license-design.md §C7.1)
/// </summary>
public interface ILicenseNoticeService
{
    /// <summary>고지 폴더 절대 경로. ⚠️ 로그·진단용 — <b>UI에 표시하지 않는다</b>(요구).</summary>
    string FolderPath { get; }

    /// <summary>고지 폴더가 배포물에 실제로 있는지(배포 누락 진단).</summary>
    bool Exists { get; }

    /// <summary>
    /// 고지 문서 열거(<c>*.txt</c> 재귀). 정렬은 <c>README.txt</c> 최상단 + 나머지 이름 오름차순.
    /// 폴더 없음·열거 실패는 **빈 목록**(예외를 던지지 않는다). 폴더를 생성하지 않는다.
    /// </summary>
    IReadOnlyList<LicenseDocument> ListDocuments();

    /// <summary>문서 본문 읽기. 실패는 예외가 아니라 <see cref="LicenseTextResult.ErrorMessage"/>로 돌려준다.</summary>
    LicenseTextResult ReadText(LicenseDocument document);
}
