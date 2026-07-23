using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPhoto.App.Services;
using MCPhoto.Capture;
using MCPhoto.Core.Capture;
using MCPhoto.Core.Models;
using MCPhoto.Core.Upload;
using MCPhoto.Firebase;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.ViewModels;

/// <summary>
/// 진단/상태 화면 VM(관리자 트러블슈팅용). 모달 다이얼로그 전용 — 별도 AppState 미추가(회귀 표면 0). (it11 §3.14)
/// 카메라(연결·목록)·ffmpeg(가용·경로)·Firebase(초기화·버킷·키 후보) 헬스체크 + 로그 폴더 경로·열기.
/// UI 타입(Visibility/Brush) 미의존 — 상태는 bool/int/string, 색은 View의 DataTrigger가 담당(§1.3).
/// 진단 화면은 라이브 프리뷰(StartAsync)를 켜지 않는다 → 카메라 점유 없음(열거만). (§3.14.4)
/// </summary>
public sealed partial class DiagnosticsViewModel : ObservableObject
{
    private readonly ICameraService _camera;
    private readonly FfmpegRunner _ffmpeg;
    private readonly IFirebaseClient _firebase;
    private readonly ILogFolderService _logFolder;
    private readonly ILogger<DiagnosticsViewModel>? _logger;

    public DiagnosticsViewModel(ICameraService camera, FfmpegRunner ffmpeg, IFirebaseClient firebase,
        ILogFolderService logFolder, ILogger<DiagnosticsViewModel>? logger = null)
    {
        _camera = camera;
        _ffmpeg = ffmpeg;
        _firebase = firebase;
        _logFolder = logFolder;
        _logger = logger;

        // Firebase 키 후보 경로 + 존재 여부(QA가 "키를 어디 두어야 하는지" 파악). (§3.14.9 E4)
        FirebaseKeyCandidates = new ReadOnlyCollection<FirebaseKeyCandidate>(
            FirebaseClient.KeyCandidatePaths()
                .Select(p => new FirebaseKeyCandidate(p, File.Exists(p)))
                .ToList());
    }

    // ── 카메라 ──
    /// <summary>카메라 열거 진행 중(로딩 표시·재검사 버튼 비활성).</summary>
    [ObservableProperty] private bool _isCheckingCamera;

    /// <summary>연결된 카메라 대수.</summary>
    [ObservableProperty] private int _cameraCount;

    /// <summary>카메라 연결 여부(색상 트리거용). 0대=false.</summary>
    [ObservableProperty] private bool _hasCamera;

    /// <summary>카메라 요약 문구(예: "2대 연결됨" / "미연결").</summary>
    [ObservableProperty] private string _cameraSummary = string.Empty;

    /// <summary>연결된 카메라 목록(Index·Name). #15 FriendlyName이 있으면 실제 장치명 표시.</summary>
    public ObservableCollection<CameraDevice> Cameras { get; } = new();

    // ── ffmpeg ──
    /// <summary>ffmpeg 실행 파일 존재 여부(실연동 가능).</summary>
    public bool FfmpegAvailable => _ffmpeg.IsAvailable;
    /// <summary>ffmpeg 해석 경로(번들/PATH).</summary>
    public string FfmpegPath => _ffmpeg.FfmpegPath;

    // ── Firebase ──
    /// <summary>Firebase 초기화(서비스 계정 키 로드) 여부.</summary>
    public bool FirebaseInitialized => _firebase.IsInitialized;
    /// <summary>초기화 시 버킷명, 미초기화 시 안내.</summary>
    public string FirebaseBucket => _firebase.IsInitialized ? _firebase.Bucket : "(미초기화)";
    /// <summary>서비스 계정 키 탐색 후보 경로 + 존재 여부(트러블슈팅용).</summary>
    public IReadOnlyList<FirebaseKeyCandidate> FirebaseKeyCandidates { get; }

    // ── 로그 ──
    /// <summary>로그 폴더 절대 경로(표시·수동 탐색용).</summary>
    public string LogFolderPath => _logFolder.LogFolderPath;

    /// <summary>카메라 재검사(백그라운드 열거 — UI 블로킹 방지). 진입 시 다이얼로그 서비스가 1회 호출.</summary>
    [RelayCommand]
    private async Task RefreshCameras()
    {
        if (IsCheckingCamera) return;
        IsCheckingCamera = true;
        try
        {
            // EnumerateDevices()는 장치 0~7 open/close(수백 ms~초) → Task.Run 백그라운드(SettingsViewModel과 동일 패턴).
            var devices = await Task.Run(() => _camera.EnumerateDevices());
            Cameras.Clear();
            foreach (var d in devices) Cameras.Add(d);
            CameraCount = Cameras.Count;
            HasCamera = CameraCount > 0;
            CameraSummary = HasCamera ? $"{CameraCount}대 연결됨" : "미연결";
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "진단 카메라 열거 실패");
            Cameras.Clear();
            CameraCount = 0;
            HasCamera = false;
            CameraSummary = "미연결";
        }
        finally { IsCheckingCamera = false; }
    }

    /// <summary>로그 폴더를 탐색기로 열기(best-effort, 실패해도 크래시 없음).</summary>
    [RelayCommand]
    private void OpenLogFolder() => _logFolder.OpenLogFolder();
}

/// <summary>Firebase 서비스 계정 키 후보 경로 + 존재 여부. ToString=경로(폴백 표기). (it11 §3.14.9 E4)</summary>
public sealed record FirebaseKeyCandidate(string Path, bool Exists)
{
    public override string ToString() => Path;
}
