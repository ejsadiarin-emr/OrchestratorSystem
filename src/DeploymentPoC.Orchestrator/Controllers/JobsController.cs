using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using DeploymentPoC.Orchestrator.Models;
using DeploymentPoC.Orchestrator.Store;

namespace DeploymentPoC.Orchestrator.Controllers;

[ApiController]
[Route("api/[controller]")]
public class JobsController : ControllerBase
{
    private readonly AppStore _store;
    private readonly ILogger<JobsController> _logger;

    public JobsController(AppStore store, ILogger<JobsController> logger)
    {
        _store = store;
        _logger = logger;
    }

    [HttpGet]
    public ActionResult<IEnumerable<InstallJob>> GetAll([FromQuery] string? status = null)
    {
        var jobs = _store.Jobs.Values.AsEnumerable();
        
        if (!string.IsNullOrEmpty(status))
        {
            jobs = jobs.Where(j => j.Status.Equals(status, StringComparison.OrdinalIgnoreCase));
        }
        
        return Ok(jobs.OrderByDescending(j => j.StartedAt).ToList());
    }

    [HttpGet("{id:guid}")]
    public ActionResult<InstallJob> GetById(Guid id)
    {
        if (_store.Jobs.TryGetValue(id, out var job))
        {
            return Ok(job);
        }
        return NotFound(new { message = $"Job {id} not found" });
    }

    [HttpPost]
    public async Task<ActionResult<InstallJob>> Create([FromBody] CreateJobRequest request)
    {
        if (!_store.Packages.TryGetValue(request.PackageId, out var package))
        {
            return BadRequest(new { message = $"Package {request.PackageId} not found" });
        }

        if (!_store.Nodes.TryGetValue(request.TargetNodeId, out var node))
        {
            return BadRequest(new { message = $"Node {request.TargetNodeId} not found" });
        }

        var job = new InstallJob
        {
            PackageId = package.Id,
            PackageName = package.Name,
            TargetNodeId = node.Id,
            TargetNodeHostname = node.Hostname,
            Status = "Running",
            CurrentStep = 1,
            TotalSteps = 2,
            Steps = new List<JobStep>
            {
                new() { Name = "PreConditionCheck", Status = "Running" },
                new() { Name = "CopyFiles", Status = "Pending" }
            }
        };

        _store.Jobs[job.Id] = job;
        _logger.LogInformation("Created job {JobId} for package {Package} on node {Node}", 
            job.Id, package.Name, node.Hostname);

        // Simulate async job execution
        _ = Task.Run(async () =>
        {
            await Task.Delay(500);
            job.Steps[0].Status = "Completed";
            job.Steps[0].Duration = "0.5s";
            job.CurrentStep = 2;
            
            await Task.Delay(1000);
            job.Steps[1].Status = "Completed";
            job.Steps[1].Duration = "1.0s";
            job.Status = "Completed";
            job.CompletedAt = DateTime.UtcNow;
        });

        return CreatedAtAction(nameof(GetById), new { id = job.Id }, job);
    }

    [HttpDelete("{id:guid}")]
    public ActionResult Cancel(Guid id)
    {
        if (!_store.Jobs.TryGetValue(id, out var job))
        {
            return NotFound(new { message = $"Job {id} not found" });
        }

        if (job.Status == "Completed" || job.Status == "Failed")
        {
            return BadRequest(new { message = "Cannot cancel completed or failed job" });
        }

        job.Status = "Cancelled";
        job.CompletedAt = DateTime.UtcNow;
        foreach (var step in job.Steps.Where(s => s.Status == "Running" || s.Status == "Pending"))
        {
            step.Status = "Cancelled";
        }

        _logger.LogInformation("Cancelled job {JobId}", id);
        
        return Ok(job);
    }
}
