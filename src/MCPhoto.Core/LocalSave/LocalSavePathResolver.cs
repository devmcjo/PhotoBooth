namespace MCPhoto.Core.LocalSave;

/// <summary>
/// 로컬 저장 루트 경로 해석(순수 함수 — 단일 지점). (it26 §3.7)
/// <para>
/// 규칙은 하나다: <b>운영자가 지정한 <c>LocalSavePath</c>가 항상 우선</b>이고, 비어 있을 때만
/// 데이터 폴더의 <c>result\</c>가 기본값이다. 종전에는 이 판정이 호출부(<c>ResultViewModel</c>)에
/// 인라인돼 있었다 — 소비자가 둘 이상(저장 · 설정 화면 캡션)이 되는 순간 규칙이 갈린다.
/// </para>
/// <para>
/// ⚠️ 기본값이 실행 폴더(<c>{exe}\result</c>)에서 데이터 폴더로 옮겨진 것은 <b>빈 값의 해석</b>뿐이다.
/// 명시값은 한 글자도 변형하지 않으며(Trim만), 구 위치의 파일을 옮기거나 지우는 코드는 존재하지 않는다.
/// </para>
/// </summary>
public static class LocalSavePathResolver
{
    /// <summary>데이터 폴더 하위 결과물 폴더명.</summary>
    public const string DefaultFolderName = "result";

    /// <param name="configuredPath">설정값(<c>AppSettings.LocalSavePath</c>). 공백이면 기본값을 쓴다.</param>
    /// <param name="dataFolder">쓰기 가능한 데이터 폴더(앱은 <c>App.DataFolder</c> = %ProgramData%\MCPhoto).</param>
    public static string Resolve(string? configuredPath, string dataFolder)
        => string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(dataFolder, DefaultFolderName)
            : configuredPath.Trim();
}
