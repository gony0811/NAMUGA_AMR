using AMR.Communication;
using AMR.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AMR.Service;

/// <summary>타워램프 색상</summary>
public enum TowerLampColor
{
    Red,
    Yellow,
    Green
}

/// <summary>
/// LS산전 XEL-BSSRT I/O 모듈 Modbus TCP 통신 서비스 —
/// 자동 연결/재연결 + 입력 모니터링 + 램프/버저/서보 제어.
/// </summary>
public class IoModuleService : BackgroundService
{
    private readonly IoModuleModbusTcpClient _modbusClient;
    private readonly ILogger<IoModuleService> _logger;

    private IoModuleInputStatus? _currentInputs;
    private bool _lastEmoState;

    public IoModuleService(IoModuleModbusTcpClient modbusClient, ILogger<IoModuleService> logger)
    {
        _modbusClient = modbusClient;
        _logger = logger;
    }

    /// <summary>Modbus TCP 연결 상태</summary>
    public bool IsConnected => _modbusClient.IsConnected;

    /// <summary>가장 최근에 폴링한 입력 상태 (연결 전/폴링 전에는 null)</summary>
    public IoModuleInputStatus? CurrentInputs => _currentInputs;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("IoModuleService 시작");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!IsConnected)
                {
                    _logger.LogWarning("I/O Module Modbus TCP 연결 시도");
                    await _modbusClient.ConnectAsync(stoppingToken);
                    _logger.LogInformation("I/O Module Modbus TCP 연결 완료");
                }
                else
                {
                    var inputs = await _modbusClient.ReadInputsAsync(stoppingToken);
                    _currentInputs = inputs;

                    if (inputs.Emo && !_lastEmoState)
                        _logger.LogWarning("EMO(비상정지) 활성 감지 — X000 ON");
                    else if (!inputs.Emo && _lastEmoState)
                        _logger.LogInformation("EMO(비상정지) 해제 — X000 OFF");

                    _lastEmoState = inputs.Emo;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "I/O Module 통신 실패 — 5초 후 재시도");
            }

            await Task.Delay(TimeSpan.FromSeconds(IsConnected ? 1 : 5), stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _modbusClient.DisconnectAsync(cancellationToken);
        _logger.LogInformation("IoModuleService 종료");
        await base.StopAsync(cancellationToken);
    }

    #region 읽기

    /// <summary>입력(X000~X005) 읽기</summary>
    public Task<IoModuleInputStatus> ReadInputsAsync(CancellationToken ct = default)
        => _modbusClient.ReadInputsAsync(ct);

    /// <summary>출력(Y000~Y005) 현재 상태 읽기</summary>
    public Task<IoModuleOutputStatus> ReadOutputsAsync(CancellationToken ct = default)
        => _modbusClient.ReadOutputsAsync(ct);

    #endregion

    #region 출력 제어

    public Task SetTowerLampRedAsync(bool value, CancellationToken ct = default)
        => _modbusClient.SetTowerLampRedAsync(value, ct);

    public Task SetTowerLampYellowAsync(bool value, CancellationToken ct = default)
        => _modbusClient.SetTowerLampYellowAsync(value, ct);

    public Task SetTowerLampGreenAsync(bool value, CancellationToken ct = default)
        => _modbusClient.SetTowerLampGreenAsync(value, ct);

    public Task SetTowerLampBuzzerAsync(bool value, CancellationToken ct = default)
        => _modbusClient.SetTowerLampBuzzerAsync(value, ct);

    public Task SetResetSwLampAsync(bool value, CancellationToken ct = default)
        => _modbusClient.SetResetSwLampAsync(value, ct);

    public Task SetCobotServoAsync(bool value, CancellationToken ct = default)
        => _modbusClient.SetCobotServoAsync(value, ct);

    /// <summary>타워램프 색상별 편의 제어</summary>
    public Task SetTowerLampAsync(TowerLampColor color, bool value, CancellationToken ct = default)
        => color switch
        {
            TowerLampColor.Red => SetTowerLampRedAsync(value, ct),
            TowerLampColor.Yellow => SetTowerLampYellowAsync(value, ct),
            TowerLampColor.Green => SetTowerLampGreenAsync(value, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(color))
        };

    /// <summary>타워램프 R/Y/G 일괄 OFF</summary>
    public Task AllTowerLampsOffAsync(CancellationToken ct = default)
        => _modbusClient.AllTowerLampsOffAsync(ct);

    #endregion
}
