namespace MCPhoto.Core.Settings;

/// <summary>
/// 설정 파일 위치 진단(순수 함수). 경로 <b>정책은 바꾸지 않는다</b> — 관측만 추가한다. (it26 §3.5)
/// <para>
/// 왜 필요한가: ini 후보 1순위는 실행경로이므로 <b>승격 실행</b>이면 <c>C:\Program Files\MCPhoto\MCPhoto.ini</c>에,
/// 평소(비승격)엔 <c>%ProgramData%\MCPhoto\MCPhoto.ini</c>에 쓰인다. 같은 PC에서 실행 방식에 따라 설정이
/// 갈리는 사고가 재현 불가능한 형태로 발생하는데, 그 사실이 로그에 한 줄도 남지 않았다.
/// </para>
/// <para>
/// ⚠️ 순서를 바꾸지 않는 이유: 바꾸면 ① 승격으로 운영해 온 기존 설치가 자기 설정을 잃고
/// ② 개발 실행이 설치본과 같은 ini를 공유해 <c>[Test]</c>(인증 우회)가 전파된다.
/// </para>
/// </summary>
public static class SettingsPathDiagnostics
{
    /// <summary>
    /// <paramref name="path"/>가 Program Files(또는 x86) 하위인지. 대소문자·후행 구분자 무관.
    /// </summary>
    /// <remarks>
    /// ⚠️ 접두가 빈 문자열이면 <b>모든 경로가 true</b>가 되는 함정이 있다(테스트 환경에서 특수 폴더가
    /// 비는 경우가 있다) — 빈 접두는 판정 대상에서 제외한다.
    /// </remarks>
    public static bool IsUnderProgramFiles(string? path, string? programFiles, string? programFilesX86)
        => IsUnder(path, programFiles) || IsUnder(path, programFilesX86);

    private static bool IsUnder(string? path, string? root)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root)) return false;

        try
        {
            var full = Normalize(path);
            var prefix = Normalize(root);
            if (prefix.Length == 0) return false;

            if (string.Equals(full, prefix, StringComparison.OrdinalIgnoreCase)) return true;

            // "C:\Program Files" 와 "C:\Program Files Extra" 를 구분하려면 경계에 구분자가 있어야 한다.
            return full.Length > prefix.Length
                && full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && (full[prefix.Length] == Path.DirectorySeparatorChar
                    || full[prefix.Length] == Path.AltDirectorySeparatorChar);
        }
        catch
        {
            // 잘못된 문자·과도한 길이 등 — 진단 목적이므로 판정 불가는 false(로그를 남기지 않는다).
            return false;
        }
    }

    private static string Normalize(string value)
        => Path.GetFullPath(value.Trim())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
