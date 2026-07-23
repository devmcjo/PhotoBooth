using System;
using System.IO;
using System.Media;

namespace MCPhoto.App;

/// <summary>
/// 효과음 재생. 셔터음은 실행 폴더의 Assets\shutter.wav 가 있으면 그대로 재생하고,
/// 없으면 메모리에서 합성한 "찰칵" 카메라 셔터음(PCM WAV)을 재생한다.
/// 오디오 실패는 조용히 무시(촬영 흐름 방해 금지). (기능#7)
/// </summary>
public static class SoundEffects
{
    /// <summary>
    /// 셔터음 재생. Assets/shutter.wav가 있으면 우선 재생(사용자 커스텀 음원 지원),
    /// 없으면 합성 셔터 클릭음으로 폴백. 비차단·백그라운드 재생, 실패는 무시.
    /// </summary>
    public static void PlayShutter()
    {
        try
        {
            var wav = Path.Combine(AppContext.BaseDirectory, "Assets", "shutter.wav");
            if (File.Exists(wav))
            {
                new SoundPlayer(wav).Play();   // 비동기 재생(백그라운드)
            }
            else
            {
                // Load()로 스트림 데이터를 SoundPlayer 내부 버퍼에 동기적으로 전부 읽어들인 뒤
                // 재생한다. Play()는 원래도 내부적으로 스트림 전체를 동기 로드하고서
                // OS(PlaySound) 비동기 재생을 시작하므로 stream/player가 이후 GC되어도
                // 재생에는 영향이 없으나, 명시적으로 순서를 드러내 안전성을 분명히 한다.
                using var stream = new MemoryStream(ShutterSoundGenerator.CreateShutterClickWav());
                var player = new SoundPlayer(stream);
                player.Load();   // 동기 로드: 스트림 데이터를 내부 버퍼로 즉시 전부 읽음
                player.Play();   // 합성 "찰칵"음, 비동기 재생(OS가 담당)
            }
        }
        catch { /* 무시: 효과음 실패가 촬영 흐름을 막아서는 안 됨 */ }
    }
}

/// <summary>
/// 카메라 셔터 "찰칵" 소리를 순수 PCM 합성으로 생성한다(외부 자산 불필요).
/// WAV 바이트 생성 로직을 부수효과 없는 순수 함수로 분리해 단위 테스트가 용이하도록 함.
/// </summary>
public static class ShutterSoundGenerator
{
    private const int SampleRate = 44100;

    /// <summary>
    /// DSLR 셔터를 흉내낸 "딸깍-딸깍" 2연타 클릭음을 생성한다.
    /// 각 클릭은 급격한 임펄스 + 지수 감쇠 화이트노이즈로 구성되어 "찰칵" 질감을 낸다.
    /// 전체 길이는 약 150ms(요구사항 120~180ms 범위).
    /// </summary>
    /// <returns>RIFF/WAVE(PCM 16bit mono 44100Hz) 형식의 완전한 WAV 바이트 배열</returns>
    public static byte[] CreateShutterClickWav()
    {
        const double totalSeconds = 0.15;       // 전체 길이 150ms
        const double click1Start = 0.0;         // 첫 클릭(미러 업) 시작
        const double gapSeconds = 0.05;         // 두 클릭 간격 50ms
        const double clickDuration = 0.02;      // 각 클릭의 감쇠 구간 길이 20ms
        const double decayRate = 260.0;         // 지수 감쇠 상수(빠르게 사그라듦)

        int totalSamples = (int)(totalSeconds * SampleRate);
        var samples = new short[totalSamples];

        AddClick(samples, click1Start, clickDuration, decayRate, amplitude: 0.9);
        AddClick(samples, click1Start + gapSeconds, clickDuration, decayRate, amplitude: 0.6);

        return EncodeWav(samples, SampleRate);
    }

    /// <summary>
    /// 지정 시작 시점에 임펄스+지수감쇠 화이트노이즈 클릭을 samples 버퍼에 가산 합성한다.
    /// </summary>
    private static void AddClick(short[] samples, double startSeconds, double duration, double decayRate, double amplitude)
    {
        var random = new Random(unchecked((int)(startSeconds * 1_000_000)));
        int startIndex = (int)(startSeconds * SampleRate);
        int lengthSamples = (int)(duration * SampleRate);

        for (int i = 0; i < lengthSamples; i++)
        {
            int idx = startIndex + i;
            if (idx < 0 || idx >= samples.Length) continue;

            double t = i / (double)SampleRate;
            // 지수 감쇠 엔벨로프
            double envelope = Math.Exp(-decayRate * t);
            // 화이트노이즈(-1..1)에 엔벨로프를 곱해 "찰칵" 질감 노이즈 버스트 생성
            double noise = (random.NextDouble() * 2.0 - 1.0) * envelope;
            // 맨 앞부분에 짧은 임펄스(기계식 셔터의 "딱" 타격감)를 추가
            double impulse = i < 6 ? (1.0 - i / 6.0) : 0.0;

            double value = amplitude * (noise * 0.7 + impulse * 0.9);

            // 기존 값에 가산(클리핑 방지 위해 -1..1로 클램프 후 스케일)
            double existing = samples[idx] / 32767.0;
            double mixed = Math.Clamp(existing + value, -1.0, 1.0);
            samples[idx] = (short)(mixed * short.MaxValue);
        }
    }

    /// <summary>
    /// PCM 16bit mono 샘플 배열을 완전한 RIFF/WAVE 바이트 스트림으로 인코딩한다(헤더 44바이트 + 데이터).
    /// </summary>
    private static byte[] EncodeWav(short[] samples, int sampleRate)
    {
        const short bitsPerSample = 16;
        const short channels = 1;
        int dataSize = samples.Length * sizeof(short);
        int byteRate = sampleRate * channels * (bitsPerSample / 8);
        short blockAlign = (short)(channels * (bitsPerSample / 8));

        using var stream = new MemoryStream(44 + dataSize);
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            // RIFF 헤더
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataSize);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

            // fmt 청크
            writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);                     // fmt 청크 크기
            writer.Write((short)1);                // PCM 포맷
            writer.Write(channels);
            writer.Write(sampleRate);
            writer.Write(byteRate);
            writer.Write(blockAlign);
            writer.Write(bitsPerSample);

            // data 청크
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(dataSize);
            foreach (var sample in samples)
                writer.Write(sample);
        }

        return stream.ToArray();
    }
}
