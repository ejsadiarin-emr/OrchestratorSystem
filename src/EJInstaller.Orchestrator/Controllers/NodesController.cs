using Microsoft.AspNetCore.Mvc;
using EJInstaller.Orchestrator.Models;
using EJInstaller.Orchestrator.Store;
using Microsoft.Extensions.Logging;

namespace EJInstaller.Orchestrator.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NodesController : ControllerBase
{
    private readonly AppStore _store;
    private readonly ILogger<NodesController> _logger;

    public NodesController(AppStore store, ILogger<NodesController> logger)
    {
        _store = store;
        _logger = logger;
    }

    [HttpGet]
    public ActionResult<IEnumerable<Node>> GetAll()
    {
        return Ok(_store.Nodes.Values.ToList());
    }

    [HttpGet("{id:guid}")]
    public ActionResult<Node> GetById(Guid id)
    {
        if (_store.Nodes.TryGetValue(id, out var node))
        {
            return Ok(node);
        }
        return NotFound(new { message = $"Node {id} not found" });
    }

    [HttpPost]
    public ActionResult<Node> Create([FromBody] CreateNodeRequest request)
    {
        var node = new Node
        {
            Hostname = request.Hostname,
            IpAddress = request.IpAddress,
            Description = request.Description,
            Status = "Online",
            LastSeenAt = DateTime.UtcNow
        };

        _store.Nodes[node.Id] = node;
        _logger.LogInformation("Registered node {Hostname} ({IpAddress})", node.Hostname, node.IpAddress);
        
        return CreatedAtAction(nameof(GetById), new { id = node.Id }, node);
    }

    [HttpPut("{id:guid}")]
    public ActionResult<Node> Update(Guid id, [FromBody] UpdateNodeRequest request)
    {
        if (!_store.Nodes.TryGetValue(id, out var node))
        {
            return NotFound(new { message = $"Node {id} not found" });
        }

        node.Hostname = request.Hostname;
        node.IpAddress = request.IpAddress;
        node.Description = request.Description;

        _logger.LogInformation("Updated node {Hostname}", node.Hostname);
        
        return Ok(node);
    }

    [HttpDelete("{id:guid}")]
    public ActionResult Delete(Guid id)
    {
        if (_store.Nodes.Remove(id))
        {
            _logger.LogInformation("Deleted node {Id}", id);
            return NoContent();
        }
        return NotFound(new { message = $"Node {id} not found" });
    }
}
