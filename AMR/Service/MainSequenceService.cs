using AMR.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AMR.Service;

/// <summary>
/// 메인 시퀀스 서비스 — AMR 상태 읽기 및 MQTT 퍼블리시 오케스트레이션
/// </summary>
public class MainSequenceService : BackgroundService
{
    private readonly AmrService _amrService;
    private readonly MqttService _mqttService;
    private readonly ILogger<MainSequenceService> _logger;

    public MainSequenceService(
        AmrService amrService,
        MqttService mqttService,
        ILogger<MainSequenceService> logger)
    {
        _amrService = amrService;
        _mqttService = mqttService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MainSequenceService 시작");

        AmrStatusMessage? previousStatus = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // AMR + MQTT 모두 연결된 경우에만 상태 퍼블리시
                if (_amrService.IsConnected && _mqttService.IsConnected)
                {
                    var robotStatus = await _amrService.ReadStatusAsync(stoppingToken);
                    var statusMessage = AmrStatusMessage.FromRobotStatus(robotStatus);

                    if (!statusMessage.Equals(previousStatus))
                    {
                        await _mqttService.PublishStatusAsync(statusMessage, stoppingToken);
                        previousStatus = statusMessage;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "메인 루프 실패");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }
}
