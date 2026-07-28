using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MCPhoto.App.Services;
using MCPhoto.Core.Models;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.ViewModels;

/// <summary>
/// "기존 프레임 불러오기" 선택 모달의 목록 VM. (it15 F2)
/// 편집기 오버레이가 목록 영역의 DataContext로 쓴다. 확인/취소 커맨드는 소유자(FrameEditorViewModel)가 갖는다 —
/// 이벤트를 정의하지도 구독하지도 않으므로 구독 해제 경로가 필요 없다(누수 없음).
/// System.Windows 타입을 노출하지 않아 창 없이 단위 테스트 가능하다.
/// </summary>
public sealed partial class FramePickerViewModel : ObservableObject
{
    private readonly FrameCatalogService _catalog;
    private readonly ILogger<FramePickerViewModel>? _logger;

    /// <summary>선택 후보 프레임(공용=번들+DB캐시+DB다운로드, + 로그인 계정 개인 로컬).</summary>
    public ObservableCollection<FrameTemplate> Frames { get; } = new();

    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [ObservableProperty] private FrameTemplate? _selectedFrame;

    /// <summary>목록 로딩 중(오버레이에 안내 표시 + 그리드 숨김).</summary>
    [ObservableProperty] private bool _isLoading;

    /// <summary>후보가 없거나 로드 실패했을 때의 안내(비어 있으면 정상).</summary>
    [ObservableProperty] private string _emptyNotice = string.Empty;

    /// <summary>선택된 프레임이 있는지(확인 버튼 활성 조건).</summary>
    public bool HasSelection => SelectedFrame is not null;

    public FramePickerViewModel(FrameCatalogService catalog, ILogger<FramePickerViewModel>? logger = null)
    {
        _catalog = catalog;
        _logger = logger;
    }

    /// <summary>
    /// 후보 목록 로드. FrameSelect 화면과 동일한 소스를 쓴다(사용자가 같은 목록을 기대).
    /// 전 구간 await — UI 스레드를 블로킹하지 않는다. 취소 시 예외를 전파하지 않고 조용히 종료한다.
    /// </summary>
    /// <param name="userId">로그인 계정 id. null이면 공용 프레임만 로드.</param>
    public async Task LoadAsync(string? userId, CancellationToken ct = default)
    {
        IsLoading = true;
        EmptyNotice = string.Empty;
        Frames.Clear();
        SelectedFrame = null;

        try
        {
            // 공용: 번들 + 파워 캐시 + DB isDefault 다운로드(이름 dedup). FrameCatalogService가 동시 호출을 직렬화한다.
            foreach (var f in await _catalog.GetDefaultFramesAsync(ct))
                Frames.Add(f);

            // 개인: 본인 `{계정}_` 접두 로컬만(타인 것은 LoadUser가 접두로 자동 제외).
            if (!string.IsNullOrEmpty(userId))
                foreach (var f in await _catalog.GetUserFramesAsync(userId, ct))
                    Frames.Add(f);

            if (Frames.Count == 0)
                EmptyNotice = "불러올 수 있는 프레임이 없습니다.";
        }
        catch (OperationCanceledException)
        {
            // 모달을 닫거나 재오픈해서 취소된 경우 — 안내 없이 종료(부분 목록은 그대로 둔다).
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "기존 프레임 목록 로드 실패");
            EmptyNotice = "프레임 목록을 불러오지 못했습니다.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>모달을 닫을 때 상태 초기화(선택 해제 · 목록 비우기 · 안내 제거).</summary>
    public void Reset()
    {
        SelectedFrame = null;
        Frames.Clear();
        EmptyNotice = string.Empty;
        IsLoading = false;
    }
}
