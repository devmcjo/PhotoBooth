using MCPhoto.Core.Devices;

namespace MCPhoto.Devices.Nikon;

/// <summary>
/// SDK 런타임 파일 존재 검사. (it23 §3.4)
/// <para>
/// <see cref="NikonExternalCamera.ConnectAsync"/>의 <b>첫 관문</b>이다 — 파일이 없으면 shim을 아예 호출하지 않고
/// 강등한다. 왜 shim에게 맡기지 않는가: 네이티브 모듈 로드 실패는 벤더 SDK 안에서 일어나면
/// (a) 사유가 숫자 코드로만 오거나, (b) 프로세스를 불안정하게 만들 수 있다. "파일이 없다"는
/// 우리가 확실히 알 수 있는 사실이므로 우리가 먼저 판정한다.
/// </para>
/// </summary>
public sealed class SdkRuntimeProbe
{
    /// <summary>SDK 파일을 두는 실행 폴더 하위 디렉터리 이름(설치·수동 배치 규약).</summary>
    public const string SdkFolderName = "NikonSdk";

    /// <summary>
    /// md3 외에 필요한 동반 런타임 파일 목록.
    /// ⚠️ 현재 비어 있는 것이 정상이다 — SDK 배포물을 열어 보기 전에는 목록을 알 수 없다(설계 A10).
    /// SDK 도착 시 §15-C2에서 이 상수만 채운다(검사 로직은 그대로).
    /// 추측으로 파일명을 채우면 실제로는 정상인 배치를 "부재"로 오판해 영구 강등된다.
    /// </summary>
    public static readonly IReadOnlyList<string> RequiredCompanionFiles = Array.Empty<string>();

    private readonly string _baseDirectory;

    /// <param name="baseDirectory">
    /// 검사 기준 폴더. null이면 실행 폴더(<see cref="AppContext.BaseDirectory"/>).
    /// 테스트가 임시 폴더를 주입할 수 있도록 열어 둔다 — 파일 부재/존재 두 경로를 실물 SDK 없이 검증한다.
    /// </param>
    public SdkRuntimeProbe(string? baseDirectory = null)
        => _baseDirectory = baseDirectory ?? AppContext.BaseDirectory;

    /// <summary>SDK 파일 폴더 절대 경로.</summary>
    public string SdkFolder => Path.Combine(_baseDirectory, SdkFolderName);

    /// <summary>모델의 md3 절대 경로.</summary>
    public string Md3Path(ExternalCameraModel model) => Path.Combine(SdkFolder, model.Md3FileName);

    /// <summary>사용자 노출용 상대 표기(예 <c>NikonSdk\Type0011.md3</c>).</summary>
    public static string RelativeDisplayPath(string fileName) => Path.Combine(SdkFolderName, fileName);

    /// <summary>
    /// 필요한 런타임 파일이 모두 있는지 검사. 없으면 <c>(false, 사유)</c>.
    /// 예외를 던지지 않는다(경로 권한 오류도 "없음"으로 취급) — 강등 경로가 크래시보다 낫다.
    /// </summary>
    public (bool ok, string? reason) Probe(ExternalCameraModel model)
    {
        try
        {
            if (!File.Exists(Md3Path(model)))
                return (false, NikonCameraReasons.ModuleFileMissing(RelativeDisplayPath(model.Md3FileName)));

            foreach (var companion in RequiredCompanionFiles)
            {
                if (!File.Exists(Path.Combine(SdkFolder, companion)))
                    return (false, NikonCameraReasons.ModuleFileMissing(RelativeDisplayPath(companion)));
            }

            return (true, null);
        }
        catch (Exception)
        {
            // 경로가 너무 길거나 접근 불가 — 사용자에게는 "파일 없음"과 동일한 조치(배치 확인)를 안내한다.
            return (false, NikonCameraReasons.ModuleFileMissing(RelativeDisplayPath(model.Md3FileName)));
        }
    }
}
