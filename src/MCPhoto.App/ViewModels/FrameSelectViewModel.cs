using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPhoto.App.Services;
using MCPhoto.Core.Frames;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.ViewModels;

/// <summary>프레임 선택. 게스트=기본만, 로그인=기본+커스텀. 촬영 전 선택·이후 고정. (PRD §F2, §9 #28)</summary>
public sealed partial class FrameSelectViewModel : ViewModelBase
{
    private readonly AppShellViewModel _shell;
    private readonly FrameCatalogService _catalog;
    private readonly ILocalFrameStore _localStore;
    private readonly IFrameRepository _repository;
    private readonly ILogger<FrameSelectViewModel>? _logger;

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

    // 삭제 결과 안내(서버 삭제 성공/실패/미발견). 성공 오인 방지.
    [ObservableProperty] private string _deleteNotice = string.Empty;
    [ObservableProperty] private bool _deleteNoticeIsError;

    public FrameSelectViewModel(AppShellViewModel shell, FrameCatalogService catalog,
        ILocalFrameStore localStore, IFrameRepository repository,
        ILogger<FrameSelectViewModel>? logger = null)
    {
        _shell = shell;
        _catalog = catalog;
        _localStore = localStore;
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// 이 프레임이 삭제 가능한지. 번들(설치 자산)·fallback은 불가, 그 외 로컬 저장분(user·파워 생성/캐시)은 가능. (it8 §4 A3 정정)
    /// user=local: 접두(로컬 전용), 파워 생성/캐시=실 DB id(접두 없음) — 둘 다 삭제 가능.
    /// </summary>
    public static bool IsDeletable(FrameTemplate frame)
        => !frame.Id.StartsWith("bundle:", StringComparison.Ordinal)
           && !frame.Id.StartsWith("fallback", StringComparison.Ordinal)
           && !string.IsNullOrEmpty(frame.Id);

    public override Task OnEnterAsync() => ReloadFramesAsync();

    /// <summary>디스크·DB 기준으로 프레임 목록을 재로드. 삭제 후에도 호출해 UI를 실제 파일 상태와 일치시킨다. (보완#3)</summary>
    private async Task ReloadFramesAsync()
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

    /// <summary>[확인]: 로컬 삭제 항상, "서버에서도 제거" 체크(파워) 시 DB 삭제(결과를 명확히 안내).</summary>
    [RelayCommand]
    private async Task ConfirmDelete()
    {
        var frame = FrameToDelete;
        if (frame is null) { CancelDelete(); return; }

        bool localOk = _localStore.DeleteLocal(frame);  // 로컬 파일(이미지+슬롯) 삭제
        var alsoServer = DeleteAlsoServer && IsPower;    // 팝업이 곧 닫히며 값이 리셋되므로 미리 확정
        DeleteNotice = string.Empty;
        DeleteNoticeIsError = false;

        Frames.Remove(frame);
        if (SelectedFrame == frame) SelectedFrame = Frames.FirstOrDefault();
        CancelDelete();

        if (alsoServer)
            await DeleteFromServerAsync(frame);

        if (!localOk)
        {
            // 성공 오인 금지: 로컬 파일이 실제로 지워지지 않았음을 알림(사용 중 등).
            DeleteNotice = string.IsNullOrEmpty(DeleteNotice)
                ? "로컬 프레임 파일을 삭제하지 못했습니다(사용 중일 수 있음)."
                : DeleteNotice + " (단, 로컬 파일 삭제 실패)";
            DeleteNoticeIsError = true;
            _logger?.LogWarning("로컬 프레임 삭제 실패: {Name} ({Path})", frame.Name, frame.ImageUrl);
        }

        // 디스크 기준 재스캔으로 목록을 실제 상태와 일치(삭제 성공분은 사라지고, 실패분은 다시 노출). (보완#3)
        await ReloadFramesAsync();
    }

    /// <summary>
    /// 서버(DB+Storage) 삭제. 저장된 서버 id(#dbid=GUID)로 삭제 시도 →
    /// 없으면(로컬 id 불일치·#dbid 누락) 이름으로 서버 기본 프레임을 재탐색해 삭제. 결과를 사용자에게 안내(성공 오인 금지).
    /// </summary>
    private async Task DeleteFromServerAsync(FrameTemplate frame)
    {
        // local: 접두는 로컬 전용 프레임(서버 문서 없음). 그 외는 실 DB 문서 id(GUID)를 담고 있음.
        var serverId = frame.Id.StartsWith("local:", StringComparison.Ordinal)
            ? frame.Id.Substring("local:".Length)
            : frame.Id;
        try
        {
            bool deleted = await _repository.DeleteAsync(serverId);

            // id로 못 찾으면(#dbid 누락/불일치) 이름으로 서버 기본 프레임을 찾아 삭제(파워 공용 프레임 대비).
            if (!deleted)
            {
                var dbFrames = await _repository.GetDefaultFramesAsync();
                var match = dbFrames.FirstOrDefault(f =>
                    string.Equals(f.Name, frame.Name, StringComparison.Ordinal) && !string.IsNullOrEmpty(f.Id));
                if (match is not null)
                {
                    _logger?.LogInformation("서버 삭제 id 불일치 → 이름 매칭 재삭제: {Name} (id={Id})", frame.Name, match.Id);
                    deleted = await _repository.DeleteAsync(match.Id);
                }
            }

            if (deleted)
            {
                DeleteNotice = "서버에서도 삭제되었습니다.";
                DeleteNoticeIsError = false;
            }
            else
            {
                DeleteNotice = $"로컬은 삭제했지만 서버에서 '{frame.Name}' 문서를 찾지 못했습니다.";
                DeleteNoticeIsError = true;
                _logger?.LogWarning("서버 삭제 실패: 문서 미발견 name={Name} triedId={Id}", frame.Name, serverId);
            }
        }
        catch (Exception ex)
        {
            // 성공 오인 금지: 서버 삭제 실패를 사용자에게 노출(미초기화·권한 등).
            DeleteNotice = $"서버 삭제 실패: {ex.Message}";
            DeleteNoticeIsError = true;
            _logger?.LogError(ex, "프레임 서버 삭제 실패 id={Id}", serverId);
        }
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
