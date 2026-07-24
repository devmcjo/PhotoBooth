namespace MCPhoto.Http.Dto;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// 백엔드 JSON 직렬화 옵션(공통). 서버(TS)는 camelCase 키를 쓰므로 camelCase 정책 + 대소문자 무시로 안전하게 매핑한다.
/// null 필드는 생략하지 않는다(finalImageUrl 등 명시적 null 의미가 계약이므로 유지, 설계 §6.2 it7 F2).
/// </summary>
internal static class BackendJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };
}
