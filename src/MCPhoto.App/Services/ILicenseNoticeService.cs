using System.Collections.Generic;
using System.Globalization;

namespace MCPhoto.App.Services;

/// <summary>
/// 고지 문서 1건(표시 이름 · 절대 경로 · 크기). <see cref="DisplayName"/>은 파일명 그대로이며
/// 하위 폴더에 있으면 <c>하위폴더/파일명.txt</c> 형태다.
/// <para>
/// ⚠️ it24에서 <b>이 타입의 역할이 좁아졌다</b>. 종전에는 화면의 기본 목록이었고 "친절한 별칭을 붙이면
/// txt의 상호 참조 안내와 어긋난다"는 이유로 별칭을 금지했다. 이제 화면의 기본 상태는
/// <see cref="LicenseComponent"/> 요약 카드이며(색인 역할을 UI가 가져갔다), 이 타입은
/// <b>강등 폴백 목록과 미참조 문서 섹션</b>에서만 쓰인다 — 그 두 곳은 우리가 아는 정보가 파일명뿐이라
/// 파일명 노출이 유일하게 허용되는 지점이다(설계 §2.1·§2.6).
/// </para>
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
/// 고지 요약 1건 = 화면 카드 1장. 배포물의 고지 폴더에 있는 요약 메타데이터(매니페스트)를 해석한 결과이며,
/// <b>법적 산출물은 여전히 이 카드가 가리키는 txt 파일</b>이다(카드는 그 파일의 색인·요약이다).
/// <para>
/// 표시 여부용 <c>Has*</c>는 계산 속성으로 둔다 — 신규 컨버터를 만들지 않기 위해서다. 특히
/// <c>NullToVis</c>는 <b>null일 때 Visible</b>이라 "값이 없으면 숨김"에 쓸 수 없다.
/// </para>
/// </summary>
/// <param name="IsSelf">이 앱 본체인가(<c>true</c>) 동봉된 제3자 구성 요소인가(<c>false</c>). 섹션 구분에 쓴다.</param>
/// <param name="Name">사용자가 아는 이름(예: <c>FFmpeg</c>).</param>
/// <param name="Version">바이너리 특정용 버전. 본체는 항상 <c>null</c>(어셈블리 버전 리소스가 단일 소스).</param>
/// <param name="LicenseName">사람이 읽는 라이선스 이름. 영문 그대로다(식별자이므로 번역하지 않는다).</param>
/// <param name="SpdxId">SPDX 표준 짧은 식별자(예: <c>GPL-3.0-or-later</c>). 전문을 쏟지 않고 라이선스를 지목하는 수단.</param>
/// <param name="Copyright">저작권 표시. GPLv3 §4가 <b>유지(retain)</b>를 요구하므로 원문 형태를 보존한다.</param>
/// <param name="Purpose">이 구성 요소가 왜 여기 있는지(예: <c>동영상 녹화 · 타임랩스 인코딩</c>).</param>
/// <param name="Distribution">배포 형태. GPLv3 §5(a) 수정 사실 표시를 이 한 줄이 겸한다.</param>
/// <param name="SourceOffer">대응 소스 제공 사실(GPLv3 §6). 첫 화면에서 그 사실을 알리기 위한 행이다.</param>
/// <param name="FullTextFile">라이선스 전문 파일명. ⚠️ UI에 표시하지 않는다 — [라이선스 전문 보기]의 대상일 뿐이다.</param>
/// <param name="NoticeFile">상세 고지 파일명(없으면 <c>null</c>). ⚠️ UI에 표시하지 않는다.</param>
/// <param name="IsFullTextMissing">전문 파일이 배포물에 없거나 참조가 무효(경로 탈출 등)다.</param>
/// <param name="IsNoticeMissing">상세 고지 파일이 배포물에 없거나 참조가 무효다.</param>
public sealed record LicenseComponent(
    bool IsSelf,
    string Name,
    string? Version,
    string LicenseName,
    string SpdxId,
    string? Copyright,
    string? Purpose,
    string? Distribution,
    string? SourceOffer,
    string FullTextFile,
    string? NoticeFile,
    bool IsFullTextMissing,
    bool IsNoticeMissing)
{
    public bool HasVersion => !string.IsNullOrEmpty(Version);
    public bool HasCopyright => !string.IsNullOrEmpty(Copyright);
    public bool HasPurpose => !string.IsNullOrEmpty(Purpose);
    public bool HasDistribution => !string.IsNullOrEmpty(Distribution);
    public bool HasSourceOffer => !string.IsNullOrEmpty(SourceOffer);

    /// <summary>[소스 코드 제공 안내] 버튼 표시 조건. 파일이 없어도 <b>선언되어 있으면 표시</b>한다(부재는 사유로 알린다).</summary>
    public bool HasNoticeFile => !string.IsNullOrEmpty(NoticeFile);

    /// <summary>카드 안 누락 경고(F7) 표시 조건. 카드 자체를 숨기지 않는다 — 누락을 감추지 않는다.</summary>
    public bool IsAnyFileMissing => IsFullTextMissing || IsNoticeMissing;
}

/// <summary>
/// 고지 요약 전체.
/// <para>
/// <see cref="DegradedMessage"/>가 비어 있지 않으면 요약을 만들 수 없었다는 뜻이고, 화면은 경고 배너 +
/// <see cref="UnlistedDocuments"/> 폴백 목록으로 축퇴한다. <b>강등 경로에서도 전문 도달은 유지</b>된다 —
/// 요약이 깨졌다고 전문을 못 보게 되면 GPLv3 §4 이행이 후퇴한다.
/// </para>
/// </summary>
/// <param name="Components">요약 카드. 매니페스트 배열 순서 = 표시 순서(코드가 재정렬하지 않는다).</param>
/// <param name="UnlistedDocuments">
/// 어느 항목도 참조하지 않는 고지 문서(+ 강등 시에는 폴더의 전체 문서). 정상 배포물에서는 <b>0건</b>이며,
/// 0건이 아니라는 것은 "선언되지 않은 파일이 실렸다"는 신호다(설계 §2.6).
/// </param>
/// <param name="UpdatedOn">고지 기준일(매니페스트 값, <c>yyyy-MM-dd</c>). 값이 없으면 <c>null</c>.</param>
/// <param name="DegradedMessage">강등 사유(D1·D2). 정상이면 <c>null</c>.</param>
public sealed record LicenseSummary(
    IReadOnlyList<LicenseComponent> Components,
    IReadOnlyList<LicenseDocument> UnlistedDocuments,
    string? UpdatedOn,
    string? DegradedMessage);

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
    /// 고지 문서 열거(<c>*.txt</c> 재귀). 정렬은 색인(<c>NOTICE.txt</c>) 최상단 + 나머지 이름 오름차순.
    /// 폴더 없음·열거 실패는 **빈 목록**(예외를 던지지 않는다). 폴더를 생성하지 않는다.
    /// </summary>
    IReadOnlyList<LicenseDocument> ListDocuments();

    /// <summary>문서 본문 읽기. 실패는 예외가 아니라 <see cref="LicenseTextResult.ErrorMessage"/>로 돌려준다.</summary>
    LicenseTextResult ReadText(LicenseDocument document);

    /// <summary>
    /// 고지 요약 산출(매니페스트 해석 + 선언된 파일의 존재 교차 검사 + 미참조 문서 산출).
    /// <b>예외를 던지지 않는다</b> — 실패는 <see cref="LicenseSummary.DegradedMessage"/>로 강등해 화면이 사유를 띄운다.
    /// <para>
    /// 존재 검사를 VM이 아니라 여기서 하는 이유: 매니페스트 해석과 파일 존재 확인이 한 트랜잭션이어야
    /// "선언된 파일이 없다"는 판정이 한 곳에서 난다. VM은 표시만 한다.
    /// </para>
    /// </summary>
    LicenseSummary ReadSummary();

    /// <summary>
    /// 매니페스트가 <b>이름으로 지목한</b> 고지 파일 읽기. 고지 폴더 하위의 파일명만 허용하며
    /// 경로 구분자·상위 경로(<c>..</c>)·드라이브 문자는 거부한다 — 허용하면 이 화면이 임의 파일 리더가 된다.
    /// 실패는 <see cref="ReadText(LicenseDocument)"/>와 같은 문구로 돌려준다(예외 없음).
    /// </summary>
    LicenseTextResult ReadText(string fileName);
}
