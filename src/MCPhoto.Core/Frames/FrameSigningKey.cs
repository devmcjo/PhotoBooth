using System.Text;

namespace MCPhoto.Core.Frames;

/// <summary>
/// 로컬 <c>.slots</c> 파일 서명 키(HMAC-SHA256).
/// <para>
/// ⚠️ <b>이 값은 비밀이 아니다.</b> 파일을 텍스트 편집기로 열어 소유자를 바꾸는 우회를 막는
/// <b>변조 난이도 상승 장치</b>일 뿐이며, exe를 리버싱하면 추출할 수 있다(설계 §10 위협 모델).
/// 완전 방어는 서버 권위(매번 온라인 검증)뿐인데 오프라인 촬영 불변식과 충돌해 채택하지 않았다.
/// </para>
/// <para>
/// <b>publish 주입이 아니라 소스 고정 상수인 이유</b>(설계 §6): 주입 방식이면 개발 빌드와 운영
/// 빌드가 서로의 프레임 파일을 읽지 못한다(검증 실패 → 목록에서 사라짐). 키 은닉으로 얻는 이득보다
/// 빌드 간 파일 호환의 실익이 크다 — 어차피 exe에서 추출 가능하므로 은닉 이득 자체가 작다.
/// </para>
/// <para>
/// ⚠️ <b>이 값을 바꾸지 말 것.</b> 바꾸면 기존 로컬 프레임이 전부 검증 실패해 목록에서 사라진다
/// (로테이션 미지원 — 불가피하면 포맷 v3 + 재서명 마이그레이션이 필요하다).
/// </para>
/// </summary>
public static class FrameSigningKey
{
    /// <summary>
    /// HMAC 키 원문(32바이트 상당). <see cref="BackendApiKey"/>류의 서버 게이트 키와 성격이 다르다 —
    /// 유출돼도 "그 PC에서 로컬 파일 우회 가능" 수준이며 서버 권한과 무관하다.
    /// </summary>
    private const string KeyMaterial = "mcphoto.frame.slots.v2/8Kq3xR7pLd2WmZfA9tYbNc6HuVsE4jQg";

    /// <summary>서명·검증에 쓰는 키 바이트. 호출마다 새 배열을 돌려준다(호출자가 변형해도 안전).</summary>
    public static byte[] GetKeyBytes() => Encoding.UTF8.GetBytes(KeyMaterial);
}
