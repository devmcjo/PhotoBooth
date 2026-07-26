using System;
using System.Threading.Tasks;

namespace MCPhoto.App.Services;

/// <summary>
/// 비밀번호 확인 모달을 띄우는 서비스(VM이 Window를 직접 참조하지 않도록 추상화). (보완#1)
/// </summary>
public interface IPasswordPromptDialogService
{
    /// <summary>
    /// 비밀번호 프롬프트(모달). 입력값을 <paramref name="verifyAsync"/>로 검증한다.
    /// 검증 성공 후 사용자가 확인하면 true, 취소하면 false를 반환한다.
    /// 검증 자체는 다이얼로그가 서버/서비스에 위임한다(클라 평문 비교 폐지).
    /// 검증 실패(false)·검증 오류(예외)는 창을 닫지 않고 인라인 오류로 안내하며, 게이트는 열리지 않는다(fail-closed).
    /// </summary>
    bool Prompt(Func<string, Task<bool>> verifyAsync);
}
