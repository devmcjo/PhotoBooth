using MCPhoto.Core.Upload;

namespace MCPhoto.Tests;

/// <summary>WBS Step 8: QR PNG 생성 검증(유효한 PNG 시그니처·비어있지 않음).</summary>
public class QrServiceTests
{
    private static readonly byte[] PngMagic = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    [Fact]
    public void Generates_Valid_Png()
    {
        var svc = new QrService();
        var png = svc.GenerateQrPng("https://mcphoto.web.app/?s=abc123", 10);

        Assert.NotNull(png);
        Assert.True(png.Length > 100, "QR PNG가 비어있지 않아야 함");
        // PNG 매직 넘버 확인
        for (int i = 0; i < PngMagic.Length; i++)
            Assert.Equal(PngMagic[i], png[i]);
    }

    [Fact]
    public void Larger_Module_Produces_Larger_Png()
    {
        var svc = new QrService();
        var small = svc.GenerateQrPng("test", 5);
        var large = svc.GenerateQrPng("test", 20);
        Assert.True(large.Length > small.Length);
    }
}
