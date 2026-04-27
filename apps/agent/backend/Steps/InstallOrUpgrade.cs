using DeploymentPoC.Contracts.Runtime.RunPayloads;
using System.ComponentModel;
using System.Diagnostics;

namespace DeploymentPoC.Agent.Steps;

public static class InstallOrUpgrade
{
    public static async Task<InstallResult> ExecuteAsync(InstallAdapterConfig config, string artifactPath, CancellationToken ct)
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
                return new InstallResult { Success = false, Error = "install_timeout" };
            }

            if (!expectedExitCodes.Contains(process.ExitCode))
            {
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
            try
            {
                using var elevatedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                elevatedCts.CancelAfter(timeout);

                using var elevatedProcess = new Process();
                elevatedProcess.StartInfo.FileName = command;
                elevatedProcess.StartInfo.Arguments = arguments;
                elevatedProcess.StartInfo.UseShellExecute = true;
                elevatedProcess.StartInfo.Verb = "runas";
                elevatedProcess.StartInfo.CreateNoWindow = true;

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
}

public sealed class InstallResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? StandardError { get; set; }
}
