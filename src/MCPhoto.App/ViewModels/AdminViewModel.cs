using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.ViewModels;

/// <summary>
/// 관리자 모드. 진입 = 좌상단 3초 롱프레스 + 로그인. AppSettings 편집(로그인 계정 누구나),
/// 사용자 관리·공용 기본 프레임(power 전용), 앱 종료. (PRD §F7)
/// </summary>
public sealed partial class AdminViewModel : ViewModelBase
{
    private readonly AppShellViewModel _shell;
    private readonly ISettingsService _settings;
    private readonly IAccountService _accounts;
    private readonly ILogger<AdminViewModel>? _logger;

    // 로그인 게이트
    [ObservableProperty] private bool _isAuthenticated;
    [ObservableProperty] private string _loginId = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _isPower;

    // AppSettings 편집 필드
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
    [ObservableProperty] private string _savedNotice = string.Empty;

    public AdminViewModel(AppShellViewModel shell, ISettingsService settings, IAccountService accounts, ILogger<AdminViewModel>? logger = null)
    {
        _shell = shell;
        _settings = settings;
        _accounts = accounts;
        _logger = logger;
    }

    public override Task OnEnterAsync()
    {
        // 이미 로그인한 세션이면 통과, 아니면 로그인 요구
        var user = _shell.Session.CurrentUser;
        if (user is not null)
        {
            Authenticate(user);
        }
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task Login()
    {
        ErrorMessage = string.Empty;
        try
        {
            var user = await _accounts.LoginAsync(LoginId.Trim(), Password);
            if (user is null) { ErrorMessage = "로그인 실패"; return; }
            _shell.Session.CurrentUser = user;
            Authenticate(user);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "관리자 로그인 실패");
            ErrorMessage = "로그인할 수 없습니다.";
        }
    }

    private void Authenticate(User user)
    {
        IsAuthenticated = true;
        IsPower = user.Role.IsPower();
        LoadSettings();
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
    }

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
        _settings.Save();
        SavedNotice = "저장되었습니다.";
        _logger?.LogInformation("AppSettings 저장(관리자 모드)");
    }

    /// <summary>사용자 관리(power 전용).</summary>
    [RelayCommand]
    private async Task OpenUserManagement()
    {
        if (!IsPower) return;
        await _shell.NavigateAsync(AppState.UserMgmt);
    }

    /// <summary>앱 종료.</summary>
    [RelayCommand]
    private void ExitApp() => Application.Current.Shutdown();

    [RelayCommand]
    private void Close() => _shell.ReturnHome("관리자 모드 종료");
}
