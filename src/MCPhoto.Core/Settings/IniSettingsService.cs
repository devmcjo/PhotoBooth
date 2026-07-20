using Microsoft.Extensions.Logging;

namespace MCPhoto.Core.Settings;

/// <summary>
/// INI 기반 설정 저장/복원. %ProgramData%\MCPhoto\MCPhoto.ini 우선, 실패 시 실행 경로. (architecture §7)
/// 손상/누락 키는 기본값 폴백(크래시 금지, WBS Step 2).
/// </summary>
public sealed class IniSettingsService : ISettingsService
{
    private const string Section = "MCPhoto";

    private readonly ILogger<IniSettingsService>? _logger;
    private readonly string _iniPath;
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

    public void Save()
    {
        var settings = _current ?? new AppSettings();
        settings.Clamp();
        try
        {
            var dir = Path.GetDirectoryName(_iniPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var ini = new IniFile();
            WriteFrom(settings, ini);
            File.WriteAllText(_iniPath, ini.ToString());
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "설정 저장 실패: {Path}", _iniPath);
        }
    }

    // ── AppSettings ↔ INI 매핑 ──

    private static void ReadInto(IniFile ini, AppSettings s)
    {
        s.CutCount = ini.GetInt(Section, nameof(s.CutCount), s.CutCount);
        s.CountdownSec = ini.GetInt(Section, nameof(s.CountdownSec), s.CountdownSec);
        s.MirrorMode = ini.GetBool(Section, nameof(s.MirrorMode), s.MirrorMode);
        s.FlashMode = ini.GetBool(Section, nameof(s.FlashMode), s.FlashMode);
        s.OutputFormat = ini.GetEnum(Section, nameof(s.OutputFormat), s.OutputFormat);
        s.RetentionHours = ini.GetInt(Section, nameof(s.RetentionHours), s.RetentionHours);
        s.EnableQrDelivery = ini.GetBool(Section, nameof(s.EnableQrDelivery), s.EnableQrDelivery);
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
        ini.SetEnum(Section, nameof(s.OutputFormat), s.OutputFormat);
        ini.SetInt(Section, nameof(s.RetentionHours), s.RetentionHours);
        ini.SetBool(Section, nameof(s.EnableQrDelivery), s.EnableQrDelivery);
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

    /// <summary>기본 INI 경로: %ProgramData%\MCPhoto\MCPhoto.ini(쓰기 가능 시), 아니면 실행 경로.</summary>
    private static string ResolveDefaultPath()
    {
        var programData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MCPhoto");
        try
        {
            Directory.CreateDirectory(programData);
            return Path.Combine(programData, "MCPhoto.ini");
        }
        catch
        {
            var exeDir = AppContext.BaseDirectory;
            return Path.Combine(exeDir, "MCPhoto.ini");
        }
    }
}
