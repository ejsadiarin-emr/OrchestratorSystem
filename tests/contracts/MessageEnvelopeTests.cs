using DeploymentPoC.Contracts.Runtime;
using Xunit;

namespace DeploymentPoC.Contracts.Tests;

public class MessageEnvelopeTests
{
    [Fact]
    public void MessageEnvelope_ShouldHaveRequiredFields()
    {
        // Arrange & Act
        var envelope = new MessageEnvelope
        {
            MessageType = MessageTypes.AssignRun,
            AssignmentId = "test-assignment",
            LeaseId = "test-lease",
            RunId = "test-run",
            AgentId = "test-agent",
            Sequence = 42
        };

        // Assert
        Assert.Equal(MessageTypes.AssignRun, envelope.MessageType);
        Assert.Equal("1.0", envelope.ProtocolVersion);
        Assert.False(string.IsNullOrEmpty(envelope.MessageId));
        Assert.NotEqual(default(DateTime), envelope.TimestampUtc);
        Assert.Equal("test-assignment", envelope.AssignmentId);
        Assert.Equal("test-lease", envelope.LeaseId);
        Assert.Equal("test-run", envelope.RunId);
        Assert.Equal("test-agent", envelope.AgentId);
        Assert.Equal(42, envelope.Sequence);
    }
}