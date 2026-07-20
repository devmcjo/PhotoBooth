using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MCPhoto.App.Imaging;
using MCPhoto.App.ViewModels;

namespace MCPhoto.App.Views;

/// <summary>
/// 라이브 프리뷰. CameraFramePresenter로 재사용 WriteableBitmap 렌더. (WBS Step 3)
/// </summary>
public partial class PreviewView : UserControl
{
    private PreviewViewModel? _vm;
    private CameraFramePresenter? _presenter;
    private readonly DispatcherTimer _fpsTimer;

    public PreviewView()
    {
        InitializeComponent();
        _presenter = new CameraFramePresenter(PreviewImage);
        _fpsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _fpsTimer.Tick += (_, _) =>
        {
            if (_vm is null) return;
            _vm.RefreshFps();
            FpsText.Text = $"{_vm.Fps:F0} fps";
            StatusText.Visibility = _vm.CameraAvailable ? Visibility.Collapsed : Visibility.Visible;
            StatusText.Text = _vm.StatusMessage;
        };

        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _vm = DataContext as PreviewViewModel;
        if (_vm is not null)
            _presenter?.Attach(_vm.Camera);
    }

    public async Task StartAsync(double targetAspect)
    {
        if (_vm is null) return;
        _fpsTimer.Start();
        await _vm.StartAsync(targetAspect);
    }

    public async Task StopAsync()
    {
        _fpsTimer.Stop();
        if (_vm is not null)
            await _vm.StopAsync();
    }

    private async void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _presenter?.Detach();
        await StopAsync();
    }
}
