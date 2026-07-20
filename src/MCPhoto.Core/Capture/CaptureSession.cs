using MCPhoto.Core.Models;

namespace MCPhoto.Core.Capture;

/// <summary>
/// 촬영 세션 상태. 컷 버퍼 관리·컷 선택(슬롯 수만큼)·재촬영 폐기. (PRD §F1, architecture §2.4)
/// 카운트다운 타이머 자체는 UI(ViewModel)가 구동하고, 이 클래스는 컷 데이터·선택 규칙을 담당.
/// </summary>
public sealed class CaptureSession
{
    private readonly List<CapturedStill> _cuts = new();
    private readonly List<int> _selection = new(); // 선택 순서 = 슬롯 순서

    /// <summary>선택된 프레임(촬영 전 고정).</summary>
    public FrameTemplate? Frame { get; private set; }

    /// <summary>촬영 컷 수(설정값).</summary>
    public int CutCount { get; private set; }

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
        CutCount = Math.Max(cutCount, frame.Slots.Count); // 컷수 ≥ 슬롯(항상 성립, VF-5)
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

    /// <summary>재촬영: 컷·선택 폐기(프레임 유지). 세션 전체 재촬영.</summary>
    public void ResetForRetake()
    {
        _cuts.Clear();
        _selection.Clear();
    }

    /// <summary>세션 완전 폐기(취소·완료·유휴).</summary>
    public void Discard()
    {
        Frame = null;
        _cuts.Clear();
        _selection.Clear();
        CutCount = 0;
    }
}
