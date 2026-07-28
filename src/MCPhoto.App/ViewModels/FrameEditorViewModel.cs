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
    // it15 F1: DB 업데이트 경로 제거로 편집 대상 원본 참조·서버 문서 id가 불필요해졌다
    // (fork/덮어쓰기 판정은 아래 _sessionSource가 전담).
    private bool _isEditing;
    private bool _suppressArrange; // LoadForEdit 중 SlotCount 설정이 기존 슬롯을 자동 배치로 덮어쓰지 않도록.

    /// <summary>편집 세션의 진입 경로 = 저장 방식(fork vs 덮어쓰기)의 유일한 판정 축. (it15 §3.3)</summary>
    private enum FrameSessionSource
    {
        /// <summary>빈 편집기에서 시작한 신규 생성(power면 DB 등록 경로).</summary>
        New,

        /// <summary>본인 로컬 프레임 편집 → 같은 이름 덮어쓰기.</summary>
        EditOwnLocal,

        /// <summary>DB/번들/fallback 유래(편집 또는 F2 불러오기) → 원본 보존 + 새 이름 분기.</summary>
        ForkFromCatalog
    }

    private FrameSessionSource _sessionSource = FrameSessionSource.New;
    private string _sourceName = string.Empty; // fork 원본 이름(원본 덮어쓰기 가드용)

    [ObservableProperty] private ImageSource? _frameImage;
    [ObservableProperty] private int _frameWidth;
    [ObservableProperty] private int _frameHeight;
    [ObservableProperty] private int _slotCount = 4;
    [NotifyPropertyChangedFor(nameof(SaveScopeNotice))]
    [ObservableProperty] private string _frameName = "새 프레임";
    [ObservableProperty] private string _editorTitle = "새 프레임 만들기";
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _canSave;

    /// <summary>
    /// 신규 생성 흐름인지(= <see cref="LoadForEdit"/>로 진입하지 않았는지).
    /// 두 곳의 게이트: F2 "기존 프레임 불러오기" 버튼 노출(it15 F2-D6)과
    /// F1 "해당 PC에서만" 정책 배너 노출(it15 F1-D1 정정 — 기존 프레임 수정 시에만 배너를 띄운다).
    /// 편집 세션 도중에는 바뀌지 않는다(<see cref="ApplyPickedFrame"/>도 _isEditing을 건드리지 않는다).
    /// </summary>
    public bool IsCreateMode => !_isEditing;

    /// <summary>
    /// 이번 저장의 실제 결과 안내(저장 버튼 위 캡션). 상단 배너가 정책(로컬 전용)을 말하고
    /// 이 캡션이 결과(서버 등록 / fork / 덮어쓰기 / 내 프레임)를 말한다. (it15 §3.1(b))
    /// 공용 스코프에서 이름에 '_'가 있으면 비차단 경고를 덧붙인다(§3.4) — 저장 직후 안내는
    /// 화면 전환으로 사라지므로 저장 전에 보이는 이 캡션이 유일한 노출 지점이다.
    /// </summary>
    public string SaveScopeNotice
    {
        get
        {
            bool isPower = _shell.Session.CurrentUser?.Role.IsPower() == true;
            var scope = isPower
                ? _sessionSource switch
                {
                    // power 신규 생성은 it15 이후에도 공용 기본 프레임 DB 등록 경로다(배너만으론 부정확).
                    FrameSessionSource.New => $"저장 시 '{FrameName}'이(가) 공용 기본 프레임으로 서버에 등록됩니다.",
                    FrameSessionSource.ForkFromCatalog => $"원본은 그대로 두고 '{FrameName}'(으)로 이 PC의 공용 목록에 저장됩니다.",
                    _ => $"'{FrameName}'을(를) 이 PC에 덮어씁니다."
                }
                : _sessionSource == FrameSessionSource.EditOwnLocal
                    ? $"'{FrameName}'을(를) 이 PC에 덮어씁니다."
                    : $"'{FrameName}'을(를) 내 프레임으로 이 PC에 저장합니다.";

            // 공용 파일명 규약상 '_'는 user 접두 구분자다(§1.5) → 이름에 '_'가 있으면 저장은 되지만
            // LoadPublic에서 탈락해 목록에 보이지 않는다. 저장 전에 알린다(비차단).
            return isPower && FrameName.Contains('_')
                ? $"{scope} ⚠ 이름에 '_'가 있어 공용 목록에서 보이지 않을 수 있습니다."
                : scope;
        }
    }

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

    // ── it15 F2: "기존 프레임 불러오기" 선택 모달(편집기 내부 오버레이 — 새 Window 아님) ──

    /// <summary>선택 모달의 목록 VM. 확인/취소 커맨드는 이 VM이 갖는다(피커는 이벤트 0개).</summary>
    public FramePickerViewModel Picker { get; }

    /// <summary>선택 모달 오버레이 표시 여부.</summary>
    [ObservableProperty] private bool _isFramePickerVisible;

    /// <summary>목록 로딩 취소용. 재오픈 시 교체(이전 것 Dispose)하고 취소·이탈 시 Cancel.</summary>
    private CancellationTokenSource? _pickerCts;

    public FrameEditorViewModel(
        AppShellViewModel shell,
        IFrameRepository repository,
        ILocalFrameStore localStore,
        FramePickerViewModel picker,
        ILogger<FrameEditorViewModel>? logger = null)
    {
        _shell = shell;
        _repository = repository;
        _localStore = localStore;
        Picker = picker;
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
    /// 기존 프레임을 편집기로 불러온다(파워=공용/DB 프레임, user=본인 로컬). 이미지·슬롯을 그대로 로드.
    /// it15 F1: 저장은 항상 로컬 전용이며, DB/번들 유래(<see cref="FrameEditPolicy.RequiresFork"/>)면
    /// 원본을 보존하고 "{원본이름} 사본"으로 분기 저장한다(이름 제안값만 계산 — 사용자가 수정 가능).
    /// </summary>
    public void LoadForEdit(FrameTemplate frame)
    {
        _isEditing = true;
        EditorTitle = "프레임 편집";

        if (FrameEditPolicy.RequiresFork(frame))
        {
            // 카탈로그 유래(DB·번들·fallback): 원본 파일 불변 + 새 이름으로 분기 저장.
            _sessionSource = FrameSessionSource.ForkFromCatalog;
            _sourceName = frame.Name;
            FrameName = FrameNaming.NextCopyName(frame.Name, ExistingNamesForCurrentScope());
        }
        else
        {
            // 본인 로컬 프레임: 현행대로 같은 이름 덮어쓰기.
            _sessionSource = FrameSessionSource.EditOwnLocal;
            _sourceName = string.Empty;
            FrameName = frame.Name;
        }
        OnPropertyChanged(nameof(IsCreateMode));
        OnPropertyChanged(nameof(SaveScopeNotice));

        if (string.IsNullOrEmpty(frame.ImageUrl) || !File.Exists(frame.ImageUrl))
        {
            StatusMessage = "프레임 이미지를 불러올 수 없습니다(로컬 파일 없음).";
            return;
        }
        try
        {
            _imageBytes = File.ReadAllBytes(frame.ImageUrl); // 로컬 저장분은 이미 PNG(가공본)
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

    // ── it15 F2: 기존 프레임 불러오기(선택 모달 → 이미지·슬롯 메모리 복사) ──

    /// <summary>[기존 프레임 불러오기] 버튼: 오버레이를 열고 후보 목록을 비동기 로드.</summary>
    [RelayCommand]
    private async Task OpenFramePicker()
    {
        _pickerCts?.Cancel();
        _pickerCts?.Dispose();
        _pickerCts = new CancellationTokenSource();

        IsFramePickerVisible = true;
        await Picker.LoadAsync(_shell.Session.CurrentUser?.Id, _pickerCts.Token);
    }

    /// <summary>[불러오기]: 선택 프레임의 이미지·슬롯을 새 편집 세션으로 복사. 실패해도 모달만 닫고 편집 상태 보존.</summary>
    [RelayCommand]
    private void ConfirmPickFrame()
    {
        var src = Picker.SelectedFrame;
        IsFramePickerVisible = false;
        if (src is null) return; // 선택 없이 확인 → 편집기 무변경(모달만 닫힘)

        ApplyPickedFrame(src); // 실패 시 StatusMessage로 안내
        Picker.Reset();
    }

    /// <summary>[취소]: 모달만 닫는다. 편집기 상태·디스크 모두 무변경(임시 파일이 없어 정리할 것도 없다).</summary>
    [RelayCommand]
    private void CancelPickFrame()
    {
        _pickerCts?.Cancel();
        IsFramePickerVisible = false;
        Picker.Reset();
    }

    /// <summary>
    /// 선택한 프레임의 이미지·슬롯을 현재 편집 세션으로 복사한다(디스크에 아무것도 쓰지 않는다).
    /// 원본 불변: 이미지는 <see cref="LoadImage"/>가 읽기만 하고, 슬롯은 새 <see cref="Slot"/> 인스턴스로 값 복사한다.
    /// 세션 정체성은 항상 "새 프레임"(fork) — 저장 시 원본을 덮어쓰지 않는다. (it15 §4.6)
    /// </summary>
    /// <returns>복사 성공 여부(실패 사유는 <see cref="StatusMessage"/>).</returns>
    public bool ApplyPickedFrame(FrameTemplate src)
    {
        if (string.IsNullOrEmpty(src.ImageUrl) || !File.Exists(src.ImageUrl))
        {
            StatusMessage = "선택한 프레임의 이미지를 찾을 수 없습니다.";
            return false;
        }

        // 번들 프레임은 .jpg일 수 있다 → 반드시 LoadImage 경유(OpenCV 디코드 → PNG 재인코딩).
        // 부작용: _imageBytes/FrameWidth·Height 세팅(장변 4000 초과 시 축소) + ArrangeSlots()로 자동 배치.
        if (!LoadImage(src.ImageUrl)) return false;

        // 원본 슬롯을 축소 배율로 보정해 복사(자동 배치 결과를 덮어씀).
        double scale = src.ImageSize.Width > 0 ? (double)FrameWidth / src.ImageSize.Width : 0;
        if (src.Slots.Count > 0 && scale > 0)
        {
            _suppressArrange = true;
            SlotCount = Math.Clamp(src.Slots.Count, SlotCountOptions[0], SlotCountOptions[^1]);
            _suppressArrange = false;

            _baseSlots.Clear();
            foreach (var s in src.Slots)
            {
                _baseSlots.Add(SlotLayout.ClampToFrame(
                    new Slot
                    {
                        Index = s.Index,
                        X = (int)Math.Round(s.X * scale),
                        Y = (int)Math.Round(s.Y * scale),
                        Width = (int)Math.Round(s.Width * scale),
                        Height = (int)Math.Round(s.Height * scale)
                    }, FrameWidth, FrameHeight));
            }
            SlotScalePercent = 100;
            ApplyScale();
        }
        // src.ImageSize가 0(메타 없음)이면 LoadImage의 자동 배치 결과를 그대로 사용.

        // 세션 정체성 — 항상 "새 프레임"(_isEditing 불변 → IsCreateMode·EditorTitle 유지).
        _sessionSource = FrameSessionSource.ForkFromCatalog;
        _sourceName = src.Name;

        FrameName = FrameNaming.NextCopyName(src.Name, ExistingNamesForCurrentScope());
        OnPropertyChanged(nameof(SaveScopeNotice));

        StatusMessage = string.Empty;
        return true;
    }

    /// <summary>
    /// 현재 저장 스코프의 기존 프레임 이름들(사본 이름 충돌 검사용).
    /// power=공용 목록, user=본인 개인 목록. 조회 실패는 비차단(충돌 검사만 생략).
    /// </summary>
    private IEnumerable<string> ExistingNamesForCurrentScope()
    {
        var user = _shell.Session.CurrentUser;
        if (user is null) return Array.Empty<string>();
        try
        {
            return user.Role.IsPower()
                ? _localStore.PublicFrameNames()
                : _localStore.LoadUser(user.Id).Select(f => f.Name).ToList();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "기존 프레임 이름 조회 실패 — 사본 이름 충돌 검사를 생략");
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// 저장. it15 F1: 서버 업데이트 경로가 없다(확인 팝업 없이 한 번에 끝난다).
    /// 스코프 = power 공용 / user 개인(현행 유지), 방식 = fork(새 이름) / 덮어쓰기.
    /// power 신규 생성만 DB에 등록되고(공용 기본 프레임 배포의 유일한 경로) 나머지는 로컬 전용이다. (§3.6)
    /// </summary>
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

        bool isPower = user.Role.IsPower();
        bool isFork = _sessionSource == FrameSessionSource.ForkFromCatalog;
        bool isNew = _sessionSource == FrameSessionSource.New;

        // 원본 덮어쓰기 가드: 공용 스코프(power)에서는 사본이 원본 파일과 같은 이름이 될 수 있으므로 차단.
        // user 스코프는 파일명이 `{계정}_{이름}`이라 공용 원본과 물리적으로 겹치지 않는다(가드 불필요).
        if (isFork && isPower && string.Equals(FrameName, _sourceName, StringComparison.Ordinal))
        {
            StatusMessage = "원본과 같은 이름은 사용할 수 없습니다. 이름을 변경해 주세요.";
            return;
        }

        try
        {
            StatusMessage = "저장 중...";

            if (isPower && isNew)
            {
                // 파워 신규 생성: 공용 기본 프레임 DB 등록(isDefault=true, userId=null) + 로컬 캐시(#dbid 기록).
                var frame = new FrameTemplate
                {
                    Id = string.Empty, // SaveAsync가 새 GUID 부여
                    UserId = null,
                    IsDefault = true,
                    Name = FrameName,
                    ImageSize = new ImageSize { Width = FrameWidth, Height = FrameHeight },
                    Slots = Slots.ToList()
                };
                var saved = await _repository.SaveAsync(frame, _imageBytes);
                _localStore.SaveLocal(saved, _imageBytes, ownerName: null);
            }
            else if (isPower)
            {
                // 파워 fork / 파워 자기 로컬 편집: 로컬 공용만. Id=""로 두면 #dbid를 기록하지 않아
                // 서버 문서와 연결이 끊긴다(= 편집은 이 PC에만 적용). (§3.3)
                var frame = new FrameTemplate
                {
                    Id = string.Empty,
                    UserId = null,
                    IsDefault = true,
                    Name = FrameName,
                    ImageSize = new ImageSize { Width = FrameWidth, Height = FrameHeight },
                    Slots = Slots.ToList()
                };
                _localStore.SaveLocal(frame, _imageBytes, ownerName: null);
            }
            else
            {
                // user 전 케이스(신규·fork·자기 로컬 편집): 개인 로컬 `{계정}_{이름}.png`. DB 미호출.
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

            // '_' 이름 경고는 저장 전에 SaveScopeNotice가 이미 안내한다 — 저장 직후 StatusMessage는
            // 곧바로 화면이 전환되어 읽을 수 없으므로 여기서 띄우지 않는다(§3.4).
            StatusMessage = string.Empty;

            await GoToFrameSelectAsync();
        }
        catch (InvalidOperationException ex)
        {
            // 10개 초과 등
            StatusMessage = ex.Message;
        }
        catch (IOException ex)
        {
            // 이름에 파일시스템 금지문자(LocalFrameStore.EnsureFileNameSafe) — 이유를 그대로 알린다.
            StatusMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "프레임 저장 실패");
            StatusMessage = "저장에 실패했습니다.";
        }
    }

    /// <summary>저장 성공 후 프레임 선택 화면으로 전환. 전환 자체 실패는 저장 결과에 영향 없음(안내만 로그).</summary>
    private async Task GoToFrameSelectAsync()
    {
        try { await _shell.NavigateAsync(AppState.FrameSelect); }
        catch (Exception ex) { _logger?.LogError(ex, "프레임 선택 화면 전환 실패(저장은 완료)"); }
    }

    [RelayCommand]
    private async Task Cancel() => await _shell.NavigateAsync(AppState.FrameSelect);
}
