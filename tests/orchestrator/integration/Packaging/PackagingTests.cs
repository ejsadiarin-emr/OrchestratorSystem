using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using NUnit.Framework;

namespace DeploymentPoC.Orchestrator.IntegrationTests;

[TestFixture]
public sealed class PackagingTests
{
    private string _repoRoot = null!;
    private string _publishOutput = null!;

    [SetUp]
    public void SetUp()
    {
        _repoRoot = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", ".."));
        _publishOutput = Path.Combine(Path.GetTempPath(), $"orchestrator-publish-{Guid.NewGuid()}");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_publishOutput))
        {
            Directory.Delete(_publishOutput, recursive: true);
        }
    }

    [Test]
    public async Task Publish_ForWindowsX64_ProducesSelfContainedExecutable()
    {
        var projectPath = Path.Combine(_repoRoot, "apps", "orchestrator", "backend", "DeploymentPoC.Orchestrator.csproj");

        var psi = new ProcessStartInfo("dotnet", $"publish \"{projectPath}\" -r win-x64 -c Release -o \"{_publishOutput}\" --self-contained true -p:PublishSingleFile=true")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi);
        Assert.That(process, Is.Not.Null);
        await process!.WaitForExitAsync();

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();

        Assert.That(process.ExitCode, Is.EqualTo(0), $"dotnet publish failed:\nSTDOUT: {stdout}\nSTDERR: {stderr}");
        Assert.That(Directory.Exists(_publishOutput), Is.True, "Publish output directory was not created");

        // Self-contained single-file produces a .exe on Windows
        var exePath = Path.Combine(_publishOutput, "DeploymentPoC.Orchestrator.exe");
        Assert.That(File.Exists(exePath), Is.True, "Expected single-file executable was not produced");
    }

    [Test]
    public async Task Publish_ForWindowsX64_IncludesSpaAssetsInWwwroot()
    {
        var projectPath = Path.Combine(_repoRoot, "apps", "orchestrator", "backend", "DeploymentPoC.Orchestrator.csproj");

        var psi = new ProcessStartInfo("dotnet", $"publish \"{projectPath}\" -r win-x64 -c Release -o \"{_publishOutput}\" --self-contained true -p:PublishSingleFile=true")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi);
        Assert.That(process, Is.Not.Null);
        await process!.WaitForExitAsync();

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();

        Assert.That(process.ExitCode, Is.EqualTo(0), $"dotnet publish failed:\nSTDOUT: {stdout}\nSTDERR: {stderr}");

        var wwwrootPath = Path.Combine(_publishOutput, "wwwroot");
        Assert.That(Directory.Exists(wwwrootPath), Is.True, "wwwroot directory was not published");

        var indexHtmlPath = Path.Combine(wwwrootPath, "index.html");
        Assert.That(File.Exists(indexHtmlPath), Is.True, "index.html was not published in wwwroot");

        var assetsPath = Path.Combine(wwwrootPath, "assets");
        Assert.That(Directory.Exists(assetsPath), Is.True, "assets directory was not published in wwwroot");

        var jsFiles = Directory.GetFiles(assetsPath, "*.js");
        var cssFiles = Directory.GetFiles(assetsPath, "*.css");

        Assert.That(jsFiles.Length, Is.GreaterThan(0), "No JavaScript assets found in published wwwroot/assets");
        Assert.That(cssFiles.Length, Is.GreaterThan(0), "No CSS assets found in published wwwroot/assets");
    }

    [Test]
    public async Task Publish_ForLinuxX64_ProducesSelfContainedExecutable()
    {
        var projectPath = Path.Combine(_repoRoot, "apps", "orchestrator", "backend", "DeploymentPoC.Orchestrator.csproj");

        var psi = new ProcessStartInfo("dotnet", $"publish \"{projectPath}\" -r linux-x64 -c Release -o \"{_publishOutput}\" --self-contained true -p:PublishSingleFile=true")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi);
        Assert.That(process, Is.Not.Null);
        await process!.WaitForExitAsync();

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();

        Assert.That(process.ExitCode, Is.EqualTo(0), $"dotnet publish failed:\nSTDOUT: {stdout}\nSTDERR: {stderr}");
        Assert.That(Directory.Exists(_publishOutput), Is.True, "Publish output directory was not created");

        // Self-contained single-file produces the executable without .exe extension on Linux
        var exePath = Path.Combine(_publishOutput, "DeploymentPoC.Orchestrator");
        Assert.That(File.Exists(exePath), Is.True, "Expected single-file executable was not produced");
    }

    [Test]
    public async Task PublishedBinary_LinuxX64_CanStartAndServeHealthEndpoint()
    {
        var projectPath = Path.Combine(_repoRoot, "apps", "orchestrator", "backend", "DeploymentPoC.Orchestrator.csproj");

        var publishPsi = new ProcessStartInfo("dotnet", $"publish \"{projectPath}\" -r linux-x64 -c Release -o \"{_publishOutput}\" --self-contained true -p:PublishSingleFile=true")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var publishProcess = Process.Start(publishPsi);
        Assert.That(publishProcess, Is.Not.Null);
        await publishProcess!.WaitForExitAsync();
        Assert.That(publishProcess.ExitCode, Is.EqualTo(0), "dotnet publish failed");

        var exePath = Path.Combine(_publishOutput, "DeploymentPoC.Orchestrator");
        Assert.That(File.Exists(exePath), Is.True, "Executable was not produced");

        // Ensure the binary is executable
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            var chmodPsi = new ProcessStartInfo("chmod", $"+x \"{exePath}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var chmodProcess = Process.Start(chmodPsi);
            await chmodProcess!.WaitForExitAsync();
        }

        // Start the binary with a free port
        var runPsi = new ProcessStartInfo(exePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            Environment =
            {
                ["ASPNETCORE_URLS"] = "http://127.0.0.1:0",
                ["ASPNETCORE_ENVIRONMENT"] = "Production",
            }
        };

        using var runProcess = Process.Start(runPsi);
        Assert.That(runProcess, Is.Not.Null);
        var runProcessRef = runProcess!;

        try
        {
            // Wait for the app to start and extract the actual port
            var port = await WaitForPortAsync(runProcessRef, TimeSpan.FromSeconds(30));
            Assert.That(port, Is.GreaterThan(0), "Failed to determine listening port");

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            var response = await client.GetAsync($"http://127.0.0.1:{port}/health");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "Health endpoint did not return 200");
        }
        finally
        {
            if (!runProcessRef.HasExited)
            {
                runProcessRef.Kill(entireProcessTree: true);
                await runProcessRef.WaitForExitAsync();
            }
        }
    }

    private static async Task<int> WaitForPortAsync(Process process, TimeSpan timeout)
    {
        var cts = new CancellationTokenSource(timeout);
        var portRegex = new System.Text.RegularExpressions.Regex(@"Now listening on: http://127\.0\.0\.1:(\d+)");

        var stdoutTask = Task.Run(async () =>
        {
            while (!process.HasExited && !cts.Token.IsCancellationRequested)
            {
                var line = await process.StandardOutput.ReadLineAsync(cts.Token);
                if (line is null) break;

                var match = portRegex.Match(line);
                if (match.Success && int.TryParse(match.Groups[1].Value, out var port))
                {
                    return port;
                }
            }
            return 0;
        }, cts.Token);

        var stderrTask = Task.Run(async () =>
        {
            while (!process.HasExited && !cts.Token.IsCancellationRequested)
            {
                var line = await process.StandardError.ReadLineAsync(cts.Token);
                if (line is null) break;
            }
        }, cts.Token);

        var completedTask = await Task.WhenAny(stdoutTask, Task.Delay(timeout, cts.Token));
        if (completedTask == stdoutTask)
        {
            return await stdoutTask;
        }

        return 0;
    }
}
