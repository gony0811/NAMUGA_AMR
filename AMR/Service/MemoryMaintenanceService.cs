using System.Runtime;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AMR.Service;

/// <summary>
/// 주기적으로 Large Object Heap(LOH) 압축을 트리거.
///
/// 배경:
///   카메라 서비스가 15fps 로 JPEG 인코딩(~100KB) 을 만들어내고,
///   85KB 초과 객체는 모두 LOH 로 들어간다. LOH 는 기본적으로 압축되지 않아서
///   collection 후에도 가용 공간이 단편화 상태로 남아 working set 이 지속 증가하는
///   것처럼 보인다. 운영 PC 메모리 점진 증가의 큰 원인.
///
/// 해결:
///   N분 마다 `LargeObjectHeapCompactionMode.CompactOnce` 를 설정하고
///   `GC.Collect()` 한 번 호출 → 그 다음 Gen2 collection 에서 LOH 압축 1회 수행.
///   강제 GC 는 비싸지만 10분 간격이면 사용자가 체감하는 지연은 거의 없음
///   (대부분 100ms 미만, 메모리 많을 때 수백 ms).
/// </summary>
public class MemoryMaintenanceService : BackgroundService
{
    private readonly ILogger<MemoryMaintenanceService> _logger;

    /// <summary>LOH 압축 주기 — 너무 자주 하면 STW 빈도 증가, 너무 드물면 효과 없음</summary>
    private static readonly TimeSpan CompactionInterval = TimeSpan.FromMinutes(10);

    public MemoryMaintenanceService(ILogger<MemoryMaintenanceService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "MemoryMaintenanceService 시작 — LOH 압축 주기 {Min}분",
            CompactionInterval.TotalMinutes);

        // 첫 압축은 시작 직후가 아니라 주기 한 번 지난 뒤
        await Task.Delay(CompactionInterval, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                CompactLoh();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LOH 압축 중 오류");
            }

            await Task.Delay(CompactionInterval, stoppingToken);
        }
    }

    private void CompactLoh()
    {
        var before = GC.GetTotalMemory(forceFullCollection: false);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(generation: 2, mode: GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();

        sw.Stop();
        var after = GC.GetTotalMemory(forceFullCollection: false);
        var freedMb = (before - after) / 1024.0 / 1024.0;

        _logger.LogInformation(
            "LOH 압축 완료 — {Ms}ms, 회수 {Mb:F1}MB ({BeforeMb:F1}MB → {AfterMb:F1}MB)",
            sw.ElapsedMilliseconds, freedMb,
            before / 1024.0 / 1024.0, after / 1024.0 / 1024.0);
    }
}
