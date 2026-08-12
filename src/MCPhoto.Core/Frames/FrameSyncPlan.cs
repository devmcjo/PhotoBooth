namespace MCPhoto.Core.Frames;

/// <summary>
/// 로컬 캐시 ↔ 서버 정본 대조 결과.
/// </summary>
/// <param name="ToDownload">서버에만 있는 문서 id — 내려받아 캐시할 대상.</param>
/// <param name="ToDelete">로컬에만 있는 문서 id — 캐시에서 지울 대상(서버에서 삭제된 프레임).</param>
/// <param name="DeleteSkipReason">삭제를 건너뛴 사유. null이면 정상 판정. 로그·진단용.</param>
public sealed record FrameSyncDecision(
    IReadOnlyList<string> ToDownload,
    IReadOnlyList<string> ToDelete,
    string? DeleteSkipReason)
{
    /// <summary>안전장치에 걸려 삭제 판정을 보류했는가.</summary>
    public bool DeleteSkipped => DeleteSkipReason is not null;
}

/// <summary>
/// 프레임 캐시 동기화 판정(순수 로직).
/// <para>
/// "서버에 없으면 로컬에서도 지운다"는 규칙은 <b>대량 삭제를 자동 실행</b>한다. 판정이 한 번 틀리면
/// 사용자 프레임이 통째로 사라지므로, 이 클래스의 본체는 대조가 아니라 <b>안전장치</b>다(설계 §10).
/// </para>
/// <list type="number">
/// <item><b>서버 미도달</b> → 삭제 0건. 오프라인을 "서버에 없음"으로 오판하면 전부 지운다</item>
/// <item><b>서버 목록이 빈 배열</b> → 삭제 0건. 장애로 0개를 받았을 때의 참사를 막는다.
///       정말 다 지운 경우라도 다음 동기화에서 정리되므로 늦지 않다</item>
/// <item><b><c>#dbid</c> 있는 것만 대상</b> → <c>#dbid</c>가 없는 프레임(서버 미동기 <c>local:</c>)은
/// 자동으로 보호된다</item>
/// </list>
/// </summary>
public static class FrameSyncPlan
{
    /// <summary>서버 응답 실패 시 사유.</summary>
    public const string SkipServerUnreachable = "서버 미도달 — 삭제 판정 보류";

    /// <summary>서버 목록이 비었을 때 사유.</summary>
    public const string SkipEmptyServerList = "서버 목록이 비어 있음 — 삭제 판정 보류(장애 방어)";

    /// <summary>
    /// 다운로드·삭제 대상 산출.
    /// </summary>
    /// <param name="serverReachable">서버 목록 조회가 <b>성공</b>했는가. false면 삭제하지 않는다.</param>
    /// <param name="serverDbIds">서버 정본의 문서 id 목록.</param>
    /// <param name="localDbIds">로컬 캐시 중 <c>#dbid</c>를 가진 것들의 문서 id 목록(호출측이 필터링).</param>
    public static FrameSyncDecision Build(
        bool serverReachable,
        IReadOnlyList<string>? serverDbIds,
        IReadOnlyList<string>? localDbIds)
    {
        var local = Clean(localDbIds);

        // 안전장치 1 — 서버에 닿지 못했으면 아무 판단도 하지 않는다(다운로드도 불가).
        if (!serverReachable)
            return new FrameSyncDecision(Array.Empty<string>(), Array.Empty<string>(), SkipServerUnreachable);

        var server = Clean(serverDbIds);

        var toDownload = server.Where(id => !local.Contains(id)).ToList();

        // 안전장치 2 — 빈 목록은 "전부 삭제하라"는 신호로 받아들이지 않는다.
        if (server.Count == 0)
            return new FrameSyncDecision(toDownload, Array.Empty<string>(), SkipEmptyServerList);

        var toDelete = local.Where(id => !server.Contains(id)).ToList();
        return new FrameSyncDecision(toDownload, toDelete, null);
    }

    /// <summary>null·공백 제거 + 중복 제거. 문서 id는 대소문자를 구분한다(GUID 원문 보존).</summary>
    private static HashSet<string> Clean(IReadOnlyList<string>? ids)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (ids is null) return set;
        foreach (var id in ids)
            if (!string.IsNullOrWhiteSpace(id))
                set.Add(id.Trim());
        return set;
    }
}
