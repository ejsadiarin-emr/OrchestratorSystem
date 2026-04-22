using DeploymentPoC.Orchestrator.Runtime;

namespace DeploymentPoC.Orchestrator.Tests.Runtime;

[TestFixture]
public class AgentConnectionTrackerTests
{
    [Test]
    public void RegisterAndLookup()
    {
        var tracker = new AgentConnectionTracker();
        var nodeId = Guid.NewGuid();
        var connectionId = "conn-1";

        tracker.Register(nodeId, connectionId);

        Assert.That(tracker.TryGetConnectionId(nodeId, out var found), Is.True);
        Assert.That(found, Is.EqualTo(connectionId));
    }

    [Test]
    public void UnregisterRemovesMapping()
    {
        var tracker = new AgentConnectionTracker();
        var nodeId = Guid.NewGuid();
        var connectionId = "conn-1";

        tracker.Register(nodeId, connectionId);
        tracker.Unregister(connectionId);

        Assert.That(tracker.TryGetConnectionId(nodeId, out _), Is.False);
    }

    [Test]
    public void ReRegisterUpdatesConnection()
    {
        var tracker = new AgentConnectionTracker();
        var nodeId = Guid.NewGuid();

        tracker.Register(nodeId, "conn-1");
        tracker.Register(nodeId, "conn-2");

        Assert.That(tracker.TryGetConnectionId(nodeId, out var found), Is.True);
        Assert.That(found, Is.EqualTo("conn-2"));
    }
}
