namespace MCPhoto.Core.Devices;

/// <summary>
/// 연동 가능 외부 카메라 모델 1행. (it23 §3.3)
/// </summary>
/// <param name="Id">
/// ini <c>ExternalCameraModel</c> 키의 저장값이자 레지스트리 키. <b>변경 금지</b> —
/// 바꾸면 기존 설치의 저장값이 미지 Id가 되어 기본 모델로 되돌아간다.
/// </param>
/// <param name="DisplayName">설정·테스트 모달의 표시명(콤보 항목).</param>
/// <param name="Md3FileName">
/// SDK 모듈 파일명(<c>{exe}\NikonSdk\</c> 기준 상대). md3는 바디 전용 모듈이라 모델과 1:1이다.
/// <para>
/// ini에 <see cref="Id"/>를 저장하고 이 파일명을 저장하지 않는 이유: md3 파일명은 SDK 버전에 따라
/// 바뀔 수 있는 **Nikon 쪽 바이너리 세부**이고, Id는 우리 관례다. 파일명을 저장값으로 삼으면
/// SDK 업데이트가 사용자 설정을 깨뜨린다.
/// </para>
/// </param>
public sealed record ExternalCameraModel(string Id, string DisplayName, string Md3FileName);

/// <summary>
/// 지원 모델 정적 레지스트리. **모델 추가 = 이 표에 한 줄**이 되도록 모델별 분기를 코드 어디에도
/// 두지 않는다(it23 요구 6).
/// <para>
/// ⚠️ 다만 코드 한 줄로 끝나지 않는 것이 하나 있다: **모델마다 법적 절차 1건**이다(설계 §3.3 rev2).
/// md3는 모델별로 별도 SDK 다운로드·별도 라이선스 동의 대상이므로, 여기 행을 추가하는 변경에는
/// 해당 SDK의 약관 확인 기록이 함께 있어야 한다. md3 자체는 리포·설치본에 넣지 않는다 —
/// 런타임에 <c>{exe}\NikonSdk\</c>를 탐색하고 없으면 강등하는 것이 정규 배포 형태다(§13.2 D1·D2).
/// </para>
/// </summary>
public static class ExternalCameraModels
{
    /// <summary>
    /// 지원 모델 표. 현재 실제 활성 항목은 D5300 하나다.
    /// ⚠️ 첫 항목이 <see cref="Default"/>다 — 순서를 바꾸면 기본 모델이 바뀐다.
    /// </summary>
    public static readonly IReadOnlyList<ExternalCameraModel> All = new[]
    {
        new ExternalCameraModel("NikonD5300", "Nikon D5300", "Type0011.md3"),
    };

    /// <summary>기본 모델(ini에 값이 없거나 미지 Id일 때의 보정 대상).</summary>
    public static ExternalCameraModel Default => All[0];

    /// <summary>
    /// Id 조회(대소문자·앞뒤 공백 무시). 미지 Id는 null을 반환하고 **보정하지 않는다** —
    /// 보정 책임은 호출측(<c>AppSettings.Clamp</c>)에 둔다. 조회 함수가 몰래 기본값을 돌려주면
    /// "설정값이 유효한가"를 판정할 수 없게 된다.
    /// </summary>
    public static ExternalCameraModel? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var needle = id.Trim();
        foreach (var m in All)
        {
            if (string.Equals(m.Id, needle, StringComparison.OrdinalIgnoreCase))
                return m;
        }
        return null;
    }

    /// <summary>Id를 유효한 모델로 해석(미지·빈 값은 <see cref="Default"/>). 런타임 소비 지점용.</summary>
    public static ExternalCameraModel Resolve(string? id) => Find(id) ?? Default;
}
