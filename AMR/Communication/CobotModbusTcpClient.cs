using System.Net.Sockets;
using AMR.Models;
using NModbus;

namespace AMR.Communication;

/// <summary>
/// Cobot Modbus TCP 통신 클라이언트 (4가지 레지스터 타입 지원)
/// </summary>
public class CobotModbusTcpClient : IDisposable
{
    private readonly CobotModbusTcpSettings _settings;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private TcpClient? _tcpClient;
    private IModbusMaster? _master;
    private bool _disposed;

    public CobotModbusTcpClient(CobotModbusTcpSettings settings)
    {
        _settings = settings;
    }

    /// <summary>연결 상태</summary>
    public bool IsConnected => _tcpClient?.Connected ?? false;

    #region 연결 관리

    /// <summary>Cobot에 Modbus TCP 연결</summary>
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

    #region Coil 쓰기 (FC 0x05) — 제어 명령

    /// <summary>일시정지</summary>
    public Task PauseAsync(CancellationToken ct = default)
        => WriteSingleCoilAsync(CobotRegisterMap.Coil.Pause, true, ct);

    /// <summary>복구</summary>
    public Task RecoveryAsync(CancellationToken ct = default)
        => WriteSingleCoilAsync(CobotRegisterMap.Coil.Recovery, true, ct);

    /// <summary>시작</summary>
    public Task StartAsync(CancellationToken ct = default)
        => WriteSingleCoilAsync(CobotRegisterMap.Coil.Start, true, ct);

    /// <summary>정지</summary>
    public Task StopAsync(CancellationToken ct = default)
        => WriteSingleCoilAsync(CobotRegisterMap.Coil.Stop, true, ct);

    /// <summary>원점 이동</summary>
    public Task MoveToJobOriginAsync(CancellationToken ct = default)
        => WriteSingleCoilAsync(CobotRegisterMap.Coil.MoveToJobOrigin, true, ct);

    /// <summary>수동/자동 전환</summary>
    public Task ManualAutoSwitchAsync(CancellationToken ct = default)
        => WriteSingleCoilAsync(CobotRegisterMap.Coil.ManualAutoSwitch, true, ct);

    /// <summary>메인 프로그램 시작</summary>
    public Task StartMainProgramAsync(CancellationToken ct = default)
        => WriteSingleCoilAsync(CobotRegisterMap.Coil.StartMainProgram, true, ct);

    /// <summary>전체 오류 해제</summary>
    public Task ClearAllFaultsAsync(CancellationToken ct = default)
        => WriteSingleCoilAsync(CobotRegisterMap.Coil.ClearAllFaults, true, ct);

    /// <summary>DI 비트 개별 쓰기 (index: 0~127)</summary>
    public Task WriteDigitalInputAsync(ushort index, bool value, CancellationToken ct = default)
    {
        if (index > 127)
            throw new ArgumentOutOfRangeException(nameof(index), "DI 인덱스는 0~127 범위여야 합니다.");

        var address = (ushort)(CobotRegisterMap.Coil.DigitalInputStart + index);
        return WriteSingleCoilAsync(address, value, ct);
    }

    #endregion

    #region Holding Register 쓰기 (FC 0x06/0x16) — AI 값

    /// <summary>AI 워드 개별 쓰기 (index: 0~31)</summary>
    public Task WriteAnalogInputAsync(ushort index, ushort value, CancellationToken ct = default)
    {
        if (index > 31)
            throw new ArgumentOutOfRangeException(nameof(index), "AI 인덱스는 0~31 범위여야 합니다.");

        var address = (ushort)(CobotRegisterMap.Holding.AnalogInputStart + index);
        return WriteRegisterAsync(address, value, ct);
    }

    #endregion

    #region 읽기 — Cobot 상태 (Input Register)

    /// <summary>Cobot 상태 읽기 (Input Register 310~322)</summary>
    public async Task<CobotStatus> ReadCobotStatusAsync(CancellationToken ct = default)
    {
        var registers = await ReadInputRegistersAsync(
            CobotRegisterMap.Input.StatusStart,
            CobotRegisterMap.Input.StatusCount, ct);

        return new CobotStatus
        {
            EnableState = registers[0],
            RobotMode = registers[1],
            OperationStatus = registers[2],
            ToolNo = registers[3],
            JobNumber = registers[4],
            ScrumState = registers[5],
            RobotStatusFault = registers[6],
            MasterFaultCode = registers[7],
            SubFaultCode = registers[8],
            CollisionDetection = registers[9],
            MotionInPlace = registers[10],
            SafetyStopS0 = registers[11],
            SafetyStopS1 = registers[12]
        };
    }

    #endregion

    #region Raw 읽기 (진단용)

    /// <summary>Coil 원시 값 읽기</summary>
    public async Task<bool[]> ReadRawCoilsAsync(ushort startAddress, ushort count, CancellationToken ct = default)
        => await ReadCoilsAsync(startAddress, count, ct);

    /// <summary>Discrete Input 원시 값 읽기</summary>
    public async Task<bool[]> ReadRawDiscreteInputsAsync(ushort startAddress, ushort count, CancellationToken ct = default)
        => await ReadDiscreteInputsAsync(startAddress, count, ct);

    /// <summary>Input Register 원시 값 읽기</summary>
    public async Task<ushort[]> ReadRawInputRegistersAsync(ushort startAddress, ushort count, CancellationToken ct = default)
        => await ReadInputRegistersAsync(startAddress, count, ct);

    /// <summary>Holding Register 원시 값 읽기</summary>
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

    #endregion

    #region 내부 헬퍼

    private async Task<bool[]> ReadCoilsAsync(ushort startAddress, ushort count, CancellationToken ct)
    {
        EnsureConnected();
        await _semaphore.WaitAsync(ct);
        try
        {
            return await _master!.ReadCoilsAsync(_settings.SlaveId, startAddress, count);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task WriteSingleCoilAsync(ushort address, bool value, CancellationToken ct)
    {
        EnsureConnected();
        await _semaphore.WaitAsync(ct);
        try
        {
            await _master!.WriteSingleCoilAsync(_settings.SlaveId, address, value);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<bool[]> ReadDiscreteInputsAsync(ushort startAddress, ushort count, CancellationToken ct)
    {
        EnsureConnected();
        await _semaphore.WaitAsync(ct);
        try
        {
            return await _master!.ReadInputsAsync(_settings.SlaveId, startAddress, count);
        }
        finally
        {
            _semaphore.Release();
        }
    }

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

    private void EnsureConnected()
    {
        if (!IsConnected)
            throw new InvalidOperationException("Cobot에 연결되어 있지 않습니다. ConnectAsync()를 먼저 호출하세요.");
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
