namespace MCPhoto.Core.Frames;

/// <summary>기본 프레임 준비 단계. 사용자에게 보이는 문구의 유일한 분기 축. (it20)</summary>
public enum FrameCatalogPhase
{
    /// <summary>설치·캐시된 로컬 프레임을 확인한다.</summary>
    ResolvingLocal,
    /// <summary>서버에서 기본 프레임 목록을 조회한다.</summary>
    QueryingServer,
    /// <summary>프레임 이미지를 내려받는다(<see cref="FrameCatalogProgress.Index"/>/<see cref="FrameCatalogProgress.Total"/>).</summary>
    DownloadingImage,
    /// <summary>모든 준비가 끝났다(마지막 보고 — 늦게 합류한 구독자의 replay용).</summary>
    Completed
}

/// <summary>
/// 기본 프레임 준비 진행 상황. <c>IProgress&lt;FrameCatalogProgress&gt;</c>로 보고된다. (it20)
/// 표시 문구를 <see cref="ToLabel"/> 순수 함수로 함께 제공한다 — ViewModel이 문자열을 조립하지 않으므로
/// 문구가 UI 없이 단위 테스트된다(<c>UserRole.ToLabel()</c> 관례와 동형).
/// </summary>
public readonly record struct FrameCatalogProgress(
    FrameCatalogPhase Phase,
    int Index = 0,
    int Total = 0)
{
    /// <summary>로딩 시작 직후(아직 어떤 보고도 없을 때) 보여줄 기본 문구.</summary>
    public const string StartLabel = "기본 프레임을 준비하고 있어요…";

    /// <summary>이 진행 상황의 한국어 표시 문구. Total&gt;0이면 "(n/m)" 카운터를 덧붙인다.</summary>
    public string ToLabel() => Phase switch
    {
        FrameCatalogPhase.ResolvingLocal => "설치된 프레임을 확인하는 중…",
        FrameCatalogPhase.QueryingServer => "서버에서 기본 프레임 목록을 확인하는 중…",
        FrameCatalogPhase.DownloadingImage => Total > 0
            ? $"기본 프레임 내려받는 중… ({Index}/{Total})"
            : "기본 프레임 내려받는 중…",
        FrameCatalogPhase.Completed => "프레임 목록을 정리하는 중…",
        _ => StartLabel
    };
}
