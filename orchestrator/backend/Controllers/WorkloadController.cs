using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orchestrator.Models;
using Orchestrator.Services;

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

    [HttpPost("upsert")]
    public async Task<ActionResult<object>> Upsert([FromBody] WorkloadDto dto)
    {
        var workload = await _workloadService.UpsertAsync(dto);
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
