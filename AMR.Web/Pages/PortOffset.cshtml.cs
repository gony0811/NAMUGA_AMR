using AMR.Models;
using AMR.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AMR.Web.Pages;

[IgnoreAntiforgeryToken]
public class PortOffsetModel : PageModel
{
    private readonly PortOffsetService _service;

    public PortOffsetModel(PortOffsetService service)
    {
        _service = service;
    }

    public void OnGet() { }

    /// <summary>전체 offset 목록 (AJAX)</summary>
    public IActionResult OnGetList()
    {
        var list = _service.GetAll().Select(o => new
        {
            nodeId = o.NodeId,
            port = o.Port,
            offsetDx = o.OffsetDx,
            offsetDy = o.OffsetDy,
            offsetDrz = o.OffsetDrz
        });
        return new JsonResult(list);
    }

    public class UpsertRequest
    {
        public string? NodeId { get; set; }
        public string? Port { get; set; }
        public double OffsetDx { get; set; }
        public double OffsetDy { get; set; }
        public double OffsetDrz { get; set; }
    }

    /// <summary>offset 추가/수정</summary>
    public IActionResult OnPostUpsert([FromBody] UpsertRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.NodeId))
            return new JsonResult(new { success = false, error = "NodeId는 필수입니다." });

        _service.Upsert(new PortOffset
        {
            NodeId = req.NodeId.Trim(),
            Port = (req.Port ?? "").Trim().ToUpperInvariant(),
            OffsetDx = req.OffsetDx,
            OffsetDy = req.OffsetDy,
            OffsetDrz = req.OffsetDrz
        });
        return new JsonResult(new { success = true });
    }

    public class DeleteRequest
    {
        public string? NodeId { get; set; }
        public string? Port { get; set; }
    }

    /// <summary>offset 삭제</summary>
    public IActionResult OnPostDelete([FromBody] DeleteRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.NodeId))
            return new JsonResult(new { success = false, error = "NodeId 필요" });

        var removed = _service.Delete(req.NodeId.Trim(), (req.Port ?? "").Trim());
        return new JsonResult(new { success = removed });
    }
}
