using System.Management;
using MCPhoto.Core.Capture;
using Microsoft.Extensions.Logging;

namespace MCPhoto.Capture;

/// <summary>
/// 카메라 장치 FriendlyName 조회 헬퍼(it11 #15). WMI I/O(<see cref="TryGetImagingDeviceNames"/>)와
/// 순수 매핑 로직(<see cref="ComposeDevices"/>)을 분리한다.
/// <para>
/// <b>핵심 안전장치(best-effort)</b>: OpenCV 인덱스 열거 순서와 WMI 열거 순서가 일치한다는 보장은 없다(A2).
/// 따라서 FriendlyName은 <b>표시 개선용 best-effort</b>로만 쓰고, 실제 동작(장치 선택)은 여전히 OpenCV 인덱스
/// 기준으로 유지한다. 조회 실패·이름 부족 시 <c>"Camera {index}"</c>로 폴백한다.
/// </para>
/// </summary>
internal static class CameraNameProbe
{
    /// <summary>
    /// WMI(<c>Win32_PnPEntity</c>, <c>PNPClass='Camera'/'Image'</c>)로 이미징/카메라 장치의 FriendlyName을
    /// best-effort 조회한다. 실패(권한 없음·WMI 미가용 등) 시 예외를 던지지 않고 빈 목록을 반환해 폴백을 보장한다.
    /// </summary>
    /// <param name="logger">조회 실패 경고 로깅용(선택).</param>
    /// <returns>WMI 열거 순서의 장치명 목록. 실패 시 빈 목록.</returns>
    public static IReadOnlyList<string> TryGetImagingDeviceNames(ILogger? logger = null)
    {
        try
        {
            var names = new List<string>();
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name FROM Win32_PnPEntity WHERE PNPClass = 'Camera' OR PNPClass = 'Image'");
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
            logger?.LogWarning(ex, "카메라 FriendlyName 조회 실패(인덱스 라벨로 폴백)");
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// 열린 OpenCV 인덱스 목록에 WMI FriendlyName을 순서대로 매핑하는 <b>순수 함수</b>(WMI/OpenCV I/O 미접촉).
    /// <list type="bullet">
    /// <item>이름이 충분하면 순서대로 매핑.</item>
    /// <item>이름이 부족하면 부족분은 <c>"Camera {index}"</c>로 폴백.</item>
    /// <item>매핑된 실제 이름이 중복(동일 모델 2대)이면 <c>"{name} (#{index})"</c> 접미로 구분성 확보(폴백 라벨은 제외).</item>
    /// </list>
    /// </summary>
    /// <param name="openIndices">OpenCV 프로빙으로 열린 장치 인덱스(동작 기준, 순서 유지).</param>
    /// <param name="friendlyNames">WMI 조회 장치명(열거 순서).</param>
    /// <returns>인덱스↔이름 매핑된 <see cref="CameraDevice"/> 목록.</returns>
    public static IReadOnlyList<CameraDevice> ComposeDevices(
        IReadOnlyList<int> openIndices, IReadOnlyList<string> friendlyNames)
    {
        // 1) 매핑된 "실제 이름"의 중복 여부를 먼저 집계(폴백 라벨은 제외).
        //    부족분 폴백("Camera {i}")은 접미 대상이 아니므로 카운트에 넣지 않는다.
        var nameCounts = new Dictionary<string, int>();
        int mappable = Math.Min(openIndices.Count, friendlyNames.Count);
        for (int k = 0; k < mappable; k++)
        {
            var name = friendlyNames[k];
            nameCounts[name] = nameCounts.TryGetValue(name, out var c) ? c + 1 : 1;
        }

        // 2) 인덱스 순서대로 이름 매핑 + 폴백 + 중복 접미.
        var devices = new List<CameraDevice>(openIndices.Count);
        for (int k = 0; k < openIndices.Count; k++)
        {
            int index = openIndices[k];
            string label;
            if (k < friendlyNames.Count)
            {
                var name = friendlyNames[k];
                // 실제 매핑 이름이 2회 이상 등장하면 인덱스 접미로 구분.
                label = nameCounts.TryGetValue(name, out var c) && c > 1
                    ? $"{name} (#{index})"
                    : name;
            }
            else
            {
                label = $"Camera {index}";
            }
            devices.Add(new CameraDevice(index, label));
        }
        return devices;
    }
}
