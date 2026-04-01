using AMR.Models;
using AMR.Service;

namespace AMR.Web.Services;

/// <summary>
/// 시뮬레이션 페이지용 서비스 — MQTT 명령 수신 버퍼링 및 상태/응답 퍼블리시
/// </summary>
public class SimulationService
{
    private readonly MqttService _mqttService;
    private readonly List<ReceivedCommand> _receivedCommands = new();
    private readonly object _lock = new();

    public SimulationService(MqttService mqttService)
    {
        _mqttService = mqttService;
        _mqttService.OnCommandReceived += OnCommandReceived;
    }

    /// <summary>MQTT 연결 상태</summary>
    public bool IsConnected => _mqttService.IsConnected;

    /// <summary>지정 인덱스 이후로 수신된 명령 목록 반환</summary>
    public List<ReceivedCommand> GetCommands(int sinceIndex)
    {
        lock (_lock)
        {
            if (sinceIndex >= _receivedCommands.Count)
                return new List<ReceivedCommand>();

            return _receivedCommands.Skip(sinceIndex).ToList();
        }
    }

    /// <summary>전체 수신 명령 수</summary>
    public int CommandCount
    {
        get { lock (_lock) { return _receivedCommands.Count; } }
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
            _receivedCommands.Add(new ReceivedCommand
            {
                ReceivedAt = DateTime.Now,
                Command = command
            });
        }
    }
}

/// <summary>수신 시간이 포함된 명령 래퍼</summary>
public class ReceivedCommand
{
    public DateTime ReceivedAt { get; set; }
    public AmrCommand Command { get; set; } = new();
}
