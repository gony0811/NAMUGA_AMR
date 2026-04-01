using AMR.Communication;
using AMR.Models;
using Microsoft.Extensions.Logging;

namespace AMR.Service;

/// <summary>
/// Cobot Modbus TCP 통신 서비스 — 로봇 상태 읽기 및 제어 명령 전달
/// </summary>
public class CobotService
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

    #region 연결 관리

    /// <summary>Modbus TCP 연결</summary>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        await _modbusClient.ConnectAsync(ct);
        _logger.LogInformation("Cobot Modbus TCP 연결 완료");
    }

    /// <summary>Modbus TCP 연결 해제</summary>
    public void Disconnect()
    {
        _modbusClient.Disconnect();
        _logger.LogInformation("Cobot Modbus TCP 연결 해제");
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
    public Task StartAsync(CancellationToken ct = default)
        => _modbusClient.StartAsync(ct);

    /// <summary>정지</summary>
    public Task StopAsync(CancellationToken ct = default)
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

    #region 아날로그 입력 (Holding Register)

    /// <summary>AI 워드 쓰기 (index: 0~31)</summary>
    public Task WriteAnalogInputAsync(ushort index, ushort value, CancellationToken ct = default)
        => _modbusClient.WriteAnalogInputAsync(index, value, ct);

    #endregion
}
