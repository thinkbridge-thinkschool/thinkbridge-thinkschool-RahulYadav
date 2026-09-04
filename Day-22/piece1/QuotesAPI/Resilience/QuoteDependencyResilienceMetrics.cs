namespace QuotesApi.Resilience;

// Day 22: lightweight, in-process counters for the QuoteDependency
// resilience pipelines (see QuoteDependencyResilienceExtensions). Exposed at
// GET /api/diagnostics/resilience purely as evidence -- no secrets or
// connection details, just counts and the current circuit state. Shared by
// both the idempotent (GET) and non-idempotent (POST) pipelines.
public sealed class QuoteDependencyResilienceMetrics
{
    private long _retryAttempts;
    private long _circuitOpenedCount;
    private long _circuitHalfOpenedCount;
    private long _circuitClosedCount;
    private long _timeoutCount;
    private long _bulkheadRejectedCount;
    private string _circuitState = "Closed";

    public void RecordRetryAttempt() => Interlocked.Increment(ref _retryAttempts);

    public void RecordTimeout() => Interlocked.Increment(ref _timeoutCount);

    public void RecordBulkheadRejected() => Interlocked.Increment(ref _bulkheadRejectedCount);

    public void RecordCircuitOpened()
    {
        Interlocked.Increment(ref _circuitOpenedCount);
        Volatile.Write(ref _circuitState, "Open");
    }

    public void RecordCircuitHalfOpened()
    {
        Interlocked.Increment(ref _circuitHalfOpenedCount);
        Volatile.Write(ref _circuitState, "HalfOpen");
    }

    public void RecordCircuitClosed()
    {
        Interlocked.Increment(ref _circuitClosedCount);
        Volatile.Write(ref _circuitState, "Closed");
    }

    public long RetryAttempts => Interlocked.Read(ref _retryAttempts);

    public long CircuitOpenedCount => Interlocked.Read(ref _circuitOpenedCount);

    public long CircuitHalfOpenedCount => Interlocked.Read(ref _circuitHalfOpenedCount);

    public long CircuitClosedCount => Interlocked.Read(ref _circuitClosedCount);

    public long TimeoutCount => Interlocked.Read(ref _timeoutCount);

    public long BulkheadRejectedCount => Interlocked.Read(ref _bulkheadRejectedCount);

    public string CircuitState => Volatile.Read(ref _circuitState);

    public void Reset()
    {
        Interlocked.Exchange(ref _retryAttempts, 0);
        Interlocked.Exchange(ref _circuitOpenedCount, 0);
        Interlocked.Exchange(ref _circuitHalfOpenedCount, 0);
        Interlocked.Exchange(ref _circuitClosedCount, 0);
        Interlocked.Exchange(ref _timeoutCount, 0);
        Interlocked.Exchange(ref _bulkheadRejectedCount, 0);
        Volatile.Write(ref _circuitState, "Closed");
    }
}
