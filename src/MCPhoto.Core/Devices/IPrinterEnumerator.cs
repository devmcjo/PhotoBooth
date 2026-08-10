namespace MCPhoto.Core.Devices;

/// <summary>
/// Windows에 설치된 프린터 1행 스냅샷. (it24 §7.3)
/// <para>
/// ⚠️ 이 레코드는 <b>스풀러 DB 기준</b>이다 — 장치의 전원·연결 상태가 아니다. 그래서 오프라인 여부를
/// 담지 않는다: 스풀러의 오프라인 플래그는 갱신 없이 stale해지기로 악명 높고, 틀린 "오프라인" 딱지는
/// 화면이 하는 거짓말이 된다(it24 §7.2 · R1). "설치됨"이라는 명제까지만 운반한다.
/// </para>
/// </summary>
/// <param name="Name">Windows 프린터명(시스템 내 유일 식별자 — 저장 키).</param>
/// <param name="IsDefault">기본 프린터인지. 기본 프린터가 없는 머신에서는 전 행이 false다.</param>
public sealed record InstalledPrinter(string Name, bool IsDefault);

/// <summary>
/// 프린터 열거 결과. (it24 §7.3)
/// <para>
/// ⚠️ <see cref="Succeeded"/>=false("확인 불가", P4)와 <b>빈 목록</b>("설치된 프린터가 없다", P2)은
/// <b>다른 명제</b>다(it24 R4). 스풀러 서비스가 멈춰 열거가 실패한 상황을 "프린터가 없습니다"로 표시하면
/// 운영자가 프린터를 다시 꽂아 보게 되지만, 실제 조치는 서비스 시작이다. 이 구분을 타입으로 강제한다.
/// </para>
/// </summary>
/// <param name="Succeeded">열거 자체가 성공했는지.</param>
/// <param name="Printers">설치 프린터 목록(실패 시 빈 목록).</param>
public sealed record PrinterEnumerationResult(bool Succeeded, IReadOnlyList<InstalledPrinter> Printers)
{
    /// <summary>열거 실패(P4) 결과. 호출측이 매번 빈 목록을 만들지 않도록 제공한다.</summary>
    public static PrinterEnumerationResult Failed { get; } = new(false, Array.Empty<InstalledPrinter>());
}

/// <summary>
/// 설치 프린터 열거 계약. (it24 §7.3)
/// <para>
/// <c>IPhotoPrinter</c>(인쇄)와 <b>별개의 계약</b>인 이유: 그쪽은 "출력"을, 이쪽은 "목록"을 다룬다.
/// 이번 이터레이션은 열거·선택·저장까지이고 실제 인쇄는 명시적 비목표이므로, 두 관심사를 한 인터페이스에
/// 묶으면 "선택했으니 인쇄되겠지"라는 오해가 계약 수준으로 굳는다.
/// </para>
/// 관례: <b>실패는 예외가 아니라 결과값</b>이다(<c>ICameraService</c>·<c>IExternalCamera</c>와 동일) —
/// 키오스크에서 설정 화면 진입이 예외로 죽는 것보다 "확인할 수 없습니다" 표시가 낫다.
/// </summary>
public interface IPrinterEnumerator
{
    /// <summary>
    /// 설치 프린터 열거. <b>예외를 던지지 않는다</b> — 실패는 <see cref="PrinterEnumerationResult.Succeeded"/>=false.
    /// </summary>
    Task<PrinterEnumerationResult> EnumerateAsync(CancellationToken ct = default);
}
