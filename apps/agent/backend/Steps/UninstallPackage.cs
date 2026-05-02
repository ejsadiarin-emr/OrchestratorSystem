using DeploymentPoC.Contracts.Runtime.RunPayloads;
using System.ComponentModel;
using System.Diagnostics;

namespace DeploymentPoC.Agent.Steps;

public static class UninstallPackage
{
    public static Task<UninstallResult> ExecuteAsync(InstallAdapterConfig config, CancellationToken ct)
        => ExecuteAsync(config, null, ct);

    public static async Task<UninstallResult> ExecuteAsync(InstallAdapterConfig config, string? artifactPath, CancellationToken ct)
    {
        if (config is null)
        {
            return new UninstallResult { Success = false, Error = "invalid_config" };
        }

        // Prefer dedicated uninstall command if specified (avoids artifact download)
        var command = config.UninstallCommand;
        var arguments = config.UninstallArgs ?? string.Empty;

        // fallback 1: use Command field if no UninstallCommand is set
        if (string.IsNullOrWhiteSpace(command))
        {
            command = config.Command;
        }

        // fallback 2: if command is missing or is the placeholder itself, execute the artifact directly
        if (string.IsNullOrWhiteSpace(command) ||
            string.Equals(command, "{artifactPath}", StringComparison.OrdinalIgnoreCase))
        {
            command = artifactPath;
        }

        if (string.IsNullOrWhiteSpace(command))
        {
            return new UninstallResult { Success = false, Error = "missing_command" };
        }

        // MSI files cannot be executed directly; invoke via msiexec for uninstall
        if (string.Equals(config.Type, "msi", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(artifactPath) &&
            string.Equals(command, artifactPath, StringComparison.OrdinalIgnoreCase))
        {
            command = "msiexec";
            arguments = $"/x \"{artifactPath}\" {arguments}".Trim();
        }

        // Expand placeholder {artifactPath} in arguments
        arguments = arguments.Replace("{artifactPath}", artifactPath, StringComparison.OrdinalIgnoreCase);

        var expectedExitCodes = config.ExpectedExitCodes is { Count: > 0 }
            ? config.ExpectedExitCodes
            : new List<int> { 0 };

        var timeout = config.TimeoutSeconds > 0
            ? TimeSpan.FromSeconds(config.TimeoutSeconds)
            : TimeSpan.FromSeconds(300);

        try
        {
            using var process = new Process();
            process.StartInfo.FileName = command;
            process.StartInfo.Arguments = arguments;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;

            process.Start();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException) when (cts.Token.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                try { process.Kill(); } catch { /* best-effort */ }
                return new UninstallResult { Success = false, Error = "uninstall_timeout" };
            }

            if (!expectedExitCodes.Contains(process.ExitCode))
            {
                var stderr = await process.StandardError.ReadToEndAsync(ct);
                return new UninstallResult
                {
                    Success = false,
                    Error = $"exit_code_{process.ExitCode}",
                    StandardError = stderr
                };
            }

            return new UninstallResult { Success = true };
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            return new UninstallResult { Success = false, Error = "command_not_found" };
        }
        catch (Win32Exception ex)
        {
            return new UninstallResult { Success = false, Error = $"win32_error_{ex.NativeErrorCode}" };
        }
        catch (OperationCanceledException)
        {
            return new UninstallResult { Success = false, Error = "cancelled" };
        }
    }
}

public sealed class UninstallResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? StandardError { get; set; }
}
