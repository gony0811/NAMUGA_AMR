using System.Net.Sockets;
using AMR.Models;
using Microsoft.Extensions.Logging;
using NModbus;

namespace AMR.Communication;

/// <summary>
/// Cobot Modbus TCP 통신 클라이언트 (4가지 레지스터 타입 지원)
/// </summary>
public class CobotModbusTcpClient : IDisposable
{
    private readonly CobotModbusTcpSettings _settings;
    private readonly ILogger<CobotModbusTcpClient> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private TcpClient? _tcpClient;
    private IModbusMaster? _master;
    private bool _disposed;

    public CobotModbusTcpClient(CobotModbusTcpSettings settings, ILogger<CobotModbusTcpClient> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    /// <summary>연결 상태</summary>
    public bool IsConnected => _tcpClient?.Connected ?? false;

    #region 연결 관리

    /// <summary>Cobot에 Modbus TCP 연결</summary>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (IsConnected) return;

        // zombie 리소스 정리: master가 있으면 master가 tcpClient 포함 정리
        if (_master != null)
        {
            try { _master.Dispose(); } catch { }
            _master = null;
            _tcpClient = null;
        }
        else if (_tcpClient != null)
        {
            try { _tcpClient.Dispose(); } catch { }
            _tcpClient = null;
        }

        var tcpClient = new TcpClient();
        try
        {
            await tcpClient.ConnectAsync(_settings.IpAddress, _settings.Port, ct);

            var factory = new ModbusFactory();
            var master = factory.CreateMaster(tcpClient);
            master.Transport.ReadTimeout = 3000;
            master.Transport.WriteTimeout = 3000;

            _tcpClient = tcpClient;
            _master = master;
        }
        catch
        {
            tcpClient.Dispose();
            throw;
        }
    }

    /// <summary>연결 해제 — 진행 중인 Modbus 작업이 끝난 후 안전하게 닫음</summary>
    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        // 진행 중인 Modbus 작업이 완료될 때까지 대기 (최대 5초)
        var acquired = await _semaphore.WaitAsync(TimeSpan.FromSeconds(5), ct);
        try
        {
            // NModbus dispose 체인: Master → TcpClientAdapter → TcpClient → Socket.Close
            try { _master?.Dispose(); }
            catch { }
            _master = null;
            _tcpClient = null;
        }
        finally
        {
            if (acquired) _semaphore.Release();
        }
    }

    /// <summary>동기 연결 해제 (Dispose용)</summary>
    public void Disconnect()
    {
        try { _master?.Dispose(); }
        catch { }
        _master = null;
        _tcpClient = null;
    }

    #endregion

    #region Coil 쓰기 (FC 0x05) — 제어 명령

    /// <summary>일시정지</summary>
    public Task PauseAsync(CancellationToken ct = default)
        => ToggleCoilAsync(CobotRegisterMap.Coil.Pause, ct);

    /// <summary>복구</summary>
    public Task RecoveryAsync(CancellationToken ct = default)
        => ToggleCoilAsync(CobotRegisterMap.Coil.Recovery, ct);

    /// <summary>시작</summary>
    public Task StartAsync(CancellationToken ct = default)
        => ToggleCoilAsync(CobotRegisterMap.Coil.Start, ct);

    /// <summary>정지</summary>
    public Task StopAsync(CancellationToken ct = default)
        => ToggleCoilAsync(CobotRegisterMap.Coil.Stop, ct);

    /// <summary>원점 이동</summary>
    public Task MoveToJobOriginAsync(CancellationToken ct = default)
        => ToggleCoilAsync(CobotRegisterMap.Coil.MoveToJobOrigin, ct);

    /// <summary>수동/자동 전환</summary>
    public Task ManualAutoSwitchAsync(CancellationToken ct = default)
        => ToggleCoilAsync(CobotRegisterMap.Coil.ManualAutoSwitch, ct);

    /// <summary>메인 프로그램 시작</summary>
    public Task StartMainProgramAsync(CancellationToken ct = default)
        => ToggleCoilAsync(CobotRegisterMap.Coil.StartMainProgram, ct);

    /// <summary>전체 오류 해제</summary>
    public Task ClearAllFaultsAsync(CancellationToken ct = default)
        => ToggleCoilAsync(CobotRegisterMap.Coil.ClearAllFaults, ct);

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
            _logger.LogDebug("Cobot ReadHoldingRegisters: address={Address}, count={Count}", startAddress, count);
            var result = await _master!.ReadHoldingRegistersAsync(_settings.SlaveId, startAddress, count);
            _logger.LogDebug("Cobot ReadHoldingRegisters 성공: [{Values}]", string.Join(", ", result));
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cobot ReadHoldingRegisters 실패: address={Address}, count={Count}", startAddress, count);
            throw;
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
            _logger.LogDebug("Cobot ReadCoils: address={Address}, count={Count}", startAddress, count);
            var result = await _master!.ReadCoilsAsync(_settings.SlaveId, startAddress, count);
            _logger.LogDebug("Cobot ReadCoils 성공: {Count}개 읽음", result.Length);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cobot ReadCoils 실패: address={Address}, count={Count}", startAddress, count);
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>Coil ON → 200ms 대기 → OFF (토글 명령)</summary>
    private async Task ToggleCoilAsync(ushort address, CancellationToken ct)
    {
        await WriteSingleCoilAsync(address, true, ct);
        await Task.Delay(200, ct);
        await WriteSingleCoilAsync(address, false, ct);
    }

    private async Task WriteSingleCoilAsync(ushort address, bool value, CancellationToken ct)
    {
        EnsureConnected();
        await _semaphore.WaitAsync(ct);
        try
        {
            _logger.LogDebug("Cobot WriteCoil: address={Address}, value={Value}", address, value);
            await _master!.WriteSingleCoilAsync(_settings.SlaveId, address, value);
            _logger.LogDebug("Cobot WriteCoil 성공: address={Address}", address);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cobot WriteCoil 실패: address={Address}, value={Value}", address, value);
            throw;
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
            _logger.LogDebug("Cobot ReadDiscreteInputs: address={Address}, count={Count}", startAddress, count);
            var result = await _master!.ReadInputsAsync(_settings.SlaveId, startAddress, count);
            _logger.LogDebug("Cobot ReadDiscreteInputs 성공: {Count}개 읽음", result.Length);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cobot ReadDiscreteInputs 실패: address={Address}, count={Count}", startAddress, count);
            throw;
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
            _logger.LogDebug("Cobot ReadInputRegisters: address={Address}, count={Count}", startAddress, count);
            var result = await _master!.ReadInputRegistersAsync(_settings.SlaveId, startAddress, count);
            _logger.LogDebug("Cobot ReadInputRegisters 성공: [{Values}]", string.Join(", ", result));
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cobot ReadInputRegisters 실패: address={Address}, count={Count}", startAddress, count);
            throw;
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
            _logger.LogDebug("Cobot WriteRegister: address={Address}, value={Value}", address, value);
            await _master!.WriteSingleRegisterAsync(_settings.SlaveId, address, value);
            _logger.LogDebug("Cobot WriteRegister 성공: address={Address}", address);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cobot WriteRegister 실패: address={Address}, value={Value}", address, value);
            throw;
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
