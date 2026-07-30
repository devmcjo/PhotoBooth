using MCPhoto.Core.Models;
using MCPhoto.Core.Settings;

namespace MCPhoto.Core.Capture;

/// <summary>
/// 촬영 세션 상태. 컷 버퍼 관리·컷 선택(슬롯 수만큼)·재촬영 폐기. (PRD §F1, architecture §2.4)
/// 카운트다운 타이머 자체는 UI(ViewModel)가 구동하고, 이 클래스는 컷 데이터·선택 규칙을 담당.
/// </summary>
public sealed class CaptureSession
{
    private readonly List<CapturedStill> _cuts = new();
    private readonly List<int> _selection = new(); // 선택 순서 = 슬롯 순서
    private int _fullRetakeCount;                   // 전체 재촬영 실행 횟수 (it11 #13)

    /// <summary>선택된 프레임(촬영 전 고정).</summary>
    public FrameTemplate? Frame { get; private set; }

    /// <summary>촬영 컷 수(실효값 — <see cref="Begin"/>이 설정 의도를 해석한 결과). (it17)</summary>
    public int CutCount { get; private set; }

    /// <summary>이 세션의 컷 수가 자동 모드로 산출됐는지(Guide 화면 "(자동)" 배지). 설정은 세션 중에도
    /// 바뀔 수 있으므로 세션이 시작 시점의 의도를 기억한다(설계 §3.3). (it17)</summary>
    public bool IsAutoCutCount { get; private set; }

    /// <summary>슬롯 수(= 선택해야 할 컷 수).</summary>
    public int SlotCount => Frame?.Slots.Count ?? 0;

    /// <summary>촬영된 컷들(메모리 버퍼).</summary>
    public IReadOnlyList<CapturedStill> Cuts => _cuts;

    /// <summary>선택된 컷 인덱스(선택 순서 = 슬롯 순서).</summary>
    public IReadOnlyList<int> Selection => _selection;

    /// <summary>모든 컷 촬영 완료.</summary>
    public bool IsCaptureComplete => _cuts.Count >= CutCount;

    /// <summary>정확히 슬롯 수만큼 선택 완료.</summary>
    public bool IsSelectionComplete => _selection.Count == SlotCount && SlotCount > 0;

    public void Begin(FrameTemplate frame, int cutCount)
    {
        Frame = frame;
        // it17: cutCount는 "의도"(고정 6/8/10 또는 자동=CutCountPolicy.AutoCutCount).
        //       슬롯 수가 확정된 이 지점이 유일한 해석 지점이다(설계 §0.4).
        //       자동 = max(6, 슬롯+2) → 슬롯보다 여유분이 남아 컷 선택의 여지가 생긴다.
        //       고정 = max(설정, 슬롯) → 컷수 ≥ 슬롯 불변 유지(VF-5, 종전 동작 그대로).
        CutCount = CutCountPolicy.Resolve(cutCount, frame.Slots.Count);
        IsAutoCutCount = CutCountPolicy.IsAuto(cutCount);
        _cuts.Clear();
        _selection.Clear();
    }

    /// <summary>촬영된 컷 추가(셔터 시점).</summary>
    public void AddCut(CapturedStill still)
    {
        if (_cuts.Count < CutCount)
            _cuts.Add(still);
    }

    /// <summary>컷 선택 토글. 이미 선택되면 해제, 아니면 추가(슬롯 수 초과 불가, §9 #29).</summary>
    public bool ToggleSelection(int cutIndex)
    {
        if (cutIndex < 0 || cutIndex >= _cuts.Count) return false;

        int pos = _selection.IndexOf(cutIndex);
        if (pos >= 0)
        {
            _selection.RemoveAt(pos);
            return true;
        }

        if (_selection.Count >= SlotCount) return false; // 정확히 슬롯 수까지만
        _selection.Add(cutIndex);
        return true;
    }

    /// <summary>선택된 컷들을 슬롯 순서대로 반환(합성 입력). 슬롯 수만큼.</summary>
    public IReadOnlyList<CapturedStill> GetSelectedCuts()
        => _selection.Select(i => _cuts[i]).ToList();

    /// <summary>재촬영: 컷·선택 폐기(프레임 유지). 세션 전체 재촬영. (레거시 경로 — 카운터 미증가, 회귀 방지 유지)</summary>
    public void ResetForRetake()
    {
        _cuts.Clear();
        _selection.Clear();
    }

    // ── 전체 재촬영 카운터 (it11 #13). 컷별 재촬영은 후속 이터레이션(제외). ──

    /// <summary>지금까지 실행한 전체 재촬영 횟수.</summary>
    public int FullRetakeCount => _fullRetakeCount;

    /// <summary>전체 재촬영을 1회 이상 했는가.</summary>
    public bool HasFullRetaken => _fullRetakeCount > 0;

    /// <summary>전체 재촬영 가능 여부(limit 미도달). limit는 호출측이 전달(설정 의존 제거).</summary>
    public bool CanFullRetake(int limit) => _fullRetakeCount < limit;

    /// <summary>전체 재촬영 실행: 컷·선택 폐기 + 카운터 증가.</summary>
    public void BeginFullRetake()
    {
        _cuts.Clear();
        _selection.Clear();
        _fullRetakeCount++;
    }

    /// <summary>세션 완전 폐기(취소·완료·유휴).</summary>
    public void Discard()
    {
        Frame = null;
        _cuts.Clear();
        _selection.Clear();
        // CutCount=0은 여기선 "세션 없음"이라는 뜻이며 자동 sentinel과 무관하다(설계 §4.1).
        CutCount = 0;
        IsAutoCutCount = false;
        _fullRetakeCount = 0;
    }
}
