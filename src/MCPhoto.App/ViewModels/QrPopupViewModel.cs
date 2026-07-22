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
    [ObservableProperty] private bool _uploadFailed; // 업로드 실패 상태(비차단 완료·재시도, it5 §2 B6)
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

        // QR off면 애초에 여기(Qr 상태)로 오지 않는다(ResultViewModel.Next의 EnableQrDelivery 분기, 정상).
        // 미디어 선택(it7 F2): 사진 옵션 on일 때만 최종 이미지 경로 전달, 타임랩스도 옵션 기준.
        var photoPath = settings.SendPhoto ? session.FinalImagePath : null;
        var timelapsePath = settings.SendTimelapse ? session.TimelapsePath : null;
        if (photoPath is null && timelapsePath is null)
        {
            // 연동 규칙상 둘 다 off면 QR 자체 off라 진입하지 않지만, 결과물 자체가 없을 때 방어.
            StatusMessage = "전송할 결과물이 없습니다.";
            return;
        }

        IsUploading = true;
        UploadFailed = false;
        UploadSucceeded = false;
        StatusMessage = "업로드 중...";
        try
        {
            var result = await _upload.UploadResultAsync(
                photoPath,
                timelapsePath,
                settings.RetentionHours,
                settings.HostingBaseUrl);

            session.Result = result;

            // 업로드 성공 후에만 QR 노출(§10)
            var png = _qr.GenerateQrPng(result.DownloadPageUrl, pixelsPerModule: 12);
            QrImage = StillImageConverter.FromPngBytes(png);
            ExpiryNotice = $"업로드된 사진·영상은 {settings.RetentionHours}시간 후 자동 삭제됩니다.";
            UploadSucceeded = true;
            UploadFailed = false;
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            // 우아 처리(it5 §2 B6 정정): QR on인데 Storage 버킷 부재 등으로 업로드가 실제 실패.
            // 흐름을 막지 않는 비위협 안내 — 결과물은 로컬 보존(QR 분기 이전 저장, 손실 0). [완료]/[재시도] 제공.
            _logger?.LogWarning(ex, "업로드/QR 실패 — 로컬 보존, 완료 진행 가능");
            UploadSucceeded = false;
            UploadFailed = true;
            QrImage = null;
            StatusMessage = settings.SaveLocalCopy
                ? "전송 실패 — 사진은 기기에 저장되었습니다."
                : "전송에 실패했습니다. 로컬 저장을 켜면 기기에 보관됩니다.";
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
