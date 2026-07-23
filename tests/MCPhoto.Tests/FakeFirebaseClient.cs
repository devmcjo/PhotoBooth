using MCPhoto.Core.Models;
using MCPhoto.Core.Upload;

namespace MCPhoto.Tests;

/// <summary>
/// it10: ViewModel 테스트용 경량 IFirebaseClient 페이크. IsInitialized·Bucket만 관측 대상.
/// 업로드/문서 계열은 이 테스트 범위 밖 → 호출 시 예외로 오사용을 드러냄.
/// </summary>
internal sealed class FakeFirebaseClient : IFirebaseClient
{
    public bool IsInitialized { get; init; }
    public string Bucket { get; init; } = string.Empty;

    public Task<string> UploadFileAsync(string storagePath, string localFilePath, string contentType, IProgress<double>? fileProgress = null, CancellationToken ct = default)
        => throw new NotSupportedException("FakeFirebaseClient: 업로드는 이 테스트 범위 밖입니다.");

    public Task DeleteStoragePrefixAsync(string prefix, CancellationToken ct = default)
        => throw new NotSupportedException("FakeFirebaseClient: 삭제는 이 테스트 범위 밖입니다.");

    public Task CreateResultSessionAsync(ResultSession session, CancellationToken ct = default)
        => throw new NotSupportedException("FakeFirebaseClient: 세션 생성은 이 테스트 범위 밖입니다.");

    public Task<IReadOnlyList<ResultSession>> QueryExpiredSessionsAsync(DateTime now, CancellationToken ct = default)
        => throw new NotSupportedException("FakeFirebaseClient: 만료 조회는 이 테스트 범위 밖입니다.");

    public Task DeleteResultSessionAsync(string sessionId, CancellationToken ct = default)
        => throw new NotSupportedException("FakeFirebaseClient: 세션 삭제는 이 테스트 범위 밖입니다.");
}
