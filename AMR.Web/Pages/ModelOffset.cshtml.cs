using AMR.Models;
using AMR.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AMR.Web.Pages;

[IgnoreAntiforgeryToken]
public class ModelOffsetModel : PageModel
{
    private readonly ModelOffsetService _service;

    public ModelOffsetModel(ModelOffsetService service)
    {
        _service = service;
    }

    public void OnGet() { }

    public IActionResult OnGetList()
    {
        var list = _service.GetAll().Select(o => new
        {
            model = o.Model,
            loadDx = o.LoadDx, loadDy = o.LoadDy, loadDrz = o.LoadDrz,
            unloadDx = o.UnloadDx, unloadDy = o.UnloadDy, unloadDrz = o.UnloadDrz
        });
        return new JsonResult(list);
    }

    public class UpsertRequest
    {
        public string? Model { get; set; }
        public double LoadDx { get; set; }
        public double LoadDy { get; set; }
        public double LoadDrz { get; set; }
        public double UnloadDx { get; set; }
        public double UnloadDy { get; set; }
        public double UnloadDrz { get; set; }
    }

    public IActionResult OnPostUpsert([FromBody] UpsertRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Model))
            return new JsonResult(new { success = false, error = "Model은 필수입니다." });

        _service.Upsert(new ModelOffset
        {
            Model = req.Model.Trim(),
            LoadDx = req.LoadDx, LoadDy = req.LoadDy, LoadDrz = req.LoadDrz,
            UnloadDx = req.UnloadDx, UnloadDy = req.UnloadDy, UnloadDrz = req.UnloadDrz
        });
        return new JsonResult(new { success = true });
    }

    public class DeleteRequest { public string? Model { get; set; } }

    public IActionResult OnPostDelete([FromBody] DeleteRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Model))
            return new JsonResult(new { success = false, error = "Model 필요" });
        var removed = _service.Delete(req.Model.Trim());
        return new JsonResult(new { success = removed });
    }
}
