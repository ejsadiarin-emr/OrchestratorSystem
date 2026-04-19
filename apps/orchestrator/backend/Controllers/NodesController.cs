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
    public async Task<ActionResult<NodeListResponse>> GetAll()
    {
        var nodes = await _db.Nodes
            .OrderBy(n => n.Hostname)
            .Select(n => new NodeSummaryDto
            {
                NodeId = n.NodeId,
                Hostname = n.Hostname,
                Status = n.Status,
                LastSeenUtc = n.LastSeenUtc
            })
            .ToListAsync();

        return Ok(new NodeListResponse { Nodes = nodes });
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
            Status = entity.Status,
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
            Status = "Online",
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
            Status = entity.Status,
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
            Status = entity.Status,
            LastSeenAt = entity.LastSeenUtc
        };

        _logger.LogInformation("Updated node {Hostname}", node.Hostname);
        
        return Ok(node);
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

    private static bool IsDuplicateHostnameConstraintViolation(DbUpdateException exception)
    {
        return exception.InnerException is SqliteException sqliteEx
            && sqliteEx.SqliteErrorCode == 19
            && sqliteEx.Message.Contains("UNIQUE constraint failed: Nodes.Hostname", StringComparison.Ordinal);
    }
}
