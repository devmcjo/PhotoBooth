using System.Security.Cryptography;
using MCPhoto.Core.Models;

namespace MCPhoto.Core.Frames;

/// <summary>원본 대비 편집본의 변경 항목(이미지·슬롯·이름). (item2 §4.2)</summary>
public readonly struct FrameChange
{
    public bool ImageChanged { get; init; }
    public bool SlotsChanged { get; init; }
    public bool NameChanged { get; init; }

    /// <summary>하나라도 변경됐는지(DB 업데이트 필요 여부).</summary>
    public bool HasAnyChange => ImageChanged || SlotsChanged || NameChanged;
}

/// <summary>
/// 프레임 편집 diff 판정(순수, 테스트 대상). (item2 §4.2)
/// 이미지=바이트 길이+SHA-256, 슬롯=개수·Index/X/Y/W/H 정수 완전일치, 이름=Ordinal 문자열.
/// 크기 변경은 반드시 이미지 재로드를 동반하므로 ImageChanged에 포함(별도 플래그 없음).
/// </summary>
public static class FrameDiff
{
    /// <summary>원본 대비 편집본의 변경 여부를 판정.</summary>
    public static FrameChange Compare(
        byte[]? originalImage, byte[]? editedImage,
        IReadOnlyList<Slot> originalSlots, IReadOnlyList<Slot> editedSlots,
        string originalName, string editedName)
        => new()
        {
            ImageChanged = !ImageEqual(originalImage, editedImage),
            SlotsChanged = !SlotsEqual(originalSlots, editedSlots),
            NameChanged = !string.Equals(originalName, editedName, StringComparison.Ordinal)
        };

    /// <summary>두 슬롯 목록이 같은지: 개수 + (Index 순 정렬 후) 각 Index/X/Y/Width/Height 완전일치.</summary>
    public static bool SlotsEqual(IReadOnlyList<Slot> a, IReadOnlyList<Slot> b)
    {
        if (a.Count != b.Count) return false;
        var sa = a.OrderBy(s => s.Index).ToList();
        var sb = b.OrderBy(s => s.Index).ToList();
        for (int i = 0; i < sa.Count; i++)
        {
            if (sa[i].Index != sb[i].Index
                || sa[i].X != sb[i].X || sa[i].Y != sb[i].Y
                || sa[i].Width != sb[i].Width || sa[i].Height != sb[i].Height)
                return false;
        }
        return true;
    }

    /// <summary>
    /// 두 이미지 바이트가 같은지: 둘 다 null이면 같음, 하나만 null이면 다름(=변경, 보수적),
    /// 길이 다르면 다름, 같으면 SHA-256 비교. (item2 §4.2 C3: 원본 확보 실패 시 변경으로 간주)
    /// </summary>
    public static bool ImageEqual(byte[]? a, byte[]? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        if (a.Length != b.Length) return false;
        return SHA256.HashData(a).AsSpan().SequenceEqual(SHA256.HashData(b));
    }
}
