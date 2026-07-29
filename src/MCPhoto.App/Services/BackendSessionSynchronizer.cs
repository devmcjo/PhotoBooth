using System;
using MCPhoto.Http.Session;

namespace MCPhoto.App.Services;

/// <summary>
/// 계정 진실 소스(<see cref="SessionContext"/>)와 백엔드 JWT 홀더(<see cref="IBackendSession"/>)의
/// 로그아웃 동기화. 게스트로 전환되는 순간 토큰을 폐기한다.
///
/// 없으면 생기는 결함: 로그아웃은 <see cref="SessionContext"/>만 비우므로 JWT가 남고, 업로드는
/// 선택적 Bearer(<c>SendJsonOptionalBearerAsync</c>)라 그 토큰을 조용히 부착한다. 그 결과 다음 게스트
/// 촬영물이 직전 계정 소유로 기록되고, TempUser 계정이면 서버가 QR 사용 횟수까지 차감한다(it13 과금 방어).
///
/// 배선을 <c>Logout()</c> 한 곳이 아니라 <see cref="SessionContext.CurrentUserChanged"/> 구독으로 둔 이유:
/// <c>CurrentUser</c>를 비우는 지점이 <c>Logout()</c>·<c>Reset(clearUser: true)</c> 둘인데 후자도 전자에 위임하므로,
/// 이 통지 한 곳이 **모든** 게스트 전환을 덮는다. 경로가 앞으로 늘어도 여기만 살아 있으면 된다.
/// (참고: 현재 <c>clearUser: true</c>를 넘기는 프로덕션 호출부는 0이다 — 유휴 타임아웃은 it8 A1로 로그아웃이
/// 금지돼 <c>clearUser: false</c>다. 즉 실호출 로그아웃은 <c>AppShellViewModel.Logout()</c> 하나뿐이다.)
///
/// 비-null(로그인) 전이에서는 아무것도 하지 않는다 — 로그인은 <c>HttpAccountService</c>가
/// <see cref="IBackendSession.SignIn"/>을 먼저 호출한 뒤 <see cref="SessionContext.Login"/>이 통지하는
/// 순서라, 여기서 손대면 방금 받은 토큰을 지운다.
/// </summary>
/// <remarks>
/// 수명: DI 싱글턴. 구독 대상(<see cref="SessionContext"/>)도 싱글턴이라 앱 수명 동안 함께 살지만,
/// 호스트 종료·테스트 컨테이너 폐기 시 확실히 끊기도록 <see cref="IDisposable"/>로 해제 경로를 갖는다.
/// 컨테이너가 이 인스턴스를 소유하므로(팩토리 등록) <c>Dispose</c>는 컨테이너가 호출한다.
/// </remarks>
internal sealed class BackendSessionSynchronizer : IDisposable
{
    private readonly SessionContext _context;
    private bool _disposed;

    /// <summary>동기화 대상 JWT 홀더. DI는 이 프로퍼티를 <see cref="IBackendSession"/>으로 노출한다.</summary>
    public IBackendSession Session { get; }

    public BackendSessionSynchronizer(SessionContext context, IBackendSession session)
    {
        _context = context;
        Session = session;
        _context.CurrentUserChanged += OnCurrentUserChanged;
    }

    private void OnCurrentUserChanged(object? sender, EventArgs e)
    {
        if (_context.CurrentUser is null)
            Session.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _context.CurrentUserChanged -= OnCurrentUserChanged;
    }
}
