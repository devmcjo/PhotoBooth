using System.IO;
using System.Media;

namespace MCPhoto.App;

/// <summary>
/// 효과음 재생. 셔터음은 실행 폴더의 Assets\shutter.wav 가 있으면 재생, 없으면 시스템음으로 폴백.
/// 오디오 실패는 조용히 무시(촬영 흐름 방해 금지). (기능#7)
/// </summary>
public static class SoundEffects
{
    public static void PlayShutter()
    {
        try
        {
            var wav = Path.Combine(AppContext.BaseDirectory, "Assets", "shutter.wav");
            if (File.Exists(wav))
                new SoundPlayer(wav).Play();   // 비동기 재생(백그라운드)
            else
                SystemSounds.Asterisk.Play();  // 폴백(자산 미동봉 시)
        }
        catch { /* 무시 */ }
    }
}
