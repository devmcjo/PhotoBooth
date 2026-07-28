using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPhoto.App.Imaging;
using MCPhoto.Core.Capture;
using MCPhoto.Core.LocalSave;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.ViewModels;

/// <summary>
/// 결과 화면: 합성 미리보기 + 필터 토글(전체 컷 일괄). 프레임은 촬영 전 선택 고정(변경 불가). (BM④)
/// [다음] 시 타임랩스 생성·로컬 저장(옵션)·QR 전송(옵션) 후 다음 상태.
/// </summary>
public sealed partial class ResultViewModel : ViewModelBase
{
    private readonly AppShellViewModel _shell;
    private readonly ICompositionService _composition;
    private readonly ITimelapseService _timelapse;
    private readonly ILocalSaveService _localSave;
    private readonly ICameraService _camera;
    private readonly ILogger<ResultViewModel>? _logger;

    [ObservableProperty] private ImageSource? _preview;
    [ObservableProperty] private FilterKind _selectedFilter = FilterKind.None;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = string.Empty;

    /// <summary>결과 화면에 노출할 필터(항상 None + 설정에서 켜진 것). (it8 §6 A6)</summary>
    public System.Collections.ObjectModel.ObservableCollection<FilterOption> AvailableFilters { get; } = new();

    public ResultViewModel(
        AppShellViewModel shell,
        ICompositionService composition,
        ITimelapseService timelapse,
        ILocalSaveService localSave,
        ICameraService camera,
        ILogger<ResultViewModel>? logger = null)
    {
        _shell = shell;
        _composition = composition;
        _timelapse = timelapse;
        _localSave = localSave;
        _camera = camera;
        _logger = logger;
    }

    public override async Task OnEnterAsync()
    {
        SelectedFilter = _shell.Session.Filter;
        BuildAvailableFilters();
        await ComposePreviewAsync();
    }

    /// <summary>설정에서 켜진 필터만(+항상 None) 노출 목록 구성. (it8 §6 A6)</summary>
    private void BuildAvailableFilters()
    {
        AvailableFilters.Clear();
        foreach (var opt in BuildFilterOptions(_shell.Settings.Current))
            AvailableFilters.Add(opt);
    }

    /// <summary>설정 → 필터 옵션 목록(순수 로직, 테스트 대상). 항상 None + 켜진 것. (it8 §6 A6)</summary>
    public static IReadOnlyList<FilterOption> BuildFilterOptions(Core.Settings.AppSettings s)
    {
        var list = new List<FilterOption> { new(FilterKind.None, "원본") }; // 항상 제공
        if (s.FilterGrayscale) list.Add(new FilterOption(FilterKind.Grayscale, "흑백"));
        if (s.FilterBrightness) list.Add(new FilterOption(FilterKind.Brightness, "밝게"));
        if (s.FilterBeauty) list.Add(new FilterOption(FilterKind.Beauty, "뷰티"));
        return list;
    }

    private async Task ComposePreviewAsync()
    {
        var session = _shell.Session;
        var frame = session.SelectedFrame;
        if (frame is null) { _shell.ReturnHome("프레임 없음"); return; }

        IsBusy = true;
        StatusMessage = "합성 중...";
        try
        {
            var ext = _shell.Settings.Current.OutputFormat == OutputFormat.Png ? "png" : "jpg";
            var workFolder = session.WorkFolder ?? Path.Combine(App.DataFolder, "sessions", "tmp");
            Directory.CreateDirectory(workFolder);
            var outPath = Path.Combine(workFolder, $"final.{ext}");

            var cuts = session.Capture.GetSelectedCuts();
            await _composition.ComposeAsync(frame, cuts, SelectedFilter, outPath);
            session.FinalImagePath = outPath;

            Preview = StillImageConverter.FromFile(outPath);
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "합성 실패");
            StatusMessage = "합성에 실패했습니다.";
        }
        finally { IsBusy = false; }
    }

    /// <summary>필터 변경 → 재합성(전체 컷 일괄).</summary>
    [RelayCommand]
    private async Task SetFilter(FilterKind filter)
    {
        if (IsBusy || SelectedFilter == filter) return;
        SelectedFilter = filter;
        _shell.Session.Filter = filter;
        await ComposePreviewAsync();
    }

    /// <summary>[다음]: 타임랩스 생성 → 로컬 저장(옵션) → QR 전송(옵션) 또는 완료.</summary>
    [RelayCommand]
    private async Task Next()
    {
        if (IsBusy) return;
        var session = _shell.Session;
        var settings = _shell.Settings.Current;
        IsBusy = true;
        try
        {
            // 타임랩스 생성(녹화본 있을 때)
            if (session.SessionVideoPath is not null && File.Exists(session.SessionVideoPath))
            {
                StatusMessage = "영상 생성 중...";
                var tlPath = Path.Combine(session.WorkFolder!, "timelapse.mp4");
                // 세션 길이 전달(CameraService가 녹화 종료 시 기록) → 배속 N 역산
                if (_timelapse is Capture.TimelapseService ts && _camera is Capture.OpenCvCameraService cam)
                    ts.LastSessionSeconds = cam.LastSessionSeconds;
                session.TimelapsePath = await _timelapse.CreateTimelapseAsync(session.SessionVideoPath, tlPath);
            }

            // 로컬 저장(saveLocalCopy on)
            if (settings.SaveLocalCopy && session.FinalImagePath is not null)
            {
                StatusMessage = "로컬 저장 중...";
                var savePath = string.IsNullOrWhiteSpace(settings.LocalSavePath)
                    ? Path.Combine(AppContext.BaseDirectory, "result")
                    : settings.LocalSavePath;
                await _localSave.SaveAsync(savePath, session.FinalImagePath, session.TimelapsePath, session.SessionTime);
            }

            // QR 전송 런타임 게이트: raw 설정 + 로그인 + TempUser 한도상태를 QrEffectivePolicy 단일 지점에서 조합.
            // ⚠️ ini(EnableQrDelivery)는 읽기만 — 어떤 경우에도 write하지 않는다(게스트·TempUser 초과는 오버라이드만, it13 §7.4).
            bool qrEffective = QrEffectivePolicy.IsQrEnabled(
                rawEnableQr: settings.EnableQrDelivery,          // ini raw — 읽기만, 변경 없음
                isLoggedIn: _shell.IsLoggedIn,
                isTempUserBlocked: _shell.IsTempUserQrBlocked);  // 셸이 역할+한도 합성(§7.5)
            if (qrEffective)
                await _shell.NavigateAsync(AppState.Qr);
            else
                await _shell.NavigateAsync(AppState.Done);       // 게스트·TempUser 초과 → 팝업 없이 완료(우아)
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "결과 처리 실패");
            StatusMessage = "처리 중 오류가 발생했습니다.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void Cancel() => _shell.ReturnHome("결과 취소");
}

/// <summary>결과 화면 필터 버튼 항목(종류 + 표시 라벨). (it8 §6 A6)</summary>
public sealed record FilterOption(FilterKind Kind, string Label);
