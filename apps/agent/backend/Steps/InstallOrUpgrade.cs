using DeploymentPoC.Contracts.Runtime.RunPayloads;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Diagnostics;

namespace DeploymentPoC.Agent.Steps;

public static class InstallOrUpgrade
{
    public static async Task<InstallResult> ExecuteAsync(InstallAdapterConfig config, string artifactPath, ILogger logger, CancellationToken ct)
    {
        if (config is null)
        {
            return new InstallResult { Success = false, Error = "invalid_config" };
        }

        if (!File.Exists(artifactPath))
        {
            return new InstallResult { Success = false, Error = "artifact_not_found" };
        }

        var command = config.Command;
        var arguments = config.Arguments ?? string.Empty;

        // fallback: if command is missing or is the placeholder itself, execute the artifact directly
        if (string.IsNullOrWhiteSpace(command) ||
            string.Equals(command, "{artifactPath}", StringComparison.OrdinalIgnoreCase))
        {
            command = artifactPath;
        }

        // MSI files cannot be executed directly; invoke via msiexec
        if (string.Equals(config.Type, "msi", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(command, artifactPath, StringComparison.OrdinalIgnoreCase))
        {
            command = "msiexec";
            arguments = $"/i \"{artifactPath}\" {arguments}".Trim();
        }

        // Expand placeholder {artifactPath} in arguments
        arguments = arguments.Replace("{artifactPath}", artifactPath, StringComparison.OrdinalIgnoreCase);

        // Apply default silent-install arguments when none are provided, so automated
        // deployment on headless agents does not pop up installer UI.
        if (string.IsNullOrWhiteSpace(arguments))
        {
            arguments = config.Type?.ToLowerInvariant() switch
            {
                "msi" => "/quiet /norestart",
                "exe" => "/S",
                _ => arguments
            };
        }

        var expectedExitCodes = config.ExpectedExitCodes is { Count: > 0 }
            ? config.ExpectedExitCodes
            : new List<int> { 0 };

        var timeout = config.TimeoutSeconds > 0
            ? TimeSpan.FromSeconds(config.TimeoutSeconds)
            : TimeSpan.FromSeconds(300);

        var workingDirectory = Path.GetDirectoryName(Path.GetFullPath(artifactPath)) ?? Directory.GetCurrentDirectory();

        logger.LogInformation("Executing install command: FileName={FileName}, Arguments={Arguments}", command, arguments);

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
                return new InstallResult { Success = false, Error = "install_timeout" };
            }

            if (!expectedExitCodes.Contains(process.ExitCode))
            {
                if (process.ExitCode == 1603 || process.ExitCode == -1)
                {
                    logger.LogWarning("Install exited with code {ExitCode} (likely insufficient privileges). Retrying with elevation.", process.ExitCode);
                    return await ExecuteWithElevationAsync(command, arguments, artifactPath, workingDirectory, expectedExitCodes, timeout, ct, logger);
                }

                var stderr = await process.StandardError.ReadToEndAsync(ct);
                return new InstallResult
                {
                    Success = false,
                    Error = $"exit_code_{process.ExitCode}",
                    StandardError = stderr
                };
            }

            return new InstallResult { Success = true };
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 740)
        {
            // ERROR_ELEVATION_REQUIRED: retry with UAC elevation via runas verb
            return await ExecuteWithElevationAsync(command, arguments, artifactPath, workingDirectory, expectedExitCodes, timeout, ct, logger);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            return new InstallResult { Success = false, Error = "command_not_found" };
        }
        catch (Win32Exception ex)
        {
            return new InstallResult { Success = false, Error = $"win32_error_{ex.NativeErrorCode}" };
        }
        catch (OperationCanceledException)
        {
            return new InstallResult { Success = false, Error = "cancelled" };
        }
    }

    private static async Task<InstallResult> ExecuteWithElevationAsync(
        string command,
        string arguments,
        string artifactPath,
        string workingDirectory,
        List<int> expectedExitCodes,
        TimeSpan timeout,
        CancellationToken ct,
        ILogger logger)
    {
        logger.LogInformation("Retrying install with elevation: FileName={FileName}, Arguments={Arguments}", command, arguments);

        try
        {
            using var elevatedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            elevatedCts.CancelAfter(timeout);

            using var elevatedProcess = new Process();
            elevatedProcess.StartInfo.FileName = command;
            elevatedProcess.StartInfo.Arguments = arguments;
            elevatedProcess.StartInfo.UseShellExecute = true;
            elevatedProcess.StartInfo.Verb = "runas";
            elevatedProcess.StartInfo.CreateNoWindow = false;
            elevatedProcess.StartInfo.WorkingDirectory = workingDirectory;

            elevatedProcess.Start();

            try
            {
                await elevatedProcess.WaitForExitAsync(elevatedCts.Token);
            }
            catch (OperationCanceledException) when (elevatedCts.Token.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                try { elevatedProcess.Kill(); } catch { /* best-effort */ }
                return new InstallResult { Success = false, Error = "install_timeout" };
            }

            if (!expectedExitCodes.Contains(elevatedProcess.ExitCode))
            {
                return new InstallResult
                {
                    Success = false,
                    Error = $"exit_code_{elevatedProcess.ExitCode}"
                };
            }

            return new InstallResult { Success = true };
        }
        catch (Win32Exception retryEx) when (retryEx.NativeErrorCode == 1223)
        {
            return new InstallResult { Success = false, Error = "elevation_denied" };
        }
        catch (Win32Exception retryEx)
        {
            return new InstallResult { Success = false, Error = $"win32_error_{retryEx.NativeErrorCode}" };
        }
    }
}

public sealed class InstallResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? StandardError { get; set; }
}
