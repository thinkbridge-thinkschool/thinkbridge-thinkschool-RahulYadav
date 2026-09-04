using QuotesApi.Modules.Quotes.Application;
using QuotesApi.Modules.Quotes.Contracts;
using QuotesApi.Repositories;

namespace QuotesApi.Modules.Quotes;

public static class QuotesModule
{
    public static IServiceCollection AddQuotesModule(this IServiceCollection services)
    {
        services.AddScoped<IQuoteRepository, QuoteRepository>();
        services.AddScoped<IQuoteCatalog, QuoteCatalog>();

        return services;
    }
}
