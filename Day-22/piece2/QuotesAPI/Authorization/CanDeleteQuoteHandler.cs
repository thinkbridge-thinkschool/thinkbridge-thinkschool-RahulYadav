using Microsoft.AspNetCore.Authorization;
using QuotesApi.Repositories;

namespace QuotesApi.Authorization;

public sealed class CanDeleteQuoteHandler
    : AuthorizationHandler<CanDeleteQuoteRequirement>
{
    private readonly IQuoteRepository _repository;

    public CanDeleteQuoteHandler(IQuoteRepository repository)
    {
        _repository = repository;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CanDeleteQuoteRequirement requirement)
    {
        if (context.Resource is not HttpContext httpContext)
            return;

        if (!httpContext.Request.RouteValues.TryGetValue("id", out var idValue))
            return;

        if (!int.TryParse(idValue?.ToString(), out var quoteId))
            return;

        var quote = await _repository.GetByIdAsync(
            quoteId,
            httpContext.RequestAborted);

        if (quote is null || quote.IsDeleted)
            return;

        context.Succeed(requirement);
    }
}