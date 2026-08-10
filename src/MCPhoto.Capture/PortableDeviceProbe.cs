using System.Management;
using Microsoft.Extensions.Logging;

namespace MCPhoto.Capture;

/// <summary>
/// PnP 트리의 휴대용/이미징 장치 이름 best-effort 조회(it24 §5.1 ③).
/// <see cref="CameraNameProbe"/>와 같은 형태로 <b>WMI I/O</b>와 <b>순수 매칭</b>을 분리한다 —
/// 판정이 I/O 안에 섞이면 상태 전수표를 장비 없이 검증할 수 없다.
/// <para>
/// ⚠️ <b>양성 신호 전용</b>(it24 R3): 매칭된 이름은 "감지되었습니다"의 근거가 되지만, 매칭 0은
/// "장치 없음"의 근거가 <b>아니다</b>. Nikon 바디는 전용 드라이버 없이 표준 MTP로 붙어 제네릭
/// "MTP Portable Device"로 열거될 수 있고(WEB1), 반대로 WPD 클래스에는 폰 저장소처럼 카메라가 아닌
/// 장치도 섞인다(L1 실측). 그래서 조회 실패·미매칭은 어떤 단정도 강화하지 않는다.
/// </para>
/// <para>
/// 이 프로브는 <c>public</c>이다(<see cref="CameraNameProbe"/>는 internal): 소비자가 App의
/// 설정 화면 VM이며, 검색 커맨드가 WMI를 <c>Task.Run</c>으로 오프로드해 호출한다.
/// </para>
/// </summary>
public static class PortableDeviceProbe
{
    /// <summary>
    /// 휴대용·이미징 장치 이름을 WMI로 조회. 실패(권한·WMI 서비스 이상) 시 <b>예외 없이 빈 목록</b>.
    /// <para>
    /// <c>PNPClass='WPD'</c>는 Windows Portable Devices 설치 클래스다(WEB2 · 이 머신에서 실측 확인 L1).
    /// <c>'Camera'</c>/<c>'Image'</c>는 기존 <see cref="CameraNameProbe"/> 관례를 포괄해 클래스가
    /// 무엇으로 뜰지 모르는 상황(U1)에서 관측 확률을 높인다.
    /// </para>
    /// </summary>
    /// <param name="logger">조회 실패 경고 로깅용(선택).</param>
    /// <returns>WMI 열거 순서의 장치명 목록. 실패 시 빈 목록.</returns>
    public static IReadOnlyList<string> TryGetPortableDeviceNames(ILogger? logger = null)
    {
        try
        {
            var names = new List<string>();
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name FROM Win32_PnPEntity "
                + "WHERE PNPClass = 'WPD' OR PNPClass = 'Camera' OR PNPClass = 'Image'");
            using var results = searcher.Get();
            foreach (var mo in results)
            {
                using (mo)
                {
                    if (mo["Name"] is string n && !string.IsNullOrWhiteSpace(n))
                        names.Add(n);
                }
            }
            return names;
        }
        catch (Exception ex)
        {
            // 실패는 판정을 오염시키지 않는다(R3) — 감지·참고 라인만 빠지고 상태는 그대로다(it24 E12).
            logger?.LogWarning(ex, "휴대용 장치 열거 실패(감지 라인 없이 진행)");
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// <b>순수 함수</b>: 장치명에 키워드가 하나라도 포함된 것만 추출(대소문자 무시 Contains).
    /// <para>
    /// 왜 전체 일치가 아니라 <b>하나라도</b>인가: 관측은 양성 신호로만 쓰이므로 느슨한 쪽이 안전하다.
    /// "Nikon"만 걸린 이름이 실제로는 스캐너일 수 있으나, 그 경우에도 화면은 "감지되었습니다"까지만
    /// 말하고 제어 가능 여부는 별도 명제로 표시한다(it24 §5.3 S3). 반대로 전체 일치를 요구하면
    /// 모델명 표기가 조금만 달라도 매칭이 사라져 신호 자체가 없어진다.
    /// </para>
    /// <para>
    /// ⚠️ 근사·부분 토큰 매칭을 더 넣지 않는다 — 실기 실측(설계 Step 9) 전에 추측으로 키워드를 보강하면
    /// 무엇이 관측된 것인지 알 수 없게 된다.
    /// </para>
    /// </summary>
    /// <param name="names">조회된 장치명(빈 목록 허용).</param>
    /// <param name="keywords">모델 표시명에서 유도한 키워드(빈 목록이면 결과도 빈 목록).</param>
    public static IReadOnlyList<string> MatchCandidates(
        IReadOnlyList<string> names, IReadOnlyList<string> keywords)
    {
        if (names is null || keywords is null || names.Count == 0 || keywords.Count == 0)
            return Array.Empty<string>();

        var matched = new List<string>();
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            foreach (var keyword in keywords)
            {
                if (string.IsNullOrWhiteSpace(keyword)) continue;
                if (name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    matched.Add(name);
                    break;   // 같은 이름을 키워드 수만큼 중복 담지 않는다
                }
            }
        }
        return matched;
    }
}
