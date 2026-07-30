namespace MCPhoto.Core.Build;

/// <summary>
/// 서버(웹) 배포 정보 조회. 진단·상태 화면의 "Web Deploy Date" 표기 전용이다.
///
/// 앱 버전·빌드 시각은 실행 파일 자신(<see cref="IBuildInfoService"/>)에서 오지만, 웹(Cloud Functions)은
/// 앱과 독립적으로 배포되므로 그 시각은 서버만 안다 → GET /health의 deployedAt을 읽는다.
/// 미구성·미도달·응답에 값 없음은 모두 null(표기 "확인 불가")로 폴백한다 — 진단 화면은 어떤 경우에도 열려야 한다.
/// </summary>
public interface IServerDeployInfoService
{
    /// <summary>최종 웹 배포 시각(서버 UTC). 미구성·도달 실패·미제공 시 null.</summary>
    Task<DateTimeOffset?> GetWebDeployedAtAsync(CancellationToken ct = default);
}
