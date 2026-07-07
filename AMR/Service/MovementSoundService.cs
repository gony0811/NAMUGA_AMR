using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AMR.Enums;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AMR.Service;

/// <summary>
/// AMR이 이동 중(RobotState=Started)일 때 시스템 스피커로 짧은 사운드를 반복 재생.
/// 웹 UI 에서 Enabled 토글로 ON/OFF 가능. BackgroundService 라 웹 클라이언트가
/// 닫혀도 계속 동작한다.
///
/// 재생 방식: WAV 파일을 winmm.dll PlaySound 로 재생.
///   - Console.Beep 은 음량 조절 불가 + OS 믹싱에 따라 들리는 크기가 들쭉날쭉했음.
///   - WAV 파일은 사전 녹음된 일정 음량이라 재생할 때마다 동일한 소리.
///   - PlaySound 는 SND_ASYNC 로 비동기 재생 — 폴링 스레드를 블로킹하지 않음.
///
/// WAV 파일 위치: exe(또는 dll) 폴더의 sounds/movement.wav
///   파일이 없으면 Console.Beep 으로 fallback (소리는 나되 음량 일정치 않음).
/// </summary>
public class MovementSoundService : BackgroundService
{
    private readonly AmrService _amrService;
    private readonly ILogger<MovementSoundService> _logger;

    private readonly string _wavPath;
    private bool _wavExists;

    /// <summary>비프/사운드 재생 활성화 여부 (런타임 토글)</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>재생 주기(ms) — 한 사이클 = 사운드 재생 + 휴식</summary>
    public int IntervalMs { get; set; } = 1500;

    /// <summary>도-미-솔 음 (Hz) — WAV 미존재 시 Console.Beep fallback 용</summary>
    public int[] MelodyFrequencies { get; set; } = { 523, 659, 784 };

    /// <summary>각 음의 길이 (ms) — Console.Beep fallback 용</summary>
    public int NoteDurationMs { get; set; } = 150;

    /// <summary>가장 최근 폴링 시점의 AMR 상태 (UI 표시용)</summary>
    public RobotState? LastRobotState { get; private set; }

    /// <summary>현재 WAV 파일로 재생 중인지 (false = Console.Beep fallback)</summary>
    public bool UsingWavFile => _wavExists;

    public MovementSoundService(AmrService amrService, ILogger<MovementSoundService> logger)
    {
        _amrService = amrService;
        _logger = logger;

        _wavPath = Path.Combine(AppContext.BaseDirectory, "sounds", "movement.wav");
        _wavExists = File.Exists(_wavPath);
    }

    #region winmm.dll PlaySound

    [Flags]
    private enum PlaySoundFlags : uint
    {
        SND_SYNC = 0x0000,       // 동기 재생 (재생 끝날 때까지 블로킹)
        SND_ASYNC = 0x0001,      // 비동기 재생 (즉시 반환)
        SND_NODEFAULT = 0x0002,  // 못 찾아도 기본음 안 냄
        SND_FILENAME = 0x00020000, // pszSound 가 파일 경로
        SND_NOSTOP = 0x0010      // 이미 재생 중이면 중단 안 함
    }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool PlaySound(string? pszSound, IntPtr hmod, PlaySoundFlags fdwSound);

    #endregion

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "MovementSoundService 시작 (Enabled={Enabled}, WAV={WavExists}: {Path})",
            Enabled, _wavExists, _wavPath);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var status = await _amrService.ReadStatusAsync(stoppingToken);
                LastRobotState = status.RobotState;

                if (Enabled && status.RobotState == RobotState.Started)
                {
                    PlaySound();
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "MovementSound 폴링 실패");
            }

            await Task.Delay(IntervalMs, stoppingToken);
        }
    }

    private void PlaySound()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            // 파일 존재 여부는 매번 다시 확인하지 않음 — 시작 시점 1회 캐시.
            // (운영 중 파일이 추가되면 재시작 후 반영)
            if (_wavExists)
            {
                // SND_ASYNC: 즉시 반환 → 폴링 스레드 블로킹 안 함
                // SND_NODEFAULT: 못 읽어도 윈도우 기본음 안 냄
                PlaySoundWav();
            }
            else
            {
                // fallback — WAV 없을 때만 Console.Beep
                PlayMelodyWindows(MelodyFrequencies, NoteDurationMs);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "이동 사운드 재생 실패");
        }
    }

    [SupportedOSPlatform("windows")]
    private void PlaySoundWav()
    {
        // SND_NOSTOP 없이 호출 → 이전 재생이 안 끝났으면 새로 시작 (겹침 대신 재시작).
        // SND_ASYNC 라 중첩 호출돼도 음량이 누적되지 않음 (한 번에 하나만 재생).
        var ok = PlaySound(_wavPath, IntPtr.Zero,
            PlaySoundFlags.SND_FILENAME | PlaySoundFlags.SND_ASYNC | PlaySoundFlags.SND_NODEFAULT);

        if (!ok)
            _logger.LogDebug("PlaySound 실패 (파일: {Path})", _wavPath);
    }

    [SupportedOSPlatform("windows")]
    private static void PlayMelodyWindows(int[] frequencies, int durationMs)
    {
        foreach (var freq in frequencies)
        {
            Console.Beep(freq, durationMs);
        }
    }
}
