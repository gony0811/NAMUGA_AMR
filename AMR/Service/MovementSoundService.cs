using System.Runtime.Versioning;
using AMR.Enums;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AMR.Service;

/// <summary>
/// AMR이 이동 중(RobotState=Started)일 때 시스템 스피커로 짧은 멜로디를 반복 재생.
/// 웹 UI 에서 Enabled 토글로 ON/OFF 가능. BackgroundService 라 웹 클라이언트가
/// 닫혀도 계속 동작한다.
/// </summary>
public class MovementSoundService : BackgroundService
{
    private readonly AmrService _amrService;
    private readonly ILogger<MovementSoundService> _logger;

    /// <summary>비프 멜로디 재생 활성화 여부 (런타임 토글)</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>재생 주기(ms) — 한 사이클 = 멜로디 + 휴식</summary>
    public int IntervalMs { get; set; } = 1000;

    /// <summary>도-미-솔 음 (Hz)</summary>
    public int[] MelodyFrequencies { get; set; } = { 523, 659, 784 };

    /// <summary>각 음의 길이 (ms)</summary>
    public int NoteDurationMs { get; set; } = 150;

    /// <summary>가장 최근 폴링 시점의 AMR 상태 (UI 표시용)</summary>
    public RobotState? LastRobotState { get; private set; }

    public MovementSoundService(AmrService amrService, ILogger<MovementSoundService> logger)
    {
        _amrService = amrService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MovementSoundService 시작 (Enabled={Enabled})", Enabled);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var status = await _amrService.ReadStatusAsync(stoppingToken);
                LastRobotState = status.RobotState;

                if (Enabled && status.RobotState == RobotState.Started)
                {
                    PlayMelody();
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

    private void PlayMelody()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            PlayMelodyWindows(MelodyFrequencies, NoteDurationMs);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "비프 멜로디 재생 실패");
        }
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
