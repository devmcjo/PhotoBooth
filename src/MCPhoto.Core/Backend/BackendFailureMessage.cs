namespace MCPhoto.Core.Backend;

using System;
using System.Linq;

/// <summary>
/// 서버 호출 실패를 <b>사용자가 읽을 수 있는 한국어 안내</b>로 바꾼다.
///
/// 왜 필요한가: 예외 메시지를 그대로 노출하면 사용자가 조치할 수 없는 문장이 화면에 뜬다.
/// 실제로 새어 나오던 것들 —
///   · 서버 주소 미설정 → "An invalid request URI was provided…"(.NET 영문)
///   · 토큰 만료        → "토큰 검증 실패: jwt expired"(라이브러리 영문 혼재)
///   · 네트워크 끊김    → "백엔드에 연결할 수 없습니다."(사실이지만 무엇을 하라는 말이 없음)
///
/// 규칙: 한 문장에 <b>①무엇이 안 됐는지 ②왜 ③무엇을 하면 되는지</b>를 담고,
/// 되돌릴 수 없는 오해를 막는 사실(편집 내용 보존 / 로컬 파일 보존)을 덧붙인다.
///
/// 서버가 준 메시지(<c>HttpError</c> 계열)는 우리 백엔드가 한국어로 작성한 사용자 노출용 문구이므로
/// 원인 타입이 특정되지 않을 때만 그대로 인용한다.
/// </summary>
public static class BackendFailureMessage
{
    /// <summary>삭제 실패 문구에 공통으로 붙는 사실 — "서버는 실패했지만 로컬은 살아 있다"(D-19).</summary>
    private const string LocalKept = "이 PC의 파일은 그대로 둡니다.";

    /// <summary>저장 실패 문구에 공통으로 붙는 사실 — 편집 세션이 살아 있으니 다시 누르면 된다(D6 원자성).</summary>
    private const string EditKept = "편집 중인 내용은 그대로 남아 있습니다.";

    /// <summary>
    /// 프레임 저장 실패 안내. 개인·공용 저장 양쪽에서 쓴다.
    /// </summary>
    public static string ForFrameSave(Exception ex) => ex switch
    {
        BackendNotConfiguredException =>
            $"서버 주소가 설정되어 있지 않아 저장할 수 없습니다. 설정 화면에서 서버 주소를 입력한 뒤 다시 시도해 주세요. {EditKept}",

        BackendUnavailableException =>
            $"서버에 연결할 수 없어 저장하지 못했습니다. 프레임은 서버에 보관되므로 인터넷 연결이 필요합니다. "
            + $"네트워크를 확인한 뒤 [저장]을 다시 눌러 주세요. {EditKept}",

        BackendLoginRequiredException { Expired: true } =>
            $"로그인이 만료되어 저장하지 못했습니다. 다시 로그인한 뒤 [저장]을 눌러 주세요. {EditKept}",

        BackendLoginRequiredException =>
            $"로그인이 필요합니다. 로그인한 뒤 [저장]을 눌러 주세요. {EditKept}",

        UnauthorizedAccessException =>
            $"이 계정에는 프레임을 저장할 권한이 없습니다. {EditKept}",

        _ => Join("저장하지 못했습니다.", ServerSaid(ex), EditKept),
    };

    /// <summary>
    /// 프레임 삭제 실패 안내. 서버 삭제가 실패하면 로컬도 지우지 않으므로(D-19) 그 사실을 항상 함께 알린다 —
    /// 알리지 않으면 "지웠는데 목록에 남아 있다"로 읽힌다.
    /// </summary>
    public static string ForFrameDelete(Exception ex) => ex switch
    {
        BackendNotConfiguredException =>
            $"서버 주소가 설정되어 있지 않아 삭제할 수 없습니다. 설정 화면에서 서버 주소를 입력해 주세요. {LocalKept}",

        BackendUnavailableException =>
            $"서버에 연결할 수 없어 삭제하지 못했습니다. 네트워크를 확인한 뒤 다시 시도해 주세요. {LocalKept}",

        BackendLoginRequiredException { Expired: true } =>
            $"로그인이 만료되어 삭제하지 못했습니다. 다시 로그인한 뒤 시도해 주세요. {LocalKept}",

        BackendLoginRequiredException =>
            $"로그인이 필요합니다. 로그인한 뒤 삭제해 주세요. {LocalKept}",

        UnauthorizedAccessException =>
            $"이 프레임을 삭제할 권한이 없습니다. {LocalKept}",

        _ => Join("삭제하지 못했습니다.", ServerSaid(ex), LocalKept),
    };

    /// <summary>
    /// 화면 맥락이 없는 일반 안내(관리자 설정 변경 등). 원인 문장만 돌려준다.
    /// </summary>
    public static string Describe(Exception ex) => ex switch
    {
        BackendNotConfiguredException =>
            "서버 주소가 설정되어 있지 않습니다. 설정 화면에서 서버 주소를 입력해 주세요.",

        BackendUnavailableException =>
            "서버에 연결할 수 없습니다. 네트워크를 확인한 뒤 다시 시도해 주세요.",

        BackendLoginRequiredException { Expired: true } =>
            "로그인이 만료되었습니다. 다시 로그인해 주세요.",

        BackendLoginRequiredException =>
            "로그인이 필요합니다.",

        UnauthorizedAccessException =>
            "권한이 없습니다.",

        _ => string.IsNullOrWhiteSpace(ex.Message) ? "알 수 없는 오류가 발생했습니다." : ex.Message,
    };

    /// <summary>
    /// 원인 타입이 특정되지 않은 실패(409 이름 중복·404·서버 5xx 등)에서 서버가 준 한국어 문구를 인용한다.
    /// 비어 있으면 조용히 생략한다 — "저장하지 못했습니다. ." 같은 문장을 만들지 않기 위함.
    /// </summary>
    private static string ServerSaid(Exception ex)
        => string.IsNullOrWhiteSpace(ex.Message) ? string.Empty : ex.Message.Trim();

    /// <summary>빈 조각을 건너뛰고 공백 하나로 잇는다(빈 인용 때문에 공백이 겹치지 않게).</summary>
    private static string Join(params string[] parts)
        => string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
}
