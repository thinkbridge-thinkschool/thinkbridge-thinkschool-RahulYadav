namespace QuotesApi.Tests;

// Day 22: the "testable external dependency / local fake endpoint" stood in
// for the real QuoteDependency HTTP endpoint. Swapped in as the PRIMARY
// HttpMessageHandler for the "QuoteDependency" named HttpClient (see
// ResilienceQuotesApiFactory), so requests never leave the process, but the
// real DI-registered Polly pipelines (retry/circuit-breaker/timeout/
// bulkhead delegating handlers) still run in front of it exactly as they do
// in production. Tests program `Handle` per scenario (success, transient
// failure, sustained failure, timeout, recovery).
internal sealed class FakeQuoteDependencyHandler : HttpMessageHandler
{
    private int _totalCalls;

    public int TotalCalls => Volatile.Read(ref _totalCalls);

    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Handle { get; set; } =
        (_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _totalCalls);
        return await Handle(request, cancellationToken);
    }
}
