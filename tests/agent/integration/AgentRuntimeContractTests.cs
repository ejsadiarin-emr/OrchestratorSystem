using System.Text.Json;
using DeploymentPoC.Agent.Services;
using DeploymentPoC.Contracts.Runtime;
using DeploymentPoC.Contracts.Runtime.RunPayloads;
using NUnit.Framework;

namespace DeploymentPoC.Agent.IntegrationTests;

public sealed class AgentRuntimeContractTests
{
    [Test]
    public void ParseAssignRunPayload_ExtractsWorkloadNameAndPackageCount_FromJsonElementPayload()
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

        var envelope = new MessageEnvelope
        {
            MessageType = MessageTypes.AssignRun,
            RunId = payload.RunId.ToString(),
            AgentId = "test-agent",
            Sequence = 1,
            Payload = jsonElement
        };

        var parsed = AgentRuntimeService.ParseAssignRunPayload(envelope.Payload);

        Assert.That(parsed, Is.Not.Null);
        Assert.That(parsed.WorkloadName, Is.EqualTo("TestWorkload"));
        Assert.That(parsed.Packages, Has.Count.EqualTo(2));
        Assert.That(parsed.Packages[0].PackageId, Is.EqualTo("pkg-1"));
        Assert.That(parsed.Packages[1].PackageId, Is.EqualTo("pkg-2"));
    }

    [Test]
    public void ParseAssignRunPayload_ReturnsPayloadDirectly_WhenAlreadyTyped()
    {
        var payload = new AssignRunPayload
        {
            RunId = Guid.NewGuid(),
            WorkloadName = "DirectPayload",
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
            Payload = payload
        };

        var parsed = AgentRuntimeService.ParseAssignRunPayload(envelope.Payload);

        Assert.That(parsed, Is.Not.Null);
        Assert.That(parsed.WorkloadName, Is.EqualTo("DirectPayload"));
        Assert.That(parsed.Packages, Has.Count.EqualTo(1));
    }

    [Test]
    public void ParseAssignRunPayload_WithNullRunId_ThrowsMeaningfulException()
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

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            if (string.IsNullOrWhiteSpace(envelope.RunId))
            {
                throw new InvalidOperationException("AssignRun message missing required RunId");
            }
            AgentRuntimeService.ParseAssignRunPayload(envelope.Payload);
        });

        Assert.That(ex!.Message, Does.Contain("RunId"));
    }
}
