using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MCPhoto.App.ViewModels;
using MCPhoto.Core.Models;
using Microsoft.Win32;

namespace MCPhoto.App.Views;

/// <summary>
/// 프레임 편집기. 슬롯을 Canvas에 렌더하고 드래그로 이동. 프레임 좌표 ↔ 화면 좌표 스케일 변환. (WBS Step 10)
/// </summary>
public partial class FrameEditorView : UserControl
{
    private FrameEditorViewModel? _vm;
    private Rectangle? _dragTarget;
    private int _dragIndex = -1;
    private Point _dragStart;
    private double _origSlotX, _origSlotY;

    public FrameEditorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        SizeChanged += (_, _) => RedrawSlots();
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

    // ── 프레임 좌표 ↔ 화면 좌표 변환 ──

    private (double scale, double offsetX, double offsetY) GetTransform()
    {
        if (_vm is null || _vm.FrameWidth <= 0 || _vm.FrameHeight <= 0)
            return (1, 0, 0);

        // Uniform 스케일된 이미지의 실제 표시 영역 계산(Image margin 16 반영)
        double areaW = FramePreview.ActualWidth;
        double areaH = FramePreview.ActualHeight;
        if (areaW <= 0 || areaH <= 0) return (1, 0, 0);

        double scale = Math.Min(areaW / _vm.FrameWidth, areaH / _vm.FrameHeight);
        double dispW = _vm.FrameWidth * scale;
        double dispH = _vm.FrameHeight * scale;
        double offsetX = FramePreview.Margin.Left + (areaW - dispW) / 2;
        double offsetY = FramePreview.Margin.Top + (areaH - dispH) / 2;
        return (scale, offsetX, offsetY);
    }

    private void RedrawSlots()
    {
        SlotCanvas.Children.Clear();
        if (_vm is null || _vm.FrameImage is null) return;

        var (scale, ox, oy) = GetTransform();
        for (int i = 0; i < _vm.Slots.Count; i++)
        {
            var slot = _vm.Slots[i];
            var rect = new Rectangle
            {
                Width = slot.Width * scale,
                Height = slot.Height * scale,
                Stroke = new SolidColorBrush(Color.FromRgb(0xC4, 0x4B, 0x9B)),
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb(0x33, 0xC4, 0x4B, 0x9B)),
                Tag = i,
                Cursor = Cursors.SizeAll
            };
            Canvas.SetLeft(rect, ox + slot.X * scale);
            Canvas.SetTop(rect, oy + slot.Y * scale);
            rect.MouseLeftButtonDown += OnSlotMouseDown;
            rect.MouseMove += OnSlotMouseMove;
            rect.MouseLeftButtonUp += OnSlotMouseUp;
            SlotCanvas.Children.Add(rect);
        }
    }

    private void OnSlotMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Rectangle rect || _vm is null) return;
        _dragTarget = rect;
        _dragIndex = (int)rect.Tag!;
        _dragStart = e.GetPosition(SlotCanvas);
        _origSlotX = _vm.Slots[_dragIndex].X;
        _origSlotY = _vm.Slots[_dragIndex].Y;
        rect.CaptureMouse();
        e.Handled = true;
    }

    private void OnSlotMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragTarget is null || _dragIndex < 0 || _vm is null) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var (scale, _, _) = GetTransform();
        if (scale <= 0) return;

        var pos = e.GetPosition(SlotCanvas);
        double dxFrame = (pos.X - _dragStart.X) / scale;
        double dyFrame = (pos.Y - _dragStart.Y) / scale;

        var slot = _vm.Slots[_dragIndex];
        _vm.UpdateSlot(_dragIndex,
            (int)(_origSlotX + dxFrame),
            (int)(_origSlotY + dyFrame),
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
}
