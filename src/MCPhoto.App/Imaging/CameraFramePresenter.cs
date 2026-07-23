using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MCPhoto.Core.Capture;

namespace MCPhoto.App.Imaging;

/// <summary>
/// 카메라 FrameReady를 재사용 WriteableBitmap으로 Image에 렌더(프레임 스킵). (architecture §2.3)
/// CaptureView·카메라 테스트 모달이 공유. 매 프레임 새 BitmapSource 생성 금지.
/// </summary>
public sealed class CameraFramePresenter : IDisposable
{
    private readonly Image _target;
    private readonly Dispatcher _dispatcher;
    private ICameraService? _camera;

    private WriteableBitmap? _bitmap;
    private int _w, _h;
    private CameraFrame? _latest;
    private readonly object _lock = new();
    private bool _queued;

    public CameraFramePresenter(Image target)
    {
        _target = target;
        _dispatcher = target.Dispatcher;
    }

    public void Attach(ICameraService camera)
    {
        Detach();
        _camera = camera;
        camera.FrameReady += OnFrameReady;
    }

    public void Detach()
    {
        if (_camera is not null)
            _camera.FrameReady -= OnFrameReady;
        _camera = null;
    }

    private void OnFrameReady(object? sender, CameraFrame frame)
    {
        lock (_lock)
        {
            _latest = frame;
            if (_queued) return;
            _queued = true;
        }
        _dispatcher.BeginInvoke(DispatcherPriority.Render, RenderLatest);
    }

    private void RenderLatest()
    {
        CameraFrame frame;
        lock (_lock)
        {
            _queued = false;
            if (_latest is null) return;
            frame = _latest;
        }
        if (frame.Width <= 0 || frame.Height <= 0) return;

        if (_bitmap is null || _w != frame.Width || _h != frame.Height)
        {
            _bitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgr24, null);
            _w = frame.Width;
            _h = frame.Height;
            _target.Source = _bitmap;
        }
        _bitmap.WritePixels(new System.Windows.Int32Rect(0, 0, frame.Width, frame.Height), frame.Pixels, frame.Stride, 0);
    }

    public void Dispose() => Detach();
}
