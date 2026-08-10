using System.Globalization;
using System.Text;

namespace MCPhoto.Core.Settings;

/// <summary>
/// 경량 INI 파서/작성기(자체 구현, 외부 의존성 없음). (architecture §1.2)
/// - 단일 섹션 그룹 지원, `key=value`, `;`/`#` 주석 무시, 대소문자 무시 키.
/// - 손상/누락 라인은 예외 없이 건너뜀(크래시 금지, WBS Step 2 완료 기준).
/// </summary>
public sealed class IniFile
{
    // section → (key → value). 키는 대소문자 무시.
    private readonly Dictionary<string, Dictionary<string, string>> _sections =
        new(StringComparer.OrdinalIgnoreCase);

    private const string DefaultSection = "";

    /// <summary>텍스트에서 파싱. 손상 라인은 무시.</summary>
    public static IniFile Parse(string content)
    {
        var ini = new IniFile();
        var current = DefaultSection;
        ini.EnsureSection(current);

        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line[0] is ';' or '#') continue;

            if (line[0] == '[' && line[^1] == ']')
            {
                current = line[1..^1].Trim();
                ini.EnsureSection(current);
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq <= 0) continue; // 손상 라인 무시

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (key.Length == 0) continue;

            ini._sections[current][key] = value;
        }

        return ini;
    }

    private void EnsureSection(string section)
    {
        if (!_sections.ContainsKey(section))
            _sections[section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public void Set(string section, string key, string value)
    {
        EnsureSection(section);
        _sections[section][key] = value;
    }

    /// <summary>섹션이 파일에 존재했는지(키가 하나도 없어도 헤더가 있었다면 true).</summary>
    public bool HasSection(string section) => _sections.ContainsKey(section);

    /// <summary>
    /// 이 딕셔너리가 갖지 <b>않은</b> 섹션을 <paramref name="source"/>에서 그대로 가져온다(외래 섹션 보존).
    /// 이미 존재하는 섹션(예: <c>[MCPhoto]</c>)은 건드리지 않는다 — 소유자가 방금 채운 값이 정본이다.
    /// <para>
    /// 왜 필요한가: <see cref="IniSettingsService.Save"/>는 빈 <see cref="IniFile"/>에 자기 섹션만 채워
    /// 파일을 통째로 덮어쓴다. 그래서 사람이 손으로 넣은 다른 섹션(<c>[Test]</c> 등)이 첫 저장에 사라졌다.
    /// 저장 직전 대상 파일을 읽어 이 메서드로 실어 보내면 그 섹션이 살아남는다.
    /// </para>
    /// <para>
    /// ⚠️ <b>키 단위 병합은 하지 않는다.</b> <c>[MCPhoto]</c> 안의 미매핑 키를 되살리면 오탈자 키
    /// (<c>Cutcount=8</c>)와 폐기된 키가 영구히 남는다 — 섹션 단위가 정확한 소유 경계다.
    /// </para>
    /// 이름이 <c>Merge</c>가 아닌 이유: 방향과 범위를 이름이 말해야 한다("없는 섹션만, source에서 이쪽으로").
    /// </summary>
    public void AdoptMissingSections(IniFile source)
    {
        if (source is null) return;
        foreach (var (section, kvs) in source._sections)
        {
            // 기본(무명) 섹션도 대상 — 파일 선두에 섹션 없이 적힌 줄을 파서가 여기에 담는다.
            if (_sections.ContainsKey(section)) continue;
            _sections[section] = new Dictionary<string, string>(kvs, StringComparer.OrdinalIgnoreCase);
        }
    }

    public string? Get(string section, string key)
        => _sections.TryGetValue(section, out var s) && s.TryGetValue(key, out var v) ? v : null;

    // ── 타입별 안전 파싱(파싱 실패 시 기본값 폴백) ──

    public string GetString(string section, string key, string fallback)
        => Get(section, key) ?? fallback;

    public int GetInt(string section, string key, int fallback)
        => int.TryParse(Get(section, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public double GetDouble(string section, string key, double fallback)
        => double.TryParse(Get(section, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public bool GetBool(string section, string key, bool fallback)
    {
        var s = Get(section, key);
        if (s is null) return fallback;
        return s.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "on" or "yes" => true,
            "false" or "0" or "off" or "no" => false,
            _ => fallback
        };
    }

    public T GetEnum<T>(string section, string key, T fallback) where T : struct, Enum
        => Enum.TryParse<T>(Get(section, key), ignoreCase: true, out var v) ? v : fallback;

    public void SetInt(string section, string key, int value)
        => Set(section, key, value.ToString(CultureInfo.InvariantCulture));

    public void SetDouble(string section, string key, double value)
        => Set(section, key, value.ToString(CultureInfo.InvariantCulture));

    public void SetBool(string section, string key, bool value)
        => Set(section, key, value ? "true" : "false");

    public void SetEnum<T>(string section, string key, T value) where T : struct, Enum
        => Set(section, key, value.ToString());

    /// <summary>INI 텍스트로 직렬화(섹션 순서: 기본 섹션 먼저).</summary>
    public override string ToString()
    {
        var sb = new StringBuilder();

        // 기본(무명) 섹션
        if (_sections.TryGetValue(DefaultSection, out var root) && root.Count > 0)
        {
            foreach (var kv in root)
                sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\n');
        }

        foreach (var (section, kvs) in _sections)
        {
            if (section == DefaultSection || kvs.Count == 0) continue;
            sb.Append('[').Append(section).Append("]\n");
            foreach (var kv in kvs)
                sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\n');
        }

        return sb.ToString();
    }
}
