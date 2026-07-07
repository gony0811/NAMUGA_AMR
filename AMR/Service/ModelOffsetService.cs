using System.Text.Json;
using AMR.Models;
using Microsoft.Extensions.Logging;

namespace AMR.Service;

/// <summary>
/// 모델별 LOAD/UNLOAD offset 보정값 관리 (노드·포트 무관 공통).
///   - 시작 시 model_offsets.json (exe 옆) 에서 로드
///   - 변경 시 즉시 저장
///   - QR offset 전달 직전 GetOffset(model) 조회 → JobType 에 따라 LOAD/UNLOAD 값 합산
///
/// PortOffset(노드+슬롯) 위에 추가로 더해진다. ACS·Fairino 무수정.
/// </summary>
public class ModelOffsetService
{
    private readonly ILogger<ModelOffsetService> _logger;
    private readonly object _lock = new();
    private readonly string _filePath;

    // 키 = MODEL (대문자)
    private Dictionary<string, ModelOffset> _offsets = new();

    public ModelOffsetService(ILogger<ModelOffsetService> logger)
    {
        _logger = logger;
        _filePath = Path.Combine(AppContext.BaseDirectory, "model_offsets.json");
        LoadFromFile();
    }

    private static string MakeKey(string model)
        => (model ?? "").Trim().ToUpperInvariant();

    /// <summary>모델에 해당하는 offset 조회 — 없으면 0 offset</summary>
    public ModelOffset GetOffset(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return new ModelOffset();
        lock (_lock)
        {
            return _offsets.TryGetValue(MakeKey(model), out var o)
                ? o
                : new ModelOffset { Model = model };
        }
    }

    /// <summary>
    /// 모델 + JobType(LOAD/UNLOAD) 에 맞는 (dx, dy, drz) 보정값 반환.
    /// JobType 이 LOAD 면 Load*, 그 외(UNLOAD 등)면 Unload* 사용.
    /// </summary>
    public (double Dx, double Dy, double Drz) GetForJob(string? model, string? jobType)
    {
        var o = GetOffset(model);
        var isLoad = string.Equals(jobType, "LOAD", StringComparison.OrdinalIgnoreCase);
        return isLoad
            ? (o.LoadDx, o.LoadDy, o.LoadDrz)
            : (o.UnloadDx, o.UnloadDy, o.UnloadDrz);
    }

    /// <summary>전체 목록 (UI 표시용)</summary>
    public List<ModelOffset> GetAll()
    {
        lock (_lock) { return _offsets.Values.OrderBy(o => o.Model).ToList(); }
    }

    /// <summary>추가/수정 → 즉시 저장</summary>
    public void Upsert(ModelOffset offset)
    {
        lock (_lock) { _offsets[MakeKey(offset.Model)] = offset; }
        _logger.LogInformation(
            "ModelOffset 저장: Model={Model}, LOAD=({LDx},{LDy},{LDrz}), UNLOAD=({UDx},{UDy},{UDrz})",
            offset.Model, offset.LoadDx, offset.LoadDy, offset.LoadDrz,
            offset.UnloadDx, offset.UnloadDy, offset.UnloadDrz);
        SaveToFile();
    }

    /// <summary>삭제 → 즉시 저장</summary>
    public bool Delete(string model)
    {
        bool removed;
        lock (_lock) { removed = _offsets.Remove(MakeKey(model)); }
        if (removed) { _logger.LogInformation("ModelOffset 삭제: Model={Model}", model); SaveToFile(); }
        return removed;
    }

    #region 영구 저장 / 로드

    private void LoadFromFile()
    {
        if (!File.Exists(_filePath))
        {
            _logger.LogInformation("ModelOffset 파일 없음 — 빈 테이블로 시작: {Path}", _filePath);
            return;
        }
        try
        {
            var json = File.ReadAllText(_filePath);
            var list = JsonSerializer.Deserialize<List<ModelOffset>>(json) ?? new();
            lock (_lock) { _offsets = list.ToDictionary(o => MakeKey(o.Model), o => o); }
            _logger.LogInformation("ModelOffset {Count}건 로드 완료: {Path}", list.Count, _filePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ModelOffset 로드 실패 — 빈 테이블 사용: {Path}", _filePath);
        }
    }

    private void SaveToFile()
    {
        try
        {
            List<ModelOffset> list;
            lock (_lock) { list = _offsets.Values.ToList(); }
            var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ModelOffset 저장 실패: {Path}", _filePath);
        }
    }

    #endregion
}
