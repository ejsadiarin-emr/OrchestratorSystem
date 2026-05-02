namespace DeploymentPoC.Contracts.Runtime.RunPayloads;

public sealed class InstallAdapterConfig
{
    public string Type { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string UninstallArgs { get; set; } = string.Empty;
    public string UninstallCommand { get; set; } = string.Empty;
    public string UpgradeBehavior { get; set; } = "InPlace";
    public List<int> ExpectedExitCodes { get; set; } = new() { 0 };
    public int TimeoutSeconds { get; set; } = 300;
}
