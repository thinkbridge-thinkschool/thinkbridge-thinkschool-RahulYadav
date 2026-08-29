using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace QuotesApi.Tests;

// Boots the real QuotesApi pipeline (Program.cs, auth, EF Core migrations,
// endpoints) against an isolated SQLite file so integration tests never touch
// the developer's local quotes.db. Uses the "Testing" environment so
// appsettings.Testing.json (already in the repo) supplies the JWT signing
// key instead of requiring dotnet user-secrets in CI.
public sealed class QuotesApiFactory : WebApplicationFactory<Program>
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
        });
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
