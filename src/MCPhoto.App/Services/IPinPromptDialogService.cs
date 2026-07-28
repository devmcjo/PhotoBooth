using System;
using System.Threading.Tasks;

namespace MCPhoto.App.Services;

/// <summary>
/// 설정·계정 관리 진입 PIN 게이트 모달을 띄우는 서비스(it14 §5.4). VM이 Window를 직접 참조하지 않도록 추상화.
/// it15: 비밀번호 게이트가 폐지되어 이것이 유일한 진입 게이트 다이얼로그다.
/// 입력 형식(4자리 숫자)과 "설정 vs 확인" 2모드를 갖고, 확인 불가 시 게이트를 열지 않는다(fail-closed).
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
