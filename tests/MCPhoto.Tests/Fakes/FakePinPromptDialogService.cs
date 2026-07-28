using MCPhoto.App.Services;

namespace MCPhoto.Tests.Fakes;

/// <summary>
/// PIN 게이트 다이얼로그 스텁. 실제 창을 띄우지 않고 호출 횟수·전달 PIN을 기록하며,
/// <see cref="Result"/>로 "사용자가 확인/취소했다"를 흉내낸다.
/// (실제 Window 인스턴스화는 headless 테스트에서 Application 싱글턴 충돌을 일으키므로 금지)
///
/// ⚠️ 콜백을 동기 대기하는 것은 <see cref="IPinPromptDialogService"/>가 ShowDialog 기반의 동기 계약이기 때문이다.
/// 테스트에는 UI SynchronizationContext가 없고 콜백은 완료된 Task를 돌려주는 fake라 데드락 위험이 없다.
/// </summary>
public sealed class FakePinPromptDialogService : IPinPromptDialogService
{
    /// <summary>다이얼로그 결과(true=확인/설정 완료, false=취소).</summary>
    public bool Result { get; set; } = true;

    /// <summary>Result=true일 때 콜백에 전달할 PIN.</summary>
    public string PinToSubmit { get; set; } = "1234";

    public int VerifyCount { get; private set; }
    public int SetupCount { get; private set; }

    public bool PromptVerify(Func<string, Task<bool>> verifyAsync)
    {
        VerifyCount++;
        if (!Result) return false;
        // 실제 다이얼로그와 동일하게 검증 콜백을 태운 뒤 그 결과를 게이트 통과 여부로 쓴다.
        return verifyAsync(PinToSubmit).GetAwaiter().GetResult();
    }

    public bool PromptSetup(Func<string, Task> setAsync)
    {
        SetupCount++;
        if (!Result) return false;
        setAsync(PinToSubmit).GetAwaiter().GetResult();
        return true;
    }
}
