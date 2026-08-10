using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Backend;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.ViewModels;

/// <summary>계정 페이지 진입 모드. 팝오버 항목이 지정. (it5 §5 C2, it15 §6.3)</summary>
public enum AccountMode
{
    /// <summary>계정 관리(본인 정보 + PIN 변경).</summary>
    Account,

    /// <summary>관리자 도구(사용자 관리 진입·전역 한도·앱 종료, power).</summary>
    Admin
}

/// <summary>
/// 계정 전용 페이지 VM. 단일 상태(AppState.Account) + 진입 모드로 UI 분기(상태 폭증 방지, it5 §5 C2).
/// it15 §6.3: 비밀번호 변경·계정 생성·이메일 인증 섹션이 폐지되고 "계정 관리"(읽기 전용 정보 + PIN 변경)로 축소.
/// 진입 시 PIN 미설정이면 최초 설정을 강제한다(설정 진입과 동일 PIN·동일 다이얼로그).
/// </summary>
public sealed partial class AccountViewModel : ViewModelBase
{
    private readonly AppShellViewModel _shell;
    private readonly IAccountService _accounts;
    private readonly ITempUserLimitsService _tempUserLimits;
    private readonly ILogger<AccountViewModel>? _logger;

    /// <summary>현재 진입 모드. 셸이 진입 전 세팅. UI가 모드별 섹션 표시.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAccount))]
    [NotifyPropertyChangedFor(nameof(IsAdmin))]
    [NotifyPropertyChangedFor(nameof(Title))]
    private AccountMode _mode = AccountMode.Account;

    public bool IsAccount => Mode == AccountMode.Account;
    public bool IsAdmin => Mode == AccountMode.Admin;

    public string Title => Mode switch
    {
        AccountMode.Account => "계정 관리",
        AccountMode.Admin => "관리자",
        _ => "계정"
    };

    // ── ① 내 계정 정보(읽기 전용, it15 §6.3) ──
    /// <summary>로그인 계정 아이디. 미로그인 시 빈 문자열.</summary>
    public string AccountId => _shell.Session.CurrentUser?.Id ?? string.Empty;

    /// <summary>Google 계정 이메일. 없으면 안내 문구.</summary>
    public string AccountEmail => _shell.Session.CurrentUser?.Email is { Length: > 0 } e ? e : "(없음)";

    /// <summary>로그인 방식 표기("Google SSO" / "알 수 없음"). 서버 authMethod 파생. (D2)</summary>
    public string AuthMethodLabel => (_shell.Session.CurrentUser?.AuthMethod ?? AuthMethod.Unknown).ToLabel();

    /// <summary>역할 한글 라벨(임시 유저/사용자/매니저/관리자).</summary>
    public string RoleLabel => _shell.Session.CurrentUser?.Role.ToLabel() ?? string.Empty;

    /// <summary>가입일(로컬 시각, 날짜만).</summary>
    public string CreatedAtText =>
        _shell.Session.CurrentUser is { } u ? u.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd") : string.Empty;

    // ── ② PIN 변경 (PasswordBox 마스킹 → code-behind 전달) ──
    public string CurrentPin { get; set; } = string.Empty;
    public string NewPin { get; set; } = string.Empty;
    public string ConfirmPin { get; set; } = string.Empty;
    [ObservableProperty] private string _pinMessage = string.Empty;
    [ObservableProperty] private bool _pinMessageIsError;

    /// <summary>이미 PIN이 설정돼 있는지(true면 현재 PIN 입력란 노출, false면 최초 설정).</summary>
    public bool HasPin => _shell.Session.CurrentUser?.HasPin == true;

    // ── it13 §7.7: Admin 전역 TempUser 한도 수정(관리자 도구 섹션, Admin 전용) ──
    // 초기값은 서버 로드 전 placeholder(진입 시 LoadTempUserLimitsAsync가 덮어씀). 기본값은 단일 소스 참조.
    /// <summary>전역 시간 한도(h) 입력. 진입 시 서버에서 로드.</summary>
    [ObservableProperty] private int _tempUserQrHours = TempUserLimits.Default.QrHours;
    /// <summary>전역 횟수 한도 입력.</summary>
    [ObservableProperty] private int _tempUserQrCount = TempUserLimits.Default.QrCount;
    [ObservableProperty] private string _tempUserLimitsMessage = string.Empty;
    [ObservableProperty] private bool _tempUserLimitsMessageIsError;
    /// <summary>전역 한도 수정 섹션 노출 여부: Admin 전용(it15: 백엔드 모드 조건 삭제 — 항상 백엔드).</summary>
    public bool CanEditTempUserLimits => _shell.Session.CurrentUser?.Role == UserRole.Admin;

    public bool IsLoggedIn => _shell.Session.CurrentUser is not null;
    public bool IsPower => _shell.Session.CurrentUser?.Role.IsPower() == true;

    public AccountViewModel(AppShellViewModel shell, IAccountService accounts,
        ITempUserLimitsService tempUserLimits, ILogger<AccountViewModel>? logger = null)
    {
        _shell = shell;
        _accounts = accounts;
        _tempUserLimits = tempUserLimits;
        _logger = logger;
    }

    public override async Task OnEnterAsync()
    {
        var user = _shell.Session.CurrentUser;

        // it15 §6.3: 계정 관리 진입 게이트 — PIN 미설정이면 최초 설정을 강제한다.
        // 설정 진입과 "동일 PIN·동일 다이얼로그"(AppShellViewModel.EnsurePinGateAsync 재사용).
        // 취소(false) 시 이 화면에 머물지 않고 직전 화면으로 되돌린다(빈 화면 노출 방지).
        // ⚠️ 되돌린 뒤에는 반드시 즉시 return — 뒤에 코드를 이어 붙이면 이중 네비게이션이 된다.
        if (user is not null && !user.HasPin)
        {
            if (!await _shell.EnsurePinGateAsync(user))
            {
                await _shell.ReturnFromOverlay();
                return;
            }
        }

        OnPropertyChanged(nameof(IsLoggedIn));
        OnPropertyChanged(nameof(IsPower));
        OnPropertyChanged(nameof(CanEditTempUserLimits));
        // 내 계정 정보·PIN 상태는 현재 로그인 계정에 의존 → 진입 시 갱신.
        OnPropertyChanged(nameof(AccountId));
        OnPropertyChanged(nameof(AccountEmail));
        OnPropertyChanged(nameof(AuthMethodLabel));
        OnPropertyChanged(nameof(RoleLabel));
        OnPropertyChanged(nameof(CreatedAtText));
        OnPropertyChanged(nameof(HasPin));
        SetPinMessage(string.Empty, isError: false);
        SetTempUserLimitsMessage(string.Empty, isError: false);

        // it13 §7.7: 관리자 도구 진입 시 현재 전역 한도 로드(Admin에서만).
        if (CanEditTempUserLimits)
            await LoadTempUserLimitsAsync();
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
        catch (Exception ex) when (ex is BackendNotConfiguredException
                                      or BackendUnavailableException
                                      or BackendLoginRequiredException)
        {
            // 저장 경로와 같은 이유로 원인을 밝힌다 — "불러오지 못했습니다"만으로는
            // 서버가 죽었는지 내 네트워크가 끊겼는지 로그인이 풀렸는지 알 수 없다.
            _logger?.LogWarning(ex, "TempUser 전역 한도 조회 실패(서버 도달·인증)");
            SetTempUserLimitsMessage($"현재 한도를 불러오지 못했습니다. {BackendFailureMessage.Describe(ex)}", isError: true);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "TempUser 전역 한도 조회 실패");
            SetTempUserLimitsMessage("현재 한도를 불러오지 못했습니다.", isError: true);
        }
    }

    // ── ② 본인 PIN 설정/변경 (it14 §6.1, it15에서 모든 계정이 대상) ──

    /// <summary>
    /// 본인 진입 PIN 설정/변경. HasPin=true면 현재 PIN 확인 후 새 PIN(2회 일치), HasPin=false면 최초 설정.
    /// 형식(4자리 숫자)·일치는 클라 1차 검증, 서버가 최종 강제(현재 PIN 불일치는 예외 → 안내).
    /// </summary>
    [RelayCommand]
    private async Task ChangePin()
    {
        var user = _shell.Session.CurrentUser;
        if (user is null) return;

        var newPin = NewPin.Trim();
        if (!IsValidPin(newPin))
        {
            SetPinMessage("새 PIN은 4자리 숫자여야 합니다.", isError: true);
            return;
        }
        if (newPin != ConfirmPin.Trim())
        {
            SetPinMessage("새 PIN이 일치하지 않습니다.", isError: true);
            return;
        }
        // 기존 PIN이 있으면 현재 PIN 확인 필수(최초 설정이면 생략). null이면 서버가 최초 설정으로 처리.
        var currentPin = HasPin ? CurrentPin.Trim() : null;
        if (HasPin && !IsValidPin(currentPin!))
        {
            SetPinMessage("현재 PIN은 4자리 숫자여야 합니다.", isError: true);
            return;
        }

        try
        {
            await _accounts.SetOwnPinAsync(user.Id, currentPin, newPin);
            user.HasPin = true;                 // 로컬 세션 반영(최초 설정→변경 경로로 전환).
            CurrentPin = NewPin = ConfirmPin = string.Empty;
            OnPropertyChanged(nameof(HasPin));  // 현재 PIN 입력란 노출 갱신(최초 설정 후 변경 모드로).
            SetPinMessage("PIN이 설정되었습니다.", isError: false);
        }
        catch (Exception ex) when (ex is BackendNotConfiguredException or BackendUnavailableException)
        {
            // 서버에 닿지 못한 경우. 이 catch가 없으면 아래 InvalidOperationException 절이 잡아
            // "현재 PIN이 올바르지 않다"는 **사실과 다른 안내**를 한다(오프라인인데 PIN을 의심하게 된다).
            _logger?.LogWarning(ex, "PIN 설정/변경 실패(서버 도달 불가)");
            SetPinMessage(BackendFailureMessage.Describe(ex), isError: true);
        }
        catch (BackendLoginRequiredException ex)
        {
            // ⚠️ 순서가 규격이다: 이 예외는 UnauthorizedAccessException 파생이라 아래 절 **뒤에 두면 절대
            //    도달하지 않는다**(it23 §B7.2 ④).
            // 이 라우트에서 이 예외는 **클라이언트 측 무토큰 가드에서만** 발생한다 — 서버 401은 PIN 불일치와
            // 만료를 구분할 수 없어 의도적으로 일반 UnauthorizedAccessException으로 올린다
            // (HttpAccountService.SetOwnPinAsync). 즉 원인이 모호하지 않으므로 "현재 PIN이 올바르지 않습니다"라는
            // **사실과 다른 안내**를 하지 않는다(사용자는 아무것도 틀리지 않았다).
            _logger?.LogWarning(ex, "PIN 설정/변경 실패(로그인 필요)");
            SetPinMessage(BackendFailureMessage.Describe(ex), isError: true);
        }
        catch (UnauthorizedAccessException)
        {
            // 이 라우트의 401은 현재 PIN 불일치이거나 토큰 만료다 — 서버가 둘 다 code="unauthorized"로 주므로
            // 구분할 수 없다(HttpAccountService.SetOwnPinAsync 주석). 한쪽으로 단정하지 않는 문구를 쓴다.
            SetPinMessage("현재 PIN이 올바르지 않습니다. 계속 실패하면 로그인이 만료된 것일 수 있으니 다시 로그인해 주세요.", isError: true);
        }
        catch (ArgumentException)
        {
            SetPinMessage("PIN 형식이 올바르지 않습니다.", isError: true);
        }
        catch (InvalidOperationException ex)
        {
            // 404(계정 없음)·서버 5xx 등.
            _logger?.LogWarning(ex, "PIN 설정/변경 실패(서버 거부)");
            SetPinMessage("PIN을 변경할 수 없습니다. 잠시 후 다시 시도해 주세요.", isError: true);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "PIN 설정/변경 실패");
            SetPinMessage("변경에 실패했습니다.", isError: true);
        }
    }

    /// <summary>PIN 형식(4자리 숫자) 검증. 서버 validatePin과 동형(클라 1차 게이트).</summary>
    private static bool IsValidPin(string value) =>
        value.Length == 4 && value.All(char.IsDigit);

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
        catch (Exception ex) when (ex is BackendNotConfiguredException
                                      or BackendUnavailableException
                                      or BackendLoginRequiredException)
        {
            // 오프라인·미설정·로그인 만료를 "권한 없음"이나 "저장 실패"로 뭉뜽그리지 않는다 —
            // 조치 방법이 서로 다르다(네트워크 확인 / 설정 입력 / 재로그인).
            _logger?.LogWarning(ex, "TempUser 한도 저장 실패(서버 도달·인증)");
            SetTempUserLimitsMessage(BackendFailureMessage.Describe(ex), isError: true);
        }
        catch (UnauthorizedAccessException)
        {
            SetTempUserLimitsMessage("한도를 변경할 권한이 없습니다.", isError: true);
        }
        catch (ArgumentException ex)
        {
            // 서버 범위 검증 위반(400) 등 — 서버가 한국어 사용자 문구를 준다.
            SetTempUserLimitsMessage(ex.Message, isError: true);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "TempUser 한도 저장 실패");
            SetTempUserLimitsMessage("저장에 실패했습니다.", isError: true);
        }
    }

    /// <summary>[닫기/뒤로]: 오버레이 복귀(직전 화면). 세션 보존.</summary>
    [RelayCommand]
    private async Task Close() => await _shell.ReturnFromOverlay();

    private void SetPinMessage(string text, bool isError)
    {
        PinMessage = text;
        PinMessageIsError = isError;
    }

    private void SetTempUserLimitsMessage(string text, bool isError)
    {
        TempUserLimitsMessage = text;
        TempUserLimitsMessageIsError = isError;
    }
}
