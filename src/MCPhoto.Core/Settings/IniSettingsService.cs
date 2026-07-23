using Microsoft.Extensions.Logging;

namespace MCPhoto.Core.Settings;

/// <summary>
/// INI 기반 설정 저장/복원. 실행경로\MCPhoto.ini 1순위, 쓰기 불가 시 %ProgramData% → %LocalAppData% 폴백. (it6 #1)
/// 손상/누락 키는 기본값 폴백(크래시 금지, WBS Step 2). 로그는 여전히 %ProgramData%\MCPhoto\logs\(변경 없음).
/// </summary>
public sealed class IniSettingsService : ISettingsService
{
    private const string Section = "MCPhoto";

    private readonly ILogger<IniSettingsService>? _logger;
    private string _iniPath; // 폴백 성공 경로로 승격될 수 있음(it3 §3.2)
    private AppSettings? _current;

    /// <param name="iniPath">테스트/커스텀용 명시 경로. null이면 기본 위치 자동 결정.</param>
    public IniSettingsService(ILogger<IniSettingsService>? logger = null, string? iniPath = null)
    {
        _logger = logger;
        _iniPath = iniPath ?? ResolveDefaultPath();
    }

    public string IniPath => _iniPath;

    public AppSettings Current => _current ??= Load();

    public AppSettings Load()
    {
        var settings = new AppSettings();
        try
        {
            if (File.Exists(_iniPath))
            {
                var ini = IniFile.Parse(File.ReadAllText(_iniPath));
                ReadInto(ini, settings);
            }
        }
        catch (Exception ex)
        {
            // 손상 파일이어도 기본값으로 진행(완료 기준: 앱 크래시 금지).
            _logger?.LogWarning(ex, "설정 로드 실패, 기본값 사용: {Path}", _iniPath);
        }

        settings.Clamp();
        _current = settings;
        return settings;
    }

    public bool Save()
    {
        var settings = _current ?? new AppSettings();
        settings.Clamp();

        var ini = new IniFile();
        WriteFrom(settings, ini);
        var content = ini.ToString();

        // 쓰기 폴백 체인(it3 §3.2): 현재 경로 → 실행 경로 → %LocalAppData%\MCPhoto\.
        // 성공한 경로를 _iniPath로 승격해 다음 저장·로드가 같은 위치를 쓰게 한다.
        foreach (var candidate in FallbackPaths())
        {
            if (TryWrite(candidate, content))
            {
                if (!string.Equals(candidate, _iniPath, StringComparison.OrdinalIgnoreCase))
                {
                    _logger?.LogInformation("설정 저장 경로 폴백 성공: {Path}", candidate);
                    _iniPath = candidate;
                }
                return true;
            }
        }

        _logger?.LogError("설정 저장 실패(모든 폴백 경로 쓰기 불가)");
        return false;
    }

    private IEnumerable<string> FallbackPaths()
    {
        yield return _iniPath;

        // 실행경로 → ProgramData → LocalAppData 순(it6 #1). 현재 경로와 중복은 건너뜀.
        foreach (var p in DefaultCandidates())
            if (!string.Equals(p, _iniPath, StringComparison.OrdinalIgnoreCase))
                yield return p;
    }

    private static IReadOnlyList<string> DefaultCandidates()
        => SettingsPathResolver.DefaultCandidates(
            AppContext.BaseDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>경로에 실제 쓰기 가능한지(디렉터리 생성 + 임시파일 쓰기·삭제)로 판정. (it6 #1)</summary>
    private static bool CanWrite(string iniPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(iniPath);
            if (string.IsNullOrEmpty(dir)) return false;
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, $".mcphoto_write_probe_{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    private bool TryWrite(string path, string content)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, content);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "설정 저장 경로 쓰기 실패(다음 폴백 시도): {Path}", path);
            return false;
        }
    }

    // ── AppSettings ↔ INI 매핑 ──

    private static void ReadInto(IniFile ini, AppSettings s)
    {
        s.CutCount = ini.GetInt(Section, nameof(s.CutCount), s.CutCount);
        s.CountdownSec = ini.GetInt(Section, nameof(s.CountdownSec), s.CountdownSec);
        s.MirrorMode = ini.GetBool(Section, nameof(s.MirrorMode), s.MirrorMode);
        s.FlashMode = ini.GetBool(Section, nameof(s.FlashMode), s.FlashMode);
        s.ShutterSound = ini.GetBool(Section, nameof(s.ShutterSound), s.ShutterSound);
        s.OutputFormat = ini.GetEnum(Section, nameof(s.OutputFormat), s.OutputFormat);
        s.RetentionHours = ini.GetInt(Section, nameof(s.RetentionHours), s.RetentionHours);
        s.EnableQrDelivery = ini.GetBool(Section, nameof(s.EnableQrDelivery), s.EnableQrDelivery);
        s.SendPhoto = ini.GetBool(Section, nameof(s.SendPhoto), s.SendPhoto);
        s.SendTimelapse = ini.GetBool(Section, nameof(s.SendTimelapse), s.SendTimelapse);
        s.FilterGrayscale = ini.GetBool(Section, nameof(s.FilterGrayscale), s.FilterGrayscale);
        s.FilterBrightness = ini.GetBool(Section, nameof(s.FilterBrightness), s.FilterBrightness);
        s.FilterBeauty = ini.GetBool(Section, nameof(s.FilterBeauty), s.FilterBeauty);
        s.SaveLocalCopy = ini.GetBool(Section, nameof(s.SaveLocalCopy), s.SaveLocalCopy);
        s.LocalSavePath = ini.GetString(Section, nameof(s.LocalSavePath), s.LocalSavePath);
        s.DisplayMode = ini.GetEnum(Section, nameof(s.DisplayMode), s.DisplayMode);
        s.CameraDevice = ini.GetInt(Section, nameof(s.CameraDevice), s.CameraDevice);
        s.HostingBaseUrl = ini.GetString(Section, nameof(s.HostingBaseUrl), s.HostingBaseUrl);
        s.StorageBucket = ini.GetString(Section, nameof(s.StorageBucket), s.StorageBucket);

        s.WindowBounds.Left = ini.GetDouble(Section, "WindowLeft", s.WindowBounds.Left);
        s.WindowBounds.Top = ini.GetDouble(Section, "WindowTop", s.WindowBounds.Top);
        s.WindowBounds.Width = ini.GetDouble(Section, "WindowWidth", s.WindowBounds.Width);
        s.WindowBounds.Height = ini.GetDouble(Section, "WindowHeight", s.WindowBounds.Height);
    }

    private static void WriteFrom(AppSettings s, IniFile ini)
    {
        ini.SetInt(Section, nameof(s.CutCount), s.CutCount);
        ini.SetInt(Section, nameof(s.CountdownSec), s.CountdownSec);
        ini.SetBool(Section, nameof(s.MirrorMode), s.MirrorMode);
        ini.SetBool(Section, nameof(s.FlashMode), s.FlashMode);
        ini.SetBool(Section, nameof(s.ShutterSound), s.ShutterSound);
        ini.SetEnum(Section, nameof(s.OutputFormat), s.OutputFormat);
        ini.SetInt(Section, nameof(s.RetentionHours), s.RetentionHours);
        ini.SetBool(Section, nameof(s.EnableQrDelivery), s.EnableQrDelivery);
        ini.SetBool(Section, nameof(s.SendPhoto), s.SendPhoto);
        ini.SetBool(Section, nameof(s.SendTimelapse), s.SendTimelapse);
        ini.SetBool(Section, nameof(s.FilterGrayscale), s.FilterGrayscale);
        ini.SetBool(Section, nameof(s.FilterBrightness), s.FilterBrightness);
        ini.SetBool(Section, nameof(s.FilterBeauty), s.FilterBeauty);
        ini.SetBool(Section, nameof(s.SaveLocalCopy), s.SaveLocalCopy);
        ini.Set(Section, nameof(s.LocalSavePath), s.LocalSavePath);
        ini.SetEnum(Section, nameof(s.DisplayMode), s.DisplayMode);
        ini.SetInt(Section, nameof(s.CameraDevice), s.CameraDevice);
        ini.Set(Section, nameof(s.HostingBaseUrl), s.HostingBaseUrl);
        ini.Set(Section, nameof(s.StorageBucket), s.StorageBucket);

        ini.SetDouble(Section, "WindowLeft", s.WindowBounds.Left);
        ini.SetDouble(Section, "WindowTop", s.WindowBounds.Top);
        ini.SetDouble(Section, "WindowWidth", s.WindowBounds.Width);
        ini.SetDouble(Section, "WindowHeight", s.WindowBounds.Height);
    }

    /// <summary>
    /// 기본 INI 경로: 실행경로\MCPhoto.ini 1순위, 쓰기 불가 시 %ProgramData% → %LocalAppData% 폴백. (it6 #1)
    /// 쓰기 가능 판정은 실제 쓰기 시도 기반.
    /// </summary>
    private static string ResolveDefaultPath()
        => SettingsPathResolver.ResolveWritable(DefaultCandidates(), CanWrite);
}
