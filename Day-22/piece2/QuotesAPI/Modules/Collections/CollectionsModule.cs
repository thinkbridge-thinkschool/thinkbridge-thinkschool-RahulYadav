using QuotesApi.Modules.Collections.Application.Commands;
using QuotesApi.Modules.Collections.Application.Ports;
using QuotesApi.Modules.Collections.Application.Queries;
using QuotesApi.Modules.Collections.Infrastructure.Repositories;

namespace QuotesApi.Modules.Collections;

public static class CollectionsModule
{
    public static IServiceCollection AddCollectionsModule(this IServiceCollection services)
    {
        services.AddScoped<ICollectionRepository, EfCollectionRepository>();

        services.AddScoped<CreateCollectionCommandHandler>();
        services.AddScoped<AddQuoteToCollectionCommandHandler>();
        services.AddScoped<RemoveQuoteFromCollectionCommandHandler>();
        services.AddScoped<GetCollectionQueryHandler>();

        return services;
    }
}
