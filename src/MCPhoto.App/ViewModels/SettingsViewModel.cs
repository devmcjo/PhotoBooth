using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.ViewModels;

/// <summary>
/// 설정 페이지 VM. [앱 설정](게스트 포함)·[계정](로그인)·[관리자](power) 3섹션. (it2 §4)
/// AppSettings 전 항목 편집(OutputFormat/DisplayMode/StorageBucket 포함) + 계정/관리자 기능.
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly AppShellViewModel _shell;
    private readonly ISettingsService _settings;
    private readonly IAccountService _accounts;
    private readonly ILogger<SettingsViewModel>? _logger;

    private DispatcherTimer? _noticeTimer;

    // PasswordBox는 바인딩 불가 → View 코드비하인드가 여기에 전달(기존 AdminView 패턴)
    public string NewPassword { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;

    // ── [계정] 비번 변경 ──
    [ObservableProperty] private string _accountMessage = string.Empty;
    [ObservableProperty] private bool _accountMessageIsError;

    // ── [관리자] 계정 생성 ──
    [ObservableProperty] private string _newAccountId = string.Empty;
    public string NewAccountPassword { get; set; } = string.Empty; // code-behind 전달
    [ObservableProperty] private UserRole _selectedNewRole = UserRole.User;
    [ObservableProperty] private string _adminMessage = string.Empty;
    [ObservableProperty] private bool _adminMessageIsError;

    /// <summary>로그인 역할이 생성 가능한 역할 목록(admin→[User,Manager], manager→[User]).</summary>
    public ObservableCollection<UserRole> CreatableRoles { get; } = new();

    // ── 섹션 표시 플래그(Session 기반) ──
    public bool IsGuest => _shell.Session.CurrentUser is null;
    public bool IsLoggedIn => _shell.Session.CurrentUser is not null;
    public bool IsPower => _shell.Session.CurrentUser?.Role.IsPower() == true;

    // ── [앱 설정] 필드 (AppSettings 전 항목, it2 §4.2) ──
    [ObservableProperty] private int _cutCount;
    [ObservableProperty] private int _countdownSec;
    [ObservableProperty] private bool _mirrorMode;
    [ObservableProperty] private bool _flashMode;
    [ObservableProperty] private bool _enableQrDelivery;
    [ObservableProperty] private bool _saveLocalCopy;
    [ObservableProperty] private int _retentionHours;
    [ObservableProperty] private string _localSavePath = string.Empty;
    [ObservableProperty] private string _hostingBaseUrl = string.Empty;
    [ObservableProperty] private int _cameraDevice;
    [ObservableProperty] private OutputFormat _outputFormat;      // 신규 노출(VF-12)
    [ObservableProperty] private DisplayMode _displayMode;        // 신규 노출(VF-12)
    [ObservableProperty] private string _storageBucket = string.Empty; // 신규 노출(VF-12)

    [ObservableProperty] private string _savedNotice = string.Empty;
    [ObservableProperty] private bool _savedNoticeIsError; // 성공=false(민트/성공색), 실패=true(로즈/danger)

    /// <summary>컷수 옵션(세그먼트 바인딩).</summary>
    public IReadOnlyList<int> CutCountOptions { get; } = AppSettings.AllowedCutCounts;
    /// <summary>카운트다운 옵션.</summary>
    public IReadOnlyList<int> CountdownOptions { get; } = AppSettings.AllowedCountdownSecs;
    /// <summary>출력 포맷 옵션.</summary>
    public IReadOnlyList<OutputFormat> OutputFormatOptions { get; } = new[] { OutputFormat.Jpg, OutputFormat.Png };
    /// <summary>표시 모드 옵션.</summary>
    public IReadOnlyList<DisplayMode> DisplayModeOptions { get; } = new[] { DisplayMode.Fullscreen, DisplayMode.Windowed };

    public SettingsViewModel(AppShellViewModel shell, ISettingsService settings, IAccountService accounts, ILogger<SettingsViewModel>? logger = null)
    {
        _shell = shell;
        _settings = settings;
        _accounts = accounts;
        _logger = logger;
    }

    public override Task OnEnterAsync()
    {
        LoadSettings();
        OnPropertyChanged(nameof(IsGuest));
        OnPropertyChanged(nameof(IsLoggedIn));
        OnPropertyChanged(nameof(IsPower));

        // 생성 가능 역할 갱신(로그인 역할 기반)
        CreatableRoles.Clear();
        var role = _shell.Session.CurrentUser?.Role;
        if (role is { } r)
            foreach (var cr in r.CreatableRoles())
                CreatableRoles.Add(cr);
        SelectedNewRole = CreatableRoles.Count > 0 ? CreatableRoles[0] : UserRole.User;

        return Task.CompletedTask;
    }

    private void LoadSettings()
    {
        var s = _settings.Current;
        CutCount = s.CutCount;
        CountdownSec = s.CountdownSec;
        MirrorMode = s.MirrorMode;
        FlashMode = s.FlashMode;
        EnableQrDelivery = s.EnableQrDelivery;
        SaveLocalCopy = s.SaveLocalCopy;
        RetentionHours = s.RetentionHours;
        LocalSavePath = s.LocalSavePath;
        HostingBaseUrl = s.HostingBaseUrl;
        CameraDevice = s.CameraDevice;
        OutputFormat = s.OutputFormat;
        DisplayMode = s.DisplayMode;
        StorageBucket = s.StorageBucket;
    }

    /// <summary>[앱 설정] 저장: 필드 → AppSettings → Clamp → INI flush. (it2 §4.2)</summary>
    [RelayCommand]
    private void SaveSettings()
    {
        var s = _settings.Current;
        s.CutCount = CutCount;
        s.CountdownSec = CountdownSec;
        s.MirrorMode = MirrorMode;
        s.FlashMode = FlashMode;
        s.EnableQrDelivery = EnableQrDelivery;
        s.SaveLocalCopy = SaveLocalCopy;
        s.RetentionHours = RetentionHours;
        s.LocalSavePath = LocalSavePath;
        s.HostingBaseUrl = HostingBaseUrl;
        s.CameraDevice = CameraDevice;
        s.OutputFormat = OutputFormat;
        s.DisplayMode = DisplayMode;
        s.StorageBucket = StorageBucket;

        var ok = _settings.Save(); // bool 반환(폴백 체인). 내부에서 Clamp() 호출
        LoadSettings();            // 클램프된 값 반영
        if (ok)
        {
            ShowNotice("저장되었습니다.", isError: false);
            _logger?.LogInformation("AppSettings 저장 성공(설정 페이지)");
        }
        else
        {
            // 성공 오인 금지(it3 §3): 쓰기 실패 시 오류 토스트
            ShowNotice("저장 위치에 쓸 수 없습니다. 관리자에게 문의하세요.", isError: true);
            _logger?.LogWarning("AppSettings 저장 실패(모든 경로 쓰기 불가)");
        }
    }

    // ── [계정] 비밀번호 변경 (it2 §4.3, 2회 확인) ──

    [RelayCommand]
    private async Task ChangePassword()
    {
        var user = _shell.Session.CurrentUser;
        if (user is null) return;

        if (string.IsNullOrWhiteSpace(NewPassword))
        {
            SetAccountMessage("새 비밀번호를 입력하세요.", isError: true);
            return;
        }
        if (NewPassword != ConfirmPassword)
        {
            SetAccountMessage("새 비밀번호가 일치하지 않습니다.", isError: true);
            return;
        }

        try
        {
            await _accounts.ChangePasswordAsync(user.Id, NewPassword);
            user.Password = NewPassword;
            NewPassword = ConfirmPassword = string.Empty;
            SetAccountMessage("비밀번호가 변경되었습니다.", isError: false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "비밀번호 변경 실패");
            SetAccountMessage("변경에 실패했습니다.", isError: true);
        }
    }

    // ── [관리자] 계정 생성 (it2 §4.4·§7, 역할 게이트) ──

    [RelayCommand]
    private async Task CreateAccount()
    {
        var acting = _shell.Session.CurrentUser?.Role;
        if (acting is not { } actingRole || !actingRole.IsPower())
        {
            SetAdminMessage("권한이 없습니다.", isError: true);
            return;
        }
        if (string.IsNullOrWhiteSpace(NewAccountId) || string.IsNullOrWhiteSpace(NewAccountPassword))
        {
            SetAdminMessage("아이디와 비밀번호를 입력하세요.", isError: true);
            return;
        }

        try
        {
            var createdId = NewAccountId.Trim(); // 비우기 전에 보존(메시지 조립용)
            await _accounts.CreateAsync(createdId, NewAccountPassword, SelectedNewRole, actingRole);
            NewAccountId = string.Empty;
            NewAccountPassword = string.Empty;
            SetAdminMessage($"'{createdId}' 계정을 생성했습니다.", isError: false);
        }
        catch (UnauthorizedAccessException)
        {
            SetAdminMessage("해당 역할을 생성할 권한이 없습니다.", isError: true);
        }
        catch (InvalidOperationException ex)
        {
            // 중복 id 또는 미초기화
            SetAdminMessage(ex.Message, isError: true);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "계정 생성 실패");
            SetAdminMessage("생성에 실패했습니다.", isError: true);
        }
    }

    /// <summary>사용자 관리 화면 진입(power).</summary>
    [RelayCommand]
    private async Task OpenUserManagement()
    {
        if (IsPower)
            await _shell.NavigateAsync(AppState.UserMgmt);
    }

    /// <summary>앱 종료(관리자, 기존 AdminView에서 이관).</summary>
    [RelayCommand]
    private void ExitApp() => Application.Current.Shutdown();

    /// <summary>[닫기]: 오버레이 복귀(직전 화면). 세션 보존.</summary>
    [RelayCommand]
    private async Task Close() => await _shell.ReturnFromOverlay();

    private void SetAccountMessage(string text, bool isError)
    {
        AccountMessage = text;
        AccountMessageIsError = isError;
    }

    private void SetAdminMessage(string text, bool isError)
    {
        AdminMessage = text;
        AdminMessageIsError = isError;
    }

    private void ShowNotice(string text, bool isError = false)
    {
        SavedNotice = text;
        SavedNoticeIsError = isError;
        _noticeTimer?.Stop();
        // 오류는 사용자가 읽을 시간을 더 준다(성공 3초, 실패 6초)
        _noticeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(isError ? 6 : 3) };
        _noticeTimer.Tick += (_, _) =>
        {
            _noticeTimer?.Stop();
            SavedNotice = string.Empty;
        };
        _noticeTimer.Start();
    }
}
