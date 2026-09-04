using System.Net.Http.Json;
using Polly;
using Polly.Registry;
using QuotesApi.Extensions;

namespace QuotesApi.Resilience;

// Day 22: real HttpClient-based implementation of the outbound dependency.
// No resilience logic lives here -- both calls simply execute through a
// resilience pipeline resolved by key from the DI-registered
// ResiliencePipelineProvider (see QuoteDependencyResilienceExtensions), which
// is exactly the pipeline production traffic uses. Tests swap the primary
// HttpMessageHandler on the "QuoteDependency" named client for a
// deterministic fake so the SAME pipeline can be exercised without a real
// network call.
public sealed class QuoteDependencyClient : IQuoteDependencyClient
{
    private readonly HttpClient _httpClient;
    private readonly ResiliencePipeline<HttpResponseMessage> _idempotentPipeline;
    private readonly ResiliencePipeline<HttpResponseMessage> _nonIdempotentPipeline;

    public QuoteDependencyClient(
        IHttpClientFactory httpClientFactory,
        ResiliencePipelineProvider<string> pipelineProvider)
    {
        _httpClient = httpClientFactory.CreateClient(QuoteDependencyResilienceExtensions.HttpClientName);

        _idempotentPipeline = pipelineProvider.GetPipeline<HttpResponseMessage>(
            QuoteDependencyResilienceExtensions.IdempotentPipelineKey);

        _nonIdempotentPipeline = pipelineProvider.GetPipeline<HttpResponseMessage>(
            QuoteDependencyResilienceExtensions.NonIdempotentPipelineKey);
    }

    // GET: idempotent, retried automatically by the pipeline on transient
    // failure.
    public async Task<string> GetQuoteOfTheDayAsync(CancellationToken cancellationToken)
    {
        using var response = await _idempotentPipeline.ExecuteAsync(
            static (client, ct) => SendAsync(client, HttpMethod.Get, "quote-of-the-day", content: null, ct),
            _httpClient,
            cancellationToken);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    // POST: creates a resource, so it is NOT idempotent. It runs through the
    // pipeline that has no retry stage -- a failure surfaces to the caller
    // after exactly one attempt.
    public async Task<string> SubmitQuoteAsync(string content, CancellationToken cancellationToken)
    {
        using var response = await _nonIdempotentPipeline.ExecuteAsync(
            static (state, ct) => SendAsync(state.Client, HttpMethod.Post, "quotes", state.Content, ct),
            (Client: _httpClient, Content: content),
            cancellationToken);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static async ValueTask<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string requestUri,
        string? content,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, requestUri);

        if (content is not null)
        {
            request.Content = JsonContent.Create(new { content });
        }

        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }
}
