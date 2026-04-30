using DeploymentPoC.Contracts.Runtime.RunPayloads;

namespace DeploymentPoC.Agent.Pipeline;

public static class InitStepEnvVars
{
    public static Dictionary<string, string> Build(PipelineContext context, PackageAssignment? package, string? artifactPath)
    {
        var vars = new Dictionary<string, string>
        {
            ["DEPLOY_RUN_ID"] = context.RunId,
            ["DEPLOY_AGENT_ID"] = context.AgentId,
            ["DEPLOY_WORKLOAD_NAME"] = context.Payload.WorkloadName,
            ["DEPLOY_ORCHESTRATOR_URL"] = context.OrchestratorBaseUrl
        };
        if (package is not null)
        {
            vars["DEPLOY_PACKAGE_NAME"] = package.Name;
            vars["DEPLOY_PACKAGE_VERSION"] = package.Version;
        }
        if (artifactPath is not null)
            vars["DEPLOY_ARTIFACT_PATH"] = artifactPath;
        return vars;
    }
}
