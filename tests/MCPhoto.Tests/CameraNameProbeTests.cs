using MCPhoto.Capture;
using MCPhoto.Core.Capture;

namespace MCPhoto.Tests;

/// <summary>
/// it11 #15: 카메라 FriendlyName 순수 매핑 헬퍼(<see cref="CameraNameProbe.ComposeDevices"/>) 검증.
/// WMI I/O(<c>TryGetImagingDeviceNames</c>)는 환경 의존이라 단위 테스트 대상이 아니고,
/// 인덱스↔이름 매핑·폴백·중복 접미 규칙만 순수 함수로 고정한다.
/// </summary>
public class CameraNameProbeTests
{
    [Fact]
    public void Compose_Maps_Names_In_Order_When_Sufficient()
    {
        var devices = CameraNameProbe.ComposeDevices(
            new[] { 0, 1 }, new[] { "Logitech", "Elgato" });

        Assert.Equal(2, devices.Count);
        Assert.Equal(new CameraDevice(0, "Logitech"), devices[0]);
        Assert.Equal(new CameraDevice(1, "Elgato"), devices[1]);
    }

    [Fact]
    public void Compose_Falls_Back_To_Index_Label_When_Names_Insufficient()
    {
        var devices = CameraNameProbe.ComposeDevices(
            new[] { 0, 1 }, new[] { "A" });

        Assert.Equal(2, devices.Count);
        Assert.Equal(new CameraDevice(0, "A"), devices[0]);
        Assert.Equal(new CameraDevice(1, "Camera 1"), devices[1]);
    }

    [Fact]
    public void Compose_Falls_Back_To_Index_Labels_When_Names_Empty()
    {
        var devices = CameraNameProbe.ComposeDevices(
            new[] { 0, 1, 2 }, Array.Empty<string>());

        Assert.Equal(3, devices.Count);
        Assert.Equal("Camera 0", devices[0].Name);
        Assert.Equal("Camera 1", devices[1].Name);
        Assert.Equal("Camera 2", devices[2].Name);
    }

    [Fact]
    public void Compose_Adds_Index_Suffix_For_Duplicate_Names()
    {
        var devices = CameraNameProbe.ComposeDevices(
            new[] { 0, 1 }, new[] { "Cam", "Cam" });

        Assert.Equal(2, devices.Count);
        Assert.Equal(new CameraDevice(0, "Cam (#0)"), devices[0]);
        Assert.Equal(new CameraDevice(1, "Cam (#1)"), devices[1]);
    }

    [Fact]
    public void Compose_Keeps_Unique_Name_Without_Suffix_Even_If_Fallback_Present()
    {
        // 인덱스 1은 폴백("Camera 1")이며, 이는 중복 접미 카운트 대상이 아니다.
        // 유일한 실제 이름 "A"는 접미 없이 그대로 유지되어야 한다.
        var devices = CameraNameProbe.ComposeDevices(
            new[] { 0, 1 }, new[] { "A" });

        Assert.Equal("A", devices[0].Name);
        Assert.Equal("Camera 1", devices[1].Name);
    }

    [Fact]
    public void Compose_Preserves_NonContiguous_Indices()
    {
        // 열린 인덱스가 연속이 아닐 때(예: 0, 2번만 열림) 인덱스 값이 폴백 라벨에 그대로 반영된다.
        var devices = CameraNameProbe.ComposeDevices(
            new[] { 0, 2 }, new[] { "OnlyOne" });

        Assert.Equal(new CameraDevice(0, "OnlyOne"), devices[0]);
        Assert.Equal(new CameraDevice(2, "Camera 2"), devices[1]);
    }

    [Fact]
    public void Compose_Empty_Indices_Returns_Empty()
    {
        var devices = CameraNameProbe.ComposeDevices(
            Array.Empty<int>(), new[] { "Ghost", "Ghost" });

        Assert.Empty(devices);
    }

    [Fact]
    public void Compose_Extra_Names_Beyond_Open_Indices_Are_Ignored()
    {
        // WMI가 OpenCV보다 많은 장치를 보고해도(가상 카메라 등) 열린 인덱스 수만큼만 매핑.
        var devices = CameraNameProbe.ComposeDevices(
            new[] { 0 }, new[] { "First", "Second", "Third" });

        Assert.Single(devices);
        Assert.Equal(new CameraDevice(0, "First"), devices[0]);
    }
}
