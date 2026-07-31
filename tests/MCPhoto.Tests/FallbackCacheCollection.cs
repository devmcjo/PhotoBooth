namespace MCPhoto.Tests;

/// <summary>
/// it20 N2: fallback 프레임 캐시(`%ProgramData%\MCPhoto\cache\fallback_frame.png`)를 **공유**하는
/// 테스트 클래스들의 직렬화 컬렉션.
///
/// `FrameCatalogService.FallbackImagePath`는 주입 불가한 머신 전역 경로이고, 로컬 프레임이 0개면
/// `EnsureFallbackFrame()`이 그 파일을 생성한다. xUnit은 **컬렉션 단위로 병렬 실행**하므로
/// (이 저장소에는 `xunit.runner.json`도 `CollectionBehavior` 설정도 없다) 서로 다른 클래스가 동시에
/// 같은 경로를 만들거나 지울 수 있다.
///
/// `_fallbackWriteSync` lock이 **쓰기**는 직렬화하지만, `Fallback_Concurrent_Creation_Produces_One_Valid_File`이
/// 단정하는 "임시 파일 잔재 0개"는 그 lock **밖**에서 관측하므로 남의 임시 파일이 스쳐 보일 창이 남는다.
/// 반복 실행으로는 좁은 경합을 배제할 수 없어 컬렉션으로 구조적으로 없앤다.
///
/// 같은 컬렉션에 속한 클래스는 서로 병렬로 돌지 않는다(클래스 내부는 원래 순차다).
/// </summary>
[CollectionDefinition(Name)]
public sealed class FallbackCacheCollection
{
    public const string Name = "FallbackFrameCache";
}
