using System.Net.Sockets;
using AMR.Models;
using Microsoft.Extensions.Logging;
using NModbus;

namespace AMR.Communication;

/// <summary>
/// LS산전 XEL-BSSRT Smart I/O 모듈 Modbus TCP 클라이언트.
/// Discrete Input(FC 0x02)으로 입력 비트를 읽고 Coil(FC 0x01/0x05)로 출력 비트를 제어한다.
/// </summary>
public class IoModuleModbusTcpClient : IDisposable
{
    private readonly IoModuleModbusTcpSettings _settings;
    private readonly ILogger<IoModuleModbusTcpClient> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private TcpClient? _tcpClient;
    private IModbusMaster? _master;
    private bool _disposed;

    public IoModuleModbusTcpClient(IoModuleModbusTcpSettings settings, ILogger<IoModuleModbusTcpClient> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    /// <summary>연결 상태</summary>
    public bool IsConnected => _tcpClient?.Connected ?? false;

    #region 연결 관리

    /// <summary>I/O 모듈에 Modbus TCP 연결</summary>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (IsConnected) return;

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
        var acquired = await _semaphore.WaitAsync(TimeSpan.FromSeconds(5), ct);
        try
        {
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

    #region 입력 읽기 (Discrete Input)

    /// <summary>입력 상태 일괄 읽기 (X000~X005)</summary>
    public async Task<IoModuleInputStatus> ReadInputsAsync(CancellationToken ct = default)
    {
        var bits = await ReadDiscreteInputsAsync(
            IoModuleRegisterMap.DiscreteInput.InputStart,
            IoModuleRegisterMap.DiscreteInput.InputCount, ct);

        // 실제 배선 매핑:
        //   X002 (bits[2]) → AMR 포트 4
        //   X003 (bits[3]) → AMR 포트 3
        //   X004 (bits[4]) → AMR 포트 2
        //   X005 (bits[5]) → AMR 포트 1
        return new IoModuleInputStatus
        {
            Emo = bits[0],
            Reset = bits[1],
            MzDetect1 = bits[5],   // X005 = AMR 포트 1
            MzDetect2 = bits[4],   // X004 = AMR 포트 2
            MzDetect3 = bits[3],   // X003 = AMR 포트 3
            MzDetect4 = bits[2]    // X002 = AMR 포트 4
        };
    }

    #endregion

    #region 출력 읽기 (Coil)

    /// <summary>출력 상태 일괄 읽기 (Y000~Y005)</summary>
    public async Task<IoModuleOutputStatus> ReadOutputsAsync(CancellationToken ct = default)
    {
        var bits = await ReadCoilsAsync(
            IoModuleRegisterMap.Coil.OutputStart,
            IoModuleRegisterMap.Coil.OutputCount, ct);

        return new IoModuleOutputStatus
        {
            TowerLampRed = bits[0],
            TowerLampYellow = bits[1],
            TowerLampGreen = bits[2],
            TowerLampBuzzer = bits[3],
            ResetSwLamp = bits[4],
            CobotServoOnOff = bits[5]
        };
    }

    #endregion

    #region 출력 제어 (Coil 래칭 ON/OFF)

    /// <summary>타워램프 적색</summary>
    public Task SetTowerLampRedAsync(bool value, CancellationToken ct = default)
        => WriteSingleCoilAsync(IoModuleRegisterMap.Coil.TowerLampRed, value, ct);

    /// <summary>타워램프 황색</summary>
    public Task SetTowerLampYellowAsync(bool value, CancellationToken ct = default)
        => WriteSingleCoilAsync(IoModuleRegisterMap.Coil.TowerLampYellow, value, ct);

    /// <summary>타워램프 녹색</summary>
    public Task SetTowerLampGreenAsync(bool value, CancellationToken ct = default)
        => WriteSingleCoilAsync(IoModuleRegisterMap.Coil.TowerLampGreen, value, ct);

    /// <summary>타워램프 부저</summary>
    public Task SetTowerLampBuzzerAsync(bool value, CancellationToken ct = default)
        => WriteSingleCoilAsync(IoModuleRegisterMap.Coil.TowerLampBuzzer, value, ct);

    /// <summary>리셋 스위치 램프</summary>
    public Task SetResetSwLampAsync(bool value, CancellationToken ct = default)
        => WriteSingleCoilAsync(IoModuleRegisterMap.Coil.ResetSwLamp, value, ct);

    /// <summary>Cobot 서보 ON/OFF</summary>
    public Task SetCobotServoAsync(bool value, CancellationToken ct = default)
        => WriteSingleCoilAsync(IoModuleRegisterMap.Coil.CobotServoOnOff, value, ct);

    /// <summary>타워램프 R/Y/G 일괄 OFF (버저/서보 유지)</summary>
    public async Task AllTowerLampsOffAsync(CancellationToken ct = default)
    {
        await SetTowerLampRedAsync(false, ct);
        await SetTowerLampYellowAsync(false, ct);
        await SetTowerLampGreenAsync(false, ct);
    }

    #endregion

    #region Raw 접근 (진단/현장 보정)

    public async Task<bool[]> ReadRawDiscreteInputsAsync(ushort startAddress, ushort count, CancellationToken ct = default)
        => await ReadDiscreteInputsAsync(startAddress, count, ct);

    public async Task<bool[]> ReadRawCoilsAsync(ushort startAddress, ushort count, CancellationToken ct = default)
        => await ReadCoilsAsync(startAddress, count, ct);

    public Task WriteCoilAsync(ushort address, bool value, CancellationToken ct = default)
        => WriteSingleCoilAsync(address, value, ct);

    #endregion

    #region 내부 헬퍼

    private async Task<bool[]> ReadDiscreteInputsAsync(ushort startAddress, ushort count, CancellationToken ct)
    {
        EnsureConnected();
        await _semaphore.WaitAsync(ct);
        try
        {
            _logger.LogDebug("IoModule ReadDiscreteInputs: address={Address}, count={Count}", startAddress, count);
            var result = await _master!.ReadInputsAsync(_settings.SlaveId, startAddress, count);
            _logger.LogDebug("IoModule ReadDiscreteInputs 성공: {Count}개 읽음", result.Length);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IoModule ReadDiscreteInputs 실패: address={Address}, count={Count}", startAddress, count);
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<bool[]> ReadCoilsAsync(ushort startAddress, ushort count, CancellationToken ct)
    {
        EnsureConnected();
        await _semaphore.WaitAsync(ct);
        try
        {
            _logger.LogDebug("IoModule ReadCoils: address={Address}, count={Count}", startAddress, count);
            var result = await _master!.ReadCoilsAsync(_settings.SlaveId, startAddress, count);
            _logger.LogDebug("IoModule ReadCoils 성공: {Count}개 읽음", result.Length);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IoModule ReadCoils 실패: address={Address}, count={Count}", startAddress, count);
            throw;
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
            _logger.LogDebug("IoModule WriteCoil: address={Address}, value={Value}", address, value);
            await _master!.WriteSingleCoilAsync(_settings.SlaveId, address, value);
            _logger.LogDebug("IoModule WriteCoil 성공: address={Address}", address);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IoModule WriteCoil 실패: address={Address}, value={Value}", address, value);
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
            throw new InvalidOperationException("I/O 모듈에 연결되어 있지 않습니다. ConnectAsync()를 먼저 호출하세요.");
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
