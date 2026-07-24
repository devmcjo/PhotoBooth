using System;
using System.Net.Http;

namespace MCPhoto.Tests.Http;

/// <summary>
/// 테스트용 IHttpClientFactory. 항상 같은 FakeHttpMessageHandler로 HttpClient를 만들고 BaseAddress를 주입한다
/// (ServiceRegistration의 AddHttpClient가 하던 BaseAddress 설정을 테스트에서 대체).
/// </summary>
internal sealed class TestHttpClientFactory : IHttpClientFactory
{
    private readonly FakeHttpMessageHandler _handler;
    private readonly Uri _baseAddress;

    public TestHttpClientFactory(FakeHttpMessageHandler handler, string baseAddress = "https://backend.test/api/")
    {
        _handler = handler;
        _baseAddress = new Uri(baseAddress);
    }

    public HttpClient CreateClient(string name)
        => new(_handler, disposeHandler: false) { BaseAddress = _baseAddress };
}
