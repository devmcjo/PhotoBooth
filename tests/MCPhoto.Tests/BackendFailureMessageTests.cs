using System;
using System.Net;
using System.Net.Http;
using MCPhoto.Core.Backend;
using MCPhoto.Http;
using Xunit;

namespace MCPhoto.Tests;

/// <summary>
/// 서버 실패 안내 문구 회귀. 이 파일이 지키는 것은 "예외 메시지가 그대로 사용자에게 새어 나가지 않는다"는
/// 한 가지 성질이다 — 문장 자체는 바뀔 수 있으므로 <b>포함 여부</b>만 검사하고 전문 일치는 쓰지 않는다.
/// </summary>
public class BackendFailureMessageTests
{
    // ── 원인별 분기 ──

    [Fact]
    public void 저장_서버미설정이면_설정화면을_안내한다()
    {
        var msg = BackendFailureMessage.ForFrameSave(
            new BackendNotConfiguredException("백엔드 서버 주소가 설정되지 않았습니다."));

        Assert.Contains("서버 주소", msg);
        Assert.Contains("설정 화면", msg);
    }

    [Fact]
    public void 저장_서버도달불가면_네트워크확인과_편집내용보존을_알린다()
    {
        var msg = BackendFailureMessage.ForFrameSave(
            new BackendUnavailableException("백엔드에 연결할 수 없습니다."));

        Assert.Contains("연결할 수 없어", msg);
        Assert.Contains("네트워크", msg);
        // 편집 내용이 날아갔다고 오해하면 사용자가 처음부터 다시 만든다 → 보존 사실을 반드시 알린다.
        Assert.Contains("편집 중인 내용은 그대로", msg);
    }

    [Fact]
    public void 저장_토큰만료면_재로그인을_안내하고_jwt문구를_노출하지_않는다()
    {
        // 서버가 실제로 주는 메시지. 그대로 노출되면 사용자는 무엇을 해야 할지 알 수 없다.
        var msg = BackendFailureMessage.ForFrameSave(
            new BackendLoginRequiredException("토큰 검증 실패: jwt expired", expired: true));

        Assert.Contains("로그인이 만료", msg);
        Assert.Contains("다시 로그인", msg);
        Assert.DoesNotContain("jwt", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void 저장_토큰없음은_만료가_아니라_로그인필요로_안내한다()
    {
        var msg = BackendFailureMessage.ForFrameSave(
            new BackendLoginRequiredException("로그인이 필요합니다(토큰 없음).", expired: false));

        Assert.Contains("로그인이 필요합니다", msg);
        Assert.DoesNotContain("만료", msg);
    }

    [Fact]
    public void 저장_403은_권한문제로_안내한다()
    {
        var msg = BackendFailureMessage.ForFrameSave(new UnauthorizedAccessException("forbidden"));

        Assert.Contains("권한", msg);
        Assert.DoesNotContain("forbidden", msg);
    }

    [Fact]
    public void 저장_그밖의_서버거부는_서버가_준_한국어_문구를_인용한다()
    {
        // 409 이름 중복 등. 서버 문구가 이미 사용자 노출용이므로 덮어쓰지 않고 인용한다.
        var msg = BackendFailureMessage.ForFrameSave(
            new InvalidOperationException("같은 이름의 프레임이 이미 있습니다."));

        Assert.Contains("같은 이름의 프레임이 이미 있습니다.", msg);
        Assert.Contains("저장하지 못했습니다.", msg);
    }

    [Fact]
    public void 메시지가_비어도_공백이_겹치지_않는다()
    {
        var msg = BackendFailureMessage.ForFrameSave(new InvalidOperationException(string.Empty));

        Assert.DoesNotContain("  ", msg);
    }

    // ── 삭제: 로컬 보존 사실을 항상 함께 알린다(D-19) ──

    [Theory]
    [InlineData(typeof(BackendNotConfiguredException))]
    [InlineData(typeof(BackendUnavailableException))]
    [InlineData(typeof(BackendLoginRequiredException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(InvalidOperationException))]
    public void 삭제실패는_원인과_무관하게_로컬보존을_알린다(Type exceptionType)
    {
        var ex = exceptionType switch
        {
            var t when t == typeof(BackendNotConfiguredException) => new BackendNotConfiguredException("x"),
            var t when t == typeof(BackendUnavailableException) => (Exception)new BackendUnavailableException("x"),
            var t when t == typeof(BackendLoginRequiredException) => new BackendLoginRequiredException("x", true),
            var t when t == typeof(UnauthorizedAccessException) => new UnauthorizedAccessException("x"),
            _ => new InvalidOperationException("서버 오류입니다."),
        };

        var msg = BackendFailureMessage.ForFrameDelete(ex);

        // 이 문장이 빠지면 "삭제했는데 목록에 남아 있다"로 읽힌다.
        Assert.Contains("이 PC의 파일은 그대로 둡니다.", msg);
    }

    // ── Http 계층이 실제로 그 타입을 던지는가(문구 매퍼가 붙을 자리) ──

    [Fact]
    public async Task 서버주소_미설정이면_영문예외_대신_BackendNotConfigured를_던진다()
    {
        // BaseAddress 없는 HttpClient + 상대 URL = HttpClient가 영문 InvalidOperationException을 던지던 조합.
        var factory = new NoBaseAddressClientFactory();
        var repo = new HttpFrameRepository(factory, new StubSession(token: "t"), apiKey: "k", logger: null);

        var ex = await Assert.ThrowsAsync<BackendNotConfiguredException>(() => repo.GetDefaultFramesAsync());

        Assert.DoesNotContain("URI", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("서버 주소", BackendFailureMessage.Describe(ex));
    }

    [Fact]
    public async Task 토큰이_없으면_BackendLoginRequired_미만료를_던진다()
    {
        var factory = new NoBaseAddressClientFactory();
        var repo = new HttpFrameRepository(factory, new StubSession(token: null), apiKey: "k", logger: null);

        // Bearer 필수 라우트 → 요청 조립 단계에서 거부(주소 판정보다 앞선다).
        var ex = await Assert.ThrowsAsync<BackendLoginRequiredException>(
            () => repo.DeleteAsync("frame-1"));

        Assert.False(ex.Expired);
        // 기존 계약(UnauthorizedAccessException 파생)이 깨지지 않았는지도 함께 확인.
        Assert.IsAssignableFrom<UnauthorizedAccessException>(ex);
    }

    /// <summary>BaseAddress를 주입하지 않는 팩토리 — "서버 주소 미설정" 상태 재현.</summary>
    private sealed class NoBaseAddressClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class StubSession : MCPhoto.Http.Session.IBackendSession
    {
        public StubSession(string? token) => Token = token;
        public string? Token { get; private set; }
        public MCPhoto.Core.Models.User? CurrentUser => null;
        public void SignIn(string token, MCPhoto.Core.Models.User user) => Token = token;
        public void Clear() => Token = null;
    }
}
