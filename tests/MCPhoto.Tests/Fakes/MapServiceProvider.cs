namespace MCPhoto.Tests.Fakes;

/// <summary>
/// 테스트용 최소 <see cref="IServiceProvider"/>. 등록된 타입만 해석하고 나머지는 null을 반환한다.
/// <c>AppShellViewModel</c>의 fail-closed 경로(서비스 미등록 → 게이트 차단)를 검증하려면
/// 해당 타입을 등록하지 않으면 된다.
/// </summary>
public sealed class MapServiceProvider : IServiceProvider
{
    private readonly Dictionary<Type, Func<object>> _factories = new();

    /// <summary>서비스 인스턴스 등록(싱글턴 취급).</summary>
    public MapServiceProvider Add<T>(T instance) where T : class
    {
        _factories[typeof(T)] = () => instance;
        return this;
    }

    /// <summary>지연 생성 등록(순환 의존 — 예: 셸을 필요로 하는 화면 VM).</summary>
    public MapServiceProvider AddFactory<T>(Func<object> factory) where T : class
    {
        _factories[typeof(T)] = factory;
        return this;
    }

    public object? GetService(Type serviceType) =>
        _factories.TryGetValue(serviceType, out var f) ? f() : null;
}
