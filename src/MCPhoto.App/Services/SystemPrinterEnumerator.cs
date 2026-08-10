using System;
using System.Collections.Generic;
using System.Printing;
using System.Threading;
using System.Threading.Tasks;
using MCPhoto.Core.Devices;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.Services;

/// <summary>
/// <see cref="IPrinterEnumerator"/>의 Windows 구현(<c>System.Printing</c> — WPF 동반 어셈블리, 추가 패키지 없음).
/// (it24 §7.2·§7.3)
/// <para>
/// 왜 WMI(<c>Win32_Printer</c>)가 아닌가: 미래의 실제 인쇄 구현이 쓸 스택과 같은 API로 열거하면
/// "열거와 인쇄가 같은 진실을 본다" — 기본 프린터 식별도 <see cref="LocalPrintServer.DefaultPrintQueue"/>로
/// 1급 지원된다. 열거만 WMI로 하면 나중에 두 진실이 갈린다.
/// </para>
/// <para>
/// ⚠️ <b>관리 권한을 요청하지 않는다</b>: 기본 생성자를 쓰며 <c>AdministrateServer</c> 액세스를 요구하지 않는다.
/// 로컬 큐 열람은 표준 사용자 권한으로 충분하고, 관리 액세스를 요구하면 키오스크 계정에서 열거가 통째로 실패한다.
/// </para>
/// <para>
/// ⚠️ <c>System.Printing</c> 타입은 <b>이 파일 밖으로 나가지 않는다</b> — 호출측에는 POCO 스냅샷만 넘긴다.
/// <c>PrintQueue</c>·<c>LocalPrintServer</c>는 네이티브 스풀러 핸들을 잡고 있어, VM이 붙들면 설정 화면이
/// 살아 있는 동안 스풀러 자원이 잠긴다.
/// </para>
/// </summary>
public sealed class SystemPrinterEnumerator : IPrinterEnumerator
{
    private readonly ILogger<SystemPrinterEnumerator>? _logger;

    public SystemPrinterEnumerator(ILogger<SystemPrinterEnumerator>? logger = null) => _logger = logger;

    /// <summary>
    /// 설치 프린터 열거. 스풀러 왕복은 네트워크 프린터가 많은 환경에서 수 초까지 갈 수 있으므로
    /// <c>Task.Run</c>으로 UI 스레드에서 떼어 낸다(웹캠 열거와 동형).
    /// </summary>
    public Task<PrinterEnumerationResult> EnumerateAsync(CancellationToken ct = default)
        => Task.Run(() => Enumerate(ct), ct);

    private PrinterEnumerationResult Enumerate(CancellationToken ct)
    {
        try
        {
            // ⚠️ 열거 후 즉시 해제하고 POCO로 복사한다 — using 스코프를 벗어난 뒤 큐 객체를 만지지 않는다.
            using var server = new LocalPrintServer();
            var defaultName = TryGetDefaultPrinterName(server);

            var printers = new List<InstalledPrinter>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Local = 이 PC에 설치된 큐, Connections = 다른 PC에 연결된 공유 프린터. 둘 다 "설치됨" 명제에 포함된다.
            using var queues = server.GetPrintQueues(new[]
            {
                EnumeratedPrintQueueTypes.Local,
                EnumeratedPrintQueueTypes.Connections
            });

            foreach (var queue in queues)
            {
                using (queue)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        // FullName은 로컬 큐면 큐 이름, 연결 프린터면 \\서버\이름 — 인쇄 API가 받는 식별자와 같다.
                        var name = queue.FullName;
                        if (string.IsNullOrWhiteSpace(name)) name = queue.Name;
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        if (!seen.Add(name)) continue;   // Local·Connections 중복 방어(콤보에 같은 줄이 두 번 뜨는 것 방지)

                        printers.Add(new InstalledPrinter(
                            name,
                            defaultName is not null && string.Equals(name, defaultName, StringComparison.OrdinalIgnoreCase)));
                    }
                    catch (Exception ex)
                    {
                        // 큐 단위 try-catch: 고아 큐 하나의 속성 접근 실패가 전체 열거를 죽이지 않게 한다.
                        _logger?.LogWarning(ex, "프린터 큐 속성 읽기 실패(해당 항목 건너뜀)");
                    }
                }
            }

            _logger?.LogInformation("설치 프린터 열거 완료: {Count}대", printers.Count);
            return new PrinterEnumerationResult(true, printers);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // 스풀러 중지·권한 등 어떤 예외여도 P4("확인할 수 없습니다")로 강등한다.
            // ⚠️ 빈 목록(P2 "설치된 프린터가 없습니다")으로 뭉개지 않는다 — 조치가 다르다(it24 R4·E17).
            _logger?.LogWarning(ex, "프린터 열거 실패(인쇄 스풀러 상태 확인)");
            return PrinterEnumerationResult.Failed;
        }
    }

    /// <summary>
    /// 기본 프린터 이름. 기본 프린터가 설정되지 않은 머신에서는 조회가 실패할 수 있어 null을 돌려준다
    /// (그 경우 "(기본)" 접미만 빠지고 목록은 정상이다 — it24 U7).
    /// </summary>
    private string? TryGetDefaultPrinterName(LocalPrintServer server)
    {
        try
        {
            using var queue = server.DefaultPrintQueue;
            var name = queue?.FullName;
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch (Exception ex)
        {
            _logger?.LogInformation(ex, "기본 프린터 조회 실패(\"(기본)\" 표시 생략)");
            return null;
        }
    }
}
