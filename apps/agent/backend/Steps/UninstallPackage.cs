using DeploymentPoC.Contracts.Runtime.RunPayloads;
using Microsoft.Win32;
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

        // Expand environment variables (e.g. %ProgramFiles%, $env:VAR) in command path
        if (!string.IsNullOrWhiteSpace(command))
        {
            command = ExpandEnvironmentVariables(command);
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
                if (process.ExitCode == 1603 || process.ExitCode == -1)
                {
                    return await ExecuteWithElevationAsync(command, arguments, artifactPath, expectedExitCodes, timeout, ct);
                }

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
        catch (Win32Exception ex) when (ex.NativeErrorCode == 740)
        {
            return await ExecuteWithElevationAsync(command, arguments, artifactPath, expectedExitCodes, timeout, ct);
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

    public static (string? Command, string? Arguments) ResolveRegistryUninstaller(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return default;

        var hives = new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser };
        var views = new[] { RegistryView.Registry64, RegistryView.Registry32 };

        foreach (var hive in hives)
        {
            foreach (var view in views)
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    var subKeyPath = hive == RegistryHive.LocalMachine
                        ? @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"
                        : @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

                    using var uninstallKey = baseKey.OpenSubKey(subKeyPath);
                    if (uninstallKey is null)
                        continue;

                    foreach (var subKeyName in uninstallKey.GetSubKeyNames())
                    {
                        try
                        {
                            using var subKey = uninstallKey.OpenSubKey(subKeyName);
                            if (subKey is null)
                                continue;

                            var name = subKey.GetValue("DisplayName") as string;
                            if (string.IsNullOrWhiteSpace(name))
                                continue;

                            if (!string.Equals(name, displayName, StringComparison.OrdinalIgnoreCase))
                                continue;

                            var quietUninstall = subKey.GetValue("QuietUninstallString") as string;
                            var uninstall = subKey.GetValue("UninstallString") as string;
                            var rawCommand = !string.IsNullOrWhiteSpace(quietUninstall) ? quietUninstall : uninstall;
                            if (string.IsNullOrWhiteSpace(rawCommand))
                                continue;

                            return ParseCommandString(rawCommand);
                        }
                        catch (UnauthorizedAccessException) { }
                        catch (System.Security.SecurityException) { }
                    }
                }
                catch (UnauthorizedAccessException) { }
                catch (System.Security.SecurityException) { }
            }
        }

        return default;
    }

    private static async Task<UninstallResult> ExecuteWithElevationAsync(
        string command,
        string arguments,
        string? artifactPath,
        List<int> expectedExitCodes,
        TimeSpan timeout,
        CancellationToken ct)
    {
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
            if (artifactPath is not null)
            {
                elevatedProcess.StartInfo.WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(artifactPath)) ?? Directory.GetCurrentDirectory();
            }

            elevatedProcess.Start();

            try
            {
                await elevatedProcess.WaitForExitAsync(elevatedCts.Token);
            }
            catch (OperationCanceledException) when (elevatedCts.Token.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                try { elevatedProcess.Kill(); } catch { }
                return new UninstallResult { Success = false, Error = "uninstall_timeout" };
            }

            if (!expectedExitCodes.Contains(elevatedProcess.ExitCode))
            {
                return new UninstallResult
                {
                    Success = false,
                    Error = $"exit_code_{elevatedProcess.ExitCode}"
                };
            }

            return new UninstallResult { Success = true };
        }
        catch (Win32Exception retryEx) when (retryEx.NativeErrorCode == 1223)
        {
            return new UninstallResult { Success = false, Error = "elevation_denied" };
        }
        catch (Win32Exception retryEx)
        {
            return new UninstallResult { Success = false, Error = $"win32_error_{retryEx.NativeErrorCode}" };
        }
    }

    private static (string Command, string Arguments) ParseCommandString(string rawCommand)
    {
        rawCommand = rawCommand.Trim();

        if (rawCommand.Length == 0)
            return (rawCommand, string.Empty);

        var firstChar = rawCommand[0];
        if (firstChar == '"')
        {
            var closingQuote = rawCommand.IndexOf('"', 1);
            if (closingQuote > 1)
            {
                var path = rawCommand[1..closingQuote];
                var args = closingQuote < rawCommand.Length - 1
                    ? rawCommand[(closingQuote + 1)..].Trim()
                    : string.Empty;
                return (path, args);
            }
            return (rawCommand, string.Empty);
        }

        var firstSpace = rawCommand.IndexOf(' ');
        if (firstSpace > 0)
        {
            var path = rawCommand[..firstSpace];
            var args = rawCommand[(firstSpace + 1)..].Trim();
            return (path, args);
        }

        return (rawCommand, string.Empty);
    }

    /// <summary>
    /// Expands both Windows-style (%VAR%) and PowerShell-style ($env:VAR) environment variables.
    /// </summary>
    private static string ExpandEnvironmentVariables(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        // Expand Windows-style %VAR% first
        var expanded = Environment.ExpandEnvironmentVariables(input);

        // Expand PowerShell-style $env:VAR
        var psEnvPattern = System.Text.RegularExpressions.Regex.Replace(
            expanded,
            @"\$env:(\w+)",
            m => Environment.GetEnvironmentVariable(m.Groups[1].Value) ?? m.Value,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return psEnvPattern;
    }

    public sealed class UninstallResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string? StandardError { get; set; }
    }
}
