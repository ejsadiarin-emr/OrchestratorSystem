namespace DeploymentPoC.Contracts.Runtime;

public static class MessageTypes
{
    public const string AssignJob = "AssignJob";
    public const string AckClaim = "AckClaim";
    public const string LeaseHeartbeat = "LeaseHeartbeat";
    public const string StepStatus = "StepStatus";
    public const string Complete = "Complete";
    public const string Fail = "Fail";
    public const string LeaseClose = "LeaseClose";
}