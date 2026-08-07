using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MCPhoto.App.ViewModels;
using MCPhoto.Core.Frames;
using MCPhoto.Core.Models;
using Microsoft.Win32;

namespace MCPhoto.App.Views;

/// <summary>
/// 프레임 편집기. 슬롯을 Canvas에 렌더하고 드래그로 이동. 프레임 좌표 ↔ 캔버스 좌표는
/// 순수 함수 <see cref="EditorTransform"/>로 통일(표시·드래그·클램프 동일 변환 → WYSIWYG). (it4 §2)
/// </summary>
public partial class FrameEditorView : UserControl
{
    private FrameEditorViewModel? _vm;
    private Rectangle? _dragTarget;
    private int _dragIndex = -1;

    // 드래그 시작 시 슬롯 내 클릭 지점(프레임 좌표 오프셋). 절대 위치 이동의 그랩 포인트.
    private double _grabOffsetX, _grabOffsetY;

    public FrameEditorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        SizeChanged += (_, _) => RedrawSlots();
        // 슬롯 키보드 이동(설계 §12). PreviewKeyDown이라야 Canvas가 방향키를 포커스 이동으로 먹기 전에 가로챈다.
        PreviewKeyDown += OnEditorPreviewKeyDown;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
            _vm.Slots.CollectionChanged -= OnSlotsChanged;
        _vm = DataContext as FrameEditorViewModel;
        if (_vm is not null)
        {
            _vm.Slots.CollectionChanged += OnSlotsChanged;
            _vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(FrameEditorViewModel.FrameImage))
                    Dispatcher.BeginInvoke(RedrawSlots);
            };
        }
    }

    private void OnSlotsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RedrawSlots();

    private async void OnLoadImage(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var dlg = new OpenFileDialog
        {
            Filter = "이미지 (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg"
        };
        if (dlg.ShowDialog() == true)
        {
            _vm.LoadImage(dlg.FileName);
            await Task.Yield();
            RedrawSlots();
        }
    }

    // ── 프레임 좌표 ↔ 캔버스 좌표 변환 ──

    // 슬롯을 실제로 그리는 SlotCanvas가 캔버스 좌표계의 기준(Image ActualWidth 의존 제거, it4 §2.3).
    private EditorTransform GetTransform()
        => _vm is null
            ? default
            : EditorTransform.Compute(SlotCanvas.ActualWidth, SlotCanvas.ActualHeight, _vm.FrameWidth, _vm.FrameHeight);

    private void RedrawSlots()
    {
        SlotCanvas.Children.Clear();
        if (_vm is null || _vm.FrameImage is null) return;

        var tf = GetTransform();
        if (!tf.IsValid) return; // 크기 0/미확정 — 다음 SizeChanged 패스에서 재그리기

        for (int i = 0; i < _vm.Slots.Count; i++)
        {
            var slot = _vm.Slots[i];
            bool selected = i == _vm.SelectedSlotIndex;   // 방향키가 움직일 대상을 눈으로 알려준다
            var rect = new Rectangle
            {
                Width = slot.Width * tf.Scale,
                Height = slot.Height * tf.Scale,
                Stroke = new SolidColorBrush(selected
                    ? Color.FromRgb(0xFF, 0x4D, 0x79)     // 선택: 로즈 강조
                    : Color.FromRgb(0xC4, 0x4B, 0x9B)),
                StrokeThickness = selected ? 4 : 2,
                Fill = new SolidColorBrush(selected
                    ? Color.FromArgb(0x55, 0xFF, 0x4D, 0x79)
                    : Color.FromArgb(0x33, 0xC4, 0x4B, 0x9B)),
                Tag = i,
                Cursor = Cursors.SizeAll
            };
            var (cx, cy) = tf.FrameToCanvas(slot.X, slot.Y);
            Canvas.SetLeft(rect, cx);
            Canvas.SetTop(rect, cy);
            rect.MouseLeftButtonDown += OnSlotMouseDown;
            rect.MouseMove += OnSlotMouseMove;
            rect.MouseLeftButtonUp += OnSlotMouseUp;
            SlotCanvas.Children.Add(rect);
        }
    }

    private void OnSlotMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Rectangle rect || _vm is null) return;
        var tf = GetTransform();
        if (!tf.IsValid) return;

        _dragTarget = rect;
        _dragIndex = (int)rect.Tag!;

        // 클릭한 슬롯을 키보드 이동 대상으로 삼고, 캔버스가 방향키를 받도록 포커스를 가져온다.
        _vm.SelectedSlotIndex = _dragIndex;
        SlotCanvas.Focus();
        RedrawSlots();

        // 그랩 오프셋 = 클릭한 프레임 좌표 − 슬롯 좌상단(프레임 좌표). 이동 내내 고정.
        var pos = e.GetPosition(SlotCanvas);
        var (fx, fy) = tf.CanvasToFrame(pos.X, pos.Y);
        var slot = _vm.Slots[_dragIndex];
        _grabOffsetX = fx - slot.X;
        _grabOffsetY = fy - slot.Y;

        rect.CaptureMouse();
        e.Handled = true;
    }

    private void OnSlotMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragTarget is null || _dragIndex < 0 || _vm is null) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var tf = GetTransform();
        if (!tf.IsValid) return;

        // 절대 위치: 현재 마우스의 프레임 좌표에서 그랩 오프셋을 빼 슬롯 좌상단을 산출(델타 누적·정수 절삭 없음).
        var pos = e.GetPosition(SlotCanvas);
        var (fx, fy) = tf.CanvasToFrame(pos.X, pos.Y);
        var slot = _vm.Slots[_dragIndex];
        _vm.UpdateSlot(_dragIndex,
            (int)Math.Round(fx - _grabOffsetX),
            (int)Math.Round(fy - _grabOffsetY),
            slot.Width, slot.Height);
        RedrawSlots();
    }

    private void OnSlotMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragTarget is not null)
        {
            _dragTarget.ReleaseMouseCapture();
            _dragTarget = null;
            _dragIndex = -1;
        }
    }

    /// <summary>
    /// 슬롯 키보드 이동(설계 §12). 방향키 1px, Shift+방향키 10px, Tab/Shift+Tab으로 대상 전환.
    /// <para>
    /// ⚠️ <b>텍스트 입력 중에는 개입하지 않는다.</b> 프레임 이름 <c>TextBox</c>에 포커스가 있을 때
    /// 방향키는 커서 이동이어야 한다 — 이 가드가 없으면 이름을 고치는 동안 슬롯이 움직인다.
    /// </para>
    /// 크기는 바꾸지 않는다(요구: "크기는 일관되게"). 경계 클램프는 VM의 UpdateSlot이 처리한다.
    /// </summary>
    private void OnEditorPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_vm is null) return;
        if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase) return;

        bool fast = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

        if (e.Key == Key.Tab)
        {
            if (!_vm.SelectAdjacentSlot(backward: fast)) return;
            SlotCanvas.Focus();
            RedrawSlots();
            e.Handled = true;   // 컨트롤 간 포커스 이동 대신 슬롯 전환으로 소비
            return;
        }

        int step = fast ? FrameEditorViewModel.NudgeStepFast : FrameEditorViewModel.NudgeStep;
        var (dx, dy) = e.Key switch
        {
            Key.Left => (-step, 0),
            Key.Right => (step, 0),
            Key.Up => (0, -step),
            Key.Down => (0, step),
            _ => (0, 0)
        };
        if (dx == 0 && dy == 0) return;

        if (!_vm.NudgeSelectedSlot(dx, dy)) return;   // 선택이 없으면 기본 동작에 맡긴다
        RedrawSlots();
        e.Handled = true;
    }
}
