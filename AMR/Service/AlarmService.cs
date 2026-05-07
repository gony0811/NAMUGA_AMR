using AMR.Models;
using Microsoft.Extensions.Logging;

namespace AMR.Service;

/// <summary>
/// 차량 알람 평가 서비스 — 호출 시점에 활성화된 알람을 판정한다.
/// docs/vehicle_alarm.md 참조.
/// </summary>
public class AlarmService
{
    private readonly CobotService _cobotService;
    private readonly AmrService _amrService;
    private readonly ILogger<AlarmService> _logger;

    public AlarmService(CobotService cobotService, AmrService amrService, ILogger<AlarmService> logger)
    {
        _cobotService = cobotService;
        _amrService = amrService;
        _logger = logger;
    }

    /// <summary>현재 활성화된 알람을 평가. 없으면 null.</summary>
    public async Task<Alarm?> EvaluateAsync(CancellationToken ct = default)
    {
        if (IsAmrNotReady())
            return Alarm.AmrNotReady;

        if (!_cobotService.IsConnected)
            return Alarm.CobotNotReady;

        try
        {
            var s = await _cobotService.ReadStatusAsync(ct);

            if (s.EnableState != 1 || s.OperationStatus != 2 || s.RobotMode != 0)
                return Alarm.CobotNotReady;

            if (s.CollisionDetection == 1)
                return Alarm.CobotCollision;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Cobot 상태 읽기 실패 — ERR-100 활성으로 판정");
            return Alarm.CobotNotReady;
        }

        if (IsAmrMapMatchingError())
            return Alarm.AmrMapMatchingError;

        return null;
    }

    /// <summary>
    /// ERR-104 AMR Map Matching Error 조건 평가.
    /// 맵 일치율이 30% 미만이면 활성.
    /// </summary>
    private bool IsAmrMapMatchingError()
    {
        var status = _amrService.LastStatus;
        return status != null && status.MapStatusPercent < 30f;
    }

    /// <summary>
    /// ERR-101 AMR Not Ready 조건 평가.
    /// 1) Modbus Disconnect
    /// </summary>
    private bool IsAmrNotReady()
    {
        return !_amrService.IsConnected;
    }
}
