namespace DeploymentPoC.Contracts.Jobs;

public enum JobState
{
    Pending = 0,
    Assigned = 1,
    Running = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5
}