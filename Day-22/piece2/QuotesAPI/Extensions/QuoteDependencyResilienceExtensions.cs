using System.Net;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Retry;
using Polly.Timeout;
using QuotesApi.Options;
using QuotesApi.Resilience;

namespace QuotesApi.Extensions;

// Day 22: wraps the outbound QuoteDependency call (see
// Resilience/QuoteDependencyClient.cs) with Polly resilience.
//
// Two separate pipelines are registered, under two separate keys, instead of
// one shared pipeline with a method-sniffing predicate. That is the most
// explicit way to satisfy the idempotency rule (requirement #7): a retry
// stage physically does not exist in the pipeline used for the
// non-idempotent POST/create call, so there is no predicate to get wrong.
//
// Pipeline order (outermost to innermost -- Polly runs strategies in the
// order they are added to the builder):
//
//   Bulkhead (concurrency limiter) -> Retry -> Circuit breaker -> Timeout
//
// This mirrors Microsoft's own "standard resilience handler" ordering
// (see Microsoft.Extensions.Http.Resilience's AddStandardResilienceHandler),
// not the naive Timeout -> Retry -> CircuitBreaker -> Bulkhead ordering:
//
//   - Bulkhead is outermost so the concurrency limit protects application
//     capacity for the whole operation, however many retries it takes --
//     a slow/retrying call still only ever holds one bulkhead permit.
//   - Retry is next so it can retry an operation that failed anywhere
//     inside it (circuit breaker rejection, timeout, transport failure).
//   - Circuit breaker sits inside retry so EVERY attempt (including
//     retries) is individually recorded against the breaker; once the
//     breaker opens, subsequent retries fail fast with
//     BrokenCircuitException instead of dialing a known-unhealthy
//     dependency again.
//   - Timeout is innermost so it bounds each individual attempt, not the
//     whole retry loop. A per-attempt timeout is itself treated as a
//     failure by both the retry and circuit-breaker layers wrapping it.
public static class QuoteDependencyResilienceExtensions
{
    public const string HttpClientName = "QuoteDependency";
    public const string IdempotentPipelineKey = "quote-dependency-get";
    public const string NonIdempotentPipelineKey = "quote-dependency-post";

    public static IServiceCollection AddQuoteDependencyResilience(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<QuoteDependencyOptions>(configuration.GetSection("QuoteDependency"));
        services.Configure<QuoteDependencyResilienceOptions>(configuration.GetSection("Resilience"));

        services.AddSingleton<QuoteDependencyResilienceMetrics>();

        services.AddHttpClient(HttpClientName, (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<QuoteDependencyOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
        });

        services.AddResiliencePipeline<string, HttpResponseMessage>(IdempotentPipelineKey, (builder, context) =>
        {
            var options = context.ServiceProvider
                .GetRequiredService<IOptions<QuoteDependencyResilienceOptions>>().Value;
            var metrics = context.ServiceProvider.GetRequiredService<QuoteDependencyResilienceMetrics>();
            var logger = CreateLogger(context.ServiceProvider);

            AddBulkhead(builder, options, metrics, logger, "GET");
            AddRetry(builder, options, metrics, logger); // idempotent: retries allowed
            AddCircuitBreaker(builder, options, metrics, logger, "GET");
            AddTimeout(builder, options, metrics, logger, "GET");
        });

        services.AddResiliencePipeline<string, HttpResponseMessage>(NonIdempotentPipelineKey, (builder, context) =>
        {
            var options = context.ServiceProvider
                .GetRequiredService<IOptions<QuoteDependencyResilienceOptions>>().Value;
            var metrics = context.ServiceProvider.GetRequiredService<QuoteDependencyResilienceMetrics>();
            var logger = CreateLogger(context.ServiceProvider);

            AddBulkhead(builder, options, metrics, logger, "POST");

            // Deliberately NO .AddRetry() here. POST/create is not
            // idempotent -- a failed attempt must be surfaced immediately,
            // exactly once, never retried automatically.

            AddCircuitBreaker(builder, options, metrics, logger, "POST");
            AddTimeout(builder, options, metrics, logger, "POST");
        });

        services.AddSingleton<IQuoteDependencyClient, QuoteDependencyClient>();

        return services;
    }

    private static ILogger CreateLogger(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("QuoteDependencyResilience");

    private static void AddBulkhead(
        ResiliencePipelineBuilder<HttpResponseMessage> builder,
        QuoteDependencyResilienceOptions options,
        QuoteDependencyResilienceMetrics metrics,
        ILogger logger,
        string operation)
    {
        builder.AddRateLimiter(new RateLimiterStrategyOptions
        {
            DefaultRateLimiterOptions = new ConcurrencyLimiterOptions
            {
                PermitLimit = options.Bulkhead.MaxConcurrency,
                QueueLimit = options.Bulkhead.QueueLimit
            },
            OnRejected = args =>
            {
                metrics.RecordBulkheadRejected();

                logger.LogWarning(
                    "[Bulkhead] {Operation} concurrency limit reached (max={MaxConcurrency}); request rejected",
                    operation,
                    options.Bulkhead.MaxConcurrency);

                return default;
            }
        });
    }

    private static void AddRetry(
        ResiliencePipelineBuilder<HttpResponseMessage> builder,
        QuoteDependencyResilienceOptions options,
        QuoteDependencyResilienceMetrics metrics,
        ILogger logger)
    {
        builder.AddRetry(new RetryStrategyOptions<HttpResponseMessage>
        {
            MaxRetryAttempts = options.Retry.MaxRetryAttempts,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            Delay = TimeSpan.FromSeconds(options.Retry.BackoffSeconds),
            ShouldHandle = args => ValueTask.FromResult(IsTransientFailure(args.Outcome)),
            OnRetry = args =>
            {
                metrics.RecordRetryAttempt();

                logger.LogWarning(
                    "[Retry] attempt={AttemptNumber} delay={DelayMs}ms reason={Reason}",
                    args.AttemptNumber + 1,
                    args.RetryDelay.TotalMilliseconds,
                    DescribeOutcome(args.Outcome));

                // The failed attempt's HttpResponseMessage (if any) is not
                // the one returned to the caller and would otherwise leak.
                args.Outcome.Result?.Dispose();

                return default;
            }
        });
    }

    private static void AddCircuitBreaker(
        ResiliencePipelineBuilder<HttpResponseMessage> builder,
        QuoteDependencyResilienceOptions options,
        QuoteDependencyResilienceMetrics metrics,
        ILogger logger,
        string operation)
    {
        builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
        {
            FailureRatio = options.CircuitBreaker.FailureRatio,
            MinimumThroughput = options.CircuitBreaker.MinimumThroughput,
            SamplingDuration = TimeSpan.FromSeconds(options.CircuitBreaker.SamplingDurationSeconds),
            BreakDuration = TimeSpan.FromSeconds(options.CircuitBreaker.BreakDurationSeconds),
            ShouldHandle = args => ValueTask.FromResult(IsTransientFailure(args.Outcome)),
            OnOpened = args =>
            {
                metrics.RecordCircuitOpened();

                logger.LogError(
                    "[Resilience] Circuit {Operation} OPENED for {BreakDurationSeconds}s (reason={Reason})",
                    operation,
                    args.BreakDuration.TotalSeconds,
                    DescribeOutcome(args.Outcome));

                return default;
            },
            OnHalfOpened = args =>
            {
                metrics.RecordCircuitHalfOpened();

                logger.LogWarning(
                    "[Resilience] Circuit {Operation} HALF-OPEN: probing dependency",
                    operation);

                return default;
            },
            OnClosed = args =>
            {
                metrics.RecordCircuitClosed();

                logger.LogInformation(
                    "[Resilience] Circuit {Operation} CLOSED: recovery confirmed",
                    operation);

                return default;
            }
        });
    }

    private static void AddTimeout(
        ResiliencePipelineBuilder<HttpResponseMessage> builder,
        QuoteDependencyResilienceOptions options,
        QuoteDependencyResilienceMetrics metrics,
        ILogger logger,
        string operation)
    {
        builder.AddTimeout(new TimeoutStrategyOptions
        {
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds),
            OnTimeout = args =>
            {
                metrics.RecordTimeout();

                logger.LogWarning(
                    "[Timeout] {Operation} attempt exceeded {TimeoutSeconds}s and was cancelled",
                    operation,
                    args.Timeout.TotalSeconds);

                return default;
            }
        });
    }

    // Shared by both the retry and circuit-breaker predicates: a network
    // failure, a per-attempt timeout, or a 5xx/408 response are all treated
    // as a dependency failure. A RateLimiterRejectedException (bulkhead
    // rejection) is never seen here because the bulkhead sits OUTSIDE retry
    // and circuit breaker in the pipeline -- a rejection short-circuits
    // before either inner strategy ever runs.
    private static bool IsTransientFailure(Outcome<HttpResponseMessage> outcome) =>
        outcome.Exception is HttpRequestException or TimeoutRejectedException
        || (outcome.Result is { } response &&
            ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout));

    private static string DescribeOutcome(Outcome<HttpResponseMessage> outcome) =>
        outcome.Exception is { } exception
            ? exception.GetType().Name
            : $"HTTP {(int)outcome.Result!.StatusCode}";
}
