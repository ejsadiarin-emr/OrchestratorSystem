namespace Orchestrator.Configuration;

public class WebHostOptions
{
    public int Port { get; set; } = 5000;
    public string BindAddress { get; set; } = "0.0.0.0";
}
