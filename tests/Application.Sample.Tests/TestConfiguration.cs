// Disables parallel execution across test collections.
// The BrunoIntegrationTests (InMemory) and BrunoGraphDbIntegrationTests (GraphDB) both
// start sample-app processes and run Bruno CLI requests. Running them in parallel causes
// resource contention that makes app startup timing non-deterministic, leading to flaky
// WaitForReadyAsync timeouts and intermittent 500 errors from the sample app.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
