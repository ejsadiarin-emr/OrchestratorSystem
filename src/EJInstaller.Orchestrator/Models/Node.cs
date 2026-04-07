namespace EJInstaller.Orchestrator.Models;

public class Node
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Hostname { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string Status { get; set; } = "Offline";
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    public string Description { get; set; } = string.Empty;
}

public class CreateNodeRequest
{
    public string Hostname { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class UpdateNodeRequest
{
    public string Hostname { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
