using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;

namespace QuotesApi.Extensions;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<QuotesDbContext>(options =>
        {
            options.UseSqlite(
                configuration.GetConnectionString("DefaultConnection"));

            // EF Core SQL logging for development only.
            if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development")
            {
                options
                    .LogTo(Console.WriteLine)
                    .EnableSensitiveDataLogging();
            }
        });

        return services;
    }
}