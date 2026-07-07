using System.Text.Json;
using AMR.Models;
using Microsoft.Extensions.Logging;

namespace AMR.Service;

/// <summary>
/// 노드+슬롯별 QR offset 보정값 관리.
///   - 시작 시 port_offsets.json (exe 옆) 에서 로드
///   - 변경 시 즉시 저장
///   - QR offset 전달 직전 GetOffset(nodeId, port) 로 조회해서 더함
///
/// 키: "{NodeId}|{Port}" (Port 대문자). Port 별 항목이 없으면 Port 빈 값("") 공통 항목 조회.
/// ACS·Fairino 무수정 — 보정은 전적으로 AMR.Web 에서 처리.
/// </summary>
public class PortOffsetService
{
    private readonly ILogger<PortOffsetService> _logger;
    private readonly object _lock = new();
    private readonly string _filePath;

    // 키 = "{NODEID}|{PORT}" (대문자)
    private Dictionary<string, PortOffset> _offsets = new();

    public PortOffsetService(ILogger<PortOffsetService> logger)
    {
        _logger = logger;
        _filePath = Path.Combine(AppContext.BaseDirectory, "port_offsets.json");
        LoadFromFile();
    }

    private static string MakeKey(string nodeId, string port)
        => $"{(nodeId ?? "").Trim().ToUpperInvariant()}|{(port ?? "").Trim().ToUpperInvariant()}";

    /// <summary>
    /// 노드+슬롯에 해당하는 offset 조회.
    /// 1) (NodeId, Port) 정확 매칭 → 2) (NodeId, "") 슬롯 공통 → 3) 없으면 0 offset.
    /// </summary>
    public PortOffset GetOffset(string? nodeId, string? port)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            return new PortOffset();

        lock (_lock)
        {
            // 1) 슬롯 정확 매칭
            if (!string.IsNullOrWhiteSpace(port) &&
                _offsets.TryGetValue(MakeKey(nodeId, port), out var exact))
                return exact;

            // 2) 슬롯 공통 (Port 빈 값)
            if (_offsets.TryGetValue(MakeKey(nodeId, ""), out var common))
                return common;
        }

        return new PortOffset { NodeId = nodeId, Port = port ?? "" };
    }

    /// <summary>전체 offset 목록 (UI 표시용)</summary>
    public List<PortOffset> GetAll()
    {
        lock (_lock)
        {
            return _offsets.Values
                .OrderBy(o => o.NodeId)
                .ThenBy(o => o.Port)
                .ToList();
        }
    }

    /// <summary>offset 추가/수정 (같은 NodeId+Port 면 덮어씀) → 즉시 저장</summary>
    public void Upsert(PortOffset offset)
    {
        lock (_lock)
        {
            _offsets[MakeKey(offset.NodeId, offset.Port)] = offset;
        }
        _logger.LogInformation(
            "PortOffset 저장: Node={Node}, Port={Port}, dx={Dx}, dy={Dy}, drz={Drz}",
            offset.NodeId, offset.Port, offset.OffsetDx, offset.OffsetDy, offset.OffsetDrz);
        SaveToFile();
    }

    /// <summary>offset 삭제 → 즉시 저장</summary>
    public bool Delete(string nodeId, string port)
    {
        bool removed;
        lock (_lock)
        {
            removed = _offsets.Remove(MakeKey(nodeId, port));
        }
        if (removed)
        {
            _logger.LogInformation("PortOffset 삭제: Node={Node}, Port={Port}", nodeId, port);
            SaveToFile();
        }
        return removed;
    }

    #region 영구 저장 / 로드

    private void LoadFromFile()
    {
        if (!File.Exists(_filePath))
        {
            _logger.LogInformation("PortOffset 파일 없음 — 빈 테이블로 시작: {Path}", _filePath);
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var list = JsonSerializer.Deserialize<List<PortOffset>>(json) ?? new();
            lock (_lock)
            {
                _offsets = list.ToDictionary(o => MakeKey(o.NodeId, o.Port), o => o);
            }
            _logger.LogInformation("PortOffset {Count}건 로드 완료: {Path}", list.Count, _filePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PortOffset 로드 실패 — 빈 테이블 사용: {Path}", _filePath);
        }
    }

    private void SaveToFile()
    {
        try
        {
            List<PortOffset> list;
            lock (_lock) { list = _offsets.Values.ToList(); }
            var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PortOffset 저장 실패: {Path}", _filePath);
        }
    }

    #endregion
}
