using Microsoft.AspNetCore.Authorization;

namespace QuotesApi.Authorization;

public sealed class CanDeleteQuoteRequirement : IAuthorizationRequirement
{
}