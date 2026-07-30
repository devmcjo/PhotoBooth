using System.Diagnostics;
using System.IO;
using MCPhoto.Core.Capture;
using Microsoft.Extensions.Logging;

namespace MCPhoto.Capture;

/// <summary>
/// ffmpeg.exe 프로세스 러너. 녹화(stdin rawvideo 파이프)와 변환(타임랩스)을 실행한다. (architecture §2.5)
/// ffmpeg 경로는 번들(tools/ffmpeg/ffmpeg.exe) 우선, 주입 가능(바이너리 부재 시 경로만 교체).
/// </summary>
public sealed class FfmpegRunner : IDisposable
{
    private readonly ILogger? _logger;
    private readonly string _ffmpegPath;

    private Process? _recordProcess;
    private Stream? _stdin;
    private readonly object _recordLock = new();

    public FfmpegRunner(string? ffmpegPath = null, ILogger? logger = null)
    {
        _logger = logger;
        _ffmpegPath = ffmpegPath ?? ResolveFfmpegPath();
    }

    public string FfmpegPath => _ffmpegPath;

    /// <summary>ffmpeg 실행 파일이 실제 존재하는지(실연동 가능 여부).</summary>
    public bool IsAvailable => File.Exists(_ffmpegPath);

    /// <summary>
    /// 번들 경로 우선 해결: {BaseDir}/tools/ffmpeg/ffmpeg.exe → {BaseDir}/ffmpeg.exe → PATH의 "ffmpeg".
    /// </summary>
    public static string ResolveFfmpegPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "tools", "ffmpeg", "ffmpeg.exe"),
            Path.Combine(baseDir, "ffmpeg.exe"),
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        // PATH 폴백(개발 환경)
        return "ffmpeg";
    }

    // ── 녹화(stdin 파이프) ──

    /// <summary>녹화 시작. 이후 <see cref="WriteFrame"/>로 프레임 바이트를 write.</summary>
    public bool StartRecording(int width, int height, int framerate, string outputPath)
    {
        lock (_recordLock)
        {
            if (_recordProcess is not null) return true;

            var args = FfmpegArgs.BuildRecordArgs(width, height, framerate, outputPath);
            var psi = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            try
            {
                _recordProcess = Process.Start(psi);
                if (_recordProcess is null)
                {
                    _logger?.LogError("ffmpeg 프로세스 시작 실패");
                    return false;
                }
                _stdin = _recordProcess.StandardInput.BaseStream;

                // stderr 비우기(버퍼 블로킹 방지)
                _ = Task.Run(async () =>
                {
                    try { await _recordProcess.StandardError.ReadToEndAsync(); }
                    catch { /* 종료 시 무시 */ }
                });

                _logger?.LogInformation("녹화 시작: {Output} ({W}x{H}@{Fps})", outputPath, width, height, framerate);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "ffmpeg 녹화 시작 오류");
                _recordProcess = null;
                _stdin = null;
                return false;
            }
        }
    }

    /// <summary>가공 프레임 바이트(BGR24)를 stdin에 write. 백프레셔 시 드롭 허용(프리뷰 우선).</summary>
    public void WriteFrame(byte[] pixels, int length)
    {
        var stdin = _stdin;
        if (stdin is null) return;
        try
        {
            stdin.Write(pixels, 0, length);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "프레임 write 실패(드롭)");
        }
    }

    /// <summary>녹화 종료. stdin flush+close 후 프로세스 종료 대기(moov atom 완성).</summary>
    public async Task StopRecordingAsync()
    {
        Process? proc;
        Stream? stdin;
        lock (_recordLock)
        {
            proc = _recordProcess;
            stdin = _stdin;
            _recordProcess = null;
            _stdin = null;
        }

        if (proc is null) return;

        try
        {
            if (stdin is not null)
            {
                await stdin.FlushAsync();
                stdin.Close(); // EOF → ffmpeg가 mp4 마무리
            }

            // 인코딩 마무리 대기(최대 30초)
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await proc.WaitForExitAsync(cts.Token);
            // exit≠0 은 녹화가 실패한 것이다 — session.mp4 가 없거나 0바이트이므로 타임랩스도
            // 만들어지지 않는다. INF 로 남기면 정상 종료와 섞여 묻히므로 WARN 으로 올린다.
            if (proc.ExitCode != 0)
                _logger?.LogWarning("녹화 실패(exit={Code}) — session.mp4 무효, 타임랩스 생성 불가", proc.ExitCode);
            else
                _logger?.LogInformation("녹화 종료(exit={Code})", proc.ExitCode);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "녹화 종료 오류");
            try { if (!proc.HasExited) proc.Kill(); } catch { /* 무시 */ }
        }
        finally
        {
            proc.Dispose();
        }
    }

    // ── 변환 실행(타임랩스) ──

    /// <summary>인자를 받아 ffmpeg를 실행하고 종료 대기. 성공 시 true.</summary>
    public async Task<bool> RunAsync(string arguments, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null) return false;

            var stderrTask = proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync(ct);
            var stderr = await stderrTask;

            if (proc.ExitCode != 0)
            {
                _logger?.LogError("ffmpeg 실패(exit={Code}): {Err}", proc.ExitCode, Tail(stderr));
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "ffmpeg 실행 오류: {Args}", arguments);
            return false;
        }
    }

    private static string Tail(string s, int n = 500)
        => s.Length <= n ? s : s[^n..];

    public void Dispose()
    {
        try
        {
            var proc = _recordProcess;
            if (proc is not null && !proc.HasExited)
                proc.Kill();
            proc?.Dispose();
        }
        catch { /* 무시 */ }
    }
}
