using System.Text.Json;
using DeploymentPoC.Contracts.Runtime;
using DeploymentPoC.Contracts.Runtime.RunPayloads;
using NUnit.Framework;

namespace DeploymentPoC.Agent.IntegrationTests;

public sealed class AgentRuntimeContractTests
{
    [Test]
    public void Serialize_ValidPayload_RoundTripsCorrectly()
    {
        var payload = new AssignRunPayload
        {
            RunId = Guid.NewGuid(),
            WorkloadId = Guid.NewGuid(),
            WorkloadName = "TestWorkload",
            RevisionId = Guid.NewGuid(),
            RevisionVersion = "1.0.0",
            Mode = "install",
            NodeId = Guid.NewGuid(),
            Packages =
            [
                new PackageAssignment
                {
                    PackageIndex = 0,
                    PackageId = "pkg-1",
                    Version = "1.0.0",
                    Channel = "stable"
                },
                new PackageAssignment
                {
                    PackageIndex = 1,
                    PackageId = "pkg-2",
                    Version = "2.0.0",
                    Channel = "beta"
                }
            ]
        };

        var json = JsonSerializer.Serialize(payload);
        var deserialized = JsonSerializer.Deserialize<AssignRunPayload>(json);

        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.WorkloadName, Is.EqualTo("TestWorkload"));
        Assert.That(deserialized.Packages, Has.Count.EqualTo(2));
        Assert.That(deserialized.Packages[0].PackageId, Is.EqualTo("pkg-1"));
        Assert.That(deserialized.Packages[1].PackageId, Is.EqualTo("pkg-2"));
        Assert.That(deserialized.RunId, Is.EqualTo(payload.RunId));
        Assert.That(deserialized.WorkloadId, Is.EqualTo(payload.WorkloadId));
        Assert.That(deserialized.RevisionVersion, Is.EqualTo(payload.RevisionVersion));
        Assert.That(deserialized.Mode, Is.EqualTo(payload.Mode));
    }

    [Test]
    public void Deserialize_FromJsonElement_PreservesAllFields()
    {
        var payload = new AssignRunPayload
        {
            RunId = Guid.NewGuid(),
            WorkloadId = Guid.NewGuid(),
            WorkloadName = "TestWorkload",
            RevisionId = Guid.NewGuid(),
            RevisionVersion = "1.0.0",
            Mode = "install",
            NodeId = Guid.NewGuid(),
            Packages =
            [
                new PackageAssignment
                {
                    PackageIndex = 0,
                    PackageId = "pkg-1",
                    Version = "1.0.0",
                    Channel = "stable"
                },
                new PackageAssignment
                {
                    PackageIndex = 1,
                    PackageId = "pkg-2",
                    Version = "2.0.0",
                    Channel = "beta"
                }
            ]
        };

        var json = JsonSerializer.Serialize(payload);
        var jsonElement = JsonSerializer.Deserialize<JsonElement>(json);
        var deserialized = JsonSerializer.Deserialize<AssignRunPayload>(jsonElement.GetRawText());

        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.WorkloadName, Is.EqualTo("TestWorkload"));
        Assert.That(deserialized.Packages, Has.Count.EqualTo(2));
        Assert.That(deserialized.Packages[0].PackageId, Is.EqualTo("pkg-1"));
        Assert.That(deserialized.Packages[1].PackageId, Is.EqualTo("pkg-2"));
    }

    [Test]
    public void Construct_WithNullRunId_AllowsNullProperty()
    {
        var payload = new AssignRunPayload
        {
            RunId = Guid.NewGuid(),
            WorkloadName = "NullRunIdTest",
            Packages =
            [
                new PackageAssignment
                {
                    PackageIndex = 0,
                    PackageId = "pkg-1",
                    Version = "1.0.0",
                    Channel = "stable"
                }
            ]
        };

        var envelope = new MessageEnvelope
        {
            MessageType = MessageTypes.AssignRun,
            RunId = null,
            AgentId = "test-agent",
            Sequence = 1,
            Payload = payload
        };

        Assert.That(envelope.MessageType, Is.EqualTo(MessageTypes.AssignRun));
        Assert.That(envelope.RunId, Is.Null);
        Assert.That(envelope.Payload, Is.Not.Null);

        var innerPayload = envelope.Payload as AssignRunPayload;
        Assert.That(innerPayload, Is.Not.Null);
        Assert.That(innerPayload!.WorkloadName, Is.EqualTo("NullRunIdTest"));
    }
}
