using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace QuotesApi.Tests;

// Boots the real QuotesApi pipeline (Program.cs, auth, EF Core migrations,
// endpoints) against an isolated SQLite file so integration tests never touch
// the developer's local quotes.db. Uses the "Testing" environment so
// appsettings.Testing.json (already in the repo) supplies the JWT signing
// key instead of requiring dotnet user-secrets in CI.
public class QuotesApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"quotesapi-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={_dbPath}"
            });

            ConfigureAdditionalConfiguration(config);
        });

        // Day 21: extension point for tests (e.g. the HybridCache stampede
        // test) that need to swap a real DI-registered service for a
        // controllable decorator around it. Not sealed so a test-specific
        // subclass can override this hook instead of duplicating the whole
        // ConfigureWebHost setup above.
        builder.ConfigureTestServices(ConfigureAdditionalTestServices);
    }

    protected virtual void ConfigureAdditionalConfiguration(IConfigurationBuilder config)
    {
    }

    protected virtual void ConfigureAdditionalTestServices(IServiceCollection services)
    {
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch (IOException)
        {
            // SQLite may briefly hold the file handle after disposal; the OS
            // temp directory gets cleaned up eventually either way.
        }
    }
}
