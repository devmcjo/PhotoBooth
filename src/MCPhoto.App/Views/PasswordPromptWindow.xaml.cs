using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace MCPhoto.App.Views;

/// <summary>
/// 설정 진입 전 비밀번호 확인 모달. 입력값을 서버/서비스로 검증(클라 평문 비교 폐지)한다.
/// 검증 성공 후 확인 시 DialogResult=true. 실패·오류는 창을 닫지 않고 인라인 오류로 안내한다(fail-closed). (보완#1)
/// </summary>
public partial class PasswordPromptWindow : Window
{
    private readonly Func<string, Task<bool>> _verify;
    private bool _busy;

    public PasswordPromptWindow(Func<string, Task<bool>> verify)
    {
        InitializeComponent();
        _verify = verify ?? throw new ArgumentNullException(nameof(verify));
        Loaded += (_, _) => Pw.Focus();
    }

    // WPF 이벤트 핸들러(유일한 async void). 내부 예외를 반드시 삼키지 않고 인라인 오류로 처리한다.
    private async void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (_busy) return; // 중복 제출 방지(Enter 연타·버튼 재클릭)
        _busy = true;
        SetBusy(true);
        try
        {
            bool ok = await _verify(Pw.Password);
            if (ok)
            {
                DialogResult = true; // 창 닫힘(검증 성공 & 확인)
                return;
            }

            ShowError("비밀번호가 일치하지 않습니다.");
        }
        catch (Exception)
        {
            // 네트워크/서버 오류 → fail-closed. 게이트를 열지 않는다(DialogResult 미설정).
            ShowError("확인할 수 없습니다. 네트워크를 확인하세요.");
        }
        finally
        {
            // DialogResult=true로 이미 닫히는 경우가 아니면 재활성화하고 포커스를 돌려준다.
            if (DialogResult != true)
            {
                SetBusy(false);
                _busy = false;
                Pw.SelectAll();
                Pw.Focus();
            }
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        if (_busy) return; // 검증 중에는 취소도 막아 상태 혼선 방지
        DialogResult = false;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnConfirm(sender, e);
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void SetBusy(bool busy)
    {
        ConfirmButton.IsEnabled = !busy;
        CancelButton.IsEnabled = !busy;
        Pw.IsEnabled = !busy;
        BusyText.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (busy)
        {
            // 새 검증 시작 시 이전 오류 메시지를 감춘다.
            ErrorText.Visibility = Visibility.Collapsed;
        }
    }
}
