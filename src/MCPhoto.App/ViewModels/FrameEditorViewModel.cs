using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPhoto.App.Imaging;
using MCPhoto.Core.Frames;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.ViewModels;

/// <summary>
/// 프레임 편집기. 이미지 업로드 → 슬롯 개수 지정(자동 배치) → 드래그 조절 → 저장. (PRD §F2)
/// 편집 범위=슬롯 배치만(텍스트/스티커/배경 제외). 경계 클램프·겹침 방지·10개 제한.
/// </summary>
public sealed partial class FrameEditorViewModel : ViewModelBase
{
    private readonly AppShellViewModel _shell;
    private readonly IFrameRepository _repository;
    private readonly ILocalFrameStore _localStore;
    private readonly ILogger<FrameEditorViewModel>? _logger;

    private byte[]? _imageBytes;

    // 편집 모드 상태(기존 프레임 편집 시 LoadForEdit가 set). 신규 생성이면 _isEditing=false.
    // FrameEditorViewModel은 Transient 등록(ServiceRegistration.cs) → 진입마다 새 인스턴스라 재진입 잔존 없음.
    private bool _isEditing;
    private string? _editingFrameId;
    private bool _suppressArrange; // LoadForEdit 중 SlotCount 설정이 기존 슬롯을 자동 배치로 덮어쓰지 않도록.

    // ── item2 §4.3: 원본 스냅샷(diff 기준). LoadForEdit 진입 시 세팅, 신규 생성에서는 비움. ──
    private byte[]? _originalImageBytes;
    private readonly List<Slot> _originalSlots = new();
    private string _originalName = string.Empty;
    private FrameTemplate? _editingFrame; // 편집 대상 원본 참조(Id·IsDefault·UserId 보존)

    [ObservableProperty] private ImageSource? _frameImage;
    [ObservableProperty] private int _frameWidth;
    [ObservableProperty] private int _frameHeight;
    [ObservableProperty] private int _slotCount = 4;
    [ObservableProperty] private string _frameName = "새 프레임";
    [ObservableProperty] private string _editorTitle = "새 프레임 만들기";
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _canSave;

    // ── item2 §4.4: 기본 프레임 DB 업데이트 확인 팝업(power가 DB 공용 기본 프레임 편집·저장 시) ──
    /// <summary>"로컬만 / DB도 업데이트 / 취소" 확인 팝업 오버레이 표시.</summary>
    [ObservableProperty] private bool _isDbUpdatePromptVisible;
    /// <summary>DB 업데이트 결과 안내(성공/변경없음/실패).</summary>
    [ObservableProperty] private string _dbUpdateNotice = string.Empty;
    /// <summary>안내가 오류인지(색상 구분용).</summary>
    [ObservableProperty] private bool _dbUpdateNoticeIsError;

    /// <summary>슬롯 종횡비(편집기 전역, MVP). 변경 시 재배치. (it4 §3)</summary>
    [ObservableProperty] private SlotAspect _slotAspect = SlotAspect.Ratio3x4;

    /// <summary>종횡비 선택 옵션(4:3 / 3:4 / 1:1).</summary>
    public IReadOnlyList<SlotAspect> AspectOptions { get; } =
        new[] { SlotAspect.Ratio4x3, SlotAspect.Ratio3x4, SlotAspect.Ratio1x1 };

    /// <summary>슬롯 개수 옵션(1~6). 값 기반 바인딩(SelectedValue)으로 초기화 clobber 차단. (it7 B9)</summary>
    public IReadOnlyList<int> SlotCountOptions { get; } = new[] { 1, 2, 3, 4, 5, 6 };

    /// <summary>슬롯 크기 일괄 스케일(%, 10~300). 기본 100. (it5 §8 F1 → 범위 대폭 확대)</summary>
    [ObservableProperty] private double _slotScalePercent = 100;

    /// <summary>슬롯 스케일 허용 범위(슬라이더·직접입력 공통).</summary>
    public const double MinScale = 10;
    public const double MaxScale = 300;

    /// <summary>스케일 기준(100% 원본) 슬롯. _baseSlots에서 매번 스케일해 누적 오차 방지. (it5 §8)</summary>
    private readonly List<Slot> _baseSlots = new();

    /// <summary>편집 중 슬롯(드래그 대상).</summary>
    public ObservableCollection<Slot> Slots { get; } = new();

    public FrameEditorViewModel(AppShellViewModel shell, IFrameRepository repository, ILocalFrameStore localStore, ILogger<FrameEditorViewModel>? logger = null)
    {
        _shell = shell;
        _repository = repository;
        _localStore = localStore;
        _logger = logger;
    }

    /// <summary>이미지 파일 로드(업로드). 제한 검사 후 프레임 크기·미리보기 설정.</summary>
    public bool LoadImage(string path)
    {
        if (!FrameImageValidator.IsSupportedExtension(path))
        {
            StatusMessage = "PNG/JPG/JPEG만 지원합니다.";
            return false;
        }
        var info = new FileInfo(path);
        if (!FrameImageValidator.IsSizeWithinLimit(info.Length))
        {
            StatusMessage = "이미지가 10MB를 초과합니다.";
            return false;
        }

        try
        {
            using var mat = OpenCvSharp.Cv2.ImRead(path, OpenCvSharp.ImreadModes.Color);
            if (mat.Empty()) { StatusMessage = "이미지를 읽을 수 없습니다."; return false; }

            var (w, h) = FrameImageValidator.ScaledSize(mat.Width, mat.Height);
            // 장변 4000 초과 시 축소
            using var resized = new OpenCvSharp.Mat();
            if (w != mat.Width || h != mat.Height)
                OpenCvSharp.Cv2.Resize(mat, resized, new OpenCvSharp.Size(w, h));
            else
                mat.CopyTo(resized);

            OpenCvSharp.Cv2.ImEncode(".png", resized, out var buf);
            _imageBytes = buf;

            FrameWidth = w;
            FrameHeight = h;
            FrameImage = StillImageConverter.FromPngBytes(_imageBytes);

            ArrangeSlots();
            StatusMessage = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "이미지 로드 실패: {Path}", path);
            StatusMessage = "이미지 로드 실패";
            return false;
        }
    }

    /// <summary>
    /// 기존 프레임을 편집기로 불러온다(파워=공용/DB 프레임, user=본인 로컬). 이미지·슬롯·이름을 그대로 로드.
    /// 저장 시 신규 생성이 아니라 해당 프레임을 갱신(파워+실 DB id면 DB도 update). (기능 요청)
    /// </summary>
    public void LoadForEdit(FrameTemplate frame)
    {
        _isEditing = true;
        _editingFrameId = frame.Id;
        _editingFrame = frame; // 원본 참조(Id·IsDefault·UserId 보존, diff·업데이트 대상)
        EditorTitle = "프레임 편집";
        FrameName = frame.Name;

        // 원본 스냅샷(diff 기준) — 이름·슬롯은 즉시 확정, 이미지는 아래 파일 로드 후.
        _originalName = frame.Name;
        _originalSlots.Clear();
        foreach (var s in frame.Slots)
            _originalSlots.Add(new Slot { Index = s.Index, X = s.X, Y = s.Y, Width = s.Width, Height = s.Height });
        _originalImageBytes = null; // 이미지 로드 성공 시 세팅(실패 시 null → diff에서 변경으로 보수 판정)

        if (string.IsNullOrEmpty(frame.ImageUrl) || !File.Exists(frame.ImageUrl))
        {
            StatusMessage = "프레임 이미지를 불러올 수 없습니다(로컬 파일 없음).";
            return;
        }
        try
        {
            _imageBytes = File.ReadAllBytes(frame.ImageUrl); // 로컬 저장분은 이미 PNG(가공본)
            _originalImageBytes = (byte[])_imageBytes.Clone(); // diff 기준 원본 스냅샷
            FrameImage = StillImageConverter.FromPngBytes(_imageBytes);
            if (frame.ImageSize.Width > 0) FrameWidth = frame.ImageSize.Width;
            if (frame.ImageSize.Height > 0) FrameHeight = frame.ImageSize.Height;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "편집 이미지 로드 실패: {Path}", frame.ImageUrl);
            StatusMessage = "이미지 로드 실패";
            return;
        }

        // 슬롯 개수 콤보 반영 — 자동 배치(OnSlotCountChanged) 억제하고 기존 슬롯 유지.
        _suppressArrange = true;
        SlotCount = Math.Clamp(frame.Slots.Count, SlotCountOptions[0], SlotCountOptions[^1]);
        _suppressArrange = false;

        // 기존 슬롯을 스케일 기준(_baseSlots)으로 로드하고 100%로 표시(자동 배치 아님).
        _baseSlots.Clear();
        foreach (var s in frame.Slots)
            _baseSlots.Add(new Slot { Index = s.Index, X = s.X, Y = s.Y, Width = s.Width, Height = s.Height });
        SlotScalePercent = 100;
        ApplyScale(); // Slots = _baseSlots (100%)
    }

    /// <summary>슬롯 개수 변경 → 자동 배치. (편집 로드 중에는 억제해 기존 슬롯 보존)</summary>
    partial void OnSlotCountChanged(int value)
    {
        if (_suppressArrange) return;
        ArrangeSlots();
    }

    /// <summary>종횡비 변경 → 선택 비율로 재배치. (it4 §3)</summary>
    partial void OnSlotAspectChanged(SlotAspect value) => ArrangeSlots();

    /// <summary>슬롯 크기 슬라이더/직접입력(%) 변경 → _baseSlots 기준 일괄 스케일. 10~300 클램프.</summary>
    partial void OnSlotScalePercentChanged(double value)
    {
        var clamped = Math.Clamp(value, MinScale, MaxScale);
        if (Math.Abs(clamped - value) > 0.0001)
        {
            SlotScalePercent = clamped; // 재진입(클램프 값으로) — 아래 ApplyScale는 다음 호출에서 수행
            return;
        }
        ApplyScale();
    }

    private void ArrangeSlots()
    {
        if (FrameWidth <= 0 || FrameHeight <= 0) return;
        // 자동 배치 원본을 스케일 기준으로 보관, 현재 배율을 재적용(개수·종횡비 변경 시).
        _baseSlots.Clear();
        _baseSlots.AddRange(SlotLayout.AutoArrange(SlotCount, FrameWidth, FrameHeight, SlotAspect.ToRatio()));
        ApplyScale();
    }

    /// <summary>_baseSlots에서 현재 배율로 스케일해 Slots 갱신(누적 오차 없음).</summary>
    private void ApplyScale()
    {
        if (_baseSlots.Count == 0) return;
        Slots.Clear();
        foreach (var s in SlotLayout.ScaleSlots(_baseSlots, SlotScalePercent / 100.0, FrameWidth, FrameHeight))
            Slots.Add(s);
        UpdateCanSave();
    }

    /// <summary>드래그 후 슬롯 위치·크기 반영(경계 클램프). 스케일 기준(_baseSlots) 위치도 중심 맞춰 갱신.</summary>
    public void UpdateSlot(int index, int x, int y, int width, int height)
    {
        if (index < 0 || index >= Slots.Count) return;
        var clamped = SlotLayout.ClampToFrame(
            new Slot { Index = index, X = x, Y = y, Width = width, Height = height },
            FrameWidth, FrameHeight);
        Slots[index] = clamped;

        // 드래그로 옮긴 중심을 _baseSlots에도 반영(원본 크기 유지) → 이후 스케일 기준 위치 일치.
        if (index < _baseSlots.Count)
        {
            var b = _baseSlots[index];
            double cx = clamped.X + clamped.Width / 2.0;
            double cy = clamped.Y + clamped.Height / 2.0;
            _baseSlots[index] = SlotLayout.ClampToFrame(
                new Slot
                {
                    Index = b.Index,
                    X = (int)Math.Round(cx - b.Width / 2.0),
                    Y = (int)Math.Round(cy - b.Height / 2.0),
                    Width = b.Width,
                    Height = b.Height
                }, FrameWidth, FrameHeight);
        }
        UpdateCanSave();
    }

    private void UpdateCanSave()
        => CanSave = _imageBytes is not null && SlotLayout.IsValid(Slots, FrameWidth, FrameHeight);

    /// <summary>편집 중이고 실 DB 문서 id(local/bundle/fallback 접두 없음)면 그 id 반환(→DB 갱신), 아니면 null(→신규 생성).</summary>
    private string? EditingServerId()
    {
        if (!_isEditing || string.IsNullOrEmpty(_editingFrameId)) return null;
        if (_editingFrameId.StartsWith("local:", StringComparison.Ordinal)
            || _editingFrameId.StartsWith("bundle:", StringComparison.Ordinal)
            || _editingFrameId.StartsWith("fallback", StringComparison.Ordinal)) return null;
        return _editingFrameId;
    }

    [RelayCommand]
    private async Task Save()
    {
        var user = _shell.Session.CurrentUser;
        if (user is null) { StatusMessage = "로그인이 필요합니다."; return; }
        if (_imageBytes is null || !SlotLayout.IsValid(Slots, FrameWidth, FrameHeight))
        {
            StatusMessage = "슬롯이 겹치거나 프레임을 벗어났습니다.";
            return;
        }

        // item2 §4.1: power가 DB 공용 기본 프레임을 편집·저장하면 "로컬만/DB도 업데이트" 확인 팝업을 띄우고 보류.
        if (_editingFrame is { } editing && FrameEditPolicy.RequiresDbUpdatePrompt(editing, user.Role))
        {
            DbUpdateNotice = string.Empty;
            DbUpdateNoticeIsError = false;
            IsDbUpdatePromptVisible = true;
            return; // 저장 보류 — 팝업 버튼(SaveLocalOnly/SaveToDb/Cancel)이 이어받음
        }

        try
        {
            StatusMessage = "저장 중...";
            bool isPower = user.Role.IsPower();

            if (isPower)
            {
                // 파워: 공용 기본 프레임 → DB(isDefault=true, userId=null) + 로컬 캐시. (it8 §3 A2)
                // 이 경로는 신규 생성(EditingServerId=null)만 도달한다. DB 기본 편집은 위에서 팝업으로 분기됨.
                var frame = new FrameTemplate
                {
                    Id = EditingServerId() ?? string.Empty, // 빈 값이면 SaveAsync가 새 GUID 부여(신규)
                    UserId = null,
                    IsDefault = true,
                    Name = FrameName,
                    ImageSize = new ImageSize { Width = FrameWidth, Height = FrameHeight },
                    Slots = Slots.ToList()
                };
                var saved = await _repository.SaveAsync(frame, _imageBytes);
                _localStore.SaveLocal(saved, _imageBytes, ownerName: null); // frameId 기반 캐시(갱신)
            }
            else
            {
                // user: 로컬 전용(DB 미저장). {계정}_{이름}.png. 편집이면 같은 이름 파일을 덮어씀. (it8 §3 A2)
                var frame = new FrameTemplate
                {
                    UserId = user.Id,
                    IsDefault = false,
                    Name = FrameName,
                    ImageSize = new ImageSize { Width = FrameWidth, Height = FrameHeight },
                    Slots = Slots.ToList()
                };
                _localStore.SaveLocal(frame, _imageBytes, ownerName: user.Id);
            }

            await _shell.NavigateAsync(AppState.FrameSelect);
        }
        catch (InvalidOperationException ex)
        {
            // 10개 초과 등
            StatusMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "프레임 저장 실패");
            StatusMessage = "저장에 실패했습니다.";
        }
    }

    // ── item2 §4.4: 기본 프레임 저장 팝업 버튼 ──

    /// <summary>[로컬에만 적용]: DB 미호출, 로컬 공용 캐시만 갱신(#dbid 보존). (item2 §4.4)</summary>
    [RelayCommand]
    private async Task SaveLocalOnly()
    {
        if (_editingFrame is null || _imageBytes is null) { IsDbUpdatePromptVisible = false; return; }
        try
        {
            var frame = BuildDbFrame(); // 같은 Id(#dbid 보존), userId=null, isDefault=true
            _localStore.SaveLocal(frame, _imageBytes, ownerName: null);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "프레임 로컬 저장 실패");
            IsDbUpdatePromptVisible = false;
            StatusMessage = "저장에 실패했습니다."; // 편집 화면 유지(이동 없음)
            return;
        }
        IsDbUpdatePromptVisible = false;
        await GoToFrameSelectAsync(); // 저장 성공 후 화면 전환(전환 실패는 저장 결과에 영향 없음)
    }

    /// <summary>
    /// [DB에도 업데이트]: diff로 변경 판정 → 변경 있으면 같은 frameId 업데이트(레포)+로컬 캐시,
    /// 변경 없으면 DB 미호출(로컬만). 실패 시 화면 유지. (item2 §4.4)
    /// </summary>
    [RelayCommand]
    private async Task SaveToDb()
    {
        if (_editingFrame is null || _imageBytes is null) { IsDbUpdatePromptVisible = false; return; }

        var change = FrameDiff.Compare(
            _originalImageBytes, _imageBytes,
            _originalSlots, Slots.ToList(),
            _originalName, FrameName);

        // 저장(로컬/DB)과 결과 안내를 먼저 확정한다. 화면 전환 실패가 저장 결과 안내를 뒤엎지 않도록 분리.
        try
        {
            var frame = BuildDbFrame();

            if (!change.HasAnyChange)
            {
                // 변경 없음: DB 미호출(no-op), 로컬 캐시만 갱신.
                _localStore.SaveLocal(frame, _imageBytes, ownerName: null);
                DbUpdateNoticeIsError = false;
                DbUpdateNotice = "변경 사항이 없어 DB 업데이트를 건너뛰었습니다.";
            }
            else if (!_repository.SupportsUpdateById)
            {
                // 이론상 도달하지 않음(레거시·HTTP 모두 지원). 방어적: 로컬만 적용 + 경고.
                _localStore.SaveLocal(frame, _imageBytes, ownerName: null);
                DbUpdateNoticeIsError = true;
                DbUpdateNotice = "현재 서버 모드에서는 기본 프레임 DB 업데이트를 지원하지 않습니다(로컬만 적용됨).";
            }
            else
            {
                // 변경 있음 + 지원 모드: 같은 frameId 업데이트(이미지 변경 시에만 replaceImage=true).
                var saved = await _repository.UpdateAsync(frame, _imageBytes, replaceImage: change.ImageChanged);
                _localStore.SaveLocal(saved, _imageBytes, ownerName: null); // saved.Id=원본 GUID → #dbid 보존
                DbUpdateNoticeIsError = false;
            }
        }
        catch (InvalidOperationException ex)
        {
            // 업데이트 대상 미발견·10개 초과 등. 화면 유지 + 안내.
            IsDbUpdatePromptVisible = false;
            DbUpdateNoticeIsError = true;
            StatusMessage = ex.Message;
            return;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "프레임 DB 업데이트 실패");
            IsDbUpdatePromptVisible = false;
            DbUpdateNoticeIsError = true;
            StatusMessage = "DB 업데이트에 실패했습니다.";
            return;
        }

        IsDbUpdatePromptVisible = false;
        await GoToFrameSelectAsync();
    }

    /// <summary>저장 성공 후 프레임 선택 화면으로 전환. 전환 자체 실패는 저장 결과에 영향 없음(안내만 로그).</summary>
    private async Task GoToFrameSelectAsync()
    {
        try { await _shell.NavigateAsync(AppState.FrameSelect); }
        catch (Exception ex) { _logger?.LogError(ex, "프레임 선택 화면 전환 실패(저장은 완료)"); }
    }

    /// <summary>[취소]: 팝업만 닫고 편집 유지(저장·이동 없음). (item2 §4.4)</summary>
    [RelayCommand]
    private void CancelDbUpdatePrompt() => IsDbUpdatePromptVisible = false;

    /// <summary>DB 공용 기본 프레임 저장용 템플릿(같은 Id로 #dbid 보존, userId=null, isDefault=true).</summary>
    private FrameTemplate BuildDbFrame() => new()
    {
        Id = _editingFrame!.Id,
        UserId = null,
        IsDefault = true,
        Name = FrameName,
        ImageSize = new ImageSize { Width = FrameWidth, Height = FrameHeight },
        Slots = Slots.ToList()
    };

    [RelayCommand]
    private async Task Cancel() => await _shell.NavigateAsync(AppState.FrameSelect);
}
