namespace QuotesApi.Options;

// Day 22: bound from the "Resilience" configuration section and consumed by
// QuoteDependencyResilienceExtensions when building the two QuoteDependency
// resilience pipelines. Production (appsettings.json) uses conservative
// values; appsettings.Testing.json overrides them with small-but-valid
// values (Polly requires SamplingDuration/BreakDuration > 0.5s) so tests
// observe real retries/circuit transitions/timeouts without slow sleeps.
public sealed class QuoteDependencyResilienceOptions
{
    public RetryOptions Retry { get; set; } = new();

    public CircuitBreakerOptions CircuitBreaker { get; set; } = new();

    public double TimeoutSeconds { get; set; } = 3;

    public BulkheadOptions Bulkhead { get; set; } = new();

    public sealed class RetryOptions
    {
        public int MaxRetryAttempts { get; set; } = 3;

        public double BackoffSeconds { get; set; } = 1;
    }

    public sealed class CircuitBreakerOptions
    {
        public double FailureRatio { get; set; } = 0.5;

        public int MinimumThroughput { get; set; } = 5;

        public double SamplingDurationSeconds { get; set; } = 30;

        public double BreakDurationSeconds { get; set; } = 10;
    }

    public sealed class BulkheadOptions
    {
        public int MaxConcurrency { get; set; } = 5;

        public int QueueLimit { get; set; }
    }
}
