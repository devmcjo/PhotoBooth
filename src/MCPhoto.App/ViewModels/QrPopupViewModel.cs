using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPhoto.App.Imaging;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Upload;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.ViewModels;

/// <summary>
/// QR 팝업. 업로드 성공 후에만 QR 노출(§10). 실패 시 오류 안내. "N시간 후 삭제" 고지. (BM⑤)
/// </summary>
public sealed partial class QrPopupViewModel : ViewModelBase
{
    private readonly AppShellViewModel _shell;
    private readonly IUploadService _upload;
    private readonly IQrService _qr;
    private readonly ILogger<QrPopupViewModel>? _logger;

    [ObservableProperty] private ImageSource? _qrImage;
    [ObservableProperty] private bool _isUploading;
    [ObservableProperty] private bool _uploadSucceeded;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _expiryNotice = string.Empty;

    public QrPopupViewModel(
        AppShellViewModel shell,
        IUploadService upload,
        IQrService qr,
        ILogger<QrPopupViewModel>? logger = null)
    {
        _shell = shell;
        _upload = upload;
        _qr = qr;
        _logger = logger;
    }

    public override async Task OnEnterAsync()
    {
        var session = _shell.Session;
        var settings = _shell.Settings.Current;

        if (session.FinalImagePath is null)
        {
            StatusMessage = "결과물이 없습니다.";
            return;
        }

        IsUploading = true;
        StatusMessage = "업로드 중...";
        try
        {
            var result = await _upload.UploadResultAsync(
                session.FinalImagePath,
                session.TimelapsePath,
                settings.RetentionHours,
                settings.HostingBaseUrl);

            session.Result = result;

            // 업로드 성공 후에만 QR 노출(§10)
            var png = _qr.GenerateQrPng(result.DownloadPageUrl, pixelsPerModule: 12);
            QrImage = StillImageConverter.FromPngBytes(png);
            ExpiryNotice = $"업로드된 사진·영상은 {settings.RetentionHours}시간 후 자동 삭제됩니다.";
            UploadSucceeded = true;
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "업로드/QR 실패");
            UploadSucceeded = false;
            StatusMessage = "전송에 실패했습니다. 네트워크 또는 Firebase 설정을 확인해 주세요.";
        }
        finally { IsUploading = false; }
    }

    /// <summary>재시도(업로드 실패 시).</summary>
    [RelayCommand]
    private async Task Retry() => await OnEnterAsync();

    /// <summary>[홈으로]/[닫기] → 완료.</summary>
    [RelayCommand]
    private async Task Done() => await _shell.NavigateAsync(AppState.Done);

    [RelayCommand]
    private void GoHome() => _shell.ReturnHome("QR 닫기");
}
