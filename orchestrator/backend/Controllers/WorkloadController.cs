using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orchestrator.Models;
using Orchestrator.Services;
using System.Text.Json;

namespace Orchestrator.Controllers;

[ApiController]
[Route("api/workloads")]
public class WorkloadController : ControllerBase
{
    private readonly IWorkloadService _workloadService;
    private readonly AppDbContext _dbContext;

    public WorkloadController(IWorkloadService workloadService, AppDbContext dbContext)
    {
        _workloadService = workloadService;
        _dbContext = dbContext;
    }

    [HttpPost]
    public async Task<ActionResult<object>> Upload()
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();

        List<WorkloadDto> dtos;
        try
        {
            var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                dtos = JsonSerializer.Deserialize<List<WorkloadDto>>(body)
                    ?? throw new InvalidOperationException("invalid workload array");
            }
            else
            {
                var single = JsonSerializer.Deserialize<WorkloadDto>(body)
                    ?? throw new InvalidOperationException("invalid workload object");
                dtos = [single];
            }
        }
        catch (JsonException ex)
        {
            return BadRequest(new { error = $"invalid JSON: {ex.Message}" });
        }

        var result = await _workloadService.ImportAsync(dtos);

        return Ok(new
        {
            imported = result.Imported.Select(w => new
            {
                w.Id,
                w.WorkloadId,
                w.WorkloadName,
                w.Version,
                w.UploadedAt,
                Packages = w.Packages.Select(p => new
                {
                    p.PackageId,
                    p.PackageVersion
                })
            }),
            updated = result.Updated.Select(w => new
            {
                w.Id,
                w.WorkloadId,
                w.WorkloadName,
                w.Version,
                w.UploadedAt,
                Packages = w.Packages.Select(p => new
                {
                    p.PackageId,
                    p.PackageVersion
                })
            }),
            failed = result.Failed.Select(f => new
            {
                workloadId = f.workloadId,
                version = f.version,
                reason = f.reason
            })
        });
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<object>>> GetAll()
    {
        var workloads = await _workloadService.GetAllAsync();
        return Ok(workloads.Select(w => new
        {
            w.Id,
            w.WorkloadId,
            w.WorkloadName,
            w.Version,
            w.UploadedAt,
            Packages = w.Packages.Select(p => new
            {
                p.PackageId,
                p.PackageVersion
            })
        }));
    }

    [HttpGet("{workloadId}/{version}")]
    public async Task<ActionResult<object>> GetById(string workloadId, string version)
    {
        var workload = await _workloadService.GetByIdAsync(workloadId, version);
        if (workload == null)
            return NotFound();

        return Ok(new
        {
            workload.Id,
            workload.WorkloadId,
            workload.WorkloadName,
            workload.Version,
            workload.UploadedAt,
            Packages = workload.Packages.Select(p => new
            {
                p.PackageId,
                p.PackageVersion
            })
        });
    }
}
