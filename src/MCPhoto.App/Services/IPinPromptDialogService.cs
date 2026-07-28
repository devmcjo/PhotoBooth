using System;
using System.Threading.Tasks;

namespace MCPhoto.App.Services;

/// <summary>
/// 설정 진입 PIN 게이트 모달을 띄우는 서비스(SSO 계정 전용, it14 §5.4). VM이 Window를 직접 참조하지 않도록 추상화.
/// 기존 <see cref="IPasswordPromptDialogService"/>(비번 게이트)와 분리 — PIN은 입력 형식(4자리 숫자)·
/// "설정 vs 확인" 2모드가 달라 응집도를 위해 별도 서비스로 둔다. fail-closed 패턴은 동일 계승.
/// </summary>
public interface IPinPromptDialogService
{
    /// <summary>
    /// PIN 확인 게이트(HasPin=true). 입력값을 <paramref name="verifyAsync"/>로 대조한다.
    /// 검증 성공 후 사용자가 확인하면 true, 취소하면 false.
    /// 검증 실패(false)·검증 오류(예외)는 창을 닫지 않고 인라인 오류로 안내하며, 게이트는 열리지 않는다(fail-closed).
    /// </summary>
    bool PromptVerify(Func<string, Task<bool>> verifyAsync);

    /// <summary>
    /// PIN 최초 설정(SSO 첫 진입, HasPin=false). 새 PIN을 2회 입력받아 일치 확인 후 <paramref name="setAsync"/>(newPin) 호출.
    /// 설정 성공 시 true, 취소하면 false. 오류(예외)는 창을 닫지 않고 인라인 오류(fail-closed).
    /// </summary>
    bool PromptSetup(Func<string, Task> setAsync);
}
