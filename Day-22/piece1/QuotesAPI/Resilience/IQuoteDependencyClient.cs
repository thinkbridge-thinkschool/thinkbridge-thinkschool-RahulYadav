namespace QuotesApi.Resilience;

// Day 22: the outbound dependency wrapped by Polly resilience (see
// QuoteDependencyResilienceExtensions). The split into two methods is
// deliberate and IS the idempotency rule (requirement #7):
//
//   - GetQuoteOfTheDayAsync is a GET: safe, idempotent, side-effect free.
//     It runs through the "quote-dependency-get" pipeline, which includes a
//     retry stage -- a transient failure is retried automatically.
//
//   - SubmitQuoteAsync is a POST that creates a resource: NOT idempotent,
//     because retrying a failed create could double-submit it. It runs
//     through the separate "quote-dependency-post" pipeline, which has NO
//     retry stage at all -- a failure is surfaced to the caller immediately,
//     exactly once.
//
// Both pipelines still apply a circuit breaker, a timeout and a concurrency
// limiter (bulkhead), because those protections are safe and desirable
// regardless of idempotency.
public interface IQuoteDependencyClient
{
    Task<string> GetQuoteOfTheDayAsync(CancellationToken cancellationToken);

    Task<string> SubmitQuoteAsync(string content, CancellationToken cancellationToken);
}
