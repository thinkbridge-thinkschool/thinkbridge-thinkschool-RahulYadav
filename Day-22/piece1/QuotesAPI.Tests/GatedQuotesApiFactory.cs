using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Repositories;

namespace QuotesApi.Tests;

// Day 21: QuotesApiFactory variant used only by the stampede test. Replaces
// the real IQuoteRepository registration with GatedQuoteRepository, which
// still runs the real EF Core/SQLite read but holds it open until the test
// releases Gate — see GatedQuoteRepository.cs for why.
internal sealed class GatedQuotesApiFactory : QuotesApiFactory
{
    public GateSignal Gate { get; } = new();

    protected override void ConfigureAdditionalTestServices(IServiceCollection services)
    {
        services.AddSingleton(Gate);
        services.AddScoped<IQuoteRepository, GatedQuoteRepository>();
    }
}
