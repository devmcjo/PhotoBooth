using System.IO;
using System.Text.RegularExpressions;

namespace MCPhoto.Core.Frames;

/// <summary>
/// 프레임 사본 이름 생성(순수). 서버 프레임을 불러와 새로 만들 때 원본과 이름을 구분해
/// 원본 파일을 보존하고 FrameCatalogService의 이름 기준 dedup(재다운로드 방지)을 유지한다. (it15 F1-D4)
/// </summary>
public static partial class FrameNaming
{
    /// <summary>사본 접미 기본 토큰. 파일명 규약상 '_'는 쓰지 않는다(LocalFrameStore 공용/user 구분자).</summary>
    public const string CopySuffix = "사본";

    /// <summary>이름이 비어 있을 때 사용하는 기본 base(결과는 "새 프레임 사본").</summary>
    public const string DefaultBaseName = "새 프레임";

    /// <summary>충돌 회피 번호 상한. 초과 시 GUID 8자 접미로 폴백한다.</summary>
    private const int MaxCopyIndex = 99;

    /// <summary>"{X} 사본" / "{X} 사본 N"(N=1~2자리) 접미 파싱. '_'를 새로 도입하지 않는다(§1.5 함정).</summary>
    [GeneratedRegex(@"^(?<base>.*?)\s*사본(\s+(?<n>\d{1,2}))?$", RegexOptions.CultureInvariant)]
    private static partial Regex CopySuffixPattern();

    /// <summary>
    /// baseName 기준으로 existingNames와 충돌하지 않는 사본 이름을 만든다.
    /// "{base} 사본" → 충돌 시 "{base} 사본 2", "{base} 사본 3" … 99까지.
    /// baseName이 이미 "{X} 사본" / "{X} 사본 N" 형태면 X를 base로 되돌려 무한 누적을 막는다.
    /// 99까지 모두 충돌하면 "{base} 사본 {8자리 GUID}"를 반환한다(항상 이름을 돌려준다).
    /// 비교는 <see cref="StringComparer.Ordinal"/>(LocalFrameStore 파일명 규약과 동일).
    /// </summary>
    /// <param name="baseName">원본 이름. null·공백이면 <see cref="DefaultBaseName"/>을 base로 사용.</param>
    /// <param name="existingNames">같은 저장 스코프의 기존 이름들(공용=PublicFrameNames, 개인=LoadUser).</param>
    public static string NextCopyName(string? baseName, IEnumerable<string> existingNames)
    {
        var root = StripCopySuffix(baseName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(root)) root = DefaultBaseName;

        var taken = new HashSet<string>(
            existingNames.Where(n => !string.IsNullOrEmpty(n)), StringComparer.Ordinal);

        var first = $"{root} {CopySuffix}";
        if (!taken.Contains(first)) return first;

        for (int n = 2; n <= MaxCopyIndex; n++)
        {
            var candidate = $"{root} {CopySuffix} {n}";
            if (!taken.Contains(candidate)) return candidate;
        }

        // 1~99 전부 충돌: 예외 대신 항상 이름을 돌려준다(저장을 막지 않는다).
        var unique = Guid.NewGuid().ToString("N")[..8];
        return $"{root} {CopySuffix} {unique}";
    }

    /// <summary>
    /// 로컬 파일명으로 쓸 수 있는 이름인지(빈 값·공백 아님 + 파일시스템 금지문자 없음). 순수 함수.
    /// <c>LocalFrameStore.EnsureFileNameSafe</c>의 판정과 **동일**하다 — 저장 전 선검증에 쓰기 위해 추출했다.
    /// 파워 신규 생성은 서버 insert를 먼저 하고 로컬 저장을 나중에 하므로, 이 선검증 없이는 잘못된 이름에서
    /// "서버에는 등록됐지만 로컬에는 없는" 반쪽 상태가 만들어진다.
    /// </summary>
    public static bool IsFileNameSafe(string? name)
        => !string.IsNullOrWhiteSpace(name)
           && name!.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    /// <summary>
    /// 저장 가능한 이름인가 — <b>본인에게 보이는 프레임과 이름이 겹치지 않아야 한다</b>(설계 D-17).
    /// <para>
    /// 판정 집합 = <b>공용 프레임 이름 ∪ 본인 개인 프레임 이름</b>. 다른 계정의 개인 프레임과는
    /// 겹쳐도 된다 — 저장 폴더가 계정별로 나뉘어 있고 서로 보이지도 않는다.
    /// </para>
    /// <para>
    /// 비교는 <b>대소문자 무시</b>다. Windows 파일시스템이 대소문자를 구분하지 않으므로
    /// <c>"Abc"</c>와 <c>"abc"</c>를 허용하면 실제 파일이 서로 덮어써진다.
    /// </para>
    /// ⚠️ 이 검증은 <b>즉시 피드백</b>용이다. PC 두 대에서 동시에 같은 이름을 만드는 경우는 막지 못하므로
    /// <b>서버가 계정 내 이름 중복을 최종 거부</b>해야 한다(설계 S8).
    /// </summary>
    /// <param name="name">저장하려는 이름.</param>
    /// <param name="visibleNames">현재 사용자에게 보이는 프레임 이름 전부(공용 + 본인 개인).</param>
    public static bool IsNameAvailable(string? name, IEnumerable<string>? visibleNames)
    {
        var candidate = (name ?? string.Empty).Trim();
        if (candidate.Length == 0) return false;
        if (visibleNames is null) return true;

        foreach (var taken in visibleNames)
        {
            if (string.IsNullOrWhiteSpace(taken)) continue;
            if (string.Equals(taken.Trim(), candidate, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    /// <summary>
    /// "{X} 사본" / "{X} 사본 N" 접미를 제거해 원형 이름을 얻는다(접미가 없으면 원문 그대로).
    /// 접미를 떼면 이름이 비게 되는 경우(예: "사본")도 원문을 그대로 반환한다 — 빈 이름을 만들지 않는다.
    /// </summary>
    public static string StripCopySuffix(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name;

        var m = CopySuffixPattern().Match(name.Trim());
        if (!m.Success) return name;

        var stripped = m.Groups["base"].Value.TrimEnd();
        return string.IsNullOrWhiteSpace(stripped) ? name : stripped;
    }
}
