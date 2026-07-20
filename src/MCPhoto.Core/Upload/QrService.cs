using QRCoder;

namespace MCPhoto.Core.Upload;

/// <summary>
/// URL → QR PNG. QRCoder PngByteQRCode(순수 .NET, System.Drawing 불필요). (architecture §1.2)
/// </summary>
public sealed class QrService : IQrService
{
    public byte[] GenerateQrPng(string text, int pixelsPerModule = 20)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data);
        return png.GetGraphic(pixelsPerModule);
    }
}
