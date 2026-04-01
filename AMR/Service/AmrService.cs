using AMR.Communication;
using AMR.Enums;
using AMR.Models;
using Microsoft.Extensions.Logging;

namespace AMR.Service;

/// <summary>
/// AMR Modbus TCP 통신 서비스 — 로봇 상태 읽기 및 명령 전달 함수 집합
/// </summary>
public class AmrService
{
    private readonly AmrModbusTcpClient _modbusClient;
    private readonly ILogger<AmrService> _logger;

    public AmrService(AmrModbusTcpClient modbusClient, ILogger<AmrService> logger)
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
        _logger.LogInformation("AMR Modbus TCP 연결 완료");
    }

    /// <summary>Modbus TCP 연결 해제</summary>
    public void Disconnect()
    {
        _modbusClient.Disconnect();
        _logger.LogInformation("AMR Modbus TCP 연결 해제");
    }

    #endregion

    #region 상태 읽기

    /// <summary>전체 로봇 상태 읽기</summary>
    public Task<RobotStatus> ReadStatusAsync(CancellationToken ct = default)
        => _modbusClient.ReadRobotStatusAsync(ct);

    /// <summary>전원 상태 읽기</summary>
    public Task<PowerState> ReadPowerStateAsync(CancellationToken ct = default)
        => _modbusClient.ReadPowerStateAsync(ct);

    /// <summary>로봇 위치 읽기</summary>
    public Task<RobotPose> ReadPoseAsync(CancellationToken ct = default)
        => _modbusClient.ReadPoseAsync(ct);

    /// <summary>배터리 상태 읽기</summary>
    public Task<BatteryStatus> ReadBatteryAsync(CancellationToken ct = default)
        => _modbusClient.ReadBatteryStatusAsync(ct);

    /// <summary>주행 모드 읽기</summary>
    public Task<DrivingMode> ReadDrivingModeAsync(CancellationToken ct = default)
        => _modbusClient.ReadDrivingModeAsync(ct);

    /// <summary>Task/Job 진행 상태 읽기</summary>
    public Task<TaskProgress> ReadTaskProgressAsync(CancellationToken ct = default)
        => _modbusClient.ReadTaskProgressAsync(ct);

    /// <summary>에러 코드 읽기</summary>
    public Task<ushort> ReadErrorCodeAsync(CancellationToken ct = default)
        => _modbusClient.ReadErrorCodeAsync(ct);

    #endregion

    #region 명령 전달

    /// <summary>전원 제어</summary>
    public Task SetPowerAsync(PowerCommand command, CancellationToken ct = default)
        => _modbusClient.SetPowerAsync(command, ct);

    /// <summary>주행 모드 설정</summary>
    public Task SetDrivingModeAsync(DrivingMode mode, CancellationToken ct = default)
        => _modbusClient.SetDrivingModeAsync(mode, ct);

    /// <summary>Error Reset</summary>
    public Task AirInitializeAsync(CancellationToken ct = default)
        => _modbusClient.AirInitializeAsync(ct);

    /// <summary>로봇 주행 정지 — 활성화(1), 비활성화(2)</summary>
    public Task SetRobotStopAsync(ushort value, CancellationToken ct = default)
        => _modbusClient.SetRobotStopAsync(value, ct);

    /// <summary>로봇 포즈 탐색 활성화</summary>
    public Task SetPoseSearchAsync(ushort value, CancellationToken ct = default)
        => _modbusClient.SetPoseSearchAsync(value, ct);

    /// <summary>포즈 탐색 좌표 설정 (X, Y: meters, RZ: radian)</summary>
    public Task SetPoseTargetAsync(float x, float y, float angle, CancellationToken ct = default)
        => _modbusClient.SetPoseTargetAsync(x, y, angle, ct);

    /// <summary>상태 제어</summary>
    public Task SetExecutionControlAsync(ExecutionControl control, CancellationToken ct = default)
        => _modbusClient.SetExecutionControlAsync(control, ct);

    /// <summary>Task Index 설정</summary>
    public Task SetTaskIndexAsync(ushort index, CancellationToken ct = default)
        => _modbusClient.SetTaskIndexAsync(index, ct);

    /// <summary>Job Index 설정</summary>
    public Task SetJobIndexAsync(ushort index, CancellationToken ct = default)
        => _modbusClient.SetJobIndexAsync(index, ct);

    /// <summary>유저 변수 쓰기 (index: 0~149)</summary>
    public Task SetUserVariableAsync(ushort variableIndex, ushort value, CancellationToken ct = default)
        => _modbusClient.SetUserVariableAsync(variableIndex, value, ct);

    #endregion
}
