using DeploymentPoC.Agent.Pipeline;
using DeploymentPoC.Contracts.Runtime;

namespace DeploymentPoC.Agent.Steps;

public static class EmitFinalization
{
    public static MessageEnvelope CreateComplete(string runId, string agentId, int sequence, int stepCount)
    {
        return new MessageEnvelope
        {
            MessageType = MessageTypes.Complete,
            RunId = runId,
            AgentId = agentId,
            Sequence = sequence,
            Payload = new FinalizationPayload
            {
                Result = "success",
                StepCount = stepCount
            }
        };
    }

    public static MessageEnvelope CreateFail(string runId, string agentId, int sequence, string? error, int stepCount)
    {
        return new MessageEnvelope
        {
            MessageType = MessageTypes.Fail,
            RunId = runId,
            AgentId = agentId,
            Sequence = sequence,
            Payload = new FinalizationPayload
            {
                Result = "failure",
                Error = error,
                StepCount = stepCount
            }
        };
    }
}
