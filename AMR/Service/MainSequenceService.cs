using AMR.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AMR.Service;

/// <summary>
/// 메인 시퀀스 서비스 — 전체 서비스 연결 및 동작 시퀀스 관리
/// </summary>
public class MainSequenceService : BackgroundService
{
    private readonly AmrService _amrService;
    private readonly CobotService _cobotService;
    private readonly MqttService _mqttService;
    private readonly ILogger<MainSequenceService> _logger;

    public MainSequenceService(
        AmrService amrService,
        CobotService cobotService,
        MqttService mqttService,
        ILogger<MainSequenceService> logger)
    {
        _amrService = amrService;
        _cobotService = cobotService;
        _mqttService = mqttService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MainSequenceService 시작");

        // AMR Modbus TCP, Cobot Modbus TCP, MQTT 브로커를 병렬로 독립 연결
        var amrTask = ConnectWithRetryAsync(
            "AMR Modbus TCP",
            () => _amrService.IsConnected,
            ct => _amrService.ConnectAsync(ct),
            stoppingToken);

        var cobotTask = ConnectWithRetryAsync(
            "Cobot Modbus TCP",
            () => _cobotService.IsConnected,
            ct => _cobotService.ConnectAsync(ct),
            stoppingToken);

        var mqttTask = ConnectWithRetryAsync(
            "MQTT 브로커",
            () => _mqttService.IsConnected,
            ct => _mqttService.ConnectAsync(ct),
            stoppingToken);

        await Task.WhenAll(amrTask, cobotTask, mqttTask);

        _logger.LogInformation("모든 서비스 연결 완료 — 상태 변경 감지 시 퍼블리시 시작 (1초 폴링)");

        AmrStatusMessage? previousStatus = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // AMR 연결이 끊어진 경우 재연결 시도
                if (!_amrService.IsConnected)
                {
                    _logger.LogWarning("AMR Modbus TCP 연결 끊김 — 재연결 시도");
                    await _amrService.ConnectAsync(stoppingToken);
                    _logger.LogInformation("AMR Modbus TCP 재연결 완료");
                    previousStatus = null;
                }

                // Cobot 연결이 끊어진 경우 재연결 시도
                if (!_cobotService.IsConnected)
                {
                    _logger.LogWarning("Cobot Modbus TCP 연결 끊김 — 재연결 시도");
                    await _cobotService.ConnectAsync(stoppingToken);
                    _logger.LogInformation("Cobot Modbus TCP 재연결 완료");
                }

                // AMR 상태 읽기 및 MQTT 발행
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
                _logger.LogWarning(ex, "AMR 메인 루프 실패");
            }

            // Cobot 상태 읽기 (AMR와 독립적으로 처리)
            try
            {
                if (_cobotService.IsConnected)
                {
                    var cobotStatus = await _cobotService.ReadStatusAsync(stoppingToken);
                    _logger.LogInformation(
                        "Cobot 상태: Enable={Enable}, Mode={Mode}, Status={Status}, Fault={Fault}, SubFault={SubFault}",
                        cobotStatus.EnableState, cobotStatus.RobotMode, cobotStatus.OperationStatus,
                        cobotStatus.MasterFaultCode, cobotStatus.SubFaultCode);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cobot 상태 읽기 실패");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("MainSequenceService 종료 중...");

        await _mqttService.DisconnectAsync();
        _amrService.Disconnect();
        _cobotService.Disconnect();

        await base.StopAsync(cancellationToken);

        _logger.LogInformation("MainSequenceService 종료 완료");
    }

    private async Task ConnectWithRetryAsync(
        string serviceName,
        Func<bool> isConnected,
        Func<CancellationToken, Task> connectAsync,
        CancellationToken ct)
    {
        while (!isConnected() && !ct.IsCancellationRequested)
        {
            try
            {
                await connectAsync(ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{ServiceName} 연결 실패. 5초 후 재시도합니다.", serviceName);
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }
}
