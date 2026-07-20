using CommunityToolkit.Mvvm.ComponentModel;

namespace MCPhoto.App.ViewModels;

/// <summary>화면 ViewModel 공통 기반. 진입/이탈 훅 제공.</summary>
public abstract class ViewModelBase : ObservableObject
{
    /// <summary>화면 진입 시 호출(비동기 초기화).</summary>
    public virtual Task OnEnterAsync() => Task.CompletedTask;

    /// <summary>화면 이탈 시 호출(리소스 정리).</summary>
    public virtual Task OnLeaveAsync() => Task.CompletedTask;
}
