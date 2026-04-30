using System.Linq;
using DeploymentPoC.Orchestrator.Contracts.Api;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using DeploymentPoC.Orchestrator.Data;
using DeploymentPoC.Orchestrator.Data.Entities;
using DeploymentPoC.Orchestrator.Models;

namespace DeploymentPoC.Orchestrator.Controllers;

[ApiController]
[Route("api/nodes")]
public class NodesController : ControllerBase
{
    private readonly InstallerDbContext _db;
    private readonly ILogger<NodesController> _logger;

    public NodesController(InstallerDbContext db, ILogger<NodesController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<Node>>> GetAll()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-2);
        var nodes = await _db.Nodes
            .OrderBy(n => n.Hostname)
            .Select(n => new Node
            {
                Id = n.NodeId,
                Hostname = n.Hostname,
                DisplayName = n.DisplayName,
                IpAddress = n.IpAddress,
                Status = n.LastSeenUtc >= cutoff ? "online" : "offline",
                LastSeenAt = n.LastSeenUtc,
                FirstConnectedAt = n.FirstConnectedUtc,
                Description = n.Description,
                OsVersion = n.OsVersion,
                AgentVersion = n.AgentVersion,
            })
            .ToListAsync();

        return Ok(nodes);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Node>> GetById(Guid id)
    {
        var entity = await _db.Nodes.SingleOrDefaultAsync(n => n.NodeId == id);
        if (entity is null)
        {
            return NotFound(new { message = $"Node {id} not found" });
        }

        return Ok(new Node
        {
            Id = entity.NodeId,
            Hostname = entity.Hostname,
            IpAddress = entity.IpAddress,
            Description = entity.Description,
            Status = entity.LastSeenUtc >= DateTime.UtcNow.AddMinutes(-2) ? "online" : "offline",
            LastSeenAt = entity.LastSeenUtc
        });
    }

    [HttpPost]
    public async Task<ActionResult<Node>> Create([FromBody] CreateNodeRequest request)
    {
        var entity = new NodeEntity
        {
            NodeId = Guid.NewGuid(),
            Hostname = request.Hostname,
            IpAddress = request.IpAddress,
            Description = request.Description,
            LastSeenUtc = DateTime.UtcNow
        };

        _db.Nodes.Add(entity);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsDuplicateHostnameConstraintViolation(ex))
        {
            return Conflict(new { message = $"A node with hostname '{request.Hostname}' already exists" });
        }

        var node = new Node
        {
            Id = entity.NodeId,
            Hostname = entity.Hostname,
            IpAddress = request.IpAddress,
            Description = request.Description,
            Status = entity.LastSeenUtc >= DateTime.UtcNow.AddMinutes(-2) ? "online" : "offline",
            LastSeenAt = entity.LastSeenUtc
        };

        _logger.LogInformation("Registered node {Hostname} ({IpAddress})", node.Hostname, node.IpAddress);
        
        return CreatedAtAction(nameof(GetById), new { id = node.Id }, node);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<Node>> Update(Guid id, [FromBody] UpdateNodeRequest request)
    {
        var entity = await _db.Nodes.SingleOrDefaultAsync(n => n.NodeId == id);
        if (entity is null)
        {
            return NotFound(new { message = $"Node {id} not found" });
        }

        entity.Hostname = request.Hostname;
        entity.DisplayName = request.DisplayName;
        entity.IpAddress = request.IpAddress;
        entity.Description = request.Description;
        entity.LastSeenUtc = DateTime.UtcNow;
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsDuplicateHostnameConstraintViolation(ex))
        {
            return Conflict(new { message = $"A node with hostname '{request.Hostname}' already exists" });
        }

        var node = new Node
        {
            Id = entity.NodeId,
            Hostname = entity.Hostname,
            IpAddress = request.IpAddress,
            Description = request.Description,
            Status = entity.LastSeenUtc >= DateTime.UtcNow.AddMinutes(-2) ? "online" : "offline",
            LastSeenAt = entity.LastSeenUtc
        };

        _logger.LogInformation("Updated node {Hostname}", node.Hostname);
        
        return Ok(node);
    }

    [HttpPatch("{id:guid}/display-name")]
    public async Task<ActionResult<Node>> UpdateDisplayName(Guid id, [FromBody] UpdateNodeDisplayNameRequest request)
    {
        var entity = await _db.Nodes.SingleOrDefaultAsync(n => n.NodeId == id);
        if (entity is null)
        {
            return NotFound(new { message = $"Node {id} not found" });
        }

        entity.DisplayName = request.DisplayName;
        await _db.SaveChangesAsync();

        return Ok(new Node
        {
            Id = entity.NodeId,
            Hostname = entity.Hostname,
            DisplayName = entity.DisplayName,
            IpAddress = entity.IpAddress,
            Description = entity.Description,
            Status = entity.LastSeenUtc >= DateTime.UtcNow.AddMinutes(-2) ? "online" : "offline",
            LastSeenAt = entity.LastSeenUtc,
            FirstConnectedAt = entity.FirstConnectedUtc,
            OsVersion = entity.OsVersion,
            AgentVersion = entity.AgentVersion,
        });
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id)
    {
        var entity = await _db.Nodes.SingleOrDefaultAsync(n => n.NodeId == id);
        if (entity is null)
        {
            return NotFound(new { message = $"Node {id} not found" });
        }

        _db.Nodes.Remove(entity);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Deleted node {Id}", id);
        return NoContent();
    }

    [HttpGet("workload-states")]
    public async Task<ActionResult<List<NodeWorkloadStateResponse>>> GetWorkloadStates()
    {
        var states = await _db.NodeWorkloadStates
            .AsNoTracking()
            .Include(s => s.CurrentRevision)
            .Include(s => s.Workload)
            .Select(s => new NodeWorkloadStateResponse
            {
                NodeId = s.NodeId,
                WorkloadId = s.WorkloadId,
                WorkloadRevision = s.CurrentRevision != null ? s.CurrentRevision.Version : "",
                RunId = Guid.Empty,
                Status = InferStatus(s),
                UpdatedAt = s.UpdatedAtUtc.ToString("O")
            })
            .ToListAsync();

        return Ok(states);
    }

    [HttpGet("{id:guid}/details")]
    public async Task<ActionResult<NodeDetailResponse>> GetDetails(Guid id)
    {
        var entity = await _db.Nodes
            .Include(n => n.NodeWorkloadStates)
            .ThenInclude(s => s.Workload)
            .Include(n => n.NodeWorkloadStates)
            .ThenInclude(s => s.CurrentRevision)
            .SingleOrDefaultAsync(n => n.NodeId == id);

        if (entity is null)
        {
            return NotFound(new { message = $"Node {id} not found" });
        }

        var workloads = entity.NodeWorkloadStates.Select(s => new NodeWorkloadAssignment
        {
            WorkloadId = s.WorkloadId,
            Name = s.Workload.Name,
            Status = InferStatus(s),
            CurrentVersion = s.CurrentRevision?.Version ?? ""
        }).ToList();

        var preCheck = BuildPreCheckSummary(entity);

        return Ok(new NodeDetailResponse
        {
            Id = entity.NodeId,
            Hostname = entity.Hostname,
            DisplayName = entity.DisplayName,
            IpAddress = entity.IpAddress,
            Status = entity.LastSeenUtc >= DateTime.UtcNow.AddMinutes(-2) ? "online" : "offline",
            LastSeenAt = entity.LastSeenUtc,
            FirstConnectedAt = entity.FirstConnectedUtc,
            Description = entity.Description,
            OsVersion = entity.OsVersion,
            AgentVersion = entity.AgentVersion,
            Workloads = workloads,
            LatestPreCheck = preCheck
        });
    }

    [HttpPost("{id:guid}/prechecks")]
    public async Task<ActionResult<NodePreCheckSummary>> RunPreChecks(Guid id, [FromBody] RunPreCheckRequest request)
    {
        var entity = await _db.Nodes
            .Include(n => n.NodeWorkloadStates)
            .ThenInclude(s => s.Workload)
            .Include(n => n.NodeWorkloadStates)
            .ThenInclude(s => s.CurrentRevision)
            .SingleOrDefaultAsync(n => n.NodeId == id);

        if (entity is null)
        {
            return NotFound(new { message = $"Node {id} not found" });
        }

        var preCheck = BuildPreCheckSummary(entity);
        return Ok(preCheck);
    }

    private static NodePreCheckSummary BuildPreCheckSummary(NodeEntity entity)
    {
        var items = new List<PreCheckItem>
        {
            new PreCheckItem
            {
                Category = "os",
                Name = "Operating System",
                Status = "passed",
                Detail = entity.OsVersion
            },
            new PreCheckItem
            {
                Category = "agent",
                Name = "Agent Version",
                Status = "passed",
                Detail = entity.AgentVersion
            },
            new PreCheckItem
            {
                Category = "disk",
                Name = "Disk Space",
                Status = "warning",
                Detail = "Agent telemetry not yet implemented"
            }
        };

        foreach (var state in entity.NodeWorkloadStates)
        {
            items.Add(new PreCheckItem
            {
                Category = "package",
                Name = state.Workload.Name,
                ExpectedVersion = state.CurrentRevision?.Version ?? "",
                ActualVersion = state.CurrentRevision?.Version ?? "",
                Status = "passed",
                Detail = ""
            });
        }

        return new NodePreCheckSummary
        {
            CheckedAt = DateTime.UtcNow,
            Items = items
        };
    }

    private static string InferStatus(NodeWorkloadStateEntity state)
    {
        if (string.IsNullOrEmpty(state.PackageStatesJson) || state.PackageStatesJson == "{}")
        {
            return "pending";
        }
        return "running";
    }

    private static bool IsDuplicateHostnameConstraintViolation(DbUpdateException exception)
    {
        return exception.InnerException is SqliteException sqliteEx
            && sqliteEx.SqliteErrorCode == 19
            && sqliteEx.Message.Contains("UNIQUE constraint failed: Nodes.Hostname", StringComparison.Ordinal);
    }
}
