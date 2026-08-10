using MCPhoto.Core.Models;

namespace MCPhoto.Core.Settings;

/// <summary>
/// <c>MCPhoto.ini</c> <c>[Test]</c> 섹션 기반 테스트 로그인 모드(it23 B부).
/// <para>
/// 이 모드는 <b>JWT를 만들지 않는다</b>(불변식 TM1) — 가짜 <see cref="User"/>가 <c>SessionContext</c>에만
/// 들어가고 <c>IBackendSession</c>에는 토큰이 없다. 그 비대칭이 권한 경계를 규정한다: 역할별 UI와 로컬 설정
/// 편집은 뚫리지만 <b>서버는 조금도 뚫리지 않는다</b>(§B10.2).
/// </para>
/// <para>
/// ⚠️ 모든 테스트 모드 분기는 <see cref="IsTestUser"/>를 통과해야 한다(불변식 TM3).
/// <see cref="IsEnabled"/>만 보고 분기하면 <b>실제 계정으로 로그인한 세션에도 우회가 적용된다</b> —
/// 그것은 인증 우회 취약점이다. <see cref="IsEnabled"/>는 배너 표시와 DI 등록에만 쓴다.
/// </para>
/// (설계: docs/design/wpf-it23-session-testmode-license-design.md §B5.2·§B8.3)
/// </summary>
public interface ITestModeService
{
    /// <summary><c>[Test] TestMode</c>가 참인가. 앱 수명 동안 불변(시작 시 1회 판정 — 재시작이 정직하다).</summary>
    bool IsEnabled { get; }

    /// <summary>검증·폴백이 끝난 옵션. <see cref="IsEnabled"/>가 false여도 기본값 객체를 돌려준다(null 금지).</summary>
    TestModeOptions Options { get; }

    /// <summary>실제로 읽은 INI 절대 경로(진단·로그용). 파일이 없어도 경로는 돌려준다.</summary>
    string SourcePath { get; }

    /// <summary>
    /// 테스트 계정 <see cref="User"/>를 1회 생성해 보관하고 <b>같은 인스턴스</b>를 돌려준다.
    /// <see cref="IsEnabled"/>가 false면 null. 이 인스턴스 동일성이 <see cref="IsTestUser"/>의 판정 근거다.
    /// </summary>
    User? TestUser { get; }

    /// <summary>
    /// 이 <see cref="User"/>가 그 테스트 계정 인스턴스인가(<b>참조 동일성</b>).
    /// 값 비교가 아니므로 실제 Google SSO 로그인이 만든 계정은 — 이메일·Id·역할이 우연히 전부 같아도 —
    /// 판정이 false다. 위조가 불가능하다는 것이 이 계약의 핵심이다.
    /// </summary>
    bool IsTestUser(User? user);
}
