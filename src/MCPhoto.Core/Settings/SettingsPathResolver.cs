namespace MCPhoto.Core.Settings;

/// <summary>
/// 설정 INI 경로 우선순위 결정(순수 로직, 테스트 대상). (it6 #1)
/// 우선순위: 실행경로 → %ProgramData%\MCPhoto → %LocalAppData%\MCPhoto.
/// 쓰기 가능 여부는 주입된 판정 함수로 결정(테스트 시 대체 가능).
/// </summary>
public static class SettingsPathResolver
{
    public const string FileName = "MCPhoto.ini";

    /// <summary>
    /// 후보 경로 순서대로 쓰기 가능한 첫 경로를 고른다. 하나도 없으면 첫 후보(실행경로)를 반환
    /// (Save 시점 폴백 체인이 다시 시도하므로 최종 실패를 여기서 단정하지 않음).
    /// </summary>
    /// <param name="candidates">우선순위 순 후보 파일 경로.</param>
    /// <param name="canWrite">해당 경로에 쓰기 가능한지 판정(디렉터리 생성+임시 쓰기 등).</param>
    public static string ResolveWritable(IReadOnlyList<string> candidates, Func<string, bool> canWrite)
    {
        if (candidates.Count == 0)
            throw new ArgumentException("후보 경로가 비어 있습니다.", nameof(candidates));

        foreach (var c in candidates)
            if (canWrite(c)) return c;

        return candidates[0]; // 전부 실패 시 1순위(실행경로) — Save 폴백이 재시도
    }

    /// <summary>기본 후보 경로 목록(우선순위 순): 실행경로 → ProgramData → LocalAppData.</summary>
    public static IReadOnlyList<string> DefaultCandidates(string exeDir, string programData, string localAppData)
        => new[]
        {
            Path.Combine(exeDir, FileName),
            Path.Combine(programData, "MCPhoto", FileName),
            Path.Combine(localAppData, "MCPhoto", FileName)
        };
}
