namespace DeploymentPoC.Contracts.Runtime;

public class MessageEnvelope
{
    public string MessageType { get; set; } = string.Empty;
    public string ProtocolVersion { get; set; } = "1.0";
    public string MessageId { get; set; } = Guid.NewGuid().ToString();
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string? AssignmentId { get; set; }
    public string? LeaseId { get; set; }
    public string? RunId { get; set; }
    public string? AgentId { get; set; }
    public int Sequence { get; set; }
    public object Payload { get; set; } = new();
}