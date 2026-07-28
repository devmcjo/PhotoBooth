using System.Windows.Controls;

namespace MCPhoto.App.Views;

/// <summary>
/// 로그인 화면(Google SSO 단독). it15 §6.1: PasswordBox·탭·포커스 이동 로직이 전부 사라져
/// 코드비하인드는 InitializeComponent만 남는다(뷰 로직 0 — 이벤트 구독 없음 = 해제 경로 불요).
/// </summary>
public partial class LoginGuestView : UserControl
{
    public LoginGuestView()
    {
        InitializeComponent();
    }
}
