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

/// <summary>계정 페이지 진입 모드. 팝오버 항목이 지정. (it5 §5 C2)</summary>
public enum AccountMode
{
    /// <summary>비밀번호 변경(로그인 사용자 자기 비번, 2회 확인).</summary>
    PasswordChange,

    /// <summary>계정 생성(power — 역할 게이트).</summary>
    AccountCreate,

    /// <summary>관리자 도구(사용자 관리 진입·앱 종료, power).</summary>
    Admin
}

/// <summary>
/// 계정 전용 페이지 VM. 단일 상태(AppState.Account) + 진입 모드로 UI 분기(상태 폭증 방지, it5 §5 C2).
/// 비번 변경·계정 생성·관리자 도구를 담당(SettingsViewModel에서 이전). 역할 게이트(it2 §7) 유지.
/// </summary>
public sealed partial class AccountViewModel : ViewModelBase
{
    private readonly AppShellViewModel _shell;
    private readonly IAccountService _accounts;
    private readonly ITempUserLimitsService _tempUserLimits;
    private readonly ILogger<AccountViewModel>? _logger;

    /// <summary>현재 진입 모드. 셸이 진입 전 세팅. UI가 모드별 섹션 표시.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPasswordChange))]
    [NotifyPropertyChangedFor(nameof(IsAccountCreate))]
    [NotifyPropertyChangedFor(nameof(IsAdmin))]
    [NotifyPropertyChangedFor(nameof(Title))]
    private AccountMode _mode = AccountMode.PasswordChange;

    public bool IsPasswordChange => Mode == AccountMode.PasswordChange;
    public bool IsAccountCreate => Mode == AccountMode.AccountCreate;
    public bool IsAdmin => Mode == AccountMode.Admin;

    public string Title => Mode switch
    {
        AccountMode.PasswordChange => "비밀번호 변경",
        AccountMode.AccountCreate => "계정 생성",
        AccountMode.Admin => "관리자",
        _ => "계정"
    };

    // ── 비밀번호 변경 (PasswordBox는 바인딩 불가 → code-behind 전달) ──
    public string NewPassword { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
    [ObservableProperty] private string _accountMessage = string.Empty;
    [ObservableProperty] private bool _accountMessageIsError;

    // ── it13 §7.7: Admin 전역 TempUser 한도 수정(관리자 도구 섹션, Admin 전용) ──
    // 초기값은 서버 로드 전 placeholder(진입 시 LoadTempUserLimitsAsync가 덮어씀). 기본값은 단일 소스 참조.
    /// <summary>전역 시간 한도(h) 입력. 진입 시 서버에서 로드(백엔드 모드).</summary>
    [ObservableProperty] private int _tempUserQrHours = TempUserLimits.Default.QrHours;
    /// <summary>전역 횟수 한도 입력.</summary>
    [ObservableProperty] private int _tempUserQrCount = TempUserLimits.Default.QrCount;
    [ObservableProperty] private string _tempUserLimitsMessage = string.Empty;
    [ObservableProperty] private bool _tempUserLimitsMessageIsError;
    /// <summary>전역 한도 수정 섹션 노출 여부: Admin + 백엔드 모드에서만(레거시엔 강제 인프라 없음).</summary>
    public bool CanEditTempUserLimits => _shell.Session.CurrentUser?.Role == UserRole.Admin && IsBackendMode;

    // ── 계정 생성 (power) ──
    [ObservableProperty] private string _newAccountId = string.Empty;
    public string NewAccountPassword { get; set; } = string.Empty; // code-behind 전달
    /// <summary>신규 계정 이메일(백엔드 모드에서만 노출·전달). 생성 시 인증 코드가 발송된다. (item1a §9.3)</summary>
    [ObservableProperty] private string _newAccountEmail = string.Empty;
    [ObservableProperty] private UserRole _selectedNewRole = UserRole.User;
    [ObservableProperty] private string _adminMessage = string.Empty;
    [ObservableProperty] private bool _adminMessageIsError;

    // ── 이메일 등록/인증 섹션 (PasswordChange 모드 하단, 백엔드 모드 전용, item1a §9.3) ──
    /// <summary>이메일 등록 입력(본인 email 추가/변경).</summary>
    [ObservableProperty] private string _emailInput = string.Empty;
    /// <summary>이메일 인증 코드 입력(6자리).</summary>
    [ObservableProperty] private string _emailVerifyCode = string.Empty;
    [ObservableProperty] private string _emailMessage = string.Empty;
    [ObservableProperty] private bool _emailMessageIsError;

    // ── 인증 코드 5분 카운트다운 (C3, §3.3) ──
    /// <summary>인증 코드 유효 시간 카운트다운(mm:ss). 표시용 — 실제 만료는 서버가 판정.</summary>
    [ObservableProperty] private string _verifyCountdownText = string.Empty;
    /// <summary>카운트다운 활성 여부(true일 때만 표시·인증 버튼 활성).</summary>
    [ObservableProperty] private bool _isVerifyCountdownActive;

    /// <summary>UI 스레드 바인딩 카운트다운 타이머(1초 tick). 진입마다 재구성, 이탈 시 정지(G6 누수 방지).</summary>
    private DispatcherTimer? _verifyCountdown;
    /// <summary>카운트다운 종료 시각(로컬 시계 기준, 표시용).</summary>
    private DateTime _verifyDeadline;

    /// <summary>인증 코드 유효 시간(서버 VERIFY_TTL_SECONDS=300과 정합, §3.3).</summary>
    private static readonly TimeSpan VerifyCodeLifetime = TimeSpan.FromMinutes(5);

    /// <summary>로그인 계정의 현재 이메일(없으면 null). 진입 시 세션에서 로드.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEmail))]
    private string? _currentEmail;

    /// <summary>로그인 계정의 이메일 인증 여부. 진입 시 세션에서 로드.</summary>
    [ObservableProperty]
    private bool _isEmailVerified;

    /// <summary>이메일이 등록돼 있는지(등록/미등록 UI 분기).</summary>
    public bool HasEmail => !string.IsNullOrWhiteSpace(CurrentEmail);

    /// <summary>로그인 역할이 생성 가능한 역할 목록(admin→[User,Manager], manager→[User]).</summary>
    public ObservableCollection<UserRole> CreatableRoles { get; } = new();

    public bool IsLoggedIn => _shell.Session.CurrentUser is not null;
    public bool IsPower => _shell.Session.CurrentUser?.Role.IsPower() == true;

    /// <summary>
    /// 백엔드 모드 여부(item1a §9.3 게이트). 이메일 인증·비밀번호 재설정 인프라는 백엔드 전용이므로
    /// 계정 생성 email 필드·이메일 인증 섹션은 이 값이 true일 때만 노출·활성한다.
    /// </summary>
    public bool IsBackendMode => _shell.Settings.Current.UseBackend;

    public AccountViewModel(AppShellViewModel shell, IAccountService accounts,
        ITempUserLimitsService tempUserLimits, ILogger<AccountViewModel>? logger = null)
    {
        _shell = shell;
        _accounts = accounts;
        _tempUserLimits = tempUserLimits;
        _logger = logger;
    }

    public override Task OnEnterAsync()
    {
        // 재사용 오버레이 VM이므로 진입마다 기존 카운트다운을 먼저 정지·해제한다(G6 누수 방지).
        StopVerifyCountdown();

        OnPropertyChanged(nameof(IsLoggedIn));
        OnPropertyChanged(nameof(IsPower));
        OnPropertyChanged(nameof(IsBackendMode));
        OnPropertyChanged(nameof(CanEditTempUserLimits));

        // 생성 가능 역할 갱신(로그인 역할 기반)
        CreatableRoles.Clear();
        var user = _shell.Session.CurrentUser;
        var role = user?.Role;
        if (role is { } r)
            foreach (var cr in r.CreatableRoles())
                CreatableRoles.Add(cr);
        SelectedNewRole = CreatableRoles.Count > 0 ? CreatableRoles[0] : UserRole.User;

        // 이메일 인증 섹션 상태 로드(백엔드 모드에서 로그인 시 채워짐).
        CurrentEmail = user?.Email;
        IsEmailVerified = user?.EmailVerified == true;
        EmailInput = user?.Email ?? string.Empty;
        EmailVerifyCode = string.Empty;
        SetEmailMessage(string.Empty, isError: false);
        SetTempUserLimitsMessage(string.Empty, isError: false);

        // it13 §7.7: 관리자 도구 진입 시 현재 전역 한도 로드(Admin·백엔드 모드에서만).
        return CanEditTempUserLimits ? LoadTempUserLimitsAsync() : Task.CompletedTask;
    }

    /// <summary>현재 전역 TempUser 한도 조회(표시용). 실패해도 기본값 표시 유지(치명 아님).</summary>
    private async Task LoadTempUserLimitsAsync()
    {
        try
        {
            var limits = await _tempUserLimits.GetLimitsAsync();
            TempUserQrHours = limits.QrHours;
            TempUserQrCount = limits.QrCount;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "TempUser 전역 한도 조회 실패");
            SetTempUserLimitsMessage("현재 한도를 불러오지 못했습니다.", isError: true);
        }
    }

    /// <summary>오버레이 이탈(닫기/복귀) 시 카운트다운 정지·핸들러 해제(G6 누수 방지).</summary>
    public override Task OnLeaveAsync()
    {
        StopVerifyCountdown();
        return Task.CompletedTask;
    }

    // ── 비밀번호 변경 (it2 §4.3, 2회 확인) ──

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

    // ── 계정 생성 (it2 §4.4·§7, 역할 게이트) ──

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

        // 백엔드 모드에서만 email 수집(레거시 경로엔 이메일 인프라 없음, item1a §9.3).
        var email = IsBackendMode && !string.IsNullOrWhiteSpace(NewAccountEmail)
            ? NewAccountEmail.Trim()
            : null;

        try
        {
            var createdId = NewAccountId.Trim(); // 비우기 전에 보존(메시지 조립용)
            await _accounts.CreateAsync(createdId, NewAccountPassword, SelectedNewRole, email, actingRole);
            NewAccountId = string.Empty;
            NewAccountPassword = string.Empty;
            NewAccountEmail = string.Empty;
            var suffix = email is not null ? " 인증 코드를 이메일로 발송했습니다." : string.Empty;
            SetAdminMessage($"'{createdId}' 계정을 생성했습니다.{suffix}", isError: false);
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

    /// <summary>앱 종료(관리자).</summary>
    [RelayCommand]
    private void ExitApp() => Application.Current.Shutdown();

    /// <summary>전역 TempUser 한도 저장(Admin). 서버가 requireAdmin·범위 검증으로 이중 방어. (it13 §7.7)</summary>
    [RelayCommand]
    private async Task SaveTempUserLimits()
    {
        if (!CanEditTempUserLimits)
        {
            SetTempUserLimitsMessage("권한이 없습니다.", isError: true);
            return;
        }
        if (TempUserQrHours < 1 || TempUserQrCount < 1)
        {
            SetTempUserLimitsMessage("시간·횟수는 1 이상이어야 합니다.", isError: true);
            return;
        }

        try
        {
            await _tempUserLimits.SetLimitsAsync(new TempUserLimits(TempUserQrHours, TempUserQrCount));
            SetTempUserLimitsMessage("한도를 저장했습니다.", isError: false);
        }
        catch (UnauthorizedAccessException)
        {
            SetTempUserLimitsMessage("한도를 변경할 권한이 없습니다.", isError: true);
        }
        catch (ArgumentException ex)
        {
            // 서버 범위 검증 위반(400) 등.
            SetTempUserLimitsMessage(ex.Message, isError: true);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "TempUser 한도 저장 실패");
            SetTempUserLimitsMessage("저장에 실패했습니다.", isError: true);
        }
    }

    // ── 이메일 등록/인증 (PasswordChange 모드 하단 섹션, 백엔드 전용, item1a §9.3) ──

    /// <summary>본인 이메일 등록/변경. 서버가 emailVerified=false 리셋 + 인증 코드 발송.</summary>
    [RelayCommand]
    private async Task RegisterEmail()
    {
        var user = _shell.Session.CurrentUser;
        if (user is null) return;
        if (!IsBackendMode)
        {
            SetEmailMessage("이메일 기능은 백엔드 모드에서만 사용할 수 있습니다.", isError: true);
            return;
        }
        var email = EmailInput.Trim();
        if (string.IsNullOrWhiteSpace(email))
        {
            SetEmailMessage("이메일을 입력하세요.", isError: true);
            return;
        }

        try
        {
            await _accounts.SetEmailAsync(user.Id, email);
            // 로컬 세션 반영(등록 직후는 미인증 상태).
            user.Email = email;
            user.EmailVerified = false;
            CurrentEmail = email;
            IsEmailVerified = false;
            EmailVerifyCode = string.Empty;
            SetEmailMessage("이메일로 인증 코드를 발송했습니다. 코드를 입력해 인증을 완료하세요.", isError: false);
            StartVerifyCountdown(VerifyCodeLifetime);
        }
        catch (ArgumentException)
        {
            SetEmailMessage("이메일 형식이 올바르지 않습니다.", isError: true);
        }
        catch (InvalidOperationException ex)
        {
            // 이메일 중복(409) 등.
            SetEmailMessage(ex.Message, isError: true);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "이메일 등록 실패");
            SetEmailMessage("이메일 등록에 실패했습니다.", isError: true);
        }
    }

    /// <summary>인증 코드 확인(6자리). 성공 시 emailVerified=true 반영.</summary>
    [RelayCommand]
    private async Task VerifyEmail()
    {
        var user = _shell.Session.CurrentUser;
        if (user is null) return;
        if (!IsBackendMode)
        {
            SetEmailMessage("이메일 기능은 백엔드 모드에서만 사용할 수 있습니다.", isError: true);
            return;
        }
        var code = EmailVerifyCode.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            SetEmailMessage("인증 코드를 입력하세요.", isError: true);
            return;
        }

        try
        {
            var verified = await _accounts.ConfirmEmailVerificationAsync(user.Id, code);
            if (verified)
            {
                user.EmailVerified = true;
                IsEmailVerified = true;
                EmailVerifyCode = string.Empty;
                StopVerifyCountdown();
                SetEmailMessage("이메일 인증이 완료되었습니다.", isError: false);
            }
            else
            {
                // 서버가 false 반환 = 코드 불일치/만료. 카운트다운은 표시용이므로 서버 판정을 신뢰한다.
                SetEmailMessage("인증 코드가 올바르지 않거나 만료되었습니다.", isError: true);
            }
        }
        catch (InvalidOperationException ex)
        {
            // 이메일 1개당 1계정만 인증 초과(409 taken). 서버 메시지 그대로 노출
            // ("해당 이메일로 생성 가능한 계정 수를 초과하였습니다."). (§3.4 C4)
            SetEmailMessage(ex.Message, isError: true);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "이메일 인증 실패");
            SetEmailMessage("인증에 실패했습니다.", isError: true);
        }
    }

    /// <summary>인증 메일 재발송(현재 등록된 이메일로).</summary>
    [RelayCommand]
    private async Task ResendEmailVerification()
    {
        var user = _shell.Session.CurrentUser;
        if (user is null) return;
        if (!IsBackendMode)
        {
            SetEmailMessage("이메일 기능은 백엔드 모드에서만 사용할 수 있습니다.", isError: true);
            return;
        }

        try
        {
            // 서버는 열거 방지로 항상 성공 응답(no-op 포함). 사용자에겐 동일 안내.
            await _accounts.RequestEmailVerificationAsync(user.Id);
            SetEmailMessage("인증 코드를 다시 발송했습니다.", isError: false);
            StartVerifyCountdown(VerifyCodeLifetime);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "인증 메일 재발송 실패");
            SetEmailMessage("재발송에 실패했습니다.", isError: true);
        }
    }

    /// <summary>[닫기/뒤로]: 오버레이 복귀(직전 화면). 세션 보존.</summary>
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

    private void SetEmailMessage(string text, bool isError)
    {
        EmailMessage = text;
        EmailMessageIsError = isError;
    }

    private void SetTempUserLimitsMessage(string text, bool isError)
    {
        TempUserLimitsMessage = text;
        TempUserLimitsMessageIsError = isError;
    }

    // ── 인증 코드 카운트다운 (C3, §3.3) ──

    /// <summary>
    /// 인증 코드 5분 카운트다운을 시작한다. 매초 mm:ss 갱신, 0 도달 시 정지 + 만료 안내.
    /// 진입마다 기존 타이머를 정지·해제 후 재구성한다(G6 누수 방지). DispatcherTimer는 UI 스레드 바인딩.
    /// </summary>
    private void StartVerifyCountdown(TimeSpan lifetime)
    {
        StopVerifyCountdown(); // 재발송 등으로 재시작 시 중복 tick 방지

        _verifyDeadline = DateTime.Now + lifetime;
        _verifyCountdown = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _verifyCountdown.Tick += OnVerifyCountdownTick;

        IsVerifyCountdownActive = true;
        UpdateVerifyCountdownText(); // 즉시 5:00 표시(첫 tick까지 1초 공백 방지)
        _verifyCountdown.Start();
    }

    /// <summary>카운트다운 정지 + 핸들러 해제 + 표시 초기화(오버레이 이탈/재진입/인증 완료).</summary>
    private void StopVerifyCountdown()
    {
        if (_verifyCountdown is { } timer)
        {
            timer.Stop();
            timer.Tick -= OnVerifyCountdownTick;
            _verifyCountdown = null;
        }
        IsVerifyCountdownActive = false;
        VerifyCountdownText = string.Empty;
    }

    /// <summary>매초 tick: 남은 시간 갱신, 0 이하면 정지 + 만료 안내. (이벤트 핸들러이나 async 불필요 → 일반 void)</summary>
    private void OnVerifyCountdownTick(object? sender, EventArgs e)
    {
        if (DateTime.Now >= _verifyDeadline)
        {
            StopVerifyCountdown();
            SetEmailMessage("코드가 만료되었습니다. 재발송하세요.", isError: true);
            return;
        }
        UpdateVerifyCountdownText();
    }

    /// <summary>남은 시간을 mm:ss로 표시(음수 방지 clamp).</summary>
    private void UpdateVerifyCountdownText()
    {
        var remaining = _verifyDeadline - DateTime.Now;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        VerifyCountdownText = $"{(int)remaining.TotalMinutes:D2}:{remaining.Seconds:D2}";
    }
}
