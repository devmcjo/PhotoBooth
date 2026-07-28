using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPhoto.App.Imaging;
using MCPhoto.Core.Accounts;
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

    // it11 #16: 업로드 진행률 UI. 전체 비율(0.0~1.0) + 단계 라벨 + 세밀 진행 불가 시 무한 표시.
    [ObservableProperty] private double _uploadProgress;
    [ObservableProperty] private string _progressLabel = string.Empty;
    [ObservableProperty] private bool _isIndeterminate = true;

    // ComputeOverall이 전송 구성을 알 수 있도록 진입 시 저장(사진/타임랩스 각각 전송 여부).
    private bool _hasPhoto;
    private bool _hasTimelapse;

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

        // it11 #16: 진행률 전송 구성 저장 + 상태 초기화(재시도 시 재호출로 자동 리셋).
        // 첫 진행 콜백 전에도 라벨이 보이도록 초기 문구 지정, 세밀 진행 전까진 indeterminate.
        _hasPhoto = photoPath is not null;
        _hasTimelapse = timelapsePath is not null;
        UploadProgress = 0;
        IsIndeterminate = true;
        ProgressLabel = "업로드 중...";

        IsUploading = true;
        UploadFailed = false;
        UploadSucceeded = false;
        StatusMessage = "업로드 중...";
        try
        {
            // Progress<T>는 생성 스레드의 SynchronizationContext로 콜백을 마샬링한다.
            // OnEnterAsync는 UI 스레드에서 실행 → 여기서 생성해야 OnUploadProgress가 UI 스레드에서 돌아
            // [ObservableProperty] 갱신이 안전(§3.16.4). 백그라운드 스레드 생성 금지.
            var progress = new Progress<UploadProgress>(OnUploadProgress);
            var result = await _upload.UploadResultAsync(
                photoPath,
                timelapsePath,
                settings.RetentionHours,
                settings.HostingBaseUrl,
                progress);

            session.Result = result;

            // 업로드 성공 후에만 QR 노출(§10)
            var png = _qr.GenerateQrPng(result.DownloadPageUrl, pixelsPerModule: 12);
            QrImage = StillImageConverter.FromPngBytes(png);
            ExpiryNotice = $"업로드된 사진·영상은 {settings.RetentionHours}시간 후 자동 삭제됩니다.";
            UploadSucceeded = true;
            UploadFailed = false;
            StatusMessage = string.Empty;
        }
        catch (QrLimitExceededException ex)
        {
            // it13 §9.3: TempUser 한도 초과로 서버가 업로드를 거부(403). 사유별 정확 문구(§0)를 노출한다.
            //            결과물은 로컬 보존(QR 분기 이전 저장, 손실 0). 카운트는 서버 commit 성공 시에만 증가(거부=미증가).
            _logger?.LogInformation("TempUser QR 한도 초과({Reason}) — 로컬 보존, 완료 진행 가능", ex.Reason);
            UploadSucceeded = false;
            UploadFailed = true;
            QrImage = null;
            StatusMessage = ex.Reason == QrGateReason.Time
                ? "무료 사용 시간이 지났습니다. 관리자에게 문의해주세요."
                : "무료 사용 횟수가 소진되었습니다. 관리자에게 문의해주세요.";
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

    /// <summary>
    /// 진행 콜백(UI 스레드에서 실행 — Progress&lt;T&gt;가 OnEnterAsync의 UI 컨텍스트로 마샬링).
    /// UI 상태(진행률·라벨)만 변경한다(§3.16.4).
    /// </summary>
    private void OnUploadProgress(UploadProgress p)
    {
        IsIndeterminate = false;
        UploadProgress = ComputeOverall(p.Stage, p.Fraction, _hasPhoto, _hasTimelapse);
        ProgressLabel = p.Label ?? StageLabel(p.Stage);
    }

    /// <summary>
    /// 단계·단계내 비율을 전송 미디어 구성 기준으로 전체 진행률(0~1)로 정규화. 순수 함수(테스트 대상).
    /// 사진만/타임랩스만이면 해당 단계가 전체 100%, 둘 다면 사진 0~0.5·타임랩스 0.5~1.
    /// Finalizing은 항상 전체 100%(문서 생성은 순간). (§3.16.8)
    /// </summary>
    public static double ComputeOverall(UploadStage stage, double fraction, bool hasPhoto, bool hasTimelapse)
    {
        var frac = Math.Clamp(fraction, 0.0, 1.0);

        if (stage == UploadStage.Finalizing)
            return 1.0;

        // 전송 미디어가 없다는 건 논리상 진입 불가지만 방어(0 나눗셈 회피).
        if (!hasPhoto && !hasTimelapse)
            return frac;

        if (hasPhoto && hasTimelapse)
        {
            // 사진 구간 [0, 0.5], 타임랩스 구간 [0.5, 1.0].
            return stage == UploadStage.Photo
                ? frac * 0.5
                : 0.5 + frac * 0.5;
        }

        // 단일 미디어(사진만 또는 타임랩스만) → 해당 단계가 전체 100% 기여.
        return frac;
    }

    private static string StageLabel(UploadStage stage) => stage switch
    {
        UploadStage.Photo => "사진 업로드 중",
        UploadStage.Timelapse => "영상 업로드 중",
        UploadStage.Finalizing => "마무리 중",
        _ => "업로드 중"
    };

    /// <summary>재시도(업로드 실패 시).</summary>
    [RelayCommand]
    private async Task Retry() => await OnEnterAsync();

    /// <summary>[홈으로]/[닫기] → 완료.</summary>
    [RelayCommand]
    private async Task Done() => await _shell.NavigateAsync(AppState.Done);

    [RelayCommand]
    private void GoHome() => _shell.ReturnHome("QR 닫기");
}
