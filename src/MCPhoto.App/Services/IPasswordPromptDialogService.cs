namespace MCPhoto.App.Services;

/// <summary>
/// 비밀번호 확인 모달을 띄우는 서비스(VM이 Window를 직접 참조하지 않도록 추상화). (보완#1)
/// </summary>
public interface IPasswordPromptDialogService
{
    /// <summary>비밀번호 프롬프트(모달). 입력이 expectedPassword와 일치하면 true, 취소/불일치면 false.</summary>
    bool Prompt(string expectedPassword);
}
