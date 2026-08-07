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
    /// 이 캡션이 결과(공용 목록 생성 / fork / 덮어쓰기 / 내 프레임)를 말한다. (it15 §3.1(b))
    /// 파워 신규 생성의 서버 등록 여부는 저장 시 확인 팝업에서 선택하므로 여기서 단정하지 않는다(R2).
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
                    // power 신규 생성은 공용 기본 프레임 DB 등록이 가능한 유일한 경로지만, R2 이후 서버 등록은
                    // 저장 시 확인 팝업의 체크박스에 달려 있다 → 여기서 "등록됩니다"로 단정하지 않는다.
                    FrameSessionSource.New =>
                        $"저장 시 '{FrameName}'을(를) 이 PC의 공용 목록에 만듭니다. 서버 등록 여부는 저장할 때 선택합니다.",
                    FrameSessionSource.ForkFromCatalog => $"원본은 그대로 두고 '{FrameName}'(으)로 이 PC의 공용 목록에 저장됩니다.",
                    _ => $"'{FrameName}'을(를) 이 PC에 덮어씁니다."
                }
                : _sessionSource == FrameSessionSource.EditOwnLocal
                    ? $"'{FrameName}'을(를) 이 PC에 덮어씁니다."
                    : $"'{FrameName}'을(를) 내 프레임으로 이 PC에 저장합니다.";

            // 로컬 접두 규약은 폐지됐지만(설계 D-3) **서버가 여전히 '_'를 거부**한다
            // (validateFrameName — 웹·모바일이 아직 접두 규약을 쓰기 때문, 설계 §9). 저장 전에 알린다(비차단).
            return isPower && FrameName.Contains('_')
                ? $"{scope} ⚠ 이름에 '_'가 있으면 서버 저장이 거부됩니다."
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

    /// <summary>
    /// F2로 불러온 원본 안내(이름 입력 필드 위 캡션). "사본이 아니라 새 프레임을 만드는 중"이라는 유일한 시각 신호다.
    /// 불러오기가 성공한 순간에만 채워지고, 이미지를 다시 직접 불러오면(<see cref="LoadImage"/>) 비운다.
    /// </summary>
    [NotifyPropertyChangedFor(nameof(HasPickedSource))]
    [ObservableProperty] private string _pickedSourceNotice = string.Empty;

    /// <summary>불러온 원본 캡션 노출 게이트(문자열→Visibility 컨버터가 없어 bool로 노출한다).</summary>
    public bool HasPickedSource => !string.IsNullOrEmpty(PickedSourceNotice);

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
            StatusMessage = "프레임 이미지는 8MB 이하여야 합니다.";
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
            // 이미지를 직접 교체하면 "'{X}'의 이미지·슬롯을 불러왔습니다" 안내가 사실과 어긋난다 → 비운다.
            // (ApplyPickedFrame은 이 메서드를 경유한 뒤 자기 안내를 다시 설정한다 — 순서상 안전하다.)
            PickedSourceNotice = string.Empty;
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

        // 수정 폐지(D-16) 이후 이 경로는 "기존 프레임 불러오기"로 들어온 것뿐이며 항상 새로 만든다.
        // 카탈로그 유래(DB·번들·fallback)는 원본을 보존해야 하므로 fork 이름을 제안한다.
        if (FrameOrigin.Classify(frame) != FrameOriginKind.UserLocal)
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
    // ── 슬롯 키보드 이동(설계 §12) ──

    /// <summary>방향키 1회 이동량(px).</summary>
    public const int NudgeStep = 1;

    /// <summary>Shift+방향키 1회 이동량(px). 마우스로 잡기 힘든 미세 조정과 큰 이동을 함께 지원한다.</summary>
    public const int NudgeStepFast = 10;

    /// <summary>
    /// 키보드로 옮길 대상 슬롯(−1=미선택). 클릭 또는 Tab으로 바뀐다.
    /// <para>
    /// 종전에는 드래그 중에만 유효한 <c>_dragIndex</c>뿐이라 마우스를 놓으면 대상이 사라졌다 —
    /// 방향키로 옮기려면 "지금 무엇을 옮기는지"가 유지돼야 한다.
    /// </para>
    /// </summary>
    [ObservableProperty] private int _selectedSlotIndex = -1;

    /// <summary>선택 슬롯을 상대 이동. 경계 클램프는 <see cref="UpdateSlot"/>이 처리한다.</summary>
    /// <returns>실제로 이동했으면 true(선택이 없거나 범위 밖이면 false).</returns>
    public bool NudgeSelectedSlot(int dx, int dy)
    {
        if (SelectedSlotIndex < 0 || SelectedSlotIndex >= Slots.Count) return false;

        var s = Slots[SelectedSlotIndex];
        // ⚠️ 크기는 건드리지 않는다(요구: "크기는 일관되게"). 겹침은 드래그와 같이 허용하고
        //    최종 검증은 저장 시점의 SlotLayout.IsValid가 맡는다 — 두 조작의 규칙을 갈라놓지 않는다.
        UpdateSlot(SelectedSlotIndex, s.X + dx, s.Y + dy, s.Width, s.Height);
        return true;
    }

    /// <summary>Tab 순환으로 선택 슬롯 전환. 슬롯이 없으면 아무 일도 하지 않는다.</summary>
    /// <param name="backward">Shift+Tab이면 true(역방향).</param>
    public bool SelectAdjacentSlot(bool backward)
    {
        if (Slots.Count == 0) { SelectedSlotIndex = -1; return false; }

        int next = SelectedSlotIndex < 0
            ? (backward ? Slots.Count - 1 : 0)
            : (SelectedSlotIndex + (backward ? -1 : 1) + Slots.Count) % Slots.Count;

        SelectedSlotIndex = next;
        return true;
    }

    /// <summary>슬롯 목록이 바뀐 뒤 선택이 범위를 벗어났으면 해제한다(개수 변경·이미지 교체).</summary>
    public void ClampSlotSelection()
    {
        if (SelectedSlotIndex >= Slots.Count) SelectedSlotIndex = -1;
    }

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
        // D-23: power는 공용까지, advanced_user는 본인 프레임만 후보로 본다.
        var picker = _shell.Session.CurrentUser;
        await Picker.LoadAsync(
            picker?.Id, picker?.Email, includePublic: picker?.Role.IsPower() == true, _pickerCts.Token);
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
    /// 세션 정체성 = 신규 생성(<see cref="FrameSessionSource.New"/>) — 이름은 사용자가 정한다(사본 자동 네이밍 없음, R1).
    /// 저장 시 원본을 덮어쓰지 않는 방어는 이름 충돌 가드(<see cref="TryValidateForSave"/> ⑦)가 담당한다.
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

        // 세션 정체성 = 신규 생성(_isEditing 불변 → IsCreateMode·EditorTitle 유지).
        // R1: 사본(fork)이 아니라 "불러온 정보를 기본값으로 한 새 프레임"이다 → FrameName을 건드리지 않는다.
        // 사용자가 이미 타이핑한 이름을 보존하고(먼저 이름 → 나중에 이미지 순서도 지원), 원본 이름을 그대로
        // 채우면 이름 충돌 가드에 100% 걸리는 값을 제안하는 셈이 되므로 제안하지 않는다.
        _sessionSource = FrameSessionSource.New;
        _sourceName = src.Name; // 추적·안내용(New 세션이라 원본 이름 가드는 발동하지 않는다)

        PickedSourceNotice = $"'{src.Name}'의 이미지·슬롯을 불러왔습니다. 새 프레임 이름을 입력해 주세요.";
        OnPropertyChanged(nameof(SaveScopeNotice));

        StatusMessage = string.Empty;
        return true;
    }

    /// <summary>
    /// <b>현재 사용자에게 보이는</b> 프레임 이름 전부(공용 + 본인 개인). 사본 이름 계산·저장 전 충돌 검사용.
    /// <para>
    /// 설계 D-17: 판정 집합은 스코프별이 아니라 "보이는 것 전부"다 — 목록에서 같은 이름 둘이 보이면
    /// 사용자가 구분할 수 없기 때문이다. 다른 계정의 개인 프레임과는 겹쳐도 된다(폴더가 다르고 보이지도 않는다).
    /// </para>
    /// ⚠️ 개인 프레임 조회 키는 <b>이메일</b>이다(계정 id 아님 — 소유 판정 단일 기준, D-4).
    /// 조회 실패는 비차단(충돌 검사만 생략)이며, 서버가 최종적으로 409로 거부한다(S8).
    /// </summary>
    private IEnumerable<string> ExistingNamesForCurrentScope()
    {
        try
        {
            var names = new List<string>(_localStore.PublicFrameNames());

            var email = _shell.Session.CurrentUser?.Email;
            if (!string.IsNullOrWhiteSpace(email))
                names.AddRange(_localStore.UserFrameNames(email!));

            return names;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "기존 프레임 이름 조회 실패 — 이름 충돌 검사를 생략");
            return Array.Empty<string>();
        }
    }

    // ── R2: 서버 등록 확인 팝업(파워 신규 생성 저장 시 — 프레임 삭제 확인 팝업과 동일 패턴) ──

    /// <summary>서버 등록 확인 오버레이 표시 여부(새 Window 아님 — VM 상태 + 오버레이).</summary>
    [ObservableProperty] private bool _isServerRegisterConfirmVisible;

    /// <summary>
    /// 저장 스코프 선택(설계 D-21). <b>기본은 미선택</b>이며 고르기 전에는 [저장]이 비활성이다.
    /// <para>
    /// 종전에는 "서버에도 등록" 체크박스 하나였고 <b>기본 on</b>이었다. 체크박스는 "선택 안 함" 상태를
    /// 표현할 수 없어 강제 선택이 불가능했고, 기본 on이라 무심코 [저장]을 누르면 공용으로 배포됐다 —
    /// 공용은 게스트를 포함한 전원에게 노출되는 되돌리기 어려운 작업이라 명시적 선택을 요구한다.
    /// </para>
    /// </summary>
    public enum FrameSaveScope
    {
        /// <summary>아직 고르지 않음. [저장] 비활성.</summary>
        None,

        /// <summary>개인 프레임(권장) — 본인에게만 보인다.</summary>
        Personal,

        /// <summary>서버 공용 프레임 — 모든 사용자·게스트에게 노출된다. power만 선택 가능.</summary>
        PublicServer
    }

    /// <summary>선택된 저장 스코프. 팝업을 열 때마다 <see cref="FrameSaveScope.None"/>으로 리셋한다.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPersonalScope))]
    [NotifyPropertyChangedFor(nameof(IsPublicScope))]
    [NotifyPropertyChangedFor(nameof(CanConfirmSaveScope))]
    private FrameSaveScope _saveScope = FrameSaveScope.None;

    /// <summary>라디오 바인딩(개인). 값 기반 바인딩 컨버터 없이 두 bool로 노출한다.</summary>
    public bool IsPersonalScope
    {
        get => SaveScope == FrameSaveScope.Personal;
        set { if (value) SaveScope = FrameSaveScope.Personal; }
    }

    /// <summary>라디오 바인딩(공용).</summary>
    public bool IsPublicScope
    {
        get => SaveScope == FrameSaveScope.PublicServer;
        set { if (value) SaveScope = FrameSaveScope.PublicServer; }
    }

    /// <summary>[저장] 활성 조건 — 스코프를 골라야 한다(D-21).</summary>
    public bool CanConfirmSaveScope => SaveScope != FrameSaveScope.None;

    /// <summary>
    /// 확인 팝업을 띄워야 하는 세션인지 = **DB insert 분기와 완전히 동일한 조건**.
    /// 두 축이 갈라지면 "팝업은 떴는데 등록은 안 되는" 조용한 불일치가 생기므로 <c>IsCreateMode</c>가 아니라
    /// 세션 축을 쓴다(현재 두 값은 동치이지만 동치에 의존하지 않는다).
    /// 권한 축은 <c>IsPower()</c>만 쓴다 — <c>CanWriteFrames()</c>(AdvancedUser 포함)로 대체하면
    /// DB 권한이 없는 계정에 서버 등록 체크박스를 노출한다(UserRole.cs 명시 경고).
    /// </summary>
    private bool RequiresServerRegisterPrompt
        => _shell.Session.CurrentUser?.Role.IsPower() == true && _sessionSource == FrameSessionSource.New;

    /// <summary>
    /// 저장 전 검증을 한 곳으로 모은다(fail-closed). 진입점이 [저장] 커맨드와 서버 등록 확인 팝업 2개이므로
    /// 양쪽에서 같은 판정을 재실행해야 우회가 생기지 않는다.
    /// 순서 고정: ①로그인 → ②권한 → ③슬롯 유효성 → ④원본 이름 → ⑤빈 이름 → ⑥금지문자 → ⑦스코프 충돌.
    /// ④를 ⑦보다 먼저 두는 이유: ④는 원본 이름이라는 확정 사실만 보고 판정하는 반면 ⑦은 디스크 열거에
    /// 의존해 실패 시 조용히 꺼진다(비차단, 빈 집합) → ④가 남아 있어야 2중 방어가 성립하고, 기존 fork
    /// 회귀 테스트가 검증하는 "원본과 같은 이름" 문구도 ⑦ 문구로 뒤바뀌지 않는다.
    /// </summary>
    /// <param name="error">차단 사유(통과 시 빈 문자열). 호출자가 <see cref="StatusMessage"/>에 그대로 넣는다.</param>
    private bool TryValidateForSave(out string error)
    {
        var user = _shell.Session.CurrentUser;
        if (user is null) { error = "로그인이 필요합니다."; return false; }
        // it16 §4.5 3중 방어(fail-closed): 화면 게이트(CanCreateFrame·CanEditSelected)로 편집기에 도달할 수 없는
        // 역할이지만, 미래에 다른 진입점이 생겨도 **저장이 거부**되도록 정책을 저장 경로에도 둔다.
        if (!user.Role.CanWriteFrames()) { error = "프레임을 만들 권한이 없습니다."; return false; }
        if (_imageBytes is null || !SlotLayout.IsValid(Slots, FrameWidth, FrameHeight))
        {
            error = "슬롯이 겹치거나 프레임을 벗어났습니다.";
            return false;
        }

        bool isPower = user.Role.IsPower();
        bool isFork = _sessionSource == FrameSessionSource.ForkFromCatalog;

        // ④ 원본 덮어쓰기 가드: 공용 스코프(power)에서는 사본이 원본 파일과 같은 이름이 될 수 있으므로 차단.
        // user 스코프는 파일명이 `{계정}_{이름}`이라 공용 원본과 물리적으로 겹치지 않는다(가드 불필요).
        if (isFork && isPower && string.Equals(FrameName, _sourceName, StringComparison.Ordinal))
        {
            error = "원본과 같은 이름은 사용할 수 없습니다. 이름을 변경해 주세요.";
            return false;
        }

        // ⑤⑥ 이름 안전성 선검증: LocalFrameStore가 IOException으로 거부하는 조건을 저장 전에 걸러낸다.
        // 파워 신규 생성은 서버 insert가 먼저이므로, 이 검증 없이는 "서버에만 문서가 남는 반쪽 상태"가
        // 가능하다(D6 원자성과 짝). 판정은 LocalFrameStore와 같은 순수 함수를 쓴다.
        if (string.IsNullOrWhiteSpace(FrameName)) { error = "프레임 이름을 입력해 주세요."; return false; }
        if (!FrameNaming.IsFileNameSafe(FrameName))
        {
            error = "이름에 사용할 수 없는 문자가 있습니다.";
            return false;
        }

        // ⑦ 이름 충돌 가드(설계 D-17): 본인에게 보이는 프레임과 이름이 겹치면 차단한다.
        // 저장은 같은 이름 파일을 경고 없이 덮어쓰므로 이 가드가 데이터 손실의 마지막 방어선이다.
        // 대소문자 무시 — Windows 파일시스템이 "Abc"와 "abc"를 같은 파일로 본다.
        // ⚠️ 클라 검증은 즉시 피드백일 뿐이다. PC 두 대에서 동시에 같은 이름을 만드는 경우는
        //    서버가 409로 막는다(S8) — 그 응답도 저장 실패로 사용자에게 그대로 노출된다.
        if (_sessionSource != FrameSessionSource.EditOwnLocal
            && !FrameNaming.IsNameAvailable(FrameName, ExistingNamesForCurrentScope()))
        {
            error = "이미 같은 이름의 프레임이 있습니다. 다른 이름을 입력해 주세요.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>
    /// [저장] 버튼: 검증 → (파워 신규 생성이면) 서버 등록 확인 팝업 → 그 밖에는 즉시 로컬 저장.
    /// it15 F1: 서버 업데이트 경로가 없다(편집은 로컬 전용).
    /// 스코프 = power 공용 / user 개인(현행 유지), 방식 = fork(새 이름) / 덮어쓰기.
    /// power 신규 생성만 DB 등록 경로이며(공용 기본 프레임 배포의 유일한 경로) 나머지는 로컬 전용이다. (§3.6)
    /// R2: 그 DB 등록조차 확인 팝업의 체크박스가 켜진 경우에만 수행한다.
    /// </summary>
    [RelayCommand]
    private async Task Save()
    {
        if (!TryValidateForSave(out var error)) { StatusMessage = error; return; }

        // R2: DB insert가 가능한 세션이면 "서버에도 만들지" 먼저 묻는다 — 이 시점에는 아무것도 저장하지 않는다.
        if (RequiresServerRegisterPrompt)
        {
            SaveScope = FrameSaveScope.None;   // D-21: 열 때마다 미선택으로 리셋(직전 선택 잔존 금지)
            IsServerRegisterConfirmVisible = true;
            return;
        }

        await PersistAsync(registerToServer: false);
    }

    /// <summary>
    /// [팝업 저장]: 체크 상태를 **닫히기 전에** 지역 변수로 확정한 뒤 저장한다(삭제 확인 팝업과 같은 관례 —
    /// 리셋이 먼저 일어나면 체크가 조용히 무시된다).
    /// </summary>
    [RelayCommand]
    private async Task ConfirmServerRegister()
    {
        if (!CanConfirmSaveScope) return;          // 방어: 미선택이면 저장하지 않는다(버튼도 비활성)

        var toPublic = SaveScope == FrameSaveScope.PublicServer;
        IsServerRegisterConfirmVisible = false;
        SaveScope = FrameSaveScope.None;
        await PersistAsync(toPublic);
    }

    /// <summary>[팝업 취소]: 팝업만 닫는다. 저장·화면 전환·디스크 모두 무변경(편집 세션 그대로 유지).</summary>
    [RelayCommand]
    private void CancelServerRegister()
    {
        IsServerRegisterConfirmVisible = false;
        SaveScope = FrameSaveScope.None;
    }

    /// <summary>
    /// 실제 저장. 진입점이 [저장] 커맨드와 확인 팝업 2개이므로 첫 줄에서 검증을 **재실행**한다(fail-closed).
    /// </summary>
    /// <param name="registerToServer">서버(DB) 공용 기본 프레임으로도 등록할지. 파워 신규 생성에서만 의미가 있다.</param>
    private async Task PersistAsync(bool registerToServer)
    {
        if (!TryValidateForSave(out var error)) { StatusMessage = error; return; }

        // 검증이 보장한 값을 지역 변수로 확정한다(재확인 자체가 fail-closed 방어이자 null 흐름의 근거).
        var user = _shell.Session.CurrentUser;
        var png = _imageBytes;
        if (user is null || png is null) { StatusMessage = "저장할 수 없습니다."; return; }

        bool isPower = user.Role.IsPower();
        bool isNew = _sessionSource == FrameSessionSource.New;

        try
        {
            StatusMessage = "저장 중...";

            if (isPower && isNew && registerToServer)
            {
                // 파워 신규 생성 + 체크 on: 공용 기본 프레임 DB 등록(isDefault=true, userId=null) + 로컬 캐시(#dbid 기록).
                var frame = new FrameTemplate
                {
                    Id = string.Empty, // SaveAsync가 새 GUID 부여
                    UserId = null,
                    IsDefault = true,
                    Name = FrameName,
                    ImageSize = new ImageSize { Width = FrameWidth, Height = FrameHeight },
                    Slots = Slots.ToList()
                };

                FrameTemplate saved;
                try
                {
                    saved = await _repository.SaveAsync(frame, png);
                }
                catch (Exception ex)
                {
                    // D6 원자성: 서버 등록이 실패하면 로컬 저장도 화면 전환도 하지 않는다(부분 성공 금지).
                    // 로컬만 저장해두면 재시도 시 이름 충돌 가드가 자기 자신과 충돌해 저장을 막는다.
                    // 편집 세션(이미지·슬롯·이름·배율)이 그대로 남으므로 체크만 해제해 즉시 로컬 저장할 수 있다.
                    _logger?.LogError(ex, "프레임 서버 등록 실패: {Name}", FrameName);
                    StatusMessage = $"서버 등록 실패: {ex.Message} 이 PC에만 저장하려면 '서버에도 등록'을 해제하고 다시 저장해 주세요.";
                    return;
                }

                // 공용 캐시(#owner=default) + #dbid 기록 → 삭제 동기화 대조 키(설계 §10).
                _localStore.SaveDefaultFrame(saved, png, dbId: saved.Id);
            }
            else
            {
                // 개인 프레임: 서버가 정본이다(설계 D-7). POST /frames/mine → 로컬은 캐시로 기록한다.
                // 서버가 userId·isDefault를 강제하므로 클라가 소유자를 지정하지 않는다.
                var ownerEmail = user.Email;
                if (string.IsNullOrWhiteSpace(ownerEmail))
                {
                    // SSO 계정은 항상 이메일을 갖지만, 없으면 소유자를 특정할 수 없어 저장하지 않는다.
                    StatusMessage = "계정 이메일을 확인할 수 없어 저장할 수 없습니다. 다시 로그인해 주세요.";
                    return;
                }

                var frame = new FrameTemplate
                {
                    Id = string.Empty,          // 서버가 새 문서 id 부여
                    UserId = null,              // 서버가 principal로 강제
                    IsDefault = false,
                    Name = FrameName,
                    ImageSize = new ImageSize { Width = FrameWidth, Height = FrameHeight },
                    Slots = Slots.ToList()
                };

                FrameTemplate savedMine;
                try
                {
                    savedMine = await _repository.SaveMineAsync(frame, png);
                }
                catch (Exception ex)
                {
                    // 원자성: 서버 저장이 실패하면 로컬에도 남기지 않는다(부분 성공 금지).
                    // 로컬만 저장해두면 이름 충돌 가드가 자기 자신과 충돌해 재시도를 막는다.
                    _logger?.LogError(ex, "개인 프레임 서버 저장 실패: {Name}", FrameName);
                    StatusMessage = $"저장 실패: {ex.Message}";
                    return;
                }

                _localStore.SaveUserFrame(savedMine, png, ownerEmail!, dbId: savedMine.Id);
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
