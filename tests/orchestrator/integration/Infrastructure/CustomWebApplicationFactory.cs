using DeploymentPoC.Orchestrator.Data;
using DeploymentPoC.Orchestrator.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DeploymentPoC.Orchestrator.IntegrationTests.Infrastructure;

public sealed class CustomWebApplicationFactory : WebApplicationFactory<InstallController>, IAsyncDisposable
{
    private SqliteConnection? _connection;

    public string BaseUrl { get; private set; } = "";

    protected override IHost CreateHost(IHostBuilder builder)
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var testHost = base.CreateHost(builder);

        builder.ConfigureWebHost(webHostBuilder =>
        {
            webHostBuilder.UseKestrel();
            webHostBuilder.UseUrls("http://0.0.0.0:0");
        });

        var host = builder.Build();
        host.Start();

        var server = host.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>();
        // retry until kestrel binds to a port
        for (int i = 0; i < 50; i++)
        {
            if (addresses?.Addresses.Count > 0)
                break;
            Thread.Sleep(100);
        }
        BaseUrl = addresses!.Addresses.First().TrimEnd('/');

        return testHost;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<InstallerDbContext>));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<InstallerDbContext>(options => options.UseSqlite(_connection!));

            var serviceProvider = services.BuildServiceProvider();
            using var scope = serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<InstallerDbContext>();
            db.Database.Migrate();

            // SQLite doesn't support filtered indexes with IN operator;
            // the migration creates a regular unique index which breaks tests.
            // Drop it and rely on application-level validation (C6 fix).
            if (db.Database.IsSqlite())
            {
                db.Database.ExecuteSqlRaw("DROP INDEX IF EXISTS IX_WorkloadRuns_NodeId_WorkloadId_Active");
            }
        });
    }

    public override async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }

        await base.DisposeAsync();
    }
}
