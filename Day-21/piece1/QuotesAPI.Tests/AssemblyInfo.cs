using Xunit;

// Day 21: these are WebApplicationFactory integration tests, and several
// (the HybridCache stampede test, the before/after load test, the Day 18
// shutdown-timing test) either drive real concurrency or make wall-clock
// timing assertions. Running test classes in parallel (xUnit's default)
// lets that concurrency-heavy work starve the CPU out from under a timing
// assertion in an unrelated class, which is exactly what made
// QuoteProcessingHttpTests.HostShutdown_DoesNotWaitOutInFlightSimulatedDelay
// (a pre-existing Day 18 test, unmodified here) fail only when the full
// suite ran together, never in isolation. Serializing the assembly trades
// some wall-clock test time for deterministic results regardless of
// machine load.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
