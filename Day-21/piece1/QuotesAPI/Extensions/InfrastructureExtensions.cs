using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;

namespace QuotesApi.Extensions;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Day 21: shared singleton so DbQueryCounterInterceptor (below) and
        // the /api/diagnostics/cache endpoint see the same real EF Core
        // command counts.
        services.AddSingleton<DbQueryCounter>();

        services.AddDbContext<QuotesDbContext>((serviceProvider, options) =>
        {
            options.UseSqlite(
                configuration.GetConnectionString("DefaultConnection"));

            options.AddInterceptors(
                new DbQueryCounterInterceptor(
                    serviceProvider.GetRequiredService<DbQueryCounter>()));
        });

        return services;
    }
}