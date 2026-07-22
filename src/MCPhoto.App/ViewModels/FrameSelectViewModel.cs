using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPhoto.App.Services;
using MCPhoto.Core.Frames;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;

namespace MCPhoto.App.ViewModels;

/// <summary>프레임 선택. 게스트=기본만, 로그인=기본+커스텀. 촬영 전 선택·이후 고정. (PRD §F2, §9 #28)</summary>
public sealed partial class FrameSelectViewModel : ViewModelBase
{
    private readonly AppShellViewModel _shell;
    private readonly FrameCatalogService _catalog;
    private readonly ILocalFrameStore _localStore;
    private readonly IFrameRepository _repository;

    public ObservableCollection<FrameTemplate> Frames { get; } = new();

    [ObservableProperty] private FrameTemplate? _selectedFrame;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isLoggedIn;

    // A3 삭제 UI 상태
    [ObservableProperty] private bool _canDeleteFrames;  // 로그인 여부(게스트 미노출)
    [ObservableProperty] private bool _isPower;
    [ObservableProperty] private bool _isDeleteConfirmVisible;
    [ObservableProperty] private FrameTemplate? _frameToDelete;
    [ObservableProperty] private bool _deleteAlsoServer;  // 파워만 노출·유효

    public FrameSelectViewModel(AppShellViewModel shell, FrameCatalogService catalog,
        ILocalFrameStore localStore, IFrameRepository repository)
    {
        _shell = shell;
        _catalog = catalog;
        _localStore = localStore;
        _repository = repository;
    }

    /// <summary>
    /// 이 프레임이 삭제 가능한지. 번들(설치 자산)·fallback은 불가, 그 외 로컬 저장분(user·파워 생성/캐시)은 가능. (it8 §4 A3 정정)
    /// user=local: 접두(로컬 전용), 파워 생성/캐시=실 DB id(접두 없음) — 둘 다 삭제 가능.
    /// </summary>
    public static bool IsDeletable(FrameTemplate frame)
        => !frame.Id.StartsWith("bundle:", StringComparison.Ordinal)
           && !frame.Id.StartsWith("fallback", StringComparison.Ordinal)
           && !string.IsNullOrEmpty(frame.Id);

    public override async Task OnEnterAsync()
    {
        IsLoading = true;
        Frames.Clear();
        try
        {
            var user = _shell.Session.CurrentUser;
            IsLoggedIn = user is not null;
            CanDeleteFrames = user is not null;      // 게스트 미노출
            IsPower = user?.Role.IsPower() == true;

            foreach (var f in await _catalog.GetDefaultFramesAsync())
                Frames.Add(f);

            if (user is not null)
                foreach (var f in await _catalog.GetUserFramesAsync(user.Id))
                    Frames.Add(f);

            SelectedFrame = Frames.FirstOrDefault();
        }
        finally { IsLoading = false; }
    }

    // ── A3: 프레임 삭제(로컬 항상 + 파워 서버 옵션) ──

    /// <summary>카드 X → 확인 팝업 표시.</summary>
    [RelayCommand]
    private void RequestDelete(FrameTemplate? frame)
    {
        if (frame is null || !CanDeleteFrames || !IsDeletable(frame)) return;
        FrameToDelete = frame;
        DeleteAlsoServer = false;          // 기본 off
        IsDeleteConfirmVisible = true;
    }

    /// <summary>[확인]: 로컬 삭제 항상, "서버에서도 제거" 체크(파워) 시 DB 삭제.</summary>
    [RelayCommand]
    private async Task ConfirmDelete()
    {
        var frame = FrameToDelete;
        if (frame is null) { CancelDelete(); return; }

        _localStore.DeleteLocal(frame);                 // 로컬 항상

        if (DeleteAlsoServer && IsPower)
        {
            // 파워 캐시/생성 프레임은 Id에 실제 DB 문서 id를 보존(CacheFromDb/SaveLocal이 .slots #dbid로 기록).
            // 방어적으로 local: 접두가 있으면 제거(로컬 전용 프레임엔 서버 문서 없어 no-op).
            var serverId = frame.Id.StartsWith("local:", StringComparison.Ordinal)
                ? frame.Id.Substring("local:".Length)
                : frame.Id;
            try { await _repository.DeleteAsync(serverId); }
            catch { /* 서버 삭제 실패는 무시(로컬은 이미 제거) */ }
        }

        Frames.Remove(frame);
        if (SelectedFrame == frame) SelectedFrame = Frames.FirstOrDefault();
        CancelDelete();
    }

    /// <summary>[취소]: 팝업 닫기.</summary>
    [RelayCommand]
    private void CancelDelete()
    {
        IsDeleteConfirmVisible = false;
        FrameToDelete = null;
        DeleteAlsoServer = false;
    }

    [RelayCommand]
    private async Task Next()
    {
        if (SelectedFrame is null) return;
        _shell.Session.SelectedFrame = SelectedFrame;
        _shell.Session.Capture.Begin(SelectedFrame, _shell.Settings.Current.CutCount);
        await _shell.NavigateAsync(AppState.Guide);
    }

    /// <summary>프레임 편집기 진입(로그인 사용자만).</summary>
    [RelayCommand]
    private async Task CreateFrame()
    {
        if (!IsLoggedIn) return;
        await _shell.NavigateAsync(AppState.FrameEditor);
    }

    [RelayCommand]
    private void Cancel() => _shell.ReturnHome("프레임 선택 취소");
}
