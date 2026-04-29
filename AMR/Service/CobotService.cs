using AMR.Communication;
using AMR.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AMR.Service;

/// <summary>
/// Cobot Modbus TCP 통신 서비스 — 자동 연결/재연결 + 로봇 상태 읽기 및 제어 명령 전달
/// </summary>
public class CobotService : BackgroundService
{
    private readonly CobotModbusTcpClient _modbusClient;
    private readonly ILogger<CobotService> _logger;

    public CobotService(CobotModbusTcpClient modbusClient, ILogger<CobotService> logger)
    {
        _modbusClient = modbusClient;
        _logger = logger;
    }

    /// <summary>Modbus TCP 연결 상태</summary>
    public bool IsConnected => _modbusClient.IsConnected;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CobotService 시작");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!IsConnected)
                {
                    _logger.LogWarning("Cobot Modbus TCP 연결 시도");
                    await _modbusClient.ConnectAsync(stoppingToken);
                    _logger.LogInformation("Cobot Modbus TCP 연결 완료");
                    await EnsureAutoAndRunningAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cobot Modbus TCP 연결 실패 — 5초 후 재시도");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _modbusClient.DisconnectAsync(cancellationToken);
        _logger.LogInformation("CobotService 종료");
        await base.StopAsync(cancellationToken);
    }

    #region 초기화

    /// <summary>
    /// 코봇이 Auto 모드 + Main Program 실행 중인 상태가 되도록 보장한다.
    /// 이미 그 상태면 아무것도 하지 않으므로 연결 직후/리셋 시퀀스 양쪽에서 안전하게 호출 가능.
    /// </summary>
    public async Task EnsureAutoAndRunningAsync(CancellationToken ct = default)
    {
        var status = await ReadStatusAsync(ct);

        // Manual → Auto 전환 (RobotMode 0 = Auto)
        if (status.RobotMode != 0)
        {
            _logger.LogInformation("Cobot Manual 모드 → Auto 모드 전환");
            await ManualAutoSwitchAsync(ct);
            await Task.Delay(500, ct);
        }
        else
        {
            _logger.LogDebug("Cobot 이미 Auto 모드 — 토글 스킵");
        }

        // Main Program 실행 (OperationStatus 2 = Running)
        // 모드 전환 직후에는 OperationStatus가 갱신되지 않을 수 있으므로 다시 읽음
        status = await ReadStatusAsync(ct);
        if (status.OperationStatus != 2)
        {
            _logger.LogInformation("Cobot Main Program 시작");
            await StartMainProgramAsync(ct);
            await Task.Delay(500, ct);
        }
        else
        {
            _logger.LogDebug("Cobot Main Program 이미 실행 중 — 시작 스킵");
        }
    }

    #endregion

    #region 상태 읽기

    /// <summary>Cobot 전체 상태 읽기 (Input Register 310~322)</summary>
    public Task<CobotStatus> ReadStatusAsync(CancellationToken ct = default)
        => _modbusClient.ReadCobotStatusAsync(ct);

    #endregion

    #region 제어 명령 (Coil)

    /// <summary>일시정지</summary>
    public Task PauseAsync(CancellationToken ct = default)
        => _modbusClient.PauseAsync(ct);

    /// <summary>복구</summary>
    public Task RecoveryAsync(CancellationToken ct = default)
        => _modbusClient.RecoveryAsync(ct);

    /// <summary>시작</summary>
    public Task RunJobAsync(CancellationToken ct = default)
        => _modbusClient.StartAsync(ct);

    /// <summary>정지</summary>
    public Task StopJobAsync(CancellationToken ct = default)
        => _modbusClient.StopAsync(ct);

    /// <summary>원점 이동</summary>
    public Task MoveToJobOriginAsync(CancellationToken ct = default)
        => _modbusClient.MoveToJobOriginAsync(ct);

    /// <summary>수동/자동 전환</summary>
    public Task ManualAutoSwitchAsync(CancellationToken ct = default)
        => _modbusClient.ManualAutoSwitchAsync(ct);

    /// <summary>메인 프로그램 시작</summary>
    public Task StartMainProgramAsync(CancellationToken ct = default)
        => _modbusClient.StartMainProgramAsync(ct);

    /// <summary>전체 오류 해제</summary>
    public Task ClearAllFaultsAsync(CancellationToken ct = default)
        => _modbusClient.ClearAllFaultsAsync(ct);

    /// <summary>DI 비트 쓰기 (index: 0~127)</summary>
    public Task WriteDigitalInputAsync(ushort index, bool value, CancellationToken ct = default)
        => _modbusClient.WriteDigitalInputAsync(index, value, ct);

    #endregion

    #region DO 읽기 (Discrete Input)

    /// <summary>DO 비트 읽기 (index: 0~127)</summary>
    public Task<bool[]> ReadDigitalOutputsAsync(ushort startIndex, ushort count, CancellationToken ct = default)
        => _modbusClient.ReadRawDiscreteInputsAsync(
            (ushort)(CobotRegisterMap.DiscreteInput.DigitalOutputStart + startIndex), count, ct);

    #endregion

    #region 아날로그 입력 (Holding Register)

    /// <summary>AI 워드 쓰기 (index: 0~31)</summary>
    public Task WriteAnalogInputAsync(ushort index, ushort value, CancellationToken ct = default)
        => _modbusClient.WriteAnalogInputAsync(index, value, ct);

    #endregion
}
