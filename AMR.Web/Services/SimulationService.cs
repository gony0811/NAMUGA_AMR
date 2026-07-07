using AMR.Models;
using AMR.Service;

namespace AMR.Web.Services;

/// <summary>
/// 시뮬레이션 페이지용 서비스 — MQTT 명령 수신 버퍼링 및 상태/응답 퍼블리시
/// </summary>
public class SimulationService
{
    private readonly MqttService _mqttService;
    private readonly LinkedList<ReceivedCommand> _receivedCommands = new();
    private readonly object _lock = new();
    private int _totalAdded;  // 전체 누적 수신 수 (인덱스 기준점 유지용)

    /// <summary>
    /// 수신 명령 버퍼 상한. 초과 시 가장 오래된 항목부터 제거.
    /// Singleton 라이프타임에서 무한 증가 방지 — 메모리 누수의 주범이었음.
    /// </summary>
    private const int MaxReceivedCommands = 1000;

    public SimulationService(MqttService mqttService)
    {
        _mqttService = mqttService;
        _mqttService.OnCommandReceived += OnCommandReceived;
    }

    /// <summary>MQTT 연결 상태</summary>
    public bool IsConnected => _mqttService.IsConnected;

    /// <summary>
    /// 지정 인덱스 이후로 수신된 명령 목록 반환.
    /// sinceIndex 는 _totalAdded(누적 수신 수) 기준 — 버퍼 트림 후에도 클라이언트
    /// 폴링이 깨지지 않도록 누적 카운터를 그대로 유지한다.
    /// </summary>
    public List<ReceivedCommand> GetCommands(int sinceIndex)
    {
        lock (_lock)
        {
            if (sinceIndex >= _totalAdded)
                return new List<ReceivedCommand>();

            // 버퍼 시작 인덱스 = 누적 - 현재 보관 개수
            var bufferStartIndex = _totalAdded - _receivedCommands.Count;
            var skip = Math.Max(0, sinceIndex - bufferStartIndex);

            return _receivedCommands.Skip(skip).ToList();
        }
    }

    /// <summary>전체 수신 명령 수 (누적, 트림 무관)</summary>
    public int CommandCount
    {
        get { lock (_lock) { return _totalAdded; } }
    }

    /// <summary>AMR 상태를 MQTT로 퍼블리시</summary>
    public async Task PublishStatusAsync(AmrStatusMessage statusMessage, CancellationToken ct = default)
    {
        await _mqttService.PublishStatusAsync(statusMessage, ct);
    }

    /// <summary>명령 응답을 MQTT로 퍼블리시</summary>
    public async Task PublishReplyAsync(CommandReply reply, CancellationToken ct = default)
    {
        await _mqttService.PublishReplyAsync(reply, ct);
    }

    private void OnCommandReceived(AmrCommand command)
    {
        lock (_lock)
        {
            _receivedCommands.AddLast(new ReceivedCommand
            {
                ReceivedAt = DateTime.Now,
                Command = command
            });
            _totalAdded++;

            // 상한 초과 시 가장 오래된 항목부터 제거 (메모리 누수 방지)
            while (_receivedCommands.Count > MaxReceivedCommands)
                _receivedCommands.RemoveFirst();
        }
    }
}

/// <summary>수신 시간이 포함된 명령 래퍼</summary>
public class ReceivedCommand
{
    public DateTime ReceivedAt { get; set; }
    public AmrCommand Command { get; set; } = new();
}
