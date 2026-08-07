using System.Security.Cryptography;
using System.Text;
using MCPhoto.Core.Models;

namespace MCPhoto.Core.Frames;

/// <summary>`.slots` 파일 해석 결과 상태. 진단 화면이 실패 사유를 구분해 보여준다.</summary>
public enum SlotsDecodeStatus
{
    /// <summary>정상(서명 검증 통과).</summary>
    Ok,

    /// <summary>base64가 아니다 — 구 포맷(v1 평문) 또는 손상. 목록에서 조용히 제외한다.</summary>
    NotEncoded,

    /// <summary>디코딩은 됐으나 `#sig` 줄이 없다.</summary>
    SignatureMissing,

    /// <summary>서명 불일치 — 내용이 변조됐다.</summary>
    SignatureInvalid,

    /// <summary>서명은 맞지만 필수 필드(`#owner`·`#imagesize`)가 없거나 슬롯이 0개다.</summary>
    Malformed
}

/// <summary>`.slots` v2 내용. 파일에서 읽어낸 값이자 쓸 값.</summary>
/// <param name="Owner">소유자(이메일 또는 <see cref="FrameOwnership.DefaultOwner"/>).</param>
/// <param name="ImageSize">프레임 원본 픽셀 크기(슬롯 좌표계 기준).</param>
/// <param name="Slots">슬롯 목록(1개 이상).</param>
/// <param name="DbId">서버 문서 id. null이면 서버 미동기 상태 → 삭제 동기화 비대상.</param>
public sealed record SlotsFileContent(
    string Owner,
    ImageSize ImageSize,
    IReadOnlyList<Slot> Slots,
    string? DbId);

/// <summary>
/// `.slots` 포맷 v2 인코딩·디코딩·서명(순수 로직).
/// <para>
/// <b>파일 = base64( payload + "\n#sig=" + HMAC_SHA256_hex(key, payload) )</b>
/// </para>
/// <para>
/// payload를 JSON이 아니라 <b>기존 텍스트 포맷의 확장</b>으로 둔 이유: 서명 대상 바이트가 명확해진다.
/// JSON은 키 순서·공백·이스케이프에 따라 같은 의미의 다른 바이트열이 나와 canonical 직렬화 규약을
/// 따로 정의해야 하는데, 줄 단위 텍스트에는 그 문제가 없다.
/// </para>
/// <para>
/// base64는 <b>가독성 차단</b>일 뿐 보호가 아니다(누구나 디코딩할 수 있다). 실제 방어는 <b>HMAC 서명</b>이며,
/// 그마저도 exe 리버싱은 막지 못한다(<see cref="FrameSigningKey"/> 주석 · 설계 §15 위협 모델).
/// </para>
/// </summary>
public static class SlotsFileCodec
{
    /// <summary>포맷 버전. `#v` 값이 이것과 다르면 v2로 취급하지 않는다.</summary>
    public const int Version = 2;

    private const string KeyVersion = "#v=";
    private const string KeyOwner = "#owner=";
    private const string KeyImageSize = "#imagesize=";
    private const string KeyDbId = "#dbid=";
    private const string KeySig = "#sig=";

    // ── 인코딩 ──

    /// <summary>v2 파일 내용(base64 문자열) 생성. 파일에는 이 문자열을 UTF-8로 그대로 쓴다.</summary>
    public static string Encode(SlotsFileContent content)
    {
        var payload = BuildPayload(content);
        var sig = ComputeSignature(payload);
        var full = payload + "\n" + KeySig + sig;
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(full));
    }

    /// <summary>
    /// 서명 대상 payload. 필드 순서를 고정한다 — 순서가 흔들리면 같은 내용이 다른 서명을 낳는다.
    /// </summary>
    private static string BuildPayload(SlotsFileContent content)
    {
        var sb = new StringBuilder();
        sb.Append(KeyVersion).Append(Version).Append('\n');
        sb.Append(KeyOwner).Append(content.Owner).Append('\n');
        sb.Append(KeyImageSize).Append(content.ImageSize.Width).Append(',').Append(content.ImageSize.Height).Append('\n');
        if (!string.IsNullOrEmpty(content.DbId))
            sb.Append(KeyDbId).Append(content.DbId).Append('\n');

        foreach (var s in content.Slots)
            sb.Append(s.Index).Append(',').Append(s.X).Append(',').Append(s.Y).Append(',')
              .Append(s.Width).Append(',').Append(s.Height).Append('\n');

        return sb.ToString().TrimEnd('\n');   // 마지막 개행 제외 — "#sig 줄 앞의 \n"과 중복되지 않게
    }

    private static string ComputeSignature(string payload)
    {
        var key = FrameSigningKey.GetKeyBytes();
        var mac = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(mac).ToLowerInvariant();
    }

    // ── 디코딩 ──

    /// <summary>
    /// 파일 내용 해석 + 서명 검증. <b>예외를 던지지 않는다</b> — 파일 하나가 깨져도 목록 로딩이 멈추면 안 된다.
    /// </summary>
    /// <param name="fileText">파일에서 읽은 문자열(UTF-8).</param>
    /// <param name="content">성공 시 내용, 실패 시 null.</param>
    public static SlotsDecodeStatus Decode(string? fileText, out SlotsFileContent? content)
    {
        content = null;
        if (string.IsNullOrWhiteSpace(fileText)) return SlotsDecodeStatus.NotEncoded;

        // 1) base64 디코딩 — v1 평문 파일은 여기서 걸러진다(마이그레이션 코드가 필요 없는 이유).
        string decoded;
        try
        {
            var bytes = Convert.FromBase64String(fileText.Trim());
            decoded = Encoding.UTF8.GetString(bytes);
        }
        catch (FormatException)
        {
            return SlotsDecodeStatus.NotEncoded;
        }

        // 2) 마지막 #sig 줄 분리
        int sigAt = decoded.LastIndexOf("\n" + KeySig, StringComparison.Ordinal);
        if (sigAt < 0) return SlotsDecodeStatus.SignatureMissing;

        var payload = decoded[..sigAt];
        var sig = decoded[(sigAt + 1 + KeySig.Length)..].Trim();

        // 3) 서명 검증(고정 시간 비교)
        var expected = ComputeSignature(payload);
        if (!FixedTimeEquals(expected, sig)) return SlotsDecodeStatus.SignatureInvalid;

        // 4) 파싱
        return ParsePayload(payload, out content);
    }

    /// <summary>hex 문자열 고정 시간 비교. 길이가 다르면 즉시 false(길이는 비밀이 아니다).</summary>
    private static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));
    }

    private static SlotsDecodeStatus ParsePayload(string payload, out SlotsFileContent? content)
    {
        content = null;

        string? owner = null;
        string? dbId = null;
        var size = new ImageSize();
        var slots = new List<Slot>();
        bool versionOk = false;

        foreach (var raw in payload.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith(KeyVersion, StringComparison.OrdinalIgnoreCase))
            {
                versionOk = int.TryParse(line[KeyVersion.Length..], out var v) && v == Version;
                continue;
            }
            if (line.StartsWith(KeyOwner, StringComparison.OrdinalIgnoreCase))
            {
                owner = line[KeyOwner.Length..].Trim();
                continue;
            }
            if (line.StartsWith(KeyImageSize, StringComparison.OrdinalIgnoreCase))
            {
                var wh = line[KeyImageSize.Length..].Split(',');
                if (wh.Length == 2 && int.TryParse(wh[0], out var w) && int.TryParse(wh[1], out var h))
                {
                    size.Width = w;
                    size.Height = h;
                }
                continue;
            }
            if (line.StartsWith(KeyDbId, StringComparison.OrdinalIgnoreCase))
            {
                dbId = line[KeyDbId.Length..].Trim();
                continue;
            }
            if (line.StartsWith("#", StringComparison.Ordinal)) continue;   // 모르는 메타는 무시(전방 호환)

            var p = line.Split(',');
            if (p.Length == 5
                && int.TryParse(p[0], out var idx) && int.TryParse(p[1], out var x)
                && int.TryParse(p[2], out var y) && int.TryParse(p[3], out var sw)
                && int.TryParse(p[4], out var sh))
            {
                slots.Add(new Slot { Index = idx, X = x, Y = y, Width = sw, Height = sh });
            }
        }

        // 서명이 맞아도 내용이 규격 미달이면 쓰지 않는다(빈 owner·슬롯 0개 등).
        if (!versionOk || string.IsNullOrEmpty(owner) || slots.Count == 0)
            return SlotsDecodeStatus.Malformed;

        content = new SlotsFileContent(owner!, size, slots, string.IsNullOrEmpty(dbId) ? null : dbId);
        return SlotsDecodeStatus.Ok;
    }
}
