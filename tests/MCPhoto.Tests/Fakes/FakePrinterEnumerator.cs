using MCPhoto.Core.Devices;

namespace MCPhoto.Tests.Fakes;

/// <summary>
/// <see cref="IPrinterEnumerator"/> 페이크. 열거 결과(성공 N대 / 성공 0대 / 실패)를 주입하고 호출 횟수를 관측한다.
/// (it24 §12.3)
/// <para>
/// 이 페이크의 핵심 용도는 <b>P2와 P4의 구분</b>을 검증하는 것이다: "설치된 프린터가 없습니다"와
/// "프린터 목록을 확인할 수 없습니다"는 조치가 다른 명제라, 실 스풀러로는 재현하기 어려운 두 상태를
/// 여기서 결정적으로 만들어 낸다.
/// </para>
/// </summary>
public sealed class FakePrinterEnumerator : IPrinterEnumerator
{
    /// <summary>열거 성공 여부. false면 P4("확인할 수 없습니다").</summary>
    public bool Succeeded { get; set; } = true;

    /// <summary>성공 시 돌려줄 목록(빈 목록이면 P2).</summary>
    public List<InstalledPrinter> Printers { get; } = new();

    /// <summary>EnumerateAsync가 던질 예외(계약 위반 구현을 모사 — VM이 P4로 강등하는지 검증).</summary>
    public Exception? Throws { get; set; }

    /// <summary>열거 직전에 실행되는 훅(열거 중 UI 상태 관측용).</summary>
    public Action? OnEnumerate { get; set; }

    public int EnumerateCalls { get; private set; }

    public Task<PrinterEnumerationResult> EnumerateAsync(CancellationToken ct = default)
    {
        EnumerateCalls++;
        OnEnumerate?.Invoke();
        if (Throws is not null) throw Throws;
        return Task.FromResult(Succeeded
            ? new PrinterEnumerationResult(true, Printers.ToArray())
            : PrinterEnumerationResult.Failed);
    }

    /// <summary>목록 주입 헬퍼(체이닝).</summary>
    public FakePrinterEnumerator With(string name, bool isDefault = false)
    {
        Printers.Add(new InstalledPrinter(name, isDefault));
        return this;
    }
}
