using DeploymentPoC.Agent.Pipeline;
using DeploymentPoC.Contracts.Runtime.RunPayloads;
using System.ComponentModel;
using System.Diagnostics;

namespace DeploymentPoC.Agent.Steps;

public class InitStepExecutor
{
    public async Task<InitStepResult> ExecuteAsync(
        string command,
        string defaultShell,
        string stepName,
        Dictionary<string, string> envVars,
        int timeoutSeconds,
        int packageIndex,
        Func<StepStatusPayload, Task> sendStatusAsync,
        CancellationToken ct)
    {
        var argPrefix = GetArgPrefix(defaultShell);
        var arguments = $"{argPrefix} {command}";

        var runningPayload = new StepStatusPayload
        {
            StepName = stepName,
            PackageIndex = packageIndex,
            Status = "Running"
        };
        await sendStatusAsync(runningPayload);

        using var process = new Process();
        process.StartInfo.FileName = defaultShell;
        process.StartInfo.Arguments = arguments;
        process.StartInfo.WorkingDirectory = Path.GetTempPath();
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = true;

        foreach (var kvp in envVars)
        {
            process.StartInfo.Environment[kvp.Key] = kvp.Value;
        }

        using var ctReg = ct.Register(() =>
        {
            try { process.Kill(); } catch { /* best-effort */ }
        });

        try
        {
            process.Start();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            var failPayload = new StepStatusPayload
            {
                StepName = stepName,
                PackageIndex = packageIndex,
                Status = "Failed",
                Error = "command_not_found"
            };
            await sendStatusAsync(failPayload);

            return new InitStepResult
            {
                Success = false,
                ExitCode = -1,
                ErrorOutput = "command_not_found"
            };
        }

        var timeoutMs = timeoutSeconds * 1000;
        var exited = process.WaitForExit(timeoutMs);

        if (ct.IsCancellationRequested)
        {
            try { process.Kill(); } catch { /* best-effort */ }

            var cancelPayload = new StepStatusPayload
            {
                StepName = stepName,
                PackageIndex = packageIndex,
                Status = "Failed",
                Error = "cancelled"
            };
            await sendStatusAsync(cancelPayload);

            return new InitStepResult
            {
                Success = false,
                ExitCode = process.HasExited ? process.ExitCode : -1,
                ErrorOutput = "cancelled"
            };
        }

        if (!exited)
        {
            try { process.Kill(); } catch { /* best-effort */ }

            var timeoutPayload = new StepStatusPayload
            {
                StepName = stepName,
                PackageIndex = packageIndex,
                Status = "Failed",
                Error = "timeout"
            };
            await sendStatusAsync(timeoutPayload);

            return new InitStepResult
            {
                Success = false,
                ExitCode = -1,
                ErrorOutput = "timeout"
            };
        }

        var success = process.ExitCode == 0;
        string? errorOutput = null;

        if (!success)
        {
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            errorOutput = $"{stdout}{stderr}".Trim();
            if (string.IsNullOrWhiteSpace(errorOutput))
                errorOutput = null;
        }

        var finalPayload = new StepStatusPayload
        {
            StepName = stepName,
            PackageIndex = packageIndex,
            Status = success ? "Completed" : "Failed",
            Error = errorOutput
        };
        await sendStatusAsync(finalPayload);

        return new InitStepResult
        {
            Success = success,
            ExitCode = process.ExitCode,
            ErrorOutput = errorOutput
        };
    }

    private static string GetArgPrefix(string shell)
    {
        var lower = shell.ToLowerInvariant();
        if (lower.Contains("cmd"))
            return "/C";
        if (lower.Contains("powershell") || lower.Contains("pwsh"))
            return "-Command";
        return "-Command";
    }
}
