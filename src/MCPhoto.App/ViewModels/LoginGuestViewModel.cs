using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Upload;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.ViewModels;

/// <summary>
/// 로그인 전용 화면. 촬영 게스트 직행(it2 §5)으로 "게스트로 계속" 버튼은 폐지.
/// 상단 바 로그인·프레임 선택의 커스텀 유도로 진입하며, 성공 시 직전 화면으로 복귀. (it2 §3.3)
/// </summary>
public sealed partial class LoginGuestViewModel : ViewModelBase
{
    /// <summary>오프라인 시드 계정 id(미초기화 시 인메모리로만 로그인 허용 — AccountService와 동일 규약).</summary>
    private const string OfflineSeedId = "devmcjo";

    private readonly AppShellViewModel _shell;
    private readonly IAccountService _accounts;
    private readonly IFirebaseClient _firebase;
    private readonly ILogger<LoginGuestViewModel>? _logger;

    [ObservableProperty] private string _loginId = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;

    /// <summary>
    /// 서버 미연결(서비스 계정 키 없음 → Firebase 미초기화) 여부. 키는 시작 시 결정되고 런타임에 변하지 않으므로
    /// 진입 시 1회 평가로 충분. 배너 표시·로그인 메시지 분기에 사용. (it10 S2-1)
    /// </summary>
    public bool IsServerOffline => !_firebase.IsInitialized;

    public LoginGuestViewModel(AppShellViewModel shell, IAccountService accounts, IFirebaseClient firebase,
        ILogger<LoginGuestViewModel>? logger = null)
    {
        _shell = shell;
        _accounts = accounts;
        _firebase = firebase;
        _logger = logger;
    }

    /// <summary>id/pw 로그인. 성공 시 계정 반영 후 직전 화면 복귀(오버레이).</summary>
    [RelayCommand]
    private async Task Login()
    {
        if (IsBusy) return;
        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            var user = await _accounts.LoginAsync(LoginId.Trim(), Password);
            if (user is null)
            {
                // it10 S2-1: 서버 미연결 상태에서 비시드 계정 로그인 실패는 "아이디/비밀번호 불일치"로 오도하지 않고
                // 실제 원인(오프라인)을 노출. 시드(devmcjo) 오입력은 기존 메시지 유지(계정 자체는 유효하므로).
                ErrorMessage = IsServerOffline && LoginId.Trim() != OfflineSeedId
                    ? "서버 미연결 상태에서는 이 계정으로 로그인할 수 없습니다."
                    : "아이디 또는 비밀번호가 올바르지 않습니다.";
                return;
            }
            _shell.Session.Login(user); // 단일 소스 로그인 + CurrentUserChanged 통지(상단 바 자동 갱신)
            // 상단 바 진입 시 원 화면, 프레임 선택 유도 시 FrameSelect 재진입(커스텀 프레임 로드)으로 복귀
            await _shell.ReturnFromOverlay();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "로그인 실패(네트워크?)");
            ErrorMessage = "로그인할 수 없습니다. 네트워크를 확인해 주세요.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task Cancel() => await _shell.ReturnFromOverlay();
}
