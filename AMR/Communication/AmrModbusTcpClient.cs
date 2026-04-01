using System.Net.Sockets;
using AMR.Enums;
using AMR.Models;
using NModbus;

namespace AMR.Communication;

/// <summary>
/// 아덴트로봇 TARS-M Modbus TCP 통신 클라이언트
/// </summary>
public class AmrModbusTcpClient : IDisposable
{
    private readonly AmrModbusTcpSettings _settings;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private TcpClient? _tcpClient;
    private IModbusMaster? _master;
    private bool _disposed;

    public AmrModbusTcpClient(AmrModbusTcpSettings settings)
    {
        _settings = settings;
    }

    /// <summary>로봇 연결 상태</summary>
    public bool IsConnected => _tcpClient?.Connected ?? false;

    #region 연결 관리

    /// <summary>로봇에 Modbus TCP 연결</summary>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (IsConnected) return;

        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(_settings.IpAddress, _settings.Port, ct);

        var factory = new ModbusFactory();
        _master = factory.CreateMaster(_tcpClient);
        _master.Transport.ReadTimeout = 3000;
        _master.Transport.WriteTimeout = 3000;
    }

    /// <summary>연결 해제</summary>
    public void Disconnect()
    {
        _master?.Dispose();
        _master = null;
        _tcpClient?.Dispose();
        _tcpClient = null;
    }

    #endregion

    #region 쓰기 메서드 (Holding Register)

    /// <summary>전원 제어</summary>
    public Task SetPowerAsync(PowerCommand command, CancellationToken ct = default)
        => WriteRegisterAsync(ModbusRegisterMap.Holding.Power, (ushort)command, ct);

    /// <summary>주행 모드 설정 — 드라이브(1), 카트(2)</summary>
    public Task SetDrivingModeAsync(DrivingMode mode, CancellationToken ct = default)
        => WriteRegisterAsync(ModbusRegisterMap.Holding.DrivingMode, (ushort)mode, ct);

    /// <summary>Error Reset — 활성화(1)</summary>
    public Task AirInitializeAsync(CancellationToken ct = default)
        => WriteRegisterAsync(ModbusRegisterMap.Holding.AirInitialize, 1, ct);

    /// <summary>로봇 주행 정지 — 활성화(1), 비활성화(2)</summary>
    public Task SetRobotStopAsync(ushort value, CancellationToken ct = default)
        => WriteRegisterAsync(ModbusRegisterMap.Holding.RobotStop, value, ct);

    /// <summary>로봇 포즈 탐색 활성화</summary>
    public Task SetPoseSearchAsync(ushort value, CancellationToken ct = default)
        => WriteRegisterAsync(ModbusRegisterMap.Holding.PoseSearch, value, ct);

    /// <summary>포즈 탐색 좌표 설정 (X, Y: meters, RZ: radian)</summary>
    public async Task SetPoseTargetAsync(float x, float y, float angle, CancellationToken ct = default)
    {
        var registers = new ushort[6];
        FloatToRegisters(x).CopyTo(registers, 0);
        FloatToRegisters(y).CopyTo(registers, 2);
        FloatToRegisters(angle).CopyTo(registers, 4);

        await WriteRegistersAsync(ModbusRegisterMap.Holding.PoseTargetX, registers, ct);
    }

    /// <summary>상태 제어 — 정지(1), 시작(2), 일시정지(3)</summary>
    public Task SetExecutionControlAsync(ExecutionControl control, CancellationToken ct = default)
        => WriteRegisterAsync(ModbusRegisterMap.Holding.ExecutionControl, (ushort)control, ct);

    /// <summary>Task Index 설정</summary>
    public Task SetTaskIndexAsync(ushort index, CancellationToken ct = default)
        => WriteRegisterAsync(ModbusRegisterMap.Holding.TaskIndex, index, ct);

    /// <summary>Job Index 설정</summary>
    public Task SetJobIndexAsync(ushort index, CancellationToken ct = default)
        => WriteRegisterAsync(ModbusRegisterMap.Holding.JobIndex, index, ct);

    /// <summary>유저 변수 쓰기 (index: 0~149 → 매뉴얼 주소 50~199)</summary>
    public Task SetUserVariableAsync(ushort variableIndex, ushort value, CancellationToken ct = default)
    {
        if (variableIndex > 149)
            throw new ArgumentOutOfRangeException(nameof(variableIndex), "유저 변수 인덱스는 0~149 범위여야 합니다.");

        var address = (ushort)(ModbusRegisterMap.Holding.UserVariablesStart + variableIndex);
        return WriteRegisterAsync(address, value, ct);
    }

    #endregion

    #region 읽기 메서드 (Input Register)

    /// <summary>전체 로봇 상태 읽기 (벌크)</summary>
    public async Task<RobotStatus> ReadRobotStatusAsync(CancellationToken ct = default)
    {
        // Input Register 0~64 (65개) 벌크 읽기
        var registers = await ReadInputRegistersAsync(0, 65, ct);

        return new RobotStatus
        {
            PowerState = (PowerState)registers[ModbusRegisterMap.Input.PowerStatus],
            RobotState = (RobotState)registers[ModbusRegisterMap.Input.RobotStatus],
            ErrorCode = registers[ModbusRegisterMap.Input.RobotError],
            RobotStopActive = registers[ModbusRegisterMap.Input.RobotStop],
            WiFi = (WiFiState)registers[ModbusRegisterMap.Input.WiFi],
            WorkStatus = (WorkStatus)registers[ModbusRegisterMap.Input.WorkStatus],
            Pose = new RobotPose(
                RegistersToFloat(registers[ModbusRegisterMap.Input.PoseX], registers[ModbusRegisterMap.Input.PoseX + 1]),
                RegistersToFloat(registers[ModbusRegisterMap.Input.PoseY], registers[ModbusRegisterMap.Input.PoseY + 1]),
                RegistersToFloat(registers[ModbusRegisterMap.Input.PoseAngle], registers[ModbusRegisterMap.Input.PoseAngle + 1])
            ),
            MapStatusPercent = registers[ModbusRegisterMap.Input.MapStatus] / 10000f * 100f,
            DrivingMode = (DrivingMode)registers[ModbusRegisterMap.Input.DrivingMode],
            Battery = new BatteryStatus
            {
                LevelPercent = registers[ModbusRegisterMap.Input.BatteryLevel] / 10000f * 100f,
                Voltage = registers[ModbusRegisterMap.Input.BatteryVoltage] / 100f,
                Current = registers[ModbusRegisterMap.Input.BatteryCurrent] / 100f,
                TemperatureCelsius = registers[ModbusRegisterMap.Input.BatteryTemp] / 100f,
                ChargingState = (ChargingState)registers[ModbusRegisterMap.Input.ChargingState]
            },
            TaskProgress = new TaskProgress
            {
                TotalTaskCount = registers[ModbusRegisterMap.Input.TotalTaskCount],
                CurrentTaskNumber = registers[ModbusRegisterMap.Input.CurrentTaskNumber],
                TotalJobCount = registers[ModbusRegisterMap.Input.TotalJobCount],
                CurrentJobNumber = registers[ModbusRegisterMap.Input.CurrentJobNumber]
            }
        };
    }

    /// <summary>전원 상태 읽기</summary>
    public async Task<PowerState> ReadPowerStateAsync(CancellationToken ct = default)
    {
        var registers = await ReadInputRegistersAsync(ModbusRegisterMap.Input.PowerStatus, 1, ct);
        return (PowerState)registers[0];
    }

    /// <summary>로봇 위치 읽기</summary>
    public async Task<RobotPose> ReadPoseAsync(CancellationToken ct = default)
    {
        var registers = await ReadInputRegistersAsync(ModbusRegisterMap.Input.PoseX, 6, ct);
        return new RobotPose(
            RegistersToFloat(registers[0], registers[1]),
            RegistersToFloat(registers[2], registers[3]),
            RegistersToFloat(registers[4], registers[5])
        );
    }

    /// <summary>배터리 상태 읽기</summary>
    public async Task<BatteryStatus> ReadBatteryStatusAsync(CancellationToken ct = default)
    {
        var registers = await ReadInputRegistersAsync(ModbusRegisterMap.Input.BatteryLevel, 5, ct);
        return new BatteryStatus
        {
            LevelPercent = registers[0] / 10000f * 100f,
            Voltage = registers[1] / 100f,
            Current = registers[2] / 100f,
            TemperatureCelsius = registers[3] / 100f,
            ChargingState = (ChargingState)registers[4]
        };
    }

    /// <summary>주행 모드 읽기</summary>
    public async Task<DrivingMode> ReadDrivingModeAsync(CancellationToken ct = default)
    {
        var registers = await ReadInputRegistersAsync(ModbusRegisterMap.Input.DrivingMode, 1, ct);
        return (DrivingMode)registers[0];
    }

    /// <summary>Task/Job 진행 상태 읽기</summary>
    public async Task<TaskProgress> ReadTaskProgressAsync(CancellationToken ct = default)
    {
        var registers = await ReadInputRegistersAsync(ModbusRegisterMap.Input.TotalTaskCount, 4, ct);
        return new TaskProgress
        {
            TotalTaskCount = registers[0],
            CurrentTaskNumber = registers[1],
            TotalJobCount = registers[2],
            CurrentJobNumber = registers[3]
        };
    }

    /// <summary>에러 코드 읽기</summary>
    public async Task<ushort> ReadErrorCodeAsync(CancellationToken ct = default)
    {
        var registers = await ReadInputRegistersAsync(ModbusRegisterMap.Input.RobotError, 1, ct);
        return registers[0];
    }

    #endregion

    /// <summary>Input Register 원시 값 읽기 (진단용)</summary>
    public async Task<ushort[]> ReadRawInputRegistersAsync(ushort startAddress, ushort count, CancellationToken ct = default)
        => await ReadInputRegistersAsync(startAddress, count, ct);

    /// <summary>Holding Register 원시 값 읽기 (진단용)</summary>
    public async Task<ushort[]> ReadRawHoldingRegistersAsync(ushort startAddress, ushort count, CancellationToken ct = default)
    {
        EnsureConnected();
        await _semaphore.WaitAsync(ct);
        try
        {
            return await _master!.ReadHoldingRegistersAsync(_settings.SlaveId, startAddress, count);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    #region 내부 헬퍼

    private async Task<ushort[]> ReadInputRegistersAsync(ushort startAddress, ushort count, CancellationToken ct)
    {
        EnsureConnected();
        await _semaphore.WaitAsync(ct);
        try
        {
            return await _master!.ReadInputRegistersAsync(_settings.SlaveId, startAddress, count);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task WriteRegisterAsync(ushort address, ushort value, CancellationToken ct)
    {
        EnsureConnected();
        await _semaphore.WaitAsync(ct);
        try
        {
            await _master!.WriteSingleRegisterAsync(_settings.SlaveId, address, value);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task WriteRegistersAsync(ushort startAddress, ushort[] values, CancellationToken ct)
    {
        EnsureConnected();
        await _semaphore.WaitAsync(ct);
        try
        {
            await _master!.WriteMultipleRegistersAsync(_settings.SlaveId, startAddress, values);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
            throw new InvalidOperationException("로봇에 연결되어 있지 않습니다. ConnectAsync()를 먼저 호출하세요.");
    }

    /// <summary>2개의 UInt16 레지스터를 Float32로 변환 (Little-Endian word order: 첫 번째=Lo, 두 번째=Hi)</summary>
    private static float RegistersToFloat(ushort first, ushort second)
    {
        var combined = ((uint)second << 16) | first;
        return BitConverter.Int32BitsToSingle((int)combined);
    }

    /// <summary>Float32를 2개의 UInt16 레지스터로 변환 (Little-Endian word order: [0]=Lo, [1]=Hi)</summary>
    private static ushort[] FloatToRegisters(float value)
    {
        var bits = (uint)BitConverter.SingleToInt32Bits(value);
        return new[]
        {
            (ushort)(bits & 0xFFFF),
            (ushort)(bits >> 16)
        };
    }

    #endregion

    #region Dispose

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Disconnect();
        _semaphore.Dispose();
        GC.SuppressFinalize(this);
    }

    #endregion
}
