namespace MCPhoto.Core.Upload;

/// <summary>URL → QR 이미지. QRCoder 기반. (architecture §1.2)</summary>
public interface IQrService
{
    /// <summary>텍스트(다운로드 페이지 URL)를 QR PNG 바이트로 인코딩.</summary>
    byte[] GenerateQrPng(string text, int pixelsPerModule = 20);
}
